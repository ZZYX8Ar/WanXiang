// ============================================================================
//  遭遇表（2026-11-02）
//  ---------------------------------------------------------------------------
//  把"某幕·某种节点 → 哪些敌人 + 强度系数"从代码里挪出来。
//  数据源：`Assets/Resources/Data/encounters.csv`（Excel 可编辑）。
//  本类只做纯数据容器 + 匹配，解析在 UI 层（那边才有 UnityEngine）。
// ============================================================================
using System.Collections.Generic;

namespace WanXiang.Campaign
{
    /// <summary>一条敌人：id + 上阵格（0~8，4 = 中宫）。</summary>
    public struct EncounterEntry
    {
        public string Id;
        public int Slot;
    }

    public sealed class EncounterDef
    {
        public int Act;
        public string NodeKind;      // "Encounter"/"Elite"/"Boss"
        public string Name;          // 编队名（设计用）
        public string Synergy;       // 这组敌人怎么配合（设计用，进 Console/图鉴）
        public EncounterEntry[] Entries;
        public float Scale = 1f;
    }

    public static class EncounterTable
    {
        private static List<EncounterDef> _rows = new List<EncounterDef>();

        public static int Count => _rows.Count;

        public static void Install(List<EncounterDef> rows)
        {
            _rows = rows ?? new List<EncounterDef>();
        }

        /// <summary>按 幕 + 节点类型 取一条（同 key 多条时随机取一条，用 seed 保证稳定）。</summary>
        public static EncounterDef Pick(int act, string nodeKind, ulong seed)
        {
            if (_rows.Count == 0) return null;
            var hits = new List<EncounterDef>(4);
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].Act == act && string.Equals(_rows[i].NodeKind, nodeKind, System.StringComparison.OrdinalIgnoreCase))
                    hits.Add(_rows[i]);
            if (hits.Count == 0) return null;
            return hits[(int)(seed % (ulong)hits.Count)];
        }
    }
}
