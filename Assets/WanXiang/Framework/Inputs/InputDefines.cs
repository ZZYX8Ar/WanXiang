// ============================================================================
//  万相 · 输入系统基础定义
//  ---------------------------------------------------------------------------
//  ⚠ 为什么命名空间叫 Inputs 而不是 Input：
//    C# 里当命名空间与类型同名时，**命名空间优先**。若这里叫
//    WanXiang.Framework.Input，那么任何 using 了本命名空间的文件里，
//    写 Input.GetKey(...) 都会报：
//        CS0118: 'Input' is a namespace but is used like a type
//    而旧输入 API 在本工程里还有人在用（QFramework 的 UIKit 与 ConsoleKit
//    就有两处真代码），第三方插件更说不准。这种坑一旦踩上，报错信息完全不
//    指向真正的原因，排查代价极高。加个 s 就彻底绕开了。
//
//  本文件只放「定义」，不放行为 —— 枚举、名字常量、上下文到 Map 的映射表。
//  行为在 InputService.cs。
// ============================================================================

namespace WanXiang.Framework.Inputs
{
    /// <summary>
    /// Action Map 的类型。与 <c>WanXiang.inputactions</c> 里的四张 Map 一一对应。
    /// </summary>
    public enum InputMapType
    {
        /// <summary>UI 导航。由 EventSystem 上的 InputSystemUIInputModule 消费。</summary>
        Ui = 0,

        /// <summary>战斗中的玩家干预：释放绝技、天时覆盖、战斗加速。</summary>
        Gameplay = 1,

        /// <summary>与场景无关的全局键：返回、菜单、截图。</summary>
        Global = 2,

        /// <summary>仅开发版：GM 指令。正式包由 <see cref="InputService"/> 整体禁用。</summary>
        Debug = 3,
    }

    /// <summary>
    /// 输入上下文 —— 「此刻玩家处于什么界面状态」。
    ///
    /// 这是整个输入系统里最重要的一个概念：Map 的启停**不由面板自己决定**，
    /// 而是由框架按上下文统一切换。
    ///
    /// 不这么做的典型事故：战斗中打开设置面板，玩家按空格想确认，
    /// 结果 UI 收到确认的同时 Gameplay 也收到「释放绝技」，大招白白交掉。
    /// 这类 bug 测试期很难复现，线上却很致命。
    /// </summary>
    public enum InputContext
    {
        /// <summary>全部禁用。加载中、过场动画、以及任何「不想吃输入」的时刻。</summary>
        None = 0,

        /// <summary>主界面：UI + Global 启用，Gameplay 禁用。</summary>
        MainMenu = 1,

        /// <summary>战斗中：Gameplay + Global 启用，UI 禁用。</summary>
        Gameplay = 2,

        /// <summary>
        /// 弹窗打开：UI + Global 启用，Gameplay 禁用。
        /// 启用集合与 <see cref="MainMenu"/> 相同，但语义不同 ——
        /// 分开定义是为了以后两者需要分化时（例如弹窗时需要额外禁掉某些 UI 子项），
        /// 改动只落在一处。
        /// </summary>
        Modal = 3,
    }

    /// <summary>
    /// Action Map 的名字常量。与 <c>WanXiang.inputactions</c> 里的 Map 名**逐字对应**，
    /// 改资产里的名字时必须同步改这里。
    /// </summary>
    /// <remarks>
    /// 用常量而不是到处写字符串字面量，是为了让「改名」这件事能被编译器拦住 ——
    /// 写错的字符串只会在运行期变成一个静默的 null。
    /// </remarks>
    public static class InputMapNames
    {
        public const string Ui = "UI";
        public const string Gameplay = "Gameplay";
        public const string Global = "Global";
        public const string Debug = "Debug";
    }

    /// <summary>
    /// Action 的名字常量。
    /// </summary>
    /// <remarks>
    /// 只收录**业务会直接关心**的 Action。UI Map 里那些
    /// （Point / Click / Navigate / Submit / Cancel …）是给
    /// InputSystemUIInputModule 用的，业务代码不该碰，故不在此列出。
    /// </remarks>
    public static class InputActionNames
    {
        // ---- Gameplay ----
        /// <summary>释放绝技。默认 Space / 手柄 A。</summary>
        public const string Ultimate = "Ultimate";

        /// <summary>天时覆盖。默认 Q / 手柄 X。</summary>
        public const string OverrideCelestial = "OverrideCelestial";

        /// <summary>战斗加速。默认左 Shift / 手柄 Y。</summary>
        public const string ToggleSpeed = "ToggleSpeed";

        // ---- Global ----
        /// <summary>返回上一层。默认 Escape / 手柄 B。</summary>
        public const string Back = "Back";

        /// <summary>打开菜单。默认 Tab / 手柄 Start。</summary>
        public const string Menu = "Menu";

        /// <summary>截图。默认 F12。PC 平台专属，手柄无绑定。</summary>
        public const string Screenshot = "Screenshot";

        // ---- Debug ----
        /// <summary>GM 面板。默认 F3（刻意避开 F1，QFramework 的 ConsoleKit 用 F1）。</summary>
        public const string ToggleGmPanel = "ToggleGmPanel";

        /// <summary>重载配置表。默认 F5。</summary>
        public const string ReloadConfig = "ReloadConfig";

        /// <summary>无敌模式。默认 F6。</summary>
        public const string ToggleGodMode = "ToggleGodMode";
    }

    /// <summary>
    /// 枚举与资产名字之间的转换，以及「上下文 → 启用哪些 Map」的映射。
    /// </summary>
    public static class InputMapUtil
    {
        // 这些数组是共享的只读常量，**调用方不要就地修改**（别 Sort、别 Clear）。
        private static readonly InputMapType[] MainMenuMaps = { InputMapType.Ui, InputMapType.Global };
        private static readonly InputMapType[] GameplayMaps = { InputMapType.Gameplay, InputMapType.Global };
        private static readonly InputMapType[] NoMaps = new InputMapType[0];

        /// <summary>拿到 Map 在资产里的名字。</summary>
        public static string ToName(this InputMapType map)
        {
            switch (map)
            {
                case InputMapType.Ui:       return InputMapNames.Ui;
                case InputMapType.Gameplay: return InputMapNames.Gameplay;
                case InputMapType.Global:   return InputMapNames.Global;
                case InputMapType.Debug:    return InputMapNames.Debug;
                default:                    return null;
            }
        }

        /// <summary>
        /// 上下文对应的启用集合，直接来自 ARCHITECTURE.md §6.2 那张表：
        /// <code>
        /// 主界面 | UI + Global     | Gameplay 禁用
        /// 战斗中 | Gameplay + Global | UI 禁用
        /// 打开弹窗 | UI + Global   | Gameplay 禁用
        /// 加载中 | —              | 全部禁用
        /// </code>
        /// </summary>
        /// <returns>
        /// 共用的只读数组。**不要在调用方就地修改**，需要改就先复制一份。
        /// </returns>
        public static InputMapType[] MapsFor(InputContext context)
        {
            switch (context)
            {
                case InputContext.MainMenu: return MainMenuMaps;
                case InputContext.Gameplay: return GameplayMaps;
                case InputContext.Modal:    return MainMenuMaps;
                default:                    return NoMaps;
            }
        }

        /// <summary>
        /// Debug Map 是否应该跟随上下文自动启用。
        /// 编辑器与开发版为 true，正式包为 false —— 避免玩家误触 GM 指令。
        /// </summary>
        public static bool DebugMapAllowedByBuild
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return true;
#else
                return false;
#endif
            }
        }
    }
}
