// ============================================================================
//  万相 · 战斗目标注册表 / 确定性分配 / 判定（2026-11-19 玩法骨架）
//  ---------------------------------------------------------------------------
//  设计要点：
//  1. 表驱动：目标定义来自 objectives.csv（UI 层 DataTables 解析后 Install）
//  2. 注册表：判定逻辑按 ObjectiveKind 注册，新增目标类型 = 注册一行
//  3. 确定性分配：按 (runSeed, act, offset) 哈希，地图重建后目标不变，零存档依赖
//  4. 附加奖励：目标未完成不判负，只是拿不到奖励 —— 玩家可无视
//  5. 可复用：局外任务可用同一套 Def/分配，只换 icon 与消费场景
//  零 Unity 依赖。
// ============================================================================
using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    public static class ObjectiveRules
    {
        private static readonly List<ObjectiveDef> _all = new List<ObjectiveDef>(16);
        private static readonly Dictionary<string, ObjectiveDef> _byId = new Dictionary<string, ObjectiveDef>(16);
        // 判定委托：返回 true = 目标已达成。参数含 progress 供读取计数器/起始回合。
        private delegate bool Judge(ObjectiveDef def, ObjectiveProgress p, BattleState st);
        private static readonly Dictionary<ObjectiveKind, Judge> _judges = new Dictionary<ObjectiveKind, Judge>(8);

        public static int Count => _all.Count;
        public static IReadOnlyList<ObjectiveDef> All => _all;

        static ObjectiveRules()
        {
            // 内置判定逻辑注册表。新增目标类型只需在这里加一行 + ObjectiveKind 加枚举。
            _judges[ObjectiveKind.KillByTurn] = (d, p, st) =>
                st != null && st.Outcome == BattleOutcome.PlayerWin
                && st.Turn <= p.StartTurn + d.Param;
            _judges[ObjectiveKind.NoDeath] = (d, p, st) =>
                st != null && st.Outcome == BattleOutcome.PlayerWin
                && st.AllPlayerUnitsSurvived();
            _judges[ObjectiveKind.ElementReactions] = (d, p, st) =>
                p.Counter >= d.Param;
            _judges[ObjectiveKind.CaptureOne] = (d, p, st) =>
                p.Counter >= d.Param;
            _judges[ObjectiveKind.NoUltimate] = (d, p, st) =>
                st != null && st.Outcome == BattleOutcome.PlayerWin
                && p.Counter == 0;
            _judges[ObjectiveKind.WinLowHp] = (d, p, st) =>
                st != null && st.Outcome == BattleOutcome.PlayerWin
                && st.PlayerLowestHpPercent() <= d.Param;
        }

        public static void Install(IEnumerable<ObjectiveDef> defs)
        {
            if (defs == null) return;
            _all.Clear();
            _byId.Clear();
            foreach (var d in defs)
            {
                if (d == null || string.IsNullOrEmpty(d.Id) || d.Kind == ObjectiveKind.None) continue;
                if (!_judges.ContainsKey(d.Kind)) continue;   // 未知类型跳过，零崩溃
                _all.Add(d);
                _byId[d.Id] = d;
            }
        }

        public static ObjectiveDef Get(string id)
        {
            ObjectiveDef d;
            return !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out d) ? d : null;
        }

        /// <summary>
        /// 确定性分配：某战斗节点是否有目标、是哪种。
        /// 相同 (seed, act, offset) 永远得到相同结果 ⇒ 地图按种子重建也不丢目标，无需存档。
        /// </summary>
        public static ObjectiveDef AssignFor(ulong runSeed, int act, int offset)
        {
            if (_all.Count == 0) return null;
            uint h = CoreMath.Fnv1a("goal:" + runSeed.ToString() + ":" + act + ":" + offset);
            int roll = (int)(h % 100u);

            // 按 chance 权重轮盘选一个目标；chance 之和不必等于 100（余数 = 无目标）
            int cursor = 0;
            for (int i = 0; i < _all.Count; i++)
            {
                int c = _all[i].Chance;
                if (c <= 0) continue;
                if (roll >= cursor && roll < cursor + c) return _all[i];
                cursor += c;
            }
            return null;   // 没命中 = 本节点无目标
        }

        /// <summary>目标是否达成（判定逻辑集中在此）。</summary>
        public static bool IsComplete(ObjectiveProgress p, BattleState st)
        {
            if (p == null || !p.Active || p.Completed) return p != null && p.Completed;
            var def = Get(p.GoalId);
            if (def == null) return false;
            Judge j;
            if (!_judges.TryGetValue(def.Kind, out j)) return false;
            bool ok = j(def, p, st);
            if (ok) p.Completed = true;
            return ok;
        }

        /// <summary>目标文案（供 UI 显示）。</summary>
        public static string Describe(string goalId)
        {
            var d = Get(goalId);
            if (d == null) return "";
            // 描述里的 {0} 用 param 填（如"N 回合"→"8 回合"），没占位符就不动
            string desc = d.Desc;
            if (desc.IndexOf("{0}") >= 0) desc = desc.Replace("{0}", d.Param.ToString());
            else if (d.Param > 0) desc = desc.Replace("N", d.Param.ToString());
            return d.Name + "：" + desc + "（" + RewardText(d) + "）";
        }

        public static string RewardText(ObjectiveDef d)
        {
            if (d == null) return "";
            switch (d.RewardType)
            {
                case ObjectiveReward.Eggs: return "+" + d.RewardValue + " 灵卵";
                case ObjectiveReward.Wudao: return "+" + d.RewardValue + " 悟道点";
                case ObjectiveReward.Ink: return "+" + d.RewardValue + " 墨铊";
                case ObjectiveReward.RelicShard: return "+" + d.RewardValue + " 遗物碎片";
                default: return "无奖励";
            }
        }
    }
}
