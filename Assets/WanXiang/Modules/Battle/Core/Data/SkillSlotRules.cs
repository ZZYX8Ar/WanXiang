// ============================================================================
//  万相 · 技能槽规则（B2-a / 纯规则层，零接线）
//  ---------------------------------------------------------------------------
//  只管三件事，**不碰战斗、不建 SkillDef**：
//    ① 一只兽有几个可换的战记槽（由品级决定）
//    ② 哪些槽是锁死的（普攻/奥义/觉醒）
//    ③ 某只兽能装哪些池子里的技能（五行 + 通用过滤）
//
//  ⚠ 池数据来自 skill_pool.csv（表缺失 ⇒ 池为空，规则仍然可用，不报错）
//  ⛔ 接线（把 Equipped 覆写进 BeastDef）留给后续批次，且要避开正在改的 BattleRequestFactory。
// ============================================================================
using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    /// <summary>技能池条目（只描述"能不能装"，不含效果原子）。</summary>
    public sealed class SkillPoolEntry
    {
        public string Id;
        public string Name;
        public Element Element = Element.None;   // None = 通用
        public SkillType Type = SkillType.Active;
        public string Tag;                       // "common" = 任何兽都能装
        public float PowerMul = 1f;
        public int HitsDelta;
        public string Target;
        public string Desc;
    }

    public static class SkillSlotRules
    {
        private static readonly List<SkillPoolEntry> _pool = new List<SkillPoolEntry>();

        public static int PoolCount => _pool.Count;
        public static IReadOnlyList<SkillPoolEntry> Pool => _pool;
        public static bool Installed { get; private set; }

        /// <summary>可换的战记槽数量：灵品 1，玄/神 2。</summary>
        public static int SwapSlotCount(Rarity r)
        {
            switch (r)
            {
                case Rarity.Rare: return 1;
                case Rarity.Epic: return 2;
                case Rarity.Legend: return 2;
                default: return 1;
            }
        }

        /// <summary>锁死的槽：普攻 / 奥义 / 觉醒 —— 不可更换。</summary>
        public static bool IsFixedSlot(SkillType t)
            => t == SkillType.Basic || t == SkillType.Ultimate || t == SkillType.Awaken;

        /// <summary>这只兽能不能装这个池条目：同五行 / 通用元素 / tag=common。</summary>
        public static bool CanEquip(Element beastElement, SkillPoolEntry entry)
        {
            if (entry == null) return false;
            if (entry.Type != SkillType.Active) return false;      // 战记槽只装战记（普攻/奥义/觉醒另有专槽）
            if (entry.Element == Element.None) return true;
            if (entry.Element == beastElement) return true;
            return string.Equals(entry.Tag, "common", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>某只兽的候选技能（用于 UI 列表；顺序稳定 = 表顺序）。</summary>
        public static List<SkillPoolEntry> Candidates(Element beastElement)
        {
            var list = new List<SkillPoolEntry>();
            for (int i = 0; i < _pool.Count; i++)
                if (CanEquip(beastElement, _pool[i])) list.Add(_pool[i]);
            return list;
        }

        public static void Install(IReadOnlyList<SkillPoolEntry> rows)
        {
            _pool.Clear();
            if (rows != null)
                for (int i = 0; i < rows.Count; i++)
                    if (rows[i] != null && !string.IsNullOrEmpty(rows[i].Id)) _pool.Add(rows[i]);
            Installed = _pool.Count > 0;
        }

        public static void Clear() { _pool.Clear(); Installed = false; }
    }
}
