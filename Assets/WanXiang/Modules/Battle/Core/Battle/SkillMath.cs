// ============================================================================
//  万相 · 技能「具体点数」展示口径
//  ---------------------------------------------------------------------------
//  背景（用户 2026-09-25 反馈）：技能描述原来全是百分比（"造成 69% 攻击伤害"），
//  玩家看不懂、也没法心算 → 要求在面板上**直接写明白会造成多少点伤害 / 多少护盾 / 回多少血**。
//
//  本类只做**面板用的战前预览**换算，数值口径与战斗结算**一一对应**：
//    · 伤害基础值  = Attack × Power                ← BattleSimulator.ComputeDamage 里的
//                                                   `float raw = src.Attack * power * (...)`
//    · 治疗/护盾   = Round((Attack × Power + 目标最大生命 × PercentOfMaxHp) × HealShieldMultiplier)
//                                                   ← BattleSimulator 的 Heal / Shield 分支
//
//  ⚠ 战前预览算不出**最终**伤害：实战还要乘
//     五行克制系数（ElementMatrix.Coefficient）、暴击、以及
//     目标的减伤 `1 - def/(def+DefenseConstant)`、再加天时修正。
//    所以面板上伤害刻意写成「**基础伤害**」，并注明"未计敌方防御与五行克制"。
//  ⚠ 改战斗公式时，这里要跟着改（注释里已标注来源行）。
// ============================================================================

namespace WanXiang.Battle.Core
{
    public static class SkillMath
    {
        /// <summary>伤害基础值（未计五行克制 / 暴击 / 敌方减伤 / 天时）。</summary>
        public static int BaseDamage(int attack, float power)
            => CoreMath.RoundDamage(attack * power);

        /// <summary>治疗/护盾点数（战斗外的 HealShieldMultiplier 视为 1）。
        /// 目标最大生命用于 PercentOfMaxHp 型效果；面板上按**自身**最大生命估算。</summary>
        public static int HealShield(int attack, float power, int targetMaxHp, float pctOfMaxHp,
                                     float healShieldMul = 1f)
            => CoreMath.RoundDamage((attack * power + targetMaxHp * pctOfMaxHp) * healShieldMul);
    }
}
