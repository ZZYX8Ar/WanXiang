// ============================================================================
//  万相 · 劫象与额外特性（GDD v1.1 §5.5）
//  ---------------------------------------------------------------------------
//  「劫象」挂在精英战的那只强化敌人身上（守关 Boss 从特性池另抽）——它的作用是
//  「让同一场战斗在不同种子里有不同的解法」，而不是单纯加数值。
//  所以每一条都必须**能被某种阵容选择对冲掉**（GDD 给的解法写在注释里）。
//
//  本批落地口径（诚实标注）：
//    ✅ 坚韧 / 锐锋 / 疾影 / 厚壁 —— 纯数值面（hp/atk/def/speed），直接改面板
//    ✅ 复苏 —— "阵亡 30% 复活 1 次"复用惊蛰虫卵的孵化机制（BattleUnit.Egg*）；
//       "治疗效果 -30%"待接（治疗接收侧还没有单位级乘区）
//    ⏳ 吞噬 —— "行动剥离 1 增益 + 自损 4%/回合"需要行动钩子，待接
//    ⏳ 锐锋的"受真伤 +20%"、疾影的"受控制时长 +1"、厚壁的"无法被治疗" 待接
//       （各需要一个单位级修正点，逐条接的时候别改公共乘区 —— 会污染无劫象路径）
// ============================================================================

namespace WanXiang.Battle.Core
{
    public static class BattleTraits
    {
        public const string Tenacity = "tenacity";       // 坚韧
        public const string Sharpedge = "sharpedge";     // 锐锋
        public const string Swiftshadow = "swiftshadow"; // 疾影
        public const string Thickwall = "thickwall";     // 厚壁
        public const string Revive = "revive";           // 复苏
        public const string Devour = "devour";           // 吞噬

        /// <summary>劫象池（§5.5，6 条）。抽签由提供方按种子做，这里只管定义。</summary>
        public static readonly string[] Pool =
        {
            Tenacity, Sharpedge, Swiftshadow, Thickwall, Revive, Devour,
        };

        public static string Cn(string id)
        {
            switch (id)
            {
                case Tenacity: return "坚韧";
                case Sharpedge: return "锐锋";
                case Swiftshadow: return "疾影";
                case Thickwall: return "厚壁";
                case Revive: return "复苏";
                case Devour: return "吞噬";
                default: return id ?? "（无）";
            }
        }

        /// <summary>把一条特性落到面板副本上（只动数值面；钩子类见文件头）。</summary>
        public static void Apply(BeastDef def, string traitId)
        {
            if (def == null || string.IsNullOrEmpty(traitId)) return;

            switch (traitId)
            {
                case Tenacity:      // 生命 +25%，但速度 -15%（解法：先手队规避它的承伤）
                    def.BaseHp = CoreMath.Max(1, CoreMath.RoundDamage(def.BaseHp * 1.25f));
                    def.BaseSpeed = CoreMath.Max(1, CoreMath.RoundDamage(def.BaseSpeed * 0.85f));
                    break;

                case Sharpedge:     // 攻击 +25%（受真伤 +20% 待接；解法：堆灼烧/冰蚀绕过防御）
                    def.BaseAtk = CoreMath.Max(1, CoreMath.RoundDamage(def.BaseAtk * 1.25f));
                    break;

                case Swiftshadow:   // 速度 +25%（受控制时长 +1 待接；解法：控制流）
                    def.BaseSpeed = CoreMath.Max(1, CoreMath.RoundDamage(def.BaseSpeed * 1.25f));
                    break;

                case Thickwall:     // 防御 +25%（无法被治疗待接；解法：禁疗与斩杀——禁疗对它无意义，斩杀是解）
                    def.BaseDef = CoreMath.Max(1, CoreMath.RoundDamage(def.BaseDef * 1.25f));
                    break;

                case Revive:        // 阵亡 30% 复活 1 次（BattleFactory 里挂虫卵；治疗 -30% 待接；解法：爆发）
                    break;

                case Devour:        // 行动剥离 1 增益 + 自损 4%/回合（待接；解法：速攻）
                    break;
            }
        }
    }
}
