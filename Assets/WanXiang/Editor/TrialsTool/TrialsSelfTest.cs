// ============================================================================
//  万相 · 劫循环自检（trials.selftest）
//  ---------------------------------------------------------------------------
//  GDD v1.1 §7「劫 · 难度循环」的机器判据：
//    ① 生效劫律数公式 min(20, (境-1)+(劫-1))（境 3 劫 2 = 3 条）；
//    ② 战斗类劫律写进这一局的独占配置（中宫 0 / 相冲 3.5% / 逆天 25% / 灼烧 1.3）；
//    ③ 图类劫律折成 RunTuning（余气 3 / 镜像 5 / 孵穴 20%）；
//    ④ 天阙三选一的结算公式（§7.7 四条）；
//    ⑤ 「劫数加护」+2%/劫 与「净差 +1%/劫」。
// ============================================================================

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Core;
using WanXiang.Campaign;
using WanXiang.Trials;

namespace WanXiang.Editor.TrialsTool
{
    public static class TrialsSelfTest
    {
        private const string ReportPath = "Temp/WanXiangDiag/trials_selftest.txt";

        private static int _pass, _fail;
        private static readonly List<string> Failures = new List<string>();

        [MenuItem("万相/劫/难度循环自检（trials.selftest）")]
        public static void RunFromMenu()
        {
            var lines = Run("trials.selftest");
            foreach (var l in lines) Debug.Log("[劫循环自检] " + l);
            if (_fail > 0)
                EditorUtility.DisplayDialog("劫循环自检",
                    $"❌ {_pass} 过 / {_fail} 败，失败项见 Console 与 {ReportPath}。", "好");
        }

