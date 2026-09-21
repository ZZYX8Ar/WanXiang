// ============================================================================
//  万相 · 战斗核心 · 九宫格棋盘
//  ---------------------------------------------------------------------------
//  对应 GDD 2.5。棋盘是本作相对《闪烁之光》最重要的机制增量：
//  站位不只是"谁承受伤害"，而是"谁和谁挨着"。
//
//      索引  行  列        行语义
//       0 1 2   0  ——      前排：御类与嘲讽型，优先承伤
//       3 4 5   1  ——      中排：辅类与术类；4 是「中宫」，全棋盘唯一特殊格
//       6 7 8   2  ——      后排：攻类与疾类
//
//  上阵上限 5 人 ⇒ 9 格里必有空位。GDD 明确说这不是浪费：
//  "把两个相冲的属性隔开一格，就是一次成功的微操。"
// ============================================================================

namespace WanXiang.Battle.Core
{
    /// <summary>棋盘上的一格。用索引而不是 (row, col) 两个 int —— 传参时更难写反。</summary>
    public struct GridPos : System.IEquatable<GridPos>
    {
        public const int Invalid = -1;

        public int Index;

        public GridPos(int index) { Index = index; }

        public bool IsValid => Index >= 0 && Index < BoardLayout.CellCount;
        public int Row => Index / BoardLayout.Columns;
        public int Col => Index % BoardLayout.Columns;

        /// <summary>
        /// 前后排（0 = 前排）—— **按阵营镜像**：双方都是"离中线最近的那一列"为前排。
        /// <para>我方在左（col 2 靠中线）⇒ 前排 = 2 - Col，即格号 <b>2 5 8</b>；</para>
        /// <para>敌方在右（col 0 靠中线）⇒ 前排 = Col，即格号 <b>0 3 6</b>。</para>
        /// （用户定义："我在左边所以 258 是前排；敌人在右边所以敌人的 036 是前排。"）
        /// ⚠ 此前用 Row（上下方向）判前后排是错的：横版对阵的中线是竖直线，离中线远近由列决定。
        /// </summary>
        public int FrontRankFor(TeamSide side)
            => side == TeamSide.Player ? (BoardLayout.Columns - 1 - Col) : Col;

        /// <summary>中宫：第 2 行第 2 列，索引 4。全棋盘唯一的特殊格。</summary>
        public bool IsCenter => Index == BoardLayout.CenterIndex;

        /// <summary>正交相邻（上下左右，**不含对角** —— GDD 2.5 明确）。</summary>
        public bool IsAdjacentTo(GridPos other)
        {
            if (!IsValid || !other.IsValid || Index == other.Index) return false;
            int dr = Row - other.Row;
            int dc = Col - other.Col;
            if (dr < 0) dr = -dr;
            if (dc < 0) dc = -dc;
            return dr + dc == 1;
        }

        /// <summary>八邻（含对角）。目前没有规则用它，但灰盒画线时会用，先留着。</summary>
        public bool IsNeighbour8To(GridPos other)
        {
            if (!IsValid || !other.IsValid || Index == other.Index) return false;
            int dr = Row - other.Row, dc = Col - other.Col;
            if (dr < 0) dr = -dr;
            if (dc < 0) dc = -dc;
            return dr <= 1 && dc <= 1;
        }

        public bool Equals(GridPos other) => Index == other.Index;
        public override bool Equals(object obj) => obj is GridPos g && g.Index == Index;
        public override int GetHashCode() => Index;
        public override string ToString() => $"({Row},{Col})#{Index}";

        public static bool operator ==(GridPos a, GridPos b) => a.Index == b.Index;
        public static bool operator !=(GridPos a, GridPos b) => a.Index != b.Index;
    }

    public static class BoardLayout
    {
        public const int Rows = 3;
        public const int Columns = 3;
        public const int CellCount = Rows * Columns;   // 9
        public const int MaxDeployed = 5;              // GDD 1.2：最多 5 人上阵
        public const int CenterIndex = 4;              // 第 2 行第 2 列

        /// <summary>行名。用于日志与 UI，索引 = GridPos.Row。</summary>
        public static readonly string[] RowNames = { "前排", "中排", "后排" };

        /// <summary>
        /// 速度相同时的出手次序：**从左到右**（索引小的先手）。
        /// GDD 6.2 明确要求"同速按站位从左到右，保证可复现"——
        /// 用别的东西（比如哈希顺序）打破平局，两次跑就可能不一样。
        /// </summary>
        public static int TieBreakOrder(GridPos p) => p.Index;

        /// <summary>中宫及与其正交相邻的四格。灰盒画中宫光圈时用。</summary>
        public static int[] CenterAndNeighbors()
        {
            var center = new GridPos(CenterIndex);
            var list = new System.Collections.Generic.List<int> { CenterIndex };
            for (int i = 0; i < CellCount; i++)
            {
                var p = new GridPos(i);
                if (center.IsAdjacentTo(p)) list.Add(i);
            }
            return list.ToArray();
        }

        /// <summary>
        /// 全部正交相邻的无序格对（共 12 对：横向 6 对 + 纵向 6 对）。
        /// 相邻结算按这个固定顺序遍历 —— 顺序固定是"可复现"的一部分，
        /// 用 HashSet/Dictionary 迭代会引入不确定顺序。
        /// </summary>
        public static readonly int[][] AdjacentPairs = BuildAdjacentPairs();

        private static int[][] BuildAdjacentPairs()
        {
            var list = new System.Collections.Generic.List<int[]>();
            for (int i = 0; i < CellCount; i++)
            {
                for (int j = i + 1; j < CellCount; j++)
                {
                    if (new GridPos(i).IsAdjacentTo(new GridPos(j)))
                        list.Add(new[] { i, j });
                }
            }
            return list.ToArray();
        }
    }
}
