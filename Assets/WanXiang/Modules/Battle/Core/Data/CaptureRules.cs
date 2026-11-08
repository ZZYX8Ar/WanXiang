// ============================================================================
//  万相 · 捕获规则（P3-a / 纯函数层）
//  ---------------------------------------------------------------------------
//  设计 v2 §10：
//    成功率 = 45% − 敌方剩余血量% × 0.35 + 灵卵加成（每 1 灵卵 +3%，上限 +15%）
//             + 道痕「游猎」（capture_rate 通道，按百分点追加）
//    封顶 85%；失败返还 50% 灵卵；首领不可捕获；一局上限由调用方管。
//
//  ⛔ 只做数学与规则判定，不碰 UI / 存档 / 战斗循环。
//  ⚠ 首领判定复用 BossCatalog.IsBoss（id 以 b_ 开头）；召唤物 summon_ 也不算可捕。
// ============================================================================
namespace WanXiang.Battle.Core
{
    public static class CaptureRules
    {
        public const int BasePercent = 45;      // 满血基础成功率
        public const int HpWeightPercent = 35;  // 血量权重：满血扣 35 点
        public const int EggBonusPerEgg = 3;    // 每灵卵 +3 点
        public const int EggBonusCap = 15;      // 灵卵加成上限
        public const int MaxPercent = 85;       // 成功率封顶
        public const int MinPercent = 5;        // 成功率保底（别让残血怪也抓不到）

        /// <summary>该单位能否被捕获：首领与召唤物不可。</summary>
        public static bool IsCapturable(BeastDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.Id)) return false;
            if (BossCatalog.IsBoss(def.Id)) return false;
            if (def.Id.StartsWith("summon_")) return false;
            return true;
        }

        /// <summary>捕获消耗的灵卵（按品级：灵 1 / 玄 2 / 神 3）。</summary>
        public static int EggCost(Rarity r)
        {
            switch (r)
            {
                case Rarity.Rare: return 1;
                case Rarity.Epic: return 2;
                case Rarity.Legend: return 3;
                default: return 1;
            }
        }

        /// <summary>失败返还（50%，向下取整，至少 1）。</summary>
        public static int Refund(int eggs) => eggs <= 0 ? 0 : System.Math.Max(1, eggs / 2);

        /// <summary>
        /// 成功率（百分点 0~100）。
        /// <paramref name="hpPct"/>：目标剩余血量百分比（0~100，越残越好抓）。
        /// <paramref name="eggs"/>：本次投入的灵卵数。
        /// <paramref name="daoBonus"/>：道痕「游猎」的百分点（0.08 → 8 点）。
        /// </summary>
        public static int ChancePercent(int hpPct, int eggs, float daoBonus)
        {
            if (hpPct < 0) hpPct = 0;
            if (hpPct > 100) hpPct = 100;

            int p = BasePercent - hpPct * HpWeightPercent / 100;
            int eggBonus = eggs * EggBonusPerEgg;
            if (eggBonus > EggBonusCap) eggBonus = EggBonusCap;
            p += eggBonus;
            if (daoBonus > 0f) p += (int)System.Math.Round(daoBonus * 100f);

            if (p > MaxPercent) p = MaxPercent;
            if (p < MinPercent) p = MinPercent;
            return p;
        }

        /// <summary>是否判定成功（调用方给随机数 0~1）。</summary>
        public static bool Roll(int chancePercent, float roll01)
            => roll01 * 100f < chancePercent;
    }
}
