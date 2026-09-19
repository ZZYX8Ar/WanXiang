// ============================================================================
//  万相 · 战斗核心 · 回合循环与结算
//  ---------------------------------------------------------------------------
//  对应 GDD 6.2 的「回合心跳」骨架（那一节要求把心跳放在 System 里由协程驱动、
//  绝不放进 Update 轮询）。这里做的是**纯逻辑那部分**：
//
//      while (!IsOver)
//          1) 回合开始：场地结算（棋盘四条规则）→ 持续伤害与「生机」
//          2) 按速度生成出手序列（同速按站位从左到右）
//          3) 逐个单位行动：选技能 → 逐个原子效果结算 → 进 CD / 涨怒气
//          4) 回合末：CD 推进 / 状态时长 -1 / 属性修正 -1
//
//  协程、协程间隔、表现层播放都**不在这里** —— 这个程序集看不见 UnityEngine
//  （见 Types.cs 文件头的三条理由），它只负责"这一步之后世界变成什么样"。
//  表现层读 BattleLog 的事件流来播动画，读的时候不回头问 BattleState。
//
//  ⚠ 关于"伤害日志里记的是哪个数"：记的是**结算后、被护盾吸收前**的伤害量。
//    理由是这个数才是玩家在头顶伤害数字里看到的量；而被护盾吃掉的差额
//    可以从事件流里前后推出的护盾值算出来，不必重复记。
// ============================================================================

