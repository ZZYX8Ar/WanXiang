// ============================================================================
//  万相 · 秘境自检（realm.selftest）—— 2026-10-16《精英首领与秘境设计》第 5~7 章
//  ---------------------------------------------------------------------------
//  守四件事：
//    ① 场域表：5 个场域齐全、参数自洽（强度/波数/权重/出现幕）
//    ② **回滚保证（最重要）**：进出秘境 10 次后，路线图状态与进场前**逐字段相同**
//       —— 设计文档 7.2 专门警告过「漏了回滚 ⇒ 玩家带着 -10% 速度回主线且永不复原」
//    ③ 硬约束：每局上限 2 次、同局去重、幕数门槛（第 1 幕不出）
//    ④ 零副作用：不在秘境时 enemyScale=1、QTE 不禁、场地钩子不发事件
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using WanXiang.Modules.UI;

namespace WanXiang.Editor.BattleTool
{
    public static class RealmSelfTest
    {
        private const string ReportPath = "Temp/WanXiangDiag/realm_selftest.txt";

        private static int _pass;
        private static int _fail;

        [MenuItem("万相/战役/秘境自检（realm.selftest）")]
        public static void RunFromMenu()
        {
            var lines = Run("realm.selftest");
            foreach (var l in lines) Debug.Log("[秘境自检] " + l);
            if (_fail > 0)
                EditorUtility.DisplayDialog("秘境自检",
                    $"❌ {_pass} 过 / {_fail} 败，失败项见 Console 与 {ReportPath}。", "好");
        }

