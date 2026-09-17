// ============================================================================
//  WanXiang · 技能效果生成器（规则化变体 + 描述自文档化）
//  ---------------------------------------------------------------------------
//  菜单：
//    WanXiang / 内容 / 生成技能占位效果（补齐空缺）
//    WanXiang / 内容 / 强制重写技能占位效果
//
//  v2 相比 v1（随机占位）的关键变化：
//
//    1) **同职业不再千人一面**：以技能 id 的 FNV-1a 做种子，在"职业框架"内
//       生成合法变体 —— 倍率浮动、段数、目标选择、附加状态都由种子决定。
//       同一技能永远生成同一套效果（稳定、可复现、git diff 干净）。
//
//    2) **描述自文档化**：Description 不再是"（占位效果…）"，而是从 Effects
//       数组**反向生成**的中文描述 —— 面板上写什么，打起来就是什么。
//       数值表来了以后，"改描述"和"改数值"天然不会脱节。
//
//    3) **元素入技**：附加状态按技能五行挑选（火→灼烧、水→湿、金→裂甲、
//       土→谷、木→蚀），每只兽的绝技带自己的五行味。
//
//  ⚠ 这是**占位节奏**：GDD v1.1 只有技能名、没有效果设计（已核对正文）。
//    策划表（万相_设计文档/数据表/技能效果设计表.xlsx）落地后，
//    走 CSV 导入或重写本文件的 Rules 即可整体替换。
//    正式翻译的描述以「※」开头 —— 带※的技能本工具一律跳过，不会覆盖。
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
        private const string FinalDescMark = "※";   // 正式翻译描述的前缀标记

        [MenuItem("WanXiang/内容/生成技能占位效果（补齐空缺）", priority = 300)]
        public static void GenerateMissing() => Run(false);

        [MenuItem("WanXiang/内容/强制重写技能占位效果", priority = 301)]
        public static void ForceRegenerate()
        {
            if (!EditorUtility.DisplayDialog("强制重写技能效果",
                "会把全部技能的 Effects 覆盖成规则化占位，已翻译的正式效果（描述以 ※ 开头）不会动。\n" +
                "建议先提交 git。确定继续？", "继续", "取消")) return;
            RegenerateSilent();
        }

        /// <summary>无对话框入口（自动化/脚本调用）。语义同「强制重写」。</summary>
        public static void RegenerateSilent() => Run(true);

        private static void Run(bool force)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogSO>(CatalogPath);
            if (catalog == null || catalog.Beasts == null)
            {
                Debug.LogError("[SkillGen] 找不到内容目录：" + CatalogPath);
                return;
            }

            int written = 0, skippedFinal = 0, skippedKept = 0;
            foreach (var b in catalog.Beasts)
            {
                if (b == null) continue;
                TryWrite(b.Basic, b.Element, SkillType.Basic, force, ref written, ref skippedFinal, ref skippedKept);
                TryWrite(b.Active, b.Element, SkillType.Active, force, ref written, ref skippedFinal, ref skippedKept);
                TryWrite(b.Ultimate, b.Element, SkillType.Ultimate, force, ref written, ref skippedFinal, ref skippedKept);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SkillGen] 技能效果生成完成：写入 {written} 个；" +
                      $"已是正式翻译跳过 {skippedFinal} 个；" +
                      (force ? "" : $"已有占位保留 {skippedKept} 个（要重刷请用「强制重写」）。"));
        }

        private static void TryWrite(SkillConfigSO s, Element beastElement, SkillType type,
                                     bool force, ref int written, ref int skippedFinal, ref int skippedKept)
        {
            if (s == null) return;

            // 正式翻译保护：描述以 ※ 开头的技能视为已设计，永不覆盖
            if (!string.IsNullOrEmpty(s.Description) && s.Description.StartsWith(FinalDescMark))
            {
                skippedFinal++;
                return;
            }

            bool hasEffects = s.Effects != null && s.Effects.Length > 0;
            if (!force && hasEffects) { skippedKept++; return; }

            var rng = new System.Random(unchecked((int)Fnv1a(s.SkillId)));
            var effects = Rules(roleOf(s), type, beastElement, rng);
            s.Effects = effects;
            if (effects.Length > 0) s.PrimaryTarget = effects[0].Target;
            s.Description = Describe(effects, type);   // 描述自文档化：写什么打什么
            EditorUtility.SetDirty(s);
            written++;
        }

        private static RoleType roleOf(SkillConfigSO s)
        {
            // 技能本身不带职业 —— 用所属异兽的职业。目录反查开销可忽略（90 个）。
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogSO>(CatalogPath);
            if (catalog != null && catalog.Beasts != null)
            {
                foreach (var b in catalog.Beasts)
                {
                    if (b == null) continue;
                    if (SameSkill(b.Basic, s) || SameSkill(b.Active, s) || SameSkill(b.Ultimate, s))
                        return b.Role;
                }
            }
            return RoleType.Swift;
        }

        private static bool SameSkill(SkillConfigSO a, SkillConfigSO b)
        {
            return a != null && b != null && a.SkillId == b.SkillId;
        }

        private static uint Fnv1a(string text)
        {
            uint h = 2166136261u;
            foreach (char c in text ?? "") { h ^= c; h *= 16777619u; }
            return h;
        }

        // ==================================================================
        //  规则：职业框架 × 技能类型 × 种子 → 效果原子组
        //  数值梯度（总倍率口径）：普攻 0.7~1.2 / 战技 1.5~2.1 / 绝技 2.6~3.4
        // ==================================================================

        private static EffectAtom[] Rules(RoleType role, SkillType type, Element elem, System.Random rng)
        {
            float Roll(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
            int RollInt(int lo, int hi) => rng.Next(lo, hi + 1);

            // 五行 → 该系状态（顺序与 Types.cs 的 Element 枚举一致）
            string elemStatus = elem == Element.Fire ? StatusCatalog.Burn
                              : elem == Element.Water ? StatusCatalog.Wet
                              : elem == Element.Metal ? StatusCatalog.ArmorBreak
                              : elem == Element.Earth ? StatusCatalog.Grain
                              : elem == Element.Wood ? StatusCatalog.Corrode
                              : StatusCatalog.Marked;

            switch (role)
            {
                case RoleType.Guard:
                    if (type == SkillType.Basic)
                        return new[] { EffectAtom.Damage(TargetSelector.SingleLowestHp, Roll(0.8f, 1.1f)) };
                    if (type == SkillType.Active)
                        return new[]
                        {
                            EffectAtom.Shield(TargetSelector.Self, Roll(0.9f, 1.3f)),
                            EffectAtom.Damage(TargetSelector.SingleLowestHp, Roll(0.7f, 1.0f)),
                        };
                    return new[]
                    {
                        EffectAtom.Damage(TargetSelector.AllEnemies, Roll(0.7f, 0.95f)),
                        EffectAtom.Status(TargetSelector.AllEnemies, StatusCatalog.ArmorBreak, RollInt(2, 3), 2),
                        EffectAtom.Shield(TargetSelector.AllAllies, Roll(0.4f, 0.6f)),
                    };

                case RoleType.Striker:
                    if (type == SkillType.Basic)
                        return new[] { EffectAtom.Damage(TargetSelector.SingleLowestHp, Roll(0.9f, 1.2f)) };
                    if (type == SkillType.Active)
                        return new[] { EffectAtom.Damage(TargetSelector.SingleHighestAtk, Roll(1.6f, 2.0f)) };
                    return new[]
                    {
                        EffectAtom.Damage(TargetSelector.RandomEnemyMultiHit, Roll(0.5f, 0.7f), RollInt(4, 6)),
                        EffectAtom.Status(TargetSelector.RandomEnemy, elemStatus, 1, 2),
                    };

                case RoleType.Caster:
                    if (type == SkillType.Basic)
                        return new[] { EffectAtom.Damage(TargetSelector.SingleLowestHp, Roll(0.8f, 1.0f)) };
                    if (type == SkillType.Active)
                        return new[]
                        {
                            EffectAtom.Dot(TargetSelector.AllEnemies, elemStatus, Roll(0.03f, 0.05f), 1, 3),
                            EffectAtom.Damage(TargetSelector.AllEnemies, Roll(0.5f, 0.7f)),
                        };
                    return new[]
                    {
                        EffectAtom.Damage(TargetSelector.AllEnemies, Roll(1.2f, 1.5f)),
                        EffectAtom.Status(TargetSelector.AllEnemies, elemStatus, RollInt(1, 2), 3),
                    };

                case RoleType.Support:
                    if (type == SkillType.Basic)
                        return new[] { EffectAtom.Damage(TargetSelector.SingleLowestHp, Roll(0.6f, 0.8f)) };
                    if (type == SkillType.Active)
                        return rng.Next(2) == 0
                            ? new[] { EffectAtom.Heal(TargetSelector.SingleLowestHp, Roll(0.9f, 1.2f)) }
                            : new[] { EffectAtom.Shield(TargetSelector.SingleLowestHp, Roll(0.9f, 1.2f)) };
                    return new[]
                    {
                        EffectAtom.Heal(TargetSelector.AllAllies, Roll(0.5f, 0.7f)),
                        EffectAtom.Shield(TargetSelector.AllAllies, Roll(0.4f, 0.6f)),
                        EffectAtom.Dispel(TargetSelector.AllAllies),
                    };

                case RoleType.Swift:
                default:
                    if (type == SkillType.Basic)
                        return new[] { EffectAtom.Damage(TargetSelector.SingleLowestHp, Roll(0.7f, 0.95f)) };
                    if (type == SkillType.Active)
                        return new[] { EffectAtom.Damage(TargetSelector.SingleLowestHp, Roll(0.55f, 0.75f), 2) };
                    return new[]
                    {
                        EffectAtom.Damage(TargetSelector.RandomEnemy, Roll(1.4f, 1.8f)),
                        EffectAtom.Status(TargetSelector.RandomEnemy, elemStatus, 2, 2),
                    };
            }
        }

        // ==================================================================
        //  描述自文档化：从原子数组反向生成中文描述
        // ==================================================================

        private static string Describe(EffectAtom[] effects, SkillType type)
        {
            var parts = new List<string>();
            foreach (var e in effects) parts.Add(DescribeAtom(e));
            var head = type == SkillType.Ultimate ? "绝技：" : (type == SkillType.Active ? "战技：" : "普攻：");
            return head + string.Join("；", parts) + "。";
        }

        private static string DescribeAtom(EffectAtom e)
        {
            switch (e.Kind)
            {
                case EffectAtomKind.Damage:
                {
                    string t = TargetName(e.Target, enemy: true);
                    string dmg = e.Hits > 1
                        ? t + "造成 " + Pct(e.Power) + " 攻击 × " + e.Hits + " 段伤害"
                        : t + "造成 " + Pct(e.Power) + " 攻击伤害";
                    if (e.TrueDamage) dmg += "（真实伤害）";
                    else if (e.IgnoreShield) dmg += "（无视护盾）";
                    return dmg;
                }
                case EffectAtomKind.Heal:
                    string heal = e.PercentOfMaxHp > 0f
                        ? "回复 " + Pct(e.PercentOfMaxHp) + " 最大生命"
                        : "回复 " + Pct(e.Power) + " 攻击的生命";
                    return TargetName(e.Target, enemy: false) + heal;
                case EffectAtomKind.Shield:
                    string sh = e.PercentOfMaxHp > 0f
                        ? "获得 " + Pct(e.PercentOfMaxHp) + " 最大生命的护盾"
                        : "获得 " + Pct(e.Power) + " 攻击的护盾";
                    return TargetName(e.Target, enemy: false) + sh;
                case EffectAtomKind.ApplyStatus when e.Power > 0f:
                    // Dot 用 ApplyStatus 承载（Power>0 即每层每回合伤害），放前面先判
                    return TargetName(e.Target, enemy: true) + "附加「" + e.StatusId + "」" +
                           e.StatusStacks + " 层：每层每回合 " + Pct(e.Power) + " 攻击伤害，持续 " + e.StatusTurns + " 回合";
                case EffectAtomKind.ApplyStatus:
                    return TargetName(e.Target, enemy: false) + "施加「" + e.StatusId + "」" +
                           e.StatusStacks + " 层，持续 " + e.StatusTurns + " 回合";
                case EffectAtomKind.RemoveStatus:
                    return "驱散" + TargetName(e.Target, enemy: false) + "的减益";
                case EffectAtomKind.StatModifier:
                {
                    string up = e.StatDelta >= 0f ? "提升" : "降低";
                    string dur = e.StatTurns > 0 ? "，持续 " + e.StatTurns + " 回合" : "，本场有效";
                    return TargetName(e.Target, enemy: false) + up + e.StatKey + " " +
                           Pct(System.Math.Abs(e.StatDelta)) + dur;
                }
                default:
                    return "效果(" + e.Kind + ")";
            }
        }

        private static string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";

        /// <summary>目标选择器的中文描述。友/敌由原子种类决定（治疗增益给友方语义）。</summary>
        private static string TargetName(TargetSelector t, bool enemy)
        {
            switch (t)
            {
                case TargetSelector.Self: return "自身";
                case TargetSelector.SingleLowestHp: return enemy ? "生命最低的敌人" : "生命最低的队友";
                case TargetSelector.SingleHighestHp: return enemy ? "生命最高的敌人" : "生命最高的队友";
                case TargetSelector.SingleHighestAtk: return "攻击最高的敌人";
                case TargetSelector.AllEnemies: return "敌方全体";
                case TargetSelector.AllAllies: return "我方全体";
                case TargetSelector.RandomEnemy: return "随机敌人";
                case TargetSelector.RandomEnemyMultiHit: return "随机敌人";
                case TargetSelector.AdjacentToSelf: return "相邻单位";
                case TargetSelector.AllOthers: return "其余全场";
                default: return "目标";
            }
        }
    }
}
