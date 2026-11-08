// ============================================================================
//  万相 · 捕获流程（P3-c / UI 层，唯一入口）
//  ---------------------------------------------------------------------------
//  战斗胜利后询问一次捕获：
//    · 目标：敌方阵中"可捕 + 品级最高"的一只（首领/召唤由 CaptureRules 排除）
//    · 消耗：按品级 1~3 灵卵（不足则按现有卵数，0 直接跳过）
//    · 概率：CaptureRules.ChancePercent（含「游猎」道痕 capture_rate）
//    · 成功 → 记图鉴（返回是否首次）；失败 → 按规则返还
//
//  ⛔ 复用现有 DialogPanel.Choose，不新建 prefab、不新增面板。
//  ⚠ 只负责"一次询问 + 结算"，是否调用由战斗胜利流程决定（见 BattlePanel.HandOff）。
// ============================================================================
using Cysharp.Threading.Tasks;
using WanXiang.Battle.Core;

namespace WanXiang.Modules.UI
{
    public static class CaptureFlow
    {
        /// <summary>胜利后询问一次捕获。返回是否真的抓到。</summary>
        public static async UniTask<bool> OfferAsync(BattleState st, WanXiang.Run.RunState run)
        {
            if (st == null || run == null) return false;

            // ---- 选目标：敌方里可捕 + 品级最高的一只 ----
            var foes = st.UnitsOf(TeamSide.Enemy);
            if (foes == null || foes.Count == 0) return false;
            BeastDef target = null;
            for (int i = 0; i < foes.Count; i++)
            {
                var f = foes[i];
                if (f == null || f.Def == null) continue;
                if (!CaptureRules.IsCapturable(f.Def)) continue;
                if (target == null || (int)f.Def.Rarity > (int)target.Rarity) target = f.Def;
            }
            if (target == null) return false;

            // ---- 消耗：不足则按现有卵数（0 直接跳过，不弹框） ----
            int cost = CaptureRules.EggCost(target.Rarity);
            if (run.Eggs <= 0) return false;
            if (cost > run.Eggs) cost = run.Eggs;

            // ---- 概率（含游猎道痕） ----
            float dao = DaoMarkRules.EquippedBonus(DaoMarkPrefs.EquippedMarks(), "capture_rate");
            int chance = CaptureRules.ChancePercent(0, cost, dao);

            int pick = await DialogPanel.Choose(
                "捕获",
                "「" + target.DisplayName + "」已经力竭。\n\n" +
                "投入 " + cost + " 灵卵尝试捕获（成功率约 " + chance + "%）。\n" +
                "失败会返还一半灵卵。",
                "捕获（" + cost + " 灵卵）", "放弃");
            if (pick != 0) return false;

            // ---- 结算 ----
            var attempt = CaptureRules.Resolve(target, 0, cost, dao, UnityEngine.Random.value);
            run.Eggs -= cost;
            if (attempt.Refund > 0) run.Eggs += attempt.Refund;

            if (attempt.Success)
            {
                bool first = BeastCodexPrefs.MarkCaught(target.Id);
                UnityEngine.Debug.Log("[Capture] 捕获成功：" + target.DisplayName +
                                      (first ? "（图鉴新收录）" : "（图鉴已有）"));
            }
            else
            {
                UnityEngine.Debug.Log("[Capture] 捕获失败：" + target.DisplayName +
                                      "（返还 " + attempt.Refund + " 灵卵）");
            }
            WanXiang.Run.RunSave.SaveCurrent();
            return attempt.Success;
        }
    }
}
