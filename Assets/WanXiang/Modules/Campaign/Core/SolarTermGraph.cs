// ============================================================================
//  万相 · 节气节点图（GDD 3.1/3.2，STEP 3）
//  ---------------------------------------------------------------------------
//  「二十四节气是地图结构」：每幕 6 个节气构成一张小型分叉图（类《杀戮尖塔》），
//  玩家只经过 4 个节点即抵达守关 —— 每幕 2 个节点被永久放弃。
//
//  ⚠ 拓扑：4 层 [2,2,1,1]，相邻层全连通 —— 每幕恰好 4 条完整路径，
//    任何一条都以下标 5（土节点：谷雨/大暑/霜降/大寒）收尾。
//    这与 GDD v1.1 §4.2 的 `SolarTermGraph.Layers = [{0,1},{2,3},{4},{5}]` 完全一致。
//
//  ⭐ v1.1 给这张图加了一条**硬约束**（决定单局场次 13~17 的关键）：
//    ① 第一层与第二层**各自恰好含 1 场战斗**（另一个选项是非战斗节点）
//       —— 玩家永远有"打法"与"绕法"两条路，且两条路拿到的战斗只差 1 场；
//    ② 第三层**固定为精英战** —— 每一幕都有一场不可回避的强度检定；
//    ③ 第四层**固定为非战斗节点**，且必是土属性季末节点（传统历法每季末十八天土旺）。
//    ⇒ 每幕战斗 2~3 场，四幕 8~12 场；加 4 场守关与 1 场天阙 = **单局 13~17 场**。
//    （v1.0 写的"16 常�� + 5 守关 = 21"是把 16 个节点全当战斗节点算的，GDD v1.1
//      明确纠正了这一点。）
//
//  幕 5（长夏 · 后土）没有节点 —— 它只是终局守关，这在 solar_terms.json 里
//  就是 declaredNodeCount = 0。
// ============================================================================

using System.Collections.Generic;
using WanXiang.Battle.Core;

namespace WanXiang.Campaign
{
    /// <summary>
    /// 七种节点类型（GDD v1.1 §4.3）。v1.0 只有"节点"一个概念，
    /// v1.1 拆成七种 —— 它说得很直白：「节点类型的分布，比节点的天时更能决定一局的体验」。
    /// </summary>
    public enum NodeKind
    {
        /// <summary>遭遇：常规战斗。敌方按幕数与节点类型的规模表定（3~5 只）。</summary>
        Encounter = 0,

        /// <summary>精英：战斗。规模 +1，其中 1 只带「劫象」额外特性，属性 ×1.18。</summary>
        Elite = 1,

        /// <summary>灵市：非战斗。用灵卵买异兽/灵魂/技能重铸，可刷新 1 次。</summary>
        Shop = 2,

        /// <summary>孵穴：非战斗。二选一：回复全队 40% 生命 ／ 取 2 枚灵卵。</summary>
        Nest = 3,

        /// <summary>异闻：非战斗。典籍轶事 + 三选一（选项有代价，可拒绝换 1 灵卵）。</summary>
        Tale = 4,

        /// <summary>铸魂台：非战斗。免费融合一次（宿主 + 灵魂）+ 赠 1 个随机灵魂。</summary>
        Forge = 5,

        /// <summary>天象：非战斗。三选一，每个增益都配一条明确的负面。</summary>
        Omen = 6,

        /// <summary>
        /// 问号：**未知**（v1.2 新增）。位置在生成时就定了，内容等玩家走上去才揭晓。
        /// 揭晓池不含精英 —— 未知带来的是期待，不是惩罚。
        /// </summary>
        Question = 7,
    }

    public static class NodeKinds
    {
        /// <summary>这个节点要不要打仗 —— 单局场次就是数它。</summary>
        public static bool IsBattle(NodeKind k) => k == NodeKind.Encounter || k == NodeKind.Elite;

