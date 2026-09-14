// ============================================================================
//  万相 · 天时结算
//  ---------------------------------------------------------------------------
//  在 BattleSimulator 的回合开始 / 回合末各有一个挂点。**st.Weather == null
//  时立即返回**，保证无天时战斗与旧版逐位一致（可复现性红线）。
//
//  为什么不复用 BattleSimulator.ResolveAtom：那是"技能原子"结算路径，
//  处处依赖施法者（攻击力、命中暴击、怒气）。天时是**无施法者的场地效果**，
//  数值只按目标最大生命 % 与状态叠加走 —— 单独一个 60 行的结算器，
//  换来技能路径零改动（指纹回归零风险），划算。
// ============================================================================

using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    public static class WeatherResolver
    {
        /// <summary>回合开始挂点（BattleSimulator 在 TickStatuses 之前调用）。</summary>
        public static void ResolveTurnStart(BattleState st)
        {
            Resolve(st, atTurnEnd: false);
        }

        /// <summary>回合末挂点（BattleSimulator 在 EndOfTurn 之后调用；顺带推覆盖计时）。</summary>
        public static void ResolveTurnEnd(BattleState st)
        {
            Resolve(st, atTurnEnd: true);
            st.Weather?.TickOverride();
        }

        private static void Resolve(BattleState st, bool atTurnEnd)
        {
            var weather = st.Weather?.Active;
            if (weather == null) return;

            var list = atTurnEnd ? weather.TurnEnd : weather.TurnStart;
            if (list == null) return;

            for (int i = 0; i < list.Length; i++)
            {
                if (list[i].Once && st.Turn != 1) continue;   // "战斗开始时"只在第 1 回合
                if (list[i].MinTurn > 0 && st.Turn < list[i].MinTurn) continue;   // 春分"超 12 回合翻倍"的第二笔
                ResolveEffect(st, weather, list[i]);
            }
        }

        private static void ResolveEffect(BattleState st, WeatherDef weather, in WeatherEffect we)
        {
            var targets = CollectTargets(st, we.Scope, we.Pick);
            if (targets.Count == 0) return;

            ref readonly var atom = ref we.Atom;

            switch (atom.Kind)
            {
                case EffectAtomKind.Heal:
                {
                    if (st.HealBanned) break;   // 小雪：禁疗期跳过回复（含天时自己的回复）
                    float overflowRatio = st.Weather.HealOverflowShieldRatio;   // 雨水：溢出转护盾
                    for (int t = 0; t < targets.Count; t++)
                    {
                        var u = targets[t];
                        if (!u.CanBeHealed) continue;
                        int amount = CoreMath.RoundDamage(u.MaxHp * atom.PercentOfMaxHp);
                        int healed = u.Heal(amount);
                        if (healed <= 0)
                        {
                            // 满血时溢出最多：healed=0 不代表没有溢出 —— 全额都转护盾。
                            if (overflowRatio > 0f && amount > 0) GrantOverflowShield(st, weather, u, amount, overflowRatio);
                            continue;
                        }
                        st.Log.Add(st.Turn, BattleEventKind.Heal, actorId: u.RuntimeId,
                                   targetId: u.RuntimeId, amount: healed, element: u.Element,
                                   note: $"天时·{weather.BuffName}");
                        // 溢出部分 = 给出的量 - 实际回上的量
                        if (overflowRatio > 0f && amount > healed)
                            GrantOverflowShield(st, weather, u, amount - healed, overflowRatio);
                    }
                    break;
                }

                case EffectAtomKind.ApplyStatus:
                {
                    for (int t = 0; t < targets.Count; t++)
                    {
                        var u = targets[t];
                        if (!u.IsAlive) continue;

                        // 天时无施法者：持续伤害只按目标最大生命 % 折算（施加瞬间定格）。
                        float dot = atom.PercentOfMaxHp > 0f ? u.MaxHp * atom.PercentOfMaxHp : 0f;
                        u.ApplyStatus(atom.StatusId, atom.StatusStacks, atom.StatusTurns, dot);
                        var def = StatusCatalog.Get(atom.StatusId);
                        st.Log.Add(st.Turn, BattleEventKind.StatusApplied, targetId: u.RuntimeId,
                                   amount: atom.StatusStacks,
                                   note: $"天时·{weather.BuffName}｜{def.Name} ×{u.GetStacks(atom.StatusId)}");
                    }
                    break;
                }

                case EffectAtomKind.Damage:
                {
                    // 场地伤害一律真实伤害（小暑"无视护盾与减伤"、霜降处决、大寒冰蚀是独立机制），
                    // 只按目标最大生命 % 计量。
                    for (int t = 0; t < targets.Count; t++)
                    {
                        var u = targets[t];
                        if (!u.IsAlive) continue;
                        int amount = CoreMath.RoundDamage(u.MaxHp * atom.PercentOfMaxHp);
                        if (amount <= 0) continue;
                        int dealt = u.TakeTrueDamage(amount);
                        if (dealt <= 0) continue;
                        st.Log.Add(st.Turn, BattleEventKind.Damage, actorId: u.RuntimeId,
                                   targetId: u.RuntimeId, amount: dealt, element: weather.Element,
                                   note: $"天时·{weather.BuffName}");
                        if (!u.IsAlive)
                            st.Log.Add(st.Turn, BattleEventKind.Death, targetId: u.RuntimeId,
                                       note: $"天时·{weather.BuffName}致死");
                    }
                    break;
                }

                case EffectAtomKind.StatModifier:
                {
                    for (int t = 0; t < targets.Count; t++)
                    {
                        var u = targets[t];
                        if (!u.IsAlive) continue;
                        u.AddModifier(atom.StatKey, atom.StatDelta, atom.StatTurns);
                        st.Log.Add(st.Turn, BattleEventKind.StatChange, targetId: u.RuntimeId,
                                   note: $"天时·{weather.BuffName}｜{atom.StatKey} " +
                                         $"{(atom.StatDelta >= 0f ? "+" : "")}{atom.StatDelta * 100f:F0}%" +
                                         (atom.StatTurns == 0 ? "（本场）" : $"（{atom.StatTurns} 回合）"));
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// 雨水「治疗溢出转化为护盾」。溢出量按比例折算（默认 0.5），再过一遍
        /// 全场护盾获取乘数（小雪类规则，若同场激活）—— 转化出来的也是"护盾效果"。
        /// </summary>
        private static void GrantOverflowShield(BattleState st, WeatherDef weather,
                                                BattleUnit u, int overflow, float ratio)
        {
            int amount = CoreMath.RoundDamage(overflow * ratio);
            if (st.Weather.ShieldGainMul != 1f) amount = CoreMath.RoundDamage(amount * st.Weather.ShieldGainMul);
            int added = u.AddShield(amount);
            if (added <= 0) return;
            st.Log.Add(st.Turn, BattleEventKind.Shield, actorId: u.RuntimeId,
                       targetId: u.RuntimeId, amount: added, element: u.Element,
                       note: $"天时·{weather.BuffName}｜溢出转化");
        }

        private static List<BattleUnit> CollectTargets(BattleState st, WeatherScope scope, TargetSelector pick)
        {
            var list = new List<BattleUnit>(12);
            if (scope == WeatherScope.Both || scope == WeatherScope.PlayerSide)
                AppendSide(st, TeamSide.Player, list);
            if (scope == WeatherScope.Both || scope == WeatherScope.EnemySide)
                AppendSide(st, TeamSide.Enemy, list);

            // 池内筛选：天时的"敌方""生命最低"这类相对表述落在绝对阵营池上做。
            if (pick == TargetSelector.SingleLowestHp && list.Count > 1)
            {
                BattleUnit lowest = list[0];
                for (int i = 1; i < list.Count; i++)
                    if (list[i].Hp < lowest.Hp) lowest = list[i];
                list.Clear();
                list.Add(lowest);
            }
            return list;
        }

        private static void AppendSide(BattleState st, TeamSide side, List<BattleUnit> into)
        {
            var units = st.UnitsOf(side);
            for (int i = 0; i < units.Count; i++)
                if (units[i].IsAlive) into.Add(units[i]);
        }
    }
}
