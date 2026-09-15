// ============================================================================
//  万相 · 确定性敌方阵容供给（STEP 3）
//  ---------------------------------------------------------------------------
//  给 <see cref="RunDriver"/> 用的默认内容来源：从"每幕一个池子"里按种子抽人。
//
//  为什么不用 HashSet/去重表来"抽不重复的 5 只"：那是集合语义，而集合的枚举顺序
//  是实现细节（.NET 版本一变就可能不同）—— 可复现性最怕这个。这里用**数组内
//  交换收缩**：抽到的下标与末尾交换、可用区收缩一，抽 n 只恰好 O(n)。
//
//  阵型固定为 { 0, 1, 4, 7, 8 }（前排两格 + 中宫 + 后排两角），与基准局一致：
//  中宫有人 ⇒ 中宫平息/中宫减伤这些棋盘规则每场都真的在跑。
// ============================================================================

using System;
using System.Collections.Generic;
using WanXiang.Battle.Core;

namespace WanXiang.Campaign
{
    public sealed class SeededEnemyProvider : ICampaignContent
    {
        /// <summary>默认阵型：0/1 前排，4 中宫，7/8 后排。</summary>
        public static readonly int[] DefaultFormation = { 0, 1, 4, 7, 8 };

        private readonly Func<int, BeastDef[]> _poolForAct;
        private readonly Func<int, BeastDef> _bossForAct;
        private readonly int[] _formation;

        /// <param name="poolForAct">某一幕的可选池（按 1..5 传入幕号）。返回 null/空 = 该幕无敌人（空阵容，自检里会看得见）。</param>
        /// <param name="bossForAct">守关战的主将（可空；为空则守关也只是普通阵容）。</param>
        public SeededEnemyProvider(Func<int, BeastDef[]> poolForAct,
                                   Func<int, BeastDef> bossForAct = null,
                                   int[] formation = null)
        {
            _poolForAct = poolForAct ?? (_ => null);
            _bossForAct = bossForAct;
            _formation = formation ?? DefaultFormation;
        }

        public DeployEntry[] EnemiesFor(int act, int termIndex, bool isBoss, ulong seed)
        {
            var pool = _poolForAct(act);
            if (pool == null || pool.Length == 0) return Array.Empty<DeployEntry>();

            var rng = new DeterministicRandom(seed ^ CoreMath.Fnv1a($"enemy:{act}:{termIndex}"));
            var boss = isBoss ? _bossForAct?.Invoke(act) : null;

            int slots = CoreMath.Min(_formation.Length, pool.Length + (boss != null ? 1 : 0));
            var picked = new List<BeastDef>(slots);
            if (boss != null) picked.Add(boss);

            // 数组内交换收缩：抽到的与末尾交换、可用区收缩一 —— 顺序完全由输入决定
            var idx = new int[pool.Length];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            int remaining = pool.Length;
            while (picked.Count < slots && remaining > 0)
            {
                int k = rng.NextInt(0, remaining);
                var pick = pool[idx[k]];
                if (pick != boss) picked.Add(pick);        // 主将已在队里就不重复抽它
                int tmp = idx[k];
                idx[k] = idx[remaining - 1];
                idx[remaining - 1] = tmp;
                remaining--;
            }

            var result = new DeployEntry[picked.Count];
            for (int i = 0; i < picked.Count; i++)
                result[i] = DeployEntry.Enemy(picked[i], _formation[i]);
            return result;
        }

        /// <summary>
        /// 天阙（v1.1 §5.5/§5.6 #17）：**后土站中宫**（御·中宫）+
        /// 玩家队伍前 3 只的镜像（劫律 19 起 5 只，这里先做默认 3 只）。
        /// 镜像 = 同一份 BeastDef 摆到敌方侧 —— BattleFactory 会为每个上阵指令克隆，
        /// 所以我方/敌方的同名单位不会互相串改。
        /// </summary>
        public DeployEntry[] FinaleFor(DeployEntry[] playerSquad, ulong seed)
        {
            var boss = _bossForAct?.Invoke(5);                    // 幕 5 = 后土
            var list = new List<DeployEntry>(4);
            int[] mirrorSlots = { 0, 1, 7, 8, 2 };                // 中宫留给后土，镜像依次落位
            int mirrorIndex = 0;

            if (boss != null) list.Add(DeployEntry.Enemy(boss, 4));
            if (playerSquad != null)
            {
                for (int i = 0; i < playerSquad.Length && mirrorIndex < 3; i++)
                {
                    var def = playerSquad[i].Def;
                    if (def == null) continue;
                    list.Add(DeployEntry.Enemy(def, mirrorSlots[mirrorIndex]));
                    mirrorIndex++;
                }
            }
            return list.ToArray();
        }
    }
}
