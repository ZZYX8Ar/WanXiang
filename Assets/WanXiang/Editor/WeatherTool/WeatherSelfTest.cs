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

            // ---- ③ 夏至（JSON 索引 10，非 22！）：全场伤害 ×1.25。⚠ 不能拿整场总伤害比 ——
            //     伤害变高会提前打死人，战斗动态整个变掉，总比值无意义。对 ComputeDamage
            //     （public static）做受控单笔对照，断言精确 ×1.25（取整误差 ≤1）。
            var xiazhi = WeatherCatalog.GetSolarTerm(10);
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

            // ---- ⑥ 余气：原子减半、禁疗不继承、乘数向 1 收敛一半（GDD 3.2 规则一） ----
            var half = xiaoxue.ScaledHalf("half_xiaoxue");
            Check(lines, half.BanHeal == false && half.BuffName.Contains("余气"),
                  "⑥ 余气：禁疗不继承、名称带「余气」标记");
            var halfQingyu = daoyu.Brings.ScaledHalf("half_rain");
            float orig = 0.02f, scaled = halfQingyu.TurnEnd[0].Atom.PercentOfMaxHp;
            Check(lines, System.Math.Abs(scaled - orig * 0.5f) < 0.0001f,
                  $"⑥ 余气：原子数值减半（回复 {orig:0.00} ⇒ {scaled:0.000}）");
            var halfXiazhi = xiazhi.ScaledHalf("half_xiazhi");
            Check(lines, System.Math.Abs(halfXiazhi.DamageAllMultiplier - 1.125f) < 0.0001f,
                  $"⑥ 余气：乘数向 1 收敛一半（×1.25 ⇒ ×{halfXiazhi.DamageAllMultiplier:0.000}）");

            // ==================================================================
            //  规则修正型通路（本批新接）：每个结算点一条受控对照
            // ==================================================================

            // ---- ⑦ 大暑：五行伤害乘数（火 ×0.7、土 ×1.3；其它属性不变） ----
            //     ⚠ 基线按**同元素**取：不同元素对同一目标的五行系数不同，不能共用一个基数。
            var dashu = WeatherCatalog.GetSolarTerm(12);
            var sDashu = Make1v1("wP", "平", Element.Metal, RoleType.Striker,
                                 "wQ", "和", Element.Wood, RoleType.Guard, dashu);
            var pD = sDashu.UnitsOf(TeamSide.Player)[0];
            var eD = sDashu.UnitsOf(TeamSide.Enemy)[0];
            int dFireBase = BattleSimulator.ComputeDamage(sPlain, pD, eD, Element.Fire, 1f, false, false);
            int dEarthBase = BattleSimulator.ComputeDamage(sPlain, pD, eD, Element.Earth, 1f, false, false);
            int dFire = BattleSimulator.ComputeDamage(sDashu, pD, eD, Element.Fire, 1f, false, false);
            int dEarth = BattleSimulator.ComputeDamage(sDashu, pD, eD, Element.Earth, 1f, false, false);
            Check(lines, System.Math.Abs(dFire - dFireBase * 0.7) <= 1 && System.Math.Abs(dEarth - dEarthBase * 1.3) <= 1,
                  $"⑦ 大暑五行乘数：火 {dFireBase}⇒{dFire}（×0.7）、土 {dEarthBase}⇒{dEarth}（×1.3）");

            // ---- ⑧ 立秋：我方暴伤 +40%（与单位自身暴伤相加后再乘） ----
            var liqiu = WeatherCatalog.GetSolarTerm(13);
            var sLiqiu = Make1v1("wP", "平", Element.Metal, RoleType.Striker,
                                 "wQ", "和", Element.Wood, RoleType.Guard, liqiu);
            float cd = sLiqiu.UnitsOf(TeamSide.Player)[0].Def.CritDamage;
            int dCritPlain = BattleSimulator.ComputeDamage(sPlain,
                sPlain.UnitsOf(TeamSide.Player)[0], sPlain.UnitsOf(TeamSide.Enemy)[0],
                Element.Fire, 1f, false, crit: true);
            int dCritLiqiu = BattleSimulator.ComputeDamage(sLiqiu,
                sLiqiu.UnitsOf(TeamSide.Player)[0], sLiqiu.UnitsOf(TeamSide.Enemy)[0],
                Element.Fire, 1f, false, crit: true);
            float expectCrit = dCritPlain * (1f + cd + 0.4f) / (1f + cd);
            Check(lines, System.Math.Abs(dCritLiqiu - expectCrit) <= 1,
                  $"⑧ 立秋暴伤加成：{dCritPlain} ⇒ {dCritLiqiu}（自身暴伤 {cd:P0} + 40%）");

            // ---- ⑨ 秋分：AOE ×0.6 / 单体 ×1.25（按原子目标形状） ----
            var qiufen = WeatherCatalog.GetSolarTerm(16);
            var sQiufen = Make1v1("wP", "平", Element.Metal, RoleType.Striker,
                                  "wQ", "和", Element.Wood, RoleType.Guard, qiufen);
            var pQ = sQiufen.UnitsOf(TeamSide.Player)[0];
            var eQ = sQiufen.UnitsOf(TeamSide.Enemy)[0];
            int dAoe = BattleSimulator.ComputeDamage(sQiufen, pQ, eQ, Element.Fire, 1f, false, false, aoeSkill: true);
            int dSingle = BattleSimulator.ComputeDamage(sQiufen, pQ, eQ, Element.Fire, 1f, false, false, aoeSkill: false);
            Check(lines, System.Math.Abs(dAoe - dPlain * 0.6) <= 1 && System.Math.Abs(dSingle - dPlain * 1.25) <= 1,
                  $"⑨ 秋分形态乘数：AOE ⇒{dAoe}（×0.6）、单体 ⇒{dSingle}（×1.25）");

            // ---- ⑩ 速度与先手：单方乘数改排序；大雪全场同乘不改序；冬至先手方判定 ----
            var enemyFirst = Make1v1("wP", "缓", Element.Metal, RoleType.Guard,
                                     "wQ", "疾", Element.Wood, RoleType.Swift, null);
            var firstPlain = enemyFirst.BuildActionOrder()[0].Side;
            var playerBoost = Make1v1("wP", "缓", Element.Metal, RoleType.Guard,
                                      "wQ", "疾", Element.Wood, RoleType.Swift,
                                      new WeatherDef { Id = "t_speed", NodeName = "测", BuffName = "测",
                                          SpeedMulPlayer = 2f });
            var firstBoost = playerBoost.BuildActionOrder()[0].Side;
            Check(lines, firstPlain == TeamSide.Enemy && firstBoost == TeamSide.Player,
                  "⑩ 单方速度乘数：敌方先手 ⇒ 我方提速后反先（排序真被改了）");
            var daxue = WeatherCatalog.GetSolarTerm(21);
            var sDaxue = Make1v1("wP", "缓", Element.Metal, RoleType.Guard,
                                 "wQ", "疾", Element.Wood, RoleType.Swift, daxue);
            Check(lines, sDaxue.BuildActionOrder()[0].Side == firstPlain,
                  "⑩ 大雪全场同乘 ×0.8：出手序列不变（全场同乘不改序）");

            // ---- ⑩ 冬至首回合先手方伤害：同一攻击者在有/无乘数两个天时下对照 ----
            //     （先手打后手 vs 后手打先手基数不同——双方面板不同，不能拿比值断言）
            var dongzhi = WeatherCatalog.GetSolarTerm(22);
            var dzBase = new WeatherDef { Id = "t_dz0", NodeName = "测", BuffName = "测",
                SpeedMulPlayer = 1.2f, SpeedMulEnemy = 1.2f };
            var sA = Make1v1("wP", "缓", Element.Metal, RoleType.Guard,
                             "wQ", "疾", Element.Wood, RoleType.Swift, dzBase);
            var sB = Make1v1("wP", "缓", Element.Metal, RoleType.Guard,
                             "wQ", "疾", Element.Wood, RoleType.Swift, dongzhi);
            sA.BuildActionOrder();
            sB.BuildActionOrder();
            var moverA = sA.UnitsOf(sA.FirstMoverSide)[0];
            var backA = sA.UnitsOf(BattleState.Opponent(sA.FirstMoverSide))[0];
            var moverB = sB.UnitsOf(sB.FirstMoverSide)[0];
            var backB = sB.UnitsOf(BattleState.Opponent(sB.FirstMoverSide))[0];
            int dNoBonus = BattleSimulator.ComputeDamage(sA, moverA, backA, Element.Fire, 1f, false, false);
            int dWithBonus = BattleSimulator.ComputeDamage(sB, moverB, backB, Element.Fire, 1f, false, false);
            int dBackNo = BattleSimulator.ComputeDamage(sA, backA, moverA, Element.Fire, 1f, false, false);
            int dBackWith = BattleSimulator.ComputeDamage(sB, backB, moverB, Element.Fire, 1f, false, false);
            Check(lines, System.Math.Abs(dWithBonus - dNoBonus * 1.5) <= 1 && dBackWith == dBackNo,
                  $"⑩ 冬至首回合先手方伤害：先手 {dNoBonus}⇒{dWithBonus}（×1.5），后手 {dBackNo}⇒{dBackWith}（不变）");

            // ---- ⑪ 小满：CD 推进 +30%（只数受影响的我方战技次数；敌方会被战局扰动） ----
            var xiaoman = WeatherCatalog.GetSolarTerm(8);
            var cPlain = Run1v1Full(
                BattleSampleContent.Make("wX", "攻", Element.Metal, RoleType.Striker),
                BattleSampleContent.Make("wY", "御", Element.Earth, RoleType.Guard), 11, null);
            var cAccel = Run1v1Full(
                BattleSampleContent.Make("wX", "攻", Element.Metal, RoleType.Striker),
                BattleSampleContent.Make("wY", "御", Element.Earth, RoleType.Guard), 11, xiaoman);
            int castsPlain = CountSkillKindBySide(cPlain.Log, SkillType.Active, 'P');
            int castsAccel = CountSkillKindBySide(cAccel.Log, SkillType.Active, 'P');
            Check(lines, castsAccel > castsPlain,
                  $"⑪ 小满 CD 推进：我方战技 对照 {castsPlain} 次 ⇒ 加速 {castsAccel} 次（同种子）");

            // ---- ⑫ 雨水：治疗溢出 50% 转护盾（受控：先打掉 1% 血，再手动推一次回合末天时） ----
            //     ⚠ 双方都会结算：敌方满血全额溢出，必须按目标 id 过滤，别把敌方的盾当成玩家的。
            var yushui = WeatherCatalog.GetSolarTerm(2);
            var sRain = Make1v1("wP", "雨", Element.Metal, RoleType.Guard,
                                "wQ", "和", Element.Wood, RoleType.Guard, yushui);
            var pR = sRain.UnitsOf(TeamSide.Player)[0];
            int chip = CoreMath.Max(1, pR.MaxHp / 100);          // 打掉 1%（< 3% 回复 ⇒ 有溢出）
            pR.TakeTrueDamage(chip);
            WeatherResolver.ResolveTurnEnd(sRain);
            int overflowExp = CoreMath.RoundDamage(pR.MaxHp * 0.03f) - chip;
            int shieldExp = CoreMath.RoundDamage(overflowExp * 0.5f);
            int gotHeal = 0, gotShield = 0;
            foreach (var e in sRain.Log.Events)
            {
                if (e.TargetId != pR.RuntimeId) continue;
                if (e.Kind == BattleEventKind.Heal && e.Note != null && e.Note.Contains("天时·润物无声")) gotHeal = e.Amount;
                if (e.Kind == BattleEventKind.Shield && e.Note != null && e.Note.Contains("溢出转化")) gotShield = e.Amount;
            }
            Check(lines, gotHeal == chip && gotShield == shieldExp,
                  $"⑫ 雨水溢出转盾：回复 {gotHeal}（=缺口 {chip}）、护盾 {gotShield}" +
                  $"（=溢出 {overflowExp} × 50%，期望 {shieldExp}）");

            // ---- ⑬ 小雪：护盾获取 ×1.5（走溢出转盾链路验证乘数；全血 ⇒ 全额溢出） ----
            var sSnowShield = Make1v1("wP", "雪", Element.Metal, RoleType.Guard,
                                      "wQ", "和", Element.Wood, RoleType.Guard,
                                      new WeatherDef { Id = "t_shield", NodeName = "测", BuffName = "测雪",
                                          TurnEnd = new[] { new WeatherEffect(WeatherScope.PlayerSide,
                                              EffectAtom.Heal(TargetSelector.AllAllies, 0f, 0.03f)) },
                                          HealOverflowShieldRatio = 1f, ShieldGainMul = 1.5f });
            WeatherResolver.ResolveTurnEnd(sSnowShield);
            int fullOverflow = CoreMath.RoundDamage(sSnowShield.UnitsOf(TeamSide.Player)[0].MaxHp * 0.03f);
            int shieldGainExp = CoreMath.RoundDamage(fullOverflow * 1.5f);
            int gotGain = 0;
            foreach (var e in sSnowShield.Log.Events)
                if (e.Kind == BattleEventKind.Shield && e.TargetId == sSnowShield.UnitsOf(TeamSide.Player)[0].RuntimeId)
                    gotGain = e.Amount;
            Check(lines, gotGain == shieldGainExp,
                  $"⑬ 护盾获取乘数：溢出 {fullOverflow} × 1.5 ⇒ 护盾 {gotGain}（期望 {shieldGainExp}）");

            // ---- ⑭ 春分：MinTurn —— 第 13 回合起回复翻倍（手动推两个回合，不受战局扰动） ----
            var chunfen = WeatherCatalog.GetSolarTerm(4);
            var sChun = Make1v1("wU", "守", Element.Water, RoleType.Guard,
                                "wV", "袭", Element.Earth, RoleType.Guard, chunfen);
            var uA = sChun.UnitsOf(TeamSide.Player)[0];
            var uB = sChun.UnitsOf(TeamSide.Enemy)[0];
            sChun.Turn = 5;
            uA.TakeTrueDamage(uA.MaxHp / 5);
            uB.TakeTrueDamage(uB.MaxHp / 5);
            WeatherResolver.ResolveTurnEnd(sChun);
            int healEarly = CountNoteTurn(sChun.Log, "天时·昼夜均分", 5);
            sChun.Turn = 13;
            uA.TakeTrueDamage(uA.MaxHp / 5);
            uB.TakeTrueDamage(uB.MaxHp / 5);
            WeatherResolver.ResolveTurnEnd(sChun);
            int healLate = CountNoteTurn(sChun.Log, "天时·昼夜均分", 13);
            Check(lines, healEarly == 2 && healLate == 4,
                  $"⑭ 春分 MinTurn：第 5 回合回复 {healEarly} 笔（2 单位 ×1），第 13 回合 {healLate} 笔（×2）");

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

        /// <summary>装配一个 1v1 战场（不跑），供受控单笔对照用。</summary>
        private static BattleState Make1v1(string idA, string nameA, Element elA, RoleType roleA,
                                           string idB, string nameB, Element elB, RoleType roleB,
                                           WeatherDef weather)
        {
            return BattleFactory.Create(BattleConfig.Default, 20260914UL,
                new[] { DeployEntry.Player(BattleSampleContent.Make(idA, nameA, elA, roleA), 0) },
                new[] { DeployEntry.Enemy(BattleSampleContent.Make(idB, nameB, elB, roleB), 8) }, weather);
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

        /// <summary>数某一回合内含指定片段的事件数（MinTurn / 每回合笔数断言用）。</summary>
        private static int CountNoteTurn(BattleLog log, string notePart, int turn)
        {
            int n = 0;
            foreach (var e in log.Events)
                if (e.Turn == turn && e.Note != null && e.Note.Contains(notePart)) n++;
            return n;
        }

        /// <summary>数某技能槽的施放次数（小满 CD 推进断言用）。</summary>
        private static int CountSkillKind(BattleLog log, SkillType skill)
        {
            int n = 0;
            foreach (var e in log.Events)
                if (e.Kind == BattleEventKind.SkillCast && e.Skill == skill) n++;
            return n;
        }

        /// <summary>按阵营前缀（P/E）数某技能槽的施放次数。
        /// CD 推进只作用一方 —— 敌方次数会被战局扰动，双方加总会稀释掉机制差异。</summary>
        private static int CountSkillKindBySide(BattleLog log, SkillType skill, char sidePrefix)
        {
            int n = 0;
            foreach (var e in log.Events)
                if (e.Kind == BattleEventKind.SkillCast && e.Skill == skill
                    && e.ActorId != null && e.ActorId.StartsWith(sidePrefix)) n++;
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
                lines.Add("说明：天时内容已翻 16 条节气 + 4 条天气技（WeatherCatalog 文件头有清单）；"
                          + "剩 8 条事件钩子型（复活卵/追击/反弹/额外普攻等）待逐条建钩子接入。"
                          + "速度/CD/暴伤/AOE/五行伤害/护盾/溢出转盾/首回合先手八条规则修正已通路，"
                          + "每条都有受控对照（⑦–⑭），指纹回归 R1/R2 在最前面。");

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
