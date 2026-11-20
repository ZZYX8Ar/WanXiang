// ============================================================================
//  万相 · 灵感经济加成收口（2026-11-20）
//  ---------------------------------------------------------------------------
//  所有"灵感 → 局内/局外经济"的读取集中在此，调用方只问结果、不碰 Meta 细节。
//  新增效果 = CSV 加一行 + 这里加一个方法；删效果 = 删数据行，方法返回 0 自动归零。
// ============================================================================
namespace WanXiang.Modules.UI
{
    public static class InspirationBonus
    {
        private static int V(string key)
        {
            var meta = WanXiang.Meta.MetaStore.Ensure();
            return meta == null ? 0 : WanXiang.Battle.Core.InspirationRules.EffectValue(meta.InspirationIds, key);
        }

        /// <summary>奇遇事件额外灵卵。</summary>
        public static int EventEggs() => V("event_egg_bonus");

        /// <summary>奇遇事件额外墨铊。</summary>
        public static int EventInk() => V("event_ink_bonus");

        /// <summary>每场战斗胜利额外灵卵（局内货币）。</summary>
        public static int WinEggs() => V("win_egg_bonus");

        /// <summary>每场战斗胜利额外墨铊（局外货币）。</summary>
        public static int WinInk() => V("win_ink_bonus");

        /// <summary>异兽升级费用折扣（百分比，20 = 打 8 折）。</summary>
        public static int LevelCostOffPct() => V("level_cost_off");

        /// <summary>局外结算墨铊加成百分比（20 = ×1.20）。</summary>
        public static int SettleInkBonusPct() => V("settle_ink_bonus");

        /// <summary>进入新幕额外灵卵。</summary>
        public static int ActEggs() => V("act_egg_bonus");

        /// <summary>开局全队护盾百分比（8 = 8% 最大生命）。</summary>
        public static int AllyShieldPct() => V("ally_shield_start");

        /// <summary>进入新幕发灵卵奖励（两处 Act++ 共用，避免散落）。</summary>
        public static void GrantActEggs(WanXiang.Run.RunState run)
        {
            int eggs = ActEggs();
            if (eggs <= 0 || run == null) return;
            run.Eggs += eggs;
            UnityEngine.Debug.Log("[Inspiration] 广陌：进入新幕 灵卵 +" + eggs);
        }
    }
}
