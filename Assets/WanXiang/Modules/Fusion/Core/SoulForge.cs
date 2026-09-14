// ============================================================================
//  万相 · 融合核心 · 灵魂锻炉（异兽 → 灵魂的派生规则）
//  ---------------------------------------------------------------------------
//  ⚠ 设计决策记录（GDD 只给了 SoulConfig 的字段引用，没给灵魂表 —— 这层规则
//    是本工程补的，改动前先想清楚对内容量的影响）：
//
//  GDD 4.1：「灵品 Rare 易获得，是**融合与技能草稿的消耗素材**」；
//  GDD 1 章：「收集异兽、融合灵魂」。⇒ 灵魂从异兽**派生**：
//  每只异兽都有对应的灵魂，融合 = 把一只异兽的「魂」注进另一只异兽的「体」。
//  这与美术方案 2 章「N 张宿主图 + M 个灵魂色板 = N×M 外观，资源量 N+M」
//  完全同构 —— 内容侧零新增美术。
//
//  派生规则（全部确定性，无随机）：
//    五行覆写 = 来源异兽的五行          （木魂注进火体，火体改行木）
//    辉光/睛色 = 来源异兽色板的对应两区  （灵魂的元气层就是它生前的元气）
//    色相偏移幅度 = 按来源稀有度 12/20/30（都压在 GDD 5.4 的 ±30 内），
//                  方向按 id 哈希奇偶 —— 稳定但两种方向都有
//    技能改写 = 战技槽 ← 来源异兽的战技  （普攻/绝技是「骨架记忆」，战技是「魂技」；
//                                          这样每个灵魂都带来一次**可观察**的战技替换）
//    魄名     = 按五行池选取，序号取内容表顺序 —— 两字古典意象词，融合名后半段
// ============================================================================

using WanXiang.Battle.Core;

namespace WanXiang.Fusion
{
    public static class SoulForge
    {
        /// <summary>稀有度 → 色相偏移幅度（度）。都压在 GDD 5.4 的 ±30 内。</summary>
        public static int HueShiftOf(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Legend: return 30;
                case Rarity.Epic: return 20;
                default: return 12;
            }
        }

        /// <summary>
        /// 从一只异兽派生它的灵魂。
        /// </summary>
        /// <param name="beast">来源异兽。</param>
        /// <param name="ordinal">该异兽在内容表中的序号（0 起），用于稳定选魄名。
        /// 同一只异兽无论哪次导入都传同一个序号 ⇒ 魄名稳定。</param>
        public static SoulDef Derive(BeastDef beast, int ordinal)
        {
            var pool = EpithetPool(beast.Element);
            string epithet = pool[System.Math.Abs(ordinal) % pool.Length];

            int magnitude = HueShiftOf(beast.Rarity);
            int sign = (CoreMath.Fnv1a(beast.Id) & 1u) == 0u ? 1 : -1;

            return new SoulDef
            {
                Id = beast.Id + "_soul",
                DisplayName = FusionNaming.SoulDisplayName(beast.DisplayName),
                Epithet = epithet,
                SourceBeastId = beast.Id,
                Rarity = beast.Rarity,
                ElementOverride = beast.Element,
                HueShiftDegrees = sign * magnitude,
                GlowHex = beast.Palette.EnergyGlow,
                EyeHex = beast.Palette.EyeCore,
                TraitInjection = new TraitDef
                {
                    Name = "魂·" + beast.Trait.Name,
                    Description = beast.Trait.Description,
                },
                SkillOverrides = new[]
                {
                    new SkillOverride(SkillType.Active, beast.Id + "_a"),
                },
            };
        }

        // ====================================================================
        //  魄名池：五行各 6 个两字意象词（原创，取古典意象再创作）。
        //  与 30 只异兽一一对应（每行 6 个），新增异兽时循环复用。
        // ====================================================================

        private static readonly string[] WoodPool =
            { "青芜", "拂柳", "春霆", "碧涛", "扶苏", "栖梧" };
        private static readonly string[] FirePool =
            { "燎原", "炽羽", "朱明", "离火", "赤霄", "焚野" };
        private static readonly string[] EarthPool =
            { "厚壤", "坤灵", "培风", "中岳", "息壤", "黄埃" };
        private static readonly string[] MetalPool =
            { "霜刃", "肃金", "白藏", "鸣鸿", "皎霜", "折锋" };
        private static readonly string[] WaterPool =
            { "玄冰", "沧溟", "凝渊", "寒潭", "沉璧", "沧浪" };

        private static string[] EpithetPool(Element e)
        {
            switch (e)
            {
                case Element.Wood: return WoodPool;
                case Element.Fire: return FirePool;
                case Element.Earth: return EarthPool;
                case Element.Metal: return MetalPool;
                case Element.Water: return WaterPool;
                default: return new[] { "无名" };
            }
        }
    }
}