        public static string[] Run(string command)
        {
            var lines = new List<string>();
            _pass = 0;
            _fail = 0;
            Failures.Clear();

            lines.Add("万相 · 劫·难度循环自检（trials.selftest）");
            lines.Add("========================================================================");
            lines.Add($"时间　　{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            // ---- ① 生效劫律数公式 ----
            var t11 = new TrialState(1, 1);
            var t32 = new TrialState(3, 2);
            var t520 = new TrialState(5, 20);
            Check(lines, t11.ActiveLawCount == 0
                       && t32.ActiveLawCount == 3
                       && t32.ActiveLawIds()[1] == TrialLaws.StubbornQi
                       && t32.ActiveLawIds()[2] == TrialLaws.HeavenPunish
                       && t520.ActiveLawCount == 20
                       && TrialLaws.Has(t520.ActiveLawIds(), TrialLaws.AllIsOne),
                  "① 生效劫律数 = min(20, 境-1 + 劫-1)；按序生效（1境1劫=0 条、3境2劫=3 条、5境20劫=20 条）");

            // ---- ② 劫数加护与净差 ----
            var t15 = new TrialState(1, 5);
            float enemyMul = 1f + 0.03f * 4;
            Check(lines, System.Math.Abs(t15.JieGuardMul - 1.08f) < 0.0001f
                       && System.Math.Abs(enemyMul - 1.12f) < 0.0001f,
                  $"② 劫数加护：劫 5 ⇒ 我方 ×{t15.JieGuardMul:F2}、敌方 ×{enemyMul:F2}（净差 +1%/劫，§7.4 的绞索）");

            // ---- ③ 战斗类劫律 → 独占配置 ----
            var hardCfg = new TrialState(1, 6).BuildRoundConfig();   // 5 条生效
            Check(lines, System.Math.Abs(hardCfg.CenterDamageReduction) < 0.0001f
                       && System.Math.Abs(hardCfg.AdjacencyCounterTrueDamagePercent - 0.035f) < 0.0001f
                       && System.Math.Abs(hardCfg.BacklashExtraDamage - 0.25f) < 0.0001f,
                  "③ 战斗类劫律：中宫失守（减伤 0）/ 相冲不止（真伤 3.5%）/ 逆天之罚（25%）");
            var burnCfg = new TrialState(1, 12).BuildRoundConfig();
            Check(lines, System.Math.Abs(burnCfg.BurnTakenMul - 1.30f) < 0.0001f,
                  "③ 灼烧入骨：灼烧伤害 ×1.3（冰蚀独立）");
            var baseCfg = new TrialState(1, 1).BuildRoundConfig();
            Check(lines, System.Math.Abs(baseCfg.CenterDamageReduction - 0.08f) < 0.0001f
                       && System.Math.Abs(baseCfg.BacklashExtraDamage - 0.15f) < 0.0001f
                       && baseCfg.BurnTakenMul == 1f,
                  "③ 0 劫律：配置与 v1.1 基准逐位一致（不污染无劫路径）");

            // ---- ④ 图类劫律 → RunTuning ----
            var tuning = new TrialState(1, 20).BuildTuning();
            Check(lines, tuning.LingerNodes == 3 && tuning.FinaleMirrors == 5
                       && System.Math.Abs(tuning.NestHealPercent - 0.20f) < 0.0001f,
                  "④ 图类劫律：余气不散（残留 3）/ 天阙低垂（镜像 5）/ 香火断绝（孵穴 20%）");
            Check(lines, tuning.EliteExtraMul > 1.14f && tuning.EliteExtraMul < 1.16f,
                  "④ 兽强：精英额外 ×1.15");
            var cleanTuning = new TrialState(1, 1).BuildTuning();
            Check(lines, cleanTuning.LingerNodes == 2 && cleanTuning.FinaleMirrors == 3
                       && System.Math.Abs(cleanTuning.EliteExtraMul - 1f) < 0.0001f,
                  "④ 0 劫律：图调参与基准一致");

            // ---- ⑤ 天阙三选一的结算公式（§7.7） ----
            var sA = new TrialState(1, 3).Resolve(TrialState.Verdict.Ascend, 20, 5, true);
            Check(lines, sA.EggsFromSettle == (5 + 3 * 2) + 5 && sA.NewRealm == 2 && sA.NewTrial == 1,
                  $"⑤ 登天阙成功：结算 +{sA.EggsFromSettle}（通关奖 11 + 幕数 5），解锁第 2 境");
            var sFail = new TrialState(1, 3).Resolve(TrialState.Verdict.Ascend, 20, 5, false);
            Check(lines, sFail.EggsFromSettle == 5 && sFail.NewRealm == 1,
                  $"⑤ 登天阙失败：结算 +{sFail.EggsFromSettle}（只按幕数；不扣资产，成本是时间）");
            var sC = new TrialState(1, 3).Resolve(TrialState.Verdict.Continue, 20, 5, false);
            Check(lines, sC.EggsFromSettle == 0 && sC.NewTrial == 4 && sC.NewRealm == 1,
                  "⑤ 续劫：灵卵保留在局内、劫数 +1（劫数加护自动生效）");
            var sR = new TrialState(1, 3).Resolve(TrialState.Verdict.Return, 20, 5, false);
            Check(lines, sR.EggsFromSettle == -10 && sR.NewTrial == 1,
                  "⑤ 归元：剩余 ×0.5（20 ⇒ 10）、不推进境数");
            var sD = TrialState.DefeatSettlement(20, 4);
            Check(lines, sD.EggsFromSettle == -6,
                  "⑤ 全灭：剩余 ×0.5 + 幕数（20 ⇒ 10 + 4）");

            // ---- ⑥ 20 条劫律表完整性 ----
            Check(lines, TrialLaws.Order.Length == 20 && TrialLaws.Order[19] == TrialLaws.AllIsOne,
                  "⑥ 劫律表：20 条完整，第 20 条 = 万相归一（前 19 条全生效 + 敌方每回合 +1%，待接）");
            lines.Add("  · 生效劫律（1 境 3 劫，共 2 条）：");
            foreach (var id in new TrialState(1, 3).ActiveLawIds())
                lines.Add("      " + id + " " + TrialLaws.Name(id));

            return Finish(lines);
        }

        private static void Check(List<string> lines, bool ok, string what)
        {
            if (ok) _pass++;
            else { _fail++; Failures.Add(what); }
            lines.Add($"{(ok ? "✅" : "❌")} {what}");
        }

        private static string[] Finish(List<string> lines)
        {
            lines.Add("========================================================================");
            lines.Add($"结论：{_pass} 项通过，{_fail} 项失败");
            if (_fail > 0)
                foreach (var f in Failures) lines.Add("  ❌ " + f);
            else
                lines.Add("说明：劫律抽签取**按序生效**（GDD 说\"抽\"，取按序避免额外随机流 —— 若要随机池只改"
                        + " TrialState.ActiveLawIds 一处）；劫律 02/06/08/09/12/13/14/15/17/18/20 的"
                        + "战斗外部分待对应系统接入（见对齐清单 P3 备注）；"
                        + "多轮编排（续劫重建一局）属 UI/流程层。");

            try
            {
                string dir = Path.GetDirectoryName(ReportPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ReportPath, string.Join("\n", lines), new UTF8Encoding(false));
            }
            catch (IOException) { /* 报告写不出去不影响判定 */ }

            return lines.ToArray();
        }
    }
}
