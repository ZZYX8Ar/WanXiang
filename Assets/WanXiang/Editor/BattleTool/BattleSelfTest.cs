// ============================================================================
//  万相 · 编辑器工具 · 战斗核心自检（命令 battle.selftest）
//  ---------------------------------------------------------------------------
//  它存在的意义是**把 GDD 第 7 章给 STEP 1 定的三条验收标准变成可执行的判据**：
//
//      验收① 同一份输入的战斗结果 100% 可复现（固定随机种子）
//      验收② 灰盒状态下连续看 10 场不觉得无聊          ← 机器判不了，见下面
//      验收③ 调 ElementMatrixConfig 的四个系数能立刻观察到变化
//
//  验收② 是**人的判断**，脚本只能把料备齐（打出棋盘快照 + 事件时间线 + 数据分布），
//  所以第 5 项自检的输出是"给人看的看板"，不是 pass/fail。这一点必须写在报告里，
//  否则下一个人会以为"跑绿了就等于好玩了"。
//
//  ⚠ 为什么是 Edit 模式命令而不是 Play 模式测试：
//    这台机器的 Unity 编辑器不维持 Play 模式的播放器循环（见项目记忆），
//    靠 Play 跑的自动化验收在这里不可靠。战斗核心是纯逻辑、零引擎依赖
//    （WanXiang.Battle.Core 的 noEngineReferences=true），
//    所以 Edit 模式同步跑完全可行 —— 而且它就是"引擎依赖漏进来"的探针：
//    哪天有人在核心层写了 UnityEngine.Debug，连这个文件都编不过。
//
//  报告：Temp/WanXiangDiag/battle_selftest.txt
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using WanXiang.Battle.Core;
using WanXiang.Battle.Presentation;

namespace WanXiang.Editor.BattleTool
{
    public static class BattleSelfTest
    {
        private const string ReportPath = "Temp/WanXiangDiag/battle_selftest.txt";

        public static string[] Run(string command)
        {
            var c = new Ctx();
            c.Sb.AppendLine("万相 · 战斗核心自检（battle.selftest）");
            c.Sb.AppendLine(new string('=', 72));
            c.Sb.AppendLine($"时间　　{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            c.Sb.AppendLine("范围　　GDD 第 7 章 STEP 1 · 棋盘战斗逻辑闭环");
            c.Sb.AppendLine("程序集　WanXiang.Battle.Core（noEngineReferences = true）");
            c.Sb.AppendLine();

            try
            {
                CheckMatrix(c);
                CheckDeterminism(c);
                CheckCoefficients(c);
                CheckBoardRules(c);
                CheckGrayBox(c);
                CheckPresentation(c);
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
                c.Sb.AppendLine("说明：验收①③ 由机器判定；**验收②（灰盒连续看 10 场不无聊）是人的判断**，");
                c.Sb.AppendLine("      请照着上面第 5 项的看板自己看 —— 脚本绿了不代表好玩。");
            }

            string text = c.Sb.ToString();
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
                // 写不进文件不算自检失败 —— 返回值里已经有全文
                UnityEngine.Debug.LogWarning($"[battle.selftest] 报告写入失败：{ex.Message}");
            }
        }

        // ================================================================
        //  1) 五行矩阵自洽
        // ================================================================

        private static void CheckMatrix(Ctx c)
        {
            c.Title("[1/6] 五行矩阵与相生/相克环");

            var coeff = ElementCoefficients.Default;
            string err = ElementMatrix.SelfCheck(coeff);
            if (err == null)
            {
                c.Ok("25 格系数与 GDD 2.3 的表逐格一致；两个环的代数性质（每属性克1生1被克1被生1、" +
                     "生克不同时成立、环是 5 元置换）全部成立");
            }
            else
            {
                c.Bad(err);
            }

            c.Info("推导出的 5×5 系数矩阵（行 = 攻方，列 = 守方）：");
            c.Note("　　　　木　　火　　土　　金　　水");
            foreach (var atk in ElementMatrix.All)
            {
                var line = new StringBuilder("　" + CnOf(atk) + "　");
                foreach (var def in ElementMatrix.All)
                    line.Append(ElementMatrix.Coefficient(atk, def, coeff).ToString("F2")).Append("　");
                c.Note(line.ToString());
            }

            c.Info($"四个系数：克 {coeff.Counter:F2}／被克 {coeff.Countered:F2}／" +
                   $"同属 {coeff.Same:F2}／无关 {coeff.Neutral:F2}");
            c.Note("「相生不参与伤害计算」已由 ElementMatrix.Coefficient 的实现保证" +
                   "（只读 Relation，相生在 Relation 里不产生分支）。");
        }

        // ================================================================
        //  2) 可复现性
        // ================================================================

