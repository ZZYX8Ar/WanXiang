// ============================================================================
//  万相 · 博戏随机表（Core，2026-11-14）
//  ⛔ Core 不读 CSV：由 UI 层 DataTables 解析后 Install 注入。
//  加一种赌局 = gamble_tables.csv 加几行；删 = 删行，行为自动归零。
// ============================================================================
using System.Collections.Generic;
using WanXiang.Battle.Core;

namespace WanXiang.Campaign
{
    public sealed class GambleRow
    {
        public string TableId;
        public int RollMin;
        public int RollMax;       // 含
        public EventEffect Effect;
        public int Value;
        public string Text;
    }

    public static class GambleTables
    {
        private static readonly Dictionary<string, List<GambleRow>> _tables
            = new Dictionary<string, List<GambleRow>>();

        public static void Install(List<GambleRow> rows)
        {
            _tables.Clear();
            if (rows == null) return;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r == null || string.IsNullOrEmpty(r.TableId)) continue;
                if (!_tables.TryGetValue(r.TableId, out var list))
                { list = new List<GambleRow>(); _tables[r.TableId] = list; }
                list.Add(r);
            }
        }

        /// <summary>确定性掷一次；返回命中的行（表缺失/区间不全 → null，调用方给兜底文案）。</summary>
        public static GambleRow Roll(string tableId, DeterministicRandom rng)
        {
            if (string.IsNullOrEmpty(tableId) || _tables == null) return null;
            if (!_tables.TryGetValue(tableId, out var list) || list == null || list.Count == 0) return null;
            // ⚠ 用 NextFloat 而不是 NextInt：相近种子下 NextInt 落点会集中（实测 8 次里 7 次同一档），
            //   玩家会觉得"博戏总赢"。浮点取满 0..99 分布更均匀，且同样可复盘。
            int roll = rng != null ? (int)(rng.NextFloat() * 100f) : 0;
            if (roll < 0) roll = 0; if (roll > 99) roll = 99;
            for (int i = 0; i < list.Count; i++)
                if (roll >= list[i].RollMin && roll <= list[i].RollMax) return list[i];
            return null;
        }
    }
}
