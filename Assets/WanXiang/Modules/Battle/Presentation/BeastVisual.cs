// ============================================================================
//  万相 · 异兽视觉收口（B5-2，Presentation）
//  ---------------------------------------------------------------------------
//  品级色表 + shader 参数 + 异色叠加的**唯一收口**——避免品级参数散落在
//  BattleStage2D 里（shader 表现设计 v1 §2.2 的要求）。
//  挂点：BattleStage2D 建立单位视图时，按 Def.Rarity / 异色名单调用 Apply*。
//  ⚠ Presentation 层不引用 Meta —— 异色名单由 BattleRequest.ShinyIds 带进来
//    （工厂从 MetaState 折算），本类只按名单设参数。
// ============================================================================
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    public static class BeastVisual
    {
        /// <summary>品级描边色表（设计 v1 §2.2：灵=冷灰 玄=冷青 神=暖金）。</summary>
        public static Color TierColor(int rarity)
        {
            switch (rarity)
            {
                case 2: return new Color(0.55f, 0.80f, 0.92f);   // 玄品：冷青
                case 3: return new Color(1.00f, 0.82f, 0.38f);   // 神品：暖金
                default: return new Color(0.72f, 0.72f, 0.74f);  // 灵品（含 None）：冷灰
            }
        }

        /// <summary>品级参数：描边色 + 呼吸光晕（灵/玄弱、神强）+ 神品斜流光。</summary>
        public static void ApplyTier(Material m, int rarity)
        {
            if (m == null || !m.HasProperty("_TierColor")) return;
            m.SetColor("_TierColor", TierColor(rarity));
            m.SetFloat("_RimWidth", 0.004f);
            bool legend = rarity == 3;
            m.SetFloat("_GlowBreath", legend ? 0.22f : 0.10f);
            m.SetFloat("_BreathSpeed", 0.8f);
            m.SetFloat("_SweepSpeed", legend ? 0.35f : 0f);   // 神品才有流光
        }

        /// <summary>异色叠加：色相 ±18°（0.06）+ 周期扫光 0.35（shader 里 2.5s 周期斜 45°）。</summary>
        public static void ApplyShiny(Material m)
        {
            if (m == null) return;
            if (m.HasProperty("_HueShift")) m.SetFloat("_HueShift", 0.06f);
            if (m.HasProperty("_ShinySweep")) m.SetFloat("_ShinySweep", 0.35f);
        }
    }
}