        public static string Cn(NodeKind k)
        {
            switch (k)
            {
                case NodeKind.Encounter: return "遭遇";
                case NodeKind.Elite: return "精英";
                case NodeKind.Shop: return "灵市";
                case NodeKind.Nest: return "孵穴";
                case NodeKind.Tale: return "异闻";
                case NodeKind.Forge: return "铸魂台";
                case NodeKind.Omen: return "天象";
                case NodeKind.Question: return "？";
                default: return "？";
            }
        }
    }
    /// <summary>一幕的节点图。数据全部来自 GDD 3.3 / solar_terms.json。</summary>
    public sealed class ActGraph
    {
        public int Act;                  // 1..5
        public string SeasonCn;          // 春 / 夏 / 秋 / 冬 / 长夏
        public WanXiang.Battle.Core.Element SeasonElement;
        public string BossName;
        public int[] Terms;              // 节气序号（GDD 3.3 的 01..24）；空幕为空数组

        /// <summary>逐节点的类型（与 <see cref="Terms"/> 等长，来自 v1.1 §4.2 的落位表）。</summary>
        public NodeKind[] Kinds;

        /// <summary>分层拓扑：每层含哪些节点（下标指 <see cref="Terms"/>）。</summary>
        public int[][] Layers;

        /// <summary>
        /// v1.2：**真拓扑边表**。Edges[offset] = 该节点可前往的下一层节点下标列表。
        /// 解锁规则跟着它走（"连线的那个才解锁"），不再是"相邻层全连通"的占位规则。
        /// </summary>
        public List<List<int>> Edges;

        /// <summary>两个节点之间有没有边（解锁判据）。</summary>
        public bool HasEdge(int fromOffset, int toOffset)
        {
            if (Edges == null || fromOffset < 0 || fromOffset >= Edges.Count) return false;
            return Edges[fromOffset].Contains(toOffset);
        }

        public int NodeCount => Terms.Length;

        /// <summary>一条完整路径要经过的节点数 = 层数（每层选一个）。</summary>
        public int PathLength => Layers.Length;

        public bool IsEmpty => NodeCount == 0;

        /// <summary>某节点在第几层。-1 = 越界。</summary>
        public int LayerOf(int offset)
        {
            for (int l = 0; l < Layers.Length; l++)
                for (int i = 0; i < Layers[l].Length; i++)
                    if (Layers[l][i] == offset) return l;
            return -1;
        }

        /// <summary>能否从 from 走到 to：只能是**相邻下一层**的任意节点（v1 全连通）。</summary>
        public bool CanMove(int fromOffset, int toOffset)
        {
            int lf = LayerOf(fromOffset);
            return lf >= 0 && LayerOf(toOffset) == lf + 1;
        }

        /// <summary>某节点的类型（越界返回 Encounter，调用方自己保证下标合法）。</summary>
        public NodeKind KindOf(int offset)
            => Kinds != null && offset >= 0 && offset < Kinds.Length ? Kinds[offset] : NodeKind.Encounter;

        /// <summary>这个节点要不要打仗。</summary>
        public bool IsBattleOffset(int offset) => NodeKinds.IsBattle(KindOf(offset));

        /// <summary>
        /// v1.1 §4.2 三条硬约束的自检判据（campaign.selftest 用它）：
        /// 一/二层各恰 1 场战斗、三层只有精英、四层只有非战斗节点。
        /// </summary>
        public bool MeetsV11Constraints(out string why)
        {
            why = null;
            if (IsEmpty) return true;   // 空幕（天阙）没有节点布局，约束只作用于四季幕
            if (Layers.Length != 4) { why = "层数不是 4"; return false; }
            for (int layer = 0; layer < 2; layer++)
            {
                int battles = 0;
                foreach (var off in Layers[layer]) if (IsBattleOffset(off)) battles++;
                if (battles != 1) { why = $"第 {layer + 1} 层战斗数 {battles} ≠ 1"; return false; }
            }
            if (Layers[2].Length != 1 || KindOf(Layers[2][0]) != NodeKind.Elite)
            { why = "第三层不是单个精英"; return false; }
            foreach (var off in Layers[3])
                if (IsBattleOffset(off)) { why = "第四层出现了战斗节点"; return false; }
            if (!EndsAtEarthTerm) { why = "第四层不是季末土节点"; return false; }
            return true;
        }

