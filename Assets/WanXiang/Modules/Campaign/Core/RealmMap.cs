// ============================================================================
//  秘境 · 内部节点图（RealmMap）—— 2026-10-16
//  ---------------------------------------------------------------------------
//  ⭐ 口径（用户 2026-10-16 明确要求）：**秘境内部直接套用现有的节点地图设计**，
//  即与主线一样的「层 + 边 + 节点类型」结构，而不是另造一套网格。
//
//  所以这一版做的是：**把场域翻译成一个 ActGraph**，然后复用 <c>ActGraph.HasEdge</c> /
//  <c>LayerOf</c> / <c>KindOf</c> 这些现成方法，面板也按层排布 ——
//  与 CampaignPanel 的路线图同一套视觉语言与交互规则。
//
//  ★ 为什么这次不用「cols × rows 网格」了（第一版的做法，已删）：
//   网格要另写一套邻接判定、另画一套连线，视觉上也和主线不像 ——
//   玩家会以为进入了另一个游戏。第二版直接生成 ActGraph：代码更少、观感统一。
//
//  ⚠ 确定性：同 (realmId, seed) 必得同一张图（可复盘）。
//  ⚠ 无 UnityEngine 依赖（放 Campaign.Core），UI 只读不改。
// ============================================================================

using System.Collections.Generic;

namespace WanXiang.Campaign
{
    /// <summary>
    /// 一张秘境内部图。
    /// ⚠ ActGraph 里**没有** Boss 节点类型（主线也是用 Elite 占位），
    ///   所以"首领裂隙"用 <see cref="BossOffset"/> 这个 offset 标记 —— 与
    ///   CampaignPanel 的守关格处理方式一致。
    /// </summary>
    public sealed class RealmMap
    {
        public RealmDef Realm;

        /// <summary>★ 内部图就是一张 ActGraph（层 + 边 + 节点类型），与主线同构。</summary>
        public ActGraph Graph;

        /// <summary>首领裂隙所在 offset（-1 = 本图没有）。</summary>
        public int BossOffset = -1;

        public int LayerCount => Graph != null ? Graph.Layers.Length : 0;
        public int NodeCount => Graph != null ? Graph.NodeCount : 0;
        public int Waves => Realm != null ? Realm.Waves : 0;

        public NodeKind KindOf(int offset)
        {
            return Graph != null ? Graph.KindOf(offset) : NodeKind.Encounter;
        }

        public int LayerOf(int offset)
        {
            return Graph != null ? Graph.LayerOf(offset) : 0;
        }

        /// <summary>可走：与主线的 <see cref="ActGraph.HasEdge"/> 完全同一条规则。</summary>
        public bool CanMove(int from, int to) => Graph != null && Graph.HasEdge(from, to);

        public bool IsBossOffset(int offset) => offset >= 0 && offset == BossOffset;

        /// <summary>战斗节点（遭遇 / 精英 / 首领）—— 只有这三类会进战斗。</summary>
        public bool IsBattleOffset(int offset)
        {
            return NodeKinds.IsBattle(KindOf(offset)) || IsBossOffset(offset);
        }

        /// <summary>这一格是出口层（最后一层，打完就该回主线了）。</summary>
        public bool IsExitOffset(int offset)
        {
            if (Graph == null || offset < 0 || offset >= Graph.NodeCount) return false;
            return LayerOf(offset) == Graph.Layers.Length - 1;
        }
    }

    public static class RealmMapBuilder
    {
        /// <summary>
        /// 生成秘境内部图。层数 = <c>波数 + 2</c>（首层入口遭遇 → 中间各波 → 顶层出口窖藏）。
        /// ⚠ 首领裂隙占**出口上一层**的一格 ⇒ 它同时是"最后一场战斗"与"回主线的许可"。
        /// </summary>
        public static RealmMap Build(RealmDef realm, ulong seed)
        {
            if (realm == null) return null;
            int layers = (realm.Waves < 1 ? 1 : realm.Waves) + 2;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                var map = TryBuild(realm, layers, seed + (ulong)attempt * 7919UL);
                if (map != null && Validate(map)) return map;
            }
            return TryBuild(realm, layers, seed);      // 兜底：也要能玩
        }

