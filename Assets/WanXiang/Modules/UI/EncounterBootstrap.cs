// ============================================================================
//  遭遇表加载 + 数据校验（2026-11-02）
//  ---------------------------------------------------------------------------
//  Resources/Data/encounters.csv → EncounterTable.Install
//  同时做一次**引用校验**：敌人 id 必须能在 BossCatalog 里查到，否则 Console 报错。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;
using WanXiang.Campaign;

namespace WanXiang.Modules.UI
{
    public static class EncounterBootstrap
    {
        private static bool _done;

        public static void EnsureInstalled()
        {
            if (_done) return;
            _done = true;

            var ta = Resources.Load<TextAsset>("Data/encounters");
            if (ta == null) { Debug.LogWarning("[Encounter] 找不到 Resources/Data/encounters.csv，沿用代码内置配置"); return; }

            var rows = new List<EncounterDef>();
            var lines = ta.text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int bad = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                var c = SplitCsv(line);
                if (c.Count < 4) { Debug.LogError("[Encounter] 第 " + (i + 1) + " 行列数不足，跳过"); bad++; continue; }

                var def = new EncounterDef
                {
                    Act = int.TryParse(c[0].Trim(), out int a) ? a : 0,
                    NodeKind = c[1].Trim(),
                    EnemyIds = ParseComp(c[2]),
                    Scale = float.TryParse(c[3].Trim(), System.Globalization.NumberStyles.Float,
                                           System.Globalization.CultureInfo.InvariantCulture, out float s) ? s : 1f,
                };
                if (def.Act <= 0 || def.EnemyIds.Length == 0)
                {
                    Debug.LogError("[Encounter] 第 " + (i + 1) + " 行数据非法（act/comp）"); bad++; continue;
                }
                for (int e = 0; e < def.EnemyIds.Length; e++)
                {
                    if (WanXiang.Battle.Core.BossCatalog.BuildBoss(def.EnemyIds[e]) == null)
                        Debug.LogError("[Encounter] 第 " + (i + 1) + " 行敌人 id 不存在：" + def.EnemyIds[e]);
                }
                rows.Add(def);
            }

            EncounterTable.Install(rows);
            Debug.Log("[Encounter] 载入 " + rows.Count + " 行" + (bad > 0 ? "，异常 " + bad + " 行" : ""));
        }

        // comp 形如 "b_fuman:3,b_manman:1"（CSV 里整体用引号包住），展开成重复 id 数组
        private static string[] ParseComp(string comp)
        {
            var list = new List<string>(4);
            if (string.IsNullOrEmpty(comp)) return list.ToArray();
            var parts = comp.Trim().Trim('"').Split(',');
            foreach (var p in parts)
            {
                var seg = p.Trim();
                if (seg.Length == 0) continue;
                int mul = 1;
                int ci = seg.LastIndexOf(':');
                if (ci > 0 && int.TryParse(seg.Substring(ci + 1), out int m)) { mul = Mathf.Clamp(m, 1, 9); seg = seg.Substring(0, ci); }
                for (int k = 0; k < mul; k++) list.Add(seg);
            }
            return list.ToArray();
        }

        private static List<string> SplitCsv(string line)
        {
            var outp = new List<string>(6);
            var sb = new System.Text.StringBuilder();
            bool quote = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    if (quote && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else quote = !quote;
                }
                else if (ch == ',' && !quote) { outp.Add(sb.ToString()); sb.Length = 0; }
                else sb.Append(ch);
            }
            outp.Add(sb.ToString());
            return outp;
        }
    }
}
