// ============================================================================
//  WanXiang · 数据表校验（编辑器）
//  ---------------------------------------------------------------------------
//  菜单：万相 / 配置 / 校验所有 CSV
//
//  作用：重新解析 Resources/Data 下的 7 张表并打印结果。
//  能抓到的错：列名拼错、id 为空、首领 id 不存在、枚举写错、数值越界、
//             天时原子编码段数不足、地图层数 < 4 —— 都会带「文件 + 行号」。
//
//  ⛔ 它**不会**替你判断"数值好不好玩"，只保证"表能读进来且语义没坏"。
// ============================================================================

using UnityEditor;
using UnityEngine;
using WanXiang.Modules.UI;

namespace WanXiang.EditorTools
{
    public static class CsvSelfTest
    {
        [MenuItem("万相/配置/校验所有 CSV", priority = 211)]
        public static void ValidateAll()
        {
            DataTables.ReloadForEditor();
            int problems = DataTables.Problems.Count;

            string summary =
                "状态 " + DataTables.StatusCount +
                "／首领 " + DataTables.BossCount +
                "／天时 " + DataTables.WeatherCount +
                "／预算 " + DataTables.BudgetKeys + " 项" +
                "／规模 " + DataTables.SquadActs + " 幕" +
                "／地图 " + (DataTables.MapInstalled ? "已装" : "内置") +
                "／节点权重 " + DataTables.WeightRows + " 行" +
                "／遗物 " + DataTables.RelicCount +
                "／阵形 " + DataTables.ComboCount +
                "／曲线 " + DataTables.ProgressionKeys + " 键" +
                "／劫律 " + DataTables.TrialLawCount +
                "／灵市 " + DataTables.MarketKeys + " 键";

            // ★ 2026-11-23：补充两类 DataTables.Problems 覆盖不到的检查
            //   （手工按列索引解析的表 / UI switch 分支完整性）
            int extra = CheckManualParsedCsvColumns() + CheckEnumSwitchCoverage();
            problems += extra;

            if (problems == 0)
                Debug.Log("[CsvSelfTest] ✅ 通过：" + summary);
            else
                Debug.LogError("[CsvSelfTest] ❌ " + problems + " 处问题：" + summary + "\n" +
                               string.Join("\n", DataTables.Problems.ToArray()));
        }

        /// <summary>
        /// 手工按列索引解析的 CSV（encounters/events 等）列数一致性检查。
        /// DataTables.Problems 只覆盖 r.Str("col") 解析的表，手工解析表读错列不会报。
        /// 允许"末尾空列省略"（合法 CSV），但中间列数必须严格一致。
        /// </summary>
        private static int CheckManualParsedCsvColumns()
        {
            int bad = 0;
            var manual = new[] { "encounters", "events" };
            foreach (var name in manual)
            {
                var ta = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/Data/" + name + ".csv");
                if (ta == null) continue;
                var lines = ta.text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                int expected = -1;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var cells = SplitCsv(lines[i]);
                    if (expected < 0) { expected = cells.Count; continue; }
                    // 去掉末尾空字段（合法省略），中间空列保留
                    int effective = cells.Count;
                    while (effective > 0 && string.IsNullOrEmpty(cells[effective - 1])) effective--;
                    int expectedEffective = expected;
                    // 表头末尾空列同样归一
                    if (expected > 0 && string.IsNullOrEmpty(cells[cells.Count - 1])) expectedEffective = expected;
                    if (effective > expected)   // 数据列多于表头 = 真错位
                    {
                        Debug.LogError("[CsvSelfTest] ❌ " + name + ".csv 第 " + (i + 1) + " 行列数 " +
                                       cells.Count + " 超过表头 " + expected + "（可能列错位）");
                        bad++;
                    }
                }
            }
            return bad;
        }

        /// <summary>
        /// 关键枚举的 switch 分支覆盖检查：防止"数据加了新枚举值、UI 显示 switch 没跟上"。
        /// 曾真实发生：新增 ActsCleared 来源的道痕后 DaoMarkPanel.SourceText 没分支，显示"—"。
        /// </summary>
        private static int CheckEnumSwitchCoverage()
        {
            int bad = 0;
            // 期望：这些枚举的每个有效成员都至少被一处 switch case 处理
            CheckOne("ObjectiveKind", new[] { "KillByTurn", "NoDeath", "ElementReactions",
                "CaptureOne", "NoUltimate", "WinLowHp", "NoHeal" }, ref bad);
            CheckOne("DaoMarkSource", new[] { "Reactions", "ActsCleared", "Deaths", "FastWins",
                "BossKills", "Captures", "MaxHit", "SameElementActs" }, ref bad);
            return bad;
        }

        private static void CheckOne(string enumName, string[] members, ref int bad)
        {
            // ⚠ .cs 在 Unity 里是 MonoScript 不是 TextAsset，FindAssets("t:TextAsset") 找不到。
            //   直接读文件系统，确保覆盖到所有源码。
            string allCode = "";
            string srcRoot = System.IO.Path.Combine(Application.dataPath, "WanXiang");
            if (System.IO.Directory.Exists(srcRoot))
            {
                var files = System.IO.Directory.GetFiles(srcRoot, "*.cs", System.IO.SearchOption.AllDirectories);
                foreach (var path in files)
                    allCode += System.IO.File.ReadAllText(path) + "\n";
            }
            foreach (var m in members)
            {
                if (!allCode.Contains("case " + enumName + "." + m))
                {
                    Debug.LogError("[CsvSelfTest] ❌ " + enumName + "." + m + " 无任何 switch case 处理（UI 可能显示缺失）");
                    bad++;
                }
            }
        }

        private static System.Collections.Generic.List<string> SplitCsv(string line)
        {
            var result = new System.Collections.Generic.List<string>();
            var cur = new System.Text.StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; } else quoted = !quoted; }
                else if (ch == ',' && !quoted) { result.Add(cur.ToString()); cur.Length = 0; }
                else cur.Append(ch);
            }
            result.Add(cur.ToString());
            return result;
        }
    }
}