        private static RealmMap TryBuild(RealmDef realm, int layers, ulong seed)
        {
            var rng = new WanXiang.Battle.Core.DeterministicRandom(
                seed ^ WanXiang.Battle.Core.CoreMath.Fnv1a("realmmap:" + realm.Id));

            var layerIndex = new List<int[]>(layers);
            var kinds = new List<NodeKind>(layers * 3);
            var terms = new List<int>(layers * 3);

            for (int layer = 0; layer < layers; layer++)
            {
                bool isFirst = layer == 0;
                bool isExit = layer == layers - 1;
                // 首层/顶层各 1 格（唯一入口 / 唯一出口）；中间每层 2~3 格（有得选）
                int width = isFirst || isExit ? 1 : rng.NextInt(2, 4);
                var row = new int[width];
                for (int k = 0; k < width; k++)
                {
                    row[k] = kinds.Count;
                    terms.Add(layer + 1);      // 秘境没有节气；编号只为让 ActGraph 结构完整
                    kinds.Add(isFirst ? NodeKind.Encounter
                             : isExit ? NodeKind.Shop
                             : PickMiddle(rng, layer, layers));
                }
                layerIndex.Add(row);
            }

            // ---- 连边：**与主线同一套真拓扑**（本层每点连下一层列序最近的 1 个，
            //      50% 再连一个相邻的；最后保证下层每点至少一条入边）----
            var edges = new List<List<int>>(kinds.Count);
            for (int i = 0; i < kinds.Count; i++) edges.Add(new List<int>());

            for (int layer = 0; layer + 1 < layers; layer++)
            {
                var cur = layerIndex[layer];
                var nxt = layerIndex[layer + 1];
                foreach (var a in cur)
                {
                    float ai = (float)System.Array.IndexOf(cur, a) / System.Math.Max(1, cur.Length - 1);
                    int best = nxt[0];
                    float bd = 9f;
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
                foreach (var b in nxt)          // 下层每点至少一条入边（否则永远到不了）
                {
                    bool has = false;
                    foreach (var a in cur) if (edges[a].Contains(b)) { has = true; break; }
                    if (has) continue;
                    float bf = (float)System.Array.IndexOf(nxt, b) / System.Math.Max(1, nxt.Length - 1);
                    int bestA = cur[0];
                    float bd2 = 9f;
                    foreach (var a in cur)
                    {
                        float ai = (float)System.Array.IndexOf(cur, a) / System.Math.Max(1, cur.Length - 1);
                        float d = System.Math.Abs(ai - bf);
                        if (d < bd2) { bd2 = d; bestA = a; }
                    }
                    edges[bestA].Add(b);
                }
            }

            var graph = new ActGraph
            {
                Act = 0,                       // 秘境不属于任何一幕
                SeasonCn = realm.Name,
                SeasonElement = ParseElement(realm.Element),
                BossName = null,
                Terms = terms.ToArray(),
                Kinds = kinds.ToArray(),
                Layers = layerIndex.ToArray(),
                Edges = edges,
            };

            var map = new RealmMap { Realm = realm, Graph = graph };
            int exitLayer = layers - 1;
            if (exitLayer - 1 >= 0)
            {
                var prev = layerIndex[exitLayer - 1];
                map.BossOffset = prev[rng.NextInt(0, prev.Length)];
            }
            return map;
        }

        /// <summary>
        /// 中间层抽类型。⚠ 出口上一层固定 Elite（它那一格会被标成首领裂隙）——
        ///   其余层战斗约 40%、锻炉/灵泉/窖藏各约 20%，
        ///   别全战斗（会变成连续三场硬仗）也别全商店（没有紧张感）。
        /// </summary>
        private static NodeKind PickMiddle(WanXiang.Battle.Core.DeterministicRandom rng,
            int layer, int layers)
        {
            if (layer == layers - 2) return NodeKind.Elite;

            int roll = rng.NextInt(0, 100);
            if (roll < 40) return NodeKind.Encounter;
            if (roll < 61) return NodeKind.Forge;     // 锻炉
            if (roll < 82) return NodeKind.Nest;      // 灵泉
            return NodeKind.Shop;                      // 窖藏
        }

        private static WanXiang.Battle.Core.Element ParseElement(string s)
        {
            switch (s)
            {
                case "木": return WanXiang.Battle.Core.Element.Wood;
                case "火": return WanXiang.Battle.Core.Element.Fire;
                case "土": return WanXiang.Battle.Core.Element.Earth;
                case "金": return WanXiang.Battle.Core.Element.Metal;
                case "水": return WanXiang.Battle.Core.Element.Water;
                default: return WanXiang.Battle.Core.Element.None;
            }
        }

        // ================================================================
        //  保底校验
        // ================================================================

        /// <summary>连通性：从入口能不能走遍全图（用 ActGraph 自己的边跑 BFS）。</summary>
        public static bool IsConnected(RealmMap map)
        {
            if (map == null || map.Graph == null || map.NodeCount == 0) return false;
            var seen = new bool[map.NodeCount];
            var stack = new Stack<int>();
            int entry = map.Graph.Layers[0][0];
            stack.Push(entry);
            seen[entry] = true;
            int reached = 1;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                var es = map.Graph.Edges[i];
                for (int k = 0; k < es.Count; k++)
                {
                    int j = es[k];
                    if (seen[j]) continue;
                    seen[j] = true;
                    reached++;
                    stack.Push(j);
                }
            }
            return reached == map.NodeCount;
        }

        /// <summary>战斗节点数（遭遇 / 精英 / 首领都算）。</summary>
        public static int BattleCount(RealmMap map)
        {
            if (map == null || map.Graph == null) return 0;
            int n = 0;
            for (int i = 0; i < map.NodeCount; i++)
                if (map.IsBattleOffset(i)) n++;
            return n;
        }

        /// <summary>首领裂隙恰好 1 个。</summary>
        public static int BossCount(RealmMap map)
        {
            if (map == null || map.Graph == null) return 0;
            int n = 0;
            for (int i = 0; i < map.NodeCount; i++) if (map.IsBossOffset(i)) n++;
            return n;
        }

        public static bool Validate(RealmMap map)
        {
            if (map == null || map.Graph == null) return false;
            if (!IsConnected(map)) return false;
            if (BossCount(map) != 1) return false;
            // 战斗数至少 = 波数：入口 1 场 + 首领 1 场 + 中间的遭遇
            return BattleCount(map) >= map.Waves;
        }
    }
}