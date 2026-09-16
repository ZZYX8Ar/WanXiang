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
    }
}