        /// <summary>起始层（第一层）的节点下标。</summary>
        public int[] StartOffsets => Layers[0];

        /// <summary>收尾层是否只有一个节点且是最后一个节气 —— GDD"收尾节点必属土"的机器判据。</summary>
        public bool EndsAtEarthTerm => Layers[Layers.Length - 1].Length == 1
                                    && Layers[Layers.Length - 1][0] == NodeCount - 1;

        /// <summary>枚举全部完整路径（节点下标序列）。幕有 6 节点 4 层时恰为 4 条。</summary>
        public List<int[]> EnumeratePaths()
        {
            var result = new List<int[]>();
            if (IsEmpty) return result;
            var current = new int[Layers.Length];
            Walk(0, current, result);
            return result;
        }

        private void Walk(int layer, int[] current, List<int[]> into)
        {
            if (layer >= Layers.Length)
            {
                into.Add((int[])current.Clone());
                return;
            }
            for (int i = 0; i < Layers[layer].Length; i++)
            {
                // 全连通：上一层的任何节点都能进本层任何节点，无需查边
                current[layer] = Layers[layer][i];
                Walk(layer + 1, current, into);
            }
        }
    }

    /// <summary>
    /// 五幕缺省图。节气分幕以 solar_terms.json 为准（幕 1: 1-6、幕 2: 7-12、幕 3: 13-18、幕 4: 19-24）。
    /// ⚠ 索引一律是 **GDD 节气序号（1..24）**，不是数组下标 —— 夏至=10、冬至=22，别按直觉猜。
    /// </summary>
    public static class SolarTermGraph
    {
        /// <summary>
        /// v1.2 路线图：每幕 <paramref name="layers"/> 层（默认 12），层内 2~3 个候选，
        /// 自下而上爬；最后一层是本幕守关。同一 (act, seed) 结果完全一致 —— 可背版、可复盘。
        ///
        /// 生成后立刻跑三条保底校验（战斗 ≥4、问号 1~3 且不连续、末层唯一），
        /// 不通过就换盐重采样，最多 8 次；仍失败则退回 BuildDefault 的缺省图（保证一定能玩）。
        /// </summary>
        public static ActGraph BuildRoute(int act, ulong seed, int layers = 12)
        {
            // ★★ 第 5 幕 = 天阙：**固定 5 节点线性图**（休整 → 商店 → 熔炼 → 看护关 → 后土）。
            //    不走随机生成：这是通关前的最后一段"登天"流程，形态必须确定（用户设计）。
            if (act >= 5) return BuildFinale();

            var fallback = BuildDefault();
            var baseGraph = act >= 1 && act <= fallback.Length ? fallback[act - 1] : null;

            // ★★ 重试次数从 8 提到 64：实测有些种子连续 8 次都生成不出合规图，
            //    于是静默兜底到"缺省图"（只有 6 个节点、布局重叠），玩家看到的就是
            //    "节点地图不对/挤在一起"（用户实测）。提高重试后基本不会落到兜底。
            for (int attempt = 0; attempt < 64; attempt++)
            {
                var g = TryBuild(act, seed + (ulong)attempt * 7919UL, layers);
                if (g != null && MeetsV12Constraints(g)) return g;
            }

            // 兜底：缺省图（层数不足 12 时按 4 层用，至少能玩）。
            // ⚠ 走到这里说明 64 次都没生成出合规图 —— 一定要有日志，否则又是"静默降级"。
            // ⚠ 本程序集不引用 UnityEngine，这里只能用 System.Diagnostics。
            //    真正的"玩家可见告警"由调用方（CampaignPanel，有 UnityEngine）在
            //    建图后检查 NodeCount 时打印（见那里的"图诊断"日志）。
            System.Diagnostics.Debug.WriteLine(
                "[SolarTermGraph] act=" + act + " 生成失败（64 次重试）→ 回退缺省图，节点数=" +
                (baseGraph != null ? baseGraph.NodeCount.ToString() : "null") + "，种子=" + seed);
            return baseGraph;
        }

