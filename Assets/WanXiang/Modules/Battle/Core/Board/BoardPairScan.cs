// ============================================================================
//  万相 · 战斗核心 · 相邻格对扫描（只读查询）
//  ---------------------------------------------------------------------------
//  BattleRules.ResolveAdjacency 算的是"这一回合发生了什么"；这里算的是
//  **"棋盘上此刻长着什么关系"** —— 两者必须由同一套判据推导，否则就会画出一条
//  "看起来该相冲、实际什么都没结算"的线，而玩家会以为规则坏了。
//
//  所以它放在核心层而不是表现层：编辑器灰盒窗口与运行时棋盘视图共用它，
//  谁都不许自己 GetComponent 式地重算一遍。
//
//  ⚠ 它与 ResolveAdjacency 的唯一刻意差别：**不筛存活**。
//    结算要跳过错尸体（尸体不该再提供同气），但画线要连到刚倒下的单位上，
//    否则画面会在承伤的那一帧提前断线，看起来像"规则漏了"。
//    存活过滤交给表现层按当前帧快照决定。
// ============================================================================

using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    public enum AdjacentPairKind
    {
        /// <summary>相生相邻：每回合回复 + 同气。</summary>
        Generate = 0,

        /// <summary>相克相冲：真实伤害 + 怒气获取 -10%。</summary>
        Counter = 1,

        /// <summary>本来会相冲，但格对涉及中宫土位而被平息。</summary>
        Pacified = 2,
    }

    /// <summary>一格对及其关系。A/B 是九宫格索引，A &lt; B（沿用 BoardLayout.AdjacentPairs 的生成顺序）。</summary>
    public struct AdjacentPair
    {
        public int A;
        public int B;
        public AdjacentPairKind Kind;

        public override string ToString() =>
            $"{A}-{B} " + (Kind == AdjacentPairKind.Generate ? "相生"
                         : Kind == AdjacentPairKind.Pacified ? "相冲·平息" : "相冲");
    }

    public static class BoardPairScan
    {
        /// <summary>
        /// 扫出某方棋盘上全部"有意义的"相邻格对。顺序 = <see cref="BoardLayout.AdjacentPairs"/>
        /// 的固定顺序（索引升序），所以同一局面每次扫出来都一样。
        /// </summary>
        /// <param name="into">结果缓冲。每次调用会先 Clear —— 表现层每帧复用同一个 List。</param>
        public static void Scan(BattleState st, TeamSide side, List<AdjacentPair> into)
        {
            into.Clear();
            if (st == null) return;

            var slots = st.SlotsOf(side);
            var pairs = BoardLayout.AdjacentPairs;
            bool centerPacifies = st.Config.CenterSuppressesAdjacentCounter;

            for (int k = 0; k < pairs.Length; k++)
            {
                int i = pairs[k][0];
                int j = pairs[k][1];
                var a = slots[i];
                var b = slots[j];
                if (a == null || b == null) continue;

                if (ElementMatrix.Generates(a.Element, b.Element)
                 || ElementMatrix.Generates(b.Element, a.Element))
                {
                    into.Add(new AdjacentPair { A = i, B = j, Kind = AdjacentPairKind.Generate });
                    continue;
                }

                if (!ElementMatrix.Counters(a.Element, b.Element)
                 && !ElementMatrix.Counters(b.Element, a.Element))
                    continue;

                bool pacified = centerPacifies && (a.Pos.IsCenter || b.Pos.IsCenter);
                into.Add(new AdjacentPair
                {
                    A = i,
                    B = j,
                    Kind = pacified ? AdjacentPairKind.Pacified : AdjacentPairKind.Counter,
                });
            }
        }

        /// <summary>两种棋盘（我方 / 敌方）一次扫完。</summary>
        public static void ScanBoth(BattleState st, List<AdjacentPair> player, List<AdjacentPair> enemy)
        {
            Scan(st, TeamSide.Player, player);
            Scan(st, TeamSide.Enemy, enemy);
        }

        /// <summary>把一组格对压成一行文本（"0-1 相生，4-7 相冲"）。自检报告与灰盒侧栏都用它。</summary>
        public static string Describe(List<AdjacentPair> pairs)
        {
            if (pairs == null || pairs.Count == 0) return "无";
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < pairs.Count; k++)
            {
                if (sb.Length > 0) sb.Append('，');
                sb.Append(pairs[k]);
            }
            return sb.ToString();
        }
    }
}
