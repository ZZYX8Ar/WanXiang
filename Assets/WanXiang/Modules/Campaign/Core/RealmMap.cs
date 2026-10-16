// ============================================================================
//  秘境 · 小地图（RealmMap）—— 2026-10-16 设计文档第 6 章
//  ---------------------------------------------------------------------------
//  主线是"12 层自下而上的路线图"，秘境要换成**小地图 + 格子推进**
//  （参考明日方舟的干员树 / 杀戮尖塔的层内地图）—— 这是"肉鸽感"的主要来源。
//
//  ★ 三条保底校验（设计文档 6.2，生成后立刻跑，不合格就重roll）：
//    ① 连通性：每个格都能走到出口
//    ② 精英不挨着精英：避免连续两场高强度
//    ③ 首领裂隙唯一且固定在出口前一格
//
//  ⚠ 无随机性泄漏：节点类型用**权重抽样 + 确定性种子**，同种子同结果（可复盘）。
//  ⚠ 无 UnityEngine 依赖（放 Campaign.Core），UI 只读不改。
// ============================================================================

using System.Collections.Generic;

namespace WanXiang.Campaign
{
    /// <summary>秘境内部节点类型（与主线的 NodeKind **分开**，别混用）。</summary>
    public enum RealmNodeKind
    {
        Skirmish = 0,   // 遭遇：常规杂兵，快速清场
        EliteRift = 1,  // 精英裂隙：单只精英 + 2 随从
        ForgeHut = 2,   // 锻炉：免费重铸 1 次 / 拆解遗物换星币
        Font = 3,       // 灵泉：回 35% 血 或 1 次免费复活
        Cache = 4,      // 窖藏：直接拿星币 / 灵卵，无战斗
        BossRift = 5,   // 首领裂隙：本幕首领，通关解锁返回
    }

    public sealed class RealmNode
    {
        public int Index;              // 线性下标
        public int Col;
        public int Row;
        public RealmNodeKind Kind;
        /// <summary>到左下角的层数（= Row，视觉上"往上爬"）。</summary>
        public int Layer => Row;

        public bool IsBattle =>
            Kind == RealmNodeKind.Skirmish || Kind == RealmNodeKind.EliteRift
         || Kind == RealmNodeKind.BossRift;
    }

    public sealed class RealmMap
    {
        public RealmDef Realm;
        public RealmNode[] Nodes;
        public int Cols;
        public int Rows;
        /// <summary>线性下标 → 节点的边（邻接：上下左右 + 斜向可选，这里用上下左右）。</summary>
        public List<int>[] Edges;

        public RealmNode At(int index)
        {
            if (Nodes == null || index < 0 || index >= Nodes.Length) return null;
            return Nodes[index];
        }

        public int IndexOf(int col, int row)
        {
            if (Nodes == null) return -1;
            for (int i = 0; i < Nodes.Length; i++)
                if (Nodes[i].Col == col && Nodes[i].Row == row) return i;
            return -1;
        }

        /// <summary>入口（最左下）。</summary>
        public int EntryIndex => 0;
        /// <summary>出口（最右上）。</summary>
        public int ExitIndex => Nodes == null ? -1 : Nodes.Length - 1;
    }

    public static class RealmMapBuilder
    {
        /// <summary>
        /// 非战斗节点的权重（遭遇/精英/首领**不参与**随机，见 Generate）。
        /// </summary>
        private const int W_Forge = 46;
        private const int W_Font = 34;
        private const int W_Cache = 20;
        private const int TotalWeight = W_Forge + W_Font + W_Cache;

