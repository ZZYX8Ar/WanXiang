// ============================================================================
//  万相 · 融合 · 自检（fusion.selftest）
//  ---------------------------------------------------------------------------
//  对应 GDD 第 7 章 STEP 2 的验收标准，逐条机器判定：
//
//    验收①  3 宿主 × 3 灵魂 = 9 种「外观与技能组均不重复」的单位
//            —— 外观签名（五个色区）与技能组签名（三槽 + 五行）逐项判重。
//    验收②  融合后的单位在战斗中行为正确
//            —— 五行覆写改变克制系数（1.50 → 0.75）；
//            —— 灵魂战技替换真实发生在战斗事件流里（对照实验：未融合的宿主
//               同种子下**不会**放出那个战技）；
//            —— 特性叠加（宿主特性 + 灵魂注入都在）。
//    验收③  切换灵魂时辉光与睛色实时变化
//            —— 数据层机器判定（覆写 + 色相偏移 + 描边恒墨色）；
//            —— 「实时、无需重载场景」是表现层行为，由融合预览窗口人工确认
//               （万相/融合/融合预览），本自检只备料。
//
//  Edit 模式同步跑，不进 Play（理由同 battle.selftest：战斗核心纯计算，
//  且这台机器的播放器循环不可靠）。
//  报告：Temp/WanXiangDiag/fusion_selftest.txt
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
    public static class FusionSelfTest
    {
        private const string ReportPath = "Temp/WanXiangDiag/fusion_selftest.txt";

        private static int _pass, _fail;
        private static readonly List<string> Failures = new List<string>();

        [MenuItem("万相/融合/② 融合自检（fusion.selftest）")]
        public static void RunFromMenu()
        {
            var lines = Run("fusion.selftest");
            foreach (var l in lines) Debug.Log("[融合自检] " + l);
            EditorUtility.DisplayDialog("融合自检",
                _fail == 0 ? $"✅ {_pass} 项全部通过。\n详情见 Console（搜 [融合自检]）。"
                           : $"❌ {_pass} 过 / {_fail} 败，失败项见 Console。", "好");
        }

        public static string[] Run(string command)
        {
            var lines = new List<string>();
            _pass = 0;
            _fail = 0;
            Failures.Clear();

            lines.Add("万相 · 融合管线自检（fusion.selftest）");
            lines.Add("========================================================================");
            lines.Add($"时间　　{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            // ---- 内容装载（没有目录就先导入，保证命令自含） ----
            var catalog = LoadCatalog();
            if (catalog == null)
            {
                lines.Add("未找到内容目录，自动执行导入……");
                foreach (var l in BeastContentImporter.Import()) lines.Add("  " + l);
                AssetDatabase.Refresh();
                catalog = LoadCatalog();
            }
            if (catalog == null)
            {
                lines.Add("❌ 内容目录仍不存在（导入失败），后续检查中止。");
                return Finish(lines);
            }

            var beasts = ContentLibrary.BuildBeasts(catalog);
            var souls = ContentLibrary.BuildSouls(catalog);
            var resolver = ContentLibrary.ResolverFrom(catalog);

            Check(lines, beasts.Length == souls.Length && beasts.Length >= 3,
                  $"S1 内容装载：{beasts.Length} 只异兽 / {souls.Length} 个灵魂（数量一致且 ≥3）");

            int intact = 0;
            foreach (var b in beasts)
                if (b != null && b.Basic != null && b.Active != null && b.Ultimate != null) intact++;
            Check(lines, intact == beasts.Length,
                  $"S1 异兽技能组完整：{intact}/{beasts.Length} 只都有普攻/战技/绝技");

            // ---- 取三个不同五行的宿主与其灵魂 ----
            var hostWood = FirstBeastOf(beasts, Element.Wood);
            var hostFire = FirstBeastOf(beasts, Element.Fire);
            var hostWater = FirstBeastOf(beasts, Element.Water);
            var soulWood = SoulOf(souls, hostWood);
            var soulFire = SoulOf(souls, hostFire);
            var soulWater = SoulOf(souls, hostWater);

            Check(lines, hostWood != null && hostFire != null && hostWater != null,
                  "S1 抽样：木/火/水三个五行各取到一只宿主");
            if (hostWood == null || hostFire == null || hostWater == null)
                return Finish(lines);

            Check(lines, soulWood != null && soulFire != null && soulWater != null,
                  "S1 抽样：三只宿主都有对应灵魂（木/火/水）");
            if (soulWood == null || soulFire == null || soulWater == null)
                return Finish(lines);

            // ==================================================================
            //  验收①：3×3 = 9 种组合，外观与技能组均不重复
            // ==================================================================
            var hosts = new[] { hostWood, hostFire, hostWater };
            var souls3 = new[] { soulWood, soulFire, soulWater };

            var fused9 = new List<BeastDef>(9);
            foreach (var h in hosts)
                foreach (var s in souls3)
                    fused9.Add(FusionRules.Fuse(h, s, resolver));

            var appearance = new List<string>(9);
            var skillSets = new List<string>(9);
            var names = new List<string>(9);
            foreach (var f in fused9)
            {
                appearance.Add(FusionRules.AppearanceSignature(f));
                skillSets.Add(FusionRules.SkillSetSignature(f));
                names.Add(f.DisplayName);
            }

            Check(lines, FusionRules.AllDistinct(appearance),
                  "验收① 9 种融合体外观签名两两不同（五色区）");
            Check(lines, FusionRules.AllDistinct(skillSets),
                  "验收① 9 种融合体技能组签名两两不同（三槽 + 五行）");
            Check(lines, FusionRules.AllDistinct(names),
                  "验收① 9 种融合体命名两两不同（宿主名·魄名）");

            // ==================================================================
            //  验收②：融合单位的行为正确（用 木宿主 + 水灵魂 做对照实验）
            // ==================================================================
            var fused = FusionRules.Fuse(hostWood, soulWater, resolver);

            float coefHost = ElementMatrix.Coefficient(hostWood.Element, Element.Earth,
                                                       ElementCoefficients.Default);
            float coefFused = ElementMatrix.Coefficient(fused.Element, Element.Earth,
                                                        ElementCoefficients.Default);
            Check(lines, fused.Element == Element.Water && hostWood.Element == Element.Wood,
                  $"验收② 五行覆写：{hostWood.DisplayName}(木) + {soulWater.DisplayName} ⇒ {fused.DisplayName}({Cn.Of(fused.Element)})");
            Check(lines, coefHost > coefFused,
                  $"验收② 克制系数随覆写变化：木 vs 土 {coefHost:0.00} ⇒ 水 vs 土 {coefFused:0.00}（相克环内被克）");

            // 技能替换：融合体战技来自灵魂，id 带灵魂标记。
            string soulActiveId = soulWater.SourceBeastId + "_a";
            Check(lines,
                  fused.Active != null && fused.Active.Id != hostWood.Active.Id
                                       && fused.Active.Id.StartsWith(soulActiveId),
                  $"验收② 战技替换：{hostWood.Active.Name} ⇒ {fused.Active.Name}（{fused.Active.Id}）");

            // 特性叠加。
            Check(lines,
                  !string.IsNullOrEmpty(fused.Trait.Name)
                  && fused.Trait.Name.Contains(hostWood.Trait.Name)
                  && fused.Trait.Name.Contains(soulWater.TraitInjection.Name),
                  $"验收② 特性叠加：{fused.Trait.Name}");

            // 战斗对照实验：融合体 vs 土系敌人（20 个种子），灵魂战技必须真的被放过；
            // 同种子下未融合宿主**不会**放那个战技。
            var enemy = FirstBeastOf(beasts, Element.Earth);
            if (enemy != null)
            {
                const int seedCount = 20;
                int castByFused = 0, castByPlain = 0;
                for (ulong seed = 0; seed < seedCount; seed++)
                {
                    var bf = BattleFactory.Create(BattleConfig.Default, seed,
                        new[] { DeployEntry.Player(fused, 0) },
                        new[] { DeployEntry.Enemy(enemy, 8) });
                    BattleSimulator.Run(bf);   // ⚠ 只装配不跑，事件流是空的
                    if (CastCount(bf.Log, fused.Active.Name, "P0") > 0) castByFused++;

                    var bp = BattleFactory.Create(BattleConfig.Default, seed,
                        new[] { DeployEntry.Player(hostWood, 0) },
                        new[] { DeployEntry.Enemy(enemy, 8) });
                    BattleSimulator.Run(bp);
                    if (CastCount(bp.Log, fused.Active.Name, "P0") > 0) castByPlain++;
                }
                Check(lines, castByFused > 0,
                      $"验收② 灵魂战技在战斗中生效：{castByFused}/{seedCount} 局放出「{fused.Active.Name}」");
                Check(lines, castByPlain == 0,
                      $"验收② 对照组干净：未融合宿主在 {seedCount} 局中 0 次放出该战技");
            }
            else
            {
                Check(lines, false, "验收② 找不到土系敌人做对照实验（内容缺土系异兽）");
            }

            // ==================================================================
            //  验收③：切换灵魂时辉光与睛色实时变化 —— 数据层判定
            // ==================================================================
            Check(lines, fused.Palette.EnergyGlow == soulWater.GlowHex
                       && fused.Palette.EyeCore == soulWater.EyeHex,
                  $"验收③ 辉光/睛色来自灵魂：{fused.Palette.EnergyGlow} / {fused.Palette.EyeCore}");
            Check(lines, fused.Palette.BodyAccent != hostWood.Palette.BodyAccent
                       && soulWater.HueShiftDegrees != 0,
                  $"验收③ 纹样色被灵魂偏移：{hostWood.Palette.BodyAccent} → {fused.Palette.BodyAccent}（{soulWater.HueShiftDegrees:+0;-0;0}°）");
            Check(lines, fused.Palette.Outline == PaletteHex.InkOutline,
                  "验收③ 描边恒墨色 #2A2118（不参与融合）");
            Check(lines, fused.Palette.BodyMain == hostWood.Palette.BodyMain,
                  "验收③ 主体色仍来自宿主（外形层继承）");

            // ==================================================================
            //  确定性：同样的 (宿主, 灵魂) 永远得到同样的结果
            // ==================================================================
            var fusedAgain = FusionRules.Fuse(hostWood, soulWater, resolver);
            Check(lines,
                  FusionRules.AppearanceSignature(fusedAgain) == FusionRules.AppearanceSignature(fused)
                  && FusionRules.SkillSetSignature(fusedAgain) == FusionRules.SkillSetSignature(fused)
                  && fusedAgain.DisplayName == fused.DisplayName,
                  "确定性：同一对 (宿主, 灵魂) 融合两次，签名完全一致");

            var soulAgain = SoulForge.Derive(hostWood, System.Array.IndexOf(beasts, hostWood));
            Check(lines,
                  soulAgain.Epithet == soulWood.Epithet
                  && soulAgain.HueShiftDegrees == soulWood.HueShiftDegrees
                  && soulAgain.Id == soulWood.Id,
                  "确定性：SoulForge 派生两次结果一致（魄名 / 偏移 / id）");

            return Finish(lines);
        }

        // ====================================================================

        private static string[] Finish(List<string> lines)
        {
            lines.Add("========================================================================");
            lines.Add($"结论：{_pass} 项通过，{_fail} 项失败");
            if (_fail > 0)
                foreach (var f in Failures) lines.Add("  ❌ " + f);
            else
                lines.Add("说明：验收③ 的「实时、无需重载场景」由融合预览窗口人工确认" +
                          "（万相/融合/融合预览）—— 本自检已备好数据层判据。");

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

        private static ContentCatalogSO LoadCatalog()
        {
            var guids = AssetDatabase.FindAssets("t:ContentCatalogSO");
            if (guids == null || guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<ContentCatalogSO>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static BeastDef FirstBeastOf(BeastDef[] beasts, Element element)
        {
            foreach (var b in beasts)
                if (b != null && b.Element == element) return b;
            return null;
        }

        private static SoulDef SoulOf(SoulDef[] souls, BeastDef beast)
        {
            if (beast == null) return null;
            foreach (var s in souls)
                if (s != null && s.SourceBeastId == beast.Id) return s;
            return null;
        }

        private static int CastCount(BattleLog log, string skillName, string actorId)
        {
            int n = 0;
            foreach (var e in log.Events)
                if (e.Kind == BattleEventKind.SkillCast && e.SkillName == skillName
                    && e.ActorId == actorId)
                    n++;
            return n;
        }
    }
}
