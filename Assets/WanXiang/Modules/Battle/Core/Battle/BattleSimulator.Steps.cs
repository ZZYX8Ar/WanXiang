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
            PassiveHooks.ApplyBattleStart(st);   // v2.1 P3b：开场被动（攻击加成 / 开场回复）
            BattleHooks.RunStart(st);            // 首领战：开场机制（免疫克制 / 双子部署等）// 开局先算一次，让"上阵即共鸣"在第一回合就成立

            int limit = CoreMath.Max(1, cfg.MaxTurns);
            int turn = 1;
            for (; turn <= limit; turn++)
            {
                st.Turn = turn;
                st.Log.Add(turn, BattleEventKind.TurnStart);
                BattleHooks.TurnStart(st);            // 首领战：回合始机制（首击标记清零 / 属性轮转 / 召唤计时）

                // 灵力自然回复（v2.1 §3）：超出上限的部分丢失 —— 逼玩家在回合内花掉
                // ★ 第 1 回合**不回灵**（2026-09-26 用户定案）：
                //   否则玩家一进战斗、还没动手就看见"灵力 2/12"，与"从 0 开始攒"的观感冲突
                //   （实测：初值确实是 0，但 TurnStart 先回灵再等令 ， 玩家第一眼看到 2）。
                //   双方同规则，保持对称。
                if (turn > 1)
                {
                    if (st.TeamMp < st.TeamMpMax)
                        st.TeamMp = System.Math.Min(st.TeamMpMax, st.TeamMp + BattleState.MpRegenPerTurn);
                    // 敌方独立池同节奏回复（双方的灵力互相不干扰）
                    if (st.EnemyMp < st.EnemyMpMax)
                        st.EnemyMp = System.Math.Min(st.EnemyMpMax, st.EnemyMp + BattleState.MpRegenPerTurn);
                }

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

                // ---- 0.5) 敌方残血反扑（2026-10-16，用户："给敌人重新设计得更有趣"）----
                //   敌人血量跌破阈值时永久加攻（一次性），并记一条可读事件：
                //   让"快打死的那只"从威胁变成真正的麻烦，而不是顺势收割。
                //   ⛔ 只判敌方、只触发一次（用 HasEnraged 标记），否则每回合无限叠加。
                EnemyTactics.EnrageWoundedEnemies(st);

                // ---- 1) 回合开始的场地结算（棋盘四条规则） ----
                Accumulate(ref total, BoardRules.ResolveAdjacency(st));
                if (st.CheckOutcome()) break;

                // ---- 持续伤害与「生机」 ----
                TickStatuses(st);
                if (st.CheckOutcome()) break;

                // ---- 2) 出手序列 ----
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
                    // 回合制断点：我方单位行动前把控制权交回调用方（手动模式等玩家下令）。
                    // ⛔ **必须判 `CanAct`**：被冻结/根缚/混乱的单位这回合动不了，
                    //   若还停下来等指令，玩家会被要求给一个"无法行动"的单位下令
                    //   （下令后又会被 ExecuteAction 拒掉、只留一条「无法行动」日志）
                    //   —— 用户实测："已经被冻结了为什么还能行动，玩起来很奇怪"。
                    //   不停 ， 直接走 ExecuteAction 记一条「无法行动」，流程自然跳过。
                    if (st.PlayerControlled && u.Side == TeamSide.Player && u.CanAct)
                        yield return new BattleStep { Kind = BattleStepKind.NeedDecision, Unit = u };

                    // ★★ 阵形连携 QTE（2026-10-16 重构，用户口径）：
                    //   "消耗中间异兽（主兽）的大招能量；根据阵容存活的异兽提升威力；
                    //    每存活一个就要多消耗一个灵力；通过 QTE 判断触发；
                    //    如果触发失败则扣除 1 个灵力和 50% 的主兽大招能量，并提示触发失败。"
                    //   流程：UI 点"连携" → 登记 PendingComboId → 这里弹 N 段窗口 → 命中过半 = ExecuteCombo，
                    //        否则 ComboFailed（扣资源 + 记「触发失败」）。成功则**占用本次行动**。
                    bool comboHandled = false;
                    if (!string.IsNullOrEmpty(st.PendingComboId))
                    {
                        string cid = st.PendingComboId;
                        st.PendingComboId = null;            // 一令一用：无论成败都消费掉
                        st.PendingCommand = default;         // ⛔ 清掉可能残留的技能指令（否则下面的终结技 QTE 可能误触发）
                        var comboDef = ComboRules.For(cid);
                        string whyNot;
                        // ⚠ 也要求 CanAct：被冻结/根缚的单位这回合动不了（与 NeedDecision 同口径）
                        if (comboDef != null && u.CanAct && ComboRules.CanFire(st, u, comboDef, out whyNot))
                        {
                            int segs = ComboRules.QteSegments;
                            int hit = 0, perfect = 0;
                            st.ParryComboName = comboDef.Name;
                            for (int h = 0; h < segs; h++)
                            {
                                st.ParriedGrade = ParryGrade.None;
                                st.ParryActorId = u.RuntimeId;
                                st.ParryQteKind = ParryQteKind.Combo;
                                st.ParrySegmentIndex = h;
                                st.ParrySegmentTotal = segs;
                                st.ParryPendingUnit = u;     // ⚠ 同终结技：UI 靠它取 ParryAttacker（画圈）
                                yield return new BattleStep { Kind = BattleStepKind.NeedParry, Unit = u };

                                if (st.ParriedGrade == ParryGrade.Perfect) { perfect++; hit++; }
                                else if (st.ParriedGrade != ParryGrade.None) hit++;
                            }
                            // ⛔ 先摘掉"判定期"标记再结算：连携 QTE 是**成败闸门**（不是伤害倍率），
                            //   若留着 ParryActorId/ParriedGrade，连携自己的伤害会被
                            //   ParryDamageMulFor 当成"被格挡的那一下"再乘一次（帮倒忙）。
                            st.ParryComboName = null;
                            st.ParryQteKind = ParryQteKind.None;
                            st.ParryActorId = null;
                            st.ParriedGrade = ParryGrade.None;

                            // 命中 >= 一半段数 = 触发成功（3 段需 >= 2 段；全 Perfect 记一条"大成功"）
                            if (hit * 2 >= segs)
                            {
                                ExecuteCombo(st, cid, u);
                                if (perfect == segs)
                                    st.Log.Add(st.Turn, BattleEventKind.ParryResult, actorId: u.RuntimeId,
                                               note: "连携·" + comboDef.Name + " 大成功（全段完美）");
                            }
                            else ComboFailed(st, u, comboDef);
                            comboHandled = true;
                            if (st.IsOver) break;
                        }
                        // CanFire 不过（条件在等令后变了）→ 不消费行动，照常走正常行动
                    }

                    // ★ 终结技 QTE（进攻向，2026-10-14 二轮；2026-10-15 改为**多段按键**）：
                    //   玩家下令放了终结技，在真正打出之前弹输出窗口 —— 完美 ⇒ 伤害加强，没按准 ⇒ 减弱。
                    //   ⛔ 用户 2026-10-15："我方异兽也可以使用这个按键多段判定而不是单单这个空格"
                    //      ⇒ 从「单发空格窗口」改成「N 段按键连按」（段数见 FinisherSegmentCount）。
                    //   ⚠ 读的是 PendingCommand.SkillIndex = SkillType.Ultimate（还没被 ExecuteAction 消费）。
                    bool finisherQte = false;
                    if (st.ParryEnabled && u.Side == TeamSide.Player && u.CanAct
                        && st.PendingCommand.SkillIndex == (int)SkillType.Ultimate
                        && u.GetSkill(SkillType.Ultimate) != null)
                    {
                        finisherQte = true;
                        int finHits = FinisherSegmentCount(u.GetSkill(SkillType.Ultimate));
                        int finPerfect = 0, finNone = 0;
                        for (int h = 0; h < finHits; h++)
                        {
                            st.ParriedGrade = ParryGrade.None;   // 待判定（超时 = 失败 = 减弱）
                            st.ParryActorId = u.RuntimeId;
                            st.ParryQteKind = ParryQteKind.Finisher;
                            st.ParrySegmentIndex = h;
                            st.ParrySegmentTotal = finHits;
                            // ⚠ 终结技窗口**要**设 ParryPendingUnit：BattlePlayback 靠它取 ParryAttacker
                            //   （提示圈要画在我方出手者头上）。格挡窗口由 AdvanceToNextDecision 的 NeedParry 分支统一设。
                            st.ParryPendingUnit = u;
                            yield return new BattleStep { Kind = BattleStepKind.NeedParry, Unit = u };

                            if (st.ParriedGrade == ParryGrade.Perfect) finPerfect++;
                            else if (st.ParriedGrade == ParryGrade.None) finNone++;
                        }
                        // 合并（对玩家宽容，别让一次失误白丢整个终结技）：
                        //   全中 → 完美（×1.60）；全失 → 减弱（×0.45）；其余 → 原样（×1.0）。
                        st.ParriedGrade = finPerfect == finHits ? ParryGrade.Perfect
                                        : finNone == finHits ? ParryGrade.None
                                        : ParryGrade.Normal;
                    }

                    // ★ 格挡窗口（防守向，QTE）：敌方"带伤害的行动"前，等玩家提交判定。
                    //   st.ParryEnabled 默认 false ⇒ 不 yield、零事件、指纹逐位不变（自检红线）。
                    //
                    //   ★★ 多段连击 = **边判断边攻击**（2026-10-15 用户报障修正）：
                    //   "敌人的多段攻击不是多次判断啊，你判断多次就攻击了一次，
                    //    应该是边判断边攻击"。旧实现是"先连排 N 个窗口、再一次性 ExecuteAction"
                    //   ⇒ 观感是"判了 3 次只挨了 1 下"。
                    //   新实现用 ParryDamageSegment 把一次行动切成 N 段执行：
                    //     头部(ActionBegin+SkillCast) → [窗口→打第 h 段] × N → 尾部(ActionEnd)
                    //   ⚠ 头部刻意放在窗口**之前**：先看到"敌人出手"，再弹格挡窗，因果顺序才对。
                    bool parryOpened = false;
                    int comboHits = 1;
                    bool parrySegmented = false;
                    if (st.ParryEnabled && !finisherQte && u.Side == TeamSide.Enemy && u.CanAct
                        && PeekActionIsDamaging(st, u))
                    {
                        parryOpened = true;
                        comboHits = PeekActionHitCount(st, u);
                        // ★ FoeKit（2026-10-16）：段数由**招式谱系配置**决定，不是原子 Hits。
                        //   没配 FoeKit 时 SegmentCountFor 返回 fallback（-1）⇒ 沿用旧口径。
                        int foeSegs = FoeKitRuntime.SegmentCountFor(u, -1);
                        if (foeSegs > 0) comboHits = foeSegs;
                        parrySegmented = comboHits > 1;
                    }

                    if (parrySegmented)
                    {
                        var comboSkill = ExecuteActionHead(st, u);
                        if (comboSkill != null)
                        {
                            var comboWorst = ParryGrade.Perfect;
                            for (int h = 0; h < comboHits; h++)
                            {
                                // ★ 起手预警（2026-10-16 FoeKit）：**每段之前**都发一次，
                                //   玩家看色块就知道这招该不该按 —— 这是「可预判」的落地处。
                                //   ⛔ 朴素敌人无FoeKit ⇒ 内部直接 return，不产生任何事件。
                                FoeKitRuntime.EmitWindup(st, u, h, comboHits);
                                st.ParriedGrade = ParryGrade.None;   // 待判定（超时/异常都落到"未挡"）
                                st.ParryActorId = u.RuntimeId;
                                st.ParryQteKind = ParryQteKind.Guard;
                                st.ParrySegmentIndex = h;
                                st.ParrySegmentTotal = comboHits;    // UI 显示"第 k/n 段"
                                // ⚠ 必须设 ParryPendingUnit，否则 UI 侧 `ApplyParry` 第一行就 return，
                                //   档位永远停在上面那个 None ⇒ 减伤完全不生效（实测踩过）。
                                st.ParryPendingUnit = u;
                                yield return new BattleStep { Kind = BattleStepKind.NeedParry, Unit = u };

                                AccumulateParryComboSegment(ref comboWorst, st.ParriedGrade);

                                // ★ 本段档位**立刻**作用于本段伤害（不再等整次行动合并）
                                st.ParryDamageSegment = h;
                                ResolveDamageAtomsOnly(st, u, comboSkill, buf);
                                st.ParryDamageSegment = -1;
                                if (st.IsOver) break;
                            }
                            st.ParriedGrade = comboWorst;    // 收口（反震判定 + ParryResult Note）用最差档
                            // ⛔ 伤害段打完就摘掉"判定期"标记：否则下面那些非伤害原子
                            //   （附加减益等）会被 ParryDamageMulFor 当成"被格挡的伤害"再乘一次。
                            st.ParryActorId = null;
                            st.ParryQteKind = ParryQteKind.None;
                            // 非伤害原子（减益/增益等）只结算一次：放在所有段之后
                            ResolveNonDamageAtomsOnly(st, u, comboSkill, buf);
                            ExecuteActionTail(st, u, comboSkill);
                        }
                    }
                    else
                    {
                    if (parryOpened)
                    {
                        // ★ 起手预警（单段路径）。⛔ 朴素敌人无事件，指纹不变。
                        FoeKitRuntime.EmitWindup(st, u, 0, 1);
                        st.ParriedGrade = ParryGrade.None;
                        st.ParryActorId = u.RuntimeId;
                        st.ParryQteKind = ParryQteKind.Guard;
                        st.ParrySegmentIndex = 0;
                        st.ParrySegmentTotal = 1;
                        // ⚠ 同上的 ParryPendingUnit 纪律。
                        st.ParryPendingUnit = u;
                        yield return new BattleStep { Kind = BattleStepKind.NeedParry, Unit = u };
                    }

                    // ★ 阵形连携消费本次行动（成败都算）：
                    //   玩家已经为这次连携做完了 3 段 QTE ⇒ 再回头让他重选技能会很别扭；
                    //   "发动连携（替代该单位本次的普攻/战记）"也是既有口径。
                    //   失败的代价是资源（1 灵力 + 50% 大招能量，见 ComboFailed），行动同样算用过。
                    if (!comboHandled)
                        ExecuteAction(st, u, buf);
                    }   // end else（单段 / 非多段）

                    // ★ 格挡收口：完美 → 反震；发结果事件；清状态（下次行动重新开窗）。
                    if (parryOpened)
                    {
                        if (st.ParriedGrade == ParryGrade.Perfect && u.IsAlive)
                        {
                            // ★ 破绽·格挡型（2026-10-16 FoeKit）：白刃「刃口」这种
                            //   "格挡 N 次"的破绽 —— **防御行为本身就是进攻路线**。
                            //⛔ 只在配了 FoeKit 的敌人才进函数体（内部按 trigger 短路）。
                            FoeKitRuntime.OnParriedBy(st, u);
                            int back = CoreMath.RoundDamage(u.MaxHp * ParryCounterMaxHpPercent);
                            if (back > 0)
                            {
                                int dealtBack = u.TakeTrueDamage(back);
                                st.Log.Add(st.Turn, BattleEventKind.Damage, actorId: u.RuntimeId,
                                           targetId: u.RuntimeId, amount: dealtBack, note: "完美格挡·反震");
                                if (dealtBack > 0 && !u.IsAlive)
                                {
                                    st.Log.Add(st.Turn, BattleEventKind.Death, targetId: u.RuntimeId,
                                               note: $"{u.DisplayName} 被反震击倒");
                                    if (st.Hooks.Count > 0) BattleHooks.UnitKilled(st, null, u);
                                }
                            }
                        }
                        st.Log.Add(st.Turn, BattleEventKind.ParryResult, actorId: u.RuntimeId,
                                   note: comboHits > 1
                                        ? GradeCn(st.ParriedGrade) + "·连挡" + comboHits + "段"
                                        : GradeCn(st.ParriedGrade));
                        st.ParriedGrade = ParryGrade.None;
                        st.ParryActorId = null;
                        st.ParryQteKind = ParryQteKind.None;
                        if (st.CheckOutcome()) break;
                    }

                    // ★ 终结技 QTE 收口：只发结果事件 + 清状态（无反震，加强/减弱已过伤害）。
                    //   ⚠ 多段后合出来的档位有三档，Note 要分三档说清楚（别把"按中一半"也叫失谐）。
                    if (finisherQte)
                    {
                        string finNote = st.ParriedGrade == ParryGrade.Perfect ? "共鸣·强化"
                                       : st.ParriedGrade == ParryGrade.None ? "失谐·减弱"
                                       : "共鸣";
                        st.Log.Add(st.Turn, BattleEventKind.ParryResult, actorId: u.RuntimeId,
                                   note: "终结技·" + finNote);
                        st.ParriedGrade = ParryGrade.None;
                        st.ParryActorId = null;
                        st.ParryQteKind = ParryQteKind.None;
                    }

                    DevourAfterAction(st, u);      // 「吞噬」劫象：行动结束剥离对侧 1 增益 + 自损
                    if (st.CheckOutcome()) break;

                    // ★ 额外行动（溟鲲「深潜」等阶段技）：标志由钩子在回合始置位，这里消费并清零。
                    //   ⛔ 必须走**同一个 ExecuteAction** —— 另写一套行动逻辑必然与常规规则漂移。
                    if (u.ExtraActionPending)
                    {
                        u.ExtraActionPending = false;
                        st.Log.Add(st.Turn, BattleEventKind.ActionBegin, actorId: u.RuntimeId, note: "额外行动");
                        ExecuteAction(st, u, buf);
                        DevourAfterAction(st, u);
                        if (st.CheckOutcome()) break;
                    }
                }
                if (st.IsOver) break;

                // （先手连击的额外行动已改为"追加进行动序列"，见上方 §2，不再在回合末直接 ExecuteAction）

                // ---- 4) 回合末（天时·回合末在 EndOfTurn 之后、TurnEnd 事件之前） ----
                EndOfTurn(st);
                PassiveHooks.ApplyTurnEnd(st);     // v2.1 P3b：回合末被动（回复类）
                // ★ 敌方失衡回合递减 + 血量型破绽检查（2026-10-16 FoeKit）。
                //   ⛔ 无 FoeKit 敌方时内部 for 循环每个都直接 continue ⇒ 零事件、指纹不变。
                FoeKitRuntime.TickUnbalanced(st);
                // ★ 死亡对账：把本回合内"被分摊/反弹/连携/天时/棋盘结算静默扣血致死"的
                //   单位补报到钩子系统（否则双子被共鸣分摊磨死时 KillLinkHook 收不到信号）。
                //   必须在 TurnEnd 之前 —— 双子复活窗口按 _deadTurn 起算，先登记再判窗口。
                BattleHooks.SweepDeaths(st);
                BattleHooks.TurnEnd(st);           // 首领战：回合末机制（硬性DPS灭团 / 双子复活判定 / 组装成长）
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
                    st.ParryPendingUnit = null;
                    st.PendingUnit = step.Unit;
                    return true;
                }
                if (step.Kind == BattleStepKind.NeedParry)
                {
                    // ★ 格挡窗口（QTE）：等待 ApplyParry 提交判定（UI 侧带着时机圈时限）
                    st.PendingUnit = null;
                    st.ParryPendingUnit = step.Unit;
                    return true;
                }
                if (step.Kind == BattleStepKind.Finished)
                {
                    st.Result = step.Result;
                    st.PendingUnit = null;
                    st.ParryPendingUnit = null;
                    return false;
                }
            }
            st.PendingUnit = null;
            st.ParryPendingUnit = null;
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

                // 护盾一个回合后自动消失（用户 2026-10-13）：本回合始把**上一回合及更早**获得的旧盾清掉。
                //   ShieldTurn 记录"第几回合获得"，ShieldTurn < st.Turn ⇒ 已撑过至少一整回合 → 消退。
                //   （Boss 破壳盾在 OnTurnStart 每回合重发、ShieldTurn=当前回合，天然不会被误清。）
                if (u.Shield > 0 && u.ShieldTurn > 0 && u.ShieldTurn < st.Turn)
                {
                    int gone = u.ClearShield();
                    st.Log.Add(st.Turn, BattleEventKind.RoundResolve, targetId: u.RuntimeId,
                               note: $"{u.DisplayName} 的护盾（{gone}）已消退");
                }

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
                        // ★ DoT 致死也必须派发 UnitKilled 钩子：双子同命 / 假死 / 击杀成长 /
                        //   共享生命等都挂在 OnUnitKilled 上。之前只写 Death 事件 ， 钩子收不到
                        //   阵亡信号，双子被持续伤害跳死时不会进复活窗口、也没"倒下"提示
                        //   （用户 2026-10-12 报：第6回合死、第11回合仍未复活且全程无提示）。
                        //   DoT 无明确施法者，killer 传 null（现有钩子体均不依赖 killer 参数）。
                        if (st.Hooks.Count > 0) BattleHooks.UnitKilled(st, null, u);
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