        private static void CheckDeterminism(Ctx c)
        {
            c.Title("[2/6] 可复现性（GDD 验收①）");

            const ulong seed = 20260914UL;
            var r1 = BattleSimulator.Run(BuildStandard(seed, null));
            var r2 = BattleSimulator.Run(BuildStandard(seed, null));

            bool same = r1.Fingerprint == r2.Fingerprint
                     && r1.Outcome == r2.Outcome
                     && r1.Turns == r2.Turns
                     && r1.EventCount == r2.EventCount;

            if (same)
            {
                c.Ok($"同种子跑两次完全一致：指纹 0x{r1.Fingerprint:X8}，" +
                     $"{CnOf(r1.Outcome)}，{r1.Turns} 回合，{r1.EventCount} 条事件");
            }
            else
            {
                c.Bad($"同种子结果不一致！第一次 0x{r1.Fingerprint:X8}/{r1.Turns}回合，" +
                      $"第二次 0x{r2.Fingerprint:X8}/{r2.Turns}回合 —— " +
                      "说明有状态泄漏到 BattleState 之外，或某处遍历顺序不确定");
            }

            var r3 = BattleSimulator.Run(BuildStandard(seed + 1, null));
            if (r3.Fingerprint == r1.Fingerprint)
            {
                c.Bad($"换种子后指纹没变（0x{r3.Fingerprint:X8}）—— " +
                      "说明随机流根本没参与战局，那么「可复现」就是空的，暴击与随机目标形同虚设");
            }
            else
            {
                c.Ok($"换种子后指纹改变（0x{r3.Fingerprint:X8}），随机流确实参与战局");
            }

            c.Info($"基准局面：5v5，我方 木御@0 木攻@1 火术@4(中宫) 土辅@7 金疾@8；" +
                   "敌方 金御@0 水攻@1 火术@4(中宫) 土辅@7 水疾@8");
            c.Note("指纹覆盖事件类型/回合/施动者/受动者/数值/五行/技能槽，**不含文案 Note** ——" +
                   "改一句说明文字不应该让可复现性判据变红。");
            c.Info("这一局的棋盘结算总量（棋盘规则不是摆设，得真的在跑）：");
            c.Note(DescribeTotals(r1));
            c.Note("摆位说明：我方 1-4/4-7/7-8 三对相生（无相冲）；" +
                   "敌方 0-1/4-7 相生、7-8 相冲、1-4 相冲但落在中宫上被平息。");

            // ---- 视图帧流：表现层的唯一数据来源，必须和逻辑状态严丝合缝 ----
            {
                var st = BuildStandard(seed, null);
                BattleSimulator.Run(st);

                // 按帧累积血量，最后应当等于各自的真实血量（阵亡单位两边都要是 0）
                var hpByUnit = new System.Collections.Generic.Dictionary<string, int>();
                bool monotonic = true;
                int lastEventIndex = -1;
                for (int i = 0; i < st.Frames.Count; i++)
                {
                    var f = st.Frames[i];
                    if (f.EventIndex < lastEventIndex) { monotonic = false; break; }
                    lastEventIndex = f.EventIndex;
                    for (int k = 0; k < f.Changed.Length; k++)
                        hpByUnit[f.Changed[k].UnitId] = f.Changed[k].Hp;
                }

                int mismatched = 0;
                var units = st.AllUnits;
                for (int i = 0; i < units.Count; i++)
                {
                    int seen;
                    if (!hpByUnit.TryGetValue(units[i].RuntimeId, out seen)) seen = units[i].MaxHp;
                    if (seen != units[i].Hp) mismatched++;
                }

                if (st.Frames.Count > 0 && monotonic && mismatched == 0
                    && st.Frames[0].Changed.Length == units.Count)
                {
                    c.Ok($"视图帧流与逻辑状态一致：{st.Frames.Count} 帧（第 0 帧为 {units.Count} 个单位的全量帧），" +
                         "帧内累积出的血量与各单位终局血量逐一对上");
                }
                else
                {
                    c.Bad($"视图帧流有问题：帧数 {st.Frames.Count}、时序单调={monotonic}、" +
                          $"血量对不上的单位 {mismatched} 个、" +
                          $"第 0 帧单位数 {(st.Frames.Count > 0 ? st.Frames[0].Changed.Length : 0)}（应为 {units.Count}）" +
                          " —— 表现层会画出和逻辑不一样的画面");
                }
                c.Note("帧流是表现层的**唯一**数据来源（灰盒窗口 / 未来的 BoardView 都只读它），" +
                       "所以它必须与逻辑严丝合缝：表现层自己算规则就等于把战斗逻辑复制第二份。");
                c.Note("第 0 帧必须是**全量**帧 —— 回放是累积式的（画第 k 帧 = 叠 0..k 的差量），" +
                       "没有全量起点就不知道「第一帧之前各人是什么样」。");
            }
        }

        // ================================================================
        //  3) 系数可调、立刻可观察
        // ================================================================

