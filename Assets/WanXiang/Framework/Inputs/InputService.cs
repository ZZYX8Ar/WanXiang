// ============================================================================
//  万相 · 输入服务实现
//  ---------------------------------------------------------------------------
//  这个类管三件事，按重要性排序：
//    ① 上下文切换 —— Map 的启停只能由这里决定，业务与面板都不许自己 Enable
//    ② 改键       —— 监听、冲突检测、恢复默认
//    ③ 改键持久化 —— 只负责导出/导入 JSON 字符串，落盘交给存档层
//
//  三件事里 ① 是它存在的理由。没有 ①，剩下两件事也没什么意义 ——
//  因为「战斗中开弹窗误触大招」那个经典事故，根因就是 Map 启停没人统管。
//
//  ⚠ 关于 Unity 的输入后端：
//    本工程 activeInputHandler 设为 Both（见 ARCHITECTURE.md §6.1）。
//    不是 New —— QFramework 的 UIKit 与 ConsoleKit 里有真代码在用旧
//    Input.mousePosition / Input.GetKeyUp，设成 New 会让它们在运行期抛异常。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WanXiang.Framework.Inputs
{
    /// <summary>
    /// 输入服务。默认实现，不依赖 QFramework。
    /// </summary>
    public sealed class InputService : IInputService
    {
        /// <summary>改键监听的最长等待秒数。超时就返回 TimedOut，避免玩家忘了自己在改键。</summary>
        public const float DefaultRebindTimeoutSeconds = 10f;

        /// <summary>
        /// 冲突检测要扫哪些 Map。
        ///
        /// 刻意**不含 UI**：UI Map 的按键由 EventSystem 消费，而上下文切换已经
        /// 保证了「UI 开时 Gameplay 关」，两者不会同时吃到同一次按键。
        /// 更要紧的是，UI 里有一处**故意**的重复 ——
        /// <c>UI/Cancel</c> 与 <c>Global/Back</c> 都绑 Escape（语义不同、接收方不同，
        /// 见 InputAssetGenerator 里的注释）。把 UI 纳入扫描会让这处设计被反复
        /// 误报成冲突。
        /// </summary>
        private static readonly InputMapType[] ConflictScanMaps =
        {
            InputMapType.Gameplay,
            InputMapType.Global,
            InputMapType.Debug,
        };

        /// <summary>按 <see cref="InputMapType"/> 的定义顺序排列，供上下文切换时遍历。</summary>
        private static readonly InputMapType[] AllMaps =
        {
            InputMapType.Ui,
            InputMapType.Gameplay,
            InputMapType.Global,
            InputMapType.Debug,
        };

        private readonly InputActionAsset _asset;
        private readonly List<InputAction> _watched = new List<InputAction>(16);

        private InputContext _context = InputContext.None;
        private bool _initialized;
        private bool _isRebinding;
        private bool _debugMapEnabled;
        private bool _disposed;

        public InputService(InputActionAsset asset, InputContext initialContext = InputContext.None)
        {
            _asset = asset != null ? asset : throw new ArgumentNullException(nameof(asset));
            _context = initialContext;
            _debugMapEnabled = InputMapUtil.DebugMapAllowedByBuild;
        }

        // ------------------------------------------------------------------
        //  状态
        // ------------------------------------------------------------------

        public bool IsReady => _initialized && !_disposed;

        public InputContext Context => _context;

        public bool IsRebinding => _isRebinding;

        public bool DebugMapEnabled
        {
            get => _debugMapEnabled;
            set
            {
                if (_debugMapEnabled == value) return;
                _debugMapEnabled = value;
                if (_initialized) ApplyContext(_context, force: true);
            }
        }

        // ------------------------------------------------------------------
        //  初始化与释放
        // ------------------------------------------------------------------

        public void Initialize()
        {
            ThrowIfDisposed();
            if (_initialized) return;

            _watched.Clear();
            WatchMap(InputMapType.Gameplay);
            WatchMap(InputMapType.Global);
            WatchMap(InputMapType.Debug);

            _initialized = true;

            // 先全部禁用，再按上下文启用 —— 避免资产在 Inspector 里被手改成
            // "已启用"状态，导致初始化瞬间有几帧漏输入出去。
            DisableAllMaps();
            ApplyContext(_context, force: true);
        }

        public void Dispose()
        {
            if (_disposed) return;

            // 顺序很重要：先把该清理的清理完，**最后**才置 _disposed。
            // 反过来的话，任何内部调用里的 ThrowIfDisposed 都会在半路抛异常，
            // 把剩下的清理工作整段跳过 —— 而且异常发生在 Dispose 内部，
            // 调用方通常不会去看，于是变成"看起来释放了、其实没有"。
            for (int i = 0; i < _watched.Count; i++)
            {
                var a = _watched[i];
                if (a == null) continue;
                a.performed -= OnActionPerformedInternal;
                a.canceled -= OnActionCanceledInternal;
            }
            _watched.Clear();

            DisableAllMaps();
            _initialized = false;

            // 注意：_asset 是外部传进来的，**不在这里 Dispose**。
            // 它的生命周期由加载它的人（Bootstrap / 资源系统）负责 ——
            // 服务无权销毁一个不属于自己的对象。
            _disposed = true;
        }

        private void WatchMap(InputMapType type)
        {
            var map = FindMap(type);
            if (map == null) return;

            foreach (var action in map.actions)
            {
                action.performed += OnActionPerformedInternal;
                action.canceled += OnActionCanceledInternal;
                _watched.Add(action);
            }
        }

        // ------------------------------------------------------------------
        //  事件转发
        //  参数统一用 Action 名（字符串），而不是 InputAction 对象 ——
        //  这样业务层拿到的永远只是"发生了什么事"，而不是"哪个输入对象"，
        //  从源头上堵住"业务自己去改 action"这条路。
        // ------------------------------------------------------------------

        private void OnActionPerformedInternal(InputAction.CallbackContext ctx)
        {
            if (_isRebinding) return;                       // 改键期间不往业务发
            ActionPerformed?.Invoke(ctx.action.name);
        }

        private void OnActionCanceledInternal(InputAction.CallbackContext ctx)
        {
            if (_isRebinding) return;
            ActionCanceled?.Invoke(ctx.action.name);
        }

        public event Action<string> ActionPerformed;
        public event Action<string> ActionCanceled;
        public event Action<InputContext> ContextChanged;

        // ------------------------------------------------------------------
        //  上下文切换
        // ------------------------------------------------------------------

        public void SwitchToContext(InputContext context)
        {
            ThrowIfDisposed();
            if (_context == context) return;

            ApplyContext(context, force: false);
            ContextChanged?.Invoke(context);
        }

        public void SetMapEnabled(InputMapType map, bool enabled)
        {
            ThrowIfDisposed();
            SetMapEnabledInternal(map, enabled);
        }

        /// <summary>
        /// 真正干活的版本，不做 disposed 检查。
        /// </summary>
        /// <remarks>
        /// ⚠ 内部流程（上下文切换、<see cref="Dispose"/>）必须走这个，不能走公开的
        /// <see cref="SetMapEnabled"/>。否则 <c>Dispose</c> 里调
        /// <c>DisableAllMaps</c> 时，因为 <c>_disposed</c> 已经置位，
        /// 会抛 <see cref="ObjectDisposedException"/> 把清理过程拦腰截断 ——
        /// 而那个异常发生在 Dispose 内部，调用方往往根本不看，
        /// 结果是"资源看似释放了、其实没释放干净"。
        /// 这个坑真实踩过（写完后第一次自测就撞上）。
        /// </remarks>
        private void SetMapEnabledInternal(InputMapType map, bool enabled)
        {
            var actionMap = FindMap(map);
            if (actionMap == null) return;

            if (enabled) actionMap.Enable();
            else actionMap.Disable();
        }

        public bool IsMapEnabled(InputMapType map)
        {
            var actionMap = FindMap(map);
            return actionMap != null && actionMap.enabled;
        }

        /// <summary>
        /// 按上下文把每张 Map 设成该有的状态。
        /// </summary>
        /// <param name="force">
        /// 为 true 时即使上下文没变也重新应用一遍。用于「Debug Map 开关变了」
        /// 或「初始化」这类需要强制对齐状态的场合。
        /// </param>
        private void ApplyContext(InputContext context, bool force)
        {
            if (!force && _context == context) return;

            var wanted = InputMapUtil.MapsFor(context);

            for (int i = 0; i < AllMaps.Length; i++)
            {
                var map = AllMaps[i];
                bool enable;

                if (map == InputMapType.Debug)
                {
                    // Debug 不归属任何上下文，独立开关 —— 它跟"玩家在哪个界面"无关，
                    // 只跟"这是不是开发版"有关。
                    // 但 Context == None（加载中）时一律禁掉，保持一致。
                    enable = context != InputContext.None
                             && _debugMapEnabled
                             && InputMapUtil.DebugMapAllowedByBuild;
                }
                else
                {
                    enable = Array.IndexOf(wanted, map) >= 0;
                }

                SetMapEnabledInternal(map, enable);
            }

            _context = context;
        }

        private void DisableAllMaps()
        {
            for (int i = 0; i < AllMaps.Length; i++)
            {
                SetMapEnabledInternal(AllMaps[i], false);
            }
        }

        // ------------------------------------------------------------------
        //  查询与显示
        // ------------------------------------------------------------------

        public int GetBindingCount(InputMapType map, string actionName)
        {
            var action = FindAction(map, actionName);
            return action != null ? action.bindings.Count : 0;
        }

        public string GetDisplayString(InputMapType map, string actionName, int bindingIndex = 0)
        {
            var action = FindAction(map, actionName);
            if (action == null) return "?";
            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count) return "?";

            // GetBindingDisplayString 会做人类可读化：
            //   <Keyboard>/space       → "Space"
            //   <Gamepad>/buttonSouth  → "Button South"（装了手柄布局后是 "A"）
            // 它同时会把 overridePath 考虑进去 —— 所以改键之后这里拿到的就是新键，
            // UI 不需要自己记任何东西。
            var text = action.GetBindingDisplayString(bindingIndex);
            return string.IsNullOrEmpty(text) ? "未设置" : text;
        }

        // ------------------------------------------------------------------
        //  改键
        // ------------------------------------------------------------------

        public async UniTask<InputRebindResult> RebindAsync(InputMapType map, string actionName,
            int bindingIndex, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (_isRebinding)
            {
                return Fail(actionName, bindingIndex, "已有改键流程正在进行。");
            }

            var action = FindAction(map, actionName);
            if (action == null)
            {
                return Fail(actionName, bindingIndex, "找不到 Action：" + map.ToName() + "/" + actionName);
            }
            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count)
            {
                return Fail(actionName, bindingIndex,
                    "Binding 下标越界：" + bindingIndex + "（共 " + action.bindings.Count + " 条）");
            }

            string previousPath = action.bindings[bindingIndex].effectivePath;

            _isRebinding = true;

            // 记下当前上下文，等改键结束原样恢复。
            // 改键期间**全部** Map 禁用 —— 否则玩家为了改"绝技"而按下的键，
            // 会先把绝技放出去。
            var contextBeforeRebind = _context;
            DisableAllMaps();

            var rebind = action.PerformInteractiveRebinding(bindingIndex)
                .WithControlsExcluding("<Mouse>/position")   // 排除指针移动这类噪声
                .WithControlsExcluding("<Mouse>/delta")
                .WithCancelingThrough("<Keyboard>/escape")   // 玩家按 Esc 可以退出改键
                .OnMatchWaitForAnother(0.1f);                // 防抖：等 0.1 秒确认没有后续输入

            bool timedOut = false;
            bool completed;

            try
            {
                rebind.Start();

                double deadline = Time.realtimeSinceStartupAsDouble + DefaultRebindTimeoutSeconds;

                // ⚠ 属性名是 canceled（美式拼写，一个 L），不是 cancelled。
                //   Unity 的 Input System 全库统一用美式拼写，而 C# 生态里
                //   British 拼写也很常见，这是高频拼错点。
                //
                // 这里不用框架自带的 WithTimeout()：它超时后走的是同一条 Canceled
                // 路径，从公开接口分不出「玩家按了 Esc」还是「等太久超时了」——
                // 而这两种情况给玩家的提示完全不同（前者静默，后者要说明原因）。
                // 所以自己看表，把两种结局分开。
                while (!rebind.completed && !rebind.canceled
                       && !ct.IsCancellationRequested
                       && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    await UniTask.NextFrame(CancellationToken.None);
                }

                completed = rebind.completed;

                if (!completed && !rebind.canceled)
                {
                    // 既没完成也没被取消 —— 那就是超时或外部取消，两种都主动 Cancel，
                    // 让 RebindingOperation 走自己的收尾流程，避免留下悬挂的监听。
                    rebind.Cancel();
                    await UniTask.NextFrame(CancellationToken.None);
                    timedOut = !ct.IsCancellationRequested;
                }
            }
            finally
            {
                rebind.Dispose();
                _isRebinding = false;

                // 恢复改键前的上下文。用 force:true —— 因为期间 _context 被我们
                // 搅乱过，靠"值相等就跳过"的判断会漏掉这次恢复。
                ApplyContext(contextBeforeRebind, force: true);
            }

            if (completed)
            {
                string newPath = action.bindings[bindingIndex].effectivePath;
                var conflict = FindConflict(action, bindingIndex);
                return new InputRebindResult(RebindOutcome.Succeeded, actionName, bindingIndex,
                    previousPath, newPath, conflict);
            }

            if (timedOut)
            {
                return new InputRebindResult(RebindOutcome.TimedOut, actionName, bindingIndex, previousPath);
            }

            return new InputRebindResult(RebindOutcome.Cancelled, actionName, bindingIndex, previousPath);
        }

        /// <summary>
        /// 扫描全资产，看这个新绑定的按键是不是已经被别处用了。
        /// </summary>
        /// <remarks>
        /// 只扫 <see cref="ConflictScanMaps"/>，理由见该字段的注释。
        /// </remarks>
        private InputBindingConflict FindConflict(InputAction action, int bindingIndex)
        {
            string newPath = action.bindings[bindingIndex].effectivePath;
            if (string.IsNullOrEmpty(newPath)) return default;

            for (int m = 0; m < ConflictScanMaps.Length; m++)
            {
                var map = FindMap(ConflictScanMaps[m]);
                if (map == null) continue;

                foreach (var other in map.actions)
                {
                    for (int i = 0; i < other.bindings.Count; i++)
                    {
                        if (other == action && i == bindingIndex) continue;

                        var binding = other.bindings[i];

                        // 组合节点本身（isComposite）没有实际路径，它的路径信息
                        // 分散在 isPartOfComposite 的子项里 —— 所以跳过组合节点本身，
                        // 但**不跳过**子项（WASD 的 W 确实会跟别的功能抢键）。
                        if (binding.isComposite) continue;

                        if (string.Equals(binding.effectivePath, newPath, StringComparison.OrdinalIgnoreCase))
                        {
                            return new InputBindingConflict(map.name, other.name, i, newPath);
                        }
                    }
                }
            }

            return default;
        }

        private static InputRebindResult Fail(string actionName, int bindingIndex, string message)
        {
            return new InputRebindResult(RebindOutcome.Failed, actionName, bindingIndex,
                errorMessage: message);
        }

        // ------------------------------------------------------------------
        //  改键的恢复默认
        // ------------------------------------------------------------------

        public bool ResetBinding(InputMapType map, string actionName, int bindingIndex)
        {
            ThrowIfDisposed();

            var action = FindAction(map, actionName);
            if (action == null) return false;
            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count) return false;

            // 没被改过就没有 override，此时删是空操作 —— 直接返回 false，
            // 让 UI 可以据此判断"这一项本来就没动过"。
            if (string.IsNullOrEmpty(action.bindings[bindingIndex].overridePath)) return false;

            action.RemoveBindingOverride(bindingIndex);
            return true;
        }

        public void ResetAllBindings()
        {
            ThrowIfDisposed();
            _asset.RemoveAllBindingOverrides();
        }

        public bool HasOverrides
        {
            get
            {
                if (_asset == null) return false;

                foreach (var map in _asset.actionMaps)
                {
                    foreach (var action in map.actions)
                    {
                        foreach (var binding in action.bindings)
                        {
                            if (!string.IsNullOrEmpty(binding.overridePath)) return true;
                        }
                    }
                }
                return false;
            }
        }

        // ------------------------------------------------------------------
        //  持久化
        //  ⚠ 本类不碰文件系统。导出的是字符串，存哪儿由存档层决定。
        //    这样「多账号」「云同步」「存档回滚」都不会跟输入系统耦合。
        // ------------------------------------------------------------------

        public string SaveOverrides()
        {
            return _asset != null ? _asset.SaveBindingOverridesAsJson() : null;
        }

        public void LoadOverrides(string json)
        {
            ThrowIfDisposed();
            if (_asset == null) return;

            if (string.IsNullOrEmpty(json))
            {
                _asset.RemoveAllBindingOverrides();
                return;
            }

            _asset.LoadBindingOverridesFromJson(json);
        }

        // ------------------------------------------------------------------
        //  查找
        // ------------------------------------------------------------------

        private InputActionMap FindMap(InputMapType type)
        {
            if (_asset == null) return null;
            var name = type.ToName();
            if (string.IsNullOrEmpty(name)) return null;
            return _asset.FindActionMap(name, throwIfNotFound: false);
        }

        private InputAction FindAction(InputMapType type, string actionName)
        {
            if (_asset == null || string.IsNullOrEmpty(actionName)) return null;
            var mapName = type.ToName();
            if (string.IsNullOrEmpty(mapName)) return null;

            // FindAction 支持 "Map/Action" 这种带路径的写法。
            return _asset.FindAction(mapName + "/" + actionName, throwIfNotFound: false);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(InputService));
            }
        }
    }
}
