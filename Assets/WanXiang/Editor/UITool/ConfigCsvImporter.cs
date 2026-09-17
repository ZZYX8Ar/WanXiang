// ============================================================================
//  WanXiang · 配置表导入（CSV → ScriptableObject）
//  ---------------------------------------------------------------------------
//  菜单：万相 / 配置 / 从 CSV 导入数据表
//
//  数据链路（与 万相_设计文档/数据表/export_config.py 配套）：
//    策划改 Excel（万相_配置表.xlsx）
//      → 另存 CSV UTF-8 覆盖 WanXiang/ConfigCSV/beast.csv | skill.csv
//      → 本菜单读 CSV，按主键更新 Config/Beasts、Config/Skills 的 SO 资产
//
//  设计取舍：
//    · Unity 运行时**不读 Excel/CSV** —— 配置在导入期就落进 ScriptableObject，
//      运行时只有强类型访问，没有解析开销与格式风险；CSV 只是编辑器期的交接格式。
//    · 只更新已存在的资产（主键匹配）；不在这条链路上新建/删除内容，
//      新内容走 BeastContentImporter / 技能占位生成器。
//    · CSV 按 RFC4180 解析（描述里带逗号/换行都用引号包裹，导出脚本已保证）。
// ============================================================================

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WanXiang.EditorTools
{
    public static class ConfigCsvImporter
    {
        private const string CsvDir = "ConfigCSV";   // 相对工程根

        [MenuItem("万相/配置/从 CSV 导入数据表", priority = 200)]
        public static void Import()
        {
            string root = Path.GetDirectoryName(Application.dataPath);   // 工程根
            int beastUpdated = ImportBeasts(Path.Combine(root, CsvDir, "beast.csv"));
            int skillUpdated = ImportSkills(Path.Combine(root, CsvDir, "skill.csv"));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ConfigCsv] 导入完成：异兽 {beastUpdated} 条、技能 {skillUpdated} 条已更新。");
        }

        // ================================================================
        //  异兽表
        // ================================================================

        private static int ImportBeasts(string path)
        {
            var rows = ReadCsvRows(path);
            if (rows == null) return 0;
            if (rows.Count == 0) { Debug.LogWarning("[ConfigCsv] beast.csv 是空的：" + path); return 0; }

            var beasts = FindAllByGuid("BeastConfigSO");   // 异兽 SO 的类名（注意不是 BeastDef，那是运行时数据类）
            int updated = 0;

            foreach (var asset in beasts)
            {
                var so = new SerializedObject(asset);
                string id = so.FindProperty("BeastId") != null ? so.FindProperty("BeastId").stringValue : "";
                if (!rows.TryGetValue(id, out var row)) continue;

                SetString(so, "DisplayName", row, "displayName");
                SetInt(so, "Element", row, "element");
                SetInt(so, "Role", row, "role");
                SetInt(so, "Rarity", row, "rarity");
                SetString(so, "Source", row, "source");
                SetString(so, "Quote", row, "quote");
                SetString(so, "Lore", row, "lore");
                SetString(so, "Codex", row, "codex");
                SetString(so, "TraitName", row, "traitName");
                SetString(so, "TraitDescription", row, "traitDescription");
                SetString(so, "BodyMainHex", row, "bodyMainHex");
                SetString(so, "BodyAccentHex", row, "bodyAccentHex");
                SetString(so, "EnergyGlowHex", row, "energyGlowHex");
                SetString(so, "EyeCoreHex", row, "eyeCoreHex");
                SetString(so, "OutlineHex", row, "outlineHex");

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                updated++;
            }

            Debug.Log($"[ConfigCsv] 异兽表：目录 {beasts.Count} 只，匹配更新 {updated} 条" +
                      $"（CSV 里多余的 {rows.Count - updated} 行没有对应资产，已忽略）。");
            return updated;
        }

        // ================================================================
        //  技能表
        // ================================================================

        private static int ImportSkills(string path)
        {
            var rows = ReadCsvRows(path);
            if (rows == null) return 0;
            if (rows.Count == 0) { Debug.LogWarning("[ConfigCsv] skill.csv 是空的：" + path); return 0; }

            var skills = FindAllByGuid("SkillConfigSO");
            int updated = 0;

            foreach (var asset in skills)
            {
                var so = new SerializedObject(asset);
                string id = so.FindProperty("SkillId") != null ? so.FindProperty("SkillId").stringValue : "";
                if (!rows.TryGetValue(id, out var row)) continue;

                SetString(so, "SkillName", row, "skillName");
                SetInt(so, "Cd", row, "cd");
                SetString(so, "Description", row, "description");

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                updated++;
            }

            Debug.Log($"[ConfigCsv] 技能表：目录 {skills.Count} 个，匹配更新 {updated} 条。");
            return updated;
        }

        // ================================================================
        //  工具
        // ================================================================

        /// <summary>按脚本类名找 SO 资产（不依赖路径）。</summary>
        private static List<Object> FindAllByGuid(string scriptClassName)
        {
            var result = new List<Object>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + scriptClassName))
            {
                var obj = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid));
                if (obj != null) result.Add(obj);
            }
            return result;
        }

        private static void SetString(SerializedObject so, string field, Dictionary<string, string> row, string col)
        {
            var p = so.FindProperty(field);
            if (p == null || !row.TryGetValue(col, out var value)) return;
            p.stringValue = value;
        }

        private static void SetInt(SerializedObject so, string field, Dictionary<string, string> row, string col)
        {
            var p = so.FindProperty(field);
            if (p == null || !row.TryGetValue(col, out var value)) return;
            if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                p.intValue = n;
        }

        /// <summary>
        /// 读 CSV → 主键 → 行。RFC4180：引号包裹、双引号转义、单元格内换行。
        /// 返回 null 表示文件不存在。
        /// </summary>
        private static Dictionary<string, Dictionary<string, string>> ReadCsvRows(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning("[ConfigCsv] 找不到 CSV：" + path);
                return null;
            }

            string text = File.ReadAllText(path);
            var table = ParseCsv(text);
            if (table.Count == 0) return new Dictionary<string, Dictionary<string, string>>();

            var header = table[0];
            var result = new Dictionary<string, Dictionary<string, string>>();
            for (int r = 1; r < table.Count; r++)
            {
                var row = new Dictionary<string, string>();
                for (int c = 0; c < header.Count; c++)
                    row[header[c]] = c < table[r].Count ? table[r][c] : "";
                result[row.TryGetValue(header[0], out var key) ? key : ""] = row;
            }
            return result;
        }

        /// <summary>最小 RFC4180 解析：处理引号包裹、转义引号、格内换行。</summary>
        private static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else cell.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { row.Add(cell.ToString()); cell.Length = 0; }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(cell.ToString()); cell.Length = 0;
                    if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
                    row = new List<string>();
                }
                else cell.Append(c);
            }

            row.Add(cell.ToString());
            if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
            return rows;
        }
    }
}