        private static void CheckCoefficients(Ctx c)
        {
            c.Title("[3/6] 四个系数可调且立刻可观察（GDD 验收③）");

            // ---- 3a：直接对四种关系各算一次伤害，逐一改系数验证 ----
            var probe = BuildProbe(out var wood, out var earth, out var water);

            var baseCoef = ElementCoefficients.Default;
            var cases = new[]
            {
                // 攻击者刻意只用来打一个关系；目标五行决定是哪一种关系
                new RelCase { Name = "克（木→土）",   Src = wood,  Dst = earth, Base = baseCoef.Counter,   Changed = 3.00f },
                new RelCase { Name = "被克（土→木）", Src = earth, Dst = wood,  Base = baseCoef.Countered, Changed = 0.20f },
                new RelCase { Name = "同属（木→木）", Src = wood,  Dst = wood,  Base = baseCoef.Same,      Changed = 0.50f },
                new RelCase { Name = "无关（木→水）", Src = wood,  Dst = water, Base = baseCoef.Neutral,   Changed = 1.50f },
            };

            foreach (var rc in cases)
            {
                probe.Config.Elements = ElementCoefficients.Default;
                int before = BattleSimulator.ComputeDamage(probe, rc.Src, rc.Dst, rc.Src.Element,
                                                           1.00f, trueDamage: false, crit: false);

                var tweaked = ElementCoefficients.Default;
                switch (ElementMatrix.Relation(rc.Src.Element, rc.Dst.Element))
                {
                    case ElementRelation.Counter: tweaked.Counter = rc.Changed; break;
                    case ElementRelation.Countered: tweaked.Countered = rc.Changed; break;
                    case ElementRelation.Same: tweaked.Same = rc.Changed; break;
                    default: tweaked.Neutral = rc.Changed; break;
                }
                probe.Config.Elements = tweaked;
                int after = BattleSimulator.ComputeDamage(probe, rc.Src, rc.Dst, rc.Src.Element,
                                                          1.00f, trueDamage: false, crit: false);
                probe.Config.Elements = ElementCoefficients.Default;

                if (before == after)
                    c.Bad($"{rc.Name}：系数 {rc.Base:F2}→{rc.Changed:F2} 后伤害没变（都是 {before}）");
                else
                    c.Ok($"{rc.Name}：系数 {rc.Base:F2}→{rc.Changed:F2}，伤害 {before} → {after}");
            }

            // ---- 3b：整场战斗层面也应观察到变化 ----
            c.Info("整场战斗层面（同一局面，只改一个系数）：");
            c.Note("变体　　　　　指纹　　　　我方技能伤害　敌方技能伤害　回合　结果");
            foreach (var variant in BuildVariants())
            {
                var st = BuildStandard(20260914UL, variant.Config);
                var r = BattleSimulator.Run(st);
                c.Note($"{Pad(variant.Name, 14)}{Pad($"0x{r.Fingerprint:X8}", 14)}" +
                       $"{Pad(SkillDamage(st, "P").ToString(), 14)}{Pad(SkillDamage(st, "E").ToString(), 14)}" +
                       $"{Pad(r.Turns.ToString(), 6)}{CnOf(r.Outcome)}");
            }
        }

        private struct Variant
        {
            public string Name;
            public BattleConfig Config;
        }

        /// <summary>一次"改一个系数看伤害"的探针用例。四种关系各一条。</summary>
        private struct RelCase
        {
            public string Name;
            public BattleUnit Src;
            public BattleUnit Dst;
            public float Base;
            public float Changed;
        }

        private static Variant[] BuildVariants()
        {
            var list = new Variant[5];
            list[0] = new Variant { Name = "基准", Config = BattleConfig.Default };

            var c1 = BattleConfig.Default; c1.Elements.Counter = 3.00f;
            list[1] = new Variant { Name = "克→3.00", Config = c1 };

            var c2 = BattleConfig.Default; c2.Elements.Countered = 0.20f;
            list[2] = new Variant { Name = "被克→0.20", Config = c2 };

            var c3 = BattleConfig.Default; c3.Elements.Same = 0.50f;
            list[3] = new Variant { Name = "同属→0.50", Config = c3 };

            var c4 = BattleConfig.Default; c4.Elements.Neutral = 1.50f;
            list[4] = new Variant { Name = "无关→1.50", Config = c4 };

            return list;
        }

        // ================================================================
        //  4) 棋盘规则
        // ================================================================

