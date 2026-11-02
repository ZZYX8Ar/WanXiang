// ============================================================================
//  万相 · 灵市定价（可外置）
//  ---------------------------------------------------------------------------
//  2026-11-02：把 MarketPanel 里的定价常量搬到一处，允许 Resources/Data/market.csv 覆盖。
//  ⛔ 价格公式只有一条：price = base(rarity) × (1 + (幕-1) × act_step) × 天时乘数，下限 min_price。
//    别在别的面板（孵穴/工坊）另写一份 —— 那会让"同一只兽两个价"。
// ============================================================================

namespace WanXiang.Run
{
    public static class MarketPricing
    {
        public static int PriceCommon = 5;   // 灵品异兽
        public static int PriceRare = 8;     // 玄品
        public static int PriceEpic = 12;    // 神品
        public static int PriceSoul = 5;     // 魂（局外解锁用）
        public static float ActStep = 0.30f; // 每幕涨价 30%
        public static int MinPrice = 2;      // 任何商品的价格下限

        /// <summary>已从外置表安装的键数（0 = 全内置）。</summary>
        public static int InstalledKeys { get; private set; }

        /// <summary>按稀有度取基础价。</summary>
        public static int BasePriceOf(WanXiang.Battle.Core.Rarity r)
        {
            switch (r)
            {
                case WanXiang.Battle.Core.Rarity.Legend: return PriceEpic;
                case WanXiang.Battle.Core.Rarity.Epic: return PriceRare;
                default: return PriceCommon;
            }
        }

        /// <summary>最终售价（含幕数上浮与天时乘数，含下限）。</summary>
        public static int FinalPrice(int basePrice, int act, float weatherMul)
        {
            float p = basePrice * (1f + (act - 1) * ActStep) * weatherMul;
            int v = (int)p;
            return v < MinPrice ? MinPrice : v;
        }

        /// <summary>parser 逐行调用；返回 false = 未知 key。</summary>
        public static bool TrySet(string key, float v)
        {
            switch (key)
            {
                case "price_common": PriceCommon = (int)v; break;
                case "price_rare": PriceRare = (int)v; break;
                case "price_epic": PriceEpic = (int)v; break;
                case "price_soul": PriceSoul = (int)v; break;
                case "act_step": ActStep = v; break;
                case "min_price": MinPrice = (int)v; break;
                default: return false;
            }
            InstalledKeys++;
            return true;
        }
    }
}
