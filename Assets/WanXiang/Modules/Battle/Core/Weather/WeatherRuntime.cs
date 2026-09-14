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

        /// <summary>全场伤害乘数（夏至 ×1.25；覆盖时用覆盖天的值）。</summary>
        public float DamageAllMultiplier => Active?.DamageAllMultiplier ?? 1f;

        /// <summary>禁疗是否生效（覆盖期的天时同样可以禁疗）。</summary>
        public bool HealBanned => Active?.BanHeal == true;
    }
}