        private static ActGraph TryBuild(int act, ulong seed, int layers)
        {
            var existing = BuildDefault();
            var src = act >= 1 && act <= existing.Length ? existing[act - 1] : null;
            if (src == null) return null;

            var rng = new DeterministicRandom(seed);
            var kinds = new List<NodeKind>(layers * 3);
            var layerIndex = new List<int[]>(layers);
            var terms = new List<int>(layers * 3);

            for (int layer = 0; layer < layers; layer++)
            {
                bool isFirst = layer == 0;
                bool isBoss = layer == layers - 1;

                int width = isBoss ? 1 : (isFirst ? 3 : rng.NextInt(2, 4));   // 2~3
                var row = new int[width];
                for (int k = 0; k < width; k++)
                {
                    row[k] = kinds.Count;
                    // ⚠ 节气必须按幕取：幕 1 只有春（立春~谷雨 1..6）、幕 2 夏（7..12）、
                    //   幕 3 秋（13..18）、幕 4 冬（19..24）。
                    //   之前写 (layer*2+k)%24 会让第一幕冒出"大暑/霜降"，四季节气混在一起（用户抓到）。
                    int termStart = (act - 1) * 6 + 1;
                    terms.Add(termStart + ((layer + k) % 6));
                    kinds.Add(isBoss ? NodeKind.Elite : PickKind(rng, isFirst));
                }
                layerIndex.Add(row);
            }

            // ---- 真拓扑连边：本层每点连下一层"列序最近"的 1 个，50% 再连一个相邻的；
            //      最后保证下层每点至少一条入边（否则那点永远到不了）----
            var edges = new List<List<int>>(kinds.Count);
            for (int i = 0; i < kinds.Count; i++) edges.Add(new List<int>());

            for (int layer = 0; layer + 1 < layers; layer++)
            {
                var cur = layerIndex[layer];
                var nxt = layerIndex[layer + 1];
                foreach (var a in cur)
                {
                    float ai = (float)System.Array.IndexOf(cur, a) / System.Math.Max(1, cur.Length - 1);
                    int best = nxt[0]; float bd = 9f;
                    for (int j = 0; j < nxt.Length; j++)
                    {
                        float d = System.Math.Abs((float)j / System.Math.Max(1, nxt.Length - 1) - ai);
                        if (d < bd) { bd = d; best = nxt[j]; }
                    }
                    edges[a].Add(best);
                    if (nxt.Length > 1 && rng.NextInt(0, 2) == 0)
                    {
                        int alt = best == nxt[0] ? nxt[nxt.Length - 1] : nxt[0];
                        if (!edges[a].Contains(alt)) edges[a].Add(alt);
                    }
                }
                foreach (var b in nxt)
                {
                    bool has = false;
                    foreach (var a in cur) if (edges[a].Contains(b)) { has = true; break; }
                    if (!has)
                    {
                        float bf = (float)System.Array.IndexOf(nxt, b) / System.Math.Max(1, nxt.Length - 1);
                        int bestA = cur[0]; float bd2 = 9f;
                        foreach (var a in cur)
                        {
                            float ai = (float)System.Array.IndexOf(cur, a) / System.Math.Max(1, cur.Length - 1);
                            float d = System.Math.Abs(ai - bf);
                            if (d < bd2) { bd2 = d; bestA = a; }
                        }
                        edges[bestA].Add(b);
                    }
                }
            }

            var g = new ActGraph
            {
                Act = act,
                SeasonCn = src.SeasonCn,
                SeasonElement = src.SeasonElement,
                BossName = src.BossName,
                Terms = terms.ToArray(),
                Kinds = kinds.ToArray(),
                Layers = layerIndex.ToArray(),
                Edges = edges,
            };
            return g;
        }

