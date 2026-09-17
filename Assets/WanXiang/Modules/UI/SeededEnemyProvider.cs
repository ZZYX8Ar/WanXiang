// ============================================================================
//  万相 · 敌方阵容抽签（SeededEnemyProvider）
//  ---------------------------------------------------------------------------
//  按「存档进度派生的种子」从内容目录抽敌方阵容。三条硬规矩：
//
//    1. **稳定**：同一存档、同一劫 → 抽到的敌人永远一致（种子由进度算出），
//       玩家背版、复盘、报 bug 都有据可查；
//    2. **可重复**：允许抽到重复的兽（有放回抽取），敌方凑不齐 5 只时重复补位；
//    3. **难度走 StatMul 不走选兽**：敌人强弱用 DeployEntry.StatMul 缩放
//       （战斗核心已保证速度/暴击不参与缩放，见 BattleFactory 注释），
//       选兽只负责"换口味"，两条轴互不干扰。
// ============================================================================

using System.Collections.Generic;
using WanXiang.Battle.Core;
using WanXiang.Fusion;

namespace WanXiang.Modules.UI
{
    public static class SeededEnemyProvider
    {
        /// <summary>由旅程进度派生抽签种子：同进度必同敌人，换劫必换阵容。</summary>
        public static ulong SeedOf(int slot, int realm, int jie, int wins)
        {
            ulong h = 1469598103934665603UL;   // FNV-1a 偏移基
            void Mix(int v)
            {
                h ^= (ulong)v;
                h *= 1099511628211UL;
            }
            Mix(slot); Mix(realm); Mix(jie); Mix(wins);
            return h;
        }

        /// <summary>
        /// 有放回抽 count 只敌方。池子为空返回空表。
        /// 同一只可以被抽中多次 —— 5 只格子装不满时这就是合法补位。
        /// </summary>
        public static List<BeastDef> Pick(ContentCatalogSO catalog, ulong seed, int count)
        {
            var list = new List<BeastDef>();
            var all = ContentLibrary.BuildBeasts(catalog);
            if (all == null || all.Length == 0 || count <= 0) return list;

            var rng = new System.Random(unchecked((int)(seed ^ (seed >> 32))));
            for (int i = 0; i < count; i++)
                list.Add(all[rng.Next(all.Length)]);
            return list;
        }

        /// <summary>敌方数量随劫数递进：一劫 3 只 → 三劫 5 只（跨境重新从 3 爬起）。</summary>
        public static int EnemyCount(int jie)
        {
            return System.Math.Clamp(2 + jie, 3, 5);
        }
    }
}