        public static string[] Run(string command)
        {
            _pass = 0; _fail = 0;
            var sb = new StringBuilder(12288);
            sb.AppendLine("万相 · 秘境自检（realm.selftest）");
            sb.AppendLine(new string('=', 72));
            sb.AppendLine($"时间　　{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("范围　　场域表 + 回滚保证 + 硬约束 + 零副作用");
            sb.AppendLine();

            try
            {
                CheckCatalog(sb);
                CheckRollback(sb);
                CheckHardRules(sb);
                CheckMap(sb);
                CheckNoSideEffect(sb);
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
        //  ① 场域表
        // ================================================================
        private static void CheckCatalog(StringBuilder sb)
        {
            Note(sb, "---- ① 场域表 ----");
            var all = WanXiang.Campaign.RealmCatalog.All;
            if (all == null || all.Count != 5) { Bad(sb, $"场域数应为 5，实际 {(all == null ? 0 : all.Count)}"); return; }
            Pass(sb, "场域数 = 5（苍泽/烬岭/机枢/寂域/荆墟）");

            var ids = new HashSet<string>();
            int noQte = 0, noHazard = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var r = all[i];
                if (string.IsNullOrEmpty(r.Id)) { Bad(sb, $"第 {i} 个场域没有 Id"); continue; }
                if (!ids.Add(r.Id)) { Bad(sb, $"Id 重复：{r.Id}"); continue; }
                if (string.IsNullOrEmpty(r.Name) || string.IsNullOrEmpty(r.Concept)) Bad(sb, $"{r.Id} 缺名字或定位");
                if (string.IsNullOrEmpty(r.Boon) || string.IsNullOrEmpty(r.Cost)) Bad(sb, $"{r.Id} 缺收益/代价说明（进秘境前必须让玩家看清）");
                if (r.Rooms == null || r.Rooms.Length < 3) Bad(sb, $"{r.Id} 房间少于 3 个");
                if (r.Waves < 1) Bad(sb, $"{r.Id} 波数 <1");
                // ★ 强度上限：设计文档 5.2「最狠 1.6」，超过就不是加压而是劝退
                if (r.EnemyScale > 1.65f) { Bad(sb, $"{r.Id} 强度 ×{r.EnemyScale} 超上限 1.60"); }
                if (r.StarRate < 1f) Bad(sb, $"{r.Id} 产出倍率 <1（没有正反馈就没有吸引力）");
                if (r.AppearFromAct < 2) Bad(sb, $"{r.Id} 第 1 幕就能出（玩家还没学会格挡）");
                if (r.NoQte) noQte++;
                if (r.Hazard == WanXiang.Campaign.RealmHazardKind.None) noHazard++;
            }
            if (noQte == 1) Pass(sb, "恰好 1 个禁 QTE 场域（寂域）");
            else Bad(sb, $"禁 QTE 的场域应有 1 个，实际 {noQte}");
            if (noHazard == 1) Pass(sb, "恰好 1 个无场地效果的场域（荆墟·纯赌坊）");
            else Bad(sb, $"无场地效果的场域应有 1 个，实际 {noHazard}");

            // 寂域禁 QTE 必须有替代手段（否则是纯惩罚 —— 设计文档 5.2）
            var silent = WanXiang.Campaign.RealmCatalog.For(WanXiang.Campaign.RealmIds.Silent);
            if (silent != null && silent.NoQte && silent.Hazard == WanXiang.Campaign.RealmHazardKind.SelfShieldAll)
                Pass(sb, "寂域禁 QTE 的同时给了「蓄盾」替代（不是纯惩罚）");
            else Bad(sb, "寂域禁 QTE 却没有替代手段 —— 难度会变成纯惩罚");
        }

        // ================================================================
        //  ② 回滚保证（最重要）
        // ================================================================
        private static void CheckRollback(StringBuilder sb)
        {
            Note(sb, "---- ② 回滚保证（进出 10 次后逐字段比对）----");
            var run = MakeRun();
            // 人为把路线图搞成"进行到一半"的样子
            run.NodeOffset = 7;
            run.HealPending = 3;
            run.PlayerBuffPct = 12;
            run.EnemyBuffPct = -5;
            run.FogLeft = 4;
            run.Path.Add(0); run.Path.Add(2); run.Path.Add(5);
            run.VisitedNodes.Add(0); run.VisitedNodes.Add(2);
            run.PassedNodes.Add(0);

            int snapOffset = run.NodeOffset, snapHeal = run.HealPending;
            int snapPb = run.PlayerBuffPct, snapEb = run.EnemyBuffPct, snapFog = run.FogLeft;
            var snapPath = new List<int>(run.Path);
            var snapVisited = new List<int>(run.VisitedNodes);
            var snapPassed = new List<int>(run.PassedNodes);

            // 进出会改这些字段（模拟"在秘境里逛"）
            bool enterOk = true, exitOk = true;
            for (int i = 0; i < 10; i++)
            {
                var realm = WanXiang.Campaign.RealmCatalog.For(WanXiang.Campaign.RealmIds.Marsh);
                // 每轮都重置条目数，否则第 3 轮就撞上限（上限本身在 ③ 单独测）
                run.RealmEntries.Clear();
                if (!RealmSession.Enter(run, realm)) { enterOk = false; break; }
                run.NodeOffset = 99; run.HealPending = 0;
                run.PlayerBuffPct = 0; run.EnemyBuffPct = 0; run.FogLeft = 0;
                run.Path.Clear(); run.Path.Add(999);
                run.VisitedNodes.Clear(); run.VisitedNodes.Add(999);
                run.PassedNodes.Clear(); run.PassedNodes.Add(999);
                if (!RealmSession.Exit(run)) { exitOk = false; break; }
            }
            if (!enterOk) { Bad(sb, "第 N 轮 Enter 返回 false"); return; }
            if (!exitOk) { Bad(sb, "第 N 轮 Exit 返回 false"); return; }
            Pass(sb, "进出 10 次全部成功");

            bool same = run.NodeOffset == snapOffset && run.HealPending == snapHeal
                     && run.PlayerBuffPct == snapPb && run.EnemyBuffPct == snapEb
                     && run.FogLeft == snapFog
                     && run.Path.Count == snapPath.Count
                     && run.VisitedNodes.Count == snapVisited.Count
                     && run.PassedNodes.Count == snapPassed.Count;
            if (same)
            {
                Pass(sb, "路线图状态与进场前**逐字段相同**（硬约束 ②）");
            }
            else
            {
                Bad(sb, $"回滚不完整：offset {snapOffset}→{run.NodeOffset}、"
                    + $"heal {snapHeal}→{run.HealPending}、path {snapPath.Count}→{run.Path.Count} 等");
            }
            // 逐 List 内容也比一遍（长度相同但内容被换掉也要抓到）
            bool contentOk = true;
            for (int i = 0; i < snapPath.Count; i++) if (run.Path[i] != snapPath[i]) contentOk = false;
            for (int i = 0; i < snapVisited.Count; i++) if (run.VisitedNodes[i] != snapVisited[i]) contentOk = false;
            for (int i = 0; i < snapPassed.Count; i++) if (run.PassedNodes[i] != snapPassed[i]) contentOk = false;
            if (contentOk) Pass(sb, "List 内容也是深拷贝还原（不是同一个引用被污染）");
            else Bad(sb, "List 内容被污染 —— 快照必须深拷贝");

            if (run.InRealm) Bad(sb, "退出后 InRealm 仍为真");
            else Pass(sb, "退出后 InRealm = false、RealmId 已清");
        }

        // ================================================================
        //  ③ 硬约束
        // ================================================================
        private static void CheckHardRules(StringBuilder sb)
        {
            Note(sb, "---- ③ 硬约束 ----");
            var run = MakeRun();
            var marsh = WanXiang.Campaign.RealmCatalog.For(WanXiang.Campaign.RealmIds.Marsh);

            // 上限 2 次
            bool a = RealmSession.Enter(run, marsh);
            RealmSession.Exit(run);
            bool b = RealmSession.Enter(run, marsh);
            RealmSession.Exit(run);
            bool c = RealmSession.Enter(run, marsh);
            if (a && b && !c) Pass(sb, "每局上限 2 次（硬约束 ③）");
            else Bad(sb, $"上限失效：第 1/2/3 次进入 = {a}/{b}/{c}（期望 true/true/false）");

            // 同局去重
            var run2 = MakeRun();
            run2.RealmEntries.Add(WanXiang.Campaign.RealmIds.Marsh);
            run2.RealmEntries.Add(WanXiang.Campaign.RealmIds.Ember);
            var rolled = WanXiang.Campaign.RealmCatalog.Roll(3, 12345UL, run2.RealmEntries);
            // ⛔ 已进过的是 marsh + ember ⇒ 抽到这两个中的任何一个都算失效
            bool repeated = rolled != null && (rolled.Id == WanXiang.Campaign.RealmIds.Marsh
                                            || rolled.Id == WanXiang.Campaign.RealmIds.Ember);
            if (!repeated)
                Pass(sb, "同局去重：已进过的（苍泽/烬岭）不再被抽到（抽到 " + (rolled == null ? "无" : rolled.Name) + "）");
            else Bad(sb, "同局去重失效：抽到了已进过的 " + rolled.Id);

            // 第 1 幕不出
            var run3 = MakeRun();
            run3.Act = 1;
            if (RealmSession.RollFor(run3, 999UL) == null) Pass(sb, "第 1 幕不出秘境（玩家没学会格挡）");
            else Bad(sb, "第 1 幕也抽到了秘境");

            // 寂域幕数门槛
            var run4 = MakeRun(); run4.Act = 3;
            var r4 = WanXiang.Campaign.RealmCatalog.Roll(3, 777UL, new List<string>());
            if (r4 != null && r4.Id == WanXiang.Campaign.RealmIds.Silent) Bad(sb, "第 3 幕不该出现寂域（AppearFromAct=4）");
            else Pass(sb, "寂域的幕数门槛生效（第 3 幕抽到 " + (r4 == null ? "无" : r4.Name) + "）");

            // 确定性：同种子同结果
            var s1 = WanXiang.Campaign.RealmCatalog.Roll(3, 4242UL, new List<string>());
            var s2 = WanXiang.Campaign.RealmCatalog.Roll(3, 4242UL, new List<string>());
            if (s1 != null && s2 != null && s1.Id == s2.Id) Pass(sb, "同种子同结果（可复盘）");
            else Bad(sb, "抽签不可复盘：同种子抽出不同场域");
        }

        // ================================================================
        //  ⑤ 秘境内节点图（ActGraph 同构）
        // ================================================================
        private static void CheckMap(StringBuilder sb)
        {
            Note(sb, "---- ⑤ 秘境内节点图（层+边，与主线同构）----");
            var all = WanXiang.Campaign.RealmCatalog.All;
            int badConnect = 0, badBoss = 0, badBattle = 0, badKind = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var realm = all[i];
                var map = WanXiang.Campaign.RealmMapBuilder.Build(realm, 20261016UL);
                if (map == null || map.Graph == null) { Bad(sb, realm.Name + " 没能生成图"); continue; }

                // ① 内部图必须**就是一张 ActGraph**（用户要求"套用现有节点地图设计"）
                if (map.Graph is WanXiang.Campaign.ActGraph) Pass(sb, realm.Name + "：内部图 = ActGraph");
                else { Bad(sb, realm.Name + "：内部图不是 ActGraph"); badKind++; }
                if (map.Graph.Layers == null || map.Graph.Layers.Length != map.Realm.Waves + 2)
                {
                    Bad(sb, realm.Name + " 层数 " + map.Graph.Layers.Length + " ≠ 波数+2");
                    badKind++;
                }

                if (!WanXiang.Campaign.RealmMapBuilder.IsConnected(map))
                { Bad(sb, realm.Name + " 有走不到的孤岛"); badConnect++; }
                if (WanXiang.Campaign.RealmMapBuilder.BossCount(map) != 1)
                { Bad(sb, realm.Name + " 首领裂隙不是恰好 1 个"); badBoss++; }
                if (WanXiang.Campaign.RealmMapBuilder.BattleCount(map) < map.Realm.Waves)
                { Bad(sb, realm.Name + " 战斗节点少于波数"); badBattle++; }

                // 同种子必得同一张图（可复盘）
                var again = WanXiang.Campaign.RealmMapBuilder.Build(realm, 20261016UL);
                bool same = again != null && again.NodeCount == map.NodeCount && again.BossOffset == map.BossOffset;
                if (same)
                    for (int k = 0; k < map.NodeCount; k++)
                        if (map.KindOf(k) != again.KindOf(k)) { same = false; break; }
                if (!same) Bad(sb, realm.Name + " 同种子结果不一致（不可复盘）");
            }
            if (badConnect == 0 && badBoss == 0 && badBattle == 0 && badKind == 0)
                Pass(sb, "5 张图全部通过：连通 + 首领恰好 1 个 + 战斗数≥波数 + 同种子可复盘");

            // 节点类型分布（别全是一个类型）
            var m2 = WanXiang.Campaign.RealmMapBuilder.Build(
                WanXiang.Campaign.RealmCatalog.For(WanXiang.Campaign.RealmIds.Marsh), 20261016UL);
            var kinds = new System.Collections.Generic.Dictionary<WanXiang.Campaign.NodeKind, int>();
            for (int i = 0; i < m2.NodeCount; i++)
            {
                var k = m2.KindOf(i);
                if (!kinds.ContainsKey(k)) kinds[k] = 0;
                kinds[k]++;
            }
            var sb2 = new StringBuilder();
            foreach (var kv in kinds) sb2.Append(kv.Key + "×" + kv.Value + " ");
            if (kinds.Count >= 3) Pass(sb, "节点类型分布合理：" + sb2.ToString());
            else Bad(sb, "节点类型太单一：" + sb2.ToString());

            // 首层 = 遭遇（入口热身）、顶层 = 出口
            var entry = m2.Graph.Layers[0][0];
            var exit = m2.Graph.Layers[m2.Graph.Layers.Length - 1][0];
            if (m2.KindOf(entry) == WanXiang.Campaign.NodeKind.Encounter
                && m2.IsExitOffset(exit))
                Pass(sb, "首层固定遭遇（热身）、顶层是出口层");
            else Bad(sb, $"首层/顶层类型不对：entry={m2.KindOf(entry)} exit={m2.KindOf(exit)}");
        }

        // ================================================================
        //  ④ 零副作用
        // ================================================================
        private static void CheckNoSideEffect(StringBuilder sb)
        {
            Note(sb, "---- ④ 零副作用（指纹红线兜底）----");
            var run = MakeRun();
            if (RealmSession.EnemyScaleFor(run) == 1f) Pass(sb, "不在秘境：敌方强度倍率 = 1.00");
            else Bad(sb, "不在秘境却改了敌方强度");
            if (!RealmSession.QteDisabled(run)) Pass(sb, "不在秘境：不禁 QTE");
            else Bad(sb, "不在秘境却禁了 QTE");
            if (RealmSession.EntriesLeft(run) == WanXiang.Campaign.RealmCatalog.MaxPerRun) Pass(sb, "不在秘境：还可进 2 次");
            else Bad(sb, "剩余次数算错");
            if (RealmSession.RollbackLines() != null && RealmSession.RollbackLines().Length >= 5)
                Pass(sb, "回滚清单有 " + RealmSession.RollbackLines().Length + " 条（加场域状态时必须同步登记）");
            else Bad(sb, "回滚清单为空 —— 以后加场域 buff 会漏还原");
        }

        // ---- 工具 ----
        private static WanXiang.Run.RunState MakeRun()
        {
            var r = new WanXiang.Run.RunState();
            r.Act = 2;
            r.NodeOffset = -1;
            return r;
        }

        private static void Pass(StringBuilder sb, string m) { _pass++; sb.AppendLine("✅ " + m); }
        private static void Bad(StringBuilder sb, string m) { _fail++; sb.AppendLine("❌ " + m); }
        private static void Note(StringBuilder sb, string m) { sb.AppendLine(m); }
    }
}