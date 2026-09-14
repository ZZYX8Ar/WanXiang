// ============================================================================
//  万相 · 节点图视图数据（灰盒窗口的"数据层"）
//  ---------------------------------------------------------------------------
//  窗口（NodeMapWindow）只负责画；**要显示什么文本、哪些分支是合法的、
//  余气还剩几个节点**全在这里算。这么分层有两个实际好处：
//    ① 自检可以直接断言这些文本（窗口本身没法自动判据）；
//    ② 将来换成正式 UI（UIToolkit / 运行时棋盘）时，这一层原样复用。
//
//  ⚠ 节点"已过/当前/可选"的来源：RunState 只记账 Path（节气序号序列）与
//    CurrentOffset。当前幕已过的**下标**由 Path 尾部反推（Boss 不入 Path，
//    所以 Path 末尾 VisitedInAct 个就是本幕走过的节点）。这条推导写在一处，
//    别在窗口里再推一遍。
// ============================================================================

using System;
using System.Collections.Generic;
using WanXiang.Battle.Core;
using WanXiang.Campaign;

namespace WanXiang.Editor.CampaignTool
{
    public static class NodeMapView
    {
        /// <summary>本幕已过的节点下标（由 Path 尾部反推，见文件头）。</summary>
        public static List<int> VisitedOffsets(ActGraph g, RunState st)
        {
            var result = new List<int>(g.PathLength);
            if (g.IsEmpty || st.VisitedInAct <= 0) return result;
            var terms = g.Terms;
            int from = st.Path.Count - st.VisitedInAct;
            for (int i = System.Math.Max(0, from); i < st.Path.Count; i++)
                for (int t = 0; t < terms.Length; t++)
                    if (terms[t] == st.Path[i]) result.Add(t);
            return result;
        }

        /// <summary>节点标记：已过 ✔ / 当前 ◀ / 可选 ○ / 未开放 ·。</summary>
        public static string MarkOf(ActGraph g, int offset, RunState st)
        {
            if (offset == st.CurrentOffset) return "◀";
            if (VisitedOffsets(g, st).Contains(offset)) return "✔";
            if (!st.AtBoss && st.CurrentLayer + 1 == g.LayerOf(offset)) return "○";
            return "·";
        }

        /// <summary>节点名 + 天时（未翻译的节点明说，别让测试者以为是空规则）。</summary>
        public static string NodeLabel(ActGraph g, int offset, Func<int, WeatherDef> weather)
        {
            int term = g.Terms[offset];
            var w = weather?.Invoke(term);
            string name = SolarTermName(term);
            if (w == null) return $"{term:00} {name}（天时未翻译）";
            return $"{term:00} {name} · {w.BuffName}";
        }

        /// <summary>一行的节点文本（带标记），用于画图。</summary>
        public static string NodeLine(ActGraph g, int offset, RunState st, Func<int, WeatherDef> weather)
            => $"{MarkOf(g, offset, st)} {NodeLabel(g, offset, weather)}";

        /// <summary>本幕每一层的节点行（层与层之间缩进表示上下游）。</summary>
        public static List<string> ActLines(ActGraph g, RunState st, Func<int, WeatherDef> weather)
        {
            var lines = new List<string>();
            if (g.IsEmpty)
            {
                lines.Add("（空幕：只有守关）");
                return lines;
            }
            for (int layer = 0; layer < g.Layers.Length; layer++)
            {
                var layerText = new System.Text.StringBuilder();
                var nodes = g.Layers[layer];
                for (int i = 0; i < nodes.Length; i++)
                {
                    if (i > 0) layerText.Append("        ");
                    layerText.Append(NodeLine(g, nodes[i], st, weather));
                }
                lines.Add($"第 {layer + 1} 层　{layerText}");
            }
            return lines;
        }

        /// <summary>下一步合法下标（当前层 +1 那一层的全部节点）。</summary>
        public static List<int> LegalNextOffsets(ActGraph g, RunState st)
        {
            var result = new List<int>();
            if (g.IsEmpty || st.AtBoss) return result;
            int next = st.CurrentLayer + 1;
            foreach (var offset in g.Layers[next]) result.Add(offset);
            return result;
        }

        /// <summary>余气面板：来源节气 + 天时名 + 还剩几个节点生效。</summary>
        public static List<string> LingerLines(RunState st)
        {
            var lines = new List<string>();
            var lingers = st.Lingers;
            if (lingers == null || lingers.Count == 0)
            {
                lines.Add("（无余气：本幕前两个节点不受上一季影响）");
                return lines;
            }
            for (int i = 0; i < lingers.Count; i++)
            {
                var l = lingers[i];
                string name = l.Weather != null ? l.Weather.BuffName : "（空）";
                int applies = System.Math.Max(0, l.NodesLeft + 1);   // -1 才移除 ⇒ 生效数 = 计数 + 1
                lines.Add($"余气 · {SolarTermName(l.FromTerm)}　{name}"
                        + $"（强度减半，还剩 {applies} 个节点）");
            }
            return lines;
        }

        /// <summary>战斗记录行（窗口列表与自检报告共用）。</summary>
        public static List<string> RecordLines(IReadOnlyList<BattleRecord> records)
        {
            var lines = new List<string>();
            if (records == null) return lines;
            for (int i = 0; i < records.Count; i++) lines.Add(records[i].Describe());
            return lines;
        }

        /// <summary>状态行：幕 / 已走节点 / 是否到守关 / 终局。</summary>
        public static string StatusLine(RunState st, RunOutcome outcome)
        {
            var g = st.CurrentGraph;
            string where = st.Finished ? "已通关"
                         : st.AtBoss ? "守关点（下一场是守关战）"
                         : $"第 {st.CurrentLayer + 2} 层待选（已走 {st.VisitedInAct} 个节点）";
            string outcomCn = outcome == RunOutcome.Completed ? "通关"
                            : outcome == RunOutcome.Defeated ? "败北" : "进行中";
            return $"第 {st.CurrentAct} 幕 · {g.SeasonCn}｜{where}｜局：{outcomCn}｜共 {st.Path.Count} 个节点已过";
        }

        /// <summary>节气序号 → 中文名。窗口与报告都需要，别各写一份。</summary>
        public static string SolarTermName(int term)
        {
            switch (term)
            {
                case 1: return "立春";   case 2: return "雨水";   case 3: return "惊蛰";
                case 4: return "春分";   case 5: return "清明";   case 6: return "谷雨";
                case 7: return "立夏";   case 8: return "小满";   case 9: return "芒种";
                case 10: return "夏至";  case 11: return "小暑";  case 12: return "大暑";
                case 13: return "立秋";  case 14: return "处暑";  case 15: return "白露";
                case 16: return "秋分";  case 17: return "寒露";  case 18: return "霜降";
                case 19: return "立冬";  case 20: return "小雪";  case 21: return "大雪";
                case 22: return "冬至";  case 23: return "小寒";  case 24: return "大寒";
                default: return "？";
            }
        }

        /// <summary>敌方阵容预览（"进这个节点要打谁"）：职业 × 数量，按站位顺序。</summary>
        public static string EnemyPreview(DeployEntry[] squad)
        {
            if (squad == null || squad.Length == 0) return "（无敌人配置）";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < squad.Length; i++)
            {
                if (i > 0) sb.Append('、');
                var d = squad[i].Def;
                sb.Append(d == null ? "（空）" : $"{Cn.Of(d.Element)}{d.DisplayName}");
            }
            return sb.ToString();
        }
    }
}
