// ============================================================================
//  万相 · 天时自检（weather.selftest）
//  ---------------------------------------------------------------------------
//  STEP 3 天时系统的机器判据。核心思路是**对照实验**：
//  同种子同阵容，只差"有没有天时/天时是什么"，差异必须可观察且方向正确。
//
//  ⭐ 第 1 项是全系统最重要的回归保护：基准局指纹必须仍是 0x265422D8 ——
//  天时层挂在战斗主循环上，若它改动了无天时战斗的任何一位，这里先红。
// ============================================================================

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Core;
using WanXiang.Editor.BattleTool;

namespace WanXiang.Editor.WeatherTool
{
    public static class WeatherSelfTest
    {
        private const string ReportPath = "Temp/WanXiangDiag/weather_selftest.txt";
        private const uint BaselineFingerprint = 0x265422D8u;

        private static int _pass, _fail;
        private static readonly List<string> Failures = new List<string>();

        [MenuItem("万相/融合/④ 天时自检（weather.selftest）")]
        public static void RunFromMenu()
        {
            var lines = Run("weather.selftest");
            foreach (var l in lines) Debug.Log("[天时自检] " + l);
            // ⚠ 只在失败时弹模态框：全绿也弹会把编辑器主线程卡在对话框上，
            // 自动化（诊断桥/MCP）跑这条命令时后续命令会全部超时。
            if (_fail > 0)
                EditorUtility.DisplayDialog("天时自检",
                    $"❌ {_pass} 过 / {_fail} 败，失败项见 Console 与 {ReportPath}。", "好");
        }

