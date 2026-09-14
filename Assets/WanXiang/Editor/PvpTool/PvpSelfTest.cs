// ============================================================================
//  万相 · 异步对战自检（pvp.selftest）
//  ---------------------------------------------------------------------------
//  GDD STEP 3 最后一项「分享 Base64 代码与他人的队伍对战」的机器判据。
//  验收的是**闭环**：我方的码 + 对方的码 ⇒ 一场双方都能复算的战斗 ⇒ 可对照的战报。
//
//  核心口径（GDD 只写"异步对战"四个字，取舍见 PvpMatch 文件头）：
//    ① 对局种子与顺序无关 ⇒ A 视角与 B 视角算的是同一场；
//    ② PvP 单场不带天时 ⇒ 与手工装配的同种子战斗**指纹逐位相同**（无隐藏修正）；
//    ③ 落位不镜像（格子编号即棋盘语义）；
//    ④ 非法码/越界一律拒绝，绝不带病开打。
// ============================================================================

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Core;
using WanXiang.Fusion;
using WanXiang.Pvp;

namespace WanXiang.Editor.PvpTool
{
    public static class PvpSelfTest
    {
        private const string ReportPath = "Temp/WanXiangDiag/pvp_selftest.txt";

        private static int _pass, _fail;
        private static readonly List<string> Failures = new List<string>();

        [MenuItem("万相/对战/异步对战自检（pvp.selftest）")]
        public static void RunFromMenu()
        {
            var lines = Run("pvp.selftest");
            foreach (var l in lines) Debug.Log("[对战自检] " + l);
            if (_fail > 0)   // 只在失败时弹（全绿弹窗会卡死 MCP 自动化）
                EditorUtility.DisplayDialog("异步对战自检",
                    $"❌ {_pass} 过 / {_fail} 败，失败项见 Console 与 {ReportPath}。", "好");
        }

