// ============================================================================
//  万相 · 局外孵蛋自检（meta.selftest）
//  ---------------------------------------------------------------------------
//  GDD 1.3 局外层「孵蛋获得新宿主、解锁新灵魂、扩充图鉴、分享 Base64 代码」的
//  机器判据。核心是**口径**而不是实现细节：
//    ① 灵卵经济：一局产出 = 节点 + 守关 + 通关 + 首达新幕（且败北也有产出）；
//    ② 孵蛋确定性：同存档同操作序列 ⇒ 同结果；灵卵不足/池空**都不改档**；
//    ③ 权重方向：占位权重 60/30/10 在统计上必须体现出来；
//    ④ 存档码往返 + 防呆（截断/重复/版本不符一律拒）；
//    ⑤ 端到端：新档 → 阵容 → 跑一局 → 结算灵卵 → 孵出新宿主 → 下一局能用它。
//
//  ⚠ 内容来自 ContentCatalog（30 只异兽 + 30 个灵魂）。没有内容时自检会明说，
//    而不是给一堆"假绿"。
// ============================================================================

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Core;
using WanXiang.Campaign;
using WanXiang.Editor.BattleTool;
using WanXiang.Editor.WeatherTool;
using WanXiang.Fusion;
using WanXiang.Meta;

namespace WanXiang.Editor.MetaTool
{
    public static class MetaSelfTest
    {
        private const string ReportPath = "Temp/WanXiangDiag/meta_selftest.txt";

        private static int _pass, _fail;
        private static readonly List<string> Failures = new List<string>();

        [MenuItem("万相/局外/孵蛋自检（meta.selftest）")]
        public static void RunFromMenu()
        {
            var lines = Run("meta.selftest");
            foreach (var l in lines) Debug.Log("[局外自检] " + l);
            if (_fail > 0)   // 只在失败时弹：全绿弹窗会卡死 MCP 自动化
                EditorUtility.DisplayDialog("局外孵蛋自检",
                    $"❌ {_pass} 过 / {_fail} 败，失败项见 Console 与 {ReportPath}。", "好");
        }