        public static string[] Run(string command)
        {
            var lines = new List<string>();
            _pass = 0;
            _fail = 0;
            Failures.Clear();

            lines.Add("万相 · 天时系统自检（weather.selftest）");
            lines.Add("========================================================================");
            lines.Add($"时间　　{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            // ---- R1 基准局指纹回归（最重要的保护项） ----
            var baseline = BattleSimulator.Run(
                BattleSampleContent.BuildScenario(2, 20260914UL, BattleConfig.Default));
            Check(lines, baseline.Fingerprint == BaselineFingerprint,
                  $"R1 基准局指纹回归：0x{baseline.Fingerprint:X8}（期望 0x{BaselineFingerprint:X8}）" +
                  "—— 天时层没有污染无天时战斗");

            // ---- R2 空天时指纹零影响（1v1 对照） ----
            var host = BattleSampleContent.Make("wA", "甲", Element.Wood, RoleType.Striker);
            var enemy = BattleSampleContent.Make("wB", "乙", Element.Fire, RoleType.Striker);
            var emptyWeather = new WeatherDef { Id = "empty", NodeName = "空", BuffName = "空" };

            var fpNone = Run1v1(host, enemy, 7, null);
            var fpEmpty = Run1v1(host, enemy, 7, emptyWeather);
            Check(lines, fpNone == fpEmpty,
                  $"R2 空天时指纹零影响：无天时 0x{fpNone:X8} == 空天时 0x{fpEmpty:X8}");

            // ---- ① 立春：我方开局 2 层生机，战斗里能看到生机回复 ----
            var lichun = WeatherCatalog.GetSolarTerm(1);
            var lp = Run1v1Full(BattleSampleContent.Make("wL", "生", Element.Wood, RoleType.Support),
                                BattleSampleContent.Make("wM", "灭", Element.Fire, RoleType.Striker),
                                9, lichun);
            Check(lines, CountNote(lp.Log, "天时·东风解冻") > 0 && CountKind(lp.Log, BattleEventKind.Heal) > 0,
                  "① 立春：生机被施加且回复真实发生（事件含「天时·东风解冻」）");
            var lp0 = Run1v1Full(BattleSampleContent.Make("wL", "生", Element.Wood, RoleType.Support),
                                 BattleSampleContent.Make("wM", "灭", Element.Fire, RoleType.Striker),
                                 9, null);
            Check(lines, CountNote(lp0.Log, "天时·东风解冻") == 0,
                  "① 对照：无天时局 0 次「天时·东风解冻」事件");

            // ---- ② 小雪：禁疗 —— 有治疗技能的阵容也 0 次治疗事件 ----
            var healer = BattleSampleContent.Make("wH", "医", Element.Wood, RoleType.Support);
            var beast = BattleSampleContent.Make("wT", "兽", Element.Earth, RoleType.Striker);
            var xiaoxue = WeatherCatalog.GetSolarTerm(20);
            int healsNormal = CountKind(Run1v1Full(healer, beast, 5, null).Log, BattleEventKind.Heal);
            int healsBanned = CountKind(Run1v1Full(healer, beast, 5, xiaoxue).Log, BattleEventKind.Heal);
            Check(lines, healsNormal > 0 && healsBanned == 0,
                  $"② 小雪禁疗：对照局 {healsNormal} 次治疗，禁疗局 {healsBanned} 次");

            // ---- ③ 夏至：全场伤害 ×1.25。⚠ 不能拿整场总伤害比 —— 伤害变高会
            //     提前打死人，战斗动态整个变掉，总比值无意义。对 ComputeDamage
            //     （public static）做受控单笔对照，断言精确 ×1.25（取整误差 ≤1）。
            var xiazhi = WeatherCatalog.GetSolarTerm(22);
            var sPlain = BattleFactory.Create(BattleConfig.Default, 20260914UL,
                new[] { DeployEntry.Player(BattleSampleContent.Make("wP", "平", Element.Metal, RoleType.Striker), 0) },
                new[] { DeployEntry.Enemy(BattleSampleContent.Make("wQ", "和", Element.Wood, RoleType.Guard), 8) }, null);
            var sXiazhi = BattleFactory.Create(BattleConfig.Default, 20260914UL,
                new[] { DeployEntry.Player(BattleSampleContent.Make("wP", "平", Element.Metal, RoleType.Striker), 0) },
                new[] { DeployEntry.Enemy(BattleSampleContent.Make("wQ", "和", Element.Wood, RoleType.Guard), 8) }, xiazhi);
            int dPlain = BattleSimulator.ComputeDamage(sPlain,
                sPlain.UnitsOf(TeamSide.Player)[0], sPlain.UnitsOf(TeamSide.Enemy)[0],
                Element.Fire, 1.0f, false, false);
            int dHot = BattleSimulator.ComputeDamage(sXiazhi,
                sXiazhi.UnitsOf(TeamSide.Player)[0], sXiazhi.UnitsOf(TeamSide.Enemy)[0],
                Element.Fire, 1.0f, false, false);
            Check(lines, System.Math.Abs(dHot - dPlain * 1.25) <= 1,
                  $"③ 夏至单笔伤害对照：{dPlain} ⇒ {dHot}（精确 ×1.25，取整误差 ≤1）");

            // ---- ④ 天气技覆盖：大寒（水）用祈晴（火），3 回合后回归原天时 ----
            //     Guard 对 Guard：高防低攻拖满回合，保证能观察到"回归"发生在第 4 回合。
            var dahan = WeatherCatalog.GetSolarTerm(24);
            var qiqing = WeatherCatalog.GetWeatherSkill("wskill_qiqing");
            var covered = Run1v1Full(
                BattleSampleContent.Make("wS", "守", Element.Water, RoleType.Guard),
                BattleSampleContent.Make("wE", "袭", Element.Earth, RoleType.Guard),
                13, dahan, qiqing);
            int sunnyEarly = CountNoteInTurns(covered.Log, "天时·祈晴", 1, 3);
            int dahanLate = CountNoteFromTurn(covered.Log, "天时·寒气之逆极", 4);
            Check(lines, sunnyEarly > 0 && dahanLate > 0,
                  $"④ 覆盖 3 回合后回归：祈晴在前 3 回合生效（{sunnyEarly} 条），" +
                  $"第 4 回合起寒气之逆极回归（{dahanLate} 条）");

            // ---- ⑤ 逆天时：水节气（大寒）用祈晴（火）⇒ 反噬；用祷雨（水）⇒ 无反噬 ----
            //     在覆盖生效窗口内判定（装配后立即读，不等战斗跑完）。
            var wFire = BuildOnly(
                BattleSampleContent.Make("wS", "守", Element.Water, RoleType.Guard),
                BattleSampleContent.Make("wE", "袭", Element.Earth, RoleType.Guard),
                13, dahan, qiqing);
            Check(lines, wFire.BacklashActive && wFire.BacklashElement == Element.Fire,
                  "⑤ 逆天时判定：水节气用祈晴 ⇒ 我方火属性单位 +15% 承伤（反噬激活）");
            var daoyu = WeatherCatalog.GetWeatherSkill("wskill_daoyu");
            var wWater = BuildOnly(
                BattleSampleContent.Make("wS", "守", Element.Water, RoleType.Guard),
                BattleSampleContent.Make("wE", "袭", Element.Earth, RoleType.Guard),
                13, dahan, daoyu);
            Check(lines, !wWater.BacklashActive,
                  "⑤ 对照：水节气用祷雨（同属性）⇒ 无逆天时反噬");

            // ---- ⑥ 余气：原子减半、禁疗不继承（GDD 3.2 规则一） ----
            var half = xiaoxue.ScaledHalf("half_xiaoxue");
            Check(lines, half.BanHeal == false && half.BuffName.Contains("余气"),
                  "⑥ 余气：禁疗不继承、名称带「余气」标记");
            var halfQingyu = daoyu.Brings.ScaledHalf("half_rain");
            float orig = 0.02f, scaled = halfQingyu.TurnEnd[0].Atom.PercentOfMaxHp;
            Check(lines, System.Math.Abs(scaled - orig * 0.5f) < 0.0001f,
                  $"⑥ 余气：原子数值减半（回复 {orig:0.00} ⇒ {scaled:0.000}）");

            return Finish(lines);
        }

        // ====================================================================

        private static uint Run1v1(BeastDef host, BeastDef enemy, ulong seed, WeatherDef weather)
        {
            var st = BattleFactory.Create(BattleConfig.Default, seed,
                new[] { DeployEntry.Player(host, 0) },
                new[] { DeployEntry.Enemy(enemy, 8) }, weather);
            return BattleSimulator.Run(st).Fingerprint;
        }

        private static BattleState Run1v1Full(BeastDef host, BeastDef enemy, ulong seed,
                                              WeatherDef weather, WeatherSkillDef useSkill = null)
        {
            var st = BattleFactory.Create(BattleConfig.Default, seed,
                new[] { DeployEntry.Player(host, 0) },
                new[] { DeployEntry.Enemy(enemy, 8) }, weather);
            if (useSkill != null && st.Weather != null) st.Weather.ApplyOverride(useSkill);
            BattleSimulator.Run(st);
            return st;
        }

        /// <summary>只装配不跑 —— 逆天时标记在覆盖生效窗口内读取（战局可能超过覆盖的 3 回合）。</summary>
        private static WeatherRuntime BuildOnly(BeastDef host, BeastDef enemy, ulong seed,
                                                WeatherDef weather, WeatherSkillDef useSkill)
        {
            var st = BattleFactory.Create(BattleConfig.Default, seed,
                new[] { DeployEntry.Player(host, 0) },
                new[] { DeployEntry.Enemy(enemy, 8) }, weather);
            st.Weather.ApplyOverride(useSkill);
            return st.Weather;
        }

        private static int CountKind(BattleLog log, BattleEventKind kind)
        {
            int n = 0;
            foreach (var e in log.Events) if (e.Kind == kind) n++;
            return n;
        }

        private static int CountNote(BattleLog log, string notePart)
        {
            int n = 0;
            foreach (var e in log.Events)
                if (e.Note != null && e.Note.Contains(notePart)) n++;
            return n;
        }

        private static int CountNoteInTurns(BattleLog log, string notePart, int fromTurn, int toTurn)
        {
            int n = 0;
            foreach (var e in log.Events)
                if (e.Turn >= fromTurn && e.Turn <= toTurn && e.Note != null && e.Note.Contains(notePart))
                    n++;
            return n;
        }

        private static int CountNoteFromTurn(BattleLog log, string notePart, int fromTurn)
        {
            int n = 0;
            foreach (var e in log.Events)
                if (e.Turn >= fromTurn && e.Note != null && e.Note.Contains(notePart))
                    n++;
            return n;
        }

        private static string[] Finish(List<string> lines)
        {
            lines.Add("========================================================================");
            lines.Add($"结论：{_pass} 项通过，{_fail} 项失败");
            if (_fail > 0)
                foreach (var f in Failures) lines.Add("  ❌ " + f);
            else
                lines.Add("说明：天时内容本批只翻 7 条节气 + 4 条天气技（WeatherCatalog 文件头有清单），"
                          + "其余 17 条按同一模式补齐；速度/CD/AOE 类规则修正逐条接入时过本自检指纹回归。");

            try
            {
                string dir = Path.GetDirectoryName(ReportPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ReportPath, string.Join("\n", lines), new UTF8Encoding(false));
            }
            catch (IOException) { /* 报告写不出去不影响判定 */ }

            return lines.ToArray();
        }

        private static void Check(List<string> lines, bool ok, string what)
        {
            if (ok) _pass++;
            else { _fail++; Failures.Add(what); }
            lines.Add($"{(ok ? "✅" : "❌")} {what}");
        }
    }
}