        private static void CheckBoardRules(Ctx c)
        {
            c.Title("[4/6] 棋盘四条规则（GDD 2.5）");

            // ---- 4a 相生相邻 ----
            {
                var st = BuildPair(Element.Wood, Element.Fire, 0, 1, out var a, out var b);
                HurtToHalf(a); HurtToHalf(b);
                int hpBeforeA = a.Hp, hpBeforeB = b.Hp;

                var rep = BoardRules.ResolveAdjacency(st);

                bool ok = rep.GeneratePairs == 1 && rep.CounterPairs == 0
                       && a.GetStacks(StatusCatalog.Qi) == 1
                       && b.GetStacks(StatusCatalog.Qi) == 1
                       && a.Hp > hpBeforeA && b.Hp > hpBeforeB;
                if (ok) c.Ok($"相生相邻（木@0 ↔ 火@1）：回复 {rep.RegenTotal}，各得同气 1 层，" +
                             $"生命 {hpBeforeA}→{a.Hp} / {hpBeforeB}→{b.Hp}");
                else c.Bad($"相生相邻行为不符：相生 {rep.GeneratePairs} 对、相克 {rep.CounterPairs} 对、" +
                           $"同气 {a.GetStacks(StatusCatalog.Qi)}/{b.GetStacks(StatusCatalog.Qi)}、" +
                           $"生命 {hpBeforeA}→{a.Hp} / {hpBeforeB}→{b.Hp}");
            }

            // ---- 4b 相克相冲 ----
            {
                var st = BuildPair(Element.Wood, Element.Metal, 0, 1, out var a, out var b);
                HurtToHalf(a); HurtToHalf(b);
                int hpBeforeA = a.Hp, hpBeforeB = b.Hp;

                var rep = BoardRules.ResolveAdjacency(st);

                bool ok = rep.CounterPairs == 1 && rep.TrueDamageTotal > 0
                       && a.Hp < hpBeforeA && b.Hp < hpBeforeB
                       && Math.Abs(a.RageGainMultiplier - 0.90f) < 1e-4f
                       && Math.Abs(b.RageGainMultiplier - 0.90f) < 1e-4f;
                if (ok) c.Ok($"相克相冲（木@0 ↔ 金@1）：真伤 {rep.TrueDamageTotal}，" +
                             $"生命 {hpBeforeA}→{a.Hp} / {hpBeforeB}→{b.Hp}，怒气乘数 {a.RageGainMultiplier:F2}");
                else c.Bad($"相克相冲行为不符：相克 {rep.CounterPairs} 对、真伤 {rep.TrueDamageTotal}、" +
                           $"生命 {hpBeforeA}→{a.Hp} / {hpBeforeB}→{b.Hp}、" +
                           $"怒气乘数 {a.RageGainMultiplier:F2}/{b.RageGainMultiplier:F2}（应为 0.90）");
            }

            // ---- 4c 中宫平息（同一个局面，只翻一个布尔值） ----
            {
                var withCenter = BuildPair(Element.Wood, Element.Metal, BoardLayout.CenterIndex, 1,
                                           out _, out _, suppress: true);
                var repOn = BoardRules.ResolveAdjacency(withCenter);

                var noCenter = BuildPair(Element.Wood, Element.Metal, BoardLayout.CenterIndex, 1,
                                         out _, out _, suppress: false);
                var repOff = BoardRules.ResolveAdjacency(noCenter);

                if (repOn.PacifiedPairs == 1 && repOn.CounterPairs == 0
                    && repOff.PacifiedPairs == 0 && repOff.CounterPairs == 1)
                {
                    c.Ok("中宫土位平息相冲：木@4(中宫) ↔ 金@1 在开关=开时被平息、" +
                         "开关=关时正常相冲（只翻一个布尔值，不用动战斗循环）");
                }
                else
                {
                    c.Bad($"中宫平息行为不符：开={repOn.PacifiedPairs}平息/{repOn.CounterPairs}相冲，" +
                          $"关={repOff.PacifiedPairs}平息/{repOff.CounterPairs}相冲（期望 1/0 与 0/1）");
                }
                c.Note("⚠ GDD 2.5「相邻四格的单位都不会触发相冲」有两种读法，正文举例倾向" +
                       "「涉及中宫的相冲被平息」。取该读法；另一种读法（中宫周围四格互不相冲）" +
                       "实际上没有作用面，因为那四格彼此并不正交相邻。待策划确认。");
            }

            // ---- 4d 中宫土德减伤 ----
            {
                // 两只**同五行、同职业**的单位，唯一差别是站位 —— 否则"中宫伤害更低"
                // 可能只是五行克制造成的，测不出中宫减伤。
                var st = BuildPair(Element.Wood, Element.Wood, BoardLayout.CenterIndex, 0,
                                   out var center, out var off);

                var enemy = BattleSampleContent.Make("probe_caster", "试术", Element.Fire, RoleType.Caster);
                st.Config.ApplyPlaceholderStats(enemy);
                var src = new BattleUnit(TeamSide.Enemy, enemy, "X0");

                st.Turn = 1;
                int dmgCenter = BattleSimulator.ComputeDamage(st, src, center, Element.Fire, 1f, false, false);
                int dmgOff = BattleSimulator.ComputeDamage(st, src, off, Element.Fire, 1f, false, false);
                float ratio = dmgOff <= 0 ? 0f : (float)dmgCenter / dmgOff;

                if (dmgCenter < dmgOff && ratio > 0.88f && ratio < 0.96f)
                    c.Ok($"中宫土德减伤：同一发伤害打中宫 {dmgCenter}、打非中宫 {dmgOff}" +
                         $"（比值 {ratio:F3}，期望 ≈ 0.92）");
                else
                    c.Bad($"中宫减伤不对：中宫 {dmgCenter}，非中宫 {dmgOff}，比值 {ratio:F3}（期望 ≈ 0.92）");
            }

            // ---- 4e 同属共鸣 ----
            {
                var c2 = BattleConfig.Default;
                var st = new BattleState(c2, 1UL);
                var defs = BattleSampleContent.TeamOf(Element.Wood, RoleType.Striker, 4, "w");
                for (int i = 0; i < defs.Length; i++)
                {
                    c2.ApplyPlaceholderStats(defs[i]);
                    st.Place(new BattleUnit(TeamSide.Player, defs[i], "P" + i), new GridPos(i));
                }
                var foe = BattleSampleContent.Make("foe", "敌", Element.Water, RoleType.Guard);
                c2.ApplyPlaceholderStats(foe);
                st.Place(new BattleUnit(TeamSide.Enemy, foe, "E0"), new GridPos(0));
                st.FinishSetup();

                BoardRules.ApplyResonance(st);
                var u0 = st.SlotAt(TeamSide.Player, 0);
                bool tier4 = Math.Abs(u0.ResonanceAttackBonus - 0.16f) < 1e-4f && u0.ResonanceUnlocked;

                var st5 = new BattleState(BattleConfig.Default, 1UL);
                var defs5 = BattleSampleContent.TeamOf(Element.Wood, RoleType.Striker, 5, "w");
                for (int i = 0; i < defs5.Length; i++)
                {
                    st5.Config.ApplyPlaceholderStats(defs5[i]);
                    st5.Place(new BattleUnit(TeamSide.Player, defs5[i], "P" + i), new GridPos(i));
                }
                var foe5 = BattleSampleContent.Make("foe5", "敌", Element.Water, RoleType.Guard);
                st5.Config.ApplyPlaceholderStats(foe5);
                st5.Place(new BattleUnit(TeamSide.Enemy, foe5, "E0"), new GridPos(0));
                st5.FinishSetup();
                BoardRules.ApplyResonance(st5);
                var u1 = st5.SlotAt(TeamSide.Player, 0);
                bool tier5 = Math.Abs(u1.ResonanceAttackBonus - 0.25f) < 1e-4f && u1.ResonanceCdDelta == -2;

                if (tier4 && tier5)
                    c.Ok("同属共鸣：4 只木 → 攻击 +16% 且解锁共鸣技；5 只木 → 攻击 +25% 且 CD -2");
                else
                    c.Bad($"同属共鸣档位不符：4只→{u0.ResonanceAttackBonus:P0}/解锁={u0.ResonanceUnlocked}，" +
                          $"5只→{u1.ResonanceAttackBonus:P0}/CD偏移={u1.ResonanceCdDelta}");
                c.Note("⚠ 5 只同属的「共鸣技 CD -2」目前落在绝技上（共鸣技需要一个第四技能位，本轮没做）。" +
                       "做共鸣技时把落点从 BattleSimulator 里挪走即可。");
            }
        }

