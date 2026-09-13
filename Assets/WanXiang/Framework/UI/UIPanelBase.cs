// ============================================================================
//  WanXiang · UI 框架 · 面板基类
//  ---------------------------------------------------------------------------
//  所有业务面板继承本类。框架负责生命周期调用、事件订阅的自动回收、
//  层级与遮罩管理；业务只需要关心"我这个界面长什么样、点了之后干什么"。
//
//  为什么事件订阅要由框架统一回收（本文件最重要的一处设计）：
//    手写 RegisterEvent 而忘记 Unregister 是 UI 项目最高频的 bug 来源，后果有三：
//      1. 内存泄漏 —— 事件持有面板引用，面板对象永远无法被 GC
//      2. 空引用崩溃 —— 事件触发时面板已销毁，访问已释放的组件
//      3. 逻辑错乱 —— 已经关闭的面板仍在响应事件，比如背包关了还在刷新
//    框架用 _disposables 统一收集、关闭时统一释放，把"依赖开发者自觉"
//    变成"框架保证"。这是商业项目和练习项目的分水岭之一。
//
//  动效（tween）也归框架管，理由与上面完全同构：
//    Normal 层面的面板走 Cached 策略，关闭时只是 SetActive(false)，对象并不销毁。
//    若此刻还有 tween 在跑，它会继续修改一个"已经关掉"的面板 —— 下次打开时
//    面板就带着残留状态出现：半透明、歪了一点、位置偏了几像素。
//    更糟的是 Cached 面板被淘汰销毁时，tween 攥着的是已销毁对象。
//    所以动效必须经 TrackTween 登记，由框架在关闭时统一 Kill。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;      // 面板动效（见 TrackTween）
using UnityEngine;
using UnityEngine.UI;   // CanvasGroup

namespace WanXiang.Framework.UI
{
    /// <summary>可释放句柄。用于把"取消订阅"这个动作包装成可统一收集的对象。</summary>
    internal sealed class UIActionDisposable : IDisposable
    {
        private Action _onDispose;

        public UIActionDisposable(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            var action = _onDispose;
            _onDispose = null;
            action?.Invoke();
        }
    }

    [RequireComponent(typeof(RectTransform))]
    public abstract class UIPanelBase : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // 元数据（由框架在实例化后注入，业务只读）
        // ------------------------------------------------------------------

        /// <summary>资源 key。</summary>
        public string Key { get; private set; }

        /// <summary>所属层级。</summary>
        public UILayer Layer { get; private set; }

        /// <summary>缓存策略。</summary>
        public UICachePolicy CachePolicy { get; private set; }

        /// <summary>点击遮罩是否关闭。</summary>
        public bool CloseOnMaskClick { get; private set; }

        /// <summary>是否全屏独占。</summary>
        public bool FullScreen { get; private set; }

        /// <summary>当前生命周期状态。</summary>
        public UIPanelState State { get; private set; } = UIPanelState.None;

        /// <summary>
        /// 关闭时是否保留事件订阅。
        /// 默认 false（关闭即解绑，安全优先）。
        /// 常驻 HUD 这类需要长期监听数据变化的面板，可覆写为 true —— 但要自己保证
        /// 处理器内部判断 <see cref="State"/>，避免在关闭状态下执行 UI 更新。
        /// </summary>
        protected virtual bool KeepSubscriptionsOnClose => false;

        /// <summary>
        /// 打开时是否自动暂停下层面板。
        ///
        /// 判据是"本面板所在的层是否需要遮罩"（见 <see cref="UILayerUtil.NeedsMask"/>）：
        /// 有遮罩 → 下层输入已被拦住 → 下层没有理由继续跑动画与计时器。
        ///
        /// 注意这里**不**看 <see cref="FullScreen"/>：全屏只是视觉表现（是否铺满屏幕），
        /// 一个居中的半屏确认框同样会拦住下层输入，同样应该让下层停下来。
        /// 把这两件事混在一起，就会出现"确认框弹出着，背包的冷却倒计时还在跑"。
        ///
        /// 个别面板有特殊需求时可覆写本属性（例如"打开背包时 HUD 血条仍要跳动"）。
        /// </summary>
        public virtual bool ShouldPauseBelow => UILayerUtil.NeedsMask(Layer);

        /// <summary>
        /// 是否允许被"返回键 / 手柄 B 键"关闭。
        /// 覆写为 false 的场景：强制引导界面、必须做出选择的弹窗、结算界面。
        /// 千万不要让玩家能用返回键跳过付费确认或新手引导 —— 这类漏洞上线后很难补。
        /// </summary>
        public virtual bool AllowBackClose => true;

        /// <summary>
        /// 打开顺序号。由框架在打开时分配，用于判定面板之间的上下遮挡关系。
        /// </summary>
        public int OpenSequence { get; private set; }

