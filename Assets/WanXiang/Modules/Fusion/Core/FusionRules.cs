// ============================================================================
//  万相 · 融合核心 · 融合规则
//  ---------------------------------------------------------------------------
//  GDD 6.2 融合算法的字段级实现，外加两条工程约束：
//
//  1) **纯函数、零随机**。融合不掷骰子：同样的 (宿主, 灵魂) 永远得到同一个结果。
//     这与战斗核心「固定种子 100% 可复现」的判据同源 —— 随机性只允许出现在
//     战斗内部（DeterministicRandom），内容装配层一次随机都不能有。
//
//  2) **颜色也是数据**。GDD 5.4 / 美术方案 2 章把融合外观从资产问题降级为数据问题：
//     N 张宿主遮罩图 + M 个灵魂色板 = N×M 种外观，资源量只有 N+M。
//     本文件用 hex 字符串承载色区（不引 UnityEngine.Color），色相偏移自己实现。
//
//  融合三层的落地（美术方案 2 章的降级方案）：
//      外形层：完全继承宿主（模型 / 骨骼 / 面板 / 稀有度都不动）
//      元气层：辉光与睛色由灵魂覆写；bodyAccent 做一次色相偏移；描边恒墨色
//      技能层：宿主骨架 + 灵魂槽位改写（替换 / 解析失败回退宿主原技能）
// ============================================================================

using System.Collections.Generic;
using WanXiang.Battle.Core;

namespace WanXiang.Fusion
{
    /// <summary>技能 id → SkillDef 的解析器。由内容库提供（运行期），自检与预览用同一份。</summary>
    public delegate SkillDef SkillResolver(string skillId);

    public static class FusionRules
    {
        /// <summary>色相偏移上限（度）。GDD 5.4：「宿主原色，可被灵魂整体色相偏移 ±30°」。</summary>
        public const int MaxHueShiftDegrees = 30;

        /// <summary>
        /// 融合：宿主 + 灵魂 = 新单位。返回的 BeastDef 是一份**全新副本**，
        /// 与宿主定义不共享任何可变对象（技能全部 Clone）——
        /// 上阵时 BattleFactory 还会再克隆一层，两层各自独立改写互不污染。
        /// </summary>
        /// <param name="host">宿主（外形与骨架的来源）。</param>
        /// <param name="soul">灵魂（元气与改写的来源）。</param>
        /// <param name="resolveSkill">技能解析器。可为 null（等效于全部回退宿主原技能）。</param>
        public static BeastDef Fuse(BeastDef host, SoulDef soul, SkillResolver resolveSkill)
        {
            var fused = host.Clone();   // 外形层：完全继承宿主（含基础面板与稀有度）

            fused.Id = host.Id + "+" + soul.Id;
            fused.DisplayName = FusionNaming.Compose(host, soul);

            // ---- 元气层：灵魂注入辉光与睛色（GDD 6.2 / 美术方案 2 章） ----
            fused.Palette.EnergyGlow = NotEmpty(soul.GlowHex) ? soul.GlowHex : host.Palette.EnergyGlow;
            fused.Palette.EyeCore = NotEmpty(soul.EyeHex) ? soul.EyeHex : host.Palette.EyeCore;
            fused.Palette.BodyAccent =
                HueShiftHex(host.Palette.BodyAccent, soul.HueShiftDegrees);
            fused.Palette.Outline = PaletteHex.InkOutline;   // 恒为 #2A2118，任何情况不参与融合

            // ---- 五行覆写 ----
            if (soul.ElementOverride != Element.None)
                fused.Element = soul.ElementOverride;

            // ---- 技能层：宿主骨架 + 灵魂槽位改写 ----
            ApplySkillOverrides(fused, soul, resolveSkill);

            // ---- 特性叠加 ----
            fused.Trait = MergeTrait(host.Trait, soul.TraitInjection);

            return fused;
        }

        /// <summary>把灵魂的技能槽改写落到融合体上。槽位为空 / 解析失败时**保留宿主原技能**。</summary>
        private static void ApplySkillOverrides(BeastDef fused, SoulDef soul, SkillResolver resolveSkill)
        {
            if (soul.SkillOverrides == null) return;

            for (int i = 0; i < soul.SkillOverrides.Length; i++)
            {
                var ov = soul.SkillOverrides[i];
                if (string.IsNullOrEmpty(ov.SkillId)) continue;

                SkillDef resolved = resolveSkill?.Invoke(ov.SkillId);
                if (resolved == null) continue;   // 回退：内容缺一个技能不该毁一次融合

                var copy = resolved.Clone();      // 每个融合体独占一份，改写不外泄
                // 灵魂注入的技能标记来源，便于战斗日志与图鉴对照。
                copy.Id = copy.Id + "@" + soul.Id;

                switch (ov.Slot)
                {
                    case SkillType.Basic: fused.Basic = copy; break;
                    case SkillType.Active: fused.Active = copy; break;
                    case SkillType.Ultimate: fused.Ultimate = copy; break;
                }
            }
        }