        public static string[] Run(string command)
        {
            var lines = new List<string>();
            _pass = 0;
            _fail = 0;
            Failures.Clear();

            lines.Add("万相 · 异步对战自检（pvp.selftest）");
            lines.Add("========================================================================");
            lines.Add($"时间　　{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            // ---- 内容 ----
            var beasts = LoadBeasts(out var souls, out var resolver);
            if (beasts == null || beasts.Length == 0 || souls.Length == 0 || resolver == null)
            {
                Check(lines, false, "内容目录为空：先跑 万相/融合/① 导入内容（自检不产出假绿）");
                return Finish(lines);
            }
            var content = new PvpContent { Beasts = beasts, Souls = souls, Resolver = resolver };
            Check(lines, content.IsValid,
                  $"内容：{beasts.Length} 异兽 / {souls.Length} 灵魂 / 技能解析器就位");

            // ---- 两份阵容码（真实内容下标） ----
            var minePayload = MakePayload(new[] { 0, 5, 10, 15, 20 }, new[] { 3, 8, 13, 18, 23 }, 111UL);
            var oppPayload = MakePayload(new[] { 29, 24, 19, 14, 9 }, new[] { 27, 22, 17, 12, 7 }, 222UL);
            string myCode = ShareCode.Encode(minePayload);
            string oppCode = ShareCode.Encode(oppPayload);
            Check(lines, myCode != null && oppCode != null && myCode.Length <= 90 && oppCode.Length <= 90,
                  $"① 两份分享码：{myCode?.Length ?? 0} / {oppCode?.Length ?? 0} 字符（≤90）");

            // ---- ② 对局种子与顺序无关 ----
            ulong seedAb = PvpMatch.MatchSeed(myCode, oppCode);
            ulong seedBa = PvpMatch.MatchSeed(oppCode, myCode);
            Check(lines, seedAb == seedBa && seedAb != PvpMatch.MatchSeed(myCode, oppCode + "x"),
                  $"② 对局种子：A→B 与 B→A 相同（{seedAb}），换对手则不同");

            // ---- ③ 打一场 + 可复现 ----
            var r1 = PvpMatch.PlayByCode(myCode, oppCode, beasts.Length, souls.Length, content);
            var r2 = PvpMatch.PlayByCode(myCode, oppCode, beasts.Length, souls.Length, content);
            Check(lines, r1.Valid && r2.Valid && r1.Fingerprint == r2.Fingerprint
                       && r1.Turns == r2.Turns && r1.Outcome == r2.Outcome,
                  $"③ 战报可复现：{Cn(r1.Outcome)}｜{r1.Turns} 回合｜{r1.EventCount} 事件｜"
                  + $"指纹 0x{r1.Fingerprint:X8}（跑两次一致）");

            // ---- ④ 等价性：PvP = 一场干净的战斗（无天时、无隐藏修正） ----
            var manual = BattleFactory.Create(BattleConfig.Default, seedAb,
                BuildSide(minePayload, content, TeamSide.Player),
                BuildSide(oppPayload, content, TeamSide.Enemy), null);
            uint manualFp = BattleSimulator.Run(manual).Fingerprint;
            Check(lines, manualFp == r1.Fingerprint,
                  $"④ 等价性：与手工装配的同种子战斗指纹一致（0x{manualFp:X8}）");

            // ---- ⑤ 阵容归属：两侧摘要 = 各自码融合后的名字 ----
            //      ⚠ 别拿宿主名做 Contains —— 融合名「句芒·碧涛」天然含宿主名（踩过）
            string myFirst = FusionRules.Fuse(beasts[minePayload.BeastIndices[0]],
                                              souls[minePayload.SoulIndices[0]], resolver).DisplayName;
            string oppFirst = FusionRules.Fuse(beasts[oppPayload.BeastIndices[0]],
                                               souls[oppPayload.SoulIndices[0]], resolver).DisplayName;
            Check(lines, r1.MySquad.Contains(myFirst) && r1.OppSquad.Contains(oppFirst)
                       && r1.MySquad != r1.OppSquad,
                  $"⑤ 阵容归属：我方 [{myFirst}]／对方 [{oppFirst}]（两侧真不同）");

            // ---- ⑥ 互换立场：种子相同、战报不同（立场不对称真实存在） ----
            var swap = PvpMatch.PlayByCode(oppCode, myCode, beasts.Length, souls.Length, content);
            Check(lines, swap.Valid && swap.Seed == r1.Seed
                       && (swap.Fingerprint != r1.Fingerprint || swap.Outcome != r1.Outcome),
                  $"⑥ 互换立场：种子相同（{swap.Seed}），战报指纹 0x{r1.Fingerprint:X8} → 0x{swap.Fingerprint:X8}");

            // ---- ⑦ 非法拒绝 ----
            var bad1 = PvpMatch.PlayByCode(myCode, "这不是分享码", beasts.Length, souls.Length, content);
            var bad2 = PvpMatch.PlayByCode(myCode, oppCode, 3, souls.Length, content);
            var bad3 = PvpMatch.Play(default, oppPayload, content);
            Check(lines, !bad1.Valid && !bad2.Valid && !bad3.Valid
                       && !string.IsNullOrEmpty(bad1.Error) && !string.IsNullOrEmpty(bad2.Error),
                  $"⑦ 非法拒绝：坏码（{bad1.Error}）／下标越界（{bad2.Error}）／空阵容（{bad3.Error}）");

            // ---- ⑧ 战报可分享 ----
            string digest = r1.Digest();
            Check(lines, digest.Contains("指纹 0x") && digest.Contains("我方：") && digest.Contains("对方："),
                  "⑧ 战报可分享：Digest 含结果/回合/种子/指纹与双方阵容");
            lines.Add("  · 战报样例：");
            foreach (var l in digest.Split('\n')) lines.Add("      " + l);

            // ---- ⑨ 端到端：对方码换成"另一套真实阵容"再打一场，两场的战报指纹必须不同 ----
            var thirdPayload = MakePayload(new[] { 2, 7, 12, 17, 22 }, new[] { 1, 6, 11, 16, 21 }, 333UL);
            var r3 = PvpMatch.PlayByCode(myCode, ShareCode.Encode(thirdPayload),
                                         beasts.Length, souls.Length, content);
            Check(lines, r3.Valid && r3.Fingerprint != r1.Fingerprint,
                  $"⑨ 换对手即换战局：对手 B 0x{r1.Fingerprint:X8} → 对手 C 0x{r3.Fingerprint:X8}");

            // ---- ⑩ 诊断（不做断言）：多组对阵的胜负/平局分布 ----
            //      这条不给硬判据，只把数字摆出来 —— 灰盒内容的治疗/护盾体系在
            //      回合上限 30 下容易打出平局（已知信号），PvP 里平局率高尤其要命。
            int win = 0, lose = 0, draw = 0;
            for (int k = 0; k < 5; k++)
            {
                var pay = MakePayload(new[] { k, k + 6, k + 12, k + 18, k + 24 },
                                      new[] { (k + 2) % 30, (k + 9) % 30, (k + 14) % 30, (k + 19) % 30, (k + 25) % 30 },
                                      (ulong)(400 + k));
                var rk = PvpMatch.Play(minePayload, pay, content);
                if (!rk.Valid) continue;
                if (rk.Outcome == BattleOutcome.PlayerWin) win++;
                else if (rk.Outcome == BattleOutcome.EnemyWin) lose++;
                else draw++;
            }
            lines.Add($"  · 诊断（不断言）：5 组对阵 → 我方胜 {win}／对方胜 {lose}／平局 {draw}"
                      + "　⚠ 平局多 = 回合上限 30 与治疗护盾体系的僵局信号，PvP 需专门评估");

            return Finish(lines);
        }

        // ================================================================

        private static SharePayload MakePayload(int[] beastIdx, int[] soulIdx, ulong seed)
            => new SharePayload
            {
                Version = ShareCode.CurrentVersion,
                BeastIndices = beastIdx,
                SoulIndices = soulIdx,
                BoardSlots = new[] { 0, 1, 4, 7, 8 },
                Seed = seed,
            };

        private static DeployEntry[] BuildSide(SharePayload p, PvpContent content, TeamSide side)
        {
            var entries = new DeployEntry[p.UnitCount];
            for (int i = 0; i < p.UnitCount; i++)
            {
                var slot = p.GetSlot(i);
                var fused = FusionRules.Fuse(content.Beasts[slot.BeastIndex],
                                             content.Souls[slot.SoulIndex], content.Resolver);
                entries[i] = side == TeamSide.Player
                    ? DeployEntry.Player(fused, slot.BoardSlot)
                    : DeployEntry.Enemy(fused, slot.BoardSlot);
            }
            return entries;
        }

        private static BeastDef[] LoadBeasts(out SoulDef[] souls, out SkillResolver resolver)
        {
            souls = System.Array.Empty<SoulDef>();
            resolver = null;
            var guids = AssetDatabase.FindAssets("t:ContentCatalogSO");
            if (guids == null || guids.Length == 0) return System.Array.Empty<BeastDef>();
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogSO>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
            if (catalog == null) return System.Array.Empty<BeastDef>();
            souls = ContentLibrary.BuildSouls(catalog) ?? System.Array.Empty<SoulDef>();
            resolver = ContentLibrary.ResolverFrom(catalog);
            return ContentLibrary.BuildBeasts(catalog) ?? System.Array.Empty<BeastDef>();
        }

        private static string Cn(BattleOutcome o)
        {
            switch (o)
            {
                case BattleOutcome.PlayerWin: return "我方胜";
                case BattleOutcome.EnemyWin: return "对方胜";
                case BattleOutcome.Draw: return "平局";
                default: return "未决";
            }
        }

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
                lines.Add("说明：PvP 单场不带天时（天时属于某一方地图的规则，双方对「在谁的节气上打」"
                          + "没有共识 —— 取舍记录在 PvpMatch 文件头）；篡改按 GDD 风险表"
                          + "只做 id 合法性校验、不做签名。");

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
