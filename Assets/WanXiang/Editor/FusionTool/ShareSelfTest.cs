// ============================================================================
//  万相 · 分享码自检（share.selftest）
//  ---------------------------------------------------------------------------
//  对应 GDD 第 7 章 STEP 3 验收③：「分享码长度控制在 90 字符以内，
//  导入后能 1:1 复现对方阵容与对局结果」，外加防篡改口径的合法性校验。
//
//  1:1 复现的判据与战斗自检同源：同一份载荷 + 同一个种子 ⇒ 事件流指纹一致。
//  Edit 模式同步跑（不进 Play，理由同 battle/fusion 自检）。
//  报告：Temp/WanXiangDiag/share_selftest.txt
// ============================================================================

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Core;
using WanXiang.Fusion;

namespace WanXiang.Editor.FusionTool
{
    public static class ShareSelfTest
    {
        private const string ReportPath = "Temp/WanXiangDiag/share_selftest.txt";

        private static int _pass, _fail;
        private static readonly List<string> Failures = new List<string>();

        [MenuItem("万相/融合/③ 分享码自检（share.selftest）")]
        public static void RunFromMenu()
        {
            var lines = Run("share.selftest");
            foreach (var l in lines) Debug.Log("[分享码自检] " + l);
            EditorUtility.DisplayDialog("分享码自检",
                _fail == 0 ? $"✅ {_pass} 项全部通过。\n详情见 Console（搜 [分享码自检]）。"
                           : $"❌ {_pass} 过 / {_fail} 败，失败项见 Console。", "好");
        }

        public static string[] Run(string command)
        {
            var lines = new List<string>();
            _pass = 0;
            _fail = 0;
            Failures.Clear();

            lines.Add("万相 · 分享码自检（share.selftest）");
            lines.Add("========================================================================");
            lines.Add($"时间　　{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            var catalog = LoadCatalog();
            if (catalog == null)
            {
                lines.Add("❌ 未找到内容目录，请先执行「万相/融合/① 导入异兽内容」。");
                return Finish(lines);
            }
            var beasts = ContentLibrary.BuildBeasts(catalog);
            var souls = ContentLibrary.BuildSouls(catalog);
            var resolver = ContentLibrary.ResolverFrom(catalog);

            // ---- 样例阵容：5 只异兽，灵魂故意取"别家"的（体现 N×M 自由组合） ----
            var original = new SharePayload
            {
                Version = ShareCode.CurrentVersion,
                BeastIndices = new[] { 0, 6, 12, 18, 24 },
                SoulIndices = new[] { 29, 7, 11, 3, 15 },
                BoardSlots = new[] { 0, 1, 4, 3, 8 },
                Seed = 0x265422D8ABCD1234UL,
            };

            // ---- 编解码往返 ----
            string code = ShareCode.Encode(original);
            Check(lines, !string.IsNullOrEmpty(code), "S1 编码成功");

            bool ok = ShareCode.TryDecode(code, beasts.Length, souls.Length, out var decoded);
            Check(lines, ok && PayloadEquals(original, decoded),
                  "S1 解码往返：字段逐项一致（阵容 / 灵魂 / 落位 / 种子 / 版本）");

            // ---- 长度验收（GDD：90 字符以内） ----
            Check(lines, code.Length <= 90,
                  $"验收③ 分享码长度 {code.Length} ≤ 90 字符（GDD 7 章）");

            // ---- 1:1 复现：同一码读两次 + 跨"两台机器"的对局指纹一致 ----
            bool okAgain = ShareCode.TryDecode(code, beasts.Length, souls.Length, out var decoded2);
            Check(lines, okAgain && PayloadEquals(decoded, decoded2),
                  "验收③ 同一码解码两次结果一致");

            uint fpA = RunBattleFingerprint(original, beasts, souls, resolver);
            uint fpB = RunBattleFingerprint(decoded, beasts, souls, resolver);
            Check(lines, fpA != 0 && fpA == fpB,
                  $"验收③ 对局 1:1 复现：原始阵容与解码阵容的战斗指纹一致（0x{fpA:X8}）");

            // ---- 确定性 ----
            Check(lines, ShareCode.Encode(original) == code, "确定性：同一载荷编码两次字符串一致");

            // ---- 防篡改：各类损坏输入必须被拒 ----
            Check(lines, !ShareCode.TryDecode("0" + code.Substring(1), beasts.Length, souls.Length, out _),
                  "防篡改：版本号被改 ⇒ 拒绝");
            Check(lines, !ShareCode.TryDecode(code + "AAAA", beasts.Length, souls.Length, out _),
                  "防篡改：尾部塞数据 ⇒ 拒绝");
            Check(lines, !ShareCode.TryDecode("不是分享码", beasts.Length, souls.Length, out _),
                  "防篡改：非 Base64 输入 ⇒ 拒绝");
            var tampered = original;
            tampered.BoardSlots = new[] { 0, 1, 2, 3, 3 };   // 落位重复
            Check(lines, ShareCode.Encode(tampered) == null,
                  "防篡改：落位重复的载荷 ⇒ 编码期拒绝");

            // ---- 越界：内容表比 byte 小的场合（用小目录做边界校验） ----
            bool rejected = !ShareCode.TryDecode(code, 5, souls.Length, out _);
            Check(lines, rejected, "防篡改：异兽下标越界（内容表变小）⇒ 拒绝");

            return Finish(lines);
        }

        // ====================================================================

        /// <summary>按载荷装配我方（宿主×灵魂融合 + 落位 + 种子），敌方固定，跑整场返回指纹。</summary>
        private static uint RunBattleFingerprint(SharePayload p, BeastDef[] beasts, SoulDef[] souls,
                                                 SkillResolver resolver)
        {
            int n = p.UnitCount;
            var player = new DeployEntry[n];
            for (int i = 0; i < n; i++)
            {
                var slot = p.GetSlot(i);
                var fused = FusionRules.Fuse(beasts[slot.BeastIndex], souls[slot.SoulIndex], resolver);
                player[i] = DeployEntry.Player(fused, slot.BoardSlot);
            }

            // 敌方固定：内容表末尾 5 只（不融合），落位 0..4 —— 只当"陪练"，不影响复现判据。
            var enemy = new DeployEntry[5];
            for (int i = 0; i < 5; i++)
                enemy[i] = DeployEntry.Enemy(beasts[beasts.Length - 5 + i], i);

            var st = BattleFactory.Create(BattleConfig.Default, p.Seed, player, enemy);
            BattleSimulator.Run(st);
            return st.Log.Fingerprint;
        }

        private static bool PayloadEquals(SharePayload a, SharePayload b)
        {
            if (a.Version != b.Version || a.Seed != b.Seed || a.UnitCount != b.UnitCount)
                return false;
            for (int i = 0; i < a.UnitCount; i++)
                if (a.BeastIndices[i] != b.BeastIndices[i]
                    || a.SoulIndices[i] != b.SoulIndices[i]
                    || a.BoardSlots[i] != b.BoardSlots[i])
                    return false;
            return true;
        }

        private static ContentCatalogSO LoadCatalog()
        {
            var guids = AssetDatabase.FindAssets("t:ContentCatalogSO");
            if (guids == null || guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<ContentCatalogSO>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static string[] Finish(List<string> lines)
        {
            lines.Add("========================================================================");
            lines.Add($"结论：{_pass} 项通过，{_fail} 项失败");
            if (_fail > 0)
                foreach (var f in Failures) lines.Add("  ❌ " + f);

            try
            {
                string dir = Path.GetDirectoryName(ReportPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ReportPath, string.Join("\n", lines), new UTF8Encoding(false));
            }
            catch (IOException) { /* 报告写不出去不影响判定 */ }

            return lines.ToArray();
        }

        private static void Check(List<string> lines, bool ok, string what)
        {
            if (ok) _pass++;
            else { _fail++; Failures.Add(what); }
            lines.Add($"{(ok ? "✅" : "❌")} {what}");
        }
    }
}
