// ============================================================================
// 万相 · 战斗核心 · QTE（格挡 / 终结技）
//  ---------------------------------------------------------------------------
//  2026-10-14 用户立项（点名参考 33号远征队）：敌方"带伤害的行动"前弹时机圈，
//  按时点击 ⇒ 完美格挡（大减伤 + 反震）/ 普通格挡（小减伤）/ 未挡（全额）。
//  同日第二轮扩到两处：
//    · 多段连击：多段攻击**每段一个窗口**（要按 N 次键），逐段取最差档。
//    · 终结技 QTE：我方终结技释放前弹输出窗口，完美 ⇒ 加强、失败 ⇒ 减弱。
//
//  ★ 确定性红线：整个机制由 st.ParryEnabled 门控 —— **默认关闭**（自检 / 自动战斗 /
//    PvP / 一切无 UI 的跑法都不开）。关闭时：不 yield、零事件、零伤害修正，
//    事件流与指纹**逐位不变**（与元素反应链同一套"零副作用"纪律）。
//
//  ★ 时序模型：模拟层不感知真实时间 —— 时机圈时长/判定全在 UI（BattlePanel），
//    模拟层只接收三档结果（None/Normal/Perfect）。伤害折减在 DealDamage 收口
//    （见 BattleSimulator.Damage.cs 的 ParryDamageMulFor 调用点）。
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

    /// <summary>
    /// 这个 QTE 窗口是**哪一种**。同一个窗口机制被两处复用，避免两份实现各自跑偏。
    /// </summary>
    public enum ParryQteKind
    {
        None = 0,
        /// <summary>防守：敌方带伤害行动前的格挡窗口（多段攻击 = 连按 N 次）。</summary>
        Guard = 1,
        /// <summary>进攻：我方终结技释放前的输出窗口（成功加强 / 失败减弱）。</summary>
        Finisher = 2,
    }

    public static partial class BattleSimulator
    {
        /// <summary>完美格挡：受到的伤害 ×0.2（数值调参就改这三行）。</summary>
        public const float ParryPerfectDamageMul = 0.2f;
        /// <summary>普通格挡：受到的伤害 ×0.6。</summary>
        public const float ParryNormalDamageMul = 0.6f;
        /// <summary>完美格挡反震：攻击者受到其最大生命 4% 的真实伤害。</summary>
        public const float ParryCounterMaxHpPercent = 0.04f;

        /// <summary>终结技 QTE · 完美：本次终结技伤害 ×此值（加强）。</summary>
        public const float FinisherQtePerfectMul = 1.60f;
        /// <summary>终结技 QTE · 失败：本次终结技伤害 ×此值（减弱但不清零，仍能打出伤害）。</summary>
        public const float FinisherQteFailMul = 0.45f;

        /// <summary>单次行动最多开几次格挡窗口（多段连击上限，防手速党刷屏）。</summary>
        public const int ParryMaxComboHits = 4;

        /// <summary>
        /// 我方终结技 QTE 的段数（2026-10-15 用户："我方异兽也可以使用这个按键多段判定
        /// 而不是单单这个空格"）。
        ///
        /// 取该终结技**伤害原子的最大 Hits**，再夹到 [2, ParryMaxComboHits]：
        ///   · 下限 2：哪怕技能是单发，也要"按两个键" —— 用户要的就是多段手感，
        ///     只按一下的 QTE 与"空格连按"没有区别。
        ///   · 上限 4：与敌方连段同一上限，避免手速党刷屏。
        /// </summary>
        private static int FinisherSegmentCount(SkillDef sk)
        {
            int best = 1;
            if (sk != null && sk.Effects != null)
            {
                for (int i = 0; i < sk.Effects.Length; i++)
                {
                    var a = sk.Effects[i];
                    if (a.Kind != EffectAtomKind.Damage || a.Target == TargetSelector.Self) continue;
                    int h = a.Hits <= 1 ? 1 : a.Hits;
                    if (h > best) best = h;
                }
            }
            if (best < 2) best = 2;
            if (best > ParryMaxComboHits) best = ParryMaxComboHits;
            return best;
        }

        /// <summary>
        /// 提交格挡结果（UI 在时机圈判定/超时后调用）。
        /// 只写状态、不推进模拟 —— 由 Step() / BattlePlayback 让迭代器继续。
        /// </summary>
        public static void ApplyParry(BattleState st, ParryGrade grade)
        {
            if (st == null || st.ParryPendingUnit == null) return;
            st.ParriedGrade = grade;
            st.ParryActorId = st.ParryPendingUnit.RuntimeId;
            st.ParryQteKind = ParryQteKind.Guard;
            st.ParryPendingUnit = null;
        }

        /// <summary>
        /// 提交**终结技 QTE** 结果（我方出手者 = 待判定的我方单位）。
        /// </summary>
        public static void ApplyFinisherQte(BattleState st, BattleUnit actor, ParryGrade grade)
        {
            if (st == null || actor == null) return;
            st.ParriedGrade = grade;
            st.ParryActorId = actor.RuntimeId;
            st.ParryQteKind = ParryQteKind.Finisher;
            st.ParryPendingUnit = null;
        }

        /// <summary>
        /// 本次行动的伤害折减系数（DealDamage 收口用）。
        /// 不在 QTE 行动中（QteKind 为空 / ActorId 对不上 / 目标阵营不对）恒为 1
        /// ⇒ 零副作用，指纹红线。
        /// </summary>
        private static float ParryDamageMulFor(BattleState st, BattleUnit src, BattleUnit dst)
        {
            if (st.ParryActorId == null || src == null) return 1f;
            if (src.RuntimeId != st.ParryActorId) return 1f;      // 只作用"被判定的那次行动者"

            // ---- 终结技 QTE：我方**输出**的加强/减弱 ----
            if (st.ParryQteKind == ParryQteKind.Finisher)
            {
                if (src.Side != TeamSide.Player) return 1f;
                if (st.ParriedGrade == ParryGrade.Perfect) return FinisherQtePerfectMul;
                if (st.ParriedGrade == ParryGrade.None) return FinisherQteFailMul;
                return 1f;                                        // 普通 = 原样
            }

            // ---- 格挡：护我方 ----
            if (dst == null || dst.Side != TeamSide.Player) return 1f;
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
            var sk = PeekChosenSkill(st, u);
            if (sk == null || sk.Effects == null) return false;
            for (int i = 0; i < sk.Effects.Length; i++)
            {
                var a = sk.Effects[i];
                if (a.Kind == EffectAtomKind.Damage && a.Target != TargetSelector.Self) return true;
            }
            return false;
        }

        /// <summary>
        /// 偷看这次行动的**伤害段数**（多段连击用）。纯读，与 PeekActionIsDamaging 同源。
        /// 取所有伤害原子的最大 Hits —— 玩家要对最狠的那一下做出反应。
        /// </summary>
        private static int PeekActionHitCount(BattleState st, BattleUnit u)
        {
            var sk = PeekChosenSkill(st, u);
            if (sk == null || sk.Effects == null) return 1;
            int best = 1;
            for (int i = 0; i < sk.Effects.Length; i++)
            {
                var a = sk.Effects[i];
                if (a.Kind != EffectAtomKind.Damage || a.Target == TargetSelector.Self) continue;
                int h = a.Hits <= 1 ? 1 : a.Hits;
                if (h > best) best = h;
            }
            if (best > ParryMaxComboHits) best = ParryMaxComboHits;
            return best;
        }

        /// <summary>
        /// 偷看这次行动选中的技能（纯读）。⚠ 只允许对**敌方**单位调用 —— 我方分支会消费
        /// PendingCommand（那是真下令），在"偷看"里被触发就是作弊。
        /// </summary>
        private static SkillDef PeekChosenSkill(BattleState st, BattleUnit u)
        {
            _ = DecideSlot(st, u, out var chosen);
            return chosen;
        }

        /// <summary>
        /// 连击合并：把这一段的档位并进本次行动的最终档位，**取最差**。
        /// 为什么取最差而不是累乘：连段里漏按一段就是"挨了一下"，最差档最符合直觉，
        /// 也让 UI 只维护一个最终档位（不逐段回放伤害）。
        /// </summary>
        public static void AccumulateParryComboSegment(ref ParryGrade acc, ParryGrade seg)
        {
            if (seg < acc) acc = seg;
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
