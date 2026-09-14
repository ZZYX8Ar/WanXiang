// ============================================================================
//  万相 · 战斗核心 · 棋盘规则结算
//  ---------------------------------------------------------------------------
//  对应 GDD 2.5 的四条棋盘规则。它们是本作相对同类产品**唯一的机制增量**，
//  所以单独一个文件、单独一次调用，不和技能结算揉在一起 ——
//  这样"相邻格到底怎么算"永远只有一处答案。
//
//  规则一的结算时机说明：
//    GDD 2.5 正文写的是「双方每回合**开始**时回复 3% 最大生命」，
//    而 GDD 6.2 的回合心跳伪代码把 ResolveAdjacency 放在**回合末**。
//    两处不一致，**取正文（回合开始）** —— 正文是规则定义，伪代码只是示意结构。
//
//  遍历顺序是语义的一部分（GDD 7 章验收①）：
//    外循环 = 我方 → 敌方（固定）
//    内循环 = BoardLayout.AdjacentPairs 的 12 对（生成时即按索引升序，固定）
//    格对内部 = 先 [0] 后 [1]
//  三个层次都固定，所以整段结算是纯确定性的函数。
// ============================================================================

namespace WanXiang.Battle.Core
{
    /// <summary>一次棋盘结算的产出统计。给自检报告用 —— 没有它就没法回答"相克相冲真的发生了吗"。</summary>
    public struct BoardRoundReport
    {
        public int GeneratePairs;      // 触发相生相邻的格对数
        public int CounterPairs;       // 触发相克相冲的格对数
        public int PacifiedPairs;      // 被中宫土位平息掉的相冲格对数
        public int RegenTotal;         // 相生回复的生命总量
        public int TrueDamageTotal;    // 相冲造成的真实伤害总量
        public int QiGranted;          // 发放的「同气」层数

        public bool AnythingHappened => GeneratePairs > 0 || CounterPairs > 0 || PacifiedPairs > 0;

        public override string ToString() =>
            $"相生 {GeneratePairs} 对（回复 {RegenTotal}）／相克 {CounterPairs} 对" +
            $"（真伤 {TrueDamageTotal}）／中宫平息 {PacifiedPairs} 对／同气 +{QiGranted} 层";
    }

    public static class BoardRules
    {
        /// <summary>
        /// 回合开始的棋盘结算。调用点：BattleSimulator 每回合第一步。
        /// </summary>
        public static BoardRoundReport ResolveAdjacency(BattleState st)
        {
            var cfg = st.Config;
            var report = new BoardRoundReport();

            // ---------------------------------------------------------------
            //  0) 怒气乘数每回合从零重算
            //  ---------------------------------------------------------------
            //  「相克相冲使双方怒气获取 -10%」是一条**持续性**影响，但它的成因
            //  （挨着）每回合都可能变。做成"每次结算时重算"而不是"增减一个修正值"，
            //  好处是不会因为回退、复活、换位而残留乘数 —— 残留乘数是那种
            //  查半天最后发现"某个 0.9 没还原"的 bug。
            var all = st.AllUnits;
            for (int i = 0; i < all.Count; i++) all[i].RageGainMultiplier = 1f;

            // ---------------------------------------------------------------
            //  1) / 2) / 3) 相生相邻 · 相克相冲 · 中宫土位
            // ---------------------------------------------------------------
            for (int s = 0; s < 2; s++)
            {
                var side = (TeamSide)s;
                var slots = st.SlotsOf(side);
                var pairs = BoardLayout.AdjacentPairs;

                for (int k = 0; k < pairs.Length; k++)
                {
                    var a = slots[pairs[k][0]];
                    var b = slots[pairs[k][1]];
                    if (a == null || b == null) continue;
                    // 中途阵亡的单位立刻退出后续格对 —— 否则会出现"尸体还在给同气"
                    if (!a.IsAlive || !b.IsAlive) continue;

                    // ---- 规则 1：相生相邻 ----
                    // 相生是有方向的（木生火），但"两个单位相生"这件事是对称的，
                    // 所以两个方向都要试。
                    bool generate = ElementMatrix.Generates(a.Element, b.Element)
                                 || ElementMatrix.Generates(b.Element, a.Element);
                    if (generate)
                    {
                        report.GeneratePairs++;
                        report.RegenTotal += Regen(st, a, cfg);
                        report.RegenTotal += Regen(st, b, cfg);
                        report.QiGranted += GrantQi(st, a, cfg);
                        report.QiGranted += GrantQi(st, b, cfg);
                        continue;
                    }

                    // ---- 规则 2：相克相冲 ----
                    bool counter = ElementMatrix.Counters(a.Element, b.Element)
                                || ElementMatrix.Counters(b.Element, a.Element);
                    if (!counter) continue;

                    // ---- 规则 3：中宫土位（平息） ----
                    // GDD 2.5：「相邻四格的单位（无论什么属性）都不会触发相冲」。
                    // 那四格彼此并不正交相邻，唯一可能与它们相邻的就是中宫本身
                    // ⇒ 这条规则的实际作用面只能是"涉及中宫的相冲被平息"。
                    // 正文的举例也印证了这个读法：「把矛盾的属性放在中宫旁边，冲突被平息」。
                    if (cfg.CenterSuppressesAdjacentCounter && (a.Pos.IsCenter || b.Pos.IsCenter))
                    {
                        report.PacifiedPairs++;
                        st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                            actorId: a.RuntimeId, targetId: b.RuntimeId,
                            note: $"中宫土位平息相冲（{Cn.Of(a.Element)}↔{Cn.Of(b.Element)}）");
                        continue;
                    }

                    report.CounterPairs++;
                    report.TrueDamageTotal += Conflict(st, a, cfg);
                    report.TrueDamageTotal += Conflict(st, b, cfg);
                }
            }

