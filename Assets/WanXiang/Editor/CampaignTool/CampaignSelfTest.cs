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
                  $"④ 一局全程：常规 {run.Path.Count} 场 + 守关 {run.BossesDefeated} 场 = 21，通关判定成立");

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
               && w1.BuffName == "麦气充盈"
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

            lines.Add("========================================================================");
            lines.Add($"结论：{_pass} 项通过，{_fail} 项失败");
            if (_fail > 0)
                foreach (var f in Failures) lines.Add("  ❌ " + f);
            else
                lines.Add("说明：分叉拓扑是 v1 占位（相邻层全连通），试玩调手感时改 ActGraph.Layers 即可，"
                          + "枚举/推进/余气三套判据不随拓扑变化。余气来源取上一幕首节点（GDD 歧义，待策划确认）。");

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

// build marker 639250152576349506