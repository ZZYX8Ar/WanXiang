// ============================================================================
//  万相 · 战斗组队工厂（运行期）
//  ---------------------------------------------------------------------------
//  结构验证版：从 ContentCatalogSO 里取前 5 只当我方、第 6~10 只当敌方。
//  正式版应改为：我方 = RunState 的队伍；敌方 = SeededEnemyProvider 按节点抽签。
//  两者都只需要换掉本文件里的两个 for 循环，BattlePanel 一行不用改。
// ============================================================================

using System.Collections.Generic;
using WanXiang.Battle.Core;
using WanXiang.Fusion;

namespace WanXiang.Modules.UI
{
    public static class BattleRequestFactory
    {
        public static bool TryBuild(ContentCatalogSO catalog, string title, string weather,
                                    ulong seed, out BattleRequest req)
        {
            req = null;
            if (catalog == null)
            {
                UnityEngine.Debug.LogError("[BattleRequestFactory] ContentCatalog 为空，无法组队。");
                return false;
            }

            var all = ContentLibrary.BuildBeasts(catalog);
            if (all == null || all.Length < 2) return false;

            req = new BattleRequest
            {
                Title = string.IsNullOrEmpty(title) ? "遭遇战" : title,
                WeatherName = weather ?? "",
                Seed = seed,
            };

            int half = all.Length / 2;
            for (int i = 0; i < 5 && i < all.Length; i++) req.Player.Add(all[i]);

            // 敌方在池子的后半段取，避免敌我和自己镜像对打（结构验证版的可读性考虑）
            var offset = all.Length > 5 ? System.Math.Min(half, all.Length - 1) : 0;
            for (int i = 0; i < 5; i++)
            {
                int idx = offset + i;
                if (idx >= all.Length) idx = i;
                req.Enemy.Add(all[idx]);
            }
            return true;
        }

        /// <summary>当前队伍里第一只的名字，用于主界面立绘展示。</summary>
        public static string FirstBeastId(ContentCatalogSO catalog)
        {
            if (catalog == null) return null;
            var all = ContentLibrary.BuildBeasts(catalog);
            return (all != null && all.Length > 0) ? all[0].Id : null;
        }

        // ==================================================================
        //  正式版组队：我方 = 存档队伍；敌方 = 种子抽签 + 劫数难度缩放
        // ==================================================================

        /// <summary>
        /// 从一段进行中的旅程组一场战斗。
        ///   我方 = 存档里记录的队伍（id 对不上的跳过；一只都对不上退回结构验证版）；
        ///   敌方 = SeededEnemyProvider 按进度种子抽签，数量随劫数 3→5，
        ///          强度用 DeployEntry.StatMul 挂旅程难度系数（速度/暴击不缩放）。
        /// 同一存档同一劫 → 敌人阵容与数值完全一致（可背版、可复盘）。
        /// </summary>
        public static bool TryBuildFromRun(ContentCatalogSO catalog, WanXiang.Run.RunState run,
                                           string weather, out BattleRequest req)
        {
            req = null;
            if (catalog == null || run == null) return false;

            var all = ContentLibrary.BuildBeasts(catalog);
            if (all == null || all.Length < 2) return false;

            // ---- 种子与规模 ----
            ulong seed = SeededEnemyProvider.SeedOf(run.Slot, run.Realm, run.Jie, run.Wins);
            int enemyCount = SeededEnemyProvider.EnemyCount(run.Jie);
            float mul = run.Difficulty;

            req = new BattleRequest
            {
                Title = "第" + Cn(run.Realm) + "境 · 第" + Cn(run.Jie) + "劫 · 遭遇战",
                WeatherName = weather ?? "",
                Seed = seed,
            };

            // ---- 我方：存档队伍按 id 回查 ----
            var byId = new Dictionary<string, BeastDef>();
            foreach (var b in all) byId[b.Id] = b;

            if (run.Team != null)
                foreach (var id in run.Team)
                    if (!string.IsNullOrEmpty(id) && byId.TryGetValue(id, out var def) &&
                        req.Player.Count < 5 && !req.Player.Contains(def))
                        req.Player.Add(def);

            // 队伍为空/全部失效 → 退回结构验证版前 5 只（保证一定能打）
            if (req.Player.Count == 0)
                for (int i = 0; i < 5 && i < all.Length; i++) req.Player.Add(all[i]);

            // ---- 敌方：种子抽签；强度走 EnemyMul（回放层用 WithMul 挂到 DeployEntry）----
            var enemies = SeededEnemyProvider.Pick(catalog, seed, enemyCount);
            foreach (var def in enemies)
            {
                req.Enemy.Add(def);
                req.EnemyMul.Add(mul);
            }

            return req.Player.Count > 0 && req.Enemy.Count > 0;
        }

        private static string Cn(int n)
        {
            switch (n)
            {
                case 1: return "一";
                case 2: return "二";
                case 3: return "三";
                default: return n.ToString();
            }
        }
    }
}
