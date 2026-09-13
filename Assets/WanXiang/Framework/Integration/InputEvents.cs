// ============================================================================
//  万相 · 输入事件（QFramework 侧）
//  ---------------------------------------------------------------------------
//  为什么事件定义放在集成层，而不是 WanXiang.Runtime：
//    WanXiang.Runtime 不依赖 QFramework，所以它只能发原生 C# 事件
//    （InputService.ActionPerformed 那种带字符串的）。
//    强类型事件类属于「QFramework 的用法约定」，所以归集成层。
//    未接入 QFramework 时，整个集成层（含本文件）不参与编译，框架照常工作。
//
//  业务代码这样用：
//
//    this.RegisterEvent<UltimateTriggeredEvent>(e =>
//    {
//        // 释放绝技
//    });
//
//  而不是：
//
//    this.RegisterEvent<InputActionPerformedEvent>(e =>
//    {
//        if (e.ActionName == "Ultimate") { ... }   // ← 字符串比较，写错不报错
//    });
//
//  后者不会编译报错，只会静默失效 —— 所以每个业务关心的 Action 都给了
//  一个具名事件类型。加新 Action 时，在 QFrameworkInputService 的 switch
//  里补一条即可。
//
//  ⚠ 事件类型刻意用 struct：QFramework 的 SendEvent<T>() 带 `where T : new()`
//    约束，struct 天然满足（C# 10 之前 struct 不能自定义无参构造）。
//    用 class 也行，但 struct 不产生堆分配，且值语义更贴近「事件快照」。
// ============================================================================

#if WANXIANG_QFRAMEWORK

using WanXiang.Framework.Inputs;

namespace WanXiang.Framework.Integration
{
    // ------------------------------------------------------------------------
    //  Gameplay —— 战斗中的玩家干预
    // ------------------------------------------------------------------------

    /// <summary>释放绝技。对应 <c>Gameplay/Ultimate</c>，默认 Space / 手柄 A。</summary>
    public struct UltimateTriggeredEvent { }

    /// <summary>天时覆盖。对应 <c>Gameplay/OverrideCelestial</c>，默认 Q / 手柄 X。</summary>
    public struct CelestialOverrideTriggeredEvent { }

    /// <summary>
    /// 战斗加速——按下。对应 <c>Gameplay/ToggleSpeed</c>，默认左 Shift / 手柄 Y。
    /// </summary>
    /// <remarks>
    /// 「加速」在不同游戏里可能是长按也可能是切换。框架不替你做这个决定，
    /// 所以按下与松开各发一个事件，由玩法层自己组合成想要的手感。
    /// </remarks>
    public struct SpeedBoostPressedEvent { }

    /// <summary>战斗加速——松开。</summary>
    public struct SpeedBoostReleasedEvent { }

    // ------------------------------------------------------------------------
    //  Global —— 与场景无关的全局键
    // ------------------------------------------------------------------------

    /// <summary>
    /// 返回上一层。对应 <c>Global/Back</c>，默认 Escape / 手柄 B。
    /// </summary>
    /// <remarks>
    /// 通常直接转交给 UISystem 的返回栈处理，不必自己写导航逻辑。
    /// </remarks>
    public struct BackPressedEvent { }

    /// <summary>打开菜单。对应 <c>Global/Menu</c>，默认 Tab / 手柄 Start。</summary>
    public struct MenuPressedEvent { }

    /// <summary>截图。对应 <c>Global/Screenshot</c>，默认 F12（PC 专属，手柄无绑定）。</summary>
    public struct ScreenshotPressedEvent { }

    // ------------------------------------------------------------------------
    //  Debug —— 仅开发版。正式包里 Debug Map 整张禁用，这些事件不会发出。
    // ------------------------------------------------------------------------

    /// <summary>开关 GM 面板。对应 <c>Debug/ToggleGmPanel</c>，默认 F3。</summary>
    public struct GmPanelToggleEvent { }

    /// <summary>重载配置表。对应 <c>Debug/ReloadConfig</c>，默认 F5。</summary>
    public struct ConfigReloadEvent { }

    /// <summary>开关无敌模式。对应 <c>Debug/ToggleGodMode</c>，默认 F6。</summary>
    public struct GodModeToggleEvent { }

    // ------------------------------------------------------------------------
    //  通用兜底 —— 给「不想为每个键写一个类型」或需要动态处理的场合。
    // ------------------------------------------------------------------------

    /// <summary>
    /// 任意受关注的 Action 被按下。带 Action 名。
    /// </summary>
    /// <remarks>
    /// 每个具名事件（如 <see cref="UltimateTriggeredEvent"/>）发出时，
    /// 本事件也会同帧发出 —— 所以两者可以混用，不会互相吞掉。
    /// </remarks>
    public struct InputActionPerformedEvent
    {
        /// <summary>Action 名，取值见 <see cref="InputActionNames"/>。</summary>
        public string ActionName;
    }

    /// <summary>任意受关注的 Action 被松开。带 Action 名。</summary>
    public struct InputActionCanceledEvent
    {
        /// <summary>Action 名，取值见 <see cref="InputActionNames"/>。</summary>
        public string ActionName;
    }

    /// <summary>
    /// 输入上下文发生了切换。
    /// </summary>
    /// <remarks>
    /// 需要「玩家回到战斗界面时恢复某些表现」这类逻辑时监听它，
    /// 比在每个面板里各写一遍稳妥。
    /// </remarks>
    public struct InputContextChangedEvent
    {
        /// <summary>切换后的上下文。</summary>
        public InputContext Context;
    }
}

#endif
