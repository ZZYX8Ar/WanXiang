// ============================================================================
//  万相 · 活链路天气容器（运行期，非存档）
//  ---------------------------------------------------------------------------
//  活地图流程（CampaignPanel 选节点）用「节点天时 + 重放 run.Path 还原的余气」
//  合成一份 WeatherDef 塞进 Current；BattleRequestFactory / FormationPanel 读取
//  它作为本场天时，传给 BattleFactory。
//
//  ⚠ 不进存档 schema：WeatherDef / LingerEntry 不可 Json 序列化（含数组与钩子），
//    所以这里只是一个「切档/新局时由调用方重置」的运行时黑板。
//  ⭐ 可复现性红线：Current 为 null = 本场无天时，战斗行为与旧版逐位一致
//    （所有结算点都包在 st.Weather != null 判空里）。
// ============================================================================

using WanXiang.Battle.Core;

namespace WanXiang.Campaign
{
    /// <summary>
    /// 当前这场战斗要用的天时。选节点时由 CampaignPanel 重算并写入；
    /// 非战役战斗（遭遇战/试炼）应把它复位成 null，避免上一场战役的天气串场。
    /// </summary>
    public static class LiveWeather
    {
        /// <summary>本场真实天时（WeatherDef）。null = 无天时（旧行为）。</summary>
        public static WeatherDef Current;
    }

    /// <summary>
    /// 天气对「地图玩法」的修正解析（Batch 2 天气地图效果层）。
    /// 读 <see cref="LiveWeather.Current"/>（当前节点真实天时）；为 null ⇒ 全部中性（旧行为逐位一致）。
    /// ⚠ 数值为占位平衡（待策划确认），集中在这里方便一处调参；不要散落到各面板。
    /// </summary>
    public static class WeatherMapEffects
    {
        /// <summary>当前节点天气的地图修正；天气为 null 时返回中性。</summary>
        public static MapEffects Current
            => LiveWeather.Current != null ? Resolve(LiveWeather.Current) : MapEffects.Neutral;

        /// <summary>一份地图修正。</summary>
        public sealed class MapEffects
        {
            public float PriceMul = 1f;       // 灵市物价乘数（&lt;1 便宜，&gt;1 贵）
            public int HealBonusPct = 0;      // 孵穴回复额外百分比（可负；BanHeal 时 -100 让回复失效）
            public float EggRewardMul = 1f;   // 节点灵卵奖励乘数（胜利 +1、孵穴 +2 等）

            public static MapEffects Neutral => new MapEffects();
        }

        private static MapEffects Resolve(WanXiang.Battle.Core.WeatherDef w)
        {
            var e = new MapEffects();
            // ★ 占位平衡（待策划确认）：以天气「属性」做基础倾向，禁疗天气单独处理。
            //   注：本作地图节点间移动免费，没有"移动消耗"机制，故不在此列（见提交说明）。
            switch (w.Element)
            {
                case WanXiang.Battle.Core.Element.Water:
                    e.PriceMul = 0.9f;        // 水令：水路通商，物价 -10%
                    e.HealBonusPct = 10;      // 润泽，孵穴回复 +10%
                    break;
                case WanXiang.Battle.Core.Element.Wood:
                    e.HealBonusPct = 15;      // 木令：生机，孵穴回复 +15%
                    break;
                case WanXiang.Battle.Core.Element.Fire:
                    e.PriceMul = 1.1f;        // 火令：旱燥，物价 +10%
                    break;
                case WanXiang.Battle.Core.Element.Metal:
                    e.EggRewardMul = 1.15f;   // 金令：肃杀，战利 +15%
                    break;
                case WanXiang.Battle.Core.Element.Earth:
                    e.EggRewardMul = 1.1f;    // 土令：厚载，战利 +10%
                    break;
            }
            if (w.BanHeal) e.HealBonusPct = -100;   // 小雪禁疗：孵穴「回复」选项直接失效（玩家应改取灵卵）
            return e;
        }
    }
}