        /// <summary>按权重抽一个节点类型。首层只出战斗 —— 开局第一脚不该是盲盒或商店。</summary>
        private static NodeKind PickKind(DeterministicRandom rng, bool firstLayer)
        {
            if (firstLayer) return NodeKind.Encounter;

            // 权重表见《节点地图设计 v1.2》第 3 节：战斗约四成、休整约六成、问号另计
            // 权重表对应《节点地图设计 v1.2》第 3 节；问号从 4% 提到 14%（用户要求增加）
            int roll = rng.NextInt(0, 100);
            if (roll < 32) return NodeKind.Encounter;
            if (roll < 44) return NodeKind.Elite;
            if (roll < 53) return NodeKind.Shop;
            if (roll < 61) return NodeKind.Nest;
            if (roll < 68) return NodeKind.Tale;
            if (roll < 74) return NodeKind.Forge;
            if (roll < 86) return NodeKind.Omen;
            return NodeKind.Question;   // 14%
        }

        /// <summary>
        /// v1.2 三条保底校验（与 v1.1 的 MeetsV11Constraints 同思路，
        /// 但 12 层随机之后「恰好一个」这种断言不再成立，只能用区间护栏）。
        /// </summary>
        public static bool MeetsV12Constraints(ActGraph g)
        {
            if (g == null || g.IsEmpty) return false;
            if (g.Layers.Length != 12) return false;

            int battles = 0, questions = 0;
            for (int i = 0; i < g.Kinds.Length; i++)
            {
                if (NodeKinds.IsBattle(g.Kinds[i])) battles++;
                if (g.Kinds[i] == NodeKind.Question) questions++;
            }

            if (battles < 4) return false;                       // 战斗保底
            if (questions < 2 || questions > 5) return false;    // 问号数量（用户要求增加：2~5）
            if (g.Layers[11].Length != 1) return false;          // 末层唯一（守关）

            // 问号不连续：相邻两层最多一个问号
            for (int l = 1; l < g.Layers.Length; l++)
            {
                int prev = 0, cur = 0;
                foreach (var o in g.Layers[l - 1]) if (g.Kinds[o] == NodeKind.Question) prev++;
                foreach (var o in g.Layers[l]) if (g.Kinds[o] == NodeKind.Question) cur++;
                if (prev > 0 && cur > 0) return false;
            }
            return true;
        }

        /// <summary>
        /// 天阙（第 5 幕）图：**固定 5 节点、逐层线性**，用户确定的设计：
        /// <code>
        ///   层1 休整（孵穴：回复全队 40% 生命 / 取 2 枚灵卵）
        ///   层2 商店（灵市：灵卵换异兽 / 灵魂 / 重铸）
        ///   层3 熔炼（铸魂台：宿主 + 灵魂融合）
        ///   层4 看护关（精英战：必须打赢才能继续）
        ///   层5 后土（终局战：由 CampaignPanel 特判触发，胜利即真通关）
        /// </code>
        /// <para>⚠ 最后一格复用 <see cref="NodeKind.Elite"/> 作为类型占位；
        /// "走到最后一格要打终局战（后土）"由 <c>CampaignPanel</c> 按 Act==5 特判。</para>
        /// </summary>
        private static ActGraph BuildFinale()
        {
            return new ActGraph
            {
                Act = 5,
                SeasonCn = "长夏",
                SeasonElement = WanXiang.Battle.Core.Element.Earth,
                BossName = "后土",
                Terms = new[] { 19, 20, 21, 22, 23 },      // 仅用于显示序号占位
                Kinds = new[]
                {
                    NodeKind.Nest,       // 休整
                    NodeKind.Shop,       // 商店
                    NodeKind.Forge,      // 熔炼
                    NodeKind.Elite,      // 看护关（精英）
                    NodeKind.Elite,      // 后土（终局战，CampaignPanel 特判）
                },
                Layers = new[] { new[] { 0 }, new[] { 1 }, new[] { 2 }, new[] { 3 }, new[] { 4 } },
                // ★★ 必须给 Edges！HasEdge 在 Edges==null 时**恒返回 false**
                //    ⇒ 整张图除了第 0 层全部不可达（用户实测："通关第一个下一个没解锁"）。
                Edges = new System.Collections.Generic.List<System.Collections.Generic.List<int>>
                {
                    new System.Collections.Generic.List<int> { 1 },
                    new System.Collections.Generic.List<int> { 2 },
                    new System.Collections.Generic.List<int> { 3 },
                    new System.Collections.Generic.List<int> { 4 },
                    new System.Collections.Generic.List<int>(),
                },
            };
        }

