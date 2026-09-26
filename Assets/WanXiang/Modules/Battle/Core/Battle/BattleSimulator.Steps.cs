// ============================================================================
//  万相 · 战斗核心 · Steps（从 BattleSimulator.cs 拆出：**纯搬家、零行为变更**）
//  ---------------------------------------------------------------------------
//  partial class —— 与主文件共享作用域，成员可访问性与语义完全不变。
//  ⚠ 只挪位置，没改任何一行逻辑。
// ============================================================================
using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    public static partial class BattleSimulator
    {

        /// <summary>
        /// 分步跑完整场战斗。`st.PlayerControlled = true` 时，每个尚能行动的我方单位
        /// 行动之前会 yield 一个 NeedDecision；调用方下令后再 MoveNext 继续。
        /// </summary>
        public static System.Collections.Generic.IEnumerable<BattleStep> RunSteps(BattleState st)
        {
            var cfg = st.Config;
            var buf = new Buffers();
            var total = new BoardRoundReport();

            st.Log.Add(0, BattleEventKind.BattleStart, note: $"种子 {st.Random.Seed}｜回合上限 {cfg.MaxTurns}");
            BoardRules.ApplyResonance(st);
            PassiveHooks.ApplyBattleStart(st);   // v2.1 P3b：开场被动（攻击加成 / 开场回复）   // 开局先算一次，让"上阵即共鸣"在第一回合就成立

            int limit = CoreMath.Max(1, cfg.MaxTurns);
            int turn = 1;
            for (; turn <= limit; turn++)
            {
                st.Turn = turn;
                st.Log.Add(turn, BattleEventKind.TurnStart);

                // 灵力自然回复（v2.1 §3）：超出上限的部分丢失 —— 逼玩家在回合内花掉
                if (st.TeamMp < st.TeamMpMax)
                    st.TeamMp = System.Math.Min(st.TeamMpMax, st.TeamMp + BattleState.MpRegenPerTurn);
                // 敌方独立池同节奏回复（双方的灵力互相不干扰）
                if (st.EnemyMp < st.EnemyMpMax)
                    st.EnemyMp = System.Math.Min(st.EnemyMpMax, st.EnemyMp + BattleState.MpRegenPerTurn);

                // ---- 劫律 20「万相归一」：敌方每回合获得 1 层「劫」（攻击 +1%，无上限）。
                //      ⚠ 只涨攻击不涨生命 —— 与 GDD"全属性"有偏差，血量同步牵扯
                //      当前生命比例，先做攻击轴（压力曲线方向一致），偏差已记录。
                if (cfg.AllIsOne)
                {
                    var foes = st.UnitsOf(TeamSide.Enemy);
                    for (int i = 0; i < foes.Count; i++)
                    {
                        if (!foes[i].IsAlive) continue;
                        foes[i].PermanentAttackBonus += 0.01f;
                    }
                }

                // ---- 0) 天时·回合开始（GDD 3.1；无天时立即返回，指纹零影响） ----
                WeatherResolver.ResolveTurnStart(st);
                WeatherTurnStartHooks(st);   // 17 寒露：每 N 回合凝神（判空短路）

                // ---- 1) 回合开始的场地结算（棋盘四条规则） ----
                Accumulate(ref total, BoardRules.ResolveAdjacency(st));
                if (st.CheckOutcome()) break;

                // ---- 持续伤害与「生机」 ----
                TickStatuses(st);
                if (st.CheckOutcome()) break;

                // ---- 2) 出手序列 ----（先手连击在生成序列**之前**判定，GDD v1.1 §3.6）
                ResolveInitiativeChain(st, buf);
                st.BuildActionOrderInto(buf.Order);

                // 同步给 UI：行动条显示"本回合实际会发生的顺序"，而不是自己重排
                st.TurnOrder.Clear();
                st.TurnOrder.AddRange(buf.Order);
                st.Log.Add(turn, BattleEventKind.RoundResolve, note: "出手序列 " + DescribeOrder(buf.Order));

                // ---- 3) 逐个行动 ----
                for (int i = 0; i < buf.Order.Count; i++)
                {
                    var u = buf.Order[i];
                    if (!u.IsAlive) continue;          // 可能在别人回合里被打死
                    // 回合制断点：我方单位行动前把控制权交回调用方（手动模式等玩家下令）
                    if (st.PlayerControlled && u.Side == TeamSide.Player)
                        yield return new BattleStep { Kind = BattleStepKind.NeedDecision, Unit = u };
                    ExecuteAction(st, u, buf);
                    DevourAfterAction(st, u);      // 「吞噬」劫象：行动结束剥离对侧 1 增益 + 自损
                    if (st.CheckOutcome()) break;
                }
                if (st.IsOver) break;

                // ---- 3b) 先手连击：本回合常规行动之后，额外获得一次行动 ----
                ResolveInitiativeExtraAction(st, buf);
                if (st.IsOver) break;

                // ---- 4) 回合末（天时·回合末在 EndOfTurn 之后、TurnEnd 事件之前） ----
                EndOfTurn(st);
                PassiveHooks.ApplyTurnEnd(st);     // v2.1 P3b：回合末被动（回复类）
                WeatherEndExtraActions(st, buf);   // 23 小寒：速度最高者额外普攻（判空短路）
                WeatherResolver.ResolveTurnEnd(st);
                st.Log.Add(turn, BattleEventKind.TurnEnd);
            }

            if (!st.IsOver)
            {
                st.Turn = limit;
                st.Outcome = BattleOutcome.Draw;
                st.Log.Add(limit, BattleEventKind.BattleEnd, note: $"达到回合上限 {limit}");
            }

            var r = new BattleResult
            {
                Outcome = st.Outcome,
                Turns = st.Turn,
                EventCount = st.Log.Count,
                Fingerprint = st.Log.Fingerprint,
                GeneratePairs = total.GeneratePairs,
                CounterPairs = total.CounterPairs,
                PacifiedPairs = total.PacifiedPairs,
                RegenTotal = total.RegenTotal,
                TrueDamageTotal = total.TrueDamageTotal,
                QiGranted = total.QiGranted,
                Deaths = st.Log.CountOf(BattleEventKind.Death),
                SkillsCast = st.Log.CountOf(BattleEventKind.SkillCast),
                Crits = st.Log.CountOf(BattleEventKind.Crit),
            };
            yield return new BattleStep { Kind = BattleStepKind.Finished, Result = r };
        }
        /// <summary>一次跑完整场（自动战斗 / 无 UI 场景）：忽略决策点，一路推进到底。</summary>
        public static BattleResult Run(BattleState st)
        {
            BattleResult r = default;
            foreach (var step in RunSteps(st))
                if (step.Kind == BattleStepKind.Finished) r = step.Result;
            return r;
        }
        /// <summary>
        /// 推进到下一个需要玩家决策的点。
        /// 返回 true = 有单位等待下令（见 st.PendingUnit）；返回 false = 战斗结束（st.Result 有效）。
        /// </summary>
        public static bool AdvanceToNextDecision(BattleState st)
        {
            if (st.Stepper == null) st.Stepper = RunSteps(st).GetEnumerator();

            while (st.Stepper.MoveNext())
            {
                var step = st.Stepper.Current;
                if (step.Kind == BattleStepKind.NeedDecision)
                {
                    st.PendingUnit = step.Unit;
                    return true;
                }
                if (step.Kind == BattleStepKind.Finished)
                {
                    st.Result = step.Result;
                    st.PendingUnit = null;
                    return false;
                }
            }
            st.PendingUnit = null;
            return false;
        }
        /// <summary>
        /// 应用玩家指令。skillIndex = -1 表示普攻，&gt;= 0 表示战记下标；
        /// targetIndex = 目标下标（-1 = 交给 AI 选目标）。
        /// 指令写入 st.PendingCommand，轮到该单位时由 ExecuteAction 读取（P1：驱动就绪，
        /// 技能/目标注入见 P2 对 ChooseSkill 的改造）。
        /// </summary>
        public static void ApplyPlayerCommand(BattleState st, int skillIndex, int targetIndex)
        {
            st.PendingCommand = new PlayerCommand
            {
                ActorId = st.PendingUnit != null ? st.PendingUnit.RuntimeId : null,
                SkillIndex = skillIndex,
                TargetIndex = targetIndex,
                Valid = st.PendingUnit != null,
            };
        }
        private static void Accumulate(ref BoardRoundReport acc, in BoardRoundReport d)
        {
            acc.GeneratePairs += d.GeneratePairs;
            acc.CounterPairs += d.CounterPairs;
            acc.PacifiedPairs += d.PacifiedPairs;
            acc.RegenTotal += d.RegenTotal;
            acc.TrueDamageTotal += d.TrueDamageTotal;
            acc.QiGranted += d.QiGranted;
        }
        // ================================================================
        //  回合内各阶段
        // ================================================================

        /// <summary>
        /// 持续伤害与「生机」回复。
        /// 顺序固定：先按 AllUnits 顺序逐人处理，人内先跳伤后回血 ——
        /// 顺序影响"跳伤把人跳死了还要不要回血"这类边界，必须钉死。
        /// </summary>
        private static void TickStatuses(BattleState st)
        {
            var all = st.AllUnits;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (!u.IsAlive) continue;

                // 跳伤。存的是数值而不是公式（见 StatusCatalog 文件头）。
                // 这里只调 TakeTrueDamage，它不碰 Statuses 列表，所以可以在列表上直接遍历。
                for (int k = 0; k < u.Statuses.Count; k++)
                {
                    var s = u.Statuses[k];
                    if (s.DotFlatPerStack <= 0f) continue;

                    int amount = CoreMath.RoundDamage(s.DotFlatPerStack * s.Stacks);

                    // 劫律 11「灼烧入骨」：灼烧伤害 +30%（只作用于灼烧状态，冰蚀独立）
                    if (s.Id == StatusCatalog.Burn && st.Config.BurnTakenMul != 1f)
                        amount = CoreMath.RoundDamage(amount * st.Config.BurnTakenMul);

                    // 大暑「土润溽暑」：火属性单位受到的持续伤害减半（天时修正，判空在前）
                    if (st.Weather != null && u.Element == Element.Fire)
                    {
                        float dotMul = st.Weather.FireUnitDotTakenMul;
                        if (dotMul != 1f) amount = CoreMath.RoundDamage(amount * dotMul);
                    }

                    if (amount <= 0) continue;

                    int dealt = u.TakeTrueDamage(amount);
                    if (dealt > 0)
                    {
                        st.Log.Add(st.Turn, BattleEventKind.Damage, actorId: u.RuntimeId,
                                   targetId: u.RuntimeId, amount: dealt, element: u.Element,
                                   note: $"{s.Def.Name}·持续伤害 ×{s.Stacks}");
                    }
                    if (!u.IsAlive)
                    {
                        st.Log.Add(st.Turn, BattleEventKind.Death, targetId: u.RuntimeId,
                                   note: $"{s.Def.Name}致死");
                        break;
                    }
                }
                if (!u.IsAlive) continue;

                // 「生机」每层每回合回复 2% 最大生命。禁疗期（小雪）跳过。
                int vigor = u.GetStacks(StatusCatalog.Vigor);
                if (vigor > 0 && u.CanBeHealed && !st.HealBanned)
                {
                    int amount = CoreMath.RoundDamage(u.MaxHp * st.Config.VigorRegenPerStack * vigor);
                    int healed = u.Heal(amount);
                    if (healed > 0)
                    {
                        st.Log.Add(st.Turn, BattleEventKind.Heal, actorId: u.RuntimeId,
                                   targetId: u.RuntimeId, amount: healed, element: u.Element,
                                   note: $"生机 ×{vigor}");
                    }
                }
            }
        }
        private static void EndOfTurn(BattleState st)
        {
            bool cdAccel = st.Weather != null;   // 天时在场才做 CD 推进修正（指纹红线）
            var all = st.AllUnits;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                u.TickCooldowns();
                if (cdAccel) TickCooldownExtra(st, u);
                u.TickStatusDurations();
                u.TickModifiers();
                if (cdAccel || u.HasEgg) TickEggHatch(st, u);   // 03 惊蛰 /「复苏」劫象：卵不依赖天时也能孵
            }
        }
        /// <summary>
        /// 天时的 CD 推进乘数（小满 ×1.3 等）。CD 是整数格子，非整数倍速用累积器兑现：
        /// 有技能正在冷却时，每回合末把 (乘数 - 1) 攒进 u.CdProgressExtra，攒满 ±1
        /// 就多推 / 少推一格。期望推进速率趋于乘数，全程不掷骰。
        /// ⚠ 闲置时不攒点（没有冷却可推进，攒了也是凭空透支未来的 CD）；
        ///   已攒的点保留到下一次进入冷却再用 —— 否则点数会在"冷却恰好归零"的
        ///   回合被白白消耗，+30% 在短冷却技能上实测推不快，等于没接。
        /// </summary>
        private static void TickCooldownExtra(BattleState st, BattleUnit u)
        {
            float mul = st.Weather.CdAdvanceMulFor(u.Side);
            if (mul == 1f) return;

            if (!AnyCooling(u)) return;   // 闲置不攒点

            u.CdProgressExtra += mul - 1f;
            while (u.CdProgressExtra >= 1f && AnyCooling(u))
            {
                u.CdProgressExtra -= 1f;
                TickOneCooldown(u);
            }
            while (u.CdProgressExtra <= -1f && AnyCooling(u))
            {
                u.CdProgressExtra += 1f;
                UntickOneCooldown(u);
            }
        }
        private static bool AnyCooling(BattleUnit u)
        {
            for (int i = 0; i < u.Cooldowns.Length; i++)
                if (u.Cooldowns[i] > 0) return true;
            return false;
        }
        private static void TickOneCooldown(BattleUnit u)
        {
            for (int i = 0; i < u.Cooldowns.Length; i++)
                if (u.Cooldowns[i] > 0) u.Cooldowns[i]--;
        }
        /// <summary>减速方向：把本回合已经推进过的一格补回去（只补还大于 0 的，不会把 CD 推成负数）。</summary>
        private static void UntickOneCooldown(BattleUnit u)
        {
            for (int i = 0; i < u.Cooldowns.Length; i++)
                if (u.Cooldowns[i] > 0) { u.Cooldowns[i]++; return; }
        }
        private static string DescribeOrder(List<BattleUnit> order)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < order.Count; i++)
            {
                if (i > 0) sb.Append(" → ");
                var u = order[i];
                sb.Append(u.RuntimeId).Append(' ').Append(Cn.Of(u.Element)).Append(u.DisplayName)
                  .Append("(速").Append(u.Speed.ToString("F0")).Append(')');
            }
            return sb.ToString();
        }
    }
}
