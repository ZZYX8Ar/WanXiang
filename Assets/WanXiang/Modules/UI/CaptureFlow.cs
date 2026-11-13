// ============================================================================
//  万相 · 捕获编排（UI 层唯一入口，2026-11-13 重构）
//  ---------------------------------------------------------------------------
//  ⛔ 旧的"战斗胜利后弹 Dialog 询问一次"已删除：用户要求捕获发生在**战斗内**，
//     由 BattleCapturePresenter 播投球动画、按当前血量判定、明确回报成败。
//  · 判定/概率/返还：CaptureRules（Core，不改）
//  · 消耗：按品级 1~3 灵卵（灵感「俭捕」−1）；道痕 capture_rate 加成
//  · 成功 → 图鉴 / 异色 / 统计写档；失败 → 灵感「余烬」提高返还
//  · 表现层（动画/结果文字）由 BattleCapturePresenter 负责，本类只给结果
// ============================================================================
using WanXiang.Battle.Core;

namespace WanXiang.Modules.UI
{
    public struct CaptureOutcome
    {
        public bool Ok;             // 是否发起成功（灵卵/目标合法）
        public bool Success;
        public int Chance;
        public int Cost;
        public int Refund;
        public string Name;
        public string Reason;       // 未发起时的原因（给玩家看）
    }

    public static class CaptureFlow
    {
        /// <summary>战斗内：对指定敌方单位投一次球。同步返回，动画由表现层按结果播。</summary>
        public static CaptureOutcome TryInBattle(BattleState st, WanXiang.Run.RunState run, BattleUnit unit)
        {
            var o = new CaptureOutcome { Name = unit != null ? unit.DisplayName : "" };
            if (st == null || run == null || unit == null || unit.Def == null)
            { o.Reason = "目标无效"; return o; }
            if (!unit.IsAlive)
            { o.Reason = "目标已力竭"; return o; }
            if (unit.Side != TeamSide.Enemy)
            { o.Reason = "只能捕获敌方异兽"; return o; }
            var def = unit.Def;
            if (!CaptureRules.IsCapturable(def))
            { o.Reason = "「" + def.DisplayName + "」不可捕获（首领/召唤物）"; return o; }

            int cost = CaptureRules.EggCost(def.Rarity);
            {   // 灵感「炉火·俭捕」
                var mI = WanXiang.Meta.MetaStore.Ensure();
                int off = InspirationRules.EffectValue(mI.InspirationIds, "capture_cost_off");
                if (off > 0) cost = System.Math.Max(1, cost - off);
            }
            if (run.Eggs <= 0) { o.Reason = "灵卵不足"; return o; }
            if (cost > run.Eggs) cost = run.Eggs;

            float dao = DaoMarkRules.EquippedBonus(DaoMarkPrefs.EquippedMarks(), "capture_rate");
            int hpPct = unit.MaxHp > 0 ? unit.Hp * 100 / unit.MaxHp : 0;   // ★ 战斗内按当前血量
            var attempt = CaptureRules.Resolve(def, hpPct, cost, dao, UnityEngine.Random.value);

            o.Ok = true;
            o.Success = attempt.Success;
            o.Chance = attempt.ChancePercent;
            o.Cost = cost;
            o.Refund = attempt.Refund;
            o.Name = def.DisplayName;

            run.Eggs -= cost;
            if (attempt.Refund > 0)
            {
                var mR = WanXiang.Meta.MetaStore.Ensure();
                int refundPct = InspirationRules.EffectValue(mR.InspirationIds, "fail_refund_up");
                int refund = refundPct > 0 ? System.Math.Max(1, cost * refundPct / 100) : attempt.Refund;
                run.Eggs += refund;
                o.Refund = refund;
            }

            if (attempt.Success) CommitCapture(def, run, dao);
            WanXiang.Run.RunSave.SaveCurrent();
            UnityEngine.Debug.Log("[Capture] " + (attempt.Success ? "成功" : "失败") + "：" + def.DisplayName
                + " 血量=" + hpPct + "% 概率=" + o.Chance + "% 消耗=" + o.Cost + " 返还=" + o.Refund);
            return o;
        }

        /// <summary>成功写档：图鉴 / 捕获计数 / 异色判定（原胜利流程逻辑，原样保留）。</summary>
        private static void CommitCapture(BeastDef def, WanXiang.Run.RunState run, float dao)
        {
            run.Stats.Captures++;
            bool first = BeastCodexPrefs.MarkCaught(def.Id);

            var meta = WanXiang.Meta.MetaStore.Ensure();
            meta.CountCaptureForShiny();
            if (!meta.IsShiny(def.Id))
            {
                float shinyChance = (1f / 256f) * (1f + dao);
                bool byPity = meta.ShinyPity >= 200;
                var rng = new DeterministicRandom(
                    CoreMath.Fnv1a("shiny:" + def.Id + ":" + meta.HatchCount + ":" + meta.ShinyPity));
                if (byPity || rng.NextFloat() < shinyChance)
                {
                    string alt = "";
                    var pool = new System.Collections.Generic.List<string>();
                    var allBeasts = BeastTable.All;
                    for (int bi = 0; bi < allBeasts.Count; bi++)
                    {
                        var cand = allBeasts[bi];
                        if (cand == null || cand.Role != def.Role) continue;
                        string ct = cand.TraitId;
                        string tt = def.Trait.Id;
                        if (!string.IsNullOrEmpty(ct) && ct != tt && pool.IndexOf(ct) < 0) pool.Add(ct);
                    }
                    if (pool.Count > 0)
                    {
                        int take = System.Math.Min(3, pool.Count);
                        alt = pool[rng.NextInt(0, take)];
                        meta.MarkShiny(def.Id, alt);
                        UnityEngine.Debug.Log("[Capture] ★异色！「" + def.DisplayName + "」备选天性=" + alt);
                    }
                }
            }
            WanXiang.Meta.MetaStore.Save();
            UnityEngine.Debug.Log("[Capture] 捕获成功：" + def.DisplayName + (first ? "（图鉴新收录）" : "（图鉴已有）"));
        }
    }
}
