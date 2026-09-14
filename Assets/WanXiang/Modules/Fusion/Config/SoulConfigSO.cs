// ============================================================================
//  万相 · 融合 · 灵魂配置资产
//  ---------------------------------------------------------------------------
//  灵魂的资产壳。内容来源是导入器按 SoulForge 派生规则从 30 只异兽生成的
//  （GDD 没有独立的灵魂表 —— 派生规则见 SoulForge 文件头的设计决策记录）。
// ============================================================================

using System;
using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Fusion
{
    /// <summary>技能槽改写的资产表示：槽位 + 技能资产引用（运行期转成 id）。</summary>
    [Serializable]
    public sealed class SoulSkillOverride
    {
        public SkillType Slot;
        public SkillConfigSO Skill;
    }

    [CreateAssetMenu(fileName = "Soul_", menuName = "万相/融合/灵魂")]
    public sealed class SoulConfigSO : ScriptableObject
    {
        [Header("标识")]
        public string SoulId;          // 如 jumang_soul
        public string DisplayName;     // 句芒之魂
        public string Epithet;         // 魄名（融合名后半段）
        public string SourceBeastId;
        public Rarity Rarity;

        [Header("元气层")]
        public Element ElementOverride;   // None = 继承宿主
        [Range(-FusionRules.MaxHueShiftDegrees, FusionRules.MaxHueShiftDegrees)]
        public int HueShiftDegrees;
        public string GlowHex;
        public string EyeHex;

        [Header("特性注入")]
        public string TraitName;
        [TextArea(2, 6)] public string TraitDescription;

        [Header("技能槽改写")]
        public SoulSkillOverride[] SkillOverrides;

        public SoulDef ToSoulDef()
        {
            var overrides = SkillOverrides;
            SkillOverride[] list = null;
            if (overrides != null)
            {
                list = new SkillOverride[overrides.Length];
                for (int i = 0; i < overrides.Length; i++)
                {
                    list[i] = new SkillOverride(
                        overrides[i].Slot,
                        overrides[i].Skill != null ? overrides[i].Skill.SkillId : null);
                }
            }

            return new SoulDef
            {
                Id = SoulId,
                DisplayName = DisplayName,
                Epithet = Epithet,
                SourceBeastId = SourceBeastId,
                Rarity = Rarity,
                ElementOverride = ElementOverride,
                HueShiftDegrees = HueShiftDegrees,
                GlowHex = GlowHex,
                EyeHex = EyeHex,
                TraitInjection = new TraitDef { Name = TraitName, Description = TraitDescription },
                SkillOverrides = list,
            };
        }
    }
}
