// ============================================================================
//  万相 · 天时运行态
//  ---------------------------------------------------------------------------
//  挂在 BattleState 上的一小块状态：节气天时（基础）+ 天气技覆盖（限时）
//  + 逆天时反噬标记。**null = 本场无天时**，所有战斗结算点先判 null ——
//  这是"天时系统不影响无天时战斗指纹"的实现保证。
//
//  覆盖回归（GDD 3.4）：天气技覆盖持续 3 回合，之后**回归原天时**。
//  逆天时（GDD 3.4）：若覆盖引入的属性被当前**节气**属性相克
//  （例：冬季水节气里用祈晴召火），覆盖期间我方该属性单位受到额外 15% 伤害。
// ============================================================================

namespace WanXiang.Battle.Core
{
    public sealed class WeatherRuntime
    {
        public const int OverrideDurationTurns = 3;

        /// <summary>节气天时（本场的基础天时，可为 null = 无天时节点/被占位场景）。</summary>
        public WeatherDef Base;

        /// <summary>天气技覆盖（非空时 Active 返回它）。</summary>
        public WeatherDef Override;
        public int OverrideTurnsLeft;

        // ---- 逆天时反噬 ----
        public bool BacklashActive;
        public Element BacklashElement = Element.None;   // 我方该属性单位 +15% 承伤
        public const float BacklashExtraDamage = 0.15f;

        /// <summary>当前生效的天时。覆盖优先，过期回归基础。</summary>
        public WeatherDef Active => Override ?? Base;

        public WeatherRuntime(WeatherDef baseWeather)
        {
            Base = baseWeather;
        }

        /// <summary>使用天气技覆盖。逆天时判定在这里一次算清（数据决定，不掷骰）。</summary>
        public void ApplyOverride(WeatherSkillDef skill)
        {
            if (skill?.Brings == null) return;
            Override = skill.Brings;
            OverrideTurnsLeft = OverrideDurationTurns;

            BacklashActive = false;
            BacklashElement = Element.None;
            var season = Base;
            if (season != null && season.Element != Element.None
                && skill.Brings.Element != Element.None
                && ElementMatrix.Counters(season.Element, skill.Brings.Element))
            {
                // 节气克引入属性（水节气召火）⇒ 反噬成立
                BacklashActive = true;
                BacklashElement = skill.Brings.Element;
            }
        }

        /// <summary>回合末递减覆盖计时。归零清覆盖（下一回合起回归原天时）。</summary>
        public void TickOverride()
        {
            if (Override == null) return;
            OverrideTurnsLeft--;
            if (OverrideTurnsLeft <= 0)
            {
                Override = null;
                OverrideTurnsLeft = 0;
                BacklashActive = false;
                BacklashElement = Element.None;
            }
        }

        // ---- 战斗结算点用的查询（全部要求 Weather 非空才会走到这里） ----
        //  统一口径：覆盖期用覆盖天的值，平时用节气天时的值（Active 已封装）。

        /// <summary>全场伤害乘数（夏至 ×1.25；覆盖时用覆盖天的值）。</summary>
        public float DamageAllMultiplier => Active?.DamageAllMultiplier ?? 1f;

        /// <summary>禁疗是否生效（覆盖期的天时同样可以禁疗）。</summary>
        public bool HealBanned => Active?.BanHeal == true;

        /// <summary>某方的有效速度乘数（大雪 ×0.8、冬至 ×1.2、召风 ×1.15）。</summary>
        public float SpeedMulFor(TeamSide side)
            => side == TeamSide.Player ? (Active?.SpeedMulPlayer ?? 1f) : (Active?.SpeedMulEnemy ?? 1f);

        /// <summary>某方的技能 CD 推进乘数（小满我方 ×1.3）。</summary>
        public float CdAdvanceMulFor(TeamSide side)
            => side == TeamSide.Player ? (Active?.CdAdvanceMulPlayer ?? 1f) : (Active?.CdAdvanceMulEnemy ?? 1f);

        /// <summary>某方的暴击伤害加成（与单位自身 CritDamage 相加，非相乘）。</summary>
        public float CritDamageBonusFor(TeamSide side)
            => side == TeamSide.Player ? (Active?.CritDamageBonusPlayer ?? 0f) : (Active?.CritDamageBonusEnemy ?? 0f);

        /// <summary>
        /// 五行伤害乘数（大暑/祷雨/祈晴）。入参按 ResolveElement 的归属五行；
        /// None / 越界一律 ×1 —— 天气不该改变"无属性伤害"的行为。
        /// </summary>
        public float ElementDamageMul(Element el)
        {
            switch (el)
            {
                case Element.Wood: return Active?.WoodDamageMul ?? 1f;
                case Element.Fire: return Active?.FireDamageMul ?? 1f;
                case Element.Earth: return Active?.EarthDamageMul ?? 1f;
                case Element.Metal: return Active?.MetalDamageMul ?? 1f;
                case Element.Water: return Active?.WaterDamageMul ?? 1f;
                default: return 1f;
            }
        }

