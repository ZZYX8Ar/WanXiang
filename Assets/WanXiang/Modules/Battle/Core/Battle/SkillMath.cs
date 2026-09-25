// ============================================================================
//  万相 · 技能「具体点数」展示口径
//  ---------------------------------------------------------------------------
//  背景（用户 2026-09-25 反馈）：技能描述原来全是百分比（"造成 69% 攻击伤害"），
//  玩家看不懂、也没法心算 → 要求在面板上**直接写明白会造成多少点伤害 / 多少护盾 / 回多少血**。
//
//  ⚠ 这里是「把效果换算成具体点数」的**唯一实现** ——
//    「异兽培养」面板（战前预览，用该兽当前等级属性）与
//    「战斗」悬浮提示（用该单位**实时**属性，含增益/减益）都调它。
//    以前两处各写一份文案，必然漂移（踩过）。
//
//  数值口径与战斗结算**一一对应**：
//    · 伤害基础值  = Attack × Power                ← BattleSimulator.ComputeDamage 里的
//                                                   `float raw = src.Attack * power * (...)`
//    · 治疗/护盾   = Round((Attack × Power + 目标最大生命 × PercentOfMaxHp) × HealShieldMultiplier)
//                                                   ← BattleSimulator 的 Heal / Shield 分支
//
//  ⚠ 战前预览算不出**最终**伤害：实战还要乘
//     五行克制系数（ElementMatrix.Coefficient）、暴击、
//     目标的减伤 `1 - def/(def+DefenseConstant)`，再加天时修正。
//    所以伤害刻意写成「**基础伤害**」并附 DamageNote 说明（否则玩家会以为面板骗人）。
//  ⚠ 改战斗公式时，这里要跟着改（注释里已标注来源行）。
// ============================================================================

namespace WanXiang.Battle.Core
{
    public static class SkillMath
    {
        /// <summary>伤害文案的免责标注 —— 两个面板共用同一句话，别各写一份。</summary>
        public const string DamageNote = "基础值·未计敌方防御与五行克制";

        /// <summary>伤害基础值（未计五行克制 / 暴击 / 敌方减伤 / 天时）。</summary>
        public static int BaseDamage(int attack, float power)
            => CoreMath.RoundDamage(attack * power);

        /// <summary>治疗/护盾点数（战斗外的 HealShieldMultiplier 视为 1）。
        /// 目标最大生命用于 PercentOfMaxHp 型效果；面板上按**自身**最大生命估算。</summary>
        public static int HealShield(int attack, float power, int targetMaxHp, float pctOfMaxHp,
                                     float healShieldMul = 1f)
            => CoreMath.RoundDamage((attack * power + targetMaxHp * pctOfMaxHp) * healShieldMul);

        /// <summary>该技能是否含伤害效果（决定要不要附 DamageNote）。</summary>
        public static bool HasDamage(SkillDef sk)
        {
            if (sk == null || sk.Effects == null) return false;
            for (int i = 0; i < sk.Effects.Length; i++)
                if (sk.Effects[i].Kind == EffectAtomKind.Damage) return true;
            return false;
        }

        /// <summary>
        /// 把技能效果换算成"具体点数"文案；**算不出点数的效果不硬凑**
        /// （状态/驱散/属性增减这类仍由内容原文描述）。
        /// </summary>
        /// <param name="attack">施法者攻击（战斗中用实时值；面板用该兽当前等级算出的值）</param>
        /// <param name="maxHp">施法者最大生命（PercentOfMaxHp 型效果按自身估算）</param>
        /// <param name="withTargetPhrase">是否把目标短语接在每条后面
        ///（战斗面板会另起一行写目标，传 false）</param>
        /// <returns>true = 至少有一条效果换算出点数，text 可用；false = 调用方退回内容原文</returns>
        public static bool TryDescribe(SkillDef sk, int attack, int maxHp, bool withTargetPhrase,
                                       out string text)
        {
            text = null;
            if (sk == null || sk.Effects == null || sk.Effects.Length == 0) return false;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < sk.Effects.Length; i++)
            {
                var a = sk.Effects[i];
                int hits = CoreMath.Max(1, a.Hits);
                string who = withTargetPhrase ? TargetCn(a.Target) : "";
                string part;

                switch (a.Kind)
                {
                    case EffectAtomKind.Damage:
                        part = "造成 " + BaseDamage(attack, a.Power) + " 点伤害"
                             + (a.TrueDamage ? "（真实伤害·无视护盾）" : "")
                             + (hits > 1 ? "×" + hits + " 段" : "") + who;
                        break;
                    case EffectAtomKind.Heal:
                        part = "回复 " + HealShield(attack, a.Power, maxHp, a.PercentOfMaxHp) + " 点生命" + who;
                        break;
                    case EffectAtomKind.Shield:
                        part = "获得 " + HealShield(attack, a.Power, maxHp, a.PercentOfMaxHp) + " 点护盾" + who;
                        break;
                    default:
                        part = null;    // 状态/驱散/属性增减：不硬凑，留给内容原文
                        break;
                }

                if (part == null) continue;
                if (sb.Length > 0) sb.Append("；");
                sb.Append(part);
            }

            if (sb.Length == 0) return false;
            text = sb.ToString();
            return true;
        }

        /// <summary>目标选择器的中文短语（两个面板共用）。</summary>
        public static string TargetCn(TargetSelector t)
        {
            switch (t)
            {
                case TargetSelector.Self: return "（自身）";
                case TargetSelector.SingleLowestHp: return "（生命最低者）";
                case TargetSelector.SingleHighestHp: return "（生命最高者）";
                case TargetSelector.SingleHighestAtk: return "（攻击最高者）";
                case TargetSelector.AllEnemies: return "（全体敌方）";
                case TargetSelector.AllAllies: return "（全体友方）";
                case TargetSelector.RandomEnemy: return "（随机敌方）";
                case TargetSelector.RandomEnemyMultiHit: return "（随机敌方·多段）";
                case TargetSelector.AdjacentToSelf: return "（自身相邻）";
                case TargetSelector.AllOthers: return "（除自己外全场）";
                case TargetSelector.SingleFrontMost: return "（最前排）";
                case TargetSelector.SingleBackMost: return "（最后排）";
                default: return "";
            }
        }
    }
}