        // ================================================================
        //  5) 灰盒看板（验收②的人工判据）
        // ================================================================

        private static void CheckGrayBox(Ctx c)
        {
            c.Title("[5/6] 灰盒看板（验收②是人的判断，这里只备料）");

            // ---- 5a 木 vs 火：GDD 7.1 验证顺序的第 0 步 ----
            var one = BattleSampleContent.BuildScenario(0, 7UL, null);
            var r = BattleSimulator.Run(one);

            c.Info("GDD 7.1 第 0 步「1 木 vs 1 火，看 20 遍」—— 单局数据：");
            c.Note($"结果 {CnOf(r.Outcome)}，{r.Turns} 回合，{r.EventCount} 条事件，出手 {r.SkillsCast} 次，" +
                   $"暴击 {r.Crits} 次");
            c.Note("木 vs 火 无克制关系（木克土、火克金），所以这一局测的是**节奏**不是克制。");
            c.Note(one.DescribeBoard(TeamSide.Player).TrimEnd());
            c.Note("事件时间线（前 40 条）：");
            foreach (var line in one.Log.Dump(40).Split('\n'))
                c.Note("  " + line.TrimEnd('\r'));

            // ---- 5b 五行关系在实战里的体现 + 先手对照 ----
            c.Info("五行关系在实战中的体现（同面板 1v1，200 个种子，唯一的差异是五行）：");
            c.Note("对位　　　　　　　　我方胜　　　敌方胜　　　平局　平均回合　我方伤害　　敌方伤害");
            Sweep(c, "木(我) vs 土(敌)  我克", Element.Wood, Element.Earth, 0, 11UL);
            Sweep(c, "土(我) vs 木(敌)  我被克", Element.Earth, Element.Wood, 0, 23UL);
            Sweep(c, "木(我) vs 水(敌)  无关系", Element.Wood, Element.Water, 0, 37UL);
            Sweep(c, "木(我) vs 木(敌)  同属(同速)", Element.Wood, Element.Wood, 0, 53UL);
            Sweep(c, "木(我) vs 木(敌)  同属(敌先手)", Element.Wood, Element.Wood, 1, 71UL);

            c.Note("读法：");
            c.Note("  第 1、2 行（木克土／土被木克）是 100% : 0% —— 五行系数确实深入到了伤害结算里，" +
                   "克制方在同面板 1v1 里**必胜**。");
            c.Note("  ⚠ 这与 GDD 2.3「克制是优势，不是胜负手」的意图有张力：1v1 里它就是胜负手。" +
                   "多人阵容里会稀释（基准 5v5 局我方 6964 / 敌方 5361，并没有一边倒），" +
                   "但若要做 1v1 或残局玩法，需要重新评估 1.50/0.75 这组数。");
            c.Note("  第 3~5 行是**先手对照**：无关系 80%/20%、同属同速 94%/6%、同属敌先手 4%/96%。" +
                   "后两组互为镜像，说明同速平局时我方恒定先手，");
            c.Note("  而先手在一场 4~6 回合的 1v1 里是决定性的。GDD 6.2 只规定了「同速按站位从左到右」，" +
                   "站位也相同时本实现按阵营定序（我方先）——");
            c.Note("  这是一条**需要策划确认**的设计选择：多人对局里先手优势会被稀释，" +
                   "但若要做 1v1 玩法，后续可能需要给后手补偿（如后手首回合减伤、或随机化同速平局）。");

            // ---- 5c 节奏体检：绝技不能开局就放 ----
            {
                var st = BuildStandard(20260914UL, null);
                var rr = BattleSimulator.Run(st);
                int firstUlt = 0, nUlt = 0, nActive = 0, nBasic = 0;

                var ev = st.Log.Events;
                for (int i = 0; i < ev.Count; i++)
                {
                    if (ev[i].Kind != BattleEventKind.SkillCast) continue;
                    switch (ev[i].Skill)
                    {
                        case SkillType.Ultimate:
                            nUlt++;
                            if (firstUlt == 0) firstUlt = ev[i].Turn;
                            break;
                        case SkillType.Active: nActive++; break;
                        default: nBasic++; break;
                    }
                }

                c.Info("节奏体检（5v5 基准局）：" +
                       $"普攻 {nBasic} 次／战技 {nActive} 次／绝技 {nUlt} 次，首次绝技在第 {(firstUlt == 0 ? "（未放出）" : firstUlt.ToString())} 回合");
                if (firstUlt >= 2)
                {
                    c.Ok("绝技受怒气约束，**开局不会全员放大招**（CD 开局为 0，唯一的闸门就是怒气）");
                }
                else
                {
                    c.Bad("第一回合就有人放绝技 —— 战斗会退化成「开局对轰大招」。"
                          + "检查 BattleConfig.UltimateNeedsRage 与 UltimateRageCost，"
                          + "以及 ExecuteAction 里有没有真的扣怒气。");
                }
                c.Note($"平均每回合入账 {(rr.Turns <= 0 ? 0f : rr.EventCount / (float)rr.Turns):F1} 条事件；" +
                       $"整局 {rr.Turns} 回合、{rr.Deaths} 人阵亡、{rr.Crits} 次暴击。");
                c.Note("⚠ 怒气必须能挡住绝技，否则 GDD 2.5「相克相冲使怒气获取 -10%」这条永远感受不到 ——" +
                       "棋盘规则与技能循环就断了联系。");
            }
        }