        /// <summary>技能形态伤害乘数（秋分）。直接暴露原值，供结算点判"是否有人改过"。</summary>
        public float AoeDamageMul => Active?.AoeDamageMul ?? 1f;
        public float SingleDamageMul => Active?.SingleDamageMul ?? 1f;

        /// <summary>
        /// 按技能形态取伤害乘数（秋分：AOE -40%、单体 +25%）。
        /// 多段随机（RandomEnemyMultiHit）按**单体**算：每段重新抽一个目标，
        /// 手感上是"连打一人一路"，与"一锅端"的 AOE 不是一类。
        /// </summary>
        public float FormDamageMul(bool aoe)
            => aoe ? AoeDamageMul : SingleDamageMul;

        /// <summary>火属性单位受到的持续伤害乘数（大暑灼烧减半）。</summary>
        public float FireUnitDotTakenMul => Active?.FireUnitDotTakenMul ?? 1f;

        /// <summary>护盾获取乘数（小雪 ×1.5）。</summary>
        public float ShieldGainMul => Active?.ShieldGainMul ?? 1f;

        /// <summary>治疗溢出转护盾比例（雨水 0.5）。</summary>
        public float HealOverflowShieldRatio => Active?.HealOverflowShieldRatio ?? 0f;

        // ---- 事件钩子型查询（GDD 3.3 剩余 8 条） ----
        //  统一口径：Active 为空时全部落到"关闭/中性"，调用方不必再判空。

        /// <summary>立夏：我方攻击附带灼烧（返回 0 表示不触发）。</summary>
        public float AttackBurnPowerFor(TeamSide attackerSide)
            => attackerSide == TeamSide.Player && Active?.AttackBurnOn == true
               ? (Active?.AttackBurnPower ?? 0f) : 0f;

        public int AttackBurnTurns => Active?.AttackBurnTurns ?? 0;

        /// <summary>芒种：我方暴击追击强度（0 = 不触发）。</summary>
        public float PursuitPowerFor(TeamSide attackerSide)
            => attackerSide == TeamSide.Player && Active?.PursuitOnCrit == true
               ? (Active?.PursuitPower ?? 0f) : 0f;

        /// <summary>处暑：击杀溢出转护盾比例（0 = 不触发）。</summary>
        public float KillOverflowShieldRatio => KillOverflowShieldOn ? (Active?.KillOverflowShieldRatio ?? 0f) : 0f;
        public bool KillOverflowShieldOn => Active?.KillOverflowShield == true;

        /// <summary>惊蛰：阵亡是否留卵（只看我方 —— GDD 写"我方单位阵亡后"）。</summary>
        public bool ReviveEggFor(TeamSide side)
            => side == TeamSide.Player && Active?.ReviveEggOn == true;

        public int ReviveEggDelayTurns => Active?.ReviveEggDelayTurns ?? 0;
        public float ReviveEggHpPercent => Active?.ReviveEggHpPercent ?? 0f;

        /// <summary>寒露：本回合是否触发凝神（0 = 关闭）。</summary>
        public int HasteEveryNTurns => Active?.HasteEveryNTurns ?? 0;
        public int HasteCdReduction => Active?.HasteCdReduction ?? 0;

        /// <summary>立冬：受击冻结概率（全场；0 = 不触发）。</summary>
        public float FreezeOnHitChance => Active?.FreezeOnHitChance ?? 0f;
        public int FreezeOnHitTurns => Active?.FreezeOnHitTurns ?? 0;

        /// <summary>清明：是否压制减益时长 / 免疫混乱沉默（只看我方）。</summary>
        public bool DebuffDurationMinusOneFor(TeamSide side)
            => side == TeamSide.Player && Active?.DebuffDurationMinusOne == true;

        public bool ImmuneConfuseSilenceFor(TeamSide side)
            => side == TeamSide.Player && Active?.ImmuneConfuseSilence == true;

        /// <summary>
        /// 先手连击门槛（大雪把它降到 1.20；其余天时用 BattleConfig 默认 1.50）。
        /// 无天时时返回默认值 —— 调用方不必判空。
        /// </summary>
        public float InitiativeRatioOrDefault(float fallback)
            => Active != null && Active.InitiativeRatioOverride > 0f
               ? Active.InitiativeRatioOverride : fallback;

        /// <summary>小寒：回合末是否给速度最高者一次额外普攻。</summary>
        public bool ExtraBasicAttackOnTurnEnd => Active?.ExtraBasicAttackOnTurnEnd == true;

        /// <summary>
        /// 冬至首回合先手方伤害乘数：攻击方是第 1 回合出手序列里的先手阵营才生效。
        /// "谁先动"由速度决定，而速度本身可能被本场天时改过，
        /// 所以必须等序列真正生成后再判定（BattleState.FirstMoverSide），不能装配期拍脑袋。
        /// </summary>
        public float FirstTurnDamageMulFor(BattleState st, TeamSide attackerSide)
        {
            if (st.Turn != 1) return 1f;
            var a = Active;
            if (a == null || a.FirstTurnDamageMul == 1f) return 1f;
            return st.FirstMoverSide == attackerSide ? a.FirstTurnDamageMul : 1f;
        }
    }
}
