// ============================================================================
//  WanXiang · QFramework 输入接入层
//  ---------------------------------------------------------------------------
//  输入框架本身不依赖 QFramework（原因见 IInputService.cs 顶部说明）。
//  接入方式是"把 InputService 包一层，注册成 Utility"。
//
//  为什么要包一层，而不是直接让 InputService 实现 IUtility：
//    因为 IUtility 来自 QFramework。让它出现在 WanXiang.Runtime 上，
//    等于把整个框架和 QFramework 焊死 —— 以后想换架构框架（或干脆不用）
//    就得改运行时核心。包一层的代价是一个 30 行的类，换来的是核心层干净。
//
//  另一个职责：把 InputService 的字符串事件翻译成强类型事件。
//    InputService 只能发 ActionPerformed("Ultimate") 这种带字符串的，
//    因为字符串是它唯一能保证在无 QFramework 环境下也可用的表示。
//    翻译工作在这里做，业务层就只需要 RegisterEvent<UltimateTriggeredEvent>。
//
//  注册方式（抄进你的 Architecture 子类）：
//
//    public class WanXiangArchitecture : Architecture<WanXiangArchitecture>
//    {
//        /// <summary>由启动流程在 Init 之前注入（InputBootstrap 或资源系统加载）。</summary>
//        public static InputActionAsset InputAsset;
//
//        protected override void Init()
//        {
//            RegisterUtility<InputServiceUtility>(
//                new InputServiceUtility(this, InputAsset, InputContext.None));
//
//            // 其余 Model / System 照常注册……
//        }
//    }
//
//  业务里的用法：
//
//    // 切上下文（界面状态变化时调，别自己去 Enable/Disable Map）
//    this.GetUtility<InputServiceUtility>().Input.SwitchToContext(InputContext.Gameplay);
//
//    // 收输入
//    this.RegisterEvent<UltimateTriggeredEvent>(e => { /* 释放绝技 */ });
//
//    // 按键提示（永远运行时取值，不要写死字符串）
//    var ui = this.GetUtility<InputServiceUtility>().Input;
//    prompt.text = "按 [" + ui.GetDisplayString(InputMapType.Gameplay, InputActionNames.Ultimate) + "] 释放绝技";
// ============================================================================

#if WANXIANG_QFRAMEWORK

using System;
using QFramework;
using UnityEngine.InputSystem;
using WanXiang.Framework.Inputs;

namespace WanXiang.Framework.Integration
{
    /// <summary>
    /// 输入系统在 QFramework 里的宿主（Utility 角色）。
    /// </summary>
    public sealed class InputServiceUtility : IUtility, IDisposable
    {
        private readonly IArchitecture _architecture;
        private readonly InputService _inner;

        private bool _subscribed;
        private bool _disposed;

        /// <param name="architecture">
        /// 所属架构。用于发事件 —— Utility 本身没有发事件的能力
        /// （<see cref="IUtility"/> 是个空标记接口），所以必须显式拿到架构。
        /// 在 <c>Init()</c> 里传 <c>this</c> 即可。
        /// </param>
        /// <param name="asset">输入资产，通常由启动流程或资源系统加载后注入。</param>
        public InputServiceUtility(IArchitecture architecture, InputActionAsset asset,
            InputContext initialContext = InputContext.None)
        {
            _architecture = architecture ?? throw new ArgumentNullException(nameof(architecture));
            if (asset == null) throw new ArgumentNullException(nameof(asset));

            _inner = new InputService(asset, initialContext);
        }

        /// <summary>
        /// 输入服务本体。首次访问会自动初始化。
        /// </summary>
        /// <remarks>
        /// 刻意不在构造里就 Initialize：构造发生在
        /// <c>Architecture.Init()</c> 期间，那一刻未必处于播放状态，
        /// 提前 Enable 输入 Map 会引发 Unity 的编辑器警告。
        /// 延迟到第一次真正用到时再初始化，时机必定是安全的。
        /// 需要更早启动的场合，显式调 <see cref="Initialize"/>。
        /// </remarks>
        public IInputService Input
        {
            get
            {
                EnsureInitialized();
                return _inner;
            }
        }

        /// <summary>显式初始化。启动流程希望输入尽早就绪时调它。</summary>
        public void Initialize() => EnsureInitialized();

        private void EnsureInitialized()
        {
            ThrowIfDisposed();

            if (!_inner.IsReady)
            {
                _inner.Initialize();
            }

            if (_subscribed) return;

            _inner.ActionPerformed += OnActionPerformed;
            _inner.ActionCanceled += OnActionCanceled;
            _inner.ContextChanged += OnContextChanged;
            _subscribed = true;
        }

        // ------------------------------------------------------------------
        //  字符串事件 → 强类型事件
        // ------------------------------------------------------------------

        private void OnActionPerformed(string actionName)
        {
            // 先发通用事件，再发具名事件 —— 顺序固定下来，
            // 免得以后有人同时监听两者时，行为随实现变动而变。
            _architecture.SendEvent(new InputActionPerformedEvent { ActionName = actionName });

            switch (actionName)
            {
                case InputActionNames.Ultimate:
                    _architecture.SendEvent<UltimateTriggeredEvent>();
                    break;

                case InputActionNames.OverrideCelestial:
                    _architecture.SendEvent<CelestialOverrideTriggeredEvent>();
                    break;

                case InputActionNames.ToggleSpeed:
                    _architecture.SendEvent<SpeedBoostPressedEvent>();
                    break;

                case InputActionNames.Back:
                    _architecture.SendEvent<BackPressedEvent>();
                    break;

                case InputActionNames.Menu:
                    _architecture.SendEvent<MenuPressedEvent>();
                    break;

                case InputActionNames.Screenshot:
                    _architecture.SendEvent<ScreenshotPressedEvent>();
                    break;

                case InputActionNames.ToggleGmPanel:
                    _architecture.SendEvent<GmPanelToggleEvent>();
                    break;

                case InputActionNames.ReloadConfig:
                    _architecture.SendEvent<ConfigReloadEvent>();
                    break;

                case InputActionNames.ToggleGodMode:
                    _architecture.SendEvent<GodModeToggleEvent>();
                    break;

                // 没有具名事件的 Action 到此为止（通用事件已发过）。
                // 新增 Action 时在这里补一条 case 即可。
            }
        }

        private void OnActionCanceled(string actionName)
        {
            _architecture.SendEvent(new InputActionCanceledEvent { ActionName = actionName });

            // 目前只有「加速」需要知道松手时机（长按加速的玩法）。
            // 其余按键是纯触发式的，松开没有语义，不发具名事件。
            if (actionName == InputActionNames.ToggleSpeed)
            {
                _architecture.SendEvent<SpeedBoostReleasedEvent>();
            }
        }

        private void OnContextChanged(InputContext context)
        {
            _architecture.SendEvent(new InputContextChangedEvent { Context = context });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_subscribed)
            {
                _inner.ActionPerformed -= OnActionPerformed;
                _inner.ActionCanceled -= OnActionCanceled;
                _inner.ContextChanged -= OnContextChanged;
                _subscribed = false;
            }

            _inner.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(InputServiceUtility));
        }
    }
}

#endif
