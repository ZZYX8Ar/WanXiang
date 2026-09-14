// ============================================================================
//  万相 · 融合核心 · 灵魂定义
//  ---------------------------------------------------------------------------
//  GDD 支柱二：「宿主（外形）+ 灵魂（元气）分离。同一只宿主配不同灵魂，
//  外观与技能组都会改变。」
//
//  本文件只定义「灵魂是什么」。它跟 BeastDef 一样是**纯数据**（本程序集
//  noEngineReferences），融合算法（FusionRules）在运行期只认这层抽象 ——
//  灵魂从哪来（内容表 / 孵蛋 / 分享码）是上层的事。
//
//  ⚠ 与 BeastDef 的分工（GDD 6.2 Fuse 的字段级对照）：
//      宿主继承：DisplayName 之外的外形与骨架 —— 模型/遮罩/骨骼/基础面板/稀有度
//      灵魂注入：五行覆写、辉光与睛色、bodyAccent 色相偏移、特性注入、技能槽改写
// ============================================================================

using WanXiang.Battle.Core;

namespace WanXiang.Fusion
{
    /// <summary>技能槽改写：把某个槽位替换成另一个技能。</summary>
    public struct SkillOverride
    {
        /// <summary>替换哪个槽位（普攻 / 战技 / 绝技）。</summary>
        public SkillType Slot;

        /// <summary>替换成的技能 id（运行时经内容库解析成 SkillDef）。
        /// 解析失败时回退为宿主原技能 —— 技能是内容数据，配错一个不该让战斗起不来。</summary>
        public string SkillId;

        public SkillOverride(SkillType slot, string skillId)
        {
            Slot = slot; SkillId = skillId;
        }
    }

    /// <summary>一个灵魂。外观上只带「元气层」的两个颜色（辉光 / 睛色），
    /// 行为上带五行覆写、特性注入与技能槽改写。</summary>
    public sealed class SoulDef
    {
        public string Id;               // 如 jumang_soul
        public string DisplayName;      // 句芒之魂
        public string Epithet;          // 魄名（两字），融合命名的后半段，如「霜魄」
        public string SourceBeastId;    // 来源异兽 id（图鉴 / 孵蛋溯源用；手工灵魂可为空）
        public Rarity Rarity;           // 来源稀有度，决定色相偏移幅度（见 SoulForge）

        /// <summary>融合后的五行。None = 继承宿主五行。</summary>
        public Element ElementOverride;

        /// <summary>bodyAccent 色相偏移（度，GDD 5.4 限定 ±30）。0 = 不偏。</summary>
        public int HueShiftDegrees;

        /// <summary>元气辉光色（energyGlow 覆写，权重 1.0，美术方案 2 章）。</summary>
        public string GlowHex;

        /// <summary>睛色（eyeCore 覆写）。</summary>
        public string EyeHex;

        /// <summary>注入的特性（叠加到宿主特性上，见 FusionRules.MergeTrait）。</summary>
        public TraitDef TraitInjection;

        /// <summary>技能槽改写列表。可为空（灵魂只改外观不改技能）。</summary>
        public SkillOverride[] SkillOverrides;

        /// <summary>融合后的外观签名：五个色区拼一起。验收①用它判定「外观不重复」。</summary>
        public string DescribePalette() =>
            $"{GlowHex}/{EyeHex}/shift{HueShiftDegrees:+0;-0;0}";

        public override string ToString() => $"{DisplayName}[{Cn.Of(ElementOverride)}]";
    }
}