        /// <summary>
        /// 跑一组对位，统计胜负与伤害。
        ///
        /// ⚠ **每一行的 seedBase 必须不同**：战斗的随机流完全由种子决定，
        ///   两行共用同一批种子时它们不是独立样本 —— 两次跑会消费同一条暴击序列，
        ///   一旦伤害不同又会在阵亡处错开，结果是"看起来有差异但说不清是五行造成的"。
        ///   第一版就是这样，同属行 55/5、无关行 41/19，两个都是"无优势"却差了一倍，
        ///   纯属耦合噪声。
        /// </summary>
        private static void Sweep(Ctx c, string label, Element mine, Element foe,
                                  int foeSpeedBonus, ulong seedBase)
        {
            const int seeds = 200;
            int win = 0, lose = 0, draw = 0, turns = 0, dmgP = 0, dmgE = 0;

            for (ulong s = 0; s < seeds; s++)
            {
                var pDef = BattleSampleContent.Make("sw", "我方", mine, RoleType.Striker);
                var eDef = BattleSampleContent.Make("se", "敌方", foe, RoleType.Striker);

                var st = BattleFactory.Create(BattleConfig.Default, s * 1000UL + seedBase,
                    new[] { DeployEntry.Player(pDef, 0) },
                    new[] { DeployEntry.Enemy(eDef, 0) });

                if (foeSpeedBonus != 0)
                    st.SlotAt(TeamSide.Enemy, 0).Def.BaseSpeed += foeSpeedBonus;

                var r = BattleSimulator.Run(st);
                if (r.Outcome == BattleOutcome.PlayerWin) win++;
                else if (r.Outcome == BattleOutcome.EnemyWin) lose++;
                else draw++;

                turns += r.Turns;
                dmgP += SkillDamage(st, "P");
                dmgE += SkillDamage(st, "E");
            }

            c.Note($"{Pad(label, 20)}{Pad($"{win} {win * 100 / seeds}%", 10)}" +
                   $"{Pad($"{lose} {lose * 100 / seeds}%", 10)}" +
                   $"{Pad(draw.ToString(), 6)}{Pad((turns / (float)seeds).ToString("F1"), 10)}" +
                   $"{Pad((dmgP / (float)seeds).ToString("F0"), 12)}{dmgE / (float)seeds:F0}");
        }

        // ================================================================
        //  装配辅助
        // ================================================================

