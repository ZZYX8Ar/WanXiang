// ============================================================================
//  BossFxStyle —— 首领技能爆发的"个性化参数"（2026-10-26）
//  ---------------------------------------------------------------------------
//  五行只给了粗分类；同一个元素的 Boss 仍然会"看着一样"。
//  这里按 boss id 覆盖：半径倍数 / 条纹数 / 时长 / 颜色提亮。
//  未配置的走五行默认（与 BattleStage2D 里的 switch 一致）。
// ============================================================================
using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Battle.Presentation
{
    public struct FxBurstStyle
    {
        public float RadiusMul;
        public int Streaks;
        public float Dur;
        public float Brightness;   // 1 = 原色
        public int Shape;          // 0 环+条纹 / 1 双刃 / 2 六角冰晶 / 3 混沌漩涡
    }

    public static class BossFxStyle
    {
        /// <summary>按 id 取爆发参数；找不到返回 null（调用方用五行默认）。</summary>
        public static FxBurstStyle? For(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            switch (id)
            {
                // ---- 幕一 ----
                case "b_fuman":      return new FxBurstStyle { RadiusMul = 1.25f, Streaks = 8,  Dur = 0.75f, Brightness = 1.0f };  // 藤蔓缠生，慢而广
                case "b_manman":     return new FxBurstStyle { RadiusMul = 0.95f, Streaks = 26, Dur = 0.30f, Brightness = 1.1f };  // 缠丝，细密快
                case "b_wangliang":  return new FxBurstStyle { RadiusMul = 1.45f, Streaks = 12, Dur = 0.85f, Brightness = 0.85f }; // 瘴气，大而慢且暗

                // ---- 幕二 ----
                case "b_chiba":      return new FxBurstStyle { RadiusMul = 1.30f, Streaks = 26, Dur = 0.36f, Brightness = 1.15f }; // 赤魃爆燃
                case "b_jinjiao":    return new FxBurstStyle { RadiusMul = 1.05f, Streaks = 20, Dur = 0.32f, Brightness = 1.1f };
                case "b_jiaozhi":    return new FxBurstStyle { RadiusMul = 0.85f, Streaks = 10, Dur = 0.45f, Brightness = 0.95f }; // 硬壳，闷

                // ---- 幕三 ----
                case "b_bairen":     return new FxBurstStyle { RadiusMul = 1.20f, Streaks = 34, Dur = 0.26f, Brightness = 1.15f, Shape = 1 }; // 双刃
                case "b_shai":       return new FxBurstStyle { RadiusMul = 1.00f, Streaks = 18, Dur = 0.40f, Brightness = 1.05f };
                case "b_shuangfeng": return new FxBurstStyle { RadiusMul = 0.90f, Streaks = 30, Dur = 0.22f, Brightness = 1.2f };  // 霜锋极快

                // ---- 幕四 ----
                case "b_xuanming":   return new FxBurstStyle { RadiusMul = 1.40f, Streaks = 6,  Dur = 0.80f, Brightness = 1.0f, Shape = 2 };  // 六角冰晶
                case "b_mingkun":    return new FxBurstStyle { RadiusMul = 1.55f, Streaks = 4,  Dur = 0.95f, Brightness = 0.9f };  // 潮汐
                case "b_shuangying": return new FxBurstStyle { RadiusMul = 1.10f, Streaks = 20, Dur = 0.40f, Brightness = 1.05f, Shape = 2 };

                // ---- 幕五 ----
                case "b_guixu":      return new FxBurstStyle { RadiusMul = 1.60f, Streaks = 28, Dur = 0.70f, Brightness = 1.1f, Shape = 2 };
                case "b_hongmeng":   return new FxBurstStyle { RadiusMul = 1.75f, Streaks = 40, Dur = 0.75f, Brightness = 1.25f, Shape = 3 }; // 混沌漩涡

                // ---- 秘境镇守（rc_<hookId>）----
                case "rc_drown":     return new FxBurstStyle { RadiusMul = 1.35f, Streaks = 8,  Dur = 0.85f, Brightness = 0.9f , Shape = 3 };  // 沉沦
                case "rc_tide":      return new FxBurstStyle { RadiusMul = 1.45f, Streaks = 6,  Dur = 0.90f, Brightness = 1.0f , Shape = 2 };
                case "rc_bloodfury": return new FxBurstStyle { RadiusMul = 1.20f, Streaks = 30, Dur = 0.40f, Brightness = 1.25f };
                case "rc_emberblade":return new FxBurstStyle { RadiusMul = 0.95f, Streaks = 36, Dur = 0.24f, Brightness = 1.2f };
                case "rc_overload":  return new FxBurstStyle { RadiusMul = 1.15f, Streaks = 44, Dur = 0.30f, Brightness = 1.3f };
                case "rc_execute":   return new FxBurstStyle { RadiusMul = 0.80f, Streaks = 16, Dur = 0.20f, Brightness = 1.25f };
                case "rc_mute":      return new FxBurstStyle { RadiusMul = 1.50f, Streaks = 0,  Dur = 1.00f, Brightness = 0.75f , Shape = 0 }; // 静默：纯环
                case "rc_pendulum":  return new FxBurstStyle { RadiusMul = 1.10f, Streaks = 12, Dur = 0.60f, Brightness = 1.0f , Shape = 1 };
                case "rc_gamble":    return new FxBurstStyle { RadiusMul = 1.25f, Streaks = 24, Dur = 0.35f, Brightness = 1.15f , Shape = 3 };
                case "rc_allin":     return new FxBurstStyle { RadiusMul = 1.40f, Streaks = 32, Dur = 0.45f, Brightness = 1.3f , Shape = 3 };
            }
            return null;
        }
    }
}