        // ------------------------------------------------------------------
        // 运行时上下文
        // ------------------------------------------------------------------

        /// <summary>
        /// UI 系统引用，由框架注入。业务可用它开关其它面板。
        /// 命名刻意不叫 UISystem —— 那样会与类型名 UISystem 同名，
        /// 派生类里写 UISystem.Close() 时很难一眼看出调的是实例还是类型。
        /// </summary>
        protected IUISystem UI { get; private set; }

        /// <summary>本次打开的入参。在 OnOpenAsync / OnOpened 中可读。</summary>
        protected object Payload { get; private set; }

        private readonly List<IDisposable> _disposables = new List<IDisposable>(8);
        private readonly List<Tween> _trackedTweens = new List<Tween>(4);
        private RectTransform _rect;
        private CanvasGroup _canvasGroup;
        private CancellationTokenSource _panelCts;

        /// <summary>
        /// 面板级取消令牌。面板关闭时自动取消。
        /// 面板内启动的异步任务都应该传入它，避免"面板关了但协程还在跑"。
        /// </summary>
        protected CancellationToken PanelToken =>
            _panelCts?.Token ?? CancellationToken.None;

        protected RectTransform Rect
        {
            get
            {
                if (_rect == null) _rect = transform as RectTransform;
                return _rect;
            }
        }

        /// <summary>CanvasGroup，用于整面板的透明度与射线开关。</summary>
        protected CanvasGroup CanvasGroup
        {
            get
            {
                // 注意这里刻意不用 ?? ：UnityEngine.Object 重载了 == 运算符，
                // 但 ?? 走的是真正的引用判空，两者语义不同。
                // 混用会造成"看着有判空其实没判"的隐患，统一写成显式 if。
                if (_canvasGroup == null)
                {
                    _canvasGroup = GetComponent<CanvasGroup>();
                    if (_canvasGroup == null)
                    {
                        _canvasGroup = gameObject.AddComponent<CanvasGroup>();
                    }
                }
                return _canvasGroup;
            }
        }

        // ==================================================================
        //  框架内部调用（业务不要调用这些）
        // ==================================================================