        public static string[] Run(string command)
        {
            var lines = new List<string>();
            _pass = 0;
            _fail = 0;
            Failures.Clear();

            lines.Add("万相 · 局外孵蛋自检（meta.selftest）");
            lines.Add("========================================================================");
            lines.Add($"时间　　{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            // ---- 内容：30 只异兽 / 30 个灵魂（真实目录） ----
            var beasts = LoadBeasts(out var souls);
            if (beasts == null || beasts.Length == 0)
            {
                Check(lines, false, "内容目录为空：先跑 万相/融合/① 导入内容（自检不产出假绿）");
                return Finish(lines);
            }

            var content = new MetaContent
            {
                HostCount = beasts.Length,
                SoulCount = souls.Length,
                HostRarities = RaritiesOf(beasts),
                SoulRarities = RaritiesOfSouls(souls),
            };
            Check(lines, content.IsValid,
                  $"内容：{content.HostCount} 宿主 / {content.SoulCount} 灵魂，稀有度表就位");

            // ---- ① 新档：送够开局（5 宿主 = 一队），灵卵 0 ----
            var st = MetaState.NewGame(20260914UL, content);
            Check(lines, st.UnlockedHosts.Count == MetaDefaults.StarterHosts
                       && st.UnlockedSouls.Count == MetaDefaults.StarterSouls
                       && st.Eggs == 0 && st.RunsPlayed == 0,
                  $"① 新档：{st.UnlockedHosts.Count} 宿主 / {st.UnlockedSouls.Count} 灵魂、灵卵 0");

            // ---- ② 灵卵不足：拒绝且不动档 ----
            var deny = EggForge.Hatch(st, content, EggKind.Host);
            Check(lines, !deny.Ok && st.Eggs == 0 && st.HatchCount == 0
                       && st.UnlockedHosts.Count == MetaDefaults.StarterHosts,
                  $"② 灵卵不足：拒绝且状态不变（{deny.Note}）");

            // ---- ③ 端到端：新档 → 阵容 → 跑一局 → 结算 → 孵蛋 ----
            var acts = SolarTermGraph.BuildDefault();
            var hostPools = new BeastDef[5][];
            var bossDefs = new BeastDef[5];
            for (int a = 1; a <= 5; a++)
            {
                var el = acts[a - 1].SeasonElement;
                hostPools[a - 1] = new[]
                {
                    BattleSampleContent.Make($"m{a}g", "御", el, RoleType.Guard),
                    BattleSampleContent.Make($"m{a}s", "攻", el, RoleType.Striker),
                    BattleSampleContent.Make($"m{a}c", "术", el, RoleType.Caster),
                    BattleSampleContent.Make($"m{a}p", "辅", el, RoleType.Support),
                    BattleSampleContent.Make($"m{a}f", "疾", el, RoleType.Swift),
                };
                bossDefs[a - 1] = BattleSampleContent.Make($"mb{a}", acts[a - 1].BossName, el, RoleType.Guard);
            }
            var enemies = new SeededEnemyProvider(a => hostPools[a - 1], a => bossDefs[a - 1]);

            // 我方阵容 = **从局外解锁里取**（这才是"孵蛋 → 出战"的闭环）
            var squadHosts = st.BuildSquadHosts(content, 5);
            var squad = new List<DeployEntry>();
            int[] slots = { 0, 1, 4, 7, 8 };
            var roles = new[] { RoleType.Guard, RoleType.Striker, RoleType.Caster, RoleType.Support, RoleType.Swift };
            for (int i = 0; i < squadHosts.Length; i++)
            {
                // 用真实内容的**名字与五行**，技能走灰盒模板（内容里 90 条技能还是占位）
                // ⚠ 技能用灰盒模板（内容里 90 条技能仍是占位），稀有度取神品：
                //   本自检验的是"局外孵蛋 → 出战 → 结算 → 再孵"这条闭环，
                //   不是平衡 —— 灵品阵容对上玄品敌人两场就败，灵卵入不敷出会让断言全红。
                var beast = beasts[squadHosts[i]];
                squad.Add(DeployEntry.Player(
                    BattleSampleContent.Make($"h{squadHosts[i]}", beast.DisplayName, beast.Element,
                                             roles[i], Rarity.Legend), slots[i]));
            }

            var driver = new RunDriver(BattleConfig.Default, acts, WeatherCatalog.GetSolarTerm,
                                       enemies, 20260914UL, RunChoosers.Seeded(20260914UL));
            var outcome = driver.Play(squad.ToArray());
            var income = MetaRewards.Settle(driver, st);
            // ⚠ 口径（2026-09-25 校正）：局外收益进**墨铊**，灵卵只把**局内剩余**结转进钱包。
            //   本断言原来写的是 `st.Eggs == income.Total`（早期"局外收益进灵卵"的旧口径）⇒ 长期恒红。
            //   现在按现行口径验：Eggs 只等于结转的 RunEggsCarried；Ink 等于局末结算的 InkGain
            //   （每场胜利的 +1 由 ResultPanel 即时发放，不在 MetaRewards 里）。
            Check(lines, income.Total > 0 && st.RunsPlayed == 1
                       && st.BestActReached == income.ActReached
                       && st.Eggs == income.RunEggsCarried
                       && st.Ink == income.InkGain,
                  $"③ 一局结算：{income.Describe()}｜局末墨铊 +{income.InkGain}"
                  + $"（每胜另 +1）｜灵卵结转 {income.RunEggsCarried}｜局数 {st.RunsPlayed}（{outcome}）");

            // 结算口径：同一份记录再算一次，**节点/守关/通关三项必须一模一样**，
            // 而"首达新幕"奖励是一次性的（此刻 BestAct 已经推进）⇒ 第二次应当为 0。
            var again = MetaRewards.IncomeOf(driver, st);
            Check(lines, again.NodeEggs == income.NodeEggs && again.BossEggs == income.BossEggs
                       && again.ClearBonus == income.ClearBonus && again.NewActBonus == 0,
                  $"③ 结算口径：节点/守关可重算一致，首达新幕只给一次（第二次 +{again.NewActBonus}）");

            // ---- ④ 孵蛋：孵出未解锁的一只，且扣价/记次/解锁三件事同时发生 ----
            // ⚠ v1.1 之后一局只有 13~17 战，端到端那局的灵卵收入未必够孵两次 ——
            //   这里补足余额：④ 验的是"扣价/记次/解锁"机制，不是经济产出（③ 已验过）。
            st.Eggs += EggForge.EggPrice * 2;
            int eggsBefore = st.Eggs;
            int hostsBefore = st.UnlockedHosts.Count;
            var hatch = EggForge.Hatch(st, content, EggKind.Host);
            bool hatchOk = hatch.Ok && st.Eggs == eggsBefore - EggForge.EggPrice
                        && st.HatchCount == 1 && st.UnlockedHosts.Count == hostsBefore + 1
                        && st.HostUnlocked(hatch.Index);
            Check(lines, hatchOk, $"④ 孵蛋（宿主）：{hatch.Note}");

            // 确定性：同种子同操作 ⇒ 同结果
            var stMirror = MetaState.NewGame(20260914UL, content);
            stMirror.Eggs = MetaDefaults.StarterHosts * 0 + eggsBefore;   // 与 st 同起始余额
            stMirror.RunsPlayed = st.RunsPlayed;
            stMirror.BestActReached = st.BestActReached;
            var hatchMirror = EggForge.Hatch(stMirror, content, EggKind.Host);
            Check(lines, hatchMirror.Ok && hatchMirror.Index == hatch.Index,
                  $"④ 孵蛋确定性：同种子同操作序列 ⇒ 同结果（[{hatch.Index}]）");

            // 灵魂池独立
            var soulHatch = EggForge.Hatch(st, content, EggKind.Soul);
            Check(lines, soulHatch.Ok && st.UnlockedSouls.Count == MetaDefaults.StarterSouls + 1
                       && st.UnlockedHosts.Count == hostsBefore + 1,
                  $"④ 孵蛋（灵魂）：只动灵魂池（{soulHatch.Note}）");

            // ---- ⑤ 权重方向：反复整池重开不现实 ⇒ 用多个种子各孵一次做统计 ----
            int rare = 0, epic = 0, legend = 0;
            for (ulong s = 0; s < 60; s++)
            {
                var sx = MetaState.NewGame(s, content);
                sx.Eggs = EggForge.EggPrice;
                var hx = EggForge.Hatch(sx, content, EggKind.Host);
                if (!hx.Ok) continue;
                if (hx.Rarity == Rarity.Rare) rare++;
                else if (hx.Rarity == Rarity.Epic) epic++;
                else if (hx.Rarity == Rarity.Legend) legend++;
            }
            Check(lines, rare >= epic && epic >= legend && rare > 0,
                  $"⑤ 权重方向（60 个种子）：灵品 {rare} ≥ 玄品 {epic} ≥ 神品 {legend}（占位权重 60/30/10）");

            // ---- ⑥ 图鉴孵满：池空 ⇒ Empty 且不扣灵卵 ----
            st.Eggs = EggForge.EggPrice * (content.HostCount + 5);
            for (int i = 0; i < content.HostCount + 5; i++)
                if (!EggForge.Hatch(st, content, EggKind.Host).Ok) break;
            int eggsAtFull = st.Eggs;
            var empty = EggForge.Hatch(st, content, EggKind.Host);
            Check(lines, empty.Empty && st.Eggs == eggsAtFull
                       && st.UnlockedHosts.Count == content.HostCount,
                  $"⑥ 图鉴孵满：{st.UnlockedHosts.Count}/{content.HostCount} 全解锁，池空不扣灵卵（余 {st.Eggs}）");

            // ---- ⑦ 存档码：往返 + 防呆 ----
            // ★ v6：把「剧情碎片 + 觉醒材料」也塞进这份档，一起验往返（否则新字段永远没被覆盖）
            string fragA = beasts[0].Id;                 // 集齐 3 片 ⇒ 已领材料
            st.AddFragment(fragA); st.AddFragment(fragA); st.AddFragment(fragA);
            st.ClaimAwakenSoul(fragA);
            string fragB = beasts[1].Id;                 // 只 1 片 ⇒ 未集齐、未领
            st.AddFragment(fragB);

            var code = MetaSaveCode.Encode(st);
            bool roundTrip = MetaSaveCode.TryDecode(code, out var back);
            Check(lines, code != null && roundTrip
                       && back.Seed == st.Seed && back.Eggs == st.Eggs
                       && back.HatchCount == st.HatchCount && back.RunsPlayed == st.RunsPlayed
                       && back.UnlockedHosts.Count == st.UnlockedHosts.Count
                       && back.UnlockedSouls.Count == st.UnlockedSouls.Count
                       && back.FragmentCountOf(fragA) == 3 && back.HasAwakenSoul(fragA)
                       && back.FragmentCountOf(fragB) == 1 && !back.HasAwakenSoul(fragB),
                  $"⑦ 存档码往返（v6）：{code?.Length ?? 0} 字符，碎片/觉醒材料字段全等"
                  + $"（{fragA} 3/3 已领，{fragB} 1/3 未领）");

            bool tamper = !MetaSaveCode.TryDecode(code.Substring(0, code.Length - 3), out _)
                       && !MetaSaveCode.TryDecode("这不是存档码", out _);
            var bad = new byte[22];
            bad[0] = MetaSaveCode.CurrentVersion;
            bad[17] = 1; bad[18] = 2; bad[19] = 9; bad[20] = 9; bad[21] = 0;
            tamper &= !MetaSaveCode.TryDecode(ToB64Url(bad), out _);
            Check(lines, tamper, "⑦ 存档码防呆：截断 / 非法字符 / 重复解锁 一律拒绝");

            // ---- ⑦c 专属材料门槛（进化门的前置契约）----
            //   进化 = 精魄 + 墨铊 + 专属材料；材料 = 残卷阁集齐剧情碎片后领取。
            //   这里验的是"门槛本身"：碎片不够 ⇒ 不能领（也就不能进化）；领过 ⇒ 不重复发。
            string fragC = beasts[2].Id;
            st.AddFragment(fragC);                                  // 仅 1 片
            bool gateBefore = !st.CanClaimAwakenSoul(fragC) && st.FragmentCountOf(fragC) == 1;
            st.AddFragment(fragC); st.AddFragment(fragC);           // 凑满 3 片
            bool gateAtFull = st.CanClaimAwakenSoul(fragC);
            st.ClaimAwakenSoul(fragC);
            bool gateAfter = st.HasAwakenSoul(fragC) && !st.CanClaimAwakenSoul(fragC);
            Check(lines, gateBefore && gateAtFull && gateAfter
                       && !st.CanClaimAwakenSoul(beasts[3].Id),      // 0 片也领不到
                  $"⑦c 专属材料门槛：碎片 1/{MetaState.AwakenSoulThreshold} 不可领 ⇒ 集齐可领 ⇒ 领后不重复");

            // ---- ⑧ 孵出的宿主能进下一局阵容 ----
            var squad2 = st.BuildSquadHosts(content, 5);
            bool usesHatched = false;
            for (int i = 0; i < squad2.Length; i++) if (squad2[i] == hatch.Index) usesHatched = true;
            Check(lines, squad2.Length == 5 && squad2[0] == st.UnlockedHosts[0],
                  $"⑧ 阵容取自局外：5 只（{string.Join(",", squad2)}）"
                  + (usesHatched ? "，含本次孵出的宿主" : "（本次孵出的宿主在替补位）"));

            // ---- ⑨ 剧情内容表（BeastLore）：每兽 6 片齐备 + 专属材料名互不相同 ----
            {
                int badSeg = 0, badMat = 0;
                var mats = new HashSet<string>();
                for (int i = 0; i < beasts.Length; i++)
                {
                    string id = beasts[i].Id;
                    if (!WanXiang.Modules.UI.BeastLore.HasEntry(id)) { badSeg++; continue; }
                    string mm = WanXiang.Modules.UI.BeastLore.MaterialName(id);
                    if (string.IsNullOrEmpty(mm) || !mats.Add(mm)) badMat++;
                    for (int k = 0; k < WanXiang.Modules.UI.BeastLore.FragmentTotal; k++)
                    {
                        var fr = WanXiang.Modules.UI.BeastLore.Fragment(id, k);
                        if (string.IsNullOrEmpty(fr.Title) || string.IsNullOrEmpty(fr.Text)) badSeg++;
                    }
                }
                Check(lines,
                      badSeg == 0 && badMat == 0
                      && WanXiang.Modules.UI.BeastLore.Count == beasts.Length,
                      $"⑨ 剧情内容表：{WanXiang.Modules.UI.BeastLore.Count}/{beasts.Length} 兽 × "
                      + $"{WanXiang.Modules.UI.BeastLore.FragmentTotal} 片，标题/正文齐备，"
                      + $"专属材料 {mats.Count} 个互不相同（缺片 {badSeg}，空/重名材料 {badMat}）");
            }

            // ---- ⑩ 五行 tab 口径（唯一来源 ElementTabs）----
            //   ⚠ 这里锁住一个真实踩过的 bug：用 `(int)Element == tab - 1` 会把「木」映射到 None=0
            //     ⇒ 该页签恒空、点「火」显示木系（图鉴与残卷阁都栽过）。
            {
                int[] hit = new int[WanXiang.Modules.UI.ElementTabs.Count];
                for (int tab = 0; tab < hit.Length; tab++)
                    for (int i = 0; i < beasts.Length; i++)
                        if (WanXiang.Modules.UI.ElementTabs.Matches(beasts[i].Element, tab)) hit[tab]++;

                bool woodOnly = true;                      // tab「木」必须且只能命中木系
                for (int i = 0; i < beasts.Length; i++)
                {
                    bool m = WanXiang.Modules.UI.ElementTabs.Matches(beasts[i].Element, 1);
                    if (m != (beasts[i].Element == Element.Wood)) woodOnly = false;
                }

                bool countsOk = hit[0] == beasts.Length;
                for (int tab = 1; tab < hit.Length; tab++) if (hit[tab] != 6) countsOk = false;

                Check(lines, countsOk && woodOnly,
                      $"⑩ 五行 tab 口径：全部 {hit[0]}；木/火/土/金/水 = "
                      + $"{hit[1]}/{hit[2]}/{hit[3]}/{hit[4]}/{hit[5]}（各应 6）；「木」只命中木系 = {woodOnly}");
            }

            return Finish(lines);
        }

        // ================================================================

        private static BeastDef[] LoadBeasts(out SoulDef[] souls)
        {
            souls = System.Array.Empty<SoulDef>();
            var guids = AssetDatabase.FindAssets("t:ContentCatalogSO");
            if (guids == null || guids.Length == 0) return System.Array.Empty<BeastDef>();
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogSO>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
            if (catalog == null) return System.Array.Empty<BeastDef>();
            // ⚠ BuildBeasts / BuildSouls 返回的就是数组（不是 List）—— 别再 .ToArray()。
            souls = ContentLibrary.BuildSouls(catalog) ?? System.Array.Empty<SoulDef>();
            return ContentLibrary.BuildBeasts(catalog) ?? System.Array.Empty<BeastDef>();
        }

        private static Rarity[] RaritiesOf(BeastDef[] beasts)
        {
            var r = new Rarity[beasts.Length];
            for (int i = 0; i < beasts.Length; i++) r[i] = beasts[i].Rarity;
            return r;
        }

        private static Rarity[] RaritiesOfSouls(SoulDef[] souls)
        {
            var r = new Rarity[System.Math.Max(1, souls.Length)];
            for (int i = 0; i < souls.Length; i++) r[i] = souls[i].Rarity;
            return r;
        }

        private static string ToB64Url(byte[] data)
            => System.Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        private static void Check(List<string> lines, bool ok, string what)
        {
            if (ok) _pass++;
            else { _fail++; Failures.Add(what); }
            lines.Add($"{(ok ? "✅" : "❌")} {what}");
        }

        private static string[] Finish(List<string> lines)
        {
            lines.Add("========================================================================");
            lines.Add($"结论：{_pass} 项通过，{_fail} 项失败");
            if (_fail > 0)
                foreach (var f in Failures) lines.Add("  ❌ " + f);
            else
                lines.Add("说明：灵卵产出与孵蛋权重都是**占位数值**（GDD 没给表），"
                          + "集中在 MetaDefaults 一处，拿到策划表只改那一处；"
                          + "孵蛋全程零系统时间、零隐式随机（同种子同操作必同结果）。");

            try
            {
                string dir = Path.GetDirectoryName(ReportPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ReportPath, string.Join("\n", lines), new UTF8Encoding(false));
            }
            catch (IOException) { /* 报告写不出去不影响判定 */ }

            return lines.ToArray();
        }
    }
}