        /// <summary>
        /// 生成一张秘境小地图。确定性：同 (realmId, seed) 必得同一张图。
        ///
        /// ★ 关键设计：**战斗节点数量 = 场域波数**（2~4），不是按权重随机撒。
        ///   第一版按 20% 权重撒精英，结果 9×7 的图上出现 16 个精英、还互相相邻 ——
        ///   既违反「精英不挨着」，也让"打完 3 波"的秘境变成打 31 场。
        ///   正确做法：**先按波数放战斗节点，再把其余格子填成非战斗**，
        ///   这样"走过的格子数"与"打的仗数"解耦（肉鸽里这是常态）。
        /// </summary>
        public static RealmMap Build(RealmDef realm, ulong seed)
        {
            if (realm == null) return null;
            int cols = realm.MapCols, rows = realm.MapRows;
            int n = cols * rows;
            if (cols < 2 || rows < 2 || n < 4) return null;

            for (int attempt = 0; attempt < 12; attempt++)
            {
                var map = TryBuild(realm, cols, rows, n, seed + (ulong)attempt * 7919UL);
                if (map == null) continue;
                if (Validate(map)) return map;
            }
            return TryBuild(realm, cols, rows, n, seed);     // 最后的保底：也要能玩
        }

        private static RealmMap TryBuild(RealmDef realm, int cols, int rows, int n, ulong seed)
        {
            var rng = new WanXiang.Battle.Core.DeterministicRandom(
                seed ^ WanXiang.Battle.Core.CoreMath.Fnv1a("realmmap:" + realm.Id));

            var map = new RealmMap { Realm = realm, Cols = cols, Rows = rows };
            map.Nodes = new RealmNode[n];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c;
                    map.Nodes[i] = new RealmNode { Index = i, Col = c, Row = r, Kind = PickCalm(rng) };
                }

            // ---- 边先建好：精英相邻判定要用 ----
            map.Edges = new List<int>[n];
            for (int i = 0; i < n; i++) map.Edges[i] = new List<int>(2);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c;
                    if (c + 1 < cols) { map.Edges[i].Add(i + 1); map.Edges[i + 1].Add(i); }
                    if (r + 1 < rows) { map.Edges[i].Add(i + cols); map.Edges[i + cols].Add(i); }
                }

            // ---- 1) 入口固定是遭遇（玩家要有一场热身）----
            map.Nodes[0].Kind = RealmNodeKind.Skirmish;

            // ---- 2) 首领裂隙固定在出口前一格，且唯一（保底校验 ③）----
            int bossAt = ExitPredecessor(map);
            if (bossAt >= 0) map.Nodes[bossAt].Kind = RealmNodeKind.BossRift;

            // ---- 3) 战斗节点总数 = 波数（**含**精英，首领裂隙不算）----
            //   ⚠ 第一版把精英**额外**加在波数之外 ⇒ 4 波的图变成 5 场仗；
            //     另一版又因为"必须不相邻"而放不满（4 波只放下 3 场）。
            //   正确做法：先按波数放满遭遇，**再把其中 1 个就地升级成精英**
            //   （总数不变），相邻约束失败时**降级重试**而不是放弃放置。
            int wantBattles = realm.Waves < 1 ? 1 : realm.Waves;
            int placed = 1;                                   // 入口已算 1 场
            int guard = 0;
            while (placed < wantBattles && guard++ < n * 8)
            {
                int i = rng.NextInt(0, n);
                // ⚠ 入口(0) / 出口(n-1) / 首领格 都不能放战斗：
                //   出口在下面第 5 步会被改成窖藏，放战斗会被**覆盖掉**，
                //   于是「4 波的图只剩 3 场仗」（自检抓到的）。
                if (i == 0 || i == n - 1 || i == bossAt) continue;
                if (!IsCalm(map.Nodes[i].Kind)) continue;
                // 先试"不相邻"，放不满时退而求其次（宁可挨着也别少一场仗）
                if (HasNeighborKind(map, i, RealmNodeKind.Skirmish)
                    && placed < wantBattles - 1) continue;
                map.Nodes[i].Kind = RealmNodeKind.Skirmish;
                placed++;
            }

            // ---- 4) 精英**在**波数预算内：把 1 场遭遇就地升级成精英 ----
            if (n >= 24)
            {
                guard = 0;
                while (guard++ < n * 8)
                {
                    int i = rng.NextInt(0, n);
                    if (map.Nodes[i].Kind != RealmNodeKind.Skirmish) continue;
                    if (i == 0) continue;                      // 入口那场留给玩家热身
                    if (i == n - 1) continue;                  // 出口固定是窖藏
                    if (HasNeighborKind(map, i, RealmNodeKind.Skirmish)
                        || HasNeighborKind(map, i, RealmNodeKind.EliteRift)) continue;
                    map.Nodes[i].Kind = RealmNodeKind.EliteRift;
                    break;
                }
            }

