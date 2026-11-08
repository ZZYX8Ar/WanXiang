// ============================================================================
//  万相 · 开局数据表（B3 / 分队 + 难度）
//  ---------------------------------------------------------------------------
//  squads.csv      → 分队（起手阵容模板 + 初始资源 + 解锁条件）
//  difficulty_mods.csv → N1~N18（倍率 + 词条 + 灵卵系数 + 里程碑规则键）
//
//  · BaseTable 只提供 Install / 查询；**不判断解锁状态**（那是上层 + 存档的事）
//  · 表缺失 ⇒ 空表但查询返回 null/默认，不抛异常
//  · 加/删分队、改难度 = 只动 CSV
//  ⛔ 本文件在 Battle.Core（noEngineReferences），解析在 UI 层 DataTables。
// ============================================================================
using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    // ------------------------------------------------------------------ 分队
    public sealed class SquadDef
    {
        public string Id;
        public string Name;
        public Element Element = Element.None;
        /// <summary>起手异兽 id；`random:N` / `random_same_element:N` 由上层解释。</summary>
        public List<string> Starters = new List<string>();
        public string StarterExpr;      // 原始表达式（random:3 之类）
        public int Eggs;
        public string Relic;
        public string Unlock;           // default / act1_clear / act3_clear / clear / end_xxx
        public string Desc;
    }

    public static class SquadTable
    {
        private static readonly List<SquadDef> _all = new List<SquadDef>();
        public static IReadOnlyList<SquadDef> All => _all;
        public static int Count => _all.Count;
        public static bool Installed { get; private set; }

        public static void Install(IReadOnlyList<SquadDef> rows)
        {
            _all.Clear();
            if (rows != null)
                for (int i = 0; i < rows.Count; i++)
                    if (rows[i] != null && !string.IsNullOrEmpty(rows[i].Id)) _all.Add(rows[i]);
            Installed = _all.Count > 0;
        }

        public static void Clear() { _all.Clear(); Installed = false; }

        public static SquadDef For(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _all.Count; i++) if (_all[i].Id == id) return _all[i];
            return null;
        }

        /// <summary>默认分队（unlock == "default"）；没有则返回第一条。</summary>
        public static SquadDef Default()
        {
            for (int i = 0; i < _all.Count; i++)
                if (string.Equals(_all[i].Unlock, "default", System.StringComparison.OrdinalIgnoreCase))
                    return _all[i];
            return _all.Count > 0 ? _all[0] : null;
        }

        /// <summary>按"已满足的解锁键集合"过滤（上层把通关进度换算成 key）。</summary>
        public static List<SquadDef> Available(ICollection<string> satisfiedUnlocks)
        {
            var list = new List<SquadDef>();
            for (int i = 0; i < _all.Count; i++)
            {
                string u = _all[i].Unlock;
                if (string.IsNullOrEmpty(u)) { list.Add(_all[i]); continue; }
                if (string.Equals(u, "default", System.StringComparison.OrdinalIgnoreCase)) { list.Add(_all[i]); continue; }
                if (satisfiedUnlocks != null && satisfiedUnlocks.Contains(u)) list.Add(_all[i]);
            }
            return list;
        }
    }

    // ------------------------------------------------------------------ 难度
    public sealed class DifficultyDef
    {
        public int Level = 1;
        public float HpMul = 1f, AtkMul = 1f, DefMul = 1f;
        public int ExtraTraits;
        public float EggMul = 1f;
        public string NewRule;      // elite_extra_add / boss_phase2 / ai_aggressive / turns_minus6 / enemy_calamity / boss_shield_all
        public string Desc;
    }

    public static class DifficultyTable
    {
        private static readonly List<DifficultyDef> _all = new List<DifficultyDef>();
        public static IReadOnlyList<DifficultyDef> All => _all;
        public static int Count => _all.Count;
        public static bool Installed { get; private set; }
        public static int MaxLevel { get; private set; } = 18;

        public static void Install(IReadOnlyList<DifficultyDef> rows)
        {
            _all.Clear();
            MaxLevel = 1;
            if (rows != null)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    var d = rows[i];
                    if (d == null || d.Level <= 0) continue;
                    _all.Add(d);
                    if (d.Level > MaxLevel) MaxLevel = d.Level;
                }
            }
            _all.Sort((a, b) => a.Level.CompareTo(b.Level));
            Installed = _all.Count > 0;
        }

        public static void Clear() { _all.Clear(); Installed = false; MaxLevel = 18; }

        /// <summary>取某档；越界自动夹到 [1, MaxLevel]；表空 ⇒ null（调用方按 1 倍处理）。</summary>
        public static DifficultyDef Row(int level)
        {
            if (_all.Count == 0) return null;
            if (level < 1) level = 1;
            if (level > MaxLevel) level = MaxLevel;
            for (int i = 0; i < _all.Count; i++) if (_all[i].Level == level) return _all[i];
            return _all[_all.Count - 1];   // 表有洞时退回最高档
        }

        /// <summary>该档是否有某条里程碑规则。</summary>
        public static bool HasRule(int level, string rule)
        {
            var d = Row(level);
            return d != null && !string.IsNullOrEmpty(d.NewRule)
                && string.Equals(d.NewRule, rule, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
