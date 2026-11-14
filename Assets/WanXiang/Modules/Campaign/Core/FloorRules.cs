// ============================================================================
//  万相 · 层规则注册表（Core，2026-11-14）
//  ---------------------------------------------------------------------------
//  目标：打破"每层都一样"。每幕 18 层分 3 段，段的候选宽度、节点池、修饰规则不同；
//        另有若干"特殊层"强制单/双节点（歇脚 / 命运 / 商队 / 险路 / 守关）。
//  ⛔ Core 不读 TextAsset：数据由 UI 层 DataTables 解析后 Install 注入。
//  扩展点：加段 = floor_rules.csv 一行；加特殊层 = floor_special.csv 一行。
//  删除点：删表行即可，行为自动归零（回退到 MapRules.Width 与主池）。
// ============================================================================
using System.Collections.Generic;
using WanXiang.Battle.Core;

namespace WanXiang.Campaign
{
    public sealed class FloorRuleDef
    {
        public int Act;
        public int LayerFrom;         // 0-based
        public int LayerTo;           // 含
        public int MinWidth;
        public int MaxWidth;
        public NodeKind[] Pool;       // 段内额外池（不进配额约束）
        public string RuleTitle;
        public string RuleText;
        public string RuleId;
    }

    public sealed class FloorSpecialDef
    {
        public int Act;
        public int Layer;             // 0-based
        public int Width;
        public NodeKind Kind;
        public string Label;
    }

    public static class FloorRules
    {
        private static readonly List<FloorRuleDef> _rules = new List<FloorRuleDef>();
        private static readonly List<FloorSpecialDef> _specials = new List<FloorSpecialDef>();

        public static int RuleCount => _rules.Count;
        public static int SpecialCount => _specials.Count;

        public static void Install(List<FloorRuleDef> rules, List<FloorSpecialDef> specials)
        {
            _rules.Clear(); _specials.Clear();
            if (rules != null) _rules.AddRange(rules);
            if (specials != null) _specials.AddRange(specials);
        }

        public static FloorRuleDef RuleFor(int act, int layer)
        {
            for (int i = 0; i < _rules.Count; i++)
            {
                var r = _rules[i];
                if (r != null && r.Act == act && layer >= r.LayerFrom && layer <= r.LayerTo) return r;
            }
            return null;
        }

        public static FloorSpecialDef SpecialFor(int act, int layer)
        {
            for (int i = 0; i < _specials.Count; i++)
            {
                var s = _specials[i];
                if (s != null && s.Act == act && s.Layer == layer) return s;
            }
            return null;
        }

        /// <summary>本层候选数：段规则 min..max 由种子决定，缺省回退 MapRules.Width。</summary>
        public static int WidthFor(int act, int layer, DeterministicRandom rng)
        {
            var sp = SpecialFor(act, layer);
            if (sp != null) return sp.Width;
            var r = RuleFor(act, layer);
            if (r == null) return MapRules.Width;
            if (r.MaxWidth <= r.MinWidth) return r.MinWidth;
            return rng != null ? rng.NextInt(r.MinWidth, r.MaxWidth + 1) : r.MinWidth;
        }

        /// <summary>段内额外池：以 30% 概率优先从池里挑一个（其余走主权重表）。</summary>
        public static NodeKind PickKind(int act, int layer, DeterministicRandom rng,
                                       NodeKind mainPick)
        {
            var r = RuleFor(act, layer);
            if (r == null || r.Pool == null || r.Pool.Length == 0 || rng == null) return mainPick;
            if (rng.NextInt(0, 10) >= 3) return mainPick;
            return r.Pool[rng.NextInt(0, r.Pool.Length)];
        }
    }
}