            // ---- 5) 出口固定是窖藏（打完拿战利品走人）----
            map.Nodes[n - 1].Kind = RealmNodeKind.Cache;
            return map;
        }

        /// <summary>非战斗节点的三选一（遭遇/精英/首领都不在这里面）。</summary>
        private static RealmNodeKind PickCalm(WanXiang.Battle.Core.DeterministicRandom rng)
        {
            int roll = rng.NextInt(0, TotalWeight);
            if ((roll -= W_Forge) < 0) return RealmNodeKind.ForgeHut;
            if ((roll -= W_Font) < 0) return RealmNodeKind.Font;
            return RealmNodeKind.Cache;
        }

        /// <summary>非战斗节点（可被改写成战斗节点的那些）。</summary>
        private static bool IsCalm(RealmNodeKind k)
        {
            return k == RealmNodeKind.Cache || k == RealmNodeKind.Font
                || k == RealmNodeKind.ForgeHut;
        }

        private static bool HasNeighborKind(RealmMap map, int index, RealmNodeKind kind)
        {
            var es = map.Edges[index];
            for (int i = 0; i < es.Count; i++)
                if (map.Nodes[es[i]].Kind == kind) return true;
            return false;
        }

        /// <summary>出口的前一格（同一行左邻；出口在最右上）。</summary>
        private static int ExitPredecessor(RealmMap map)
        {
            if (map.Nodes == null || map.Nodes.Length < 2) return -1;
            int exit = map.ExitIndex;
            var e = map.Nodes[exit];
            if (e.Col > 0) return map.IndexOf(e.Col - 1, e.Row);
            if (e.Row > 0) return map.IndexOf(e.Col, e.Row - 1);
            return -1;
        }

        // ================================================================
        //  三条保底校验
        // ================================================================

        /// <summary>连通性：每个格都能从入口走到（防"孤岛"）。</summary>
        public static bool IsConnected(RealmMap map)
        {
            if (map == null || map.Nodes == null) return false;
            int n = map.Nodes.Length;
            var seen = new bool[n];
            var stack = new Stack<int>();
            stack.Push(0);
            seen[0] = true;
            int reached = 1;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                var es = map.Edges[i];
                for (int k = 0; k < es.Count; k++)
                {
                    int j = es[k];
                    if (seen[j]) continue;
                    seen[j] = true;
                    reached++;
                    stack.Push(j);
                }
            }
            return reached == n;
        }

        /// <summary>精英不挨着精英：避免连续两场高强度。</summary>
        public static bool NoAdjacentElites(RealmMap map)
        {
            if (map == null || map.Nodes == null) return true;
            for (int i = 0; i < map.Nodes.Length; i++)
            {
                if (map.Nodes[i].Kind != RealmNodeKind.EliteRift) continue;
                var es = map.Edges[i];
                for (int k = 0; k < es.Count; k++)
                    if (map.Nodes[es[k]].Kind == RealmNodeKind.EliteRift) return false;
            }
            return true;
        }

        /// <summary>首领裂隙恰好 1 个。</summary>
        public static int BossCount(RealmMap map)
        {
            int n = 0;
            if (map == null || map.Nodes == null) return 0;
            for (int i = 0; i < map.Nodes.Length; i++)
                if (map.Nodes[i].Kind == RealmNodeKind.BossRift) n++;
            return n;
        }

        /// <summary>战斗节点数量 = 波数（含入口那 1 场，不含首领裂隙）。</summary>
        public static int BattleCount(RealmMap map)
        {
            if (map == null || map.Nodes == null) return 0;
            int n = 0;
            for (int i = 0; i < map.Nodes.Length; i++)
            {
                var k = map.Nodes[i].Kind;
                if (k == RealmNodeKind.Skirmish || k == RealmNodeKind.EliteRift) n++;
            }
            return n;
        }

        public static bool Validate(RealmMap map)
        {
            return map != null && IsConnected(map) && NoAdjacentElites(map) && BossCount(map) == 1;
        }
    }
}
