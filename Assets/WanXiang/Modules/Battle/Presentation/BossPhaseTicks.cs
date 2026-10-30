// ============================================================================
//  BossPhaseTicks —— 首领血条上的"阶段刻度"（2026-10-30）
//  ---------------------------------------------------------------------------
//  用户："归墟/赤魃这些有 75/50/25% 阶段，血条上看不出来"。
//  这里只是**表现层**的刻度表：与 `BossCatalog.AttachHooks` 里的 PhaseHook 阈值对应。
//  ⛔ 改机制阈值时记得同步这里（否则刻度和实际触发点对不上）。
// ============================================================================
namespace WanXiang.Battle.Presentation
{
    public static class BossPhaseTicks
    {
        /// <summary>返回该首领的相位阈值（0~1，血条从左到右）。空 = 无阶段。</summary>
        public static float[] For(string bossId)
        {
            switch (bossId)
            {
                case "b_fuman":      return new[] { 0.50f };                          // 枯荣
                case "b_chiba":      return new[] { 0.75f, 0.50f, 0.25f };            // 三段分身
                case "b_jinjiao":    return new[] { 0.40f };                          // 焚身（叠层翻倍）
                case "b_shuangfeng": return new[] { 0.75f, 0.50f, 0.25f };            // 三次全场
                case "b_xuanming":   return new[] { 0.35f };                          // 冰晶重生
                case "b_mingkun":    return new[] { 0.35f, 0.20f };                   // 深潜
                case "b_guixu":      return new[] { 0.30f };                          // 终焉之卵
                case "b_hongmeng":   return new[] { 0.50f };
                default:             return null;
            }
        }
    }
}
