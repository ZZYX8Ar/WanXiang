// ============================================================================
//  万相 · 改键结果
//  ---------------------------------------------------------------------------
//  改键是一个「可能以多种方式失败」的操作，所以返回值不能是 bool。
//  玩家可能中途按 Esc 取消、可能拔了手柄导致超时、也可能改出来的键
//  跟别的功能撞了 —— 这三种情况的 UI 表现完全不同：
//    · 取消   → 静默恢复，不要弹红字吓人
//    · 超时   → 提示「没检测到输入，已取消」
//    · 冲突   → 提示「该键已用于【释放绝技】，仍要覆盖吗？」
//  所以这里把所有结果都建模出来。
// ============================================================================

using System;

namespace WanXiang.Framework.Inputs
{
    /// <summary>改键的结局。</summary>
    public enum RebindOutcome
    {
        /// <summary>改键成功，新绑定已写入并已持久化。</summary>
        Succeeded = 0,

        /// <summary>玩家主动取消（按 Esc，或调用了取消）。不算错误。</summary>
        Cancelled = 1,

        /// <summary>等待超时，没等到任何有效输入。</summary>
        TimedOut = 2,

        /// <summary>失败（未知错误，或正处于另一个改键流程中）。看 <c>ErrorMessage</c>。</summary>
        Failed = 3,
    }

    /// <summary>
    /// 一次改键操作的结果。
    /// </summary>
    /// <remarks>
    /// 刻意做成 <c>readonly struct</c>：改键调用频率极低（玩家一辈子改不了几次），
    /// 但结构体避免了堆分配，且值语义让「把结果存起来慢慢看」变得安全。
    /// </remarks>
    public readonly struct InputRebindResult
    {
        /// <summary>结局。</summary>
        public readonly RebindOutcome Outcome;

        /// <summary>被改的 Action 名。</summary>
        public readonly string ActionName;

        /// <summary>被改的 Binding 下标（同一个 Action 可以有多条绑定，例如键鼠一条、手柄一条）。</summary>
        public readonly int BindingIndex;

        /// <summary>改之前的绑定路径，例如 <c>&lt;Keyboard&gt;/space</c>。失败时可能为空。</summary>
        public readonly string PreviousPath;

        /// <summary>改之后的绑定路径。仅 <see cref="RebindOutcome.Succeeded"/> 时有值。</summary>
        public readonly string NewPath;

        /// <summary>
        /// 冲突信息。没有冲突时 <see cref="InputBindingConflict.HasConflict"/> 为 false。
        /// </summary>
        /// <remarks>
        /// ⚠ 有冲突**不代表改键失败** —— 绑定已经写进去了。
        /// 是否回滚由调用方决定：QFramework 风格的做法是弹窗问玩家
        /// 「该键已用于【X】，仍要覆盖吗？」，由玩家拍板。
        /// 框架不替玩家做这个决定，因为有些玩家就是故意要一键两用。
        /// </remarks>
        public readonly InputBindingConflict Conflict;

        /// <summary>仅 <see cref="RebindOutcome.Failed"/> 时有值。</summary>
        public readonly string ErrorMessage;

        public InputRebindResult(RebindOutcome outcome, string actionName, int bindingIndex,
            string previousPath = null, string newPath = null,
            InputBindingConflict conflict = default, string errorMessage = null)
        {
            Outcome = outcome;
            ActionName = actionName;
            BindingIndex = bindingIndex;
            PreviousPath = previousPath;
            NewPath = newPath;
            Conflict = conflict;
            ErrorMessage = errorMessage;
        }

        /// <summary>是否改成功。</summary>
        public bool Succeeded => Outcome == RebindOutcome.Succeeded;

        /// <summary>是否检测到与其他功能撞键。</summary>
        public bool HasConflict => Conflict.HasConflict;

        public override string ToString()
        {
            switch (Outcome)
            {
                case RebindOutcome.Succeeded:
                    return "改键成功：" + ActionName + " → " + NewPath +
                           (HasConflict ? "（⚠ 与 " + Conflict.Describe() + " 冲突）" : "");

                case RebindOutcome.Cancelled:
                    return "改键取消：" + ActionName;

                case RebindOutcome.TimedOut:
                    return "改键超时：" + ActionName + "（未检测到输入）";

                default:
                    return "改键失败：" + ActionName + " —— " + ErrorMessage;
            }
        }
    }

    /// <summary>
    /// 一处按键冲突。指「同一个按键在别处也被用了」。
    /// </summary>
    /// <remarks>
    /// 冲突检测的范围是**整份资产的全部 Map**，不只当前 Map。
    /// 只查当前 Map 是常见错误：玩家把「确认」和「取消」绑到同一个键，
    /// 而这两个键分属不同 Map，只查当前 Map 就漏掉了。
    /// </remarks>
    public readonly struct InputBindingConflict
    {
        /// <summary>冲突所在的 Map 名。</summary>
        public readonly string MapName;

        /// <summary>冲突所在的 Action 名。</summary>
        public readonly string ActionName;

        /// <summary>冲突所在的 Binding 下标。</summary>
        public readonly int BindingIndex;

        /// <summary>冲突的绑定路径。</summary>
        public readonly string Path;

        public InputBindingConflict(string mapName, string actionName, int bindingIndex, string path)
        {
            MapName = mapName;
            ActionName = actionName;
            BindingIndex = bindingIndex;
            Path = path;
        }

        /// <summary>是否存在冲突。</summary>
        public bool HasConflict => !string.IsNullOrEmpty(ActionName);

        /// <summary>给玩家看的描述，例如 <c>Gameplay/释放绝技</c>。</summary>
        public string Describe() => MapName + "/" + ActionName;

        public override string ToString() => Describe() + "  [" + Path + "]";
    }
}