            // ---------------------------------------------------------------
            //  4) 同属共鸣
            // ---------------------------------------------------------------
            ApplyResonance(st);

            if (report.AnythingHappened)
            {
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve, note: report.ToString());
            }
            return report;
        }

        /// <summary>相生相邻的回复。3% 最大生命，取整走项目唯一的取整入口。</summary>
        private static int Regen(BattleState st, BattleUnit u, BattleConfig cfg)
        {
            int amount = CoreMath.RoundDamage(u.MaxHp * cfg.AdjacencyGenerateRegenPercent);
            int healed = u.Heal(amount);
            st.Log.Add(st.Turn, BattleEventKind.Heal, actorId: u.RuntimeId, targetId: u.RuntimeId,
                       amount: healed, element: u.Element, note: "相生相邻");
            return healed;
        }

        /// <summary>相生相邻的「同气」。层数上限由 StatusDef.MaxStacks 管（5 层）。</summary>
        private static int GrantQi(BattleState st, BattleUnit u, BattleConfig cfg)
        {
            int before = u.GetStacks(StatusCatalog.Qi);
            u.ApplyStatus(StatusCatalog.Qi, cfg.AdjacencyGenerateQiPerTurn, cfg.QiDurationTurns);
            int gained = u.GetStacks(StatusCatalog.Qi) - before;
            if (gained > 0)
            {
                st.Log.Add(st.Turn, BattleEventKind.StatusApplied, actorId: u.RuntimeId,
                           targetId: u.RuntimeId, amount: gained,
                           note: $"同气 ×{u.GetStacks(StatusCatalog.Qi)}");
            }
            return gained;
        }

        /// <summary>
        /// 相克相冲：2% 最大生命的真实伤害（无视护盾与减伤）+ 怒气获取 -10%。
        /// 返回实际造成的伤害。
        /// </summary>
        private static int Conflict(BattleState st, BattleUnit u, BattleConfig cfg)
        {
            int amount = CoreMath.RoundDamage(u.MaxHp * cfg.AdjacencyCounterTrueDamagePercent);
            int dealt = u.TakeTrueDamage(amount);
            u.RageGainMultiplier *= (1f + cfg.AdjacencyCounterRageDelta);

            st.Log.Add(st.Turn, BattleEventKind.Damage, actorId: u.RuntimeId, targetId: u.RuntimeId,
                       amount: dealt, element: u.Element, note: "相克相冲·真实伤害");

            if (dealt > 0 && !u.IsAlive)
            {
                st.Log.Add(st.Turn, BattleEventKind.Death, targetId: u.RuntimeId,
                           note: "相冲致死（内耗）");
            }
            return dealt;
        }

        /// <summary>
        /// 同属共鸣：2 / 4 / 5 只同属性 → 该属性单位 +8% / +16% / +25% 攻击，
        /// 4 只解锁共鸣技，5 只共鸣技 CD -2（STEP 1 暂落在绝技上，见 BattleUnit.ResonanceCdDelta）。
        ///
        /// 每回合重算而不是开局算一次：口径可切换到"只数存活"（见 BattleConfig），
        /// 而且单位阵亡、五行被融合改写之后，缓存值必然过期。这个循环只有 5×5 次，
        /// 不值得为它引入"什么时候该标脏"这类问题。
        /// </summary>
        public static void ApplyResonance(BattleState st)
        {
            var tiers = BattleConfig.ResonanceTiers;
            bool aliveOnly = st.Config.ResonanceCountsAliveOnly;

            for (int s = 0; s < 2; s++)
            {
                var side = (TeamSide)s;
                var list = st.UnitsOf(side);
                for (int i = 0; i < list.Count; i++)
                {
                    var u = list[i];
                    int n = st.CountOfElement(side, u.Element, aliveOnly);

                    float atk = 0f;
                    int cdDelta = 0;
                    bool unlocked = false;
                    int bestCount = -1;
                    for (int t = 0; t < tiers.Length; t++)
                    {
                        if (n < tiers[t].Count) continue;
                        if (tiers[t].Count <= bestCount) continue;
                        bestCount = tiers[t].Count;
                        atk = tiers[t].AttackBonus;
                        cdDelta = tiers[t].ResonanceCdDelta;
                        unlocked = tiers[t].UnlockResonanceSkill;
                    }

                    u.ResonanceAttackBonus = atk;
                    u.ResonanceCdDelta = cdDelta;
                    u.ResonanceUnlocked = unlocked;
                }
            }
        }

        /// <summary>同属共鸣的文本描述，给灰盒报告用。</summary>
        public static string DescribeResonance(BattleState st)
        {
            var sb = new System.Text.StringBuilder();
            for (int s = 0; s < 2; s++)
            {
                var side = (TeamSide)s;
                sb.Append($"  【{Cn.Of(side)}】");
                bool first = true;
                foreach (var e in ElementMatrix.All)
                {
                    int n = st.CountOfElement(side, e, false);
                    if (n < 2) continue;
                    sb.Append(first ? " " : "，");
                    first = false;
                    sb.Append($"{Cn.Of(e)}×{n}");
                }
                if (first) sb.Append(" 无共鸣");
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
