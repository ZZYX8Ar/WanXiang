// 事件表加载（2026-11-02）：Resources/Data/events.csv → EventCatalog.Install
using System.Collections.Generic;
using UnityEngine;
using WanXiang.Campaign;

namespace WanXiang.Modules.UI
{
    public static class EventBootstrap
    {
        private static bool _done;

        public static void EnsureInstalled()
        {
            if (_done) return;
            _done = true;

            var ta = Resources.Load<TextAsset>("Data/events");
            if (ta == null) { Debug.LogWarning("[Event] 找不到 events.csv，用内置表"); return; }

            var order = new List<string>();
            var map = new Dictionary<string, List<EventChoice>>();
            var meta = new Dictionary<string, EventDef>();

            var lines = ta.text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var c = SplitCsv(lines[i]);
                if (c.Count < 8 || string.IsNullOrEmpty(c[0])) { Debug.LogError("[Event] 第 " + (i + 1) + " 行列数不足"); continue; }
                string id = c[0].Trim();
                if (!map.ContainsKey(id))
                {
                    order.Add(id);
                    map[id] = new List<EventChoice>();
                    meta[id] = new EventDef { Id = id, Title = c[1].Trim(), Story = c[2].Trim() };
                }
                if (!System.Enum.TryParse(c[5].Trim(), out EventEffect eff))
                {
                    Debug.LogError("[Event] 第 " + (i + 1) + " 行未知效果：" + c[5]);
                    eff = EventEffect.Nothing;
                }
                map[id].Add(new EventChoice
                {
                    Text = c[4].Trim(),
                    Effect = eff,
                    Value = int.TryParse(c[6].Trim(), out int v) ? v : 0,
                    ScopeId = c.Count > 7 ? c[7].Trim() : null,
                });
            }

            var list = new List<EventDef>(order.Count);
            foreach (var id in order)
            {
                var d = meta[id];
                d.Choices = map[id].ToArray();
                list.Add(d);
            }
            if (list.Count == 0) { Debug.LogWarning("[Event] CSV 无有效事件"); return; }
            EventCatalog.Install(list.ToArray());
            Debug.Log("[Event] 从 CSV 载入 " + list.Count + " 个事件");
        }

        private static List<string> SplitCsv(string line)
        {
            var outp = new List<string>(10);
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
