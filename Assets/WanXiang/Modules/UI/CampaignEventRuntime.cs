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
        public static string Apply(EventDef ev, int choiceIndex)
        {
            var run = RunSave.Current;
            if (ev == null || ev.Choices == null) return "（无事件）";
            if (run == null) return "（没有存档，事件未生效）";
            if (choiceIndex < 0 || choiceIndex >= ev.Choices.Length) return "（无效选项）";

            var c = ev.Choices[choiceIndex];
            switch (c.Effect)
            {
                case EventEffect.Nothing:
                    return "什么都没发生。";

                case EventEffect.GainExp:
                {
                    int ups = run.AddExp(Mathf.Max(0, c.Value));
                    return "+" + c.Value + " 经验" + (ups > 0 ? "，主等级 → Lv" + run.PlayerLevel : "");
                }

                case EventEffect.GainEggs:
                    run.Eggs += c.Value;
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
                    var id = PickTeamBeast(run, wantEvolved: false, preferMasteryRoom: false);
                    if (string.IsNullOrEmpty(id)) return "队伍里没有可进化的异兽";
                    int route = (c.Value == 1 || c.Value == 2) ? c.Value
                                : (UnityEngine.Random.value < 0.5f ? 1 : 2);
                    run.Evolve(id, route);
                    return id + " 进化（路线 " + (route == 1 ? "A·消耗化" : "B·引爆化") + "）";
                }

                case EventEffect.TurnLimit:
                    run.EventTurnLimit = Mathf.Max(1, c.Value);
                    return "限时：下一场须在 " + run.EventTurnLimit + " 回合内通过";

                case EventEffect.TurnDelta:
                    // TODO: 回合上限改写在 BattleRequestFactory 读取 run 的字段，等该字段落地
                    return "本幕回合上限 " + (c.Value >= 0 ? "+" : "") + c.Value + "（待接战斗参数）";

                case EventEffect.GrantMaterial:
                    // TODO: 进化素材系统未落地，先折算灵卵
                    run.Eggs += Mathf.Max(0, c.Value) * 40;
                    return "+" + (Mathf.Max(0, c.Value) * 40) + " 灵卵（素材系统待接）";
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
    }
}