        /// <summary>
        /// 基准局面：5v5。阵容定义在 <see cref="BattleSampleContent.BuildScenario"/>，
        /// 灰盒窗口读的是同一份 —— 两处各写一份必然分叉，然后会出现
        /// "自检跑的局面和眼睛看的不一样"。
        /// </summary>
        private static BattleState BuildStandard(ulong seed, BattleConfig cfg)
            => BattleSampleContent.BuildScenario(2, seed, cfg);

        /// <summary>两个我方单位的极简局面，用来单独验棋盘规则。</summary>
        private static BattleState BuildPair(Element a, Element b, int posA, int posB,
                                             out BattleUnit ua, out BattleUnit ub, bool suppress = true)
        {
            var cfg = BattleConfig.Default;
            cfg.CenterSuppressesAdjacentCounter = suppress;

            var da = BattleSampleContent.Make("pa", "甲", a, RoleType.Guard);
            var db = BattleSampleContent.Make("pb", "乙", b, RoleType.Guard);
            var foe = BattleSampleContent.Make("foe", "敌", Element.Water, RoleType.Guard);

            var st = BattleFactory.Create(cfg, 1UL,
                new[] { DeployEntry.Player(da, posA), DeployEntry.Player(db, posB) },
                new[] { DeployEntry.Enemy(foe, 0) });

            ua = st.SlotAt(TeamSide.Player, posA);
            ub = st.SlotAt(TeamSide.Player, posB);
            return st;
        }

        /// <summary>把单位打到半血 —— 满血时"回复 3%"的数值是 0，量不出来。</summary>
        private static void HurtToHalf(BattleUnit u)
        {
            u.TakeDamage(u.MaxHp / 2);
        }

        /// <summary>四种关系的伤害探针局面。</summary>
        private static BattleState BuildProbe(out BattleUnit wood, out BattleUnit earth, out BattleUnit water)
        {
            var cfg = BattleConfig.Default;
            var dw = BattleSampleContent.Make("w", "木", Element.Wood, RoleType.Guard);
            var de = BattleSampleContent.Make("e", "土", Element.Earth, RoleType.Guard);
            var dt = BattleSampleContent.Make("t", "水", Element.Water, RoleType.Guard);
            var foe = BattleSampleContent.Make("foe", "敌", Element.Water, RoleType.Guard);

            var st = BattleFactory.Create(cfg, 1UL,
                new[] { DeployEntry.Player(dw, 0), DeployEntry.Player(de, 1), DeployEntry.Player(dt, 2) },
                new[] { DeployEntry.Enemy(foe, 0) });

            wood = st.SlotAt(TeamSide.Player, 0);
            earth = st.SlotAt(TeamSide.Player, 1);
            water = st.SlotAt(TeamSide.Player, 2);
            return st;
        }

        /// <summary>只统计技能造成的伤害（排除棋盘真伤），并按阵营汇总。</summary>
        private static int SkillDamage(BattleState st, string idPrefix)
        {
            int sum = 0;
            var ev = st.Log.Events;
            for (int i = 0; i < ev.Count; i++)
            {
                var e = ev[i];
                if (e.Kind != BattleEventKind.Damage) continue;
                if (string.IsNullOrEmpty(e.SkillName)) continue;
                if (string.IsNullOrEmpty(e.ActorId)) continue;
                if (!e.ActorId.StartsWith(idPrefix, StringComparison.Ordinal)) continue;
                sum += e.Amount;
            }
            return sum;
        }

        // ================================================================
        //  6) 表现层素材与文档对账
        //  ----------------------------------------------------------------
        //  这一节回答的是"灰盒画出来的东西可不可信"，而不是"战斗对不对"。
        //  表现层最容易出的问题不是崩溃，而是**画的和算的不一样**：
        //  画了一条相冲线、那一对却什么都没结算 —— 我们看到之后会把"规则坏了"
        //  当成结论，然后去改本来没错的逻辑。
        //
        //  所以这里做两件事：
        //    ① 色板必须仍然来自 palette.json（改了颜色、忘了改文档 ⇒ 立刻报）
        //    ② 棋盘连线的判据（BoardPairScan）与结算（BoardRules）数出来的格对数必须相等
        // ================================================================

        private const string PaletteJsonPath = "Docs/Design/data/palette.json";