        public static ActGraph[] BuildDefault()
        {
            // 每幕的 Kinds 逐位对应 Terms（下标 0..5 = 该幕六个节气按序）：
            //   春 立春·遭遇 / 雨水·孵穴 ｜ 惊蛰·遭遇 / 春分·灵市 ｜ 清明·精英 ｜ 谷雨·铸魂台
            //   夏 立夏·遭遇 / 小满·异闻 ｜ 芒种·遭遇 / 夏至·天象 ｜ 小暑·精英 ｜ 大暑·灵市
            //   秋 立秋·遭遇 / 处暑·灵市 ｜ 白露·遭遇 / 秋分·异闻 ｜ 寒露·精英 ｜ 霜降·孵穴
            //   冬 立冬·遭遇 / 小雪·天象 ｜ 大雪·遭遇 / 冬至·异闻 ｜ 小寒·精英 ｜ 大寒·铸魂台
            // ⚠ 一/二层各恰 1 场战斗、三层固定精英、四层固定非战斗 —— 三条硬约束缺一不可，
            //   MeetsV11Constraints 会逐幕检查（campaign.selftest 的第 ② 项）。
            return new[]
            {
                new ActGraph
                {
                    Act = 1, SeasonCn = "春", SeasonElement = WanXiang.Battle.Core.Element.Wood,
                    BossName = "句芒",
                    Terms = new[] { 1, 2, 3, 4, 5, 6 },
                    Kinds = new[]
                    {
                        NodeKind.Encounter, NodeKind.Nest, NodeKind.Encounter,
                        NodeKind.Shop, NodeKind.Elite, NodeKind.Forge,
                    },
                    Layers = new[] { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4 }, new[] { 5 } },
                },
                new ActGraph
                {
                    Act = 2, SeasonCn = "夏", SeasonElement = WanXiang.Battle.Core.Element.Fire,
                    BossName = "祝融",
                    Terms = new[] { 7, 8, 9, 10, 11, 12 },
                    Kinds = new[]
                    {
                        NodeKind.Encounter, NodeKind.Tale, NodeKind.Encounter,
                        NodeKind.Omen, NodeKind.Elite, NodeKind.Shop,
                    },
                    Layers = new[] { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4 }, new[] { 5 } },
                },
                new ActGraph
                {
                    Act = 3, SeasonCn = "秋", SeasonElement = WanXiang.Battle.Core.Element.Metal,
                    BossName = "蓐收",
                    Terms = new[] { 13, 14, 15, 16, 17, 18 },
                    Kinds = new[]
                    {
                        NodeKind.Encounter, NodeKind.Shop, NodeKind.Encounter,
                        NodeKind.Tale, NodeKind.Elite, NodeKind.Nest,
                    },
                    Layers = new[] { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4 }, new[] { 5 } },
                },
                new ActGraph
                {
                    Act = 4, SeasonCn = "冬", SeasonElement = WanXiang.Battle.Core.Element.Water,
                    BossName = "禺强",
                    Terms = new[] { 19, 20, 21, 22, 23, 24 },
                    Kinds = new[]
                    {
                        NodeKind.Encounter, NodeKind.Omen, NodeKind.Encounter,
                        NodeKind.Tale, NodeKind.Elite, NodeKind.Forge,
                    },
                    Layers = new[] { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4 }, new[] { 5 } },
                },
                new ActGraph
                {
                    Act = 5, SeasonCn = "长夏", SeasonElement = WanXiang.Battle.Core.Element.Earth,
                    BossName = "后土",
                    Terms = new int[] { },                       // declaredNodeCount = 0
                    Layers = new[] { new int[] { } },            // 天阙没有节点，只有一场终局战
                    Kinds = new NodeKind[] { },                  // 后土 + 玩家队伍前 3 只的镜像
                },
            };
        }
    }
}
