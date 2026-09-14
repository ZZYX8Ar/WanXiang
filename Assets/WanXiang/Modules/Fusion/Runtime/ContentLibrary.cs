// ============================================================================
//  万相 · 融合 · 内容库
//  ---------------------------------------------------------------------------
//  资产壳 → 纯数据的单向转换入口。战斗核心（noEngineReferences）只认
//  BeastDef / SoulDef / SkillDef，这里负责把 ContentCatalogSO 翻成它们。
//
//  ⚠ 转换是**无状态纯函数**：同一份目录永远转出同一批数据。
//    融合的确定性从「目录 → 数据」这一步就开始了，不是只靠战斗内部。
// ============================================================================

using System.Collections.Generic;
using WanXiang.Battle.Core;

namespace WanXiang.Fusion
{
    public static class ContentLibrary
    {
        /// <summary>把目录里全部异兽转成 BeastDef。null 宿主技能的槽位保持 null（战斗装配时按缺省处理）。</summary>
        public static BeastDef[] BuildBeasts(ContentCatalogSO catalog)
        {
            if (catalog == null || catalog.Beasts == null) return System.Array.Empty<BeastDef>();
            var result = new BeastDef[catalog.Beasts.Length];
            for (int i = 0; i < catalog.Beasts.Length; i++)
                result[i] = catalog.Beasts[i] != null ? catalog.Beasts[i].ToDef() : null;
            return result;
        }

        /// <summary>把目录里全部灵魂转成 SoulDef。</summary>
        public static SoulDef[] BuildSouls(ContentCatalogSO catalog)
        {
            if (catalog == null || catalog.Souls == null) return System.Array.Empty<SoulDef>();
            var result = new SoulDef[catalog.Souls.Length];
            for (int i = 0; i < catalog.Souls.Length; i++)
                result[i] = catalog.Souls[i] != null ? catalog.Souls[i].ToSoulDef() : null;
            return result;
        }

        /// <summary>技能 id → SkillDef。融合的槽位改写靠它解析（解析失败回退宿主技能）。</summary>
        public static Dictionary<string, SkillDef> BuildSkillMap(ContentCatalogSO catalog)
        {
            var map = new Dictionary<string, SkillDef>();
            if (catalog == null || catalog.Beasts == null) return map;

            for (int i = 0; i < catalog.Beasts.Length; i++)
            {
                var b = catalog.Beasts[i];
                if (b == null) continue;
                AddSkill(map, b.Basic);
                AddSkill(map, b.Active);
                AddSkill(map, b.Ultimate);
            }
            return map;
        }

        /// <summary>便捷封装：从目录构造一个技能解析器（融合规则的分发点）。</summary>
        public static SkillResolver ResolverFrom(ContentCatalogSO catalog)
        {
            var map = BuildSkillMap(catalog);
            return skillId => map.TryGetValue(skillId, out var def) ? def : null;
        }

        private static void AddSkill(Dictionary<string, SkillDef> map, SkillConfigSO so)
        {
            if (so == null || string.IsNullOrEmpty(so.SkillId)) return;
            if (!map.ContainsKey(so.SkillId)) map.Add(so.SkillId, so.ToDef());
        }
    }
}
