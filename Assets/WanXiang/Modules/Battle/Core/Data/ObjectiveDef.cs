// ============================================================================
//  万相 · 战斗目标定义（2026-11-19 玩法骨架）
//  纯数据，零 Unity 依赖。目标 = 附加奖励，未完成不判负。
//  扩展方式：objectives.csv 加一行即可，判定逻辑在 ObjectiveRules 注册表。
// ============================================================================
namespace WanXiang.Battle.Core
{
    /// <summary>目标判定类型。新增一种 = 加枚举 + 在 ObjectiveRules 注册一个判定委托。</summary>
    public enum ObjectiveKind
    {
        None = 0,
        KillByTurn = 1,       // N 回合内击杀首领
        NoDeath = 2,          // 全员存活
        ElementReactions = 3, // 触发 N 次元素反应
        CaptureOne = 4,       // 捕获至少 1 只
        NoUltimate = 5,       // 不用终结技
        WinLowHp = 6,         // 低血量取胜
    }

    /// <summary>奖励类型。</summary>
    public enum ObjectiveReward
    {
        None = 0, Eggs = 1, Wudao = 2, Ink = 3, RelicShard = 4,
    }

    /// <summary>一条目标定义（来自 objectives.csv）。</summary>
    public sealed class ObjectiveDef
    {
        public string Id;
        public string Name;
        public string Desc;
        public ObjectiveKind Kind;
        public int Param;
        public ObjectiveReward RewardType;
        public int RewardValue;
        public string Icon;
        public int Chance;   // 分配到战斗节点的权重
    }

    /// <summary>一场战斗里目标的运行时进度（BattleState 持有一个）。</summary>
    public sealed class ObjectiveProgress
    {
        public string GoalId;          // 对应的 ObjectiveDef.Id，空 = 本场无目标
        public bool Completed;         // 是否已达成
        public int Counter;            // 通用计数器（反应次数/捕获数等）
        public bool Failed;            // 是否已确定无法达成（如全员阵亡）
        public int StartTurn;          // 目标开始时的回合（限时类用）

        public void Reset(string goalId, int turn)
        {
            GoalId = goalId ?? "";
            Completed = false;
            Counter = 0;
            Failed = false;
            StartTurn = turn;
        }

        public bool Active => !string.IsNullOrEmpty(GoalId);
    }
}
