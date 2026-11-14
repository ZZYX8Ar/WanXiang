// ============================================================================
//  不期而遇 · 结算（2026-11-01）
//  ---------------------------------------------------------------------------
//  数据在 `Campaign.EventCatalog`；这里把选项落成 RunState 的变化。
//  ⚠ 只做**已存在字段**能表达的效果；专精/进化/回合上限改变等留 TODO（等系统落地再补）。
// ============================================================================
using UnityEngine;
using WanXiang.Campaign;
using WanXiang.Run;

namespace WanXiang.Modules.UI
{
    public static class CampaignEventRuntime
    {
        /// <summary>应用一个选项，返回给玩家看的结果摘要（可直接显示在事件面板上）。</summary>
        /// <summary>连续事件：返回该选项指向的下一段事件 id（无 = 空串）。</summary>
        public static string NextEventId(WanXiang.Campaign.EventDef ev, int choiceIndex)
        {
            if (ev == null || ev.Choices == null) return "";
            if (choiceIndex < 0 || choiceIndex >= ev.Choices.Length) return "";
            var next = ev.Choices[choiceIndex].NextId;
            return string.IsNullOrEmpty(next) ? "" : next;
        }

        /// <summary>先应用选项代价；返回可读文案（无代价 = 空串）。</summary>
        private static string ApplyCost(WanXiang.Run.RunState run, WanXiang.Campaign.EventChoice c)
        {
            if (c == null || c.CostEffect == WanXiang.Campaign.EventEffect.Nothing) return "";
            switch (c.CostEffect)
            {
                case WanXiang.Campaign.EventEffect.SpendEggs:
                {
                    int pay = System.Math.Min(run.Eggs, System.Math.Max(0, c.CostValue));
                    run.Eggs -= pay;
                    return "付出 " + pay + " 灵卵。";
                }
                case WanXiang.Campaign.EventEffect.DamageTeamPct:
                {
                    int n = DamageTeam(run, c.CostValue);
                    return "全队 -" + c.CostValue + "% 生命（" + n + " 只）。";
                }
                default: return "";
            }
        }

        public static string Apply(EventDef ev, int choiceIndex)
        {
            var run = RunSave.Current;
            if (ev == null || ev.Choices == null) return "（无事件）";
            if (run == null) return "（没有存档，事件未生效）";
            if (choiceIndex < 0 || choiceIndex >= ev.Choices.Length) return "（无效选项）";

            var c = ev.Choices[choiceIndex];
            string costMsg = ApplyCost(run, c);   // ★ 通用代价：先扣，再发收益（选项文字必须写清）
            switch (c.Effect)
            {
                case EventEffect.Nothing:
                    return costMsg + "什么都没发生。";

                case EventEffect.GrantGrowth:
                    run.GrowthPct += System.Math.Max(0, c.Value);
                    WanXiang.Run.RunSave.SaveCurrent();
                    return costMsg + "本局全队攻击 +" + c.Value + "%（永久成长，战斗立即生效）";

                case EventEffect.Gamble:
                {
                    var rng = new WanXiang.Battle.Core.DeterministicRandom(
                        WanXiang.Battle.Core.CoreMath.Fnv1a(
                            "gamble:" + run.RunSeed + ":" + run.NodeOffset + ":" + c.ScopeId));
                    var row = WanXiang.Campaign.GambleTables.Roll(c.ScopeId, rng);
                    if (row == null)
                        return costMsg + "（赌局表缺失，本次空手 —— 请检查 gamble_tables.csv）";
                    switch (row.Effect)
                    {
                        case WanXiang.Campaign.EventEffect.GainEggs: run.AddRewardEggs(row.Value); break;
                        case WanXiang.Campaign.EventEffect.HealTeamPct: HealTeam(run, row.Value); break;
                        case WanXiang.Campaign.EventEffect.GrantGrowth: run.GrowthPct += row.Value; break;
                    }
                    WanXiang.Run.RunSave.SaveCurrent();
                    return costMsg + row.Text;
                }

                case EventEffect.GainExp:
                {
                    int ups = run.AddExp(Mathf.Max(0, c.Value));
                    return "+" + c.Value + " 经验" + (ups > 0 ? "，主等级 → Lv" + run.PlayerLevel : "");
                }

                case EventEffect.GainEggs:
                    run.AddRewardEggs(c.Value);   // B3-d：事件灵卵奖励走难度 egg_mul
                    return "+" + c.Value + " 灵卵";

                case EventEffect.HealTeamPct:
                {
                    int n = HealTeam(run, c.Value);
                    return "全队回复 " + c.Value + "%（" + n + " 只）";
                }

                case EventEffect.DamageTeamPct:
                {
                    // ⚠ 先扣血再发对应收益：把"代价"落实，收益按选项 ScopeId 决定
                    int n = DamageTeam(run, c.Value);
                    string extra = "";
                    if (!string.IsNullOrEmpty(c.ScopeId))
                    {
                        if (c.ScopeId.StartsWith("r_")) { run.Relics.Add(c.ScopeId); extra = "，获得遗物 " + c.ScopeId; }
                    }
                    return "全队 -" + c.Value + "% 生命（" + n + " 只）" + extra;
                }

                case EventEffect.StartFog:
                    run.FogLeft = Mathf.Max(run.FogLeft, c.Value);   // 复用现有瘴雾：走 1 格扣 1 节
                    run.Eggs += 0;
                    run.Relics.Add("");                              // 占位：素材系统未落地
                    run.Relics.RemoveAt(run.Relics.Count - 1);
                    return "瘴雾升起：接下来 " + c.Value + " 节不可见（雾中首节点奖励 +20% 待接）";

                case EventEffect.GrantRelic:
                    if (!string.IsNullOrEmpty(c.ScopeId)) run.Relics.Add(c.ScopeId);
                    return "获得遗物 " + c.ScopeId;

                case EventEffect.GrantMastery:
                {
                    var id = PickTeamBeast(run, wantEvolved: false, preferMasteryRoom: true);
                    if (string.IsNullOrEmpty(id)) return "没有可专精的异兽";
                    int slot = UnityEngine.Random.Range(0, 3);
                    int lv = run.AddMastery(id, slot, 1);
                    return id + " 的" + SlotCn(slot) + " 专精 → Lv" + lv;
                }

                case EventEffect.Evolve:
                {
                    // ★ 2026-11-02 进化要花素材：背包里没有就明说，不静默失败
                    int need = WanXiang.Run.ProgressionTuning.EvolveMaterialCost;
                    if (run.Materials < need)
                        return "缺少进化素材（需 " + need + " 个，现有 " + run.Materials + "）";
                    var id = PickTeamBeast(run, wantEvolved: false, preferMasteryRoom: false);
                    if (string.IsNullOrEmpty(id)) return "队伍里没有可进化的异兽";
                    int route = (c.Value == 1 || c.Value == 2) ? c.Value
                                : (UnityEngine.Random.value < 0.5f ? 1 : 2);
                    run.Materials -= need;
                    run.Evolve(id, route);
                    return id + " 进化（路线 " + (route == 1 ? "A·消耗化" : "B·引爆化") + "，余素材 " + run.Materials + "）";
                }

                case EventEffect.TurnLimit:
                    run.EventTurnLimit = Mathf.Max(1, c.Value);
                    return "限时：下一场须在 " + run.EventTurnLimit + " 回合内通过";

                case EventEffect.TurnDelta:
                    // TODO: 回合上限改写在 BattleRequestFactory 读取 run 的字段，等该字段落地
                    return "本幕回合上限 " + (c.Value >= 0 ? "+" : "") + c.Value + "（待接战斗参数）";

                case EventEffect.PickEgg:
                {
                    // ★ 2026-11-02：把「孵穴」的拾蛋从死面板搬进事件选项。
                    //   ⛔ 原来蛋正在孵化时点休整只会弹「先走吧」，等于这个节点白给 ——
                    //      现在改成：已有一枚在孵化就明说，没有就真给一枚。
                    if (run.EggHatchIn > 0)
                        return "巢里只剩空壳 —— 你手上那枚蛋还要 " + run.EggHatchIn + " 个节点才出壳。";

                    var all = Contents();
                    if (all == null) return "（没有内容目录，拾蛋失败）";
                    var pool = new System.Collections.Generic.List<WanXiang.Battle.Core.BeastDef>();
                    for (int i = 0; i < all.Length; i++)
                    {
                        var b = all[i];
                        if (b == null) continue;
                        bool owned = (run.Collection != null && run.Collection.Contains(b.Id))
                                  || (run.Team != null && run.Team.Contains(b.Id));
                        if (!owned) pool.Add(b);
                    }
                    if (pool.Count == 0) return "巢是空的 —— 世间的异兽都被你收过了。";

                    var egg = pool[UnityEngine.Random.Range(0, pool.Count)];
                    run.EggHatchIn = 2;
                    run.EggBeastId = egg.Id;
                    return "拾起一枚蛋（蛋壳上有淡淡的纹路）—— 再过 2 个节点就出壳。";
                }

                case EventEffect.GrantMaterial:
                    if (c.Value >= 0)
                    {
                        run.Materials += c.Value;
                        return "+" + c.Value + " 进化素材（现有 " + run.Materials + "）";
                    }
                    // 负值 = 消耗素材（不够就换灵卵补偿）
                    if (run.Materials + c.Value < 0)
                    {
                        int pay = (1 + c.Value) * 40;      // 缺 1 个折 40 灵卵
                        run.Eggs = Mathf.Max(0, run.Eggs - pay);
                        return "素材不足，改扣 " + pay + " 灵卵";
                    }
                    run.Materials += c.Value;
                    return "消耗 " + (-c.Value) + " 素材（余 " + run.Materials + "）";
            }
            return "（未处理的效果）";
        }

        private static string SlotCn(int slot) => slot == 0 ? "普攻" : slot == 1 ? "战技" : "绝技";

        /// <summary>从队伍里挑一只（优先没进化的 / 还有专精空间的）。</summary>
        private static string PickTeamBeast(WanXiang.Run.RunState run, bool wantEvolved, bool preferMasteryRoom)
        {
            if (run.Team == null || run.Team.Count == 0) return null;
            string fallback = null;
            for (int i = 0; i < run.Team.Count; i++)
            {
                var id = run.Team[i];
                if (string.IsNullOrEmpty(id)) continue;
                if (fallback == null) fallback = id;
                if (!wantEvolved && run.IsEvolved(id)) continue;
                if (preferMasteryRoom)
                {
                    int cap = run.MasteryCap(id);
                    int sum = 0;
                    for (int s = 0; s < 3; s++) sum = System.Math.Max(sum, run.MasteryOf(id, s));
                    if (sum >= cap) continue;
                }
                return id;
            }
            return fallback;
        }

        private static int HealTeam(WanXiang.Run.RunState run, int pct)
        {
            int n = 0;
            if (run.Team == null) return 0;
            for (int i = 0; i < run.Team.Count; i++)
            {
                var id = run.Team[i];
                if (string.IsNullOrEmpty(id)) continue;
                int cur = 100;
                if (run.TeamHpPct != null && run.TeamHpPct.TryGetValue(id, out int v)) cur = v;
                int next = Mathf.Min(100, cur + Mathf.Max(0, pct));
                if (next != cur) { run.TeamHpPct[id] = next; n++; }
            }
            return n;
        }

        private static int DamageTeam(WanXiang.Run.RunState run, int pct)
        {
            int n = 0;
            if (run.Team == null) return 0;
            for (int i = 0; i < run.Team.Count; i++)
            {
                var id = run.Team[i];
                if (string.IsNullOrEmpty(id)) continue;
                int cur = 100;
                if (run.TeamHpPct != null && run.TeamHpPct.TryGetValue(id, out int v)) cur = v;
                int next = Mathf.Max(1, cur - Mathf.Max(0, pct));
                run.TeamHpPct[id] = next;
                n++;
            }
            return n;
        }

        /// <summary>当前内容目录里的全部异兽（懒查一次；编辑器里目录可能存在多个实例）。</summary>
        private static WanXiang.Battle.Core.BeastDef[] Contents()
        {
            var all = UnityEngine.Resources.FindObjectsOfTypeAll<WanXiang.Fusion.ContentCatalogSO>();
            if (all == null || all.Length == 0) return null;
            return WanXiang.Fusion.ContentLibrary.BuildBeasts(all[0]);
        }
    }
}
