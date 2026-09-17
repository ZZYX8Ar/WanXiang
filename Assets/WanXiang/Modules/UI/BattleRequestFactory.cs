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
                                           string weather, out BattleRequest req,
                                           WanXiang.Campaign.NodeKind kind = WanXiang.Campaign.NodeKind.Encounter,
                                           int termIndex = -1)
        {
            req = null;
            if (catalog == null || run == null) return false;

            var all = ContentLibrary.BuildBeasts(catalog);
            if (all == null || all.Length < 2) return false;

            int act = System.Math.Max(1, System.Math.Min(run.Act, 5));
            int term = termIndex >= 0 ? termIndex : System.Math.Max(0, run.NodeOffset);
            bool elite = kind == WanXiang.Campaign.NodeKind.Elite;

            // 种子：同存档同幕同节点 → 同一套敌人（可背版、可复盘）
            ulong seed = CoreMath.Fnv1a("run:" + run.Slot + ":" + act + ":" + term + ":" + run.Wins);

            req = new BattleRequest
            {
                Title = "第" + Cn(act) + "幕 · 第 " + (term + 1) + " 节 · " +
                        (elite ? "精英战" : "遭遇战"),
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

            if (req.Player.Count == 0)
                for (int i = 0; i < 5 && i < all.Length; i++) req.Player.Add(all[i]);

            // ---- 敌方：走正式内容供给（Campaign.SeededEnemyProvider）----
            // 它按 GDD §5.1/§5.5 的规模表定阵容大小、算属性倍率，
            // 并给精英战的 1 只挂「劫象」—— 这些是"兽 + 倍率"那种简版表达不了的。
            // 池子暂用全图鉴（后续按幕/季节过滤，接口已经留好）。
            var provider = new WanXiang.Campaign.SeededEnemyProvider(actIdx => all);
            var entries = provider.EnemiesFor(act, term, kind, seed);

            req.EnemyEntries.Clear();
            req.Enemy.Clear();
            req.EnemyMul.Clear();
            foreach (var en in entries)
            {
                req.EnemyEntries.Add(en);
                req.Enemy.Add(en.Def);          // 兼容通道：HUD/预览按 BeastDef 显示名字
                req.EnemyMul.Add(en.StatMul);
            }

            return req.Player.Count > 0 && req.EnemyEntries.Count > 0;
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
