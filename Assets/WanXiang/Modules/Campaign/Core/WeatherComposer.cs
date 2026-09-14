// ============================================================================
//  万相 · 天时合成（GDD 3.2 规则一「余气叠加」的落地）
//  ---------------------------------------------------------------------------
//  进入新一幕时，上一季的天时以「余气」残留 2 个节点（强度减半），与本季
//  节点天时**叠加**。战斗结算点只认一个 WeatherDef，所以叠加在**进节点时
//  一次合成**：节点天时为主、余气为辅，产出一份普通 WeatherDef 交给
//  BattleFactory —— 战斗核心零改动（指纹红线天然成立：无余气时合成结果
//  就是节点天时本身）。
//
//  合成规则（全部记录在案）：
//    原子效果（TurnStart/TurnEnd）—— 拼接，节点在前、余气按获得顺序在后；
//      余气的原子在获得时已过 ScaledHalf（数值减半），这里不再折算。
//    乘数类（伤害/速度/CD/形态/五行/先手/火DoT/护盾）—— 连乘。
//    加成类（暴伤）—— 相加。
//    离散类（禁疗）—— 取或（余气的禁疗在 ScaledHalf 里已被剥掉，这里防御性兜底）。
//    治疗溢出转护盾比例 —— 取大者（两份"溢出转化率"取更强的那份，不用加法：
//      50%+25%=75% 会让叠层变成免费变强，取大更接近"残留的是旧季的规则"）。
//    元素/名字 —— 全取节点的：逆天时判定、日志文案都以**当前节气**为准；
//      余气效果的事件会以节点天时的名字记账（见 WeatherResolver），
//      这是已记录的取舍 —— 想要来源可辨时再给 WeatherEffect 加来源字段。
// ============================================================================

using System.Collections.Generic;

namespace WanXiang.Campaign
{
    public static class WeatherComposer
    {
        /// <summary>
        /// 合成一份进战斗用的天时。<paramref name="node"/> 与 <paramref name="lingers"/>
        /// 全为 null 时返回 null（本场无天时）。余气必须**已经**是减半后的版本。
        /// </summary>
        public static WanXiang.Battle.Core.WeatherDef Compose(
            WanXiang.Battle.Core.WeatherDef node, IReadOnlyList<WanXiang.Battle.Core.WeatherDef> lingers)
        {
            if (node == null && (lingers == null || lingers.Count == 0)) return null;

            var result = new WanXiang.Battle.Core.WeatherDef
            {
                Id = BuildId(node, lingers),
                NodeName = node?.NodeName ?? "余气",
                BuffName = node?.BuffName ?? "余气",
                Element = node?.Element ?? WanXiang.Battle.Core.Element.None,
            };

            // ---- 原子拼接：节点在前，余气按获得顺序在后 ----
            var turnStart = new List<WanXiang.Battle.Core.WeatherEffect>();
            var turnEnd = new List<WanXiang.Battle.Core.WeatherEffect>();
            if (node?.TurnStart != null) turnStart.AddRange(node.TurnStart);
            if (node?.TurnEnd != null) turnEnd.AddRange(node.TurnEnd);
            if (lingers != null)
            {
                for (int i = 0; i < lingers.Count; i++)
                {
                    var l = lingers[i];
                    if (l?.TurnStart != null) turnStart.AddRange(l.TurnStart);
                    if (l?.TurnEnd != null) turnEnd.AddRange(l.TurnEnd);
                }
            }
            if (turnStart.Count > 0) result.TurnStart = turnStart.ToArray();
            if (turnEnd.Count > 0) result.TurnEnd = turnEnd.ToArray();

            // ---- 乘数连乘 / 加成相加 ----
            float damageAll = 1f, spP = 1f, spE = 1f, cdP = 1f, cdE = 1f;
            float aoe = 1f, single = 1f, firstTurn = 1f, fireDot = 1f, shieldGain = 1f;
            float wood = 1f, fire = 1f, earth = 1f, metal = 1f, water = 1f;
            float critP = 0f, critE = 0f;
            float overflow = 0f;
            bool banHeal = false;

            Apply(node, ref damageAll, ref spP, ref spE, ref cdP, ref cdE, ref aoe, ref single,
                  ref firstTurn, ref fireDot, ref shieldGain, ref wood, ref fire, ref earth,
                  ref metal, ref water, ref critP, ref critE, ref overflow, ref banHeal);
            if (lingers != null)
                for (int i = 0; i < lingers.Count; i++)
                    Apply(lingers[i], ref damageAll, ref spP, ref spE, ref cdP, ref cdE, ref aoe,
                          ref single, ref firstTurn, ref fireDot, ref shieldGain, ref wood,
                          ref fire, ref earth, ref metal, ref water, ref critP, ref critE,
                          ref overflow, ref banHeal);

            result.DamageAllMultiplier = damageAll;
            result.SpeedMulPlayer = spP;
            result.SpeedMulEnemy = spE;
            result.CdAdvanceMulPlayer = cdP;
            result.CdAdvanceMulEnemy = cdE;
            result.AoeDamageMul = aoe;
            result.SingleDamageMul = single;
            result.FirstTurnDamageMul = firstTurn;
            result.FireUnitDotTakenMul = fireDot;
            result.ShieldGainMul = shieldGain;
            result.WoodDamageMul = wood;
            result.FireDamageMul = fire;
            result.EarthDamageMul = earth;
            result.MetalDamageMul = metal;
            result.WaterDamageMul = water;
            result.CritDamageBonusPlayer = critP;
            result.CritDamageBonusEnemy = critE;
            result.HealOverflowShieldRatio = overflow;
            result.BanHeal = banHeal;
            return result;
        }

