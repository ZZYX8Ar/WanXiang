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
            // ★★ 层数（2026-10-16 用户："你这个秘境节点太少了，起码得有12层往上吧"）。
            //   旧口径 = Waves + 2 ⇒ 只有 5~6 层，一眼就看完了，没有"深入"的感觉。
            //   新口径与 Waves **解耦**：Waves 仍然只表示"几场硬仗"，层数单独给足。
            //   ⛔ 别再拿 Waves 当层数用 —— 它们是两个轴（战斗密度 vs 地图长度），
            //     绑在一起就会出现"想加长度结果战斗也翻倍"。
            int layers = RealmLayersFor(realm);
            for (int attempt = 0; attempt < 10; attempt++)
            {
                var map = TryBuild(realm, layers, seed + (ulong)attempt * 7919UL);
                if (map != null && Validate(map)) return map;
            }
            return TryBuild(realm, layers, seed);      // 兜底：也要能玩
        }

        /// <summary>
        /// 秘境层数：**基础 14 层**，按场域难度上下浮动 1 层（12~15）。
        /// <para>为什么是 14：主线一幕 12 层，秘境要比"一幕"更长才撑得起
        ///   "高风险支线"的定位；但也不宜超过 15（一屏放不下、玩家会失去方向感）。</para>
        /// </summary>
        public static int RealmLayersFor(RealmDef realm)
        {
            int w = realm != null ? realm.Waves : 3;
            // Waves 3→14 / 4→13 / 5→12：**越难的场域越短**（单场更硬，总长收一点）
            int n = 14 - (w - 3);
            if (n < 12) n = 12;
            if (n > 15) n = 15;
            return n;
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

            // ★ 秘境专属节点：生成完再**钉**上去（秘藏 ×1 / 镇守 ×2），
            //   不靠概率抽 —— "每张图恰好有"是设计承诺，概率保证不了。
            //   ⚠ 必须在连边**之前**：连边只看层与列序，不看类型，放前后都行；
            //     但放前面可以让 Validate 之后的兜底重建也带着专属节点。
            PinRealmOnlyNodes(layerIndex.ToArray(), kinds, rng);

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
        /// 中间层抽类型。
        /// ⚠ 出口上一层固定 Elite（它那一格会被标成首领裂隙）。
        ///
        /// ★★ 2026-10-16 加"秘境专属节点"（用户："设置一些特殊的奖励节点或者一些特殊的敌人，
        ///   这些在外面是没有的"）：
        ///   · <see cref="NodeKind.RealmVault"/> 秘藏 —— 一次性重奖，**每张图恰好 1 个**
        ///     （放在中段随机一层，让玩家有"再走几层就能拿到"的牵引）
        ///   · <see cref="NodeKind.RealmChampion"/> 镇守 —— 特殊敌人，**每张图 2 个**
        ///     （刻意分散在上半段，且**不在必经的单格层**上，给玩家"绕开还是正面打"的选择）
        ///   ⛔ 这两个类型只从这里产出，主线的权重表里没有它们。
        /// </summary>
        private static NodeKind PickMiddle(WanXiang.Battle.Core.DeterministicRandom rng,
            int layer, int layers)
        {
            if (layer == layers - 2) return NodeKind.Elite;

            int roll = rng.NextInt(0, 100);
            if (roll < 34) return NodeKind.Encounter;
            if (roll < 52) return NodeKind.Forge;     // 锻炉
            if (roll < 70) return NodeKind.Nest;      // 灵泉
            return NodeKind.Shop;                      // 窖藏
        }

        /// <summary>
        /// 把秘境专属节点**钉**到图上（生成完再覆盖，而不是丢进随机池）——
        /// 因为"每张图恰好 1 个秘藏 / 2 个镇守"是设计承诺，靠概率抽是保证不了的。
        /// ⛔ 只覆盖**中间层**：首层（唯一入口）和出口层（唯一出口）不能动，
        ///   否则会出现"入口就是秘藏"或"出口打镇守"这种不讲理的图。
        /// </summary>
        private static void PinRealmOnlyNodes(int[][] layerIndex, List<NodeKind> kinds,
            WanXiang.Battle.Core.DeterministicRandom rng)
        {
            int layers = layerIndex.Length;
            if (layers < 5) return;

            // ---- 秘藏 ×1：放在 40%~60% 深度、列数 >=2 的层里 ----
            int vaultLayer = -1;
            int lo = (int)System.Math.Floor(layers * 0.40);
            int hi = (int)System.Math.Ceiling(layers * 0.60);
            for (int i = lo; i <= hi && vaultLayer < 0; i++)
                if (i >= 1 && i <= layers - 2 && layerIndex[i].Length >= 2) vaultLayer = i;
            if (vaultLayer < 0)   // 没找到就放宽：任意中间层
                for (int i = 1; i <= layers - 2 && vaultLayer < 0; i++)
                    if (layerIndex[i].Length >= 2) vaultLayer = i;
            if (vaultLayer > 0)
            {
                var row = layerIndex[vaultLayer];
                kinds[row[rng.NextInt(0, row.Length)]] = NodeKind.RealmVault;
            }

            // ---- 镇守 ×2：放在 25% / 75% 深度附近、列数 >=2 的层（错开秘藏那一层）----
            int placed = 0;
            int[] prefer = { (int)System.Math.Round(layers * 0.75), (int)System.Math.Round(layers * 0.25) };
            foreach (int want in prefer)
            {
                if (placed >= 2) break;
                int pick = -1;
                for (int d = 0; d <= 2 && pick < 0; d++)
                {
                    foreach (int cand in new[] { want - d, want + d })
                    {
                        if (cand < 1 || cand > layers - 2) continue;
                        if (cand == vaultLayer) continue;
                        if (layerIndex[cand].Length < 2) continue;   // 单格层 = 必经，不给"绕开"的选择
                        pick = cand; break;
                    }
                }
                if (pick < 0) continue;
                var row = layerIndex[pick];
                kinds[row[rng.NextInt(0, row.Length)]] = NodeKind.RealmChampion;
                placed++;
            }
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