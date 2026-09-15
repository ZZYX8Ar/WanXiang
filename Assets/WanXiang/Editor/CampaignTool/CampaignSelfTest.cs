// ============================================================================
//  万相 · 节气节点图自检（campaign.selftest）
//  ---------------------------------------------------------------------------
//  GDD STEP 3「节气节点图与分叉路径」的机器判据。核心判据三类：
//  ① 图形正确：6 节点 4 层、收尾必土、路径枚举 = 4 条 × 4 节点；
//  ② 推进正确：非法移动被拒、一局 16 常规 + 5 守关 = 21 战；
//  ③ 余气正确：跨幕残留 2 节点、强度减半、过期消失、合成确定性。
//  战斗侧只做一条"指纹可观察"对照 —— 天时结算本身的回归仍在 weather.selftest。
// ============================================================================

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Core;
using WanXiang.Campaign;
using WanXiang.Editor.BattleTool;
using WanXiang.Editor.WeatherTool;

namespace WanXiang.Editor.CampaignTool
{
    public static class CampaignSelfTest
    {
        private const string ReportPath = "Temp/WanXiangDiag/campaign_selftest.txt";

        private static int _pass, _fail;
        private static readonly List<string> Failures = new List<string>();

        [MenuItem("万相/节气/节点图自检（campaign.selftest）")]
        public static void RunFromMenu()
        {
            var lines = Run("campaign.selftest");
            foreach (var l in lines) Debug.Log("[节点图自检] " + l);
            // 只在失败时弹模态框（全绿弹窗会卡死 MCP 自动化，见项目记忆）
            if (_fail > 0)
                EditorUtility.DisplayDialog("节点图自检",
                    $"❌ {_pass} 过 / {_fail} 败，失败项见 Console 与 {ReportPath}。", "好");
        }

        public static string[] Run(string command)
        {
            var lines = new List<string>();
            _pass = 0;
            _fail = 0;
            Failures.Clear();

            lines.Add("万相 · 节气节点图自检（campaign.selftest）");
            lines.Add("========================================================================");
            lines.Add($"时间　　{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            lines.Add("拓扑　　v1 占位：每幕 4 层 [2,2,1,1] 相邻层全连通（GDD 只定 6 选 4 + 收尾必土）");

            var acts = SolarTermGraph.BuildDefault();

            // ---- ① 幕表 ----
            bool shapeOk = acts.Length == 5;
            for (int a = 0; a < 4 && shapeOk; a++)
            {
                var g = acts[a];
                shapeOk &= g.NodeCount == 6 && g.PathLength == 4
                        && g.Layers[0].Length == 2 && g.Layers[1].Length == 2
                        && g.Layers[2].Length == 1 && g.Layers[3].Length == 1
                        && g.EndsAtEarthTerm;
            }
            shapeOk &= acts[4].IsEmpty && acts[4].BossName == "后土";
            Check(lines, shapeOk,
                  "① 幕表：4 幕各 6 节点 4 层 [2,2,1,1]、收尾=土节点（6/12/18/24），幕 5 空幕（后土）");

            // ---- ② 路径枚举：每幕 4 条 × 4 节点，末节点=土，节点不重复 ----
            bool pathsOk = true;
            int totalPaths = 0;
            foreach (var g in acts)
            {
                var paths = g.EnumeratePaths();
                totalPaths += paths.Count;
                foreach (var p in paths)
                {
                    pathsOk &= p.Length == 4;
                    pathsOk &= p[3] == g.NodeCount - 1;                 // 末节点=土（下标 5）
                    pathsOk &= p[0] != p[1] && p[1] != p[2] && p[2] != p[3];
                }
            }
            Check(lines, pathsOk && totalPaths == 16,
                  $"② 路径枚举：4 幕 × 4 条 = {totalPaths} 条，每条 4 节点、末节点=土、不重复");

            // ---- ②b v1.1 §4.2 三条硬约束（决定单局场次 13~17 的关键） ----
            bool constraintsOk = true;
            string constraintWhy = "全部通过";
            foreach (var g in acts)
                if (!g.MeetsV11Constraints(out var why)) { constraintsOk = false; constraintWhy = $"幕{g.Act}：{why}"; break; }
            int battleNodes = 0;
            foreach (var g in acts)
                foreach (var k in g.Kinds) if (NodeKinds.IsBattle(k)) battleNodes++;
            Check(lines, constraintsOk && battleNodes == 12,
                  $"②b v1.1 硬约束：一/二层各恰 1 战、三层固定精英、四层固定非战斗土节"
                  + $"（{constraintWhy}）；战斗节点 12 个（24 节点里正好一半）");

            // ---- ③ 非法移动被拒：跳层 / 同层 / 回退 ----
            var g1 = acts[0];
            Check(lines, !g1.CanMove(0, 4) && !g1.CanMove(0, 1) && !g1.CanMove(2, 0) && g1.CanMove(0, 2) && g1.CanMove(0, 3),
                  "③ CanMove：跳层/同层/回退拒绝，相邻下一层放行");

            // ---- ④ 一局 21 战：16 常规 + 5 守关 ----
            var run = new RunState(acts, WeatherCatalog.GetSolarTerm);
            bool ok = true;
            for (int a = 0; a < 4; a++)
            {
                ok &= run.EnterNode(0) && run.EnterNode(2) && run.EnterNode(4) && run.EnterNode(5);
                ok &= run.AtBoss && run.DefeatBoss();
            }
            ok &= run.CurrentAct == 5 && run.Path.Count == 16;
            ok &= run.EnterNode(0) == false;                            // 空幕无节点
            ok &= run.AtBoss && run.DefeatBoss() && run.Finished;
            Check(lines, ok && run.BossesDefeated == 5 && run.Path.Count == 16,
                  $"④ 一局全程：每幕走 4 节点（含非战斗）×4 幕 = {run.Path.Count} 节点"
                  + $" + 4 守关 + 1 天阙；通关判定成立");

            // ---- ⑤ 余气：跨幕残留 2 节点、强度减半、过期消失（真实天时目录） ----
            var run2 = new RunState(acts, WeatherCatalog.GetSolarTerm);
            ok = run2.EnterNode(0) && run2.EnterNode(2) && run2.EnterNode(4) && run2.EnterNode(5)
                 && run2.DefeatBoss();
            ok &= run2.Lingers.Count == 1 && run2.Lingers[0].FromTerm == 1
               && run2.Lingers[0].NodesLeft == 2
               && run2.Lingers[0].Weather.BuffName.Contains("余气")
               && run2.Lingers[0].Weather.TurnStart[0].Atom.StatusStacks == 1;   // 生机 2 层减半 ⇒ 1 层
            Check(lines, ok, "⑤ 跨幕：余气=上一幕首节点（立春·东风解冻）减半版，残留 2 节点");

            // 幕 2 节点 1（小满·8）：节点无原子 + 立春余气的生机
            ok = run2.EnterNode(1);
            var w1 = run2.ComposeCurrentWeather();
            ok &= w1 != null && w1.Element == Element.Fire                // 元素/名字全取节点
               && w1.BuffName == "麦气至"
               && System.Math.Abs(w1.CdAdvanceMulPlayer - 1.3f) < 0.0001f  // 节点修正保留
               && w1.TurnStart.Length == 1                                 // 余气原子拼进来
               && w1.TurnStart[0].Atom.Kind == EffectAtomKind.ApplyStatus;
            Check(lines, ok, "⑤ 节点1（小满）：节点 CD+30% 保留，余气生机原子拼入，元素/名字取节点");

            // 节点 2（夏至·10）：余气仍在场（残留 2 节点 ⇒ 前两个节点都算）
            ok = run2.EnterNode(3);
            var w2 = run2.ComposeCurrentWeather();
            ok &= run2.Lingers[0].NodesLeft == 0
               && w2 != null && w2.TurnStart != null && w2.TurnStart.Length == 1
               && System.Math.Abs(w2.DamageAllMultiplier - 1.25f) < 0.0001f;
            Check(lines, ok, $"⑤ 节点2（夏至）：余气仍生效（残留 2 节点），夏至 ×1.25 保留"
                           + $"（composed={(w2 == null ? "null" : w2.Id)}）");

            // 节点 3（小暑·11）：余气过期，只剩节点自己的灼烧
            ok = run2.EnterNode(4);
            var w3 = run2.ComposeCurrentWeather();
            ok &= run2.Lingers.Count == 0
               && w3 != null && w3.TurnStart != null && w3.TurnStart.Length == 1
               && w3.TurnStart[0].Atom.Kind == EffectAtomKind.Damage       // 小暑的 3% 灼烧，不是生机
               && System.Math.Abs(w3.DamageAllMultiplier - 1f) < 0.0001f;
            Check(lines, ok, $"⑤ 节点3（小暑）：余气过期消失，只剩节点自己的原子"
                           + $"（composed={(w3 == null ? "null" : w3.Id)}）");

            // 节点 4（大暑·12）：无余气，五行乘数完好
            ok = run2.EnterNode(5);
            var w4 = run2.ComposeCurrentWeather();
            ok &= w4 != null
               && System.Math.Abs(w4.FireDamageMul - 0.7f) < 0.0001f
               && System.Math.Abs(w4.EarthDamageMul - 1.3f) < 0.0001f;
            Check(lines, ok, $"⑤ 节点4（大暑）：无余气，五行乘数完好（composed={(w4 == null ? "null" : w4.Id)}）");

            // ---- ⑥ 合成确定性 ----
            //     ⚠ 大暑只有 TurnEnd 没有 TurnStart，拿 TurnStart 断言会空引用（实测踩过）。
            var wAgain = run2.ComposeCurrentWeather();
            bool detOk = ReferenceEquals(wAgain, w4)
               || (wAgain != null && w4 != null
                   && wAgain.Id == w4.Id
                   && wAgain.TurnEnd != null && w4.TurnEnd != null
                   && wAgain.TurnEnd.Length == w4.TurnEnd.Length
                   && System.Math.Abs(wAgain.FireDamageMul - w4.FireDamageMul) < 0.0001f);
            Check(lines, detOk, $"⑥ 合成确定性：同一状态合成两次逐字段一致"
                              + $"（w4={(w4 == null ? "null" : w4.Id)}，wAgain={(wAgain == null ? "null" : wAgain.Id)}）");

            // ---- ⑦ 未翻译节点：无节点天时但余气照常（内容缺口不拖死地图系统） ----
            //     ⚠ 空注在节气 7（立夏）——别注 1（立春），它是余气来源，注空了整条余气链就断。
            var run3 = new RunState(acts, i => i == 7 ? null : WeatherCatalog.GetSolarTerm(i));
            ok = run3.EnterNode(0) && run3.EnterNode(2) && run3.EnterNode(4) && run3.EnterNode(5)
                 && run3.DefeatBoss() && run3.EnterNode(0);              // 幕2 节点1 = 立夏（未翻 ⇒ null）
            var w5 = run3.ComposeCurrentWeather();
            ok &= w5 != null && w5.TurnStart != null && w5.TurnStart.Length == 1   // 只剩立春余气
               && w5.BuffName == "余气";                                  // 无节点名可取
            Check(lines, ok, "⑦ 未翻译节点（立夏）：节点天时为空但余气照常生效，不崩");

            // ---- ⑧ 非法推进：0/1 个节点打 Boss 被拒、走满放行、通关后再打被拒 ----
            //     拆成逐步断言：链式 && 一处挂了不知道挂在哪。
            var run4 = new RunState(acts, WeatherCatalog.GetSolarTerm);
            Check(lines, !run4.DefeatBoss(), "⑧a 0 个节点打 Boss ⇒ 拒");
            Check(lines, run4.EnterNode(0) && !run4.DefeatBoss(), "⑧b 走 1 个节点打 Boss ⇒ 拒");
            Check(lines, run4.EnterNode(2) && run4.EnterNode(4) && run4.EnterNode(5)
                       && run4.DefeatBoss(), "⑧c 走满 4 个节点 ⇒ 打 Boss 放行（进第 2 幕）");
            Check(lines, !run4.DefeatBoss(), "⑧d 新幕 0 个节点 ⇒ 再打被拒");
            Check(lines, run4.EnterNode(0) && run4.EnterNode(2) && run4.EnterNode(4) && run4.EnterNode(5)
                       && run4.DefeatBoss()
                       && run4.EnterNode(0) && run4.EnterNode(2) && run4.EnterNode(4) && run4.EnterNode(5)
                       && run4.DefeatBoss()
                       && run4.EnterNode(0) && run4.EnterNode(2) && run4.EnterNode(4) && run4.EnterNode(5)
                       && run4.DefeatBoss()
                       && run4.CurrentAct == 5, "⑧e 幕 2/3/4 各走满 4 节点并守关，抵达长夏（CurrentAct=5）");
            Check(lines, run4.DefeatBoss() && run4.Finished && !run4.DefeatBoss(),
                  "⑧f 后土可打、通关判定成立、通关后重复打被拒");

            // ---- ⑨ 战斗可观察：余气在场时战局真的变了 ----
            //     小满+立春余气 vs 小单单独：多一笔"生机"事件流，指纹必然不同。
            var nodeOnly = WeatherCatalog.GetSolarTerm(8);
            var composed1 = new RunState(acts, WeatherCatalog.GetSolarTerm);
            composed1.EnterNode(0); composed1.EnterNode(2); composed1.EnterNode(4);
            composed1.EnterNode(5); composed1.DefeatBoss(); composed1.EnterNode(1);
            uint fpNode = Run1v1Fingerprint(nodeOnly);
            uint fpComposed = Run1v1Fingerprint(composed1.ComposeCurrentWeather());
            Check(lines, fpNode != fpComposed,
                  $"⑨ 余气可观察：小满单独 0x{fpNode:X8} ≠ 小满+立春余气 0x{fpComposed:X8}");

            // ================================================================
            //  一局编排（RunDriver）：节点 → 天时 → 敌队 → 战斗 → 推进
            // ================================================================

            // 内容：每幕一套"该幕五行 × 五职业"的灰盒池 + 该幕守关。
            var pools = new BeastDef[5][];
            for (int a = 1; a <= 5; a++)
            {
                var el = acts[a - 1].SeasonElement;
                pools[a - 1] = new[]
                {
                    BattleSampleContent.Make($"a{a}g", "御", el, RoleType.Guard),
                    BattleSampleContent.Make($"a{a}s", "攻", el, RoleType.Striker),
                    BattleSampleContent.Make($"a{a}c", "术", el, RoleType.Caster),
                    BattleSampleContent.Make($"a{a}p", "辅", el, RoleType.Support),
                    BattleSampleContent.Make($"a{a}f", "疾", el, RoleType.Swift),
                };
            }
            var bosses = new BeastDef[5];
            for (int a = 1; a <= 5; a++)
                // ⚠ 守关取灵品（与池子同级）：本自检验的是**编排链路**能否跑通 21 战，
                //   不是平衡。神品守关 + 5 神品玩家 = 胜负五五开，自检会随机变红
                //   （实测：第一版用神品守关，第 1 幕守关就败北）。平衡是策划给数值表之后的事。
                bosses[a - 1] = BattleSampleContent.Make($"boss{a}", acts[a - 1].BossName,
                                                          acts[a - 1].SeasonElement, RoleType.Guard);
            var content = new SeededEnemyProvider(a => pools[a - 1], a => bosses[a - 1]);

            // 我方：一套神品**进攻型**满编。
            // ⚠ 刻意不带 Support 位：灰盒内容里治疗 + 护盾能互相拖住（实测：5 神品带辅助
            //   对上 5 灵品，第 1 幕守关打成 30 回合平局 ⇒ 按"平局=没打过"这局就结束了）。
            //   本自检验的是编排链路能不能跑通 21 战，阵容取能分出胜负的那种。
            //   ⚠ 同一条实测也给策划一个信号：**回合上限 30 + 治疗护盾体系容易产生僵局**，
            //   正式数值表出来时要专门看这件事。
            var strong = new DeployEntry[]
            {
                DeployEntry.Player(BattleSampleContent.Make("p0", "甲", Element.Wood,  RoleType.Guard,  Rarity.Legend), 0),
                DeployEntry.Player(BattleSampleContent.Make("p1", "乙", Element.Fire,  RoleType.Striker, Rarity.Legend), 1),
                DeployEntry.Player(BattleSampleContent.Make("p2", "丙", Element.Water, RoleType.Striker, Rarity.Legend), 4),
                DeployEntry.Player(BattleSampleContent.Make("p3", "丁", Element.Metal, RoleType.Caster, Rarity.Legend), 7),
                DeployEntry.Player(BattleSampleContent.Make("p4", "戊", Element.Earth, RoleType.Swift,  Rarity.Legend), 8),
            };

            // ---- ⑩ 敌队供给：5 人、站位合法唯一、同种子同阵容、换种子换人 ----
            var squad1 = content.EnemiesFor(2, 10, NodeKind.Encounter, 12345UL);
            var squad2 = content.EnemiesFor(2, 10, NodeKind.Encounter, 12345UL);
            var seen = new bool[9];
            bool squadOk = squad1.Length == EnemyBudget.SquadSize(2, NodeKind.Encounter);   // v1.1：幕 2 遭遇 = 4 只
            for (int i = 0; i < squad1.Length; i++)
            {
                squadOk &= squad1[i].PosIndex == SeededEnemyProvider.DefaultFormation[i]
                        && squad1[i].Side == TeamSide.Enemy && !seen[squad1[i].PosIndex];
                seen[squad1[i].PosIndex] = true;
                squadOk &= ReferenceEquals(squad1[i].Def, squad2[i].Def);
            }
            bool squadVaries = false;
            for (ulong alt = 701UL; alt < 720UL && !squadVaries; alt++)
            {
                var s = content.EnemiesFor(2, 10, NodeKind.Encounter, alt);
                for (int i = 0; i < s.Length && i < squad1.Length; i++)
                    if (!ReferenceEquals(s[i].Def, squad1[i].Def)) { squadVaries = true; break; }
            }
            Check(lines, squadOk && squadVaries,
                  $"⑩ 敌队供给：{squad1.Length} 人（按规模表）/ 站位 0·1·4·7·8 唯一合法 / 同种子同阵容 / 换种子换人");

            // ---- ⑪ 整局跑完：21 战（16 常规 + 5 守关），终局=通关 ----
            // ⚠ 用"成长后的"神品倍率（×1.9）跑通结构：BP 曲线到幕 3~4 会追平静态神品队
            //   （敌方 Rare×1.551 vs 神品 ×1.30，见对齐清单 §5.7），占位内容没有灵市/融合
            //   可成长，所以用倍率模拟"毕业队"——曲线本身是设计行为。
            var growthCfg = BattleConfig.Default;
            growthCfg.LegendMultiplier = 1.90f;
            var runA = new RunDriver(growthCfg, acts, WeatherCatalog.GetSolarTerm,
                                     content, 20260914UL, RunChoosers.Seeded(20260914UL));
            var outcomeA = runA.Play(strong);
            // v1.1：每幕 2~3 战（一/二层各 1 场 + 三层精英）+ 4 守关 + 1 天阙 = 13~17
            int finaleSteps = 0, bossSteps = 0, nonBattleSteps = 0;
            foreach (var s in runA.Steps)
            {
                if (s.StepKind == RunStepKind.Finale) finaleSteps++;
                if (s.StepKind == RunStepKind.Boss) bossSteps++;
                if (!s.IsBattle) nonBattleSteps++;
            }
            Check(lines, outcomeA == RunOutcome.Completed
                       && runA.BattleCount >= 13 && runA.BattleCount <= 17
                       && runA.Steps.Count == 21                     // 16 节点 + 4 守关 + 1 天阙
                       && runA.State.Path.Count == 16
                       && finaleSteps == 1 && bossSteps == 4 && nonBattleSteps >= 4,
                  $"⑪ 整局跑完：{outcomeA}，{runA.BattleCount} 战（合法区间 13~17）"
                  + $"／{runA.Steps.Count} 步（16 节点 + 4 守关 + 1 天阙）"
                  + $"／非战斗节点 {nonBattleSteps} 个（每幕至少 1 个）");

            // ---- ⑫ 一局确定性：同种子逐场指纹/回合/结局一致 ----
            var runB = new RunDriver(growthCfg, acts, WeatherCatalog.GetSolarTerm,
                                     content, 20260914UL, RunChoosers.Seeded(20260914UL));
            runB.Play(strong);
            var battlesA = new List<RunStep>(runA.Battles);
            var battlesB = new List<RunStep>(runB.Battles);
            bool sameRun = battlesA.Count == battlesB.Count;
            for (int i = 0; i < battlesA.Count && sameRun; i++)
                sameRun &= battlesA[i].Fingerprint == battlesB[i].Fingerprint
                        && battlesA[i].Turns == battlesB[i].Turns
                        && battlesA[i].TermIndex == battlesB[i].TermIndex;
            Check(lines, sameRun, "⑫ 一局确定性：同种子两局逐场指纹/回合/路径一致");

            // ---- ⑬ 天时贯通（真实目录）：幕 2 前两场带立春余气，第 3 场起散尽 ----
            var runD = new RunDriver(growthCfg, acts, WeatherCatalog.GetSolarTerm,
                                     content, 20260914UL, RunChoosers.First);
            var outcomeD = runD.Play(strong);
            var act2 = new List<RunStep>();
            foreach (var s in runD.Battles)
                if (s.Act == 2 && s.StepKind != RunStepKind.Boss && s.StepKind != RunStepKind.Finale)
                    act2.Add(s);
            string act2Desc = act2.Count == 0 ? "（无）" : "";
            for (int i = 0; i < act2.Count; i++)
                act2Desc += (i > 0 ? "｜" : "") + $"第{i + 1}场 {act2[i].WeatherId ?? "null"}";
            // ⚠ 幕 2 的战斗只有 3 场（First 选路：立夏/芒种/小暑；大暑是灵市）——
            //   "第四层必非战斗"正是 v1.1 的硬约束之一
            bool weatherOk = act2.Count == 3
                          && (act2[0].WeatherId ?? "").Contains("linger_solar_lichun")
                          && (act2[1].WeatherId ?? "").Contains("linger_solar_lichun")
                          && !(act2[2].WeatherId ?? "").Contains("linger");
            Check(lines, weatherOk,
                  $"⑬ 天时贯通（{outcomeD}／共 {runD.BattleCount} 战）：{act2Desc}");

            bool bossOk = false;
            foreach (var s in runD.Battles)
                if (s.StepKind == RunStepKind.Boss)
                { bossOk = s.TermIndex == -1 && s.WeatherId == null; break; }
            Check(lines, bossOk, "⑬ 守关战：不挂节点天时（WeatherId=null），也不占节气节点");

            // ---- ⑭ 败北终止：单只灵品后卫对上满编 ⇒ 早于 21 战结束 ----
            var weak = new DeployEntry[]
            {
                DeployEntry.Player(BattleSampleContent.Make("solo", "独", Element.Wood, RoleType.Guard), 4),
            };
            var runE = new RunDriver(BattleConfig.Default, acts, WeatherCatalog.GetSolarTerm,
                                     content, 20260914UL, RunChoosers.First);
            var outcomeE = runE.Play(weak);
            Check(lines, outcomeE == RunOutcome.Defeated && runE.BattleCount < 13,
                  $"⑭ 败北终止：{outcomeE}，只打了 {runE.BattleCount} 战就结束");

            // ---- ⑮ 整局摘要可读（节点图窗口与报告共用同一份文本） ----
            var summary = runA.Summary();
            Check(lines, summary.Contains("通关") && summary.Split('\n').Length >= 21,
                  "⑮ 整局摘要：含结论与全部行程（21 步以上）");
            lines.Add("  · 整局摘要（⑪ 那一局，逐场）：");
            foreach (var l in summary.Split('\n'))
                if (l.Trim().Length > 0) lines.Add("      " + l);

            // ---- ⑯ 节点图视图数据（灰盒窗口的数据层；窗口本身是人工判据，但这一层能自动断言） ----
            var viewRun = new RunDriver(BattleConfig.Default, acts, WeatherCatalog.GetSolarTerm,
                                        content, 20260914UL, RunChoosers.First);
            var actLines0 = NodeMapView.ActLines(viewRun.State.CurrentGraph, viewRun.State,
                                                 WeatherCatalog.GetSolarTerm);
            bool viewOk = actLines0.Count == 4                                   // 4 层 → 4 行
                       && actLines0[0].Contains("○")                             // 第一层全部可选
                       && actLines0[0].Contains("立春") && actLines0[0].Contains("东风解冻");
            Check(lines, viewOk, $"⑯ 视图数据·开局：4 层行、第一层可选中带节气名与天时名（{actLines0[0]}）");

            viewRun.PlayNextStep(strong);                                        // 走一个节点
            int visitedOffsets = NodeMapView.VisitedOffsets(viewRun.State.CurrentGraph, viewRun.State).Count;
            int legalNext = NodeMapView.LegalNextOffsets(viewRun.State.CurrentGraph, viewRun.State).Count;
            var lines1 = NodeMapView.ActLines(viewRun.State.CurrentGraph, viewRun.State,
                                              WeatherCatalog.GetSolarTerm);
            // 刚走过的节点同时是"已过"与"当前"，标记优先显示 ◀（当前）——
            // ✔ 要到再走一个节点之后才会出现在它身上。
            Check(lines, visitedOffsets == 1 && legalNext == 2 && lines1[0].Contains("◀"),
                  $"⑯ 视图数据·走过一个节点：已过 {visitedOffsets} 个（当前 ◀）、下一步 {legalNext} 个合法分支");

            viewRun.PlayNextStep(strong);                                        // 再走一个（跨层）
            var lines2 = NodeMapView.ActLines(viewRun.State.CurrentGraph, viewRun.State,
                                              WeatherCatalog.GetSolarTerm);
            Check(lines, lines2[0].Contains("✔") && lines2[1].Contains("◀")
                       && NodeMapView.VisitedOffsets(viewRun.State.CurrentGraph, viewRun.State).Count == 2,
                  $"⑯ 视图数据·跨层：上一层节点转 ✔、下一层当前 ◀（{lines2[0]}）");

            var lingersView = NodeMapView.LingerLines(viewRun.State);
            var recordLines = NodeMapView.RecordLines(viewRun.Steps);
            Check(lines, lingersView.Count == 1 && recordLines.Count == viewRun.Steps.Count
                       && recordLines[0].Contains("幕1")
                       && NodeMapView.StatusLine(viewRun.State, viewRun.Outcome).Contains("第 1 幕"),
                  $"⑯ 视图数据·余气/记录/状态（{recordLines.Count} 步）：{lingersView[0]}｜{recordLines[0]}");

            // ---- ⑰ 灰盒窗口类型可用（不做弹窗副作用：自检不该改编辑器 UI 状态；
            //      真正打开由人工/MCP 走菜单「万相/节气/节点图（灰盒）」） ----
            var windowType = System.Type.GetType("WanXiang.Editor.CampaignTool.NodeMapWindow, WanXiang.Editor");
            Check(lines, windowType != null && typeof(EditorWindow).IsAssignableFrom(windowType),
                  "⑰ 节点图窗口类型可用（人工判据入口：万相/节气/节点图（灰盒））");

            // ---- ⑱ 天阙（v1.1 §5.6 #17）：后土 + 玩家队伍前 3 只的镜像 ----
            var finaleSquad = content.FinaleFor(strong, 20260914UL);
            bool hasBoss = false;
            int mirrors = 0;
            foreach (var e in finaleSquad)
            {
                if (e.Def == null) continue;
                if (e.Def.DisplayName == acts[4].BossName && e.PosIndex == 4) hasBoss = true;   // 后土站中宫
                else
                {
                    for (int i = 0; i < 3; i++)
                        if (ReferenceEquals(e.Def, strong[i].Def)) { mirrors++; break; }
                }
            }
            Check(lines, finaleSquad.Length == 4 && hasBoss && mirrors == 3,
                  $"⑱ 天阙阵容：后土（中宫）+ 玩家前 3 只的镜像（实得 {finaleSquad.Length} 只，镜像 {mirrors}）");

            // ---- ⑲ 局内灵卵（v1.1 §4.3：遭遇 +1 / 精英 +2 / 孵穴 +2 / 异闻拒绝 +1） ----
            Check(lines, runA.RunEggs > 0,
                  $"⑲ 局内灵卵：这一局攒了 {runA.RunEggs} 枚（遭遇/精英/孵穴/异闻的产出口径）");

            lines.Add("========================================================================");
            lines.Add($"结论：{_pass} 项通过，{_fail} 项失败");
            if (_fail > 0)
                foreach (var f in Failures) lines.Add("  ❌ " + f);
            else
                lines.Add("说明：分叉拓扑是 v1 占位（相邻层全连通），试玩调手感时改 ActGraph.Layers 即可，"
                          + "枚举/推进/余气/编排四套判据不随拓扑变化。余气来源取上一幕首节点（GDD 歧义，待策划确认）；"
                          + "每场战斗独立（血量不跨场继承）、平局按败北处理（均为 GDD 未规定处的取舍）。");

            try
            {
                string dir = Path.GetDirectoryName(ReportPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ReportPath, string.Join("\n", lines), new UTF8Encoding(false));
            }
            catch (IOException) { /* 报告写不出去不影响判定 */ }

            return lines.ToArray();
        }

        private static uint Run1v1Fingerprint(WeatherDef weather)
        {
            var st = BattleFactory.Create(BattleConfig.Default, 20260914UL,
                new[] { DeployEntry.Player(BattleSampleContent.Make("cA", "甲", Element.Wood, RoleType.Support), 0) },
                new[] { DeployEntry.Enemy(BattleSampleContent.Make("cB", "乙", Element.Fire, RoleType.Striker), 8) },
                weather);
            return BattleSimulator.Run(st).Fingerprint;
        }

        private static void Check(List<string> lines, bool ok, string what)
        {
            if (ok) _pass++;
            else { _fail++; Failures.Add(what); }
            lines.Add($"{(ok ? "✅" : "❌")} {what}");
        }
    }
}
