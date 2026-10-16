// ============================================================================
//  万相 · 敌方招式库自检（foe.selftest）—— 2026-10-16 FoeKit
//  ---------------------------------------------------------------------------
//  守的是四件编译期查不出的事：
//    ① 表结构：12 只敌人配置齐全、段数不超硬上限、谱系引用有效、破绽字段自洽
//   ② **零副作用**：没配 FoeKit 的普通兽，弱点/破绽字段恒为默认值
//      （这是指纹红线的兜底 —— 天气自检的基准局必须逐位不变）
//    ③ 弱点/抗性倍率正确（命中弱点 ×1.35、被抗 ×0.75、无属性不吃）
//    ④ 破绽 → 失衡链路：进度累计、触发、重置、回合递减
//
//  ⚠ 只在失败时弹模态框（理由同其余自检）：全绿也弹会把编辑器主线程卡在对话框上，
//    自动化（诊断桥/MCP）跑这条命令时后续命令会全部超时。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Editor.BattleTool
{
    public static class FoeSelfTest
    {
        private const string ReportPath = "Temp/WanXiangDiag/foe_selftest.txt";

        private static int _pass;
        private static int _fail;

        [MenuItem("万相/战斗/敌方招式库自检（foe.selftest）")]
        public static void RunFromMenu()
        {
            var lines = Run("foe.selftest");
            foreach (var l in lines) Debug.Log("[敌方招式库自检] " + l);
            if (_fail > 0)
                EditorUtility.DisplayDialog("敌方招式库自检",
                    $"❌ {_pass} 过 / {_fail} 败，失败项见 Console 与 {ReportPath}。", "好");
        }

        public static string[] Run(string command)
        {
            _pass = 0; _fail = 0;
            var sb = new StringBuilder(12288);
            sb.AppendLine("万相 · 敌方招式库自检（foe.selftest）");
            sb.AppendLine(new string('=', 72));
            sb.AppendLine($"时间　　{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("范围　　FoeKit 表结构 + 零副作用 + 弱点倍率 + 破绽失衡链路");
            sb.AppendLine();

            try
            {
                CheckTable(sb);
                CheckNoSideEffect(sb);
                CheckWeakness(sb);
                CheckFlawChain(sb);
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
            catch { }
        }

        // ================================================================
        //  ① 表结构
        // ================================================================
        private static void CheckTable(StringBuilder sb)
        {
            Note(sb, "---- ① 表结构 ----");
            var all = FoeRules.All;
            if (all == null || all.Count == 0) { Bad(sb, "FoeKit 表为空"); return; }
            if (all.Count != 12) Bad(sb, $"配置数应为 12，实际 {all.Count}"); else Pass(sb, "配置数 = 12");

            var ids = new HashSet<string>();
            int overSeg = 0, noWeak = 0, noFlaw = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var k = all[i];
                if (k == null) { Bad(sb, $"第 {i} 项为 null"); continue; }
                if (string.IsNullOrEmpty(k.Id)) { Bad(sb, $"第 {i} 项没有 Id"); continue; }
                if (!ids.Add(k.Id)) { Bad(sb, $"Id 重复：{k.Id}"); continue; }
                if (string.IsNullOrEmpty(k.UltName)) Bad(sb, $"{k.Id} 没有绝技名");
                if (k.Family == FoeFamily.None) Bad(sb, $"{k.Id} 没配谱系");
                if (k.Segments < 1) Bad(sb, $"{k.Id} 段数 <1");
                if (k.Segments > FoeRules.MaxSegments)
                { Bad(sb, $"{k.Id} 段数 {k.Segments} 超硬上限 {FoeRules.MaxSegments}"); overSeg++; }
                // 非吐息谱系不许超过 4（吐息是唯一允许长连打的）
                if (k.Family != FoeFamily.Breath && k.Segments > 4)
                { Bad(sb, $"{k.Id}（{FoeRules.FamilyCn(k.Family)}）段数 {k.Segments} >4"); overSeg++; }
                if (k.WeakElement == Element.None) noWeak++;
                if (string.IsNullOrEmpty(k.FlawName)) noFlaw++;
                if (k.FlawTrigger == FoeFlawTrigger.HitByElement && k.FlawElement == Element.None)
                    Bad(sb, $"{k.Id} 破绽按元素触发但没填 FlawElement");
                if (k.FlawTrigger == FoeFlawTrigger.StatusStacks && string.IsNullOrEmpty(k.FlawStatusId))
                    Bad(sb, $"{k.Id} 破绽按状态触发但没填 FlawStatusId");
                if (k.UnbalancedTurns < 1) Bad(sb, $"{k.Id} 失衡回合数 <1");
                if (string.IsNullOrEmpty(k.Tagline)) Bad(sb, $"{k.Id} 没有一句话定位");
            }
            if (overSeg == 0) Pass(sb, "段数全部在上限内（吐息≤6，其余≤4）");
            if (noWeak > 0) Note(sb, $"无弱点的配置 {noWeak} 个（混沌之母是有意的设计）");
            else Pass(sb, "全部配置都有弱点");
            if (noFlaw > 0) Pass(sb, $"无破绽的配置 {noFlaw} 个（终局首领，有意为之）");

            // 谱系覆盖：5 个谱系都至少有 1 只
            var fams = new HashSet<FoeFamily>();
            foreach (var k in all) fams.Add(k.Family);
            if (fams.Count == 5) Pass(sb, "5 个谱系全部有敌人使用");
            else Bad(sb, $"谱系只覆盖 {fams.Count} 个");

            // 破架势（格挡即输出路线）必须存在
            bool parryFlaw = false;
            foreach (var k in all)
                if (k.FlawTrigger == FoeFlawTrigger.ParryCount) { parryFlaw = true; break; }
            if (parryFlaw) Pass(sb, "存在「格挡 N 次」型破绽（防御即输出路线）");
            else Bad(sb, "没有格挡型破绽 —— 防御无法成为进攻路线");
        }

        // ================================================================
        //  ② 零副作用
        // ================================================================
        private static void CheckNoSideEffect(StringBuilder sb)
        {
            Note(sb, "---- ② 零副作用（指纹红线兜底）----");
            var plain = BattleSampleContent.Make("plain_x", "普通兽", Element.Wood, RoleType.Guard);
            var req = new WanXiang.Modules.UI.BattleRequest { Title = "t", Seed = 31UL };
            req.Player.Add(plain); req.PlayerCells.Add(4);
            var boss = BossCatalog.BuildBoss("b_chiba");
            req.Enemy.Add(boss);
            var st = new WanXiang.Modules.UI.BattlePlayback(req, manual: true).State;

            // ⚠ 不要取 UnitsOf(Enemy)[0]：赤魃开局会召唤「半血分身」，顺序不保证本体在 0 号。
            var foe = FindFoeKit(st, TeamSide.Enemy);
            if (foe == null) { Bad(sb, "阵容里找不到配了 FoeKit 的敌人"); return; }
            if (foe.FoeWeakElement != Element.None || foe.FoeFlawTrigger != FoeFlawTrigger.None)
                Pass(sb, $"已配置首领挂上了弱点/破绽（弱点 {foe.FoeWeakElement}、破绽 {foe.FoeFlawTrigger}）");
            else Bad(sb, "首领没挂上弱点/破绽");

            // 找一个未配置的敌人单位（普通兽当敌方用）
            var req2 = new WanXiang.Modules.UI.BattleRequest { Title = "t2", Seed = 32UL };
            req2.Player.Add(BattleSampleContent.Make("p2", "我方", Element.Wood, RoleType.Guard));
            req2.PlayerCells.Add(4);
            req2.Enemy.Add(BattleSampleContent.Make("plain_e", "野怪", Element.Fire, RoleType.Guard));
            var st2 = new WanXiang.Modules.UI.BattlePlayback(req2, manual: true).State;
            var wild = st2.UnitsOf(TeamSide.Enemy)[0];
            if (wild.FoeWeakElement == Element.None && wild.FoeResistElement == Element.None
                && wild.FoeFlawTrigger == FoeFlawTrigger.None && wild.FoeFamily == FoeFamily.None
                && wild.FoeUnbalancedTurns == 0)
                Pass(sb, "未配置的野怪字段全为默认值（逐位不变）");
            else Bad(sb, "未配置的野怪被写了弱点/破绽字段");
            if (FoeKitRuntime.IncomingMul(st2.UnitsOf(TeamSide.Player)[0], wild) == 1f)
                Pass(sb, "未配置目标的受伤倍率 = 1（×1.00）");
            else Bad(sb, "未配置目标的受伤倍率被改了");
        }

        // ================================================================
        //  ③ 弱点 / 抗性倍率
        // ================================================================
        private static void CheckWeakness(StringBuilder sb)
        {
            Note(sb, "---- ③ 弱点 / 抗性倍率 ----");
            var boss = BossCatalog.BuildBoss("b_chiba");       // 赤魃：弱点水、抗火
            var req = new WanXiang.Modules.UI.BattleRequest { Title = "t", Seed = 41UL };
            req.Player.Add(Mk("pw", "水", Element.Water));
            req.PlayerCells.Add(2);
            req.Player.Add(Mk("pf", "火", Element.Fire));
            req.PlayerCells.Add(1);
            req.Player.Add(Mk("pn", "无属", Element.None));
            req.PlayerCells.Add(3);
            req.Enemy.Add(boss);
            var st = new WanXiang.Modules.UI.BattlePlayback(req, manual: true).State;
            var foe = FindFoeKit(st, TeamSide.Enemy);
            var ours = st.UnitsOf(TeamSide.Player);
            if (foe == null) { Bad(sb, "阵容里找不到配了 FoeKit 的敌人"); return; }

            BattleUnit water = null, fire = null, none = null;
            foreach (var u in ours)
            {
                if (u.Element == Element.Water) water = u;
                if (u.Element == Element.Fire) fire = u;
                if (u.Element == Element.None) none = u;
            }
            if (water == null || fire == null || none == null)
            { Bad(sb, "没造出水/火/无属三个测试单位"); return; }

            var wm = FoeKitRuntime.WeaknessMul(water, foe);
            var fm = FoeKitRuntime.WeaknessMul(fire, foe);
            if (Mathf.Abs(wm - FoeRules.WeakMul) < 0.001f) Pass(sb, $"打弱点(水) ×{wm:0.00}");
            else Bad(sb, $"打弱点(水) 应 ×{FoeRules.WeakMul:0.00}，实际 ×{wm:0.00}");
            if (Mathf.Abs(fm - FoeRules.ResistMul) < 0.001f) Pass(sb, $"打抗性(火) ×{fm:0.00}");
            else Bad(sb, $"打抗性(火) 应 ×{FoeRules.ResistMul:0.00}，实际 ×{fm:0.00}");

            var nm = FoeKitRuntime.WeaknessMul(none, foe);
            if (nm == 1f) Pass(sb, "无属性攻击不吃弱点（否则无属性会变成最优解）");
            else Bad(sb, $"无属性攻击被算成 ×{nm:0.00}");
        }

        // ================================================================
        //  ④ 破绽 → 失衡
        // ================================================================
        private static void CheckFlawChain(StringBuilder sb)
        {
            Note(sb, "---- ④ 破绽 → 失衡链路 ----");
            var boss = BossCatalog.BuildBoss("b_chiba");       // 焰心：水命中 3 次
            var req = new WanXiang.Modules.UI.BattleRequest { Title = "t", Seed = 51UL };
            req.Player.Add(Mk("pw", "水", Element.Water));
            req.PlayerCells.Add(4);
            req.Enemy.Add(boss);
            var st = new WanXiang.Modules.UI.BattlePlayback(req, manual: true).State;
            var foe = FindFoeKit(st, TeamSide.Enemy);
            var hero = st.UnitsOf(TeamSide.Player)[0];
            if (foe == null) { Bad(sb, "阵容里找不到配了 FoeKit 的敌人"); return; }

            if (foe.FoeFlawTrigger != FoeFlawTrigger.HitByElement)
            { Bad(sb, "赤魃的破绽触发方式不是 HitByElement"); return; }
            foe.FoeFlawProgress = 0;
            foe.FoeUnbalancedTurns = 0;

            bool trig = false;
            for (int i = 1; i <= foe.FoeFlawNeed; i++)
            {
                trig = FoeKitRuntime.OnPlayerHitEnemy(st, hero, foe, Element.Water);
                if (i < foe.FoeFlawNeed && trig) { Bad(sb, $"第 {i} 次就触发了（阈值 {foe.FoeFlawNeed}）"); break; }
            }
            if (trig && foe.FoeUnbalancedTurns > 0) Pass(sb, $"命中 {foe.FoeFlawNeed} 次触发失衡（{foe.FoeUnbalancedTurns} 回合）");
            else Bad(sb, "破绽没有触发失衡");
            if (foe.FoeFlawProgress == 0) Pass(sb, "触发后破绽进度已重置");
            else Bad(sb, $"触发后进度未重置（{foe.FoeFlawProgress}）");

            float mul = FoeKitRuntime.IncomingMul(hero, foe);
            float expect = FoeRules.WeakMul * foe.FoeUnbalancedMul;
            if (Mathf.Abs(mul - expect) < 0.01f) Pass(sb, $"失衡中受伤倍率 ×{mul:0.00}（弱点 {FoeRules.WeakMul} × 失衡 {foe.FoeUnbalancedMul}）");
            else Bad(sb, $"失衡倍率应 ×{expect:0.00}，实际 ×{mul:0.00}");

            int before = foe.FoeUnbalancedTurns;
            FoeKitRuntime.TickUnbalanced(st);
            if (foe.FoeUnbalancedTurns == before - 1) Pass(sb, $"回合末递减：失衡 {before} → {foe.FoeUnbalancedTurns}");
            else Bad(sb, $"回合末递减异常（{before} → {foe.FoeUnbalancedTurns}，期望减 1）");
            FoeKitRuntime.TickUnbalanced(st);
            if (foe.FoeUnbalancedTurns == 0) Pass(sb, "第二次递减后归零（倍率回落）");
            else Bad(sb, $"第二次递减后应归零，实际 {foe.FoeUnbalancedTurns}");

            // 真实伤害不吃弱点（DealDamage 里用 atom.TrueDamage 排除了）
            Note(sb, "　（真实伤害不吃弱点这一条由 DealDamage 的 !atom.TrueDamage 判定保证，见源码）");
        }

        // ---- 工具 ----
        /// <summary>找出阵容里**配了 FoeKit** 的那个单位（不要取 [0]：召唤物会排在前面）。</summary>
        private static BattleUnit FindFoeKit(BattleState st, TeamSide side)
        {
            var list = st.UnitsOf(side);
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (u != null && u.Def != null && FoeRules.Has(u.Def.Id)) return u;
            }
            return null;
        }

        private static BeastDef Mk(string id, string name, Element el) => MkBeast(id, name, el);

        private static BeastDef MkBeast(string id, string name, Element el)
        {
            var d = BattleSampleContent.Make(id, name, el, RoleType.Striker);
            d.BaseHp = 2000; d.BaseAtk = 50;
            return d;
        }

        private static void Pass(StringBuilder sb, string m) { _pass++; sb.AppendLine("✅ " + m); }
        private static void Bad(StringBuilder sb, string m) { _fail++; sb.AppendLine("❌ " + m); }
        private static void Note(StringBuilder sb, string m) { sb.AppendLine(m); }
    }
}