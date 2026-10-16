// ============================================================================
//  万相 · 阵形连携自检（combo.selftest）—— 2026-10-16 全替换重构版
//  ---------------------------------------------------------------------------
//  旧版守的是"50 条异兽专属连携"的表结构；重构后表变成 **4 条阵形**，
//  这里守的东西随之变成：
//    ① 表结构（格号合法 / 不重复 / 有名字有效果 / Id 唯一）
//    ② 文案与系数纯函数（CellName / ShapeText / PowerMulFor）
//    ③ **成阵判定**：单位摆到指定格位 → FormationMatched 应为真/假
//    ④ **QTE 发放全链路**：登记 PendingComboId → 3 段窗口 → 全完美 ⇒ 成功
//       （扣 灵力=存活数、扣满份大招能量、标记 UsedCombos）
//    ⑤ **QTE 失败惩罚**：全失 ⇒ 扣 1 灵力 + 50% 大招能量、**不**标记 UsedCombos
//
//  ⚠ 只在失败时弹模态框（理由同其余自检）：全绿也弹会把编辑器主线程卡在对话框上，
//     自动化（诊断桥/MCP）跑这条命令时后续命令会全部超时。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Core;
using WanXiang.Modules.UI;

namespace WanXiang.Editor.BattleTool
{
    public static class ComboSelfTest
    {
        private const string ReportPath = "Temp/WanXiangDiag/combo_selftest.txt";

        private static int _pass;
        private static int _fail;

        [MenuItem("万相/战斗/连携技自检（combo.selftest）")]
        public static void RunFromMenu()
        {
            var lines = Run("combo.selftest");
            foreach (var l in lines) Debug.Log("[连携技自检] " + l);
            if (_fail > 0)
                EditorUtility.DisplayDialog("连携技自检",
                    $"❌ {_pass} 过 / {_fail} 败，失败项见 Console 与 {ReportPath}。", "好");
        }

        public static string[] Run(string command)
        {
            _pass = 0;
            _fail = 0;

            var sb = new StringBuilder(16384);
            sb.AppendLine("万相 · 阵形连携自检（combo.selftest）");
            sb.AppendLine(new string('=', 72));
            sb.AppendLine($"时间　　{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("范围　　ComboDef 表结构 + 成阵判定 + QTE 发放/失败惩罚全链路");
            sb.AppendLine("程序集　WanXiang.Battle.Core（noEngineReferences = true）");
            sb.AppendLine();

            try
            {
                CheckTableShape(sb);
                CheckPureText(sb);
                CheckFormationMatch(sb);
                CheckQteFire(sb);
                CheckQteFail(sb);
            }
            catch (Exception ex)
            {
                Bad(sb, $"自检过程抛出异常：{ex.GetType().Name}：{ex.Message}");
                Note(sb, ex.StackTrace ?? "");
            }

            sb.AppendLine();
            sb.AppendLine(new string('=', 72));
            sb.AppendLine($"结论：{_pass} 项通过，{_fail} 项失败");

            string text = sb.ToString();
            WriteReport(text);
            return text.Replace("\r\n", "\n").Split('\n');
        }

        private static void WriteReport(string text)
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(ReportPath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(ReportPath, text, new UTF8Encoding(false));
            }
            catch { /* 报告写不出去不影响自检结论 */ }
        }

        // ================================================================
        //  ① 表结构
        // ================================================================
        private static void CheckTableShape(StringBuilder sb)
        {
            Note(sb, "---- ① 表结构 ----");
            var all = ComboRules.All;
            if (all == null || all.Count == 0) { Bad(sb, "阵形表为空"); return; }

            var ids = new HashSet<string>();
            int checkedCells = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null) { Bad(sb, $"第 {i} 条为 null"); continue; }
                if (string.IsNullOrEmpty(c.Id)) { Bad(sb, $"第 {i} 条 Id 为空"); continue; }
                if (!ids.Add(c.Id)) { Bad(sb, $"Id 重复：{c.Id}"); continue; }
                if (string.IsNullOrEmpty(c.Name)) Bad(sb, $"{c.Id} 没有名字");
                if (string.IsNullOrEmpty(c.Note)) Bad(sb, $"{c.Id} 没有 Note");

                bool ok = CheckCells(sb, c.Id, "Cells", c.Cells, true);
                if (c.CellsAlt != null && c.CellsAlt.Length > 0)
                    ok &= CheckCells(sb, c.Id, "CellsAlt", c.CellsAlt, false);
                if (ok) checkedCells++;

