// ============================================================================
//  万相 · 一局 → 局外结算（GDD 1.3「死亡即结束，返回局外孵蛋与图鉴」）
//  ---------------------------------------------------------------------------
//  把 RunDriver 跑完的一局换算成局外收益：灵卵 + 计数（局数/通关数/最远幕）。
//
//  收尾口径：**败北也给灵卵**（打过的节点都算），只是没有通关奖励 ——
//  肉鸽的"再来一局"必须靠局外有积累，输光了就什么都不给会把人赶走。
//
//  ⚠ 结算函数**纯**：只读 driver、只写 state，不碰战斗、不掷随机
//    ⇒ 自检可以"同记录算两次"做对照，也可以离线重放复盘。
// ============================================================================

using WanXiang.Campaign;

namespace WanXiang.Meta
{
    public static class MetaRewards
    {
        /// <summary>一局的收益明细（用于窗口展示"这一局赚了多少"）。</summary>
        public struct RunIncome
        {
            public int NodeEggs;      // 普通节点
            public int BossEggs;      // 守关
            public int ClearBonus;    // 通关额外
            public int NewActBonus;   // 首次抵达新幕
            public int Total;
            public bool Cleared;
            public int ActReached;
            public int Battles;

            public string Describe()
                => $"灵卵 +{Total}（节点 {NodeEggs} + 守关 {BossEggs}"
                 + (ClearBonus > 0 ? $" + 通关 {ClearBonus}" : "")
                 + (NewActBonus > 0 ? $" + 新幕 {NewActBonus}" : "")
                 + $"，共 {Battles} 战，抵达第 {ActReached} 幕）";
        }

        /// <summary>
        /// 只算收益、不改存档 —— **纯计数版**：不依赖 RunDriver，窗口预览、
        /// 自检对照、离线复盘（只有一份战斗记录文本）都能用同一个口径。
        /// </summary>
        public static RunIncome IncomeOf(MetaState state, int nodes, int bosses,
                                         bool cleared, int actReached)
        {
            var income = new RunIncome
            {
                NodeEggs = nodes * MetaDefaults.EggsPerNode,
                BossEggs = bosses * MetaDefaults.EggsPerBoss,
                Cleared = cleared,
                ActReached = actReached,
                Battles = nodes + bosses,
            };
            if (cleared) income.ClearBonus = MetaDefaults.EggsPerClear;

            // 首次抵达新幕才给（避免"刷低幕"变成刷分）
            if (state != null && actReached > state.BestActReached)
                income.NewActBonus = (actReached - state.BestActReached) * MetaDefaults.EggsPerNewAct;

            income.Total = income.NodeEggs + income.BossEggs + income.ClearBonus + income.NewActBonus;
            return income;
        }

        /// <summary>从一局的推进器读计数（RunDriver 的 Outcome 只读 ⇒ 计数从 Records 里数）。</summary>
        public static RunIncome IncomeOf(RunDriver driver, MetaState state)
        {
            int nodes = 0, bosses = 0, actReached = 1;
            for (int i = 0; i < driver.Records.Count; i++)
            {
                var r = driver.Records[i];
                if (r.Act > actReached) actReached = r.Act;
                if (r.IsBoss) bosses++; else nodes++;
            }
            bool cleared = driver.Outcome == RunOutcome.Completed;
            return IncomeOf(state, nodes, bosses, cleared, actReached);
        }

        /// <summary>按计数结算并写进存档（无 driver 的场景，如"导入的战斗记录"）。</summary>
        public static RunIncome Settle(MetaState state, int nodes, int bosses,
                                       bool cleared, int actReached)
        {
            var income = IncomeOf(state, nodes, bosses, cleared, actReached);
            state.RunsPlayed++;
            if (cleared) state.RunsCompleted++;
            if (actReached > state.BestActReached) state.BestActReached = actReached;
            state.Eggs += income.Total;
            return income;
        }

        /// <summary>结算一局并写进存档：灵卵入账 + 计数更新（最远幕取较大者）。</summary>
        public static RunIncome Settle(RunDriver driver, MetaState state)
        {
            int nodes = 0, bosses = 0, actReached = 1;
            for (int i = 0; i < driver.Records.Count; i++)
            {
                var r = driver.Records[i];
                if (r.Act > actReached) actReached = r.Act;
                if (r.IsBoss) bosses++; else nodes++;
            }
            return Settle(state, nodes, bosses, driver.Outcome == RunOutcome.Completed, actReached);
        }
    }
}
