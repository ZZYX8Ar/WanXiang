// ============================================================================
//  万相 · 战斗核心 · 格挡（QTE）
//  ---------------------------------------------------------------------------
//  2026-10-14 用户立项（点名参考 33号远征队）：敌方"带伤害的行动"前弹一个时机圈，
//  按时点击 ⇒ 完美格挡（大减伤 + 反震）/ 普通格挡（小减伤）/ 未挡（全额）。
//
//  ★ 确定性红线：整个机制由 st.ParryEnabled 门控 —— **默认关闭**（自检 / 自动战斗 /
//    PvP / 一切无 UI 的跑法都不开）。关闭时：不 yield、零事件、零伤害修正，
//    事件流与指纹**逐位不变**（与元素反应链同一套"零副作用"纪律）。
//
//  ★ 时序模型：模拟层不感知真实时间 —— 时机圈时长/判定全在 UI（BattlePanel），
//    模拟层只接收三档结果（None/Normal/Perfect）。伤害折减在 DealDamage 收口
//    （见 BattleSimulator.Damage.cs 的 ParryDamageMulFor 调用点）。
//
//  窗口粒度：**每次行动一个窗口**（AOE 命中多个我方目标也只弹一次，档位覆盖该次
//  行动对我方的全部伤害）。额外行动（溟鲲「深潜」等）暂不开窗（MVP）。
// ============================================================================
using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    /// <summary>格挡判定档位。</summary>
    public enum ParryGrade
    {
        None = 0,      // 未挡（没按 / 按歪 / 超时）
        Normal = 1,    // 格挡
        Perfect = 2,   // 完美格挡
    }

    public static partial class BattleSimulator
    {
        /// <summary>完美格挡：受到的伤害 ×0.2（数值调参就改这三行）。</summary>
        public const float ParryPerfectDamageMul = 0.2f;
        /// <summary>普通格挡：受到的伤害 ×0.6。</summary>
        public const float ParryNormalDamageMul = 0.6f;
        /// <summary>完美格挡反震：攻击者受到其最大生命 4% 的真实伤害。</summary>
        public const float ParryCounterMaxHpPercent = 0.04f;

        /// <summary>
        /// 提交格挡结果（UI 在时机圈判定/超时后调用）。
        /// 只写状态、不推进模拟 —— 由 Step() / BattlePlayback 让迭代器继续。
        /// </summary>
        public static void ApplyParry(BattleState st, ParryGrade grade)
        {
            if (st == null || st.ParryPendingUnit == null) return;
            st.ParriedGrade = grade;
            st.ParryActorId = st.ParryPendingUnit.RuntimeId;
            st.ParryPendingUnit = null;
        }

        /// <summary>
        /// 本次敌方行动的伤害折减系数（DealDamage 收口用）。
        /// 不在格挡行动中（ParryActorId 为空/不是同一行动者/目标不是我方）恒为 1
        /// ⇒ 零副作用，指纹红线。
        /// </summary>
        private static float ParryDamageMulFor(BattleState st, BattleUnit src, BattleUnit dst)
        {
            if (st.ParryActorId == null || src == null || dst == null) return 1f;
            if (dst.Side != TeamSide.Player) return 1f;               // 只护我方
            if (src.RuntimeId != st.ParryActorId) return 1f;          // 只折"被格挡的那次行动"
            if (st.ParriedGrade == ParryGrade.Perfect) return ParryPerfectDamageMul;
            if (st.ParriedGrade == ParryGrade.Normal) return ParryNormalDamageMul;
            return 1f;
        }

        /// <summary>
        /// 偷看这次行动会不会打人（用于决定要不要开格挡窗口）。**纯读，不改状态**：
        /// 只允许对**敌方**单位调用（敌方 DecideSlot 走 AI 分支不消费任何状态；
        /// 我方分支会消费 PendingCommand，绝不能在这里被触发）。
        /// </summary>
        private static bool PeekActionIsDamaging(BattleState st, BattleUnit u)
        {
            _ = DecideSlot(st, u, out var chosen);
            if (chosen == null || chosen.Effects == null) return false;
            for (int i = 0; i < chosen.Effects.Length; i++)
            {
                var a = chosen.Effects[i];
                if (a.Kind == EffectAtomKind.Damage && a.Target != TargetSelector.Self) return true;
            }
            return false;
        }

        /// <summary>格挡结果的中文名（事件 Note 用）。</summary>
        private static string GradeCn(ParryGrade g)
        {
            if (g == ParryGrade.Perfect) return "完美格挡";
            if (g == ParryGrade.Normal) return "格挡";
            return "未挡";
        }
    }
}
