// ============================================================================
//  万相 · 融合 · 异兽配置资产（宿主层）
//  ---------------------------------------------------------------------------
//  GDD 6.3 的 BeastConfig。字段与 beasts.json（extract_gdd_data.py 产出）逐条对应。
//  基础面板**不在这里**：GDD 没给数值表，占位面板由 BattleConfig.ApplyPlaceholderStats
//  按「职业 + 稀有度」生成 —— 等策划给正式表后只改那一处。
// ============================================================================

using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Fusion
{
    [CreateAssetMenu(fileName = "Beast_", menuName = "万相/融合/异兽")]
    public sealed class BeastConfigSO : ScriptableObject
    {
        [Header("标识")]
        public string BeastId;       // 拼音，如 jumang
        public string DisplayName;   // 句芒
        public Element Element;
        public RoleType Role;
        public Rarity Rarity;

        [Header("典籍与图鉴")]
        public string Source;        // 《山海经·海外东经》
        [TextArea(1, 3)] public string Quote;
        [TextArea(3, 8)] public string Lore;
        [TextArea(2, 5)] public string Codex;

        [Header("特性")]
        public string TraitName;
        [TextArea(2, 6)] public string TraitDescription;

        [Header("技能组（宿主骨架）")]
        public SkillConfigSO Basic;
        public SkillConfigSO Active;
        public SkillConfigSO Ultimate;

        [Header("色区（GDD 5.4 五通道）")]
        public string BodyMainHex;
        public string BodyAccentHex;
        public string EnergyGlowHex;
        public string EyeCoreHex;
        public string OutlineHex;    // 恒 #2A2118

        public BeastDef ToDef()
        {
            return new BeastDef
            {
                Id = BeastId,
                DisplayName = DisplayName,
                Element = Element,
                Role = Role,
                Rarity = Rarity,
                Source = Source,
                Quote = Quote,
                Lore = Lore,
                Codex = Codex,
                Trait = new TraitDef { Name = TraitName, Description = TraitDescription },
                Basic = Basic != null ? Basic.ToDef() : null,
                Active = Active != null ? Active.ToDef() : null,
                Ultimate = Ultimate != null ? Ultimate.ToDef() : null,
                Palette = new PaletteHex
                {
                    BodyMain = BodyMainHex,
                    BodyAccent = BodyAccentHex,
                    EnergyGlow = EnergyGlowHex,
                    EyeCore = EyeCoreHex,
                    Outline = OutlineHex,
                },
            };
        }
    }
}
