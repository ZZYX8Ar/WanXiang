// ============================================================================
//  万相 · 数据表加载 · 战役部分（2026-11-17 代码骨架重构）
//  从 DataTables.cs 原样搬入，逻辑零改动。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using WanXiang.Battle.Core;
using WanXiang.Campaign;

namespace WanXiang.Modules.UI
{
    public static partial class DataTables
    {
        //  map.csv / node_weights.csv
        // ================================================================

        private static void LoadMap()
        {
            var rows = Read("map", out var header);
            if (rows == null) return;
            int layers = 0, width = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "map.csv", i + 2, Problems);
                string key = r.Str("key").Trim().ToLowerInvariant();
                int v = r.Int("value", 0);
                if (key == "layers") layers = v;
                else if (key == "width") width = v;
                else r.Fail("未知 key：" + key);
            }
            if (layers <= 0 && width <= 0) return;
            // ⛔ 层数下限 4：低于 4 层时 BuildRoute 会退化成缺省图（见 SolarTermGraph 的注释）
            if (layers > 0 && layers < 4) { Problems.Add("map.csv：layers=" + layers + " 太小（≥4），已忽略"); layers = 0; }
            if (width > 0 && (width < 1 || width > 5)) { Problems.Add("map.csv：width=" + width + " 越界（1..5），已忽略"); width = 0; }
            MapRules.InstallShape(layers, width);
            MapInstalled = layers > 0 || width > 0;
        }

        private static void LoadNodeWeights()
        {
            var rows = Read("node_weights", out var header);
            if (rows == null) return;
            int count = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "node_weights.csv", i + 2, Problems);
                string kindText = r.Str("kind");
                if (!Enum.TryParse(kindText, true, out NodeKind kind))
                {
                    r.Fail("未知节点类型：" + kindText);
                    continue;
                }
                int weight = r.Int("weight", 0);
                int minAct = r.Int("minAct", 0);
                if (weight <= 0 && minAct <= 0) { r.Fail("weight 与 minAct 至少要有一个 > 0"); continue; }
                MapRules.InstallNode(kind, weight, minAct);
                count++;
            }
            WeightRows = count;
        }

        // ================================================================
        //  relics.csv / combos.csv / progression.csv
        // ================================================================

        /// <summary>层规则 / 特殊层（唯一解析层；Core 只接受 Install）。</summary>
        private static void LoadFloorRules()
        {
            var rules = new System.Collections.Generic.List<WanXiang.Campaign.FloorRuleDef>();
            var rows = Read("floor_rules", out var header);
            if (rows != null)
                for (int i = 0; i < rows.Count; i++)
                {
                    var r = new Row(header, rows[i], "floor_rules.csv", i + 2, Problems);
                    var pool = new System.Collections.Generic.List<WanXiang.Campaign.NodeKind>();
                    foreach (var part in r.Str("pool").Split('|'))
                    {
                        if (string.IsNullOrWhiteSpace(part)) continue;
                        if (System.Enum.TryParse(part.Trim(), true, out WanXiang.Campaign.NodeKind k)) pool.Add(k);
                        else r.Fail("未知节点类型：" + part);
                    }
                    rules.Add(new WanXiang.Campaign.FloorRuleDef
                    {
                        Act = r.Int("act", 1),
                        LayerFrom = r.Int("layer0From", 0),
                        LayerTo = r.Int("layer0To", 17),
                        MinWidth = r.Int("minWidth", 2),
                        MaxWidth = r.Int("maxWidth", 3),
                        Pool = pool.ToArray(),
                        RuleTitle = r.Str("ruleTitle"),
                        RuleText = r.Str("ruleText"),
                        RuleId = r.Str("ruleId"),
                    });
                }
            var specials = new System.Collections.Generic.List<WanXiang.Campaign.FloorSpecialDef>();
            var rows2 = Read("floor_special", out var header2);
            if (rows2 != null)
                for (int i = 0; i < rows2.Count; i++)
                {
                    var r = new Row(header2, rows2[i], "floor_special.csv", i + 2, Problems);
                    if (!System.Enum.TryParse(r.Str("kind"), true, out WanXiang.Campaign.NodeKind k))
                    { r.Fail("未知节点类型：" + r.Str("kind")); continue; }
                    specials.Add(new WanXiang.Campaign.FloorSpecialDef
                    {
                        Act = r.Int("act", 1),
                        Layer = r.Int("layer0", 0),
                        Width = r.Int("width", 1),
                        Kind = k,
                        Label = r.Str("label"),
                    });
                }
            WanXiang.Campaign.FloorRules.Install(rules, specials);
        }

        /// <summary>博戏随机结果表（唯一解析层）。</summary>
        private static void LoadGambleTables()
        {
            var rows = Read("gamble_tables", out var header);
            var list = new System.Collections.Generic.List<WanXiang.Campaign.GambleRow>();
            if (rows != null)
                for (int i = 0; i < rows.Count; i++)
                {
                    var r = new Row(header, rows[i], "gamble_tables.csv", i + 2, Problems);
                    if (!System.Enum.TryParse(r.Str("effect"), true, out WanXiang.Campaign.EventEffect eff))
                    { r.Fail("未知事件效果：" + r.Str("effect")); continue; }
                    list.Add(new WanXiang.Campaign.GambleRow
                    {
                        TableId = r.Str("tableId"),
                        RollMin = r.Int("rollMin", 0),
                        RollMax = r.Int("rollMax", 99),
                        Effect = eff,
                        Value = r.Int("value", 0),
                        Text = r.Str("text"),
                    });
                }
            WanXiang.Campaign.GambleTables.Install(list);
        }

        private static void LoadRelics()
        {
            var rows = Read("relics", out var header);
            if (rows == null) return;
            var list = new List<RelicDef>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "relics.csv", i + 2, Problems);
                string id = r.Str("id");
                if (string.IsNullOrEmpty(id)) { r.Fail("id 为空"); continue; }
                list.Add(new RelicDef(
                    id,
                    r.Str("name", id),
                    r.Str("desc"),
                    r.Enum("rarity", RelicRarity.Common),
                    r.Enum("effect", RelicEffect.None),
                    r.Float("value", 0f),
                    r.Float("value2", 0f),
                    r.Str("scopeId"),
                    r.Enum("mechanic", RelicMechanic.None)));
            }
            if (list.Count == 0) { Problems.Add("relics.csv：没有有效行，保持内置池"); return; }
            RelicCatalog.Install(list);
            RelicCount = list.Count;
        }

        private static void LoadCombos()
        {
            var rows = Read("combos", out var header);
            if (rows == null) return;
            var list = new List<ComboDef>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "combos.csv", i + 2, Problems);
                string id = r.Str("id");
                if (string.IsNullOrEmpty(id)) { r.Fail("id 为空"); continue; }
                var cells = ParseCells(r.Str("cells"), r);
                if (cells == null || cells.Length == 0) { r.Fail("cells 为空或格式不对（应为 0;2;4）"); continue; }
                list.Add(new ComboDef
                {
                    Id = id,
                    Name = r.Str("name", id),
                    Note = r.Str("note"),
                    Cells = cells,
                    CellsAlt = ParseCells(r.Str("cellsAlt"), r),
                    Effect = r.Enum("effect", ComboEffect.FireStorm),
                    Power = r.Float("power", 1f),
                    Power2 = r.Float("power2", 0f),
                });
            }
            if (list.Count == 0) { Problems.Add("combos.csv：没有有效行，保持内置 10 组"); return; }
            ComboRules.Install(list);
            ComboCount = list.Count;
        }

        private static int[] ParseCells(string text, Row row)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var parts = text.Split(new[] { ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var cells = new List<int>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                {
                    row.Fail("格号不是整数：" + parts[i]);
                    return null;
                }
                if (v < 0 || v > 8) { row.Fail("格号越界（0..8）：" + v); return null; }
                cells.Add(v);
            }
            return cells.ToArray();
        }

        private static void LoadProgression()
        {
            var rows = Read("progression", out var header);
            if (rows == null) return;
            int ok = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "progression.csv", i + 2, Problems);
                string key = r.Str("key").Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(key)) { r.Fail("key 为空"); continue; }
                float v = r.Float("value", float.NaN);
                if (float.IsNaN(v)) { r.Fail("value 不是数字"); continue; }
                if (!WanXiang.Run.ProgressionTuning.TrySet(key, v))
                {
                    r.Fail("未知 key：" + key + "（见 文档/02_数据与配置/万相_数据表_说明.md）");
                    continue;
                }
                ok++;
            }
            ProgressionKeys = ok;
        }

        // ================================================================
        //  trial_laws.csv / market.csv
        // ================================================================

        private static void LoadTrialLaws()
        {
            var rows = Read("trial_laws", out var header);
            if (rows == null) return;
            int ok = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "trial_laws.csv", i + 2, Problems);
                string id = r.Str("id");
                if (string.IsNullOrEmpty(id)) { r.Fail("id 为空"); continue; }
                if (System.Array.IndexOf(WanXiang.Trials.TrialLaws.Order, id) < 0)
                {
                    r.Fail("未知劫律 id：" + id + "（只能是 01..20，见 TrialLaws.Order）");
                    continue;
                }
                string key = r.Str("effect_key", "keep").Trim().ToLowerInvariant();
                if (System.Array.IndexOf(WanXiang.Trials.TrialLaws.KnownEffectKeys, key) < 0)
                {
                    r.Fail("未知 effect_key：" + key + "（写 none 表示只改文案，写 keep 表示沿用内置效果）");
                    continue;
                }
                WanXiang.Trials.TrialLaws.Install(id, r.Str("name"), r.Str("desc"), key, r.Float("effect_value", 0f));
                ok++;
            }
            TrialLawCount = ok;
        }

        private static void LoadMarket()
        {
            var rows = Read("market", out var header);
            if (rows == null) return;
            int ok = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "market.csv", i + 2, Problems);
                string key = r.Str("key").Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(key)) { r.Fail("key 为空"); continue; }
                float v = r.Float("value", float.NaN);
                if (float.IsNaN(v)) { r.Fail("value 不是数字"); continue; }
                if (!WanXiang.Run.MarketPricing.TrySet(key, v))
                {
                    r.Fail("未知 key：" + key + "（见 文档/02_数据与配置/万相_数据表_说明.md）");
                    continue;
                }
                ok++;
            }
            MarketKeys = ok;
        }

    }
}
