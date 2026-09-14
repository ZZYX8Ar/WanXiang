// ============================================================================
//  万相 · 战斗核心 · 战场状态
//  ---------------------------------------------------------------------------
//  BattleState 持有"这一场战斗的全部可变状态"。它**不是**一个 God Object，
//  它只做三件事：
//
//    1) 托管双方的九宫格槽位（谁是哪一格）
//    2) 提供**顺序确定**的遍历（出手序列、双方名单）
//    3) 判定终局
//
//  为什么"顺序确定"要单列一条：GDD 7 章验收标准①要求"同一份输入 100% 可复现"。
//  复现性最常见的杀手不是随机数，而是**遍历顺序不确定** ——
//  用 Dictionary/HashSet 存单位，两次运行的枚举顺序就可能不同；
//  用 List.Sort（内排序不稳定）比速度，同速单位的相对次序也不保证。
//  所以这里：
//    - 槽位用**定长数组**而不是 Dictionary
//    - 出手排序给出**全序**比较器（速度 → 站位索引 → 阵营 → 实例 id）
//
//  ⚠ 关于"双方是否共用一块棋盘"：GDD 2.5 描述的是**每方各自的 3×3**。
//    相生相邻 / 相克相冲 / 中宫土位 / 同属共鸣四条规则都在本方棋盘内结算，
//    这与"上阵 5 人、9 格必有空位、把相冲属性隔开一格"的微操描述一致 ——
//    如果敌我共用一块板，玩家就没法通过"隔开一格"来布局了。
// ============================================================================

using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    public sealed class BattleState
    {
        public readonly BattleConfig Config;
        public readonly DeterministicRandom Random;
        public readonly BattleLog Log = new BattleLog();

        /// <summary>当前回合序号，从 1 开始。</summary>
        public int Turn = 1;

        public BattleOutcome Outcome = BattleOutcome.Ongoing;
        public bool IsOver => Outcome != BattleOutcome.Ongoing;

        private readonly BattleUnit[][] _slots = new BattleUnit[2][];
        private readonly List<BattleUnit>[] _units = new List<BattleUnit>[2];
        private readonly List<BattleUnit> _all = new List<BattleUnit>(BoardLayout.MaxDeployed * 2);
        private bool _setupDone;

        // ---- 视图帧流（见 ViewFrames.cs 的说明） ----
        private readonly List<ViewFrame> _frames = new List<ViewFrame>(2048);
        private readonly List<UnitSnapshot> _frameBuf = new List<UnitSnapshot>(10);
        private UnitSnapshot[] _lastSnapshot = new UnitSnapshot[0];

        /// <summary>
        /// 是否记录视图帧。默认开 —— 灰盒阶段它便宜到不值得关，而"表现层不用自己算规则"
        /// 这个好处太大。真要跑批量数值模拟（几万场）时置 false 省掉这部分开销。
        /// </summary>
        public bool CaptureFrames = true;

        /// <summary>视图帧流。表现层按 EventIndex 与事件流对齐。</summary>
        public IReadOnlyList<ViewFrame> Frames => _frames;

        /// <summary>
        /// 场地天时（GDD 3.1，STEP 3）。**null = 本场无天时** —— 所有结算点先判空，
        /// 保证无天时的战斗与引入天时系统之前逐位一致（可复现性红线）。
        /// </summary>
        public WeatherRuntime Weather;

        /// <summary>禁疗生效中（小雪「虹藏不见」/ 覆盖天时）。</summary>
        public bool HealBanned => Weather != null && Weather.HealBanned;

        public BattleState(BattleConfig config, ulong seed)
        {
            Config = config ?? BattleConfig.Default;
            Random = new DeterministicRandom(seed);
            for (int s = 0; s < 2; s++)
            {
                _slots[s] = new BattleUnit[BoardLayout.CellCount];
                _units[s] = new List<BattleUnit>(BoardLayout.MaxDeployed);
            }
            // 每条事件入队后自动抓一帧：事件流是唯一的时序权威，
            // 把抓帧挂在它上面，就不会出现"打了伤害却没抓帧"这种漏。
            // 用 lambda 而不是方法组：CaptureFrame 有个可选参数，签名不匹配 Action。
            Log.OnEventAdded = () => CaptureFrame();
        }

        public static BattleState Create(BattleConfig config, ulong seed)
            => new BattleState(config, seed);

        public static TeamSide Opponent(TeamSide side)
            => side == TeamSide.Player ? TeamSide.Enemy : TeamSide.Player;

        // ================================================================
        //  布置
        // ================================================================

        /// <summary>
        /// 把单位放上棋盘。返回 false 表示格子非法或已被占用（调用方应记一条错误，
        /// 不要静默吞掉 —— 站位配错是内容问题，不是运行时异常）。
        /// </summary>
        public bool Place(BattleUnit unit, GridPos pos)
        {
            if (unit == null) return false;
            if (!pos.IsValid) return false;

            int s = (int)unit.Side;
            if (_slots[s][pos.Index] != null) return false;

            unit.Pos = pos;
            unit.CenterDamageReduction = Config.CenterDamageReduction;
            unit.RageCap = Config.RageMax;
            _slots[s][pos.Index] = unit;
            _units[s].Add(unit);
            return true;
        }

        /// <summary>
        /// 布置完成后必须调一次：把双方名单按站位索引升序排好，并重建"全体"列表。
        /// 排序是手写的插入排序而不是 List.Sort —— 存量的比较器已经够用，
        /// 但插入排序**顺序完全由输入决定**，不给内排序算法留自由度。
        /// </summary>
        public void FinishSetup()
        {
            for (int s = 0; s < 2; s++)
            {
                var list = _units[s];
                for (int i = 1; i < list.Count; i++)
                {
                    var cur = list[i];
                    int j = i - 1;
                    while (j >= 0 && list[j].Pos.Index > cur.Pos.Index)
                    {
                        list[j + 1] = list[j];
                        j--;
                    }
                    list[j + 1] = cur;
                }
            }

            // 「全体」= 我方（按站位）→ 敌方（按站位）。顺序固定，是全部遍历的基准。
            _all.Clear();
            for (int s = 0; s < 2; s++) _all.AddRange(_units[s]);

            _setupDone = true;

            _lastSnapshot = new UnitSnapshot[_all.Count];
            CaptureFrame(true);   // 第 0 帧 = 战斗开始前的**全量**初始画面
        }

        // ================================================================
        //  视图帧流
        // ================================================================

        /// <summary>
        /// 抓一帧。默认**只记变化过的单位**（例：Crit 事件不改状态，它就不该占一帧）。
        /// <paramref name="forceAll"/> 用于抓初始帧 —— 累积式回放必须有一个全量起点，
        /// 否则表现层没法知道"第一帧之前各人是什么样"。
        /// 多次调用是安全的：它对状态没有任何副作用，只是看一眼、比一比。
        /// </summary>
        public void CaptureFrame(bool forceAll = false)
        {
            if (!CaptureFrames) return;
            if (_lastSnapshot.Length != _all.Count)
                _lastSnapshot = new UnitSnapshot[_all.Count];

            _frameBuf.Clear();
            for (int i = 0; i < _all.Count; i++)
            {
                var snap = ViewSnapshot.Of(_all[i]);
                if (!forceAll && _lastSnapshot[i].Equals(snap)) continue;
                _lastSnapshot[i] = snap;
                _frameBuf.Add(snap);
            }
            if (_frameBuf.Count == 0) return;

            _frames.Add(new ViewFrame
            {
                Turn = Turn,
                EventIndex = Log.Count - 1,      // -1 = 还没有任何事件
                Changed = _frameBuf.ToArray(),
            });
        }

        /// <summary>丢掉帧流并重抓初始帧（在同一个 State 上重跑战斗时用）。</summary>
        public void ResetFrames()
        {
            _frames.Clear();
            for (int i = 0; i < _lastSnapshot.Length; i++) _lastSnapshot[i] = default;
            CaptureFrame(true);
        }

        // ================================================================
        //  读取
        // ================================================================

        /// <summary>取某格的单位，空格返回 null。</summary>
        public BattleUnit SlotAt(TeamSide side, int index)
        {
            if (index < 0 || index >= BoardLayout.CellCount) return null;
            return _slots[(int)side][index];
        }

        /// <summary>取某方 9 格的原样数组（BoardRules 按固定顺序遍历 12 对相邻格时用它）。
        /// ⚠ 返回的是**内部数组本身**，请不要写它。</summary>
        public BattleUnit[] SlotsOf(TeamSide side) => _slots[(int)side];

        /// <summary>某方全部上阵单位，按站位索引升序。</summary>
        public IReadOnlyList<BattleUnit> UnitsOf(TeamSide side) => _units[(int)side];

        /// <summary>全体单位：我方先、敌方后，各自按站位升序。</summary>
        public IReadOnlyList<BattleUnit> AllUnits => _all;

        public bool IsSetupDone => _setupDone;

        public int AliveCountOf(TeamSide side)
        {
            int n = 0;
            var list = _units[(int)side];
            for (int i = 0; i < list.Count; i++) if (list[i].IsAlive) n++;
            return n;
        }

        /// <summary>把某方的存活单位填进调用方给的缓冲。<see cref="AllUnits"/> 的顺序。</summary>
        public void CollectAlive(TeamSide side, List<BattleUnit> into)
        {
            into.Clear();
            var list = _units[(int)side];
            for (int i = 0; i < list.Count; i++)
                if (list[i].IsAlive) into.Add(list[i]);
        }

        public void CollectAliveAll(List<BattleUnit> into)
        {
            into.Clear();
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].IsAlive) into.Add(_all[i]);
        }

        /// <summary>
        /// 数某方某个五行的单位数。同属共鸣用。
        /// <paramref name="aliveOnly"/> 由 <see cref="BattleConfig.ResonanceCountsAliveOnly"/> 传入。
        /// </summary>
        public int CountOfElement(TeamSide side, Element element, bool aliveOnly)
        {
            int n = 0;
            var list = _units[(int)side];
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Element != element) continue;
                if (aliveOnly && !list[i].IsAlive) continue;
                n++;
            }
            return n;
        }

        // ================================================================
        //  出手序列
        // ================================================================

        /// <summary>
        /// 生成本回合的出手序列：速度高者先手，同速**按站位从左到右**（GDD 6.2 明文要求），
        /// 再同则我方先、最后按实例 id 兜底。
        ///
        /// 为什么要一路兜到实例 id：只要比较器不是**全序**，内排序就可能对同一份输入
        /// 给出两种次序，而"同速"在归一化的面板下并不罕见。多写一层不亏。
        /// </summary>
        public List<BattleUnit> BuildActionOrder()
        {
            var order = new List<BattleUnit>(_all.Count);
            BuildActionOrderInto(order);
            return order;
        }

        /// <summary>同 <see cref="BuildActionOrder"/>，但填进调用方给的缓冲（战斗循环每回合复用同一个 List）。</summary>
        public void BuildActionOrderInto(List<BattleUnit> order)
        {
            order.Clear();
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].IsAlive) order.Add(_all[i]);
            order.Sort(CompareActionOrder);
        }

        private static int CompareActionOrder(BattleUnit a, BattleUnit b)
        {
            if (a.Speed > b.Speed) return -1;
            if (a.Speed < b.Speed) return 1;

            int pa = a.Pos.IsValid ? a.Pos.Index : BoardLayout.CellCount;
            int pb = b.Pos.IsValid ? b.Pos.Index : BoardLayout.CellCount;
            if (pa != pb) return pa < pb ? -1 : 1;

            if (a.Side != b.Side) return a.Side < b.Side ? -1 : 1;

            return string.CompareOrdinal(a.RuntimeId, b.RuntimeId);
        }

        // ================================================================
        //  终局判定
        // ================================================================

        /// <summary>
        /// 检查并记录终局。返回 true 表示战斗已结束。
        /// 双方同时全灭记为 Draw —— 这在"真伤同归于尽"时真的会发生，不能只判一边。
        /// </summary>
        public bool CheckOutcome()
        {
            if (IsOver) return true;

            bool anyPlayer = AliveCountOf(TeamSide.Player) > 0;
            bool anyEnemy = AliveCountOf(TeamSide.Enemy) > 0;
            if (anyPlayer && anyEnemy) return false;

            if (!anyPlayer && !anyEnemy) Outcome = BattleOutcome.Draw;
            else Outcome = anyPlayer ? BattleOutcome.PlayerWin : BattleOutcome.EnemyWin;

            Log.Add(Turn, BattleEventKind.BattleEnd, note: Cn.Of(Outcome));
            return true;
        }

        // ================================================================
        //  文本视图（灰盒自检与日志用）
        // ================================================================

        /// <summary>把一方棋盘画成三行文本。灰盒阶段这就是"画面"。</summary>
        public string DescribeBoard(TeamSide side)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"  【{Cn.Of(side)}】");
            for (int r = 0; r < BoardLayout.Rows; r++)
            {
                sb.Append("  ").Append(BoardLayout.RowNames[r]).Append(' ');
                for (int c = 0; c < BoardLayout.Columns; c++)
                {
                    int idx = r * BoardLayout.Columns + c;
                    var u = _slots[(int)side][idx];
                    string cell;
                    if (u == null) cell = "————";
                    else
                    {
                        string hp = u.IsAlive ? $"{u.Hp}/{u.MaxHp}" : "已阵亡";
                        cell = $"{Cn.Of(u.Element)}·{u.DisplayName}({hp})";
                        if (idx == BoardLayout.CenterIndex) cell += "〔中宫〕";
                    }
                    sb.Append(cell).Append(c == BoardLayout.Columns - 1 ? "" : " | ");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"回合 {Turn} / 上限 {Config.MaxTurns}　随机种子 {Random.Seed}");
            sb.Append(DescribeBoard(TeamSide.Player));
            sb.Append(DescribeBoard(TeamSide.Enemy));
            return sb.ToString();
        }
    }
}
