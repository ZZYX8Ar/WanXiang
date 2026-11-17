// ============================================================================
//  万相 · 数据解析与美术工具（2026-11-17 代码骨架重构）
//  从 CampaignPanel.cs 原样搬入，逻辑零改动。
// ============================================================================
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Modules.UI
{
    public sealed partial class CampaignPanel
    {
        private static bool _eventsCsvLoaded;

        /// <summary>读取 Resources/Data/events.csv（放在本类内，避免新增顶层类型导致 Unity 解析不到）。</summary>
        private static void EnsureEventsLoadedFromCsv()
        {
            if (_eventsCsvLoaded) return;
            _eventsCsvLoaded = true;
            var ta = UnityEngine.Resources.Load<UnityEngine.TextAsset>("Data/events");
            if (ta == null) { Debug.LogWarning("[Campaign] 找不到 events.csv，用内置事件"); return; }

            var order = new System.Collections.Generic.List<string>();
            var choices = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<WanXiang.Campaign.EventChoice>>();
            var meta = new System.Collections.Generic.Dictionary<string, WanXiang.Campaign.EventDef>();
            var lines = ta.text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int bad = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var c = SplitCsvLine(lines[i]);
                if (c.Count < 7) { bad++; continue; }
                string id = c[0].Trim();
                if (!choices.ContainsKey(id))
                {
                    order.Add(id);
                    choices[id] = new System.Collections.Generic.List<WanXiang.Campaign.EventChoice>();
                    meta[id] = new WanXiang.Campaign.EventDef { Id = id, Title = c[1].Trim(), Story = c[2].Trim() };
                }
                WanXiang.Campaign.EventEffect eff;
                if (!System.Enum.TryParse(c[5].Trim(), out eff))
                {
                    Debug.LogError("[Campaign] events.csv 第 " + (i + 1) + " 行未知效果：" + c[5]);
                    eff = WanXiang.Campaign.EventEffect.Nothing;
                }
                choices[id].Add(new WanXiang.Campaign.EventChoice
                {
                    Text = c[4].Trim(), Effect = eff,
                    Value = int.TryParse(c[6].Trim(), out int v) ? v : 0,
                    ScopeId = c.Count > 7 ? c[7].Trim() : null,
                    NextId = c.Count > 8 ? c[8].Trim() : null,
                });
            }
            if (order.Count == 0) { Debug.LogWarning("[Campaign] events.csv 无有效行"); return; }
            var list = new System.Collections.Generic.List<WanXiang.Campaign.EventDef>(order.Count);
            foreach (var id in order) { var d = meta[id]; d.Choices = choices[id].ToArray(); list.Add(d); }
            WanXiang.Campaign.EventCatalog.Install(list.ToArray());
            Debug.Log("[Campaign] events.csv 载入 " + list.Count + " 个事件" + (bad > 0 ? "（异常 " + bad + " 行）" : ""));
        }

        private static System.Collections.Generic.List<string> SplitCsvLine(string line)
        {
            var outp = new System.Collections.Generic.List<string>(10);
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

        /// <summary>按该层节点数算卡宽：4 节点收窄到 326，避免最左/最右被裁（用户 11-14）。</summary>
        private static float NodeWidthFor(int count)
        {
            if (count <= 1) return NodeW;
            float w = (NodeAreaW - (count - 1) * GapX) / count;
            return Mathf.Min(NodeW, Mathf.Floor(w));
        }   // 2026-10-10 节点加大、间距收窄（配合 Scroll_Nodes 加宽到 1376，三列不再被裁）
        private static readonly Color EdgeInk = new Color(0.72f, 0.68f, 0.60f, 0.9f);
        private static Color PathGold = new Color(0.79f, 0.63f, 0.39f, 1f);

        private static bool _inkColorsFixed;

        /// <summary>
        /// 把节点文字色从 sRGB 转到当前色彩空间（Linear 下必须转，否则显示偏亮偏淡）。
        /// ⛔ 只能在这里（运行时）调 UiInk，不能在静态字段初始化器里调 —— 见上方注释。
        /// </summary>
        private static void EnsureInkColors()
        {
            if (_inkColorsFixed) return;
            _inkColorsFixed = true;
            NodeVisited = UiInk.Srgb(0.79f, 0.63f, 0.39f);
            NodeReachable = UiInk.Srgb(0.47f, 0.57f, 0.38f);
            NodeLocked = UiInk.Srgb(0.30f, 0.27f, 0.24f);
            PathGold = UiInk.Srgb(0.79f, 0.63f, 0.39f, 1f);
        }
        private static readonly Color QuestionInk = new Color(0.45f, 0.42f, 0.62f, 1f);

        // ★ 节点/连线按【幕=季节】换风格（用户要求）：春绿 / 夏深绿 / 秋赭金 / 冬冰蓝 / 长夏土黄。
        private static readonly Color[] SeasonPalette =
        {
            new Color(0.44f, 0.66f, 0.38f),   // 幕1 春
            new Color(0.28f, 0.55f, 0.36f),   // 幕2 夏
            new Color(0.80f, 0.55f, 0.22f),   // 幕3 秋
            new Color(0.45f, 0.62f, 0.78f),   // 幕4 冬
            new Color(0.70f, 0.53f, 0.30f),   // 幕5 长夏
        };

        /// <summary>本幕季节下标 0..4（由 _graph.Act 推得）。</summary>
        private int SeasonIdx
        {
            get { int a = _graph != null ? _graph.Act : 1; return Mathf.Clamp(a - 1, 0, SeasonPalette.Length - 1); }
        }
        /// <summary>本幕季节强调色（节点徽 + 连线）。</summary>
        private Color SeasonAccent { get { return SeasonPalette[SeasonIdx]; } }

        // 节点季节徽（Resources/UI/Campaign/node_s1..5：花/竹/枫/雪/土）。缺图则保留 prefab 默认。
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> _kindIconCache
            = new System.Collections.Generic.Dictionary<string, Sprite>(16);

        /// <summary>节点类型图标（node_kind_*.png；缺失返回 null → 回退季节徽）。</summary>
        private static Sprite KindIconSprite(string kindName)
        {
            if (string.IsNullOrEmpty(kindName)) return null;
            Sprite s;
            if (_kindIconCache.TryGetValue(kindName, out s)) return s;
            s = UnityEngine.Resources.Load<Sprite>("UI/Campaign/node_kind_" + kindName);
            _kindIconCache[kindName] = s;
            return s;
        }

        private static Sprite[] _seasonNodeSprites;
        private static Sprite SeasonNodeSpriteFor(int idx)
        {
            if (_seasonNodeSprites == null) _seasonNodeSprites = new Sprite[5];
            if (idx < 0 || idx >= _seasonNodeSprites.Length) idx = 0;
            if (_seasonNodeSprites[idx] == null)
                _seasonNodeSprites[idx] = UnityEngine.Resources.Load<Sprite>("UI/Campaign/node_s" + (idx + 1));
            return _seasonNodeSprites[idx];
        }
        private Sprite SeasonNodeSprite() { return SeasonNodeSpriteFor(SeasonIdx); }

        // 本幕季节「节点卡」（Resources/UI/Campaign/node_card_1..5：纸卡 + 四角季节装饰）。
        private static Sprite[] _seasonCards;
        private static Sprite SeasonCardSpriteFor(int idx)
        {
            if (_seasonCards == null) _seasonCards = new Sprite[5];
            if (idx < 0 || idx >= _seasonCards.Length) idx = 0;
            if (_seasonCards[idx] == null)
                _seasonCards[idx] = UnityEngine.Resources.Load<Sprite>("UI/Campaign/node_card_" + (idx + 1));
            return _seasonCards[idx];
        }
        // 本幕季节「连线」（Resources/UI/Campaign/line_s1..5：笔触墨线，春软/夏满/秋飞白/冬冽/长夏厚）。
        private static Sprite[] _seasonLines;
        private static Sprite SeasonLineSpriteFor(int idx)
        {
            if (_seasonLines == null) _seasonLines = new Sprite[5];
            if (idx < 0 || idx >= _seasonLines.Length) idx = 0;
            if (_seasonLines[idx] == null)
                _seasonLines[idx] = UnityEngine.Resources.Load<Sprite>("UI/Campaign/line_s" + (idx + 1));
            return _seasonLines[idx];
        }

        private readonly HashSet<int> _visited = new HashSet<int>();
        private WanXiang.Campaign.ActGraph[] _acts;
        private WanXiang.Campaign.ActGraph _graph;

        // =====================================================================
        //  ★ 秘境 = 「特殊的一幕」（用户 2026-10-16："你直接把这些秘境想象成特殊的幕不就行了吗"）
        //  ---------------------------------------------------------------------------
        //  秘境内部图本来就是一张 <see cref="ActGraph"/>（RealmMapBuilder 产出，与天阙图同类型），
        //  而这个面板的图**只有一个来源**：<c>SolarTermGraph.BuildRoute</c>。
        //  所以"秘境和节点地图一模一样"这件事，根本不需要另写一套地图 ——
        //  只要**把图换掉**，剩下所有东西（排布/连线/三态/落印/选中/出征）自动就是主线那套。
        //
        //  用法：<c>CampaignPanel.OpenAsRealm(graph)</c> 打开即可，行为差异只有三处：
        //     · 图来自传入的 graph，而不是 BuildRoute
        //     · 不做"通过本幕 ⇒ 推进 Act"（秘境打完不推进主线）
        //     · 多一个「撤离秘境」按钮（回滚场域状态 + 回原节点）
        // =====================================================================

        /// <summary>待注入的秘境图（static：面板是 Transient，跨实例传参最省事）。</summary>
        private static WanXiang.Campaign.ActGraph _injectedGraph;
   /// <summary>本次打开要用的外部图（由 payload / static 注入，见 OnOpenAsync）。</summary>
   private WanXiang.Campaign.ActGraph _realmGraphPayload;

        /// <summary>当前是否处于"秘境模式"（外部注入的图在用）。</summary>
        private bool _realmMode;

        /// <summary>
        /// 以"秘境模式"打开路线图。传进来的 graph 会被整个面板当成"本幕的图"使用。
    }
}