        /// <summary>
        /// 合成 id：`<节点 id>` 或 `<节点 id>+<余气 id>+…`。
        /// 记账/窗口/自检都靠它一眼看出"这场天时里混了谁的余气"——
        /// 只用节点 id 的话，余气在场与不在场的记录长得一模一样。
        /// </summary>
        private static string BuildId(WanXiang.Battle.Core.WeatherDef node,
                                      IReadOnlyList<WanXiang.Battle.Core.WeatherDef> lingers)
        {
            var sb = new System.Text.StringBuilder(node?.Id ?? "linger_only");
            if (lingers != null)
                for (int i = 0; i < lingers.Count; i++)
                    if (lingers[i] != null) sb.Append('+').Append(lingers[i].Id);
            return sb.ToString();
        }

        private static void Apply(WanXiang.Battle.Core.WeatherDef w,
            ref float damageAll, ref float spP, ref float spE, ref float cdP, ref float cdE,
            ref float aoe, ref float single, ref float firstTurn, ref float fireDot,
            ref float shieldGain, ref float wood, ref float fire, ref float earth,
            ref float metal, ref float water, ref float critP, ref float critE,
            ref float overflow, ref bool banHeal)
        {
            if (w == null) return;
            damageAll *= w.DamageAllMultiplier;
            spP *= w.SpeedMulPlayer;
            spE *= w.SpeedMulEnemy;
            cdP *= w.CdAdvanceMulPlayer;
            cdE *= w.CdAdvanceMulEnemy;
            aoe *= w.AoeDamageMul;
            single *= w.SingleDamageMul;
            firstTurn *= w.FirstTurnDamageMul;
            fireDot *= w.FireUnitDotTakenMul;
            shieldGain *= w.ShieldGainMul;
            wood *= w.WoodDamageMul;
            fire *= w.FireDamageMul;
            earth *= w.EarthDamageMul;
            metal *= w.MetalDamageMul;
            water *= w.WaterDamageMul;
            critP += w.CritDamageBonusPlayer;
            critE += w.CritDamageBonusEnemy;
            overflow = System.Math.Max(overflow, w.HealOverflowShieldRatio);
            banHeal |= w.BanHeal;
        }
    }
}