        private static void CheckPresentation(Ctx c)
        {
            c.Title("[6/6] 表现层素材与文档对账");

            // ---- ① 色板 vs palette.json ----
            string json = null;
            string full = null;
            try
            {
                string root = System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName;
                full = System.IO.Path.Combine(root, PaletteJsonPath);
                if (System.IO.File.Exists(full)) json = System.IO.File.ReadAllText(full);
            }
            catch (Exception ex)
            {
                c.Note("读 palette.json 时出错：" + ex.Message);
            }

            string err = BattlePalette.VerifyAgainstDoc(json);
            if (err == null)
            {
                c.Ok($"色板与 palette.json 对得上：BattlePalette 引用的 {BattlePalette.DocHexes.Length} 个色值" +
                     $"全部能在文档里找到（另有 {BattlePalette.GrayboxOnly.Length} 个明确标为「灰盒自定」的色值，不参与对账）");
            }
            else
            {
                c.Bad("色板与 palette.json 对不上：" + err);
            }
            c.Note("色板单一来源 = BattlePalette（WanXiang.Modules.Battle）。编辑器灰盒窗口与运行时棋盘视图读的**是同一份**：" +
                   "之前窗口自己抄了一份颜色常量，那是最容易分叉的写法（改了运行时的绿、忘了改窗口的绿），" +
                   "而灰盒的全部意义就是「眼睛看到的算数」。");
            if (full != null) c.Note($"对账文件：{PaletteJsonPath}");

            // ---- ② 连线判据 vs 结算判据 ----
            var st = BattleSampleContent.BuildScenario(2, 20260914UL, BattleConfig.Default);
            var playerPairs = new List<AdjacentPair>();
            var enemyPairs = new List<AdjacentPair>();
            BoardPairScan.ScanBoth(st, playerPairs, enemyPairs);

            int scanGen = Count(playerPairs, AdjacentPairKind.Generate) + Count(enemyPairs, AdjacentPairKind.Generate);
            int scanCnt = Count(playerPairs, AdjacentPairKind.Counter) + Count(enemyPairs, AdjacentPairKind.Counter);
            int scanPac = Count(playerPairs, AdjacentPairKind.Pacified) + Count(enemyPairs, AdjacentPairKind.Pacified);

            // 开局盘面：全员存活 ⇒ 结算里"跳过尸体"那条分支不生效，两份数应当严格相等
            var rep = BoardRules.ResolveAdjacency(st);

            if (scanGen == rep.GeneratePairs && scanCnt == rep.CounterPairs && scanPac == rep.PacifiedPairs)
            {
                c.Ok($"棋盘连线与结算同源：开局盘面上 BoardPairScan 数出 相生 {scanGen} / 相冲 {scanCnt} / " +
                     $"中宫平息 {scanPac} 对，BoardRules 结算出的格对数逐项相同");
            }
            else
            {
                c.Bad($"棋盘连线与结算对不上：扫描 相生 {scanGen}/相冲 {scanCnt}/平息 {scanPac}，" +
                      $"结算 相生 {rep.GeneratePairs}/相冲 {rep.CounterPairs}/平息 {rep.PacifiedPairs}" +
                      " —— 画面会画出规则没做的事");
            }
            c.Note($"我方：{BoardPairScan.Describe(playerPairs)}");
            c.Note($"敌方：{BoardPairScan.Describe(enemyPairs)}");
            c.Note("判据单一来源 = BoardPairScan（WanXiang.Battle.Core）。它刻意**不筛存活**：" +
                   "结算要跳过尸体（尸体不该再给同气），而画线要连到刚倒下的单位上，" +
                   "否则画面会在承伤那一帧提前断线，看起来像「规则漏了」。存活过滤由表现层按当前帧快照决定。");
        }

        private static int Count(List<AdjacentPair> pairs, AdjacentPairKind kind)
        {
            int n = 0;
            for (int i = 0; i < pairs.Count; i++) if (pairs[i].Kind == kind) n++;
            return n;
        }

        // ---- 文本小工具 ----

        private static string DescribeTotals(in BattleResult r)
        {
            return $"相生相邻 {r.GeneratePairs} 对（回复 {r.RegenTotal}）｜" +
                   $"相克相冲 {r.CounterPairs} 对（真伤 {r.TrueDamageTotal}）｜" +
                   $"中宫平息 {r.PacifiedPairs} 对｜同气 +{r.QiGranted} 层｜" +
                   $"出手 {r.SkillsCast} 次（暴击 {r.Crits}）｜阵亡 {r.Deaths} 人";
        }

        private static string Pad(string s, int width)
        {
            int w = 0;
            foreach (var ch in s) w += ch > 0x2E7F ? 2 : 1;   // 中文按两格宽算
            var sb = new StringBuilder(s);
            while (w < width) { sb.Append(' '); w++; }
            return sb.ToString();
        }

        private static string CnOf(Element e)
        {
            switch (e)
            {
                case Element.Wood: return "木";
                case Element.Fire: return "火";
                case Element.Earth: return "土";
                case Element.Metal: return "金";
                case Element.Water: return "水";
                default: return "无";
            }
        }

        private static string CnOf(BattleOutcome o)
        {
            switch (o)
            {
                case BattleOutcome.PlayerWin: return "我方胜";
                case BattleOutcome.EnemyWin: return "敌方胜";
                case BattleOutcome.Draw: return "平局";
                default: return "进行中";
            }
        }

        private sealed class Ctx
        {
            public readonly StringBuilder Sb = new StringBuilder(8192);
            public int Pass;
            public int Fail;

            public void Title(string t)
            {
                Sb.AppendLine();
                Sb.AppendLine("── " + t + " " + new string('─', Math.Max(1, 64 - t.Length * 2)));
            }

            public void Ok(string msg) { Pass++; Sb.AppendLine("  ✅ " + msg); }
            public void Bad(string msg) { Fail++; Sb.AppendLine("  ❌ " + msg); }
            public void Info(string msg) { Sb.AppendLine("  · " + msg); }
            public void Note(string msg) { Sb.AppendLine("      " + msg); }
        }
    }
}
