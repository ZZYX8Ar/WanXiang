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
                run.Stats.Captures++;   // B2 骨架：捕获计数（道痕来源之一）
                bool first = BeastCodexPrefs.MarkCaught(target.Id);

                // B4：异色判定（v2 §19.2 C′）—— 1/256 ×(1+游猎)，ShinyPity≥200 必出；
                //     异色 = 生成时定死 1 条备选天性（同职业池 3 选 1，种子确定性），图鉴二选一（B5 表现）
                var meta = WanXiang.Meta.MetaStore.Ensure();
                meta.CountCaptureForShiny();
                if (!meta.IsShiny(target.Id))
                {
                    float shinyChance = (1f / 256f) * (1f + dao);
                    bool byPity = meta.ShinyPity >= 200;
                    var rng = new WanXiang.Battle.Core.DeterministicRandom(
                        WanXiang.Battle.Core.CoreMath.Fnv1a("shiny:" + target.Id + ":" + meta.HatchCount + ":" + meta.ShinyPity));
                    if (byPity || rng.NextFloat() < shinyChance)
                    {
                        // 备选天性：同职业（role）异兽的天性池，排除自身，3 选 1
                        string alt = "";
                        var pool = new System.Collections.Generic.List<string>();
                        var allBeasts = BeastTable.All;
                        for (int bi = 0; bi < allBeasts.Count; bi++)
                        {
                            var cand = allBeasts[bi];
                            if (cand == null || cand.Role != target.Role) continue;
                            string ct = cand.TraitId;
                            string tt = target.Trait.Id;   // TraitDef 是 struct，直接取
                            if (!string.IsNullOrEmpty(ct) && ct != tt && pool.IndexOf(ct) < 0) pool.Add(ct);
                        }
                        if (pool.Count > 0)
                        {
                            // 3 选 1（池内确定性取 3 个候选再随机——池小则全池）
                            int take = System.Math.Min(3, pool.Count);
                            alt = pool[rng.NextInt(0, take)];
                            meta.MarkShiny(target.Id, alt);
                            UnityEngine.Debug.Log("[Capture] ★异色！「" + target.DisplayName + "」备选天性=" + alt
                                + (byPity ? "（保底 " + meta.ShinyPity + "）" : ""));
                        }
                    }
                }
                WanXiang.Meta.MetaStore.Save();

                UnityEngine.Debug.Log("[Capture] 捕获成功：" + target.DisplayName +
                                      (first ? "（图鉴新收录）" : "（图鉴已有）")
                                      + (meta.IsShiny(target.Id) ? "（★异色）" : ""));
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
