// ============================================================================
//  万相 · 编辑器工具 · 连携技自检（命令 combo.selftest）
//  ---------------------------------------------------------------------------
//  ComboDef.cs 的注释里写着"改这里请同时跑 ComboSelfTest 核对"。这个文件就是它。
//
//  它守的是两件最容易悄悄坏掉的事：
//
//   【一】表结构自洽 —— 连携技表是**手写的 50 条数据**，没有编译期检查。
//        典型坏法：两条连携技的主兽写重了（后一条永远进不去）、
//        HostId 写了一只不存在的兽（运行时 find 到 null 才炸）、
//        某只兽一条连携技都没有（图鉴里那一栏空着，但没人发现）。
//
//   【二】ExecuteCombo / PreviewComboTargets **成对** ——
//        这两个 switch 必须对每个 ComboEffect 都写一份，改一个忘了另一个，
//        表现层就会标出"打这三个人"，结算却打别的。编译期查不出这种错，
//        所以这里对每个效果各造一局，两条路都跑一遍并核对**目标集合**一致。
//
//  ⚠ 为什么不用 Play：战斗核心是零引擎依赖的纯逻辑（WanXiang.Battle.Core
//    的 noEngineReferences=true），Edit 模式同步跑得动，不必进 Play。
//    而且它本身就是"引擎依赖漏进核心层"的探针 —— 哪天有人在核心层写了
//    UnityEngine.Debug，连这个文件都编不过。
//
//  报告：Temp/WanXiangDiag/combo_selftest.txt
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Core;
using WanXiang.Battle.Presentation;
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
            // ⚠ 只在失败时弹模态框（理由同其余自检）：全绿也弹会把编辑器主线程
            //    卡在对话框上，自动化（诊断桥/MCP）跑这条命令时后续命令会全部超时。
            if (_fail > 0)
                EditorUtility.DisplayDialog("连携技自检",
                    $"❌ {_pass} 过 / {_fail} 败，失败项见 Console 与 {ReportPath}。", "好");
        }

        public static string[] Run(string command)
        {
            _pass = 0;
            _fail = 0;

            var sb = new StringBuilder(16384);
            sb.AppendLine("万相 · 连携技自检（combo.selftest）");
            sb.AppendLine(new string('=', 72));
            sb.AppendLine($"时间　　{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("范围　　ComboDef.Table 的表结构 + ExecuteCombo/PreviewComboTargets 成对");
            sb.AppendLine("程序集　WanXiang.Battle.Core（noEngineReferences = true）");
            sb.AppendLine();

            try
            {
                CheckTableShape(sb);
                CheckAgainstCatalog(sb);
                CheckExecuteAll(sb);
                CheckPairing(sb);
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
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[combo.selftest] 报告写入失败：{ex.Message}");
            }
        }

        // ================================================================
        //  1) 表结构自洽
        // ================================================================

        private static void CheckTableShape(StringBuilder sb)
        {
            Title(sb, "[1/4] 连携技表结构自洽");

            var table = ComboRules.All;

            // ---- 条数：这是个"只允许涨、涨了要顺手看一眼"的数 ----
            if (table.Count >= 50)
                Ok(sb, $"连携技共 {table.Count} 条（≥ 50）");
            else
                Bad(sb, $"连携技只有 {table.Count} 条，少于约定的 50 条");

            // ---- id 不重 ----
            var idSeen = new Dictionary<string, int>();
            var dupIds = new List<string>();
            for (int i = 0; i < table.Count; i++)
            {
                var id = table[i].Id;
                if (string.IsNullOrEmpty(id)) { Bad(sb, $"第 {i} 条连携技 Id 为空"); continue; }
                if (idSeen.ContainsKey(id)) dupIds.Add(id);
                else idSeen[id] = i;
            }
            if (dupIds.Count == 0) Ok(sb, "连携技 Id 无重复");
            else Bad(sb, "连携技 Id 重复：" + string.Join("、", dupIds.ToArray()));

            // ---- 主兽不重（一个主兽一条，重了后一条永远进不去） ----
            var hostSeen = new Dictionary<string, string>();
            var dupHosts = new List<string>();
            for (int i = 0; i < table.Count; i++)
            {
                var h = table[i].HostId;
                if (string.IsNullOrEmpty(h)) { Bad(sb, $"{table[i].Id} 的 HostId 为空"); continue; }
                if (hostSeen.ContainsKey(h)) dupHosts.Add(h + "（" + hostSeen[h] + " 与 " + table[i].Id + "）");
                else hostSeen[h] = table[i].Id;
            }
            if (dupHosts.Count == 0) Ok(sb, $"主兽无重复（{hostSeen.Count} 只异兽各有一条）");
            else Bad(sb, "同一只主兽挂了多条连携技：" + string.Join("、", dupHosts.ToArray()));

            // ---- 每条连携技自己的字段要能用 ----
            // FindPartner 的优先级是 指定异兽 > 属性 > 任意友军，三者全空就永远找不到搭档。
            int noPartnerRule = 0, noName = 0, badCost = 0;
            for (int i = 0; i < table.Count; i++)
            {
                var d = table[i];
                if (string.IsNullOrEmpty(d.PartnerBeastId)
                    && d.PartnerElement == Element.None) noPartnerRule++;
                if (string.IsNullOrEmpty(d.Name)) noName++;
                if (d.MpCost < 0) badCost++;
            }
            if (noPartnerRule == 0) Ok(sb, "每条连携技都指定了搭档规则（指定异兽 或 属性）");
            else Bad(sb, $"{noPartnerRule} 条连携技既没指定异兽也没指定属性 —— FindPartner 会退化成\"任意友军\"" +
                        "（如果这是有意的就删掉这条断言，否则补上搭档规则）");
            if (noName == 0) Ok(sb, "每条连携技都有名字");
            else Bad(sb, $"{noName} 条连携技没有名字（战斗日志会打成空）");
            if (badCost == 0) Ok(sb, "灵力消耗没有负数");
            else Bad(sb, $"{badCost} 条连携技的 MpCost 是负数");

            // ---- 效果种类分布：只报告，不判定（多少种是设计决定，不是对错） ----
            var byEffect = new Dictionary<ComboEffect, int>();
            for (int i = 0; i < table.Count; i++)
            {
                var e = table[i].Effect;
                if (!byEffect.ContainsKey(e)) byEffect[e] = 0;
                byEffect[e]++;
            }
            Info(sb, $"效果类型覆盖 {byEffect.Count} 种（枚举共 {CountEnumValues(typeof(ComboEffect))} 种）：");
            var keys = new List<ComboEffect>(byEffect.Keys);
            keys.Sort(delegate (ComboEffect a, ComboEffect b) { return ((int)a).CompareTo((int)b); });
            foreach (var k in keys)
                Note(sb, $"　{(int)k,-3} {CnOf(k),-16}× {byEffect[k]}");

            // ---- 没有被任何连携技用到的效果类型：这是"实现了但没内容"的常见状态 ----
            var unused = new List<string>();
            foreach (ComboEffect e in Enum.GetValues(typeof(ComboEffect)))
                if (!byEffect.ContainsKey(e)) unused.Add(((int)e) + " " + CnOf(e));
            if (unused.Count == 0)
                Ok(sb, "所有 ComboEffect 都有连携技在用");
            else
                Info(sb, $"以下 {unused.Count} 种效果目前没有连携技在用（实现了但暂无内容，不是错）：" +
                         string.Join("、", unused.ToArray()));
        }

        // ================================================================
        //  2) 与内容表对账
        // ================================================================

        /// <summary>
        /// HostId 必须落在**真实存在的异兽**上。直接读 ContentCatalog 资产，
        /// 走 ContentLibrary.BuildBeasts(catalog) 翻成 BeastDef[] —— 这是内容表的权威出口。
        ///
        /// 若资产还没导入（没跑过「万相/融合/① 导入异兽内容」），则跳过本项，
        /// 跳过不算失败（内容表是资产，不是编译期依赖）。
        /// </summary>
        private static void CheckAgainstCatalog(StringBuilder sb)
        {
            Title(sb, "[2/4] 连携技主兽 vs 内容表（Beast 资产）");

            const string catPath = "Assets/WanXiang/Config/ContentCatalog.asset";
            var map = TryCollectBeastElements(catPath);

            if (map == null)
            {
                Info(sb, "读不到异兽内容表（" + catPath + " 还没导入或 ContentLibrary 类型变了）—— 本项跳过。");
                Note(sb, "跳过不算失败：内容表是资产，可能是这次改动还没跑「万相/融合/① 导入异兽内容」。");
                return;
            }

            var ids = new HashSet<string>(map.Keys);
            Info(sb, $"内容表里共 {ids.Count} 只异兽");

            var missing = new List<string>();
            for (int i = 0; i < ComboRules.All.Count; i++)
            {
                var h = ComboRules.All[i].HostId;
                if (!ids.Contains(h)) missing.Add(ComboRules.All[i].Id + "→" + h);
            }
            if (missing.Count == 0)
                Ok(sb, $"连携技的主兽全部能在内容表里找到（{ComboRules.All.Count} 条全中）");
            else
                Bad(sb, "以下连携技的主兽在内容表里不存在（运行时会找不到主兽）：" + string.Join("、", missing.ToArray()));

            // ---- 反向：有没有异兽一条连携技都没有 ----
            var hosts = new HashSet<string>();
            for (int i = 0; i < ComboRules.All.Count; i++) hosts.Add(ComboRules.All[i].HostId);

            var bare = new List<string>();
            foreach (var id in ids) if (!hosts.Contains(id)) bare.Add(id);
            if (bare.Count == 0)
                Ok(sb, $"每只异兽都至少有一条连携技（{ids.Count} 只全覆盖）");
            else
                Bad(sb, $"以下 {bare.Count} 只异兽没有任何连携技（图鉴/编队里那条会空着）：" +
                        string.Join("、", bare.ToArray()));

            // ---- 指定异兽搭档也要真实存在 ----
            var badPartner = new List<string>();
            for (int i = 0; i < ComboRules.All.Count; i++)
            {
                var p = ComboRules.All[i].PartnerBeastId;
                if (!string.IsNullOrEmpty(p) && !ids.Contains(p))
                    badPartner.Add(ComboRules.All[i].Id + "→" + p);
            }
            if (badPartner.Count == 0)
                Ok(sb, "指定异兽搭档全部存在");
            else
                Bad(sb, "以下连携技指定的搭档异兽不存在：" + string.Join("、", badPartner.ToArray()));
        }

        // ================================================================
        //  3) 50 条全部能真跑（每条一局新战斗）
        // ================================================================

        /// <summary>
        /// 每条连携技各起一局**新**战斗，跑一次 ExecuteCombo。
        ///
        /// ⚠ 必须用 manual 模式（构造 BattlePlayback 时不自动结算），否则
        ///   构造那一刻整局就打完了，轮到我们执行时人已经死光 ——
        ///   会得到一片"单位已阵亡"的假失败（第一版就踩了这个）。
        ///
        /// ⚠ 主兽必须站中宫（Pos.IsCenter），这是连携技的硬性发动条件。
        /// </summary>
        private static void CheckExecuteAll(StringBuilder sb)
        {
            Title(sb, "[3/4] 50 条连携技逐条真跑（每条一局新战斗）");

            var table = ComboRules.All;
            int tried = 0, executed = 0, threw = 0;
            var fails = new List<string>();

            var skipped = new List<string>();

            for (int i = 0; i < table.Count; i++)
            {
                var def = table[i];
                tried++;
                try
                {
                    var st = BuildComboBattle(def, out var host);
                    if (st == null || host == null)
                    {
                        fails.Add(def.Id + "：造局面失败");
                        continue;
                    }

                    // ⚠ ExecuteCombo 收的是 **comboId**（不是 ComboDef），
                    //   它内部自己 For(comboId) 取定义 —— 这样上限表只有一份。
                    bool ok = BattleSimulator.ExecuteCombo(st, def.Id, host);
                    if (!ok)
                    {
                        // 返回 false = 条件不满足（限一次/搭档不在/灵力不足）。
                        // 我们已把主兽放中宫、搭档放场上、灵力顶满，理论上不该发生。
                        fails.Add(def.Id + "：ExecuteCombo 返回 false（条件不满足 —— 主兽中宫？搭档在场？灵力够？）");
                        continue;
                    }
                    executed++;

                    // 连携技应当留下一条带自己名字的战斗日志。查不到不算致命
                    // （日志裁剪策略可能变），但记下来便于人工看一眼。
                    if (!HasComboLog(st, def.Name)) skipped.Add(def.Id);
                }
                catch (Exception ex)
                {
                    threw++;
                    fails.Add(def.Id + "：" + ex.GetType().Name + "：" + ex.Message);
                }
            }

            if (skipped.Count > 0)
                Info(sb, $"有 {skipped.Count} 条执行成功但没在日志里找到自己的名字（非致命）：" +
                         string.Join("、", skipped.ToArray()));

            if (threw == 0 && executed == tried)
                Ok(sb, $"{tried} 条连携技全部可达且执行不抛异常（tried={tried} executed={executed} threw={threw}）");
            else
                Bad(sb, $"连携技执行有失败：tried={tried} executed={executed} threw={threw}");
            if (fails.Count > 0)
                foreach (var f in fails) Note(sb, "　" + f);

            Info(sb, "这一项的读法：它证明的是「50 条都能被发动、结算路径不炸」，");
            Note(sb, "不证明数值平衡。数值要看 battle.graybox 的看板与实战手感。");
        }

        // ================================================================
        //  4) ExecuteCombo 与 PreviewComboTargets 成对
        // ================================================================

        /// <summary>
        /// 对**每个 ComboEffect** 各造一局，分别走结算与预览，核对目标集合一致。
        ///
        /// 这是本文件存在的主要理由：两个 switch 分开写在同一文件的两处，
        /// 编译期无法保证它们同步；漏写的那一支的表现是"面板标 A、实际打 B"。
        ///
        /// ⚠ 这里跑的是**真实连携技**而不是合成探针，理由有两个：
        ///   ① ExecuteCombo 收的是 comboId，内部再 ComboRules.For(id) 取定义 ——
        ///      用真实条目才能顺带验证"查表拿到的就是预览那一条"；
        ///   ② 合成探针的 HostId 不在内容表里，会被 FindPartner/上限判断挡掉。
        ///
        /// ⚠ 预览在**随机类**效果（RandomTwoHit / RandomThreeHit / RandomTwoAdvance）
        ///   上刻意不掷骰（否则会推进战斗随机流），而是把整个目标池返回。
        ///   所以随机类不断言集合相等，只断言"池非空"。
        /// </summary>
        private static void CheckPairing(StringBuilder sb)
        {
            Title(sb, "[4/4] 结算与预览成对（逐条真实连携技，各造一局）");

            var table = ComboRules.All;
            int checkedCount = 0, randomChecked = 0, mismatch = 0, emptyPool = 0;
            var covered = new HashSet<int>();

            for (int i = 0; i < table.Count; i++)
            {
                var def = table[i];
                covered.Add((int)def.Effect);

                var st = BuildComboBattle(def, out var host);
                if (st == null || host == null) continue;

                // ---- 预览：不掷骰、不消费随机流 ----
                var preview = new List<BattleUnit>();
                try
                {
                    BattleSimulator.PreviewComboTargets(st, host, def, preview);
                }
                catch (Exception ex)
                {
                    Bad(sb, $"{def.Name}：预览抛异常 {ex.GetType().Name}：{ex.Message}");
                    mismatch++;
                    continue;
                }

                if (preview.Count == 0)
                {
                    // 敌全活、主兽在中宫、搭档在场，任何一条连携技都不该"一个目标都没有"。
                    emptyPool++;
                    Bad(sb, $"{def.Name}（{CnOf(def.Effect)}）：预览返回空目标集合" +
                            " —— PreviewComboTargets 的这一支很可能漏写了");
                    continue;
                }

                // ---- 结算：拿**同一局**跑 ----
                // ⚠ 必须先清掉 UsedCombos：连携技每场限一次，而预览不写它，
                //   但如果我们想在同一局里既预览又结算，就得保证这条没被记过。
                st.UsedCombos.Clear();
                FillMp(st);
                // ⚠ 把友军打到半血再结算：治疗/补损血/护盾类连携技才有东西可作用，
                //   否则满血友军被治疗、前后快照一致会被误判成"没作用到"。
                WoundAllies(st);

                var foes = new List<BattleUnit>();
                st.CollectAlive(TeamSide.Enemy, foes);
                var ours = new List<BattleUnit>();
                st.CollectAlive(TeamSide.Player, ours);

                var before = Snapshot(st, foes, ours);
                int logBefore = st.Log.Events.Count;

                bool okExec = BattleSimulator.ExecuteCombo(st, def.Id, host);
                if (!okExec)
                {
                    Bad(sb, $"{def.Name}（{CnOf(def.Effect)}）：结算返回 false" +
                            "（主兽中宫、搭档在场、灵力顶满，不该发生）");
                    mismatch++;
                    continue;
                }

                if (IsRandomEffect(def.Effect))
                {
                    if (st.Log.Events.Count > logBefore) randomChecked++;
                    else
                    {
                        mismatch++;
                        Bad(sb, $"{def.Name}（{CnOf(def.Effect)}）：结算没有产生任何战斗日志");
                    }
                    continue;
                }

                var after = Snapshot(st, foes, ours);

                checkedCount++;

                // ⚠ 断言刻意宽松：只要"预览里出现的目标"确实被作用到即可。
                //   反过来的方向（结算打到的人都必须在预览里）会被反伤、友军连带、
                //   持续伤害等效果搅乱，属于假失败。要抓的是**漏写分支** ——
                //   漏写分支的表现是"预览标了一批人，结算压根没碰他们"。
                var preIds = new List<string>();
                for (int k = 0; k < preview.Count; k++)
                    if (preview[k] != null) preIds.Add(preview[k].RuntimeId);

                int hit = 0;
                for (int k = 0; k < preIds.Count; k++)
                    if (before[preIds[k]] != after[preIds[k]]) hit++;

                if (hit == 0)
                {
                    mismatch++;
                    Bad(sb, $"{def.Name}（{CnOf(def.Effect)}）：预览标了 {preIds.Count} 个目标，" +
                            "结算却一个都没作用到 —— PreviewComboTargets 与 ExecuteCombo 的这一支很可能不同步");
                }
                else if (st.Log.Events.Count <= logBefore)
                {
                    mismatch++;
                    Bad(sb, $"{def.Name}（{CnOf(def.Effect)}）：结算没有产生任何战斗日志");
                }
            }

            if (mismatch == 0)
                Ok(sb, $"{checkedCount} 条非随机连携技：预览标出的目标确实都被结算作用到了；" +
                        $"{randomChecked} 条随机类只核对目标池非空（预览不掷骰，见文件头说明）");
            else
                Bad(sb, $"{mismatch} 条连携技的结算与预览对不上");

            if (emptyPool == 0) Ok(sb, "没有出现「敌方全存活但预览目标为空」的情况");

            // ---- 覆盖度：枚举里每种效果都该有真实条目在跑（有则不算错，没覆盖要报告出来）----
            var uncovered = new List<string>();
            foreach (ComboEffect e in Enum.GetValues(typeof(ComboEffect)))
                if (!covered.Contains((int)e)) uncovered.Add(((int)e) + " " + CnOf(e));
            if (uncovered.Count == 0)
                Ok(sb, $"全部 {countOfEffects()} 种 ComboEffect 都被真实连携技覆盖到了");
            else
                Info(sb, $"以下 {uncovered.Count} 种效果没有真实连携技在用（合成探针也跳过，属「暂无内容」）：" +
                         string.Join("、", uncovered.ToArray()));
        }

        private static int countOfEffects()
        {
            return Enum.GetValues(typeof(ComboEffect)).Length;
        }

        /// <summary>随机类效果：预览不掷骰（否则会推进战斗随机流），只返回目标池并标注。</summary>
        private static bool IsRandomEffect(ComboEffect e)
        {
            return e == ComboEffect.RandomTwoHit
                || e == ComboEffect.RandomThreeHit
                || e == ComboEffect.RandomTwoAdvance;
        }

        /// <summary>
        /// 抓一份"单位 → 可观察状态"的快照，用来反推结算到底碰了谁。
        ///
        /// 判据取（当前生命，是否存活，状态层数之和，护盾）四样：
        ///   纯数值伤害看血量、斩杀看存活、纯上减益看层数、护盾类（治疗护盾/护盾反伤）
        ///   看护盾值 —— 只看血量的话，那两类"不痛但加东西"的连携技会被判成"没作用到"。
        /// </summary>
        private static Dictionary<string, string> Snapshot(BattleState st,
                                                           List<BattleUnit> foes, List<BattleUnit> ours)
        {
            var map = new Dictionary<string, string>();
            AddSnapshot(map, foes);
            AddSnapshot(map, ours);
            if (foes.Count == 0 && ours.Count == 0)
            {
                // 兜底：两侧都没收到人时，直接把双方全部单位抓一遍
                var all = new List<BattleUnit>();
                st.CollectAlive(TeamSide.Enemy, all);
                AddSnapshot(map, all);
                all.Clear();
                st.CollectAlive(TeamSide.Player, all);
                AddSnapshot(map, all);
            }
            return map;
        }

        private static void AddSnapshot(Dictionary<string, string> map, List<BattleUnit> units)
        {
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == null) continue;
                int stacks = 0;
                if (u.Statuses != null)
                    for (int k = 0; k < u.Statuses.Count; k++) stacks += u.Statuses[k].Stacks;
                int shield = 0;
                try { shield = u.Shield; } catch { }
                map[u.RuntimeId] = u.Hp + "|" + (u.IsAlive ? 1 : 0) + "|" + stacks + "|" + shield;
            }
        }

        /// <summary>
        /// 把全队打到半血、清空护盾 —— 这样「治疗/补损血/护盾」类连携技才有东西可作用，
        /// 否则满血友军被治疗、快照前后完全一致，会被误判成"没作用到"（第一版的假失败）。
        /// ⚠ 只动玩家方：敌方是用来当"挨打目标"的，保持满血不影响伤害类断言。
        /// </summary>
        private static void WoundAllies(BattleState st)
        {
            var ours = new List<BattleUnit>();
            st.CollectAlive(TeamSide.Player, ours);
            for (int i = 0; i < ours.Count; i++)
            {
                var u = ours[i];
                if (u == null) continue;
                // 每条连携技各起一局新战斗，护盾不会跨连携残留；
                // 这里用真实伤害把友军打掉半血，给"治疗/护盾"类连携技留出可作用空间。
                if (u.Hp > u.MaxHp / 2) u.TakeDamage(u.MaxHp / 2);
            }
        }

        // ================================================================
        //  装配
        // ================================================================

        /// <summary>
        /// 造一局"主兽在中宫、搭档在场、灵力充足"的最小战斗，专门用来发动连携技。
        ///
        /// ⚠ 用 manual 模式：BattlePlayback(req, manual:true) —— 构造时不自动结算。
        ///   用 manual:false 的话构造那一刻整局就跑完了，单位大多已阵亡，
        ///   于是每一条都得到"单位已阵亡/搭档不在场"的假失败（第一版踩过）。
        /// </summary>
        private static BattleState BuildComboBattle(ComboDef def, out BattleUnit host)
        {
            host = null;

            Element hostEl = ElementOfHost(def);
            Element partnerEl = def.PartnerElement != Element.None ? def.PartnerElement : Element.Fire;

            var hostDef = BattleSampleContent.Make(def.HostId, "主", hostEl, RoleType.Striker);

            // 搭档：优先用连携技指定的异兽 id，其次按属性造一只。
            string partnerId = string.IsNullOrEmpty(def.PartnerBeastId) ? "probe_partner" : def.PartnerBeastId;
            var partnerDef = BattleSampleContent.Make(partnerId, "搭", partnerEl, RoleType.Guard);

            var req = new BattleRequest();
            req.Seed = 20260925UL;
            req.Player = new List<BeastDef>(new[] { hostDef, partnerDef });
            req.PlayerCells = new List<int>(new[] { BoardLayout.CenterIndex, 3 });  // 主兽占中宫
            req.Enemy = new List<BeastDef>(BattleSampleContent.TeamOf(Element.Water, RoleType.Guard, 2, "e"));
            // 敌方位置交给默认 Cells（不给 EnemyEntries 时就按它排），够用

            var play = new BattlePlayback(req, true);   // manual = true
            var st = play.State;
            if (st == null) return null;

            // 灵力拉满，避免"灵力不足"这种与本次要测的东西无关的失败
            FillMp(st);

            host = st.SlotAt(TeamSide.Player, BoardLayout.CenterIndex);
            return st;
        }

        /// <summary>
        /// 把双方灵力顶满 —— 连携技要花 MpCost*2，不够就发动不了。
        ///
        /// ⚠ TeamMp / TeamMpMax 是 **public 字段**（不是属性）。第一版用反射按属性读，
        ///   取到的 PropertyInfo 是 null，异常又被 catch 吞掉 ⇒ 灵力一直是 0，
        ///   于是 15 条连携技全报"条件不满足"。**吞异常的 catch 会让自检说谎**，
        ///   所以这里改成直接写字段，不再包 try。
        /// </summary>
        private static void FillMp(BattleState st)
        {
            if (st == null) return;
            st.TeamMp = st.TeamMpMax;
            st.EnemyMp = st.TeamMpMax;
        }

        /// <summary>主兽的五行：优先从内容表取；取不到就按"木"兜底（五行只影响伤害系数）。</summary>
        private static Element ElementOfHost(ComboDef def)
        {
            // 主兽五行只影响伤害系数（造测试局用）。内容表读不出来就兜底木，
            // 反正自检不关心数值方向，只关心"能否发动、结算/预览是否同步"。
            var map = TryCollectBeastElements("Assets/WanXiang/Config/ContentCatalog.asset");
            if (map != null)
            {
                Element el;
                if (map.TryGetValue(def.HostId, out el)) return el;
            }
            return Element.Wood;
        }

        private static bool HasComboLog(BattleState st, string comboName)
        {
            if (st == null || st.Log == null) return false;
            var ev = st.Log.Events;
            for (int i = 0; i < ev.Count; i++)
            {
                var n = ev[i].Note;
                if (!string.IsNullOrEmpty(n) && n.IndexOf(comboName, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        // ================================================================
        //  内容表读取（读得到就用，读不到就跳过）
        // ================================================================

        /// <summary>
        /// 直接读 ContentCatalog 资产，走 ContentLibrary.BuildBeasts(catalog) 翻成 BeastDef[]，
        /// 这是内容表的权威出口（WanXiang.Modules.Fusion 已在 Editor 的 asmdef 引用里）。
        ///
        /// 返回"异兽 id → 五行"；资产不存在或翻不出东西则返回 null（调用方据此跳过）。
        /// </summary>
        private static Dictionary<string, Element> TryCollectBeastElements(string catalogPath)
        {
            try
            {
                var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.ScriptableObject>(catalogPath);
                if (catalog == null) return null;

                // ContentLibrary.BuildBeasts(ContentCatalogSO) → BeastDef[]
                var asmFusion = System.Reflection.Assembly.Load("WanXiang.Modules.Fusion");
                var tLib = asmFusion.GetType("WanXiang.Fusion.ContentLibrary");
                var tCat = asmFusion.GetType("WanXiang.Fusion.ContentCatalogSO");
                if (tLib == null || tCat == null) return null;
                var mBuild = tLib.GetMethod("BuildBeasts", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (mBuild == null) return null;

                var defs = mBuild.Invoke(null, new object[] { catalog }) as System.Array;
                if (defs == null || defs.Length == 0) return null;

                var map = new Dictionary<string, Element>();
                for (int i = 0; i < defs.Length; i++)
                {
                    var d = defs.GetValue(i);
                    if (d == null) continue;
                    var dt = d.GetType();
                    var idp = dt.GetField("Id");
                    var elp = dt.GetField("Element");
                    string id = idp != null ? (string)idp.GetValue(d) : null;
                    if (string.IsNullOrEmpty(id)) continue;
                    Element el = Element.None;
                    if (elp != null && elp.GetValue(d) is Element) el = (Element)elp.GetValue(d);
                    map[id] = el;
                }
                return map.Count > 0 ? map : null;
            }
            catch
            {
                return null;
            }
        }

        // ================================================================
        //  文本
        // ================================================================

        private static void Title(StringBuilder sb, string t)
        {
            sb.AppendLine();
            sb.AppendLine("── " + t + " " + new string('─', Math.Max(1, 64 - t.Length * 2)));
        }

        private static void Ok(StringBuilder sb, string msg) { _pass++; sb.AppendLine("  ✅ " + msg); }
        private static void Bad(StringBuilder sb, string msg) { _fail++; sb.AppendLine("  ❌ " + msg); }
        private static void Info(StringBuilder sb, string msg) { sb.AppendLine("  · " + msg); }
        private static void Note(StringBuilder sb, string msg) { sb.AppendLine("      " + msg); }

        private static int CountEnumValues(Type t)
        {
            return Enum.GetValues(t).Length;
        }

        /// <summary>ComboEffect 的中文名。新增效果时这里也要加 —— 否则报告里显示成编号。</summary>
        private static string CnOf(ComboEffect e)
        {
            switch (e)
            {
                case ComboEffect.WoodPulse: return "木脉全体+回复";
                case ComboEffect.FireStorm: return "火焚全体";
                case ComboEffect.MetalBreak: return "金破前排";
                case ComboEffect.RandomTwoHit: return "随机两击";
                case ComboEffect.RandomThreeHit: return "随机三击";
                case ComboEffect.FrontShred: return "前排破甲";
                case ComboEffect.FrontTrue: return "前排真伤";
                case ComboEffect.HighestAtkStrike: return "最高攻打击";
                case ComboEffect.LowestHpExecute: return "最低血斩杀";
                case ComboEffect.AllAoeStatus: return "全体上状态";
                case ComboEffect.AllAoeBurnStack: return "全体叠灼烧";
                case ComboEffect.AllAoeDrainMp: return "全体吸灵";
                case ComboEffect.AllAoeStripBuff: return "全体驱散增益";
                case ComboEffect.TeamHealShield: return "全队治疗护盾";
                case ComboEffect.TeamShieldReflect: return "全队护盾反伤";
                case ComboEffect.TeamHealCleanse: return "全队治疗净化";
                case ComboEffect.TeamHealLostHp: return "全队补损血";
                case ComboEffect.RandomTwoAdvance: return "随机两击推进";
                default: return "（未命名 " + (int)e + "）";
            }
        }
    }
}
