// ============================================================================
//  万相 · 融合 · 技能配置资产
//  ---------------------------------------------------------------------------
//  GDD 6.3 的 SkillConfig。它只是**资产壳**：编辑器里可摆弄、可引用，
//  运行期真正参与战斗的是 ToDef() 转出来的纯数据 SkillDef（战斗核心不认 SO）。
//
//  ⚠ effects 是占位规则生成的（按职业 + 技能类型），GDD 的 90 条技能描述
//    原文保留在 description 里，逐条翻译成效果原子是后续内容工作
//    （Defs.cs 文件头有完整说明）。cd / 名字 / 类型 / 五行用的是 GDD 真值。
// ============================================================================

using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Fusion
{
    [CreateAssetMenu(fileName = "Skill_", menuName = "万相/融合/技能")]
    public sealed class SkillConfigSO : ScriptableObject
    {
        [Header("标识")]
        public string SkillId;      // 如 jumang_a
        public string SkillName;    // 青阳律

        [Header("规则")]
        public SkillType Type;
        public int Cd;              // 0 = 无冷却（普攻）
        public Element Element;     // 技能自身五行（默认跟宿主走）
        public TargetSelector PrimaryTarget;

        [Header("效果（占位规则生成，见文件头说明）")]
        public EffectAtom[] Effects;

        [TextArea(2, 6)]
        public string Description;  // GDD 原文，图鉴与调试对照用

        public SkillDef ToDef()
        {
            return new SkillDef
            {
                Id = SkillId,
                Name = SkillName,
                Type = Type,
                Cd = Cd,
                Element = Element,
                PrimaryTarget = PrimaryTarget,
                Effects = Effects == null ? null : (EffectAtom[])Effects.Clone(),
                Description = Description,
            };
        }
    }
}
