// ============================================================================
//  WanXiang · 技能占位效果生成器
//  ---------------------------------------------------------------------------
//  菜单：WanXiang / 内容 / 生成技能占位效果（补齐空缺）
//
//  背景：ContentCatalog 里的 30 只异兽、90 个技能，SkillId / 名字 / CD / 类型
//        都是 GDD 真值，但 Effects 数组是空的 —— 战斗打起来"零伤害"就是这个原因。
//        本工具按「职业 + 技能类型」把占位规则写进资产，规则与
//        Editor/BattleTool/BattleSampleContent.cs 里的灰盒技能组完全一致，
//        所以补齐后自检与真机行为对得上。
//
//  ⚠ 只填空缺（Effects 为空或长度为 0 的技能），已有内容一律不动 —— 不会覆盖
//    后续逐条翻译的正式效果。想强制重写用「强制重写」菜单，用前先提交 git。
// ============================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Core;
using WanXiang.Fusion;

namespace WanXiang.EditorTools
{
    public static class SkillPlaceholderGenerator
    {
        private const string CatalogPath = "Assets/WanXiang/Config/ContentCatalog.asset";

        [MenuItem("WanXiang/内容/生成技能占位效果（补齐空缺）", priority = 300)]
        public static void GenerateMissing() => Run(false);

        [MenuItem("WanXiang/内容/强制重写技能占位效果", priority = 301)]
        public static void ForceRegenerate()
        {
            if (!EditorUtility.DisplayDialog("强制重写技能效果",
                "会把全部 90 个技能的 Effects 覆盖成占位规则，已翻译的正式效果会丢失。\n" +
                "请先提交 git。确定继续？", "继续", "取消")) return;
            Run(true);
        }

        private static void Run(bool force)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogSO>(CatalogPath);
            if (catalog == null || catalog.Beasts == null)
            {
                Debug.LogError("[SkillGen] 找不到内容目录：" + CatalogPath);
                return;
            }

            int filled = 0, skipped = 0, touchedAssets = 0;
            foreach (var b in catalog.Beasts)
            {
                if (b == null) continue;
                if (Apply(b.Basic, b.Role, SkillType.Basic, force)) { filled++; touchedAssets++; }
                else skipped++;
                if (Apply(b.Active, b.Role, SkillType.Active, force)) { filled++; touchedAssets++; }
                else skipped++;
                if (Apply(b.Ultimate, b.Role, SkillType.Ultimate, force)) { filled++; touchedAssets++; }
                else skipped++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SkillGen] 技能占位效果完成：写入 {filled} 个（跳过已有内容的 {skipped} 个）。" +
                      (force ? "（强制模式）" : ""));
        }

        private static bool Apply(SkillConfigSO s, RoleType role, SkillType type, bool force)
        {
            if (s == null) return false;
            if (!force && s.Effects != null && s.Effects.Length > 0) return false;

            var effects = Rules(role, type);
            s.Effects = effects;
            if (effects.Length > 0) s.PrimaryTarget = effects[0].Target;
            if (string.IsNullOrEmpty(s.Description))
                s.Description = "（占位效果：按职业 " + role + " + 技能类型 " + type + " 生成）";
            EditorUtility.SetDirty(s);
            return true;
        }

        /// <summary>占位规则 —— 与 BattleSampleContent.BuildSkills 同源，改一处要同步另一处。</summary>
        private static EffectAtom[] Rules(RoleType role, SkillType type)
        {
            switch (role)
            {
                case RoleType.Guard:
                    if (type == SkillType.Basic)
                        return new[] { EffectAtom.Damage(TargetSelector.SingleLowestHp, 1.00f) };
                    if (type == SkillType.Active)
                        return new[] { EffectAtom.Shield(TargetSelector.Self, 0.80f) };
                    return new[]
                    {
                        EffectAtom.Damage(TargetSelector.AllEnemies, 0.80f),
                        EffectAtom.Status(TargetSelector.AllEnemies, StatusCatalog.ArmorBreak, 2, 2),
                    };

                case RoleType.Striker:
                    if (type == SkillType.Basic)
                        return new[] { EffectAtom.Damage(TargetSelector.SingleLowestHp, 1.00f) };
                    if (type == SkillType.Active)
                        return new[] { EffectAtom.Damage(TargetSelector.SingleHighestAtk, 1.80f) };
                    return new[] { EffectAtom.Damage(TargetSelector.RandomEnemyMultiHit, 0.60f, 5) };

                case RoleType.Caster:
                    if (type == SkillType.Basic)
                        return new[] { EffectAtom.Damage(TargetSelector.SingleLowestHp, 0.90f) };
                    if (type == SkillType.Active)
                        return new[] { EffectAtom.Dot(TargetSelector.AllEnemies, StatusCatalog.Burn, 0.04f, 1, 3) };
                    return new[]
                    {
                        EffectAtom.Damage(TargetSelector.AllEnemies, 1.30f),
                        EffectAtom.Status(TargetSelector.AllEnemies, StatusCatalog.Wet, 1, 3),
                    };

                case RoleType.Support:
                    if (type == SkillType.Basic)
                        return new[] { EffectAtom.Damage(TargetSelector.SingleLowestHp, 0.70f) };
                    if (type == SkillType.Active)
                        return new[] { EffectAtom.Heal(TargetSelector.SingleLowestHp, 0.90f) };
                    return new[]
                    {
                        EffectAtom.Heal(TargetSelector.AllAllies, 0.50f),
                        EffectAtom.Shield(TargetSelector.AllAllies, 0.50f),
                        EffectAtom.Dispel(TargetSelector.AllAllies),
                    };

                case RoleType.Swift:
                default:
                    if (type == SkillType.Basic)
                        return new[] { EffectAtom.Damage(TargetSelector.SingleLowestHp, 0.80f) };
                    if (type == SkillType.Active)
                        return new[] { EffectAtom.Damage(TargetSelector.SingleLowestHp, 1.20f, 2) };
                    return new[]
                    {
                        EffectAtom.Damage(TargetSelector.RandomEnemy, 1.40f),
                        EffectAtom.Status(TargetSelector.RandomEnemy, StatusCatalog.Frost, 2, 3),
                    };
            }
        }
    }
}