using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    /// <summary>一场战斗的结果摘要。自检报告直接用它，不需要再翻 BattleState。</summary>
    public struct BattleResult
    {
        public BattleOutcome Outcome;
        public int Turns;
        public int EventCount;
        public uint Fingerprint;

        public int GeneratePairs;    // 全程触发的相生相邻格对总数
        public int CounterPairs;     // 全程触发的相克相冲格对总数
        public int PacifiedPairs;    // 全程被中宫平息掉的相冲格对总数
        public int RegenTotal;
        public int TrueDamageTotal;
        public int QiGranted;
        public int Deaths;
        public int SkillsCast;
        public int Crits;

        public string Summary()
        {
            return $"结果 {Cn.Of(Outcome)}｜回合 {Turns}｜事件 {EventCount} 条｜指纹 0x{Fingerprint:X8}\n" +
                   $"  相生相邻 {GeneratePairs} 对（回复 {RegenTotal}）｜相克相冲 {CounterPairs} 对（真伤 {TrueDamageTotal}）\n" +
                   $"  中宫平息 {PacifiedPairs} 对｜同气 +{QiGranted} 层｜出手 {SkillsCast} 次｜暴击 {Crits} 次｜阵亡 {Deaths} 人";
        }
    }

    public static class BattleSimulator
    {
        // ================================================================
        //  断点驱动（回合制 v2.1 P1）
        //  ------------------------------------------------------------------
        //  实现要点：用 C# 迭代器当状态机 —— buf / total 这些局部变量在迭代器里
        //  天然保持状态，**不需要**把它们提升到 BattleState，也不必重写回合逻辑。
        //    · RunSteps           原 Run 的循环体，仅在我方单位行动前 yield 一个决策点
        //    · Run                消费步骤流一次跑完（自动战斗 / 无 UI / 测试）
        //    · AdvanceToNextDecision / ApplyPlayerCommand   手动模式的驱动接口
        //  伤害、五行、状态、结算的全部既有逻辑一行未改。
        // ================================================================

        /// <summary>战斗推进的一步。</summary>
        public struct BattleStep
        {
            public BattleStepKind Kind;
            public BattleUnit Unit;        // NeedDecision：等待下令的单位
            public BattleResult Result;    // Finished：整场结算
        }

        public enum BattleStepKind
        {
            /// <summary>自动推进中（敌方行动、回合结算等），UI 可继续。</summary>
            Animating = 0,
            /// <summary>轮到我方单位，等待玩家下令。</summary>
            NeedDecision = 1,
            /// <summary>战斗结束（Result 有效）。</summary>
            Finished = 2,
        }

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

        /// <summary>
        /// 一个单位的行动。技能优先级：绝技 → 战技 → 普攻。
        /// （STEP 1 走自动战斗；绝技在正式玩法里是玩家的手动干预点，见 BattleConfig.AutoCastUltimate。）
        /// </summary>
        private static void ExecuteAction(BattleState st, BattleUnit actor, Buffers buf)
        {
            var cfg = st.Config;
            actor.PursuitUsedThisAction = false;   // 09 芒种：追击"每次行动限 1 次"的计数
            st.Log.Add(st.Turn, BattleEventKind.ActionBegin, actorId: actor.RuntimeId,
                       note: actor.DisplayName);

            if (!actor.CanAct)
            {
                // 冻结之类的"不能行动"。注意**不能跳过回合末的 CD 推进** ——
                // 被冻住不等于技能也跟着暂停，否则控制队会被自己坑。
                st.Log.Add(st.Turn, BattleEventKind.ActionEnd, actorId: actor.RuntimeId, note: "无法行动");
                return;
            }

            var skill = ChooseSkill(st, actor);
            if (skill == null || skill.Effects == null || skill.Effects.Length == 0)
            {
                st.Log.Add(st.Turn, BattleEventKind.ActionEnd, actorId: actor.RuntimeId, note: "没有可用技能");
                return;
            }

            // ---- v2.1 P4：普攻（无冷却）默认打「最前排」----
            //  棋盘的"前排/后排"必须决定谁先承伤，站位才有策略意义；
            //  否则人人都能随手切后排，站位就只是摆着好看。
            //  特殊技能保留各自的目标规则（如刺客类打"生命最低"/后排）。
            //  ⚠ Clone() 是深拷贝 Effects（已确认），改副本不会污染技能定义；
            //     但每次普攻都 Clone 一次有分配开销 —— 普攻频率不高，先做对再做快。
            if (skill.Cd == 0)
            {
                var patched = skill.Clone();
                patched.PrimaryTarget = TargetSelector.SingleFrontMost;
                for (int i = 0; i < patched.Effects.Length; i++)
                {
                    var a = patched.Effects[i];
                    if (a.Kind == EffectAtomKind.Damage)
                    {
                        a.Target = TargetSelector.SingleFrontMost;
                        patched.Effects[i] = a;
                    }
                }
                skill = patched;
            }

            st.Log.Add(st.Turn, BattleEventKind.SkillCast, actorId: actor.RuntimeId,
                       skillName: skill.Name, element: ResolveElement(Element.None, skill, actor),
                       note: $"{actor.DisplayName}·{Cn.Of(skill.Type)}", skill: skill.Type);

            for (int i = 0; i < skill.Effects.Length; i++)
                ResolveAtom(st, actor, skill, skill.Effects[i], buf);

            // 冷却：绝技额外吃「同属共鸣」的 CD 缩减（STEP 1 的替身用法，见 BattleUnit.ResonanceCdDelta）
            int cdDelta = skill.Type == SkillType.Ultimate ? actor.ResonanceCdDelta : 0;
            actor.PutOnCooldown(skill.Type, skill.Cd, cdDelta);

            if (skill.Type == SkillType.Basic) actor.AddRage(cfg.RagePerBasicAttack);
            if (skill.Type == SkillType.Ultimate) actor.SpendRage(cfg.UltimateRageCost);

            st.Log.Add(st.Turn, BattleEventKind.ActionEnd, actorId: actor.RuntimeId);
        }

        private static SkillDef ChooseSkill(BattleState st, BattleUnit u)
        {
            var cfg = st.Config;
            if (!u.CanCastSkills) return u.GetSkill(SkillType.Basic);   // 05 清明的对立面：沉默只封技能

            // ---- 回合制 v2.1 P2：灵力裁决 ----
            //  先按既有规则（玩家指令优先 / AI）选出技能，再统一结算灵力消耗：
            //  不够就落回普攻（普攻 0 耗，永远可用的兜底，避免回合卡死）。
            {
                var slot = DecideSlot(st, u, out var chosen);
                if (chosen != null)
                {
                    int cost = BattleState.MpCostOf(slot);
                    if (cost <= st.TeamMp)
                    {
                        st.TeamMp -= cost;
                        return chosen;
                    }
                }
                return u.GetSkill(SkillType.Basic);
            }
            //  SkillIndex 直接用 SkillType 的枚举值（Basic/Active/Ultimate），-1 = 未指定（走 AI）。
            //  不可用（冷却/元气不足）时回退普攻 —— 玩家的选择不该把回合卡死，
            //  但界面在点之前就该禁用按钮，所以这里的回退只是兜底。
            if (st.PlayerControlled && u.Side == TeamSide.Player
                && st.PendingCommand.Valid && st.PendingCommand.ActorId == u.RuntimeId
                && st.PendingCommand.SkillIndex >= 0)
            {
                var want = (SkillType)st.PendingCommand.SkillIndex;
                st.PendingCommand = default;      // 一令一用：消费掉，避免影响下一个单位
                var picked = u.GetSkill(want);
                bool usable = picked != null && (want == SkillType.Basic || u.CanCast(want, cfg));
                if (usable) return picked;
                return u.GetSkill(SkillType.Basic);
            }

            return u.GetSkill(SkillType.Basic);
        }

        /// <summary>
        /// 决定"这个单位这次放哪个槽"（不扣灵力）：玩家指令优先，其次 AI 规则。
        /// 灵力消耗交给 ChooseSkill 统一结算。
        /// </summary>
        private static SkillType DecideSlot(BattleState st, BattleUnit u, out SkillDef chosen)
        {
            var cfg = st.Config;

            // ---- 玩家指令优先（P1-2）----
            if (st.PlayerControlled && u.Side == TeamSide.Player
                && st.PendingCommand.Valid && st.PendingCommand.ActorId == u.RuntimeId
                && st.PendingCommand.SkillIndex >= 0)
            {
                var want = (SkillType)st.PendingCommand.SkillIndex;
                st.PendingCommand = default;      // 一令一用
                var picked = u.GetSkill(want);
                if (picked != null && (want == SkillType.Basic || u.CanCast(want, cfg)))
                {
                    chosen = picked;
                    return want;
                }
                chosen = u.GetSkill(SkillType.Basic);
                return SkillType.Basic;
            }

            // ---- AI 规则（按策略组调整，v2.1 P4；只影响敌方，我方由玩家下令）----
            var ult = u.GetSkill(SkillType.Ultimate);
            var act = u.GetSkill(SkillType.Active);

            if (cfg.AiProfile == AiProfile.Aggressive)
            {
                // 激进：资源优先兑现成伤害 —— 终结技能放就放，不等时机
                if (ult != null && u.CanCast(SkillType.Ultimate, cfg)) { chosen = ult; return SkillType.Ultimate; }
                if (act != null && u.CanCast(SkillType.Active, cfg)) { chosen = act; return SkillType.Active; }
            }
            else if (cfg.AiProfile == AiProfile.Cautious)
            {
                // 稳健：生命低于一半才舍得放战记（其余普攻攒着），终结技同理
                bool hurt = u.HpPercent < 0.5f;
                if (!hurt && act != null && u.CanCast(SkillType.Active, cfg)) { chosen = act; return SkillType.Active; }
            }
            else
            {
                // 均衡（默认）：沿用原规则
                if (ult != null && cfg.AutoCastUltimate && u.CanCast(SkillType.Ultimate, cfg))
                { chosen = ult; return SkillType.Ultimate; }
                if (act != null && u.CanCast(SkillType.Active, cfg)) { chosen = act; return SkillType.Active; }
            }

            chosen = u.GetSkill(SkillType.Basic);
            return SkillType.Basic;
        }

        /// <summary>
        /// 执行连携技（v2.1 P4）：主兽 + 伙伴各扣 2 灵力，按 ComboEffect 结算。
        /// 返回 false = 条件不满足（每场限一次 / 伙伴不在 / 灵力不足），不产生任何事件。
        /// </summary>
        public static bool ExecuteCombo(BattleState st, string comboId, BattleUnit host)
        {
            var combo = ComboRules.For(comboId);
            if (combo == null || host == null || !host.IsAlive) return false;
            if (st.UsedCombos.Contains(comboId)) return false;
            var partner = ComboRules.FindPartner(st, host, combo);
            if (partner == null) return false;
            if (st.TeamMp < combo.MpCost * 2) return false;

            st.TeamMp -= combo.MpCost * 2;
            st.UsedCombos.Add(comboId);

            st.Log.Add(st.Turn, BattleEventKind.SkillCast, actorId: host.RuntimeId,
                       skillName: combo.Name, element: Element.None,
                       note: host.DisplayName + "×" + partner.DisplayName + "·" + combo.Name,
                       skill: SkillType.Active);

            int power = CoreMath.RoundDamage(host.Attack * combo.Power);
            var foeSide = BattleState.Opponent(host.Side);

            switch (combo.Effect)
            {
                case ComboEffect.WoodPulse:
                {
                    var foes = st.UnitsOf(foeSide);
                    for (int i = 0; i < foes.Count; i++)
                        if (foes[i].IsAlive) foes[i].TakeDamage(power);
                    var ours = st.UnitsOf(host.Side);
                    for (int i = 0; i < ours.Count; i++)
                        if (ours[i].IsAlive) ours[i].Heal((int)(ours[i].MaxHp * 0.15f));
                    break;
                }
                case ComboEffect.FireStorm:
                {
                    var foes = st.UnitsOf(foeSide);
                    for (int i = 0; i < foes.Count; i++)
                        if (foes[i].IsAlive) foes[i].TakeDamage(power);
                    break;
                }
                case ComboEffect.MetalBreak:
                {
                    var list = new System.Collections.Generic.List<BattleUnit>();
                    PickByRank(st, foeSide, list, host, true);      // 最前排单体
                    if (list.Count > 0 && list[0].IsAlive) list[0].TakeDamage(power);
                    break;
                }
            }
            return true;
        }

        // ================================================================
        //  原子效果结算
        // ================================================================

        private static void ResolveAtom(BattleState st, BattleUnit src, SkillDef skill,
                                        in EffectAtom atom, Buffers buf)
        {
            // 目标池口径：伤害类打对面，其余（治疗/护盾/增益）打自己这边。
            // 这是最不容易出错的默认值 —— 真要跨阵营（如"给敌方上减益"），
            // 用 ApplyStatus 的减益状态也一样落在对面，见下面的分支。
            bool debuffLike = atom.Kind == EffectAtomKind.Damage
                           || (atom.Kind == EffectAtomKind.ApplyStatus
                               && StatusCatalog.Get(atom.StatusId).IsDebuff);
            TeamSide pool = debuffLike ? BattleState.Opponent(src.Side) : src.Side;

            ResolveTargets(st, src, atom.Target, pool, buf.Targets);
            if (buf.Targets.Count == 0) return;

            switch (atom.Kind)
            {
                case EffectAtomKind.Damage:
                {
                    Element el = ResolveElement(atom.ElementOverride, skill, src);
                    int hits = CoreMath.Max(1, atom.Hits);

                    if (atom.Target == TargetSelector.RandomEnemyMultiHit)
                    {
                        // 多段随机：**每一段重新抽一次目标**，这才是"乱击"的手感
                        for (int h = 0; h < hits; h++)
                        {
                            ResolveTargets(st, src, TargetSelector.RandomEnemy, pool, buf.Targets);
                            if (buf.Targets.Count == 0) break;
                            DealDamage(st, src, skill, atom, el, buf.Targets[0]);
                            if (st.IsOver) break;
                        }
                    }
                    else
                    {
                        for (int t = 0; t < buf.Targets.Count; t++)
                        {
                            for (int h = 0; h < hits; h++)
                            {
                                if (!buf.Targets[t].IsAlive) break;
                                DealDamage(st, src, skill, atom, el, buf.Targets[t]);
                                if (st.IsOver) break;
                            }
                            if (st.IsOver) break;
                        }
                    }
                    break;
                }

                case EffectAtomKind.Heal:
                {
                    for (int t = 0; t < buf.Targets.Count; t++)
                    {
                        var dst = buf.Targets[t];
                        if (!dst.CanBeHealed || st.HealBanned) continue;   // 禁疗（小雪）
                        float raw = src.Attack * atom.Power + dst.MaxHp * atom.PercentOfMaxHp;
                        int amount = CoreMath.RoundDamage(raw * src.HealShieldMultiplier);
                        int healed = dst.Heal(amount);
                        if (healed <= 0) continue;
                        st.Log.Add(st.Turn, BattleEventKind.Heal, actorId: src.RuntimeId,
                                   targetId: dst.RuntimeId, skillName: skill?.Name,
                                   amount: healed, element: src.Element);
                    }
                    break;
                }

                case EffectAtomKind.Shield:
                {
                    for (int t = 0; t < buf.Targets.Count; t++)
                    {
                        var dst = buf.Targets[t];
                        if (!dst.IsAlive) continue;
                        float raw = src.Attack * atom.Power + dst.MaxHp * atom.PercentOfMaxHp;
                        int amount = CoreMath.RoundDamage(raw * src.HealShieldMultiplier);

                        // 小雪「虹藏不见」：所有护盾效果 +50%（天时修正，判空在前）
                        if (st.Weather != null)
                        {
                            float shieldMul = st.Weather.ShieldGainMul;
                            if (shieldMul != 1f) amount = CoreMath.RoundDamage(amount * shieldMul);
                        }

                        int added = dst.AddShield(amount);
                        if (added <= 0) continue;
                        st.Log.Add(st.Turn, BattleEventKind.Shield, actorId: src.RuntimeId,
                                   targetId: dst.RuntimeId, skillName: skill?.Name,
                                   amount: added, element: src.Element);
                    }
                    break;
                }

                case EffectAtomKind.ApplyStatus:
                {
                    for (int t = 0; t < buf.Targets.Count; t++)
                    {
                        var dst = buf.Targets[t];
                        if (!dst.IsAlive) continue;

                        // 持续伤害在**施加瞬间**折算成数值（见 StatusCatalog 文件头的取舍说明）：
                        //   Power>0            → 按施加者攻击力的比例
                        //   PercentOfMaxHp>0   → 按目标最大生命的比例
                        float dot = 0f;
                        if (atom.Power > 0f) dot += src.Attack * atom.Power;
                        if (atom.PercentOfMaxHp > 0f) dot += dst.MaxHp * atom.PercentOfMaxHp;

                        // 05 清明：免疫混乱/沉默、我方减益时长 -1（与天时自身的施加路径共用）
                        int turns = atom.StatusTurns;
                        var def = StatusCatalog.Get(atom.StatusId);
                        if (!WeatherFilterStatus(st, dst, atom.StatusId, ref turns))
                        {
                            st.Log.Add(st.Turn, BattleEventKind.StatusRemoved, actorId: src.RuntimeId,
                                       targetId: dst.RuntimeId, skillName: skill?.Name,
                                       note: $"天时免疫 {def.Name}");
                            continue;
                        }

                        dst.ApplyStatus(atom.StatusId, atom.StatusStacks, turns, dot);
                        st.Log.Add(st.Turn, BattleEventKind.StatusApplied, actorId: src.RuntimeId,
                                   targetId: dst.RuntimeId, skillName: skill?.Name,
                                   amount: atom.StatusStacks,
                                   note: $"{def.Name} ×{dst.GetStacks(atom.StatusId)}");
                    }
                    break;
                }

                case EffectAtomKind.RemoveStatus:
                {
                    for (int t = 0; t < buf.Targets.Count; t++)
                    {
                        var dst = buf.Targets[t];
                        string what = atom.StatusId == null ? "全部减益" : StatusCatalog.Get(atom.StatusId).Name;
                        int removed = dst.RemoveStatus(atom.StatusId);
                        if (removed <= 0) continue;
                        st.Log.Add(st.Turn, BattleEventKind.StatusRemoved, actorId: src.RuntimeId,
                                   targetId: dst.RuntimeId, skillName: skill?.Name,
                                   amount: removed, note: $"驱散 {what}");
                    }
                    break;
                }

                case EffectAtomKind.StatModifier:
                {
                    for (int t = 0; t < buf.Targets.Count; t++)
                    {
                        var dst = buf.Targets[t];
                        if (!dst.IsAlive) continue;
                        dst.AddModifier(atom.StatKey, atom.StatDelta, atom.StatTurns);
                        st.Log.Add(st.Turn, BattleEventKind.StatChange, actorId: src.RuntimeId,
                                   targetId: dst.RuntimeId, skillName: skill?.Name,
                                   note: $"{atom.StatKey} {(atom.StatDelta >= 0f ? "+" : "")}" +
                                         $"{atom.StatDelta * 100f:F0}%" +
                                         (atom.StatTurns == 0 ? "（本场）" : $"（{atom.StatTurns} 回合）"));
                    }
                    break;
                }

                case EffectAtomKind.Revive:
                {
                    // STEP 1 不做复活：它牵涉"阵亡单位的槽位还占不占格"这类棋盘语义，
                    // 值得单独一轮设计。这里显式留痕，不静默吞掉。
                    st.Log.Add(st.Turn, BattleEventKind.RoundResolve, actorId: src.RuntimeId,
                               skillName: skill?.Name, note: "复活原子尚未实现（STEP 1 范围外）");
                    break;
                }
            }
        }

        /// <summary>一段伤害的完整结算。多段技能每段独立判定暴击（GDD 未写，取独立）。</summary>
        private static void DealDamage(BattleState st, BattleUnit src, SkillDef skill,
                                       in EffectAtom atom, Element el, BattleUnit dst)
        {
            if (!dst.IsAlive) return;

            bool crit = st.Random.Chance(src.Def.CritRate);
            int dmg = ComputeDamage(st, src, dst, el, atom.Power, atom.TrueDamage, crit,
                                    IsAoeTarget(atom.Target));

            int dealt;
            if (atom.TrueDamage) dealt = dst.TakeTrueDamage(dmg);
            else dealt = dst.TakeDamage(dmg, atom.IgnoreShield);

            if (crit) st.Log.Add(st.Turn, BattleEventKind.Crit, actorId: src.RuntimeId, targetId: dst.RuntimeId);

            st.Log.Add(st.Turn, BattleEventKind.Damage, actorId: src.RuntimeId, targetId: dst.RuntimeId,
                       skillName: skill?.Name, amount: dmg, element: el,
                       note: BuildDamageNote(st, src, dst, el, crit, atom.TrueDamage, dealt));

            dst.AddRage(st.Config.RageWhenHit);

            if (dealt > 0 && !dst.IsAlive)
            {
                st.Log.Add(st.Turn, BattleEventKind.Death, targetId: dst.RuntimeId,
                           note: $"{dst.DisplayName} 阵亡");
            }

            // ---- 天时钩子（GDD 3.3 剩余 8 条）：命中/暴击/击杀/受击四条通路 ----
            // 判空短路：无天时这一行不进函数体，战斗逐位不变（指纹红线）。
            if (st.Weather != null) PostDamageHooks(st, src, dst, el, dmg, dealt, crit, atom.TrueDamage);
        }

        // ================================================================
        //  先手连击（GDD v1.1 §3.6，v1.1 新增规则）
        //  ----------------------------------------------------------------
        //  为什么要有这条：回合制里"先出手"本身价值有限（早 0.1 秒行动还是一回合），
        //  导致「疾」这个职业拿 1100 生命 / 130 攻击换来一个几乎没价值的属性。
        //  这条规则给速度确权 —— 跨过门槛直接**多打一次**。
        //
        //  判定：每回合生成出手序列前，若某单位速度 ≥ 敌方当前最高速度 × 门槛，
        //        该单位本回合常规行动后额外行动一次；**每方每回合最多 1 次**。
        //  门槛：BattleConfig.InitiativeRatio（1.50）；大雪节点降到 1.20。
        //
        //  ⚠ 「敌方当前最高速度」取**本回合开始时**的快照（含天时/状态修正），
        //    不取行动过程中的动态值 —— 否则先手方打掉对方最快的单位时，
        //    连击资格会凭空出现，规则变得不可预测、也无法在回合开始时预告给玩家。
        // ================================================================

        private static BattleUnit _initiativePlayer;
        private static BattleUnit _initiativeEnemy;
        private static bool _initiativePlayerUsed;
        private static bool _initiativeEnemyUsed;

        private static void ResolveInitiativeChain(BattleState st, Buffers buf)
        {
            float ratio = st.Weather != null
                ? st.Weather.InitiativeRatioOrDefault(st.Config.InitiativeRatio)
                : st.Config.InitiativeRatio;

            _initiativePlayer = PickInitiativeUnit(st, TeamSide.Player, ratio);
            _initiativeEnemy = PickInitiativeUnit(st, TeamSide.Enemy, ratio);
            _initiativePlayerUsed = false;
            _initiativeEnemyUsed = false;

            if (_initiativePlayer != null)
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve, actorId: _initiativePlayer.RuntimeId,
                           note: $"先手连击：{_initiativePlayer.DisplayName} 本回合额外行动（门槛 {ratio:F2}×）");
            if (_initiativeEnemy != null)
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve, actorId: _initiativeEnemy.RuntimeId,
                           note: $"先手连击（敌）：{_initiativeEnemy.DisplayName} 本回合额外行动（门槛 {ratio:F2}×）");
        }

        /// <summary>挑出本回合获得额外行动的单位：速度达标者里最快的那个（每方最多 1 个）。</summary>
        private static BattleUnit PickInitiativeUnit(BattleState st, TeamSide side, float ratio)
        {
            float oppMax = 0f;
            var opp = st.UnitsOf(side == TeamSide.Player ? TeamSide.Enemy : TeamSide.Player);
            for (int i = 0; i < opp.Count; i++)
            {
                var u = opp[i];
                if (!u.IsAlive) continue;
                float sp = st.EffectiveSpeed(u);
                if (sp > oppMax) oppMax = sp;
            }
            if (oppMax <= 0f) return null;

            BattleUnit best = null;
            float bestSpeed = 0f;
            var mine = st.UnitsOf(side);
            for (int i = 0; i < mine.Count; i++)
            {
                var u = mine[i];
                if (!u.IsAlive) continue;
                float sp = st.EffectiveSpeed(u);
                if (sp < oppMax * ratio) continue;
                if (best == null || sp > bestSpeed) { best = u; bestSpeed = sp; }
            }
            return best;
        }

        /// <summary>常规行动结束后补上那次额外行动（同一回合内只补一次）。</summary>
        private static void ResolveInitiativeExtraAction(BattleState st, Buffers buf)
        {
            ExtraActionFor(st, buf, _initiativePlayer, TeamSide.Player, ref _initiativePlayerUsed);
            ExtraActionFor(st, buf, _initiativeEnemy, TeamSide.Enemy, ref _initiativeEnemyUsed);
        }

        private static void ExtraActionFor(BattleState st, Buffers buf, BattleUnit u,
                                           TeamSide side, ref bool used)
        {
            if (used || u == null) return;
            used = true;
            if (!u.IsAlive || !u.CanAct) return;      // 这回合被打死/被控就不补了
            ExecuteAction(st, u, buf);
        }

        // ================================================================
        //  天时事件钩子（GDD 3.3 剩余 8 条）
        //  ----------------------------------------------------------------
        //  这些规则的共同点是"必须挂在战斗过程的某个点上"，没法用回合开始/结束的
        //  原子与乘数表达。全部**先判字段为假就返回** —— 无天时/空天时零影响。
        // ================================================================

        /// <summary>
        /// 一段伤害结算之后的钩子：立夏附烧（命中）→ 处暑溢出转盾（击杀）→ 芒种追击（暴击）
        /// → 立冬受击冻结（受击）。
        /// 顺序即语义：先结算这一击自身的效果，再判击杀溢出，最后才追加追击 ——
        /// 追击打出去时"目标是否已死"的答案才是最终的。
        /// </summary>
        private static void PostDamageHooks(BattleState st, BattleUnit src, BattleUnit dst,
                                            Element el, int dmg, int dealt, bool crit, bool trueDamage)
        {
            var w = st.Weather;

            // 07 立夏「炎气初升」：我方所有攻击附带燃烧（按施法者攻击力折算，与技能 DoT 同一套）
            float burnPower = w.AttackBurnPowerFor(src.Side);
            if (burnPower > 0f && dst.IsAlive)
            {
                dst.ApplyStatus(StatusCatalog.Burn, 1, w.AttackBurnTurns, src.Attack * burnPower);
                var burnDef = StatusCatalog.Get(StatusCatalog.Burn);
                st.Log.Add(st.Turn, BattleEventKind.StatusApplied, actorId: src.RuntimeId,
                           targetId: dst.RuntimeId, amount: 1,
                           note: $"{burnDef.Name} ×{dst.GetStacks(StatusCatalog.Burn)}（天时附魔）");
            }

            // 14 处暑「鹰祭而后猎」：我方**击杀**时，溢出伤害按比例转成全队护盾
            if (!dst.IsAlive && src.Side == TeamSide.Player && w.KillOverflowShieldOn)
            {
                int overkill = dmg - dealt;                       // dealt = 实际掉的血（含被护盾吃掉的部分）
                if (overkill > 0)
                {
                    int total = CoreMath.RoundDamage(overkill * w.KillOverflowShieldRatio);
                    GrantTeamShieldFromKill(st, w, src, total);
                }
            }

            // 24 大寒「寒气之逆极」下半：火属性技能命中 → 融冰（公开成 WeatherIceMelt 供测试）
            if (w.IceMeltOnFireSkill && el == Element.Fire)
                WeatherIceMelt(st, src, dst);

            // 09 芒种「螳螂生」：我方暴击时追加一次追击（每次行动限 1 次）
            // 取舍：追击**不再判暴击**（暴击的追击再暴击会链式触发，GDD 没规定这种递归）。
            if (crit && dst.IsAlive && !src.PursuitUsedThisAction)
            {
                float pursuitPower = w.PursuitPowerFor(src.Side);
                if (pursuitPower > 0f)
                {
                    src.PursuitUsedThisAction = true;
                    int pd = ComputeDamage(st, src, dst, el, pursuitPower, false, false);
                    int pdl = dst.TakeDamage(pd);
                    st.Log.Add(st.Turn, BattleEventKind.Damage, actorId: src.RuntimeId,
                               targetId: dst.RuntimeId, amount: pd, element: el,
                               note: "天时·螳螂生：追击");
                    if (pdl > 0 && !dst.IsAlive)
                        st.Log.Add(st.Turn, BattleEventKind.Death, targetId: dst.RuntimeId,
                                   note: $"{dst.DisplayName} 阵亡（追击）");
                }
            }

            // 19 立冬「水始成冰」：受击时按概率被冻结（**全场** —— GDD 只说"受击时"）
            float freezeChance = w.FreezeOnHitChance;
            if (freezeChance > 0f && dst.IsAlive && !dst.HasStatus(StatusCatalog.Freeze))
            {
                if (st.Random.Chance(freezeChance))
                {
                    dst.ApplyStatus(StatusCatalog.Freeze, 1, w.FreezeOnHitTurns);
                    st.Log.Add(st.Turn, BattleEventKind.StatusApplied, actorId: src.RuntimeId,
                               targetId: dst.RuntimeId, amount: 1,
                               note: $"天时·水始成冰：{dst.DisplayName} 被冻结");
                }
            }
        }

        /// <summary>
        /// 24 大寒「寒气之逆极」下半：火属性技能命中时，移除该单位 2 层冰蚀
        /// 并造成 5% 最大生命的额外伤害 —— 「火能融冰」把五行相克变成看得见的画面。
        /// 取舍：普攻也是技能槽 0，算"火属性技能"（GDD 没区分）；持续伤害不走这里。
        /// public：自检直接调它（DealDamage 是私有的，测试不该为它开洞）。
        /// </summary>
        public static void WeatherIceMelt(BattleState st, BattleUnit src, BattleUnit dst)
        {
            if (st.Weather == null || !st.Weather.IceMeltOnFireSkill) return;
            if (!dst.HasStatus(StatusCatalog.IceErosion)) return;

            dst.RemoveStatusStacks(StatusCatalog.IceErosion, 2);
            int melt = CoreMath.RoundDamage(dst.MaxHp * 0.05f);
            int meltDealt = dst.IsAlive ? dst.TakeTrueDamage(melt) : 0;
            st.Log.Add(st.Turn, BattleEventKind.Damage, actorId: src.RuntimeId,
                       targetId: dst.RuntimeId, amount: meltDealt, element: Element.Fire,
                       note: "天时·火能融冰");
            if (meltDealt > 0 && !dst.IsAlive)
                st.Log.Add(st.Turn, BattleEventKind.Death, targetId: dst.RuntimeId,
                           note: $"{dst.DisplayName} 阵亡（融冰）");
        }

        /// <summary>
        /// 击杀溢出转全队护盾：总量按**存活人数均分**，除不尽的余数给站得最前的那位。
        /// ⚠ GDD 只写"转化为全队护盾"，没说"每人一份"还是"大家分一份" —— 取分一份
        ///   （每人一份会让 5 人队凭空拿到 5 倍护盾，显然过强）。待策划确认。
        /// </summary>
        private static void GrantTeamShieldFromKill(BattleState st, WeatherRuntime w,
                                                    BattleUnit killer, int total)
        {
            int alive = st.AliveCountOf(TeamSide.Player);
            if (alive <= 0 || total <= 0) return;

            int share = total / alive;
            int remainder = total - share * alive;
            var list = st.UnitsOf(TeamSide.Player);
            bool first = true;
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (!u.IsAlive) continue;
                int amount = share + (first ? remainder : 0);
                first = false;
                if (amount <= 0) continue;
                int added = u.AddShield(amount);
                if (added <= 0) continue;
                st.Log.Add(st.Turn, BattleEventKind.Shield, actorId: killer.RuntimeId,
                           targetId: u.RuntimeId, amount: added, element: u.Element,
                           note: "天时·鹰祭而后猎：击杀溢出转护盾");
            }
        }

        /// <summary>
        /// 17 寒露「寒露凝华」：每 N 回合，我方全体获得「凝神」。
        /// 语义取舍：GDD 写"下一次技能 CD 立即减少 2 回合"，这里在**获得时立即扣减**
        /// 当前所有在冷却的技能（对下一次可放的技能等价，且不需要"技能槽级"的钩子）。
        /// 凝神状态本身留作可读凭据（日志/UI 看得到谁拿到了）。
        /// </summary>
        private static void WeatherTurnStartHooks(BattleState st)
        {
            if (st.Weather == null) return;
            int every = st.Weather.HasteEveryNTurns;
            if (every <= 0 || st.Turn % every != 0) return;

            int cdCut = st.Weather.HasteCdReduction;
            var list = st.UnitsOf(TeamSide.Player);
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (!u.IsAlive) continue;
                for (int k = 0; k < u.Cooldowns.Length; k++)
                    if (u.Cooldowns[k] > 0) u.Cooldowns[k] = CoreMath.Max(0, u.Cooldowns[k] - cdCut);
                u.ApplyStatus(StatusCatalog.Haste, 1, 2);
                st.Log.Add(st.Turn, BattleEventKind.StatusApplied, actorId: u.RuntimeId,
                           targetId: u.RuntimeId, amount: 1,
                           note: $"天时·寒露凝华：凝神（技能 CD -{cdCut}）");
            }
        }

        /// <summary>
        /// 23 小寒「寒鸦北去」：每回合结束，我方速度最高的单位获得一次额外普攻。
        /// 取舍：这次普攻**不加怒气、不进冷却**（它是天时给的"白送一击"，不是技能循环的一环）。
        /// 冻结/混乱（不能行动）的单位不给 —— 控制流不该被天时绕过。
        /// </summary>
        private static void WeatherEndExtraActions(BattleState st, Buffers buf)
        {
            if (st.Weather == null || !st.Weather.ExtraBasicAttackOnTurnEnd) return;
            if (st.IsOver) return;

            BattleUnit fastest = null;
            float bestSpeed = 0f;
            var list = st.UnitsOf(TeamSide.Player);
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (!u.IsAlive || !u.CanAct) continue;
                float sp = st.EffectiveSpeed(u);
                if (fastest == null || sp > bestSpeed) { fastest = u; bestSpeed = sp; }
            }
            if (fastest == null) return;

            var basic = fastest.GetSkill(SkillType.Basic);
            if (basic == null || basic.Effects == null || basic.Effects.Length == 0) return;

            st.Log.Add(st.Turn, BattleEventKind.SkillCast, actorId: fastest.RuntimeId,
                       skillName: basic.Name, element: ResolveElement(Element.None, basic, fastest),
                       note: $"天时·寒鸦北去：{fastest.DisplayName} 额外普攻",
                       skill: SkillType.Basic);
            for (int i = 0; i < basic.Effects.Length; i++)
            {
                ResolveAtom(st, fastest, basic, basic.Effects[i], buf);
                if (st.IsOver) break;
            }
        }

        /// <summary>
        /// 「吞噬」劫象（§5.5）：行动结束后剥离对侧 1 个增益（第一个非减益状态），
        /// 自身损失 4% 最大生命（真实伤害）。解法：速攻 —— 别给它行动机会。
        /// </summary>
        private static void DevourAfterAction(BattleState st, BattleUnit actor)
        {
            if (actor == null || !actor.TraitDevour || !actor.IsAlive) return;
            if (!actor.CanAct) return;

            var foes = st.UnitsOf(actor.Side == TeamSide.Player ? TeamSide.Enemy : TeamSide.Player);
            for (int i = 0; i < foes.Count; i++)
            {
                var f = foes[i];
                if (!f.IsAlive) continue;
                for (int k = f.Statuses.Count - 1; k >= 0; k--)
                {
                    if (f.Statuses[k].Def.IsDebuff) continue;
                    var inst = f.Statuses[k];
                    string name = inst.Def.Name;
                    inst.Stacks -= 1;
                    if (inst.Stacks <= 0) f.Statuses.RemoveAt(k);
                    else f.Statuses[k] = inst;
                    st.Log.Add(st.Turn, BattleEventKind.StatusRemoved, actorId: actor.RuntimeId,
                               targetId: f.RuntimeId, note: $"天时·吞噬：剥离 {name}");
                    break;
                }
                break;   // 每次行动只剥一个目标的一个增益
            }

            int self = CoreMath.RoundDamage(actor.MaxHp * 0.04f);
            if (self > 0)
            {
                int dealt = actor.TakeTrueDamage(self);
                st.Log.Add(st.Turn, BattleEventKind.Damage, targetId: actor.RuntimeId,
                           amount: dealt, note: "天时·吞噬：自损");
                if (!actor.IsAlive)
                    st.Log.Add(st.Turn, BattleEventKind.Death, targetId: actor.RuntimeId,
                               note: $"{actor.DisplayName} 阵亡（吞噬自损）");
            }
        }

        /// <summary>
        /// 03 惊蛰「蛰虫始振」/「复苏」劫象共用：虫卵倒计时，到点破卵复活。
        /// ⚠ 复苏劫象的卵**不依赖天时**也能孵（EndOfTurn 的调用条件是"有天时或有卵"）。
        /// </summary>
        private static void TickEggHatch(BattleState st, BattleUnit u)
        {
            if (!u.HasEgg) return;
            u.EggTurnsLeft--;
            if (u.EggTurnsLeft > 0) return;

            int hp = CoreMath.RoundDamage(u.MaxHp * u.EggReviveHpPercent);
            u.ReviveAtHp(hp);
            st.Log.Add(st.Turn, BattleEventKind.Revive, actorId: u.RuntimeId, targetId: u.RuntimeId,
                       amount: u.Hp,
                       note: $"{(u.EggFromTrait ? "劫象·复苏" : "天时·蛰虫始振")}：{u.DisplayName} 破卵而生");
        }

        /// <summary>
        /// 05 清明「桐始华」对"施加状态"的过滤：返回 false = 被免疫。
        /// **技能与天时两条施加路径共用它** —— 分成两份口径迟早会分叉
        /// （出现过"技能被免疫、天时上状态却能上"这类不一致）。
        /// </summary>
        public static bool WeatherFilterStatus(BattleState st, BattleUnit dst, string statusId, ref int turns)
        {
            if (st.Weather == null) return true;
            var def = StatusCatalog.Get(statusId);
            if (!def.IsDebuff) return true;

            if (st.Weather.ImmuneConfuseSilenceFor(dst.Side)
                && (statusId == StatusCatalog.Confuse || statusId == StatusCatalog.Silence))
                return false;

            if (st.Weather.DebuffDurationMinusOneFor(dst.Side))
                turns = CoreMath.Max(1, turns - 1);
            return true;
        }

        /// <summary>
        /// 伤害公式。**唯一实现**，别在别处再拼一次 —— 两处公式迟早会分叉。
        ///
        ///     普通伤害 = 攻击 × 技能倍率 × (1 + 同气) × 五行系数 × (1 + 暴伤若暴击)
        ///                × (1 - 防御减伤) × 受伤乘数
        ///     真实伤害 = 攻击 × 技能倍率 × (1 + 同气)      ← 连减伤与受伤乘数一起跳过
        ///
        /// ⚠ 五行系数**只盖章在伤害上**，相生不参与（GDD 2.3 设计说明）。
        /// ⚠ 无天时路径的浮点运算顺序必须与引入天时前完全一致（指纹红线）；
        ///   天时分支全部包在判空里，乘数默认值恰为 1（IEEE 恒等，不引入舍入）。
        /// </summary>
        public static int ComputeDamage(BattleState st, BattleUnit src, BattleUnit dst,
                                        Element el, float power, bool trueDamage, bool crit,
                                        bool aoeSkill = false)
        {
            var cfg = st.Config;
            float raw = src.Attack * power * (1f + src.QiSkillBonus(cfg.QiEffectPerStack));
            if (trueDamage) return CoreMath.RoundDamage(raw);

            float v = raw * ElementMatrix.Coefficient(el, dst.Element, cfg.Elements);
            if (crit) v *= (1f + src.Def.CritDamage);

            float defense = dst.Def.BaseDef;
            float mitigation = defense / (defense + cfg.DefenseConstant);
            v *= (1f - mitigation);
            v *= dst.DamageTakenMultiplier;

            // ---- 天时修正（GDD 3.1/3.4）。st.Weather 为 null 时零改动 ⇒ 指纹不变 ----
            if (st.Weather != null)
            {
                // 全场伤害乘数（夏至「极阳」：造成的与受到的同时 +25%）
                v *= st.Weather.DamageAllMultiplier;

                // 五行伤害乘数（大暑火 -30% 土 +30%、祷雨火 -20%、祈晴火 +25%）
                v *= st.Weather.ElementDamageMul(el);

                // 技能形态乘数（秋分：AOE -40%、单体 +25%）
                if (st.Weather.AoeDamageMul != 1f || st.Weather.SingleDamageMul != 1f)
                    v *= st.Weather.FormDamageMul(aoeSkill);

                // 暴伤加成（立秋/白露）：GDD"暴击伤害 +40%"＝ 最终暴伤倍率相加
                //（150% + 40% = 190%），不是 (1+暴伤)×(1+加成) 的连乘。
                // 上面的暴击已按 (1+暴伤) 乘过一次，这里先把除回来再加到位。
                // ⚠ bonus == 0 时不动 v —— 保证夏至这类无暴伤修正的天时路径逐位不变。
                if (crit)
                {
                    float bonus = st.Weather.CritDamageBonusFor(src.Side);
                    if (bonus != 0f)
                    {
                        v /= (1f + src.Def.CritDamage);
                        v *= (1f + src.Def.CritDamage + bonus);
                    }
                }

                // 首回合先手方伤害乘数（冬至 +50%）
                float firstTurn = st.Weather.FirstTurnDamageMulFor(st, src.Side);
                if (firstTurn != 1f) v *= firstTurn;

                // 逆天时反噬：覆盖天时的属性被节气相克时，我方该属性单位 +15% 承伤
                var w = st.Weather;
                if (w.BacklashActive && dst.Side == TeamSide.Player && dst.Element == w.BacklashElement)
                    v *= (1f + st.Config.BacklashExtraDamage);   // 劫律 03「逆天之罚」可调
            }

            // 抖动（GDD v1.1 §3.1：Rand ∈ [0.95, 1.05]）。
            // 只在这里消费一次随机数 ⇒ 同种子逐位可复现；DamageJitter=0 时完全不掷骰
            //（对照实验用：证明其他结算路径没有被抖动污染）。
            float jitter = st.Config.DamageJitter;
            if (jitter > 0f)
                v *= 1f + (st.Random.NextFloat() - 0.5f) * 2f * jitter;

            return CoreMath.RoundDamage(v);
        }

        /// <summary>技能的五行归属：原子覆盖 &gt; 技能自身 &gt; 施法者五行。</summary>
        public static Element ResolveElement(Element atomOverride, SkillDef skill, BattleUnit src)
        {
            if (atomOverride != Element.None) return atomOverride;
            if (skill != null && skill.Element != Element.None) return skill.Element;
            return src.Element;
        }

        /// <summary>
        /// 原子的目标形状是不是 AOE（秋分/移山这类"按形态修正"的判定口径）：
        /// 一口气打多个的算 AOE；单体（含多段随机——每段重新抽一个目标，手感是连打而非一锅端）算单体。
        /// </summary>
        public static bool IsAoeTarget(TargetSelector target)
            => target == TargetSelector.AllEnemies
            || target == TargetSelector.AllAllies
            || target == TargetSelector.AllOthers;

        private static string BuildDamageNote(BattleState st, BattleUnit src, BattleUnit dst,
                                              Element el, bool crit, bool trueDamage, int dealtToHp)
        {
            var sb = new System.Text.StringBuilder();
            if (trueDamage) sb.Append("真实伤害");
            else
            {
                var rel = ElementMatrix.Relation(el, dst.Element);
                if (rel != ElementRelation.Neutral)
                {
                    sb.Append(Cn.Of(rel)).Append(" ×").Append(
                        ElementMatrix.Coefficient(el, dst.Element, st.Config.Elements).ToString("F2"));
                }
            }
            if (crit) sb.Append(sb.Length > 0 ? "·暴击" : "暴击");
            if (!trueDamage && dst.Pos.IsCenter) sb.Append(sb.Length > 0 ? "·中宫减伤" : "中宫减伤");
            if (dealtToHp == 0 && dst.IsAlive) sb.Append(sb.Length > 0 ? "·全被护盾吸收" : "全被护盾吸收");
            return sb.ToString();
        }

        // ================================================================
        //  目标选择
        // ================================================================

        private const int PickLowestHp = 1;
        private const int PickHighestHp = 2;
        private const int PickHighestAtk = 3;

        private static void ResolveTargets(BattleState st, BattleUnit src, TargetSelector sel,
                                           TeamSide pool, List<BattleUnit> into)
        {
            into.Clear();
            switch (sel)
            {
                case TargetSelector.Self:
                    into.Add(src);
                    break;

                case TargetSelector.AllEnemies:
                    st.CollectAlive(BattleState.Opponent(src.Side), into);
                    break;

                case TargetSelector.AllAllies:
                    st.CollectAlive(src.Side, into);
                    break;

                case TargetSelector.AllOthers:
                    st.CollectAliveAll(into);
                    for (int i = into.Count - 1; i >= 0; i--)
                        if (ReferenceEquals(into[i], src)) into.RemoveAt(i);
                    break;

                case TargetSelector.AdjacentToSelf:
                {
                    if (!src.Pos.IsValid) break;
                    var slots = st.SlotsOf(src.Side);
                    var pairs = BoardLayout.AdjacentPairs;   // 直接扫 12 对，比手写"上下左右"更不容易错
                    for (int k = 0; k < pairs.Length; k++)
                    {
                        int i = pairs[k][0], j = pairs[k][1];
                        int other;
                        if (i == src.Pos.Index) other = j;
                        else if (j == src.Pos.Index) other = i;
                        else continue;
                        var u = slots[other];
                        if (u != null && u.IsAlive) into.Add(u);
                    }
                    break;
                }

                case TargetSelector.SingleLowestHp:
                    PickOne(st, pool, into, PickLowestHp);
                    break;

                case TargetSelector.SingleHighestHp:
                    PickOne(st, pool, into, PickHighestHp);
                    break;

                case TargetSelector.SingleHighestAtk:
                    PickOne(st, pool, into, PickHighestAtk);
                    break;

                // ---- 按站位选目标：自包含实现（需要 src 参与比较，PickOne 的 mode 传不了）----
                case TargetSelector.SingleFrontMost:
                    PickByRank(st, pool, into, src, true);
                    break;

                case TargetSelector.SingleBackMost:
                    PickByRank(st, pool, into, src, false);
                    break;

                case TargetSelector.RandomEnemy:
                case TargetSelector.RandomEnemyMultiHit:
                {
                    st.CollectAlive(BattleState.Opponent(src.Side), into);
                    if (into.Count == 0) break;
                    var pick = into[st.Random.NextInt(0, into.Count)];
                    into.Clear();
                    into.Add(pick);
                    break;
                }

                default:
                    break;
            }
        }

        /// <summary>
        /// 选一个目标。平局按站位索引从左到右，再按阵营、实例 id ——
        /// 与出手序列同一套全序，保证"生命值一样时打谁"不会因运行而变。
        /// </summary>
        /// <summary>
        /// 按站位选一个目标（v2.1 回合制 P4）。
        /// <para>
        /// ⚠ 判据用 **Row 大小**，不是"与施法者的行距" —— 实测发现双方**共用同一套格位**
        /// （Player/Enemy 都用 BattleRequest.Cells），行距恒为 0，用它判不出前后排。
        /// 项目已有明确语义：<c>BattleStage2D</c> 里 <c>sortingOrder = Pos.Index / 3</c>
        /// 且注释写明「row 0=后 1=中 2=前」，双方镜像绘制 ⇒ **Row 大 = 前排**，双方对称。
        /// </para>
        /// </summary>
        private static void PickByRank(BattleState st, TeamSide side, List<BattleUnit> into,
                                       BattleUnit src, bool frontMost)
        {
            st.CollectAlive(side, into);
            if (into.Count <= 1) return;

            int best = 0;
            for (int i = 1; i < into.Count; i++)
                if (RanksCloser(into[i], into[best], src, frontMost)) best = i;

            var pick = into[best];
            into.Clear();
            into.Add(pick);
        }

        private static bool RanksCloser(BattleUnit a, BattleUnit b, BattleUnit src, bool frontMost)
        {
            int ra = a.Pos.IsValid ? a.Pos.Row : -1;
            int rb = b.Pos.IsValid ? b.Pos.Row : -1;
            if (ra != rb) return frontMost ? ra > rb : ra < rb;   // 前排 = Row 大；后排 = Row 小

            // 同排：列小的先（左侧优先，稳定），再取生命比例高的（更像"挡在前面"的）
            int ca = a.Pos.IsValid ? a.Pos.Col : int.MaxValue;
            int cb = b.Pos.IsValid ? b.Pos.Col : int.MaxValue;
            if (ca != cb) return ca < cb;
            if (a.HpPercent != b.HpPercent) return a.HpPercent > b.HpPercent;
            return true;
        }

        private static void PickOne(BattleState st, TeamSide side, List<BattleUnit> into, int mode)
        {
            st.CollectAlive(side, into);
            if (into.Count <= 1) return;

            int best = 0;
            for (int i = 1; i < into.Count; i++)
                if (Better(into[i], into[best], mode)) best = i;

            var pick = into[best];
            into.Clear();
            into.Add(pick);
        }

        private static bool Better(BattleUnit a, BattleUnit b, int mode)
        {
            if (mode == PickLowestHp)
            {
                if (a.HpPercent < b.HpPercent) return true;
                if (a.HpPercent > b.HpPercent) return false;
            }
            else if (mode == PickHighestHp)
            {
                if (a.HpPercent > b.HpPercent) return true;
                if (a.HpPercent < b.HpPercent) return false;
            }
            else if (mode == PickHighestAtk)
            {
                if (a.Attack > b.Attack) return true;
                if (a.Attack < b.Attack) return false;
            }

            int pa = a.Pos.IsValid ? a.Pos.Index : BoardLayout.CellCount;
            int pb = b.Pos.IsValid ? b.Pos.Index : BoardLayout.CellCount;
            if (pa != pb) return pa < pb;

            if (a.Side != b.Side) return a.Side < b.Side;

            return string.CompareOrdinal(a.RuntimeId, b.RuntimeId) < 0;
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

        /// <summary>每趟战斗复用的缓冲，避免在热路径里反复分配小 List。</summary>
        private sealed class Buffers
        {
            public readonly List<BattleUnit> Targets = new List<BattleUnit>(8);
            public readonly List<BattleUnit> Order = new List<BattleUnit>(10);
        }
    }
}