        internal void InternalInit(UIPanelMeta meta, IUISystem uiSystem)
        {
            Key = meta.Key;
            Layer = meta.Layer;
            CachePolicy = meta.CachePolicy;
            CloseOnMaskClick = meta.CloseOnMaskClick;
            FullScreen = meta.FullScreen;
            UI = uiSystem;

            _rect = transform as RectTransform;
            if (_rect == null)
            {
                // 面板必须是 UI 元素。这里不尝试补救（给普通 GameObject 硬加 RectTransform
                // 会替换掉它的 Transform，造成更难查的问题），直接报错暴露配置错误。
                Debug.LogError(
                    $"[UI] 面板 {GetType().Name} 所在对象上没有 RectTransform，" +
                    $"它不是 UI 元素。请确认面板 Prefab 是在 Canvas 下创建的标准 UI 对象。");
            }

            _panelCts = new CancellationTokenSource();

            State = UIPanelState.Created;
            try
            {
                OnCreate();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI] 面板 {GetType().Name}.OnCreate 抛出异常：{ex}");
            }
        }

        internal async UniTask InternalOpenAsync(object payload, int openSequence)
        {
            OpenSequence = openSequence;
            Payload = payload;
            gameObject.SetActive(true);
            SetInteractive(true);

            State = UIPanelState.Opening;
            try
            {
                await OnOpenAsync(payload);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI] 面板 {GetType().Name}.OnOpenAsync 抛出异常：{ex}");
            }

            State = UIPanelState.Opened;
            try
            {
                OnOpened();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI] 面板 {GetType().Name}.OnOpened 抛出异常：{ex}");
            }
        }

        /// <summary>
        /// 面板已处于打开状态，又被 OpenAsync 调用了一次时执行（不重放 OnOpenAsync）。
        /// 用途：连续查看图鉴时"从异兽甲切到异兽乙"，避免闪一下面板。
        /// </summary>
        internal void InternalRefresh(object payload)
        {
            Payload = payload;
            try
            {
                OnRefresh(payload);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI] 面板 {GetType().Name}.OnRefresh 抛出异常：{ex}");
            }
        }

        internal void InternalPause()
        {
            if (State != UIPanelState.Opened) return;

            State = UIPanelState.Paused;
            SetInteractive(false);
            try
            {
                OnPause();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI] 面板 {GetType().Name}.OnPause 抛出异常：{ex}");
            }
        }

        internal void InternalResume()
        {
            if (State != UIPanelState.Paused) return;

            State = UIPanelState.Opened;
            SetInteractive(true);
            try
            {
                OnResume();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI] 面板 {GetType().Name}.OnResume 抛出异常：{ex}");
            }
        }

        internal void InternalClose()
        {
            if (State == UIPanelState.Closed ||
                State == UIPanelState.Closing ||
                State == UIPanelState.None) return;

            State = UIPanelState.Closing;
            SetInteractive(false);

            try
            {
                OnClose();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI] 面板 {GetType().Name}.OnClose 抛出异常：{ex}");
            }

            // 取消面板内所有异步任务
            try
            {
                if (_panelCts != null && !_panelCts.IsCancellationRequested)
                {
                    _panelCts.Cancel();
                }
            }
            catch (ObjectDisposedException) { /* 忽略 */ }

            // 统一释放事件订阅
            if (!KeepSubscriptionsOnClose)
            {
                DisposeSubscriptions();
            }

            // 统一停掉本面板登记的动效。
            // 必须显式 Kill：DOTween 的 tween 由它自己的 update loop 驱动，
            // 与 GameObject 的 active 状态无关 —— 面板 SetActive(false) 之后
            // 动效照样会继续跑、继续改属性。
            KillTrackedTweens();

            Payload = null;
            gameObject.SetActive(false);
            State = UIPanelState.Closed;
        }

        /// <summary>重新打开一个 Closed 状态的面板前调用：重建取消令牌。</summary>
        internal void PrepareReopen()
        {
            if (_panelCts == null || _panelCts.IsCancellationRequested)
            {
                try { _panelCts?.Dispose(); } catch { /* 忽略 */ }
                _panelCts = new CancellationTokenSource();
            }

            // 若关闭时解绑了订阅，重新打开时需重新订阅
            if (!KeepSubscriptionsOnClose && State == UIPanelState.Closed)
            {
                State = UIPanelState.Created;
            }
        }

        internal void InternalDestroy()
        {
            DisposeSubscriptions();
            KillTrackedTweens();

            try
            {
                if (_panelCts != null)
                {
                    if (!_panelCts.IsCancellationRequested) _panelCts.Cancel();
                    _panelCts.Dispose();
                    _panelCts = null;
                }
            }
            catch (Exception) { /* 忽略 */ }

            try
            {
                OnDestroyed();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI] 面板 {GetType().Name}.OnDestroyed 抛出异常：{ex}");
            }

            State = UIPanelState.None;
        }

        private void SetInteractive(bool on)
        {
            var cg = CanvasGroup;
            if (cg != null)
            {
                cg.interactable = on;
                cg.blocksRaycasts = on;
            }
        }

        private void DisposeSubscriptions()
        {
            for (int i = 0; i < _disposables.Count; i++)
            {
                try { _disposables[i]?.Dispose(); }
                catch (Exception ex) { Debug.LogError($"[UI] 释放订阅失败：{ex}"); }
            }
            _disposables.Clear();
        }

        /// <summary>
        /// 停掉本面板登记过的所有动效。
        ///
        /// 不靠 SetLink(KillOnDestroy) 兜底 —— 那只覆盖"对象被销毁"，
        /// 覆盖不到"面板关了但对象留着复用"，而这恰是 Cached 策略的常态。
        /// </summary>
        private void KillTrackedTweens()
        {
            for (int i = _trackedTweens.Count - 1; i >= 0; i--)
            {
                var tween = _trackedTweens[i];
                if (tween != null && tween.IsActive())
                {
                    tween.Kill(false);
                }
            }
            _trackedTweens.Clear();
        }

        // ==================================================================
        //  业务重写点
        // ==================================================================

        /// <summary>
        /// 只执行一次：缓存组件引用、准备对象池。
        /// 【不要在这里订阅事件】—— 事件订阅请写在 <see cref="OnOpenAsync"/> 中，
        /// 框架会在关闭时统一解绑，并在下次打开时重新调用 OnOpenAsync。
        /// </summary>
        protected abstract void OnCreate();

        /// <summary>每次打开时调用。事件订阅、数据绑定写在这里。</summary>
        protected virtual UniTask OnOpenAsync(object payload) => UniTask.CompletedTask;

        /// <summary>
        /// 面板已打开时再次被 OpenAsync 调用（如连续查看图鉴下一个条目）。
        /// 若未覆写，框架会退化为"什么都不做"，面板保持原样。
        /// </summary>
        protected virtual void OnRefresh(object payload) { }

        /// <summary>打开动画/过渡结束后调用。</summary>
        protected virtual void OnOpened() { }

        /// <summary>
        /// 被上层全屏面板遮挡时调用。
        /// 在这里停止动画、停掉计时器、暂停滚动 —— 别忘了，
        /// 少了这个回调，会出现"背包被详情页盖住但冷却倒计时还在跑"。
        /// </summary>
        protected virtual void OnPause() { }

        /// <summary>上层面板关闭、重新可见时调用。</summary>
        protected virtual void OnResume() { }

        /// <summary>每次关闭时调用。框架随后会自动解绑订阅、取消 PanelToken、禁用 GameObject。</summary>
        protected virtual void OnClose() { }

        /// <summary>被真正销毁前调用（Transient 策略关闭后、场景卸载前）。</summary>
        protected virtual void OnDestroyed() { }

        // ==================================================================
        //  业务辅助 API
        // ==================================================================

        /// <summary>关闭自身（等价于 <c>UI.Close(this)</c>）。</summary>
        public void CloseSelf()
        {
            UI?.Close(this);
        }

        /// <summary>按类型关闭自身（推荐用这个）。</summary>
        protected void CloseSelf<T>() where T : UIPanelBase
        {
            UI?.Close<T>();
        }

        /// <summary>打开另一个面板。</summary>
        protected UniTask<T> OpenPanelAsync<T>(object payload = null) where T : UIPanelBase
        {
            if (UI == null)
            {
                Debug.LogError($"[UI] {GetType().Name} 的 UISystem 未注入，无法打开 {typeof(T).Name}");
                return UniTask.FromResult<T>(null);
            }
            return UI.OpenAsync<T>(payload);
        }

        /// <summary>
        /// 登记一个动效，面板关闭时由框架统一 Kill。
        ///
        /// 用法：
        /// <code>
        /// TrackTween(CanvasGroup.DOFade(1f, 0.2f));
        /// TrackTween(Rect.DOScale(Vector3.one, 0.22f).SetEase(Ease.OutBack));
        /// </code>
        ///
        /// 除登记之外，这里顺手做了 SetLink(KillOnDestroy)：万一业务漏了处理，
        /// Cached 面板被淘汰销毁时 DOTween 也会自己把动效收掉，不会去戳已销毁对象。
        /// </summary>
        protected T TrackTween<T>(T tween) where T : Tween
        {
            if (tween == null) return null;

            // 同一实例只登记一次：面板反复打开时业务可能重复开头同一个动效
            // （例如复用缓存的 Sequence），重复登记会让列表无谓增长
            if (!_trackedTweens.Contains(tween))
            {
                tween.SetLink(gameObject, LinkBehaviour.KillOnDestroy);
                _trackedTweens.Add(tween);
            }
            return tween;
        }

        /// <summary>
        /// 注册一个可自动释放的订阅。
        /// 用法：<c>Register(disposable);</c> 或配合 <see cref="RegisterEvent{T}"/>。
        /// </summary>
        protected void Register(IDisposable disposable)
        {
            if (disposable == null) return;
            _disposables.Add(disposable);
        }

        /// <summary>
        /// 注册一个基于事件的订阅（把注册与注销函数配对传入）。
        ///
        /// 示例（接入 QFramework 的 TypeEventSystem）：
        /// <code>
        /// RegisterEvent&lt;PlayerGoldChangedEvent&gt;(
        ///     h => this.RegisterEvent(h),
        ///     h => this.UnRegisterEvent(h),
        ///     OnGoldChanged);
        /// </code>
        /// 这样写虽然比直接调用 RegisterEvent 多两个参数，但换来的是
        /// "绝不可能忘记解绑" —— 值得。
        /// </summary>
        protected void RegisterEvent<TDelegate>(
            Func<TDelegate, IDisposable> subscribe,
            TDelegate handler) where TDelegate : Delegate
        {
            if (subscribe == null || handler == null) return;
            Register(subscribe(handler));
        }

        /// <summary>启动一个与面板生命周期绑定的异步任务。面板关闭时自动取消。</summary>
        protected void RunTask(Func<CancellationToken, UniTask> task)
        {
            if (task == null) return;
            RunTaskInternal(task).Forget();
        }

        private async UniTaskVoid RunTaskInternal(Func<CancellationToken, UniTask> task)
        {
            try
            {
                await task(PanelToken);
            }
            catch (OperationCanceledException)
            {
                // 面板关闭导致的正常取消，不视为错误
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI] 面板 {GetType().Name} 异步任务异常：{ex}");
            }
        }

        // ==================================================================
        //  编辑器辅助
        // ==================================================================

        /// <summary>从类型名推导默认资源 key：BackpackPanel → Panel_Backpack</summary>
        internal static string DeriveDefaultKey(Type type)
        {
            string name = type.Name;
            if (name.EndsWith("Panel", StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - "Panel".Length);
            }
            return "Panel_" + name;
        }
    }

    /// <summary>面板元数据（运行时值对象）。由 UISystem 从特性解析得到。</summary>
    public struct UIPanelMeta
    {
        public string Key;
        public UILayer Layer;
        public UICachePolicy CachePolicy;
        public bool CloseOnMaskClick;
        public bool FullScreen;
    }
}
