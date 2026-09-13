// ============================================================================
//  万相 · 输入服务接口
//  ---------------------------------------------------------------------------
//  核心原则：业务代码不直接引用 InputActionAsset，只通过本接口 + 事件接收输入。
//
//  这样做的收益：改键、多设备、输入重映射全部被封在实现里，业务层完全无感。
//  将来要加手柄或触屏虚拟摇杆，业务代码一行不用改。
//
//  ⚠ 注意 base 接口是纯 C# 的，**没有继承 QFramework 的 IUtility**。
//    这是刻意的：WanXiang.Runtime 不依赖 QFramework（见 ARCHITECTURE.md §2.2），
//    这样「框架能不能单独跑起来」这件事始终是真的 —— 出 bug 时可以先把
//    QFramework 摘掉，确认问题不在集成层。
//    接入 QFramework 由一个薄包装类完成，见 Framework/Integration/。
//
//  ⚠ 接口里刻意不出现 UnityEngine.InputSystem.InputAction：
//    一旦暴露，业务层就会开始直接改 action（改 enabled、加 binding），
//    上下文切换的纪律会当场瓦解 —— 而那正是本系统存在的理由。
//    需要显示按键名？用 GetDisplayString；需要改键？用 RebindAsync。
// ============================================================================

using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace WanXiang.Framework.Inputs
{
    /// <summary>
    /// 输入服务。负责 Action Map 的启停、上下文切换、改键与持久化。
    /// </summary>
    public interface IInputService : IDisposable
    {
        /// <summary>是否已初始化完成（资产已载入、事件已挂上）。</summary>
        bool IsReady { get; }

        /// <summary>当前所处的输入上下文。</summary>
        InputContext Context { get; }

        /// <summary>是否正在进行改键监听。为 true 时业务应忽略所有游戏输入。</summary>
        bool IsRebinding { get; }

        /// <summary>
        /// Debug Map 是否参与自动启停。默认跟随构建类型（编辑器/开发版开启）。
        /// 正式包里即使手动设 true，也会被构建类型判据否决。
        /// </summary>
        bool DebugMapEnabled { get; set; }

        /// <summary>初始化：载入资产、挂上回调、按初始上下文启用 Map。</summary>
        /// <exception cref="InvalidOperationException">重复调用，或资产为空。</exception>
        void Initialize();

        /// <summary>
        /// 切换输入上下文。这是本系统的核心 API —— 界面状态变化时调它，
        /// 不要自己去 Enable/Disable 单张 Map（那会让状态失控）。
        /// </summary>
        /// <remarks>
        /// 实现会先禁用当前上下文的 Map 集合，再启用新集合，并广播
        /// <see cref="ContextChanged"/>。重复设置为同一上下文是安全的空操作。
        /// </remarks>
        void SwitchToContext(InputContext context);

        /// <summary>
        /// 临时开关某一张 Map。用于上下文之外的例外情况
        /// （例如过场动画里只想开 UI）。
        /// </summary>
        /// <remarks>
        /// ⚠ 这是「逃生舱口」，不是常规用法。常规态一律走
        /// <see cref="SwitchToContext"/> —— 混用两者会让「现在哪些 Map 是开的」
        /// 变得无法推理。而且下一次上下文切换会把你的临时改动冲掉。
        /// </remarks>
        void SetMapEnabled(InputMapType map, bool enabled);

        /// <summary>某张 Map 当前是否启用。</summary>
        bool IsMapEnabled(InputMapType map);

        /// <summary>某个 Action 上挂了几个绑定（改键界面要按这个数量列行）。找不到返回 0。</summary>
        int GetBindingCount(InputMapType map, string actionName);

        /// <summary>
        /// 拿到给玩家看的按键名，例如 <c>Space</c>、<c>左 Shift</c>、<c>手柄 A</c>。
        /// </summary>
        /// <remarks>
        /// ⚠ 按键提示**绝不要在 UI 里写死字符串**。改键功能一旦上线，
        /// 写死的「按 [空格] 释放绝技」就会永远停在空格上，而玩家早改成别的键了。
        /// 正确做法是运行时调本方法取值。
        /// </remarks>
        string GetDisplayString(InputMapType map, string actionName, int bindingIndex = 0);

        /// <summary>
        /// 进入改键监听，等玩家按下一个新键。
        /// </summary>
        /// <param name="map">要改的 Action 所在 Map。</param>
        /// <param name="actionName">要改的 Action 名。</param>
        /// <param name="bindingIndex">要改第几条绑定。</param>
        /// <param name="ct">取消令牌。外部取消（例如玩家关了设置面板）会走 Cancelled 结局。</param>
        /// <remarks>
        /// 监听期间所有非改键 Map 会被临时禁用，结束后自动恢复原上下文 ——
        /// 否则玩家在改键时按下的键会顺带触发游戏行为（把「改键」按成「放大招」）。
        /// </remarks>
        UniTask<InputRebindResult> RebindAsync(InputMapType map, string actionName,
            int bindingIndex, CancellationToken ct = default);

        /// <summary>把某一条绑定恢复成资产里的原始值。返回是否确实改了东西。</summary>
        bool ResetBinding(InputMapType map, string actionName, int bindingIndex);

        /// <summary>恢复所有绑定的原始值，并清空 override 记录。</summary>
        void ResetAllBindings();

        /// <summary>是否存在任何改键记录（UI 据此决定「恢复默认」按钮是否可点）。</summary>
        bool HasOverrides { get; }

        /// <summary>
        /// 导出改键记录为 JSON 字符串。
        /// </summary>
        /// <remarks>
        /// ⚠ 拿到之后请存进**存档系统**，不要图省事塞进 PlayerPrefs。
        /// PlayerPrefs 在多账号、云同步、存档回滚三种场景下都会变成麻烦。
        /// 本方法只管导出，落盘是存档层的职责。
        /// </remarks>
        string SaveOverrides();

        /// <summary>从 JSON 字符串恢复改键记录。传入 null 或空串等价于清空。</summary>
        void LoadOverrides(string json);

        /// <summary>某个受关注的 Action 被按下。参数是 Action 名（见 <see cref="InputActionNames"/>）。</summary>
        event Action<string> ActionPerformed;

        /// <summary>
        /// 某个受关注的 Action 被松开。
        /// 只对 Button 类 Action 有意义，用于「长按加速」这类需要知道松开时机的玩法。
        /// </summary>
        event Action<string> ActionCanceled;

        /// <summary>上下文发生了切换。</summary>
        event Action<InputContext> ContextChanged;
    }
}
