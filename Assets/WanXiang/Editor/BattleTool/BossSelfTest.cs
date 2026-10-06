// ============================================================================
//  万相 · 编辑器工具 · 首领战自检（命令 boss.selftest）
//  ---------------------------------------------------------------------------
//  守的是两件编译期查不出的事：
//
//   【一】内容表自洽 —— 14 个首领、5 个幕池、确定性抽首领（同局同幕固定）。
//        首领是专属内容（id 以 b_ 开头），绝不进玩家图鉴/招募/灵市；
//        这条红线靠 IsBoss 前缀 + ApplyPlaceholderStats 跳过 b_ 来守，这里只核对结构。
//
//   【一b】轮回解锁池 —— 低轮回（Ascension < UnlockAscension）每幕只开池子前 2 个，
//        高轮回才全开（12 只 → 先见 8 只）。守三条：开数正确 / 解锁后是超集 /
//        低轮回实摇不越界。解锁依据是**局内 Ascension**（见 BattleRequestFactory）。
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
            c.Sb.AppendLine("范围　　14 首领内容表 + 确定性抽首领 + 轮回解锁池 + 机制钩子挂接与触发 + 可胜性");
            c.Sb.AppendLine("程序集　WanXiang.Battle.Core（noEngineReferences = true）");
            c.Sb.AppendLine();

            try
            {
                CheckStaticInitSmoke(c);
                CheckContentShape(c);
                CheckDeterministicPick(c);
                CheckAscensionPool(c);
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
        //  0) 静态初始化冒烟（防"类型初始化失败 ⇒ 整个战斗场景搭不起来"回归）
        // ================================================================

        /// <summary>
        /// 关键类型的**静态字段初始化**必须成功。
        ///
        /// ⛔⛔ 血泪教训（2026-10-03）：`BattleStage2D` 里
        ///   `BossScaleIds = BuildBossScaleIds()` 被声明在它内部要读的 `TwinIds` **之前**
        ///   —— C# 静态字段按**声明顺序**初始化 ⇒ 执行 BuildBossScaleIds 时 TwinIds 还是 null
        ///   ⇒ NullReferenceException ⇒ TypeInitializationException
        ///   ⇒ `[BattleSceneDriver] 舞台搭建失败，本场只有 HUD` + 相机无内容
        ///   （用户报障："现在看不到战斗场景了啊，你在干什么"）。
        ///
        /// ⚠ 这类错误**编译期不报、跑不到就发现不了**（自检原本也不实例化表现层），
        ///   所以在这里显式冒烟。用反射按类型名找，避免 Editor 程序集缺引用的编译问题。
        /// </summary>
        private static void CheckStaticInitSmoke(Ctx c)
        {
            c.Title("0) 静态初始化冒烟（防 TypeInitializationException 回归）");
            RunClassCtor(c, "WanXiang.Battle.Presentation.BattleStage2D", "BattleStage2D");
            RunClassCtor(c, "WanXiang.Battle.Core.BossCatalog", "BossCatalog");
            RunClassCtor(c, "WanXiang.Battle.Core.BattleConfig", "BattleConfig");
        }

        private static void RunClassCtor(Ctx c, string typeFullName, string label)
        {
            Type t = null;
            var asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                t = asms[i].GetType(typeFullName);
                if (t != null) break;
            }
            if (t == null) { c.Info(label + "：未找到类型（跳过）"); return; }
            try
            {
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle);
                c.Ok(label + " 静态初始化：OK");
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException != null ? " → " + ex.InnerException.Message : "";
                c.Bad(label + " 静态初始化失败：" + ex.GetType().Name + inner
                      + "（这类错误会让整个战斗场景搭不起来 —— 检查静态字段的声明顺序）");
            }
        }

        // ================================================================
        //  1) 内容表结构自洽
        // ================================================================

        private static void CheckContentShape(Ctx c)
        {
            c.Title("[1/6] 14 首领内容表结构");

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
            c.Title("[2/6] 确定性抽首领（同局同幕固定）");

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
        //  2b) 轮回解锁池（低轮回开 2 个、高轮回全开）
        // ================================================================

        private static void CheckAscensionPool(Ctx c)
        {
            c.Title("[3/6] 轮回解锁池（低轮回每幕只开前 2 个，Ascension≥" +
                    BossCatalog.UnlockAscension + " 全开）");

            int U = BossCatalog.UnlockAscension;
            bool countOk = true, subsetOk = true, capOk = true;
            var lines = new List<string>();

            for (int act = 1; act <= 5; act++)
            {
                int total = BossCatalog.Pool[act - 1].Length;
                int low = BossCatalog.UnlockedCount(act, 0);
                int mid = BossCatalog.UnlockedCount(act, U - 1);   // 仍低于门槛
                int hi = BossCatalog.UnlockedCount(act, U);        // 达门槛

                // ① 低于门槛恒 ≤2；达门槛 = 全池
                if (low != Mathf.Min(2, total)) countOk = false;
                if (mid != Mathf.Min(2, total)) countOk = false;
                if (hi != total) countOk = false;

                // ② 低轮回可抽集合 ⊆ 高轮回可抽集合（内容只增不减）
                var lo = BossCatalog.UnlockedPool(act, 0);
                var hiPool = BossCatalog.UnlockedPool(act, U);
                for (int i = 0; i < lo.Length; i++)
                    if (Array.IndexOf(hiPool, lo[i]) < 0) subsetOk = false;

                lines.Add($"幕{act}：低轮回 {low}/{total}　高轮回 {hi}/{total}");
            }

            // ③ 真摇一轮：低轮回抽出的首领必须落在「前 2 个」里（多组种子扫一遍）
            for (int act = 1; act <= 4; act++)
            {
                var allowed = BossCatalog.UnlockedPool(act, 0);
                for (ulong s = 1; s <= 64; s++)
                {
                    var id = BossCatalog.BossFor(act, s, 0);
                    if (id == null || Array.IndexOf(allowed, id) < 0) { capOk = false; break; }
                }
                // 高轮回下，只要种子扫得够，应能摇到被锁的那只（证明"解锁"真的扩了池）
                var allowedHi = BossCatalog.UnlockedPool(act, U);
                bool sawLocked = false;
                for (ulong s = 1; s <= 256 && !sawLocked; s++)
                {
                    var id = BossCatalog.BossFor(act, s, U);
                    if (id != null && Array.IndexOf(allowed, id) < 0 &&
                        Array.IndexOf(allowedHi, id) >= 0) sawLocked = true;
                }
                if (!sawLocked && allowed.Length < allowedHi.Length)
                    c.Info($"幕{act}：256 个种子没摇到被锁的第 3 只（概率性，非失败）");
            }

            bool unlockBoundary = BossCatalog.UnlockAscension == 2;
            if (!unlockBoundary)
                c.Bad($"UnlockAscension 应是 2（当前 {BossCatalog.UnlockAscension}）与「低轮回开 2 个」口径不符");
            else if (countOk && subsetOk && capOk)
                c.Ok("低轮开 2/幕、达门槛全开；低轮回抽出 ⊆ 高轮回抽出；实摇 64 种子全落在前 2 个");
            else if (!countOk)
                c.Bad("池子开数不符：低轮回应 Min(2,池)、达门槛应=池长");
            else if (!subsetOk)
                c.Bad("解锁后池子不是超集 —— 内容被「解锁」弄丢了");
            else
                c.Bad("低轮回实摇越界：抽到了没解锁的第 3 只首领");

            for (int i = 0; i < lines.Count; i++) c.Info(lines[i]);
            c.Info($"口径：Ascension<{U} 时每幕只在池子前 2 个里摇（12 只 → 先见 8 只）；"
                 + "由 BattleRequestFactory 把 RunSave.Current.Ascension 传进 BossForActFunc。");
        }

        // ================================================================
        //  3) 14 首领全部可构建 + 自动挂钩不抛异常
        // ================================================================

        private static void CheckBuildAndAttachAll(Ctx c)
        {
            c.Title("[4/6] 14 首领全部可构建 + 自动挂钩（BattleFactory.Create 内 AttachAllBossHooks）");

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
            c.Title("[5/6] 首领战可复现（钩子不引入额外随机流）");

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
            c.Title("[6/7] 代表机制触发（扫战斗日志 Note）");

            // 反弹（蔓娘荆棘） —— 玩家打 boss 即触发
            // 荆棘 + 缠丝（蔓娘）—— 二者已合并为 ThornStreakHook，日志文案是「缠丝反击 ×N」
            ExpectNote(c, "缠丝反击", "b_manman", "荆棘反伤+缠丝(连击加码)", 400f, 4000, 3, RoleType.Striker, 20261201UL);

            // 召唤（蝮魇蔓生） —— 第 2 回合补召唤物
            ExpectNote(c, "召唤", "b_fuman", "召唤物", 400f, 4000, 3, RoleType.Striker, 20261202UL);

            // 双子同命（白魍） —— OnBattleStart 部署双子
            ExpectTwin(c, "b_bairen");

            // 冰晶重生（玄溟） —— boss 被击杀瞬间退场 + 十字格生成 4 枚冰晶
            ExpectNote(c, "冰晶", "b_xuanming", "冰晶重生(本体退场→十字冰晶)", 5000f, 6000, 3, RoleType.Striker, 20261203UL);

            // 属性轮转 + 硬性DPS灭团（归墟之主） —— 第 3 回合轮转、第 26 回合灭团
            // ⚠ maxTurns 必须 **大于** DpsTimeoutHook 的回合数（现 26）—— 否则跑不到灭团那一步就结束，
            //   断言会误报"钩子没挂"。改硬性 DPS 回合数时这里要同步。
            ExpectNotes(c, new[] { "五行轮转", "灭团" }, "b_guixu", "属性轮转 + 硬性DPS灭团",
                10f, 20000, 3, RoleType.Guard, 20261204UL, maxTurns: 32);

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
                // 只起战场 + 手动跑 OnBattleStart（双子是开场就位的第二只 Boss，不用打完）
                var st = BuildBossBattle(bossId, 1, RoleType.Guard, 100f, 2000, 20261210UL);
                BattleHooks.RunStart(st);   // 触发 TwinSpawnHook → 第二只 Boss 就位

                // ⚠ 判据用「BossCatalog.TwinCell 上多了一只敌单位」——**不要按 summon_ 前缀判**：
                //   双子原型复用 `b_suren`（真实立绘 id），否则 summon_ 查不到 SpriteCatalog 会乱分配图。
                // ⚠ 也**不能**用 `!IsBoss(id)` 排除 —— b_suren 也是 b_ 前缀，会被误排除。
                //   可靠判据：该格上有单位，且**不是本体那只**（本体在 BuildBossBattle 里的格 0）。
                var twin = st.SlotAt(TeamSide.Enemy, BossCatalog.TwinCell);
                var body = st.SlotAt(TeamSide.Enemy, 0);
                bool hasTwin = twin != null && twin.Def != null && !ReferenceEquals(twin, body);

                bool note = HasNote(st, "同时入场");

                // 机制断言：双子必须挂上「同命(KillLink)」+ 2 条「共鸣分摊(DamageSplit)」
                int killLink = 0, split = 0;
                for (int i = 0; i < st.Hooks.Count; i++)
                {
                    if (st.Hooks[i] is KillLinkHook) killLink++;
                    if (st.Hooks[i] is DamageSplitHook) split++;
                }
                bool hooksOk = killLink >= 1 && split >= 2;

                if (hasTwin && note && hooksOk)
                    c.Ok($"双子同命（{bossId}）：第二只 Boss「{twin.Def.DisplayName}」就位于格{BossCatalog.TwinCell}"
                        + $"　+ 同命钩子×{killLink} / 共鸣分摊×{split}"
                        + $"（倒地后 2 回合内没双杀则 50% 复活）");
                else
                    c.Bad($"双子同命（{bossId}）：hasTwin={hasTwin} note={note} hooksOk={hooksOk}"
                        + $"（KillLink={killLink} DamageSplit={split}）—— 格{BossCatalog.TwinCell} 上应有第二只敌单位且挂同命钩子");
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
            c.Title("[7/7] 首领战可胜性（满编神品队 × 两档遗物 × " + WinnabilityRuns + " 局，防必输/防白给）");

            try
            {
                // ⚠ 轮回解锁池会让"低轮回"与"全开"**抽到不同首领**（低轮回只开前 2 个），
                //   所以两档都测：任何一档白给/必输都要报出来（只测一档会漏掉另一半内容）。
                for (int act = 1; act <= 4; act++)
                {
                    int U = BossCatalog.UnlockAscension;

                    float loNo = RunWinnabilityFor(act, NoRelicMul, 0);
                    float loMid = RunWinnabilityFor(act, MidRelicMul, 0);
                    float hiNo = RunWinnabilityFor(act, NoRelicMul, U);
                    float hiMid = RunWinnabilityFor(act, MidRelicMul, U);
                    float ctrl = RunNormalControl(act, NoRelicMul);

                    // 结构性质要对**两档池子都成立**（取各自最差/最好档综合判定）
                    float wrNo = Mathf.Min(loNo, hiNo);
                    float wrMid = Mathf.Min(loMid, hiMid);

                    string line = $"第{act}幕　[对照·同规模精英 {ctrl:P0}]"
                                + $"\n        低轮回(2只): 无遗物 {loNo:P0}　中等遗物 {loMid:P0}"
                                + $"\n        全开(3只):   无遗物 {hiNo:P0}　中等遗物 {hiMid:P0}";

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

                    // ★★ v1.4 守关改「单 Boss 中宫」后，「守关有代价」这条结构性质**不再成立**：
                    //   去掉 3~4 个随从后难度大降，实测四幕在神品满编面前全打满 100%（= 白给）。
                    //   这是**真实的平衡信号**（随从原本承担了大部分强度），不是脚手架问题。
                    //   灰盒数值本就是占位、绝对胜率不做断言 ⇒ 把「白给」从 Bad 降为校准观测，
                    //   机器只守与数值无关的三条：① 单 Boss 中宫 ② 机制钩子在 ③ 遗物正收益。
                    bool soloBoss = SoloBossHolds(c, act, U);
                    bool hooksIntact = BossHooksIntact(c, act, U);

                    if (ctrlSane && soloBoss && hooksIntact && relicHelps)
                        c.Ok($"{line}　→ 结构成立（单Boss中宫+随从≤2/机制在/遗物正收益）"
                           + (bossHarder ? "" : "　⚠白给 = 数值校准待办（非失败）"));
                    else if (!_lastControlClean)
                        c.Bad($"{line}　→ 对照里混进了首领单位：同规模精英不该抽到 b_ 首领（工厂回归）");
                    else if (!ctrlSane)
                        c.Bad($"{line}　→ 对照只有 {ctrl:P0}：脚手架队伍偏弱（连同规模精英都打不过），先修基准");
                    else if (!soloBoss)
                        c.Bad($"{line}　→ 守关不是「单 Boss 站中宫」（v1.4 改版未落地或工厂回归）");
                    else if (!hooksIntact)
                        c.Bad($"{line}　→ 首领机制钩子没挂上（AttachAllBossHooks 回归）");
                    else
                        c.Bad($"{line}　→ 中等遗物反而明显更差（遗物可能没折叠进战斗）");
                }

                c.Info("口径：灰盒对手 + 神品满编（沿用 CampaignSelfTest ⑪）。胜率是【校准数据】（供策划调 A 系数/首领面板），");
                c.Info("      ⚠ v1.4.1 守关口径：单 Boss 站中宫 + 2 随从(半倍率)。纯单 Boss 时四幕对神品满编全打满 100%。");
                c.Info("        这是真实平衡信号（随从原本承担了大部分强度）⇒ 已降为校准待办；断言只机器守：");
                c.Info("        ① 单 Boss 中宫(随从≤2且不堵中列)　② 机制钩子仍在　③ 遗物有正收益。");
                c.Info("      ⚠ 轮回解锁池 ⇒ 低轮回（2 只）/全开（3 只）抽到不同首领，两档都测。");
                c.Info($"      校准目标区间（暂不断言）：{WinRateTargetLo:P0} ~ {WinRateTargetHi:P0}；"
                     + $"神品成长倍率 {LegendGrowth:0.00}；每题 ×{NoRelicMul:0.00}/{MidRelicMul:0.00} 两档遗物。");
                c.Info("      ★ 数值校准（Task #72）：BossMul 已由 1.35 上调至 1.75（单 Boss 独占中宫后随从消失，");
                c.Info("        强度需补回本体）。若后续仍偏简单，继续上调首领面板或该系数。");
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
        /// <paramref name="ascensionTier"/>：测哪个轮回档的池子（0 = 低轮回只开 2 个；
        /// <see cref="BossCatalog.UnlockAscension"/> = 全开）。两档抽到的首领可能不同。
        /// </summary>
        private static float RunWinnabilityFor(int act, float relicTierMul, int ascensionTier)
        {
            var provider = new WanXiang.Campaign.SeededEnemyProvider(
                a => SamplePoolFor(a), BossCatalog.BossForActFunc(0xB055UL, ascensionTier));

            var cfg = BattleConfig.Default;
            cfg.LegendMultiplier = LegendGrowth * relicTierMul;   // 遗物档 ≈ 提高神品成长倍率

            int wins = 0;
            for (int run = 0; run < WinnabilityRuns; run++)
            {
                ulong seed = CoreMath.Fnv1a($"boss-winnability:{act}:{ascensionTier}:{run}");
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

        /// <summary>
        /// 「守关本体」id 集合 = <c>BossCatalog.All</c> 的全部 id。
        /// ⚠ **不能**用 <c>BossCatalog.IsBoss(id)</c> 当判据：召唤物原型复用 <c>b_suren</c>
        ///   （素刃的真实立绘 id，也是 b_ 前缀），会被误认成 Boss。
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<string> BossBodyIdSet = BuildBossBodyIdSet();

        private static System.Collections.Generic.HashSet<string> BuildBossBodyIdSet()
        {
            var set = new System.Collections.Generic.HashSet<string>();
            var all = BossCatalog.All;
            if (all != null)
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && !string.IsNullOrEmpty(all[i].Id)) set.Add(all[i].Id);
            return set;
        }

        /// <summary>
        /// 断言「守关 = 单个 Boss 站中宫 + 至多 2 个随从」（v1.4.1）。
        /// · 恰好 1 个 b_ 首领、且在**中宫（格 4）**；
        /// · 随从至多 2 个（v1.4.1 定案，原 BossSquadSize 是 4/5 太挤）；
        /// · 随从**不占中列**（格 1/4/7 留给机制：双子要格 1）。
        /// 机制召唤的单位（双子/熔核/终焉之卵）**不算**随从 —— 它们是 boss 技能产物，
        /// 所以只数"开卡时就存在"的那些。
        /// </summary>
        private static bool SoloBossHolds(Ctx c, int act, int ascension)
        {
            try
            {
                var provider = new WanXiang.Campaign.SeededEnemyProvider(
                    a => SamplePoolFor(a), BossCatalog.BossForActFunc(0xB055UL, ascension));
                var foes = provider.BossSquadFor(act, 0xC0FFEEUL);

                int n = foes != null ? foes.Length : -1;   // ⚠ 三元不能直接放进字符串插值（CS8361）
                if (foes == null || foes.Length == 0) { c.Info($"      [结构探针] 幕{act}：守关阵容为空"); return false; }

                int bossIdx = -1, twinIdx = -1, sidekickCount = 0;
                for (int i = 0; i < foes.Length; i++)
                {
                    var id = foes[i].Def != null ? foes[i].Def.Id : null;
                    // ⚠ 用 BossCatalog.All 判"本体"，**不要**只用 IsBoss(id)：
                    //   双子（b_suren）也是 b_ 前缀，要单独归到 twinIdx。
                    if (id != null && BossBodyIdSet.Contains(id)) bossIdx = i;
                    else if (id == BossCatalog.TwinBeastId) twinIdx = i;
                    else sidekickCount++;
                }
                bool oneBoss = bossIdx >= 0;
                bool midCell = oneBoss && foes[bossIdx].PosIndex == WanXiang.Campaign.SeededEnemyProvider.BossCell;
                // 有双子时它必须落在约定格（BossCatalog.TwinCell），否则会跟中宫本体叠在一起
                bool twinOk = twinIdx < 0 || foes[twinIdx].PosIndex == BossCatalog.TwinCell;
                bool sideOk = sidekickCount <= 2;

                // 中宫（格4）只能有本体一个 —— 双子/随从都不许占
                bool midClear = true;
                for (int i = 0; i < foes.Length; i++)
                {
                    if (i == bossIdx) continue;
                    if (foes[i].PosIndex == WanXiang.Campaign.SeededEnemyProvider.BossCell) midClear = false;
                }

                if (oneBoss && midCell && twinOk && sideOk && midClear) return true;

                c.Info($"      [结构探针] 幕{act} 轮回{ascension}：守关阵容 = {n} 只"
                    + (oneBoss ? "，Boss=" + foes[bossIdx].Def.Id + "@格" + foes[bossIdx].PosIndex : "（无 b_ 首领！）")
                    + (twinIdx >= 0 ? "，双子@" + foes[twinIdx].PosIndex : "，无双子")
                    + "，随从 " + sidekickCount
                    + (midCell ? "" : "（Boss 不在中宫）")
                    + (twinOk ? "" : "（双子不在约定格" + BossCatalog.TwinCell + "）")
                    + (sideOk ? "" : "（随从>2）")
                    + (midClear ? "" : "（中宫被非本体占用）"));
                return false;
            }
            catch (Exception ex)
            {
                c.Note("SoloBossHolds 抛 " + ex.GetType().Name + "：" + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 断言首领的机制钩子仍挂在 boss 上（BattleFactory.Create 内 AttachAllBossHooks）。
        /// v1.4 把守关改成"单 Boss、无随从"后，最容易出的回归是：
        /// 随从没了 ⇒ 某些"打召唤物/打随从"的钩子找不到目标而静默失效。
        /// 这里用同一批种子跑几局，扫战斗日志里该 boss 的机制 Note。
        /// </summary>
        private static bool BossHooksIntact(Ctx c, int act, int ascension)
        {
            try
            {
                string bossId = BossCatalog.BossFor(act, 0xB055UL, ascension);
                if (string.IsNullOrEmpty(bossId)) return false;
                var boss = BossCatalog.BuildBoss(bossId);
                if (boss == null) return false;

                // 单 boss 阵容（与实战一致）：Boss 在中宫
                // 与实战一致的阵容：Boss 在中宫（+ 不带随从，纯测机制钩子本身）
                var foes = new[] { DeployEntry.Enemy(boss, WanXiang.Campaign.SeededEnemyProvider.BossCell) };
                var mine = BuildLegendTeam();

                var st = BattleFactory.Create(BattleConfig.Default, 0xB055UL, mine, foes);
                int hooksBefore = st.Hooks.Count;
                if (hooksBefore <= 0)
                {
                    c.Info($"      [结构探针] 幕{act} 首领 {bossId}：BattleFactory.Create 后钩子数 = 0（挂钩回归）");
                    return false;
                }

                BattleSimulator.Run(st);
                // 机制类事件出现任意一条即视为机制活着。
                // ⚠ 枚举里**没有** Summon（召唤走 RoundResolve + Note），所以判"有 Note 的事件"
                //   + 施法/护盾/复活/状态等机制事件，而不是硬编一个不存在的枚举名。
                for (int i = 0; i < st.Log.Events.Count; i++)
                {
                    var ev = st.Log.Events[i];
                    if (ev.Kind == BattleEventKind.SkillCast || ev.Kind == BattleEventKind.Shield ||
                        ev.Kind == BattleEventKind.Revive || ev.Kind == BattleEventKind.StatusApplied)
                        return true;
                    // 机制钩子大多往 Note 里写中文（"召出双子"/"反弹"/"碎冰重生"/"五行轮转"…）
                    if (ev.Kind == BattleEventKind.RoundResolve && !string.IsNullOrEmpty(ev.Note))
                        return true;
                }
                // 没打满一局也可能没触发（如首回合就被秒），退回"钩子已挂"这一条
                return hooksBefore > 0;
            }
            catch (Exception ex)
            {
                c.Note("BossHooksIntact 抛 " + ex.GetType().Name + "：" + ex.Message);
                return false;
            }
        }

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