        /// <summary>
        /// 特性叠加：宿主特性在前、灵魂注入在后，名字用「＋」连接、描述拼接。
        /// 注入为空（Name 与 Description 都空）时原样返回宿主特性。
        /// 注意特性**行为**仍按需逐条接（STEP 1 的约定不变），这里只负责数据叠加。
        /// </summary>
        public static TraitDef MergeTrait(TraitDef host, TraitDef injection)
        {
            bool empty = string.IsNullOrEmpty(injection.Name) && string.IsNullOrEmpty(injection.Description);
            if (empty) return host;

            return new TraitDef
            {
                Name = string.IsNullOrEmpty(host.Name)
                    ? injection.Name
                    : host.Name + "＋" + injection.Name,
                Description = string.IsNullOrEmpty(host.Description)
                    ? injection.Description
                    : host.Description + "\n灵魂注入 —— " + injection.Description,
            };
        }

        // ====================================================================
        //  颜色：hex → HSV → 偏移 → hex。纯实现，不引 UnityEngine.Color。
        // ====================================================================

        /// <summary>
        /// 对 "#RRGGBB" 做色相偏移（度）。解析失败 / 偏移为 0 时**原样返回** ——
        /// 色值是美术数据，坏一个不该让融合起不来（与 PaletteHex 的容错口径一致）。
        /// </summary>
        public static string HueShiftHex(string hex, int degrees)
        {
            if (degrees == 0 || !TryParseHex(hex, out int r, out int g, out int b))
                return hex;

            RgbToHsv(r, g, b, out double h, out double s, out double v);
            h = (h + degrees) % 360.0;
            if (h < 0) h += 360.0;
            HsvToRgb(h, s, v, out int r2, out int g2, out int b2);
            return ToHex(r2, g2, b2);
        }

        private static bool NotEmpty(string s) => !string.IsNullOrEmpty(s);

        internal static bool TryParseHex(string hex, out int r, out int g, out int b)
        {
            r = g = b = 0;
            if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#') return false;
            if (!TryByte(hex, 1, out r) | !TryByte(hex, 3, out g) | !TryByte(hex, 5, out b))
                return false;
            return true;
        }

        private static bool TryByte(string hex, int start, out int value)
        {
            int hi = Nibble(hex[start]), lo = Nibble(hex[start + 1]);
            if (hi < 0 || lo < 0) { value = 0; return false; }
            value = hi * 16 + lo;
            return true;
        }

        private static int Nibble(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        internal static string ToHex(int r, int g, int b) =>
            "#" + Byte(r) + Byte(g) + Byte(b);

        private static string Byte(int v)
        {
            v = v < 0 ? 0 : (v > 255 ? 255 : v);
            const string digits = "0123456789ABCDEF";
            return $"{digits[v >> 4]}{digits[v & 15]}";
        }

        internal static void RgbToHsv(int r, int g, int b,
                                      out double h, out double s, out double v)
        {
            double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
            double max = Max3(rf, gf, bf), min = Min3(rf, gf, bf);
            double d = max - min;

            v = max;
            s = max <= 0 ? 0 : d / max;

            if (d <= 0) { h = 0; return; }
            if (max == rf) h = 60.0 * (((gf - bf) / d) % 6.0);
            else if (max == gf) h = 60.0 * (((bf - rf) / d) + 2.0);
            else h = 60.0 * (((rf - gf) / d) + 4.0);
        }

        internal static void HsvToRgb(double h, double s, double v,
                                      out int r, out int g, out int b)
        {
            double c = v * s;
            double hp = (h % 360.0 + 360.0) % 360.0 / 60.0;
            double x = c * (1.0 - System.Math.Abs(hp % 2.0 - 1.0));
            double r1 = 0, g1 = 0, b1 = 0;
            switch ((int)hp)
            {
                case 0: r1 = c; g1 = x; break;
                case 1: r1 = x; g1 = c; break;
                case 2: g1 = c; b1 = x; break;
                case 3: g1 = x; b1 = c; break;
                case 4: r1 = x; b1 = c; break;
                default: r1 = c; b1 = x; break;
            }
            double m = v - c;
            r = (int)System.Math.Round((r1 + m) * 255.0);
            g = (int)System.Math.Round((g1 + m) * 255.0);
            b = (int)System.Math.Round((b1 + m) * 255.0);
        }

        private static double Max3(double a, double b, double c)
            => a > b ? (a > c ? a : c) : (b > c ? b : c);
        private static double Min3(double a, double b, double c)
            => a < b ? (a < c ? a : c) : (b < c ? b : c);

        // ====================================================================
        //  判重辅助（验收①「9 种外观与技能组均不重复」用）
        // ====================================================================

        /// <summary>外观签名：五个色区拼一起。</summary>
        public static string AppearanceSignature(BeastDef def) =>
            $"{def.Palette.BodyMain}|{def.Palette.BodyAccent}|{def.Palette.EnergyGlow}|" +
            $"{def.Palette.EyeCore}|{def.Palette.Outline}";

        /// <summary>技能组签名：三个槽位的 id + 五行。</summary>
        public static string SkillSetSignature(BeastDef def) =>
            $"{def.Element}|{SkillIdOf(def.Basic)}|{SkillIdOf(def.Active)}|{SkillIdOf(def.Ultimate)}";

        private static string SkillIdOf(SkillDef s) => s?.Id ?? "-";

        /// <summary>逐项判重的便捷器（自检用）。</summary>
        public static bool AllDistinct(IEnumerable<string> signatures)
        {
            var seen = new HashSet<string>();
            foreach (var s in signatures)
                if (!seen.Add(s)) return false;
            return true;
        }
    }
}
