// ============================================================================
//  技能表加载（2026-11-02）
//  ---------------------------------------------------------------------------
//  数据源：`Assets/Resources/Data/skills.csv`（Excel 可直接打开/另存为 CSV UTF-8）。
//  运行时解析 → `SkillOverrideCatalog.Install(...)`，只覆盖克隆体，不动 asset。
//  ⛔ 不做"编辑器导入"这一步：少一个环节就少一处不同步。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;
using WanXiang.Campaign;

namespace WanXiang.Modules.UI
{
    public static class SkillOverrideBootstrap
    {
        private static bool _done;

        public static void EnsureInstalled()
        {
            if (_done) return;
            _done = true;

            var ta = Resources.Load<TextAsset>("Data/skills");
            if (ta == null) { Debug.LogWarning("[SkillOverride] 找不到 Resources/Data/skills.csv，用内置表"); return; }

            var list = new List<SkillOverride>();
            var lines = ta.text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 1; i < lines.Length; i++)            // 第 0 行是表头
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                var c = SplitCsv(line);
                if (c.Count < 7 || string.IsNullOrEmpty(c[1])) continue;
                while (c.Count < 8) c.Add("");
                list.Add(new SkillOverride
                {
                    Enemy = c[0].Trim().Equals("Enemy", System.StringComparison.OrdinalIgnoreCase),
                    BeastId = c[1].Trim(),
                    Slot = ParseInt(c[2]),
                    Name = c[3].Trim(),
                    Desc = c[4].Trim(),
                    PowerMul = ParseFloat(c[5], 1f),
                    HitsDelta = ParseInt(c[6]),
                    Target = c[7].Trim(),
                });
            }

            if (list.Count == 0) { Debug.LogWarning("[SkillOverride] CSV 没有有效行"); return; }
            // ★ 2026-11-02 自检：重复 (id,slot)、槽位越界、空名字/空描述 —— 改表后立刻能看出问题
            {
                var seen = new HashSet<string>();
                int bad = 0;
                foreach (var o in list)
                {
                    string key = (o.Enemy ? "E:" : "P:") + o.BeastId + ":" + o.Slot;
                    if (!seen.Add(key)) { Debug.LogError("[SkillOverride] 重复条目 " + key); bad++; }
                    if (o.Slot < 0 || o.Slot > 2) { Debug.LogError("[SkillOverride] 槽位越界 " + key); bad++; }
                    if (string.IsNullOrEmpty(o.Name)) { Debug.LogError("[SkillOverride] 缺技能名 " + key); bad++; }
                    if (string.IsNullOrEmpty(o.Desc)) { Debug.LogError("[SkillOverride] 缺技能描述 " + key); bad++; }
                }
                Debug.Log("[SkillOverride] 校验完成：异常 " + bad + " 条");
            }

            SkillOverrideCatalog.Install(list.ToArray());
            Debug.Log("[SkillOverride] 已从 CSV 载入 " + list.Count + " 条技能覆盖");
        }

        /// <summary>支持引号包裹与逗号转义的极简 CSV 切分（够用即可）。</summary>
        private static List<string> SplitCsv(string line)
        {
            var outp = new List<string>(8);
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

        private static int ParseInt(string s) => int.TryParse(s.Trim(), out int v) ? v : 0;
        private static float ParseFloat(string s, float fallback)
            => float.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
    }
}