                if (c.Cells != null && c.Cells.Length > BoardLayout.MaxDeployed)
                    Bad(sb, $"{c.Id} 需要 {c.Cells.Length} 格 > 最多上阵 {BoardLayout.MaxDeployed}");
                if (c.Power <= 0f) Bad(sb, $"{c.Id} Power<=0");
            }
            if (_fail == 0) Pass(sb, $"表结构 OK（{all.Count} 条阵形，{checkedCells} 条格号合法）");
            else Note(sb, $"（{all.Count} 条阵形，{checkedCells} 条格号合法）");
        }

        private static bool CheckCells(StringBuilder sb, string id, string field, int[] cells, bool required)
        {
            if (cells == null || cells.Length == 0)
            {
                if (required) Bad(sb, $"{id}.{field} 为空");
                return !required;
            }
            var seen = new HashSet<int>();
            for (int i = 0; i < cells.Length; i++)
            {
                int v = cells[i];
                if (v < 0 || v >= BoardLayout.CellCount) { Bad(sb, $"{id}.{field}[{i}]={v} 越界"); return false; }
                if (!seen.Add(v)) { Bad(sb, $"{id}.{field} 有重复格 {v}"); return false; }
            }
            return true;
        }

        // ================================================================
        //  ② 纯函数：格名 / 形状文案 / 威力系数
        // ================================================================
        private static void CheckPureText(StringBuilder sb)
        {
            Note(sb, "---- ② 文案与系数 ----");
            Eq(sb, "CellName(4)=中宫", ComboRules.CellName(4), "中宫");
            Eq(sb, "CellName(0)=后上", ComboRules.CellName(0), "后上");
            Eq(sb, "CellName(8)=前下", ComboRules.CellName(8), "前下");

            var c = ComboRules.For("fm_goose");
            if (c != null)
            {
                string shape = ComboRules.ShapeText(c);
                bool ok = shape.Contains("中宫") && shape.Contains("或");
                if (ok) Pass(sb, "ShapeText(雁行阵) 含中宫与备用形状：" + shape);
                else Bad(sb, "ShapeText(雁行阵) 缺内容：" + shape);
            }
            else Bad(sb, "找不到 fm_goose");

            float m5 = ComboRules.PowerMulFor(5);
            float m1 = ComboRules.PowerMulFor(1);
            if (Mathf.Abs(m5 - 1f) < 0.001f && m1 < m5) Pass(sb, $"PowerMulFor 5只={m5:0.00} > 1只={m1:0.00}");
            else Bad(sb, $"PowerMulFor 5只={m5} 1只={m1}（期望 5只=1.00 且大于 1只）");
        }

        // ================================================================
        //  ③ 成阵判定（摆位 → FormationMatched）
        // ================================================================
        private static void CheckFormationMatch(StringBuilder sb)
        {
            Note(sb, "---- ③ 成阵判定 ----");

            var play = MakePlay(new[] { 0, 2, 4, 6, 8 });        // 鱼鳞阵位
            MatchCase(sb, play.State, "fm_scale", true, "五只按鱼鳞阵摆位");
            MatchCase(sb, play.State, "fm_wedge", false, "鱼鳞位缺前中(5) ⇒ 锋矢阵不成");

            var play2 = MakePlay(new[] { 1, 3, 4, 5, 7 });       // 云龙阵位
            MatchCase(sb, play2.State, "fm_cloud", true, "五只按云龙阵摆位");
            MatchCase(sb, play2.State, "fm_scale", false, "云龙位缺角(0) ⇒ 鱼鳞阵不成");

            var play3 = MakePlay(new[] { 0, 4, 8, 1, 2 });       // 对角线 0/4/8
            MatchCase(sb, play3.State, "fm_goose", true, "对角线(0/4/8)成雁行阵");

            var play4 = MakePlay(new[] { 0, 1, 2, 6, 7 });       // 中宫空
            MatchCase(sb, play4.State, "fm_scale", false, "中宫空 ⇒ 不成阵");
        }

        private static void MatchCase(StringBuilder sb, BattleState st, string id, bool expect, string label)
        {
            var c = ComboRules.For(id);
            if (c == null) { Bad(sb, $"找不到阵形 {id}"); return; }
            bool got = ComboRules.FormationMatched(st, TeamSide.Player, c);
            if (got == expect) Pass(sb, $"{label}：{c.Name} matched={got}（期望 {expect}）");
            else Bad(sb, $"{label}：{c.Name} matched={got}（期望 {expect}）");
        }

        // ================================================================
        //  ④ QTE 成功：扣 灵力=存活数 + 满份大招能量 + 标记 UsedCombos
        // ================================================================
        private static void CheckQteFire(StringBuilder sb)
        {
            Note(sb, "---- ④ QTE 成功发放 ----");
            var play = MakePlay(new[] { 0, 2, 4, 6, 8 });
            play.State.ParryEnabled = true;
            var st = play.State;
            st.TeamMp = 12;

            var center = CenterUnit(st);
            if (center == null) { Bad(sb, "没有中宫单位（摆位失败）"); return; }
            center.Rage = st.Config.UltimateRageCost;

            var avail = ComboRules.AvailableFor(st, center);
            if (avail.Count == 0) { Bad(sb, "中宫单位 AvailableFor 为空（应含鱼鳞阵）"); return; }
            Pass(sb, "AvailableFor 含：" + avail[0].Name);

            int mpBefore = st.TeamMp;
            bool submitted = RunComboQte(play, "fm_scale", ParryGrade.Perfect);
            if (!submitted) { Bad(sb, "SubmitCombo 失败"); return; }

            if (st.UsedCombos.Contains("fm_scale")) Pass(sb, "已在 UsedCombos 标记（每场一次）");
            else Bad(sb, "未标记 UsedCombos");

            int alive = ComboRules.AliveCount(st, TeamSide.Player);
            int mpAfter = st.TeamMp;
            // ⚠ 回合自然回灵会 +2，这里用"至少扣了 alive 点"的宽松判据
            bool mpOk = mpAfter <= mpBefore - alive + 2;
            if (mpOk) Pass(sb, $"灵力按存活数扣：存活{alive} 前{mpBefore} → 后{mpAfter}");
            else Bad(sb, $"灵力扣减异常：存活{alive} 前{mpBefore} → 后{mpAfter}（期望至少 -{alive}）");

            if (center.Rage <= 0.01f) Pass(sb, "主兽大招能量已扣（Rage=" + center.Rage + "）");
            else Bad(sb, "主兽大招能量未扣（Rage=" + center.Rage + "）");
        }

        // ================================================================
        //  ⑤ QTE 失败：扣 1 灵力 + 50% 大招能量、不标记 UsedCombos
        // ================================================================
        private static void CheckQteFail(StringBuilder sb)
        {
            Note(sb, "---- ⑤ QTE 失败惩罚 ----");
            var play = MakePlay(new[] { 0, 2, 4, 6, 8 });
            play.State.ParryEnabled = true;
            var st = play.State;
            st.TeamMp = 12;

            var center = CenterUnit(st);
            if (center == null) { Bad(sb, "没有中宫单位"); return; }
            center.Rage = st.Config.UltimateRageCost;

            int mpBefore = st.TeamMp;
            float rageBefore = center.Rage;
            bool submitted = RunComboQte(play, "fm_scale", ParryGrade.None);
            if (!submitted) { Bad(sb, "SubmitCombo 失败"); return; }

            if (st.UsedCombos.Contains("fm_scale")) Bad(sb, "失败却标记了 UsedCombos（应不标记，可再试）");
            else Pass(sb, "失败未标记 UsedCombos（阵形还在可再试）");

            bool mpOk = st.TeamMp <= mpBefore + 1;   // 失败 -1（回合 +2 的宽松判据）
            if (mpOk) Pass(sb, $"失败扣 1 灵力：前{mpBefore} → 后{st.TeamMp}");
            else Bad(sb, $"失败灵力异常：前{mpBefore} → 后{st.TeamMp}");

            float expectRage = rageBefore - st.Config.UltimateRageCost * 0.5f;
            bool rageOk = Mathf.Abs(center.Rage - expectRage) < 0.6f;
            if (rageOk) Pass(sb, $"失败扣 50% 大招能量：{rageBefore:0} → {center.Rage:0}（期望 {expectRage:0}）");
            else Bad(sb, $"失败大招能量异常：{rageBefore:0} → {center.Rage:0}（期望 {expectRage:0}）");

            if (HasLogNote(st, "触发失败")) Pass(sb, "已记「触发失败」事件");
            else Bad(sb, "没有「触发失败」事件");
        }

        // ================================================================
        //  驱动工具
        // ================================================================

        /// <summary>按指定格位摆 5 只我方（敌方为高血量木桩，保证跑得完）。</summary>
        private static BattlePlayback MakePlay(int[] cells)
        {
            var req = new BattleRequest
            {
                Title = "阵形连携自检",
                Seed = 20261016UL,
                Player = new List<BeastDef>(),
                PlayerCells = new List<int>(cells),
                Enemy = new List<BeastDef>(),
            };
            for (int i = 0; i < cells.Length; i++) req.Player.Add(Mk("ct_p" + i, "我" + (i + 1), 400, 60, 20, 30 - i));
            for (int i = 0; i < 5; i++) req.Enemy.Add(Mk("ct_e" + i, "桩" + (i + 1), 4000, 20, 20, 10));
            return new BattlePlayback(req, manual: true);
        }

        private static BattleUnit CenterUnit(BattleState st)
        {
            var ours = st.UnitsOf(TeamSide.Player);
            for (int i = 0; i < ours.Count; i++)
                if (ours[i] != null && ours[i].IsAlive && ours[i].Pos.IsCenter) return ours[i];
            return null;
        }

        /// <summary>
        /// 把回放驱动到"中宫单位待令"，提交连携，并按指定档位应答它的全部 QTE 段。
        /// 返回 false = 一路走不到提交那一步。
        /// </summary>
        private static bool RunComboQte(BattlePlayback play, string comboId, ParryGrade grade)
        {
            int guard = 0;
            bool submitted = false;

            while (!play.Finished && guard++ < 4000)
            {
                if (play.AwaitingParry)
                {
                    // 连携窗口按测试档位应答；敌方格挡一律完美，避免节外生枝
                    var kind = play.State.ParryQteKind;
                    play.SubmitParry(kind == ParryQteKind.Combo ? grade : ParryGrade.Perfect);
                    play.Step();
                    continue;
                }
                if (play.AwaitingCommand)
                {
                    var u = play.PendingUnit;
                    if (!submitted && u != null && u.Pos.IsCenter)
                    {
                        submitted = play.SubmitCombo(comboId);
                        if (!submitted) return false;
                        play.Step();
                        continue;
                    }
                    play.SubmitCommand(0, -1);   // 其它单位普攻
                    play.Step();
                    continue;
                }
                if (!play.Step()) break;

                // 结算完毕的两个信号：标记 UsedCombos（成功）或「触发失败」事件（失败）
                if (submitted && play.State.UsedCombos.Contains(comboId)) break;
                if (submitted && HasLogNote(play.State, "触发失败")) break;
            }
            return submitted;
        }

        private static bool HasLogNote(BattleState st, string keyword)
        {
            if (st == null || st.Log == null) return false;
            var log = st.Log;
            for (int i = 0; i < log.Count; i++)
            {
                var e = log.Events[i];
                if (e.Note != null && e.Note.Contains(keyword)) return true;
            }
            return false;
        }

        private static BeastDef Mk(string id, string name, int hp, int atk, int def, int spd)
        {
            return new BeastDef
            {
                Id = id, DisplayName = name,
                Element = Element.Wood, Role = RoleType.Guard, Rarity = Rarity.Rare,
                BaseHp = hp, BaseAtk = atk, BaseDef = def, BaseSpeed = spd,
                CritRate = 0f, CritDamage = 1.5f,
                Basic = new SkillDef { Name = "普攻", Type = SkillType.Basic, Cd = 0, PrimaryTarget = TargetSelector.SingleLowestHp,
                                       Effects = new[] { EffectAtom.Damage(TargetSelector.SingleLowestHp, 0.30f, 1) } },
                Active = new SkillDef { Name = "战技", Type = SkillType.Active, Cd = 0, PrimaryTarget = TargetSelector.SingleLowestHp,
                                        Effects = new[] { EffectAtom.Damage(TargetSelector.SingleLowestHp, 0.30f, 1) } },
                Ultimate = new SkillDef { Name = "终结技", Type = SkillType.Ultimate, Cd = 0, PrimaryTarget = TargetSelector.SingleLowestHp,
                                          Effects = new[] { EffectAtom.Damage(TargetSelector.SingleLowestHp, 0.30f, 1) } },
            };
        }

        // ---- 计数小工具 ----
        private static void Pass(StringBuilder sb, string msg) { _pass++; sb.AppendLine("✅ " + msg); }
        private static void Bad(StringBuilder sb, string msg) { _fail++; sb.AppendLine("❌ " + msg); }
        private static void Note(StringBuilder sb, string msg) { sb.AppendLine(msg); }
        private static void Eq(StringBuilder sb, string label, string got, string expect)
        {
            if (got == expect) Pass(sb, label);
            else Bad(sb, label + $"（实际 {got}）");
        }
    }
}
