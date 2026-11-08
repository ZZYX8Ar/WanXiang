// ============================================================================
//  万相 · 灵感树规则（B5-3 骨架，Battle/Core/Data）
//  ---------------------------------------------------------------------------
//  设计 v2 §12：三支 × 5 节点（行旅=地图事件 / 战阵=战斗 / 炉火=养成）。
//  ⛔ 节点只允许"改规则/给选项"，禁止纯数值（保护 N1~N18 难度曲线）。
//  查询：Has(id)（是否已解锁）/ EffectValue(key)（已解锁节点的效果值合计）——
//  各系统挂点（捕获/升级/QTE/事件池…）按 key 取值即可，不需要知道树结构。
// ============================================================================
using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    public sealed class InspirationDef
    {
        public string Id;
        public string Name;
        public string Branch;      // travel / battle / forge
        public int Tier;           // 1~5（支内层级；解锁上级才能买下级，购买 UI 批处理）
        public int Cost;           // 悟道点消耗
        public string Desc;
        public string EffectKey;   // 效果键（各系统挂点查询用）
        public int EffectValue;
    }

    public static class InspirationRules
    {
        private static readonly List<InspirationDef> _all = new List<InspirationDef>();
        public static IReadOnlyList<InspirationDef> All => _all;
        public static int Count => _all.Count;

        public static void Install(List<InspirationDef> rows)
        {
            _all.Clear();
            if (rows != null) _all.AddRange(rows);
        }

        public static InspirationDef DefOf(string id)
        {
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].Id == id) return _all[i];
            return null;
        }

        /// <summary>效果值合计：已解锁节点中 effect_key 匹配者的 effect_value 之和（未解锁 = 0）。</summary>
        public static int EffectValue(IEnumerable<string> unlockedIds, string effectKey)
        {
            if (unlockedIds == null || string.IsNullOrEmpty(effectKey)) return 0;
            int sum = 0;
            foreach (var uid in unlockedIds)
            {
                var d = DefOf(uid);
                if (d != null && d.EffectKey == effectKey) sum += d.EffectValue;
            }
            return sum;
        }

        /// <summary>能否购买：未解锁 + 层级合规（tier 1 恒可；否则支内 tier-1 已解锁）。</summary>
        public static bool CanBuy(IEnumerable<string> unlockedIds, string id)
        {
            var d = DefOf(id);
            if (d == null) return false;
            foreach (var uid in unlockedIds) if (uid == id) return false;   // 已解锁
            if (d.Tier <= 1) return true;
            foreach (var uid in unlockedIds)
            {
                var prev = DefOf(uid);
                if (prev != null && prev.Branch == d.Branch && prev.Tier == d.Tier - 1) return true;
            }
            return false;
        }

        /// <summary>悟道点发放（设计 v2 §12：N 等级 ×2 + 通关 +10 + 首次通关某幕 +5）。
        ///  firstClearActs = 首次通关的幕数（调用方按 MetaState.BestActReached 差值折算）。</summary>
        public static int WudaoGain(int challengeLevel, bool completed, int firstClearActs)
        {
            return challengeLevel * 2 + (completed ? 10 : 0) + firstClearActs * 5;
        }
    }
}
