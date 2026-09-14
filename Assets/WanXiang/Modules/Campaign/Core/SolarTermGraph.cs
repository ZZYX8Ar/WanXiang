// ============================================================================
//  万相 · 节气节点图（GDD 3.1/3.2，STEP 3）
//  ---------------------------------------------------------------------------
//  「二十四节气是地图结构」：每幕 6 个节气构成一张小型分叉图（类《杀戮尖塔》），
//  玩家只经过 4 个节点即抵达守关 —— 每幕 2 个节点被永久放弃。
//
//  ⚠ 拓扑是**占位设计**：GDD 只定了"6 选 4 + 分叉"和"收尾节点必属土"，
//    没给边表。v1 取最直白的形状：4 层 [2,2,1,1]，相邻层全连通 ——
//    每幕恰好 4 条完整路径，任何一条都以下标 5（土节点：谷雨/大暑/霜降/大寒）收尾。
//    试玩要调手感时改 Layers / 加边表即可，遍历与验收不动。
//
//  幕 5（长夏 · 后土）没有节点 —— 它只是终局守关，这在 solar_terms.json 里
//  就是 declaredNodeCount = 0。
// ============================================================================

using System.Collections.Generic;

namespace WanXiang.Campaign
{
    /// <summary>一幕的节点图。数据全部来自 GDD 3.3 / solar_terms.json。</summary>
    public sealed class ActGraph
    {
        public int Act;                  // 1..5
        public string SeasonCn;          // 春 / 夏 / 秋 / 冬 / 长夏
        public WanXiang.Battle.Core.Element SeasonElement;
        public string BossName;
        public int[] Terms;              // 节气序号（GDD 3.3 的 01..24）；空幕为空数组

        /// <summary>分层拓扑：每层含哪些节点（下标指 <see cref="Terms"/>）。
        /// 相邻层之间全连通（v1 占位，见文件头）。</summary>
        public int[][] Layers;

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
        public static ActGraph[] BuildDefault()
        {
            return new[]
            {
                new ActGraph
                {
                    Act = 1, SeasonCn = "春", SeasonElement = WanXiang.Battle.Core.Element.Wood,
                    BossName = "句芒",
                    Terms = new[] { 1, 2, 3, 4, 5, 6 },
                    Layers = new[] { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4 }, new[] { 5 } },
                },
                new ActGraph
                {
                    Act = 2, SeasonCn = "夏", SeasonElement = WanXiang.Battle.Core.Element.Fire,
                    BossName = "祝融",
                    Terms = new[] { 7, 8, 9, 10, 11, 12 },
                    Layers = new[] { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4 }, new[] { 5 } },
                },
                new ActGraph
                {
                    Act = 3, SeasonCn = "秋", SeasonElement = WanXiang.Battle.Core.Element.Metal,
                    BossName = "蓐收",
                    Terms = new[] { 13, 14, 15, 16, 17, 18 },
                    Layers = new[] { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4 }, new[] { 5 } },
                },
                new ActGraph
                {
                    Act = 4, SeasonCn = "冬", SeasonElement = WanXiang.Battle.Core.Element.Water,
                    BossName = "禺强",
                    Terms = new[] { 19, 20, 21, 22, 23, 24 },
                    Layers = new[] { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4 }, new[] { 5 } },
                },
                new ActGraph
                {
                    Act = 5, SeasonCn = "长夏", SeasonElement = WanXiang.Battle.Core.Element.Earth,
                    BossName = "后土",
                    Terms = new int[] { },                       // declaredNodeCount = 0
                    Layers = new[] { new int[] { } },            // 只有守关，没有节点
                },
            };
        }
    }
}
