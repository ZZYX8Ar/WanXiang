// ============================================================================
//  万相 · 战斗核心 · 回合循环与结算
//  ---------------------------------------------------------------------------
//  对应 GDD 6.2 的「回合心跳」骨架（那一节要求把心跳放在 System 里由协程驱动、
//  绝不放进 Update 轮询）。这里做的是**纯逻辑那部分**：
//
//      while (!IsOver)
//          1) 回合开始：场地结算（棋盘四条规则）→ 持续伤害与「生机」
//          2) 按速度生成出手序列（同速按站位从左到右）
//          3) 逐个单位行动：选技能 → 逐个原子效果结算 → 进 CD / 涨怒气
//          4) 回合末：CD 推进 / 状态时长 -1 / 属性修正 -1
//
//  协程、协程间隔、表现层播放都**不在这里** —— 这个程序集看不见 UnityEngine
//  （见 Types.cs 文件头的三条理由），它只负责"这一步之后世界变成什么样"。
//  表现层读 BattleLog 的事件流来播动画，读的时候不回头问 BattleState。
//
//  ⚠ 关于"伤害日志里记的是哪个数"：记的是**结算后、被护盾吸收前**的伤害量。
//    理由是这个数才是玩家在头顶伤害数字里看到的量；而被护盾吃掉的差额
//    可以从事件流里前后推出的护盾值算出来，不必重复记。
// ============================================================================

using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    /// <summary>一场战斗的结果摘要。自检报告直接用它，不需要再翻 BattleState。</summary>
    public struct BattleResult
    {
        public BattleOutcome Outcome;
        public int Turns;
        public int EventCount;
        public uint Fingerprint;

        public int GeneratePairs;    // 全程触发的相生相邻格对总数
        public int CounterPairs;     // 全程触发的相克相冲格对总数
        public int PacifiedPairs;    // 全程被中宫平息掉的相冲格对总数
        public int RegenTotal;
        public int TrueDamageTotal;
        public int QiGranted;
        public int Deaths;
        public int SkillsCast;
        public int Crits;

        public string Summary()
        {
            return $"结果 {Cn.Of(Outcome)}｜回合 {Turns}｜事件 {EventCount} 条｜指纹 0x{Fingerprint:X8}\n" +
                   $"  相生相邻 {GeneratePairs} 对（回复 {RegenTotal}）｜相克相冲 {CounterPairs} 对（真伤 {TrueDamageTotal}）\n" +
                   $"  中宫平息 {PacifiedPairs} 对｜同气 +{QiGranted} 层｜出手 {SkillsCast} 次｜暴击 {Crits} 次｜阵亡 {Deaths} 人";
        }
    }

    public static partial class BattleSimulator
    {        // ================================================================
        //  断点驱动（回合制 v2.1 P1）
        //  ------------------------------------------------------------------
        //  实现要点：用 C# 迭代器当状态机 —— buf / total 这些局部变量在迭代器里
        //  天然保持状态，**不需要**把它们提升到 BattleState，也不必重写回合逻辑。
        //    · RunSteps           原 Run 的循环体，仅在我方单位行动前 yield 一个决策点
        //    · Run                消费步骤流一次跑完（自动战斗 / 无 UI / 测试）
        //    · AdvanceToNextDecision / ApplyPlayerCommand   手动模式的驱动接口
        //  伤害、五行、状态、结算的全部既有逻辑一行未改。
        // ================================================================

        /// <summary>战斗推进的一步。</summary>
        public struct BattleStep
        {
            public BattleStepKind Kind;
            public BattleUnit Unit;        // NeedDecision：等待下令的单位
            public BattleResult Result;    // Finished：整场结算
        }
        public enum BattleStepKind
        {
            /// <summary>自动推进中（敌方行动、回合结算等），UI 可继续。</summary>
            Animating = 0,
            /// <summary>轮到我方单位，等待玩家下令。</summary>
            NeedDecision = 1,
            /// <summary>战斗结束（Result 有效）。</summary>
            Finished = 2,
        }
        // ================================================================
        //  目标选择
        // ================================================================

        private const int PickLowestHp = 1;        private const int PickHighestHp = 2;        private const int PickHighestAtk = 3;
        /// <summary>每趟战斗复用的缓冲，避免在热路径里反复分配小 List。</summary>
        private sealed class Buffers
        {
            public readonly List<BattleUnit> Targets = new List<BattleUnit>(8);
            public readonly List<BattleUnit> Order = new List<BattleUnit>(10);
        }
    }
}
