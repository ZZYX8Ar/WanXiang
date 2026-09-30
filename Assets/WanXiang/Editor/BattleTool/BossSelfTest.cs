// ============================================================================
//  万相 · 编辑器工具 · 首领战自检（命令 boss.selftest）
//  ---------------------------------------------------------------------------
//  守的是两件编译期查不出的事：
//
//   【一】内容表自洽 —— 14 个首领、5 个幕池、确定性抽首领（同局同幕固定）。
//        首领是专属内容（id 以 b_ 开头），绝不进玩家图鉴/招募/灵市；
//        这条红线靠 IsBoss 前缀 + ApplyPlaceholderStats 跳过 b_ 来守，这里只核对结构。
//
//   【二】机制真的会挂、真的会触发 —— 每个首领在 BattleFactory.Create 时自动
//        挂上对应钩子（BossCatalog.AttachAllBossHooks），空列表短路保证普通
//        战斗逐位不变。这里挑了 8 类代表机制，各造一局扫战斗日志 Note 验证：
//        反弹 / 召唤 / 双子同命 / 假死(碎冰重生) / 属性轮转 + 硬性DPS灭团 /
//        破壳护盾 / 免疫克制 / 首击减伤。
//
//  ⚠ 为什么是 Edit 模式命令而不是 Play：
//    战斗核心是零引擎依赖的纯逻辑（WanXiang.Battle.Core 的 noEngineReferences=true），
//    Edit 模式同步跑得动，不必进 Play。这台机器也不维持 Play 循环（见项目记忆）。
//
//  ⚠ 日志 Note 不进指纹（BattleLog 的 Note 字段被指纹排除），所以"扫 Note 判机制"
//    不会污染可复现性判据；可复现性另用 [4] 一节用指纹直接验证。
//
//  报告：Temp/WanXiangDiag/boss_selftest.txt
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Editor.BattleTool
{
    public static class BossSelfTest
    {
        private const string ReportPath = "Temp/WanXiangDiag/boss_selftest.txt";

        public static string[] Run(string command)
        {
            var c = new Ctx();
            c.Sb.AppendLine("万相 · 首领战自检（boss.selftest）");
            c.Sb.AppendLine(new string('=', 72));
            c.Sb.AppendLine($"时间　　{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            c.Sb.AppendLine("范围　　14 首领内容表 + 确定性抽首领 + 机制钩子挂接与触发");
            c.Sb.AppendLine("程序集　WanXiang.Battle.Core（noEngineReferences = true）");
            c.Sb.AppendLine();

            try
            {
                CheckContentShape(c);
                CheckDeterministicPick(c);
                CheckBuildAndAttachAll(c);
                CheckDeterminismRedLine(c);
                CheckRepresentativeMechanics(c);
            }
            catch (Exception ex)
            {
                c.Bad($"自检过程抛出异常：{ex.GetType().Name}：{ex.Message}");
                c.Note(ex.StackTrace ?? "");
            }

            c.Sb.AppendLine();
            c.Sb.AppendLine(new string('=', 72));
            c.Sb.AppendLine($"结论：{c.Pass} 项通过，{c.Fail} 项失败");
            if (c.Fail == 0)
            {
                c.Sb.AppendLine("说明：内容表/确定性/挂钩是机器判定；代表机制触发靠扫战斗日志 Note，");
                c.Sb.AppendLine("      只证明「钩子挂上了且确实跑了」，不证明数值平衡（平衡是 P5 的事）。");
            }

            string text = c.Sb.ToString();
            WriteReport(text);
            return text.Replace("\r\n", "\n").Split('\n');
        }

        [MenuItem("万相/战斗/首领战自检（boss.selftest）")]
        public static void RunFromMenu()
        {
            var lines = Run("boss.selftest");
            foreach (var l in lines) Debug.Log("[首领战自检] " + l);
            if (/* fail count tracked in report */ lines.Length > 0 && Array.Exists(lines, x => x.Contains("项失败") && !x.Contains("0 项失败")))
                EditorUtility.DisplayDialog("首领战自检",
                    "❌ 有失败项，见 Console 与 " + ReportPath + "。", "好");
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
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[boss.selftest] 报告写入失败：{ex.Message}");
            }
        }

        // ================================================================
        //  1) 内容表结构自洽
        // ================================================================

        private static void CheckContentShape(Ctx c)
        {
            c.Title("[1/5] 14 首领内容表结构");

            if (BossCatalog.All.Count == 14)
                c.Ok("共 14 个首领（幕一~四各 3 + 幕五 2）");
            else
                c.Bad($"首领数量 = {BossCatalog.All.Count}（期望 14）");

            if (BossCatalog.Pool.Length == 5)
                c.Ok("5 个幕池（下标 = 幕-1）");
            else
                c.Bad($"幕池数量 = {BossCatalog.Pool.Length}（期望 5）");

            int total = 0;
            var badPrefix = new List<string>();
            for (int a = 0; a < BossCatalog.Pool.Length; a++)
            {
                var p = BossCatalog.Pool[a];
                total += p.Length;
                for (int i = 0; i < p.Length; i++)
                    if (!BossCatalog.IsBoss(p[i])) badPrefix.Add(p[i]);
            }
            if (total == 14)
                c.Ok("5 个池合计 14 个 id（与 All 数量一致）");
            else
                c.Bad($"幕池合计 {total} 个 id（期望 14）");

            if (badPrefix.Count == 0)
                c.Ok("每个池内 id 都以 b_ 开头（首领专属内容红线：绝不进玩家图鉴/招募/灵市）");
            else
                c.Bad("以下池内 id 不以 b_ 开头：" + string.Join("、", badPrefix.ToArray()));

            int[] expectLens = { 3, 3, 3, 3, 2 };
            bool lensOk = true;
            for (int a = 0; a < 5; a++)
                if (BossCatalog.Pool[a].Length != expectLens[a]) lensOk = false;
            if (lensOk)
                c.Ok("各幕池容量 3/3/3/3/2（幕五只有 2 个终局首领）");
            else
                c.Bad("幕池容量不符 3/3/3/3/2（检查 BossCatalog.Pool）");

            // BuildBoss 每个都能翻成带 b_ 前缀的 BeastDef
            int built = 0;
            var badBuild = new List<string>();
            for (int i = 0; i < BossCatalog.All.Count; i++)
            {
                var def = BossCatalog.BuildBoss(BossCatalog.All[i].Id);
                if (def == null || !def.Id.StartsWith("b_")) badBuild.Add(BossCatalog.All[i].Id);
                else built++;
            }
            if (built == 14 && badBuild.Count == 0)
                c.Ok("14 个首领都能 BuildBoss 成 b_ 前缀的 BeastDef（可直接上阵）");
            else
                c.Bad($"BuildBoss 失败 {badBuild.Count} 个：" + string.Join("、", badBuild.ToArray()));
        }

        // ================================================================
        //  2) 确定性抽首领
        // ================================================================

        private static void CheckDeterministicPick(Ctx c)
        {
            c.Title("[2/5] 确定性抽首领（同局同幕固定）");

            bool stable = true;
            for (int act = 1; act <= 5; act++)
            {
                string s1 = BossCatalog.BossFor(act, 7777UL);
                string s2 = BossCatalog.BossFor(act, 7777UL);
                if (s1 != s2) stable = false;
                if (Array.IndexOf(BossCatalog.Pool[act - 1], s1) < 0) stable = false;
            }
            if (stable)
                c.Ok("同种子同幕固定抽（同局确定性），且抽到的都落在对应幕池内");
            else
                c.Bad("确定性抽首领不稳：同种子两次结果不同，或抽到了别的幕的池");

            // BossForActFunc 闭包：实战接入（SeededEnemyProvider.bossForAct）用的就是它
        var fn = BossCatalog.BossForActFunc(9999UL);
        bool fnOk = (fn(1) != null && fn(1).Id == BossCatalog.BossFor(1, 9999UL))
                 && (fn(5) != null && fn(5).Id == BossCatalog.BossFor(5, 9999UL));
            if (fnOk)
                c.Ok("BossForActFunc(seed) 闭包与 BossFor(act,seed) 一致（RunDriver 实战接入用）");
            else
                c.Bad("BossForActFunc 与 BossFor 不一致 —— 实战接入口会对不上");

            var a1 = BossCatalog.BossFor(1, 1UL);
            var a2 = BossCatalog.BossFor(1, 2UL);
            c.Info($"幕一 种子1→{a1}，种子2→{a2}（换局/换轮回会变化，属预期，不强制不同）");
        }

        // ================================================================
        //  3) 14 首领全部可构建 + 自动挂钩不抛异常
        // ================================================================

        private static void CheckBuildAndAttachAll(Ctx c)
        {
            c.Title("[3/5] 14 首领全部可构建 + 自动挂钩（BattleFactory.Create 内 AttachAllBossHooks）");

            int built = 0, attached = 0;
            var fails = new List<string>();

            for (int i = 0; i < BossCatalog.All.Count; i++)
            {
                var id = BossCatalog.All[i].Id;
                try
                {
                    var def = BossCatalog.BuildBoss(id);
                    if (def == null) { fails.Add(id + "：BuildBoss 返回 null"); continue; }
                    built++;

                    // 只放一个垫背玩家，确保战场能 FinishSetup；boss 在敌方 0 格
                    var st = BuildBossBattle(id, 1, RoleType.Guard, 100f, 2000, 20261120UL);
                    if (st.Hooks.Count > 0) attached++;
                    else fails.Add(id + "：自动挂钩数为 0（AttachHooks 没加任何钩子）");
                }
                catch (Exception ex)
                {
                    fails.Add($"{id}：{ex.GetType().Name}：{ex.Message}");
                }
            }

            if (built == 14 && attached == 14 && fails.Count == 0)
                c.Ok("14 个首领全部 BuildBoss 成功，且 BattleFactory.Create 后自动挂上 ≥1 个机制钩子");
            else
                c.Bad($"构建/挂钩失败：built={built} attached={attached} fails={fails.Count}");
            foreach (var f in fails) c.Note("　" + f);
        }

        // ================================================================
        //  4) 首领战可复现（钩子空列表短路，逐位不变）
        // ================================================================

        private static void CheckDeterminismRedLine(Ctx c)
        {
            c.Title("[4/5] 首领战可复现（钩子不引入额外随机流）");

            var st1 = BuildBossBattle("b_manman", 3, RoleType.Striker, 400f, 4000, 20261130UL);
            var r1 = BattleSimulator.Run(st1);
            var st2 = BuildBossBattle("b_manman", 3, RoleType.Striker, 400f, 4000, 20261130UL);
            var r2 = BattleSimulator.Run(st2);

            if (r1.Fingerprint == r2.Fingerprint && r1.Outcome == r2.Outcome && r1.Turns == r2.Turns)
                c.Ok($"带头衔（反射/汲养）的首领战同种子跑两次完全一致：指纹 0x{r1.Fingerprint:X8}，" +
                     $"{r1.Outcome}，{r1.Turns} 回合 —— 钩子空列表短路生效，逐位不变");
            else
                c.Bad($"首领战同种子结果不一致：0x{r1.Fingerprint:X8}/{r1.Turns}回合 vs " +
                      $"0x{r2.Fingerprint:X8}/{r2.Turns}回合 —— 钩子里可能引入了额外随机流或遍历顺序不确定");

            // 对照组：普通战斗（无钩子）也应一致 —— 顺带证明"加钩子后"仍保持同等可复现
            var n1 = BattleSimulator.Run(BuildStandard(20261131UL));
            var n2 = BattleSimulator.Run(BuildStandard(20261131UL));
            if (n1.Fingerprint == n2.Fingerprint)
                c.Ok("对照：无首领的普通战斗同种子也一致（基线）");
            else
                c.Bad("对照：普通战斗同种子都不一致 —— 基底可复现性坏了，与首领战无关，先查 BattleSimulator");
        }

        // ================================================================
        //  5) 代表机制触发（扫战斗日志 Note）
        // ================================================================

        private static void CheckRepresentativeMechanics(Ctx c)
        {
            c.Title("[5/5] 代表机制触发（扫战斗日志 Note）");

            // 反弹（蔓娘荆棘） —— 玩家打 boss 即触发
            ExpectNote(c, "反弹", "b_manman", "伤害反弹", 400f, 4000, 3, RoleType.Striker, 20261201UL);

            // 召唤（蝮魇蔓生） —— 第 2 回合补召唤物
            ExpectNote(c, "召唤", "b_fuman", "召唤物", 400f, 4000, 3, RoleType.Striker, 20261202UL);

            // 双子同命（白魍） —— OnBattleStart 部署双子
            ExpectTwin(c, "b_baiwang");

            // 假死（玄溟碎冰重生） —— boss 被击杀瞬间化冰核
            ExpectNote(c, "碎冰重生", "b_xuanming", "假死(碎冰重生)", 5000f, 6000, 3, RoleType.Striker, 20261203UL);

            // 属性轮转 + 硬性DPS灭团（归墟之主） —— 第 3 回合轮转、第 12 回合灭团
            ExpectNotes(c, new[] { "五行轮转", "灭团" }, "b_guixu", "属性轮转 + 硬性DPS灭团",
                10f, 20000, 3, RoleType.Guard, 20261204UL, maxTurns: 16);

            // 破壳护盾（燋彘硬壳） —— 每回合始加护盾
            ExpectNote(c, "护盾", "b_jiaozhi", "破壳护盾", 700f, 6000, 3, RoleType.Striker, 20261205UL);

            // 免疫克制（鸿蒙混元） —— OnBattleStart 置 IgnoreElementCounter
            ExpectIgnoreCounter(c, "b_hongmeng");

            // 首击减伤（魍魉隐遁） —— 隔离单测，确定性核对数学
            ExpectFirstHitReduce(c);
        }

        // ================================================================
        //  装配辅助
        // ================================================================

        /// <summary>
        /// 造一局「1 个 boss（敌方 0 格）+ N 个我方」的战斗。
        /// 玩家数值在 ApplyPlaceholderStats 之后覆盖（Def.BaseAtk 实时读取、GrowMaxHp 改上限），
        /// 用来精确控制"强/弱"以逼出特定机制。
        /// </summary>
        private static BattleState BuildBossBattle(string bossId, int playerCount,
            RoleType role, float atk, int hp, ulong seed, int maxTurns = -1)
        {
            var bossDef = BossCatalog.BuildBoss(bossId);
            if (bossDef == null) throw new InvalidOperationException("BuildBoss 返回 null：" + bossId);

            var cfg = BattleConfig.Default;
            if (maxTurns > 0) cfg.MaxTurns = maxTurns;

            var players = new DeployEntry[playerCount];
            for (int i = 0; i < playerCount; i++)
                players[i] = DeployEntry.Player(
                    BattleSampleContent.Make("p" + i, "我", Element.Wood, role), i);

            var enemies = new[] { DeployEntry.Enemy(bossDef, 0) };

            // Create 内部自动 AttachAllBossHooks
            var st = BattleFactory.Create(cfg, seed, players, enemies);

            // 覆盖玩家数值（boss 的 b_ 面板已在 Create 内被 ApplyPlaceholderStats 跳过，保留原值）
            var list = st.UnitsOf(TeamSide.Player);
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (u == null) continue;
                u.Def.BaseAtk = (int)atk;
                int need = hp - u.MaxHp;
                if (need > 0) u.GrowMaxHp(need);
            }
            return st;
        }

        /// <summary>无首领的普通战斗（可复现性对照）。</summary>
        private static BattleState BuildStandard(ulong seed)
        {
            var p = new[]
            {
                DeployEntry.Player(BattleSampleContent.Make("w0", "木御", Element.Wood, RoleType.Guard), 0),
                DeployEntry.Player(BattleSampleContent.Make("w1", "木攻", Element.Wood, RoleType.Striker), 1),
                DeployEntry.Player(BattleSampleContent.Make("f2", "火术", Element.Fire, RoleType.Caster), 4),
            };
            var e = new[]
            {
                DeployEntry.Enemy(BattleSampleContent.Make("m0", "金御", Element.Metal, RoleType.Guard), 0),
                DeployEntry.Enemy(BattleSampleContent.Make("h1", "火攻", Element.Fire, RoleType.Striker), 1),
                DeployEntry.Enemy(BattleSampleContent.Make("s4", "水术", Element.Water, RoleType.Caster), 4),
            };
            return BattleFactory.Create(BattleConfig.Default, seed, p, e);
        }

        private static bool HasNote(BattleState st, string needle)
        {
            if (st == null || st.Log == null) return false;
            var ev = st.Log.Events;
            for (int i = 0; i < ev.Count; i++)
            {
                var n = ev[i].Note;
                if (!string.IsNullOrEmpty(n) && n.IndexOf(needle, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        private static void ExpectNote(Ctx c, string needle, string bossId, string label,
            float atk, int hp, int count, RoleType role, ulong seed)
        {
            try
            {
                var st = BuildBossBattle(bossId, count, role, atk, hp, seed);
                BattleSimulator.Run(st);
                if (HasNote(st, needle))
                    c.Ok($"{label}（{bossId}）：战斗日志出现「{needle}」");
                else
                    c.Bad($"{label}（{bossId}）：跑完一局没找到「{needle}」—— 钩子可能没挂或没触发");
            }
            catch (Exception ex)
            {
                c.Bad($"{label}（{bossId}）：构造/运行抛 {ex.GetType().Name}：{ex.Message}");
            }
        }

        private static void ExpectNotes(Ctx c, string[] needles, string bossId, string label,
            float atk, int hp, int count, RoleType role, ulong seed, int maxTurns = -1)
        {
            try
            {
                var st = BuildBossBattle(bossId, count, role, atk, hp, seed, maxTurns);
                BattleSimulator.Run(st);
                var missing = new List<string>();
                foreach (var n in needles) if (!HasNote(st, n)) missing.Add(n);
                if (missing.Count == 0)
                    c.Ok($"{label}（{bossId}）：全部命中 {string.Join("、", needles)}");
                else
                    c.Bad($"{label}（{bossId}）：缺 {string.Join("、", missing)} —— 钩子可能没挂或没触发");
            }
            catch (Exception ex)
            {
                c.Bad($"{label}（{bossId}）：构造/运行抛 {ex.GetType().Name}：{ex.Message}");
            }
        }

        private static void ExpectTwin(Ctx c, string bossId)
        {
            try
            {
                // 只起战场 + 手动跑 OnBattleStart（双子部署在开场钩子里，不用打完）
                var st = BuildBossBattle(bossId, 1, RoleType.Guard, 100f, 2000, 20261210UL);
                BattleHooks.RunStart(st);   // 触发 TwinSpawnHook → 部署双子 + 记「召出双子」

                bool hasTwin = false;
                var enemies = st.UnitsOf(TeamSide.Enemy);
                for (int i = 0; i < enemies.Count; i++)
                    if (enemies[i] != null && enemies[i].Def != null
                        && enemies[i].Def.Id != null && enemies[i].Def.Id.StartsWith("summon_"))
                    { hasTwin = true; break; }

                bool note = HasNote(st, "召出双子");
                if (hasTwin && note)
                    c.Ok($"双子同命（{bossId}）：开场部署双子实例 + 日志「召出双子」（共鸣分摊/同命钩子已加）");
                else
                    c.Bad($"双子同命（{bossId}）：hasTwin={hasTwin} note={note} —— 双子没部署");
            }
            catch (Exception ex)
            {
                c.Bad($"双子同命（{bossId}）：抛 {ex.GetType().Name}：{ex.Message}");
            }
        }

        private static void ExpectIgnoreCounter(Ctx c, string bossId)
        {
            try
            {
                var st = BuildBossBattle(bossId, 1, RoleType.Guard, 100f, 2000, 20261211UL);
                var boss = st.SlotAt(TeamSide.Enemy, 0);
                BattleHooks.RunStart(st);   // IgnoreCounterHook 在 OnBattleStart 置 IgnoreElementCounter
                if (boss != null && boss.IgnoreElementCounter)
                    c.Ok($"免疫克制（{bossId}）：开场后 boss.IgnoreElementCounter = true（混元生效）");
                else
                    c.Bad($"免疫克制（{bossId}）：IgnoreElementCounter 未被置 true —— 鸿蒙混元没生效");
            }
            catch (Exception ex)
            {
                c.Bad($"免疫克制（{bossId}）：抛 {ex.GetType().Name}：{ex.Message}");
            }
        }

        private static void ExpectFirstHitReduce(Ctx c)
        {
            try
            {
                // 隔离单测：首击 ×(1-0.6)，次击不变（每回合只减首击）
                var hook = new FirstHitReduceHook(0.60f);
                var st = new BattleState(BattleConfig.Default.WithoutJitter(), 1UL);
                var dst = new BattleUnit(TeamSide.Enemy,
                    BattleSampleContent.Make("d", "目标", Element.Wood, RoleType.Guard), "E0");

                int d1 = 1000; hook.ModifyIncoming(st, null, dst, ref d1, Element.None);
                int d2 = 1000; hook.ModifyIncoming(st, null, dst, ref d2, Element.None);

                if (d1 == 400 && d2 == 1000)
                    c.Ok("首击减伤（b_wangliang）：首击 1000→400，次击仍 1000（每回合仅首击减伤）");
                else
                    c.Bad($"首击减伤（b_wangliang）不符：首击 {d1}、次击 {d2}（期望 400 / 1000）");
            }
            catch (Exception ex)
            {
                c.Bad($"首击减伤（b_wangliang）：隔离单测抛 {ex.GetType().Name}：{ex.Message}");
            }
        }

        // ================================================================
        //  文本
        // ================================================================

        private sealed class Ctx
        {
            public readonly StringBuilder Sb = new StringBuilder(8192);
            public int Pass;
            public int Fail;

            public void Title(string t)
            {
                Sb.AppendLine();
                Sb.AppendLine("── " + t + " " + new string('─', Math.Max(1, 60 - t.Length * 2)));
            }

            public void Ok(string msg) { Pass++; Sb.AppendLine("  ✅ " + msg); }
            public void Bad(string msg) { Fail++; Sb.AppendLine("  ❌ " + msg); }
            public void Info(string msg) { Sb.AppendLine("  · " + msg); }
            public void Note(string msg) { Sb.AppendLine("      " + msg); }
        }
    }
}
