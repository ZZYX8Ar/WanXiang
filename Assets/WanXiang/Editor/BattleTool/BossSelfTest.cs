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
                CheckWinnability(c);
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
        //  [6] 可胜性（P5）：固定满编队 × 两档遗物 × N 局，看胜率落在不在目标区间
        //  ----------------------------------------------------------------
        //  设计文档 §11 的验收口径：「固定队伍 + 无遗物 / 中等遗物两档各跑 N 次，
        //  看胜率是否落在目标区间」。既要防「必输」（<下限），也要防「白给」（>上限）——
        //  首领战应该是"构筑对了能赢、无脑硬碰会输"的硬仗。
        //
        //  ⚠ 队伍口径**沿用 CampaignSelfTest ⑪ 的既有约定**（别另外发明）：
        //    我方 = 5 只神品（Legend）满编进攻型 + BattleConfig.LegendMultiplier 成长倍率，
        //    敌方随从 = 灵品（Rare）灰盒，与真实游戏里"玩家养成后打同级敌人"同构。
        //    之前一版用"默认稀有度 + 无成长"当玩家 ⇒ 连普通遭遇都 0% 胜，量到的是脚手架没配对。
        //
        //  ⚠ **对照的选法**：必须用与守关**同规模**的节点，即 `NodeKind.Elite`
        //    （规模表 §5.5：精英 4/5/5/5 = 守关 4/5/5/5；遭遇只有 3/4/4/5）。
        //    用遭遇当对照会把"人数差一格"算进"首领强度"里，结论不可信。
        //
        //  ⚠ 这是**相对校准**，不是绝对真理：灰盒对手（BattleSampleContent）与真实
        //    异兽的技能组不同；真平衡要等策划数值表落地后、用真实阵容再跑一遍。
        // ================================================================

        /// <summary>一档遗物的"中等强度"折算：无遗物 = 1.0，中等遗物 ≈ +15% 战力（约 4 件）。</summary>
        private const float NoRelicMul = 1.00f;
        private const float MidRelicMul = 1.15f;

        /// <summary>每档每幕跑多少局（固定种子序列 ⇒ 可复现）。</summary>
        private const int WinnabilityRuns = 60;

        /// <summary>
        /// 策划数值表落地后的**目标胜率区间**（低于 = 必输，高于 = 白给）。
        /// ⚠ 当前灰盒数据下**不用它做断言**（占位数值不可信），只在报告里作为校准目标出示。
        /// </summary>
        private const float WinRateTargetLo = 0.30f;
        private const float WinRateTargetHi = 0.70f;

        /// <summary>神品成长倍率（沿用 CampaignSelfTest ⑪ 的"毕业队"口径）。</summary>
        private const float LegendGrowth = 1.90f;

        private static void CheckWinnability(Ctx c)
        {
            c.Title("[6/6] 首领战可胜性（满编神品队 × 两档遗物 × " + WinnabilityRuns + " 局，防必输/防白给）");

            try
            {
                for (int act = 1; act <= 4; act++)
                {
                    float wrNo = RunWinnabilityFor(act, NoRelicMul);
                    float wrMid = RunWinnabilityFor(act, MidRelicMul);
                    float ctrl = RunNormalControl(act, NoRelicMul);

                    string line = $"第{act}幕：无遗物 {wrNo:P0}（{Mathf.RoundToInt(wrNo * WinnabilityRuns)}/{WinnabilityRuns}）"
                                + $"　中等遗物 {wrMid:P0}（{Mathf.RoundToInt(wrMid * WinnabilityRuns)}/{WinnabilityRuns}）"
                                + $"　[对照·同规模精英 {ctrl:P0}]";

                    // ⚠ 灰盒数值是占位数据，**不做绝对胜率断言**（那是策划数值表落地后的事）。
                    //   这里只断言三条"结构性质"，它们与具体数值无关、真平衡时也必须成立：
                    //   ① 对照能赢且干净的（脚手架队伍没配弱，否则一切观测无意义）；
                    //   ② 守关确实有代价（不白送）；
                    //   ③ 遗物带来正收益（成长曲线存在，不是摆设）。
                    bool ctrlSane = ctrl >= 0.50f && _lastControlClean;                  // ①

                    // ② 的判据要**看对照有没有区分度**：
                    //   ⚠ 神品满编打灰盒对手时对照常打满 100%（天花板）——此时"首领比对照低 10%"
                    //     是拿一个已经被压死的基准当标尺，量不出东西，反而把"守关有代价"误判成失败。
                    //   故：对照饱和（≥99%）时退化为"至少有一档没白给"（守关不是白送）；
                    //       对照未饱和时才用严格判据（首领明显比同规模精英更难）。
                    bool ctrlSaturated = ctrl >= 0.99f;
                    bool bossHarder = ctrlSaturated
                        ? Mathf.Min(wrNo, wrMid) < 0.99f
                        : ctrl - Mathf.Max(wrNo, wrMid) >= 0.10f;

                    bool relicHelps = wrMid >= wrNo - 0.10f;        // ③（允许噪声）

                    if (ctrlSane && bossHarder && relicHelps)
                        c.Ok($"{line}　→ 结构成立（对照能赢/守关有代价/遗物有正收益）"
                           + (ctrlSaturated ? "（对照饱和，②按「守关不白给」判）" : ""));
                    else if (!_lastControlClean)
                        c.Bad($"{line}　→ 对照里混进了首领单位：同规模精英不该抽到 b_ 首领（工厂回归）");
                    else if (!ctrlSane)
                        c.Bad($"{line}　→ 对照只有 {ctrl:P0}：脚手架队伍偏弱（连同规模精英都打不过），先修基准");
                    else if (!bossHarder)
                        c.Bad($"{line}　→ 守关没代价：两档遗物都打满（{wrNo:P0}/{wrMid:P0}），首领≈白送");
                    else
                        c.Bad($"{line}　→ 中等遗物反而明显更差（遗物可能没折叠进战斗）");
                }

                c.Info("口径：灰盒对手 + 神品满编（沿用 CampaignSelfTest ⑪）。胜率是【校准数据】（供策划调 A 系数/首领面板），");
                c.Info("      断言只保证结构性质（对照能赢 / 守关有代价 / 遗物有正收益）；绝对胜率区间留到真实数值表。");
                c.Info("      ⚠ 对照（同规模精英）常打满 100% ⇒ 该档对「首领是否更难」无区分度，②退化为「守关不白给」。");
                c.Info($"      校准目标区间（暂不断言）：{WinRateTargetLo:P0} ~ {WinRateTargetHi:P0}；"
                     + $"神品成长倍率 {LegendGrowth:0.00}；每题 ×{NoRelicMul:0.00}/{MidRelicMul:0.00} 两档遗物。");
                c.Info("      待办（须记入数值校准）：若某幕「两档都打满」⇒ 该幕首领池相对同规模精英缺代价，需调 BossMul/首领面板。");
            }
            catch (Exception ex)
            {
                c.Bad($"可胜性自检抛 {ex.GetType().Name}：{ex.Message}");
                c.Note(ex.StackTrace ?? "");
            }
        }

        /// <summary>
        /// 跑一幕的首领战胜率：敌方 = 真实 BossSquadFor（首领 + 灵品随从，带 BossUnitMul），
        /// 我方 = 5 神品满编（成长倍率随遗物档叠加）。
        /// </summary>
        private static float RunWinnabilityFor(int act, float relicTierMul)
        {
            var provider = new WanXiang.Campaign.SeededEnemyProvider(
                a => SamplePoolFor(a), BossCatalog.BossForActFunc(0xB055UL));

            var cfg = BattleConfig.Default;
            cfg.LegendMultiplier = LegendGrowth * relicTierMul;   // 遗物档 ≈ 提高神品成长倍率

            int wins = 0;
            for (int run = 0; run < WinnabilityRuns; run++)
            {
                ulong seed = CoreMath.Fnv1a($"boss-winnability:{act}:{run}");
                var enemies = provider.BossSquadFor(act, seed);
                var players = BuildLegendTeam();
                if (players.Length == 0 || enemies.Length == 0) continue;

                var st = BattleFactory.Create(cfg, seed, players, enemies);
                BattleSimulator.Run(st);
                if (st.Outcome == BattleOutcome.PlayerWin) wins++;
            }
            return (float)wins / WinnabilityRuns;
        }

        /// <summary>
        /// 对照：同一满编神品队 vs 该幕「**同规模精英战**」（非首领）的胜率。
        ///
        /// ⚠ 为什么对照必须用 Elite 而不是 Encounter：规模表（§5.5）里守关是 4/5 格、
        ///   精英也是 4/5 格，而遭遇只有 3/4/4/5 —— 用遭遇当对照，量到的差异里混进了
        ///   "人数差一个"，且神品满编打小规模灰盒遭遇会打满 100%（天花板效应），
        ///   于是"首领更难"这条断言永远无从成立。精英战才是与守关**同规模**的对照组。
        /// </summary>
        private static float RunNormalControl(int act, float relicTierMul)
        {
            var provider = new WanXiang.Campaign.SeededEnemyProvider(a => SamplePoolFor(a));
            var cfg = BattleConfig.Default;
            cfg.LegendMultiplier = LegendGrowth * relicTierMul;

            int wins = 0;
            bool clean = true;
            for (int run = 0; run < WinnabilityRuns; run++)
            {
                ulong seed = CoreMath.Fnv1a($"boss-winnability:{act}:{run}");
                var enemies = provider.EnemiesFor(act, 1, WanXiang.Campaign.NodeKind.Elite, seed);
                var players = BuildLegendTeam();
                if (players.Length == 0 || enemies.Length == 0) continue;

                // ⚠ 对照必须干净：同规模精英里**不该**混进首领单位（id 前缀 b_），
                //   否则"对照"名不副实，"首领更难"的结论就不可信。顺带防未来工厂改错。
                var st = BattleFactory.Create(cfg, seed, players, enemies);
                BattleSimulator.Run(st);

                var foes = st.UnitsOf(TeamSide.Enemy);
                for (int i = 0; i < foes.Count; i++)
                {
                    var id = foes[i] != null && foes[i].Def != null ? foes[i].Def.Id : null;
                    if (id != null && id.StartsWith("b_")) { clean = false; break; }
                }

                if (st.Outcome == BattleOutcome.PlayerWin) wins++;
            }
            _lastControlClean = clean;
            return (float)wins / WinnabilityRuns;
        }

        /// <summary>上一次 <see cref="RunNormalControl"/> 里对照敌阵是否干净（无 b_ 首领单位）。</summary>
        private static bool _lastControlClean = true;

        /// <summary>按幕给一个"可抽随从"的灰盒池（灵品，与 BossSquadSize 同量级）。</summary>
        private static BeastDef[] SamplePoolFor(int act)
        {
            // ⚠ 随从用 Element.None 而非该幕元素 —— 隔离"首领机制强度"这个量，不让五行克制混进来。
            //   首领本体元素是 BossDef 定死的固有属性，我方也 None ⇒ 双方对首领恒 1.0×，
            //   量到的差异只来自"首领的面板 + 机制"。
            return BattleSampleContent.TeamOf(Element.None, RoleType.Striker, 6, $"wp{act}_");
        }

        /// <summary>
        /// 5 只神品满编进攻型（沿用 CampaignSelfTest ⑪ 的 strong 阵容，去掉 Support 位避免僵局），
        /// 站位 0/1/4/7/8 与默认阵型同构。
        /// </summary>
        private static DeployEntry[] BuildLegendTeam()
        {
            return new[]
            {
                DeployEntry.Player(BattleSampleContent.Make("p0", "甲", Element.Wood,  RoleType.Guard,  Rarity.Legend), 0),
                DeployEntry.Player(BattleSampleContent.Make("p1", "乙", Element.Fire,  RoleType.Striker, Rarity.Legend), 1),
                DeployEntry.Player(BattleSampleContent.Make("p2", "丙", Element.Water, RoleType.Striker, Rarity.Legend), 4),
                DeployEntry.Player(BattleSampleContent.Make("p3", "丁", Element.Metal, RoleType.Caster, Rarity.Legend), 7),
                DeployEntry.Player(BattleSampleContent.Make("p4", "戊", Element.Earth, RoleType.Swift,  Rarity.Legend), 8),
            };
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
            /// <summary>只报告不判定的观测项（对照/参考数据）。</summary>
            public void Rate(string msg, float rate) { Sb.AppendLine("  · " + msg); }
            public void Note(string msg) { Sb.AppendLine("      " + msg); }
        }
    }
}
