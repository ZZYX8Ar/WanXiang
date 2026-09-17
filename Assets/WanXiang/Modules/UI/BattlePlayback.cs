// ============================================================================
//  万相 · 战斗回放（运行期）
//  ---------------------------------------------------------------------------
//  为什么需要它：战斗核心（BattleSimulator）只有「一次跑完」的 Run，
//  没有逐事件推进的接口；而 UI 要"一格一格打给你看"。
//  所以这里的做法是：一次跑完 → 拿到完整事件流与帧流 → 用双游标回放。
//
//  双游标（与 BattleStageWindow / Battle2DBuilderWindow 同一契约，不要改）：
//    _eventIndex  事件游标：每 0.28 秒 +1
//    _frameIndex  帧游标：把所有 EventIndex <= _eventIndex 的帧叠加进来
//  帧是**差量**（只装变化过的单位），第 0 帧才是全量初始帧。
// ============================================================================

using System.Collections.Generic;
using WanXiang.Battle.Core;

namespace WanXiang.Modules.UI
{
    /// <summary>开一场战斗需要的入参。UI 层构造，战斗层消费。</summary>
    public sealed class BattleRequest
    {
        public string Title = "遭遇战";
        public string WeatherName = "";
        public ulong Seed = 20260914UL;
        public List<BeastDef> Player = new List<BeastDef>();
        public List<BeastDef> Enemy = new List<BeastDef>();

        /// <summary>
        /// 敌方属性倍率（与 Enemy 一一对应；缺省按 1 处理）。
        /// 旧的简版通道 —— 只用于"没有战役内容供给"的兜底组队。
        /// </summary>
        public List<float> EnemyMul = new List<float>();

        /// <summary>
        /// 敌方上阵条目（**正式通道**，优先于 Enemy/EnemyMul）。
        /// 由 Campaign.SeededEnemyProvider.EnemiesFor 供给：自带阵位、属性倍率（规模表）
        /// 和精英的「劫象」特性 —— BeastDef + 倍率那种简版表达不了劫象。
        /// </summary>
        public List<DeployEntry> EnemyEntries = new List<DeployEntry>();

        /// <summary>上阵格位顺序：前排 → 中宫 → 后排两侧（与 GDD 的推荐站位一致）。</summary>
        public static readonly int[] Cells = { 0, 1, 4, 7, 8 };
    }

    public sealed class BattlePlayback
    {
        public BattleState State { get; private set; }
        public BattleResult Result { get; private set; }

        private int _eventIndex = -1;
        private int _frameIndex = 0;

        public int EventCount => State != null ? State.Log.Count : 0;
        public bool Finished => State != null && _eventIndex + 1 >= State.Log.Count;
        public BattleEvent Current { get; private set; }

        public BattlePlayback(BattleRequest req)
        {
            var cfg = BattleConfig.Default;
            cfg.AutoCastUltimate = true;      // 结构验证版：绝技自动放，先不接手动干预
            cfg.MaxTurns = 24;

            var p = new DeployEntry[req.Player.Count];
            for (int i = 0; i < p.Length; i++)
                p[i] = DeployEntry.Player(req.Player[i], BattleRequest.Cells[i % BattleRequest.Cells.Length]);

            DeployEntry[] e;
            if (req.EnemyEntries != null && req.EnemyEntries.Count > 0)
            {
                // 正式通道：阵位/倍率/劫象都在条目里，原样送入战斗核心
                e = req.EnemyEntries.ToArray();
            }
            else
            {
                e = new DeployEntry[req.Enemy.Count];
                for (int i = 0; i < e.Length; i++)
                {
                    float mul = (req.EnemyMul != null && i < req.EnemyMul.Count) ? req.EnemyMul[i] : 1f;
                    e[i] = DeployEntry.Enemy(req.Enemy[i], BattleRequest.Cells[i % BattleRequest.Cells.Length])
                                      .WithMul(mul);
                }
            }

            State = BattleFactory.Create(cfg, req.Seed, p, e);
            Result = BattleSimulator.Run(State);   // 一次跑完，事件流/帧流即完整

            ApplyInitialFrame();
        }

        /// <summary>套上第 0 帧（全量初始快照）。</summary>
        private void ApplyInitialFrame()
        {
            _frameIndex = 0;
            if (State.Frames.Count > 0) PendingFrames.Add(State.Frames[0]);
        }

        /// <summary>本次 Step 需要叠加的帧（调用方消费后清空）。</summary>
        public readonly List<ViewFrame> PendingFrames = new List<ViewFrame>(4);

        /// <summary>推进一步：返回 false 表示战斗已播完。</summary>
        public bool Step()
        {
            PendingFrames.Clear();
            if (Finished) return false;

            _eventIndex++;
            Current = State.Log.Events[_eventIndex];

            // 严格 <= ：帧的 EventIndex 可能重复、可能跳号，差量必须全部叠加
            while (_frameIndex + 1 < State.Frames.Count
                   && State.Frames[_frameIndex + 1].EventIndex <= _eventIndex)
            {
                _frameIndex++;
                PendingFrames.Add(State.Frames[_frameIndex]);
            }
            return true;
        }

        /// <summary>直接跳到结尾（测试/跳过用）。</summary>
        public void FastForward()
        {
            while (Step()) { }
        }

        public bool PlayerWin => State != null && State.Outcome == BattleOutcome.PlayerWin;

        /// <summary>战报一行摘要。</summary>
        public string Summary()
        {
            return string.Format("{0}｜{1} 回合｜{2} 条事件｜{3}",
                Cn.Of(State.Outcome), State.Turn, EventCount, State.Log.Fingerprint);
        }
    }
}
