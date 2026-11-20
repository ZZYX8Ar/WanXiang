// ============================================================================
//  万相 · 图鉴收集永久加成（2026-11-20）
//  ---------------------------------------------------------------------------
//  设计目的：让"抓到新异兽"有长期价值 —— 收集度直接换算成全队永久属性。
//  数据来源：CodexUnlock.Count（真实在增长的图鉴解锁数，跨局持久）。
//  参数表驱动：codex_bonus.csv（每档解锁数 + 每档加成%）。
//  删功能 = 删表行，加成自动归零；加档 = 加一行。
// ============================================================================
using System.Collections.Generic;

namespace WanXiang.Modules.UI
{
    public static class CodexBonus
    {
        private struct Tier { public int Need; public int AtkPct; public int HpPct; }
        private static readonly List<Tier> _tiers = new List<Tier>(8);
        private static bool _loaded;

        /// <summary>由 DataTables 解析 codex_bonus.csv 后注入。</summary>
        public static void Install(IEnumerable<TierData> rows)
        {
            _tiers.Clear();
            if (rows != null)
                foreach (var r in rows)
                    _tiers.Add(new Tier { Need = r.Need, AtkPct = r.AtkPct, HpPct = r.HpPct });
            _tiers.Sort((a, b) => a.Need.CompareTo(b.Need));
            _loaded = true;
        }

        public struct TierData { public int Need; public int AtkPct; public int HpPct; }

        /// <summary>当前解锁数量达成的档数。</summary>
        public static int TierReached(int unlocked)
        {
            int n = 0;
            for (int i = 0; i < _tiers.Count; i++) if (unlocked >= _tiers[i].Need) n++;
            return n;
        }

        /// <summary>累计攻击加成百分比（如 12 = +12%）。</summary>
        public static int AtkPct(int unlocked)
        {
            EnsureDefault();
            int sum = 0;
            for (int i = 0; i < _tiers.Count; i++) if (unlocked >= _tiers[i].Need) sum += _tiers[i].AtkPct;
            return sum;
        }

        /// <summary>累计生命加成百分比。</summary>
        public static int HpPct(int unlocked)
        {
            EnsureDefault();
            int sum = 0;
            for (int i = 0; i < _tiers.Count; i++) if (unlocked >= _tiers[i].Need) sum += _tiers[i].HpPct;
            return sum;
        }

        /// <summary>战斗注入用：当前图鉴给的全队攻击乘区。</summary>
        public static float AttackMul()
        {
            int pct = AtkPct(WanXiang.Run.CodexUnlock.Count);
            return pct > 0 ? 1f + pct / 100f : 1f;
        }

        /// <summary>战斗注入用：当前图鉴给的全队生命乘区。</summary>
        public static float HpMul()
        {
            int pct = HpPct(WanXiang.Run.CodexUnlock.Count);
            return pct > 0 ? 1f + pct / 100f : 1f;
        }

        private static void EnsureDefault()
        {
            if (_loaded) return;
            // 内置默认（表缺失时兜底，跟项目其它表一致）：每 5 只一档，共 6 档，每档攻/血 +2%
            _tiers.Clear();
            for (int i = 1; i <= 6; i++)
                _tiers.Add(new Tier { Need = i * 5, AtkPct = 2, HpPct = 2 });
            _loaded = true;
        }
    }
}
