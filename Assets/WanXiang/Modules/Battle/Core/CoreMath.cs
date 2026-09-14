// ============================================================================
//  万相 · 战斗核心 · 自带的数学与哈希
//  ---------------------------------------------------------------------------
//  本程序集 noEngineReferences=true，用不了 Mathf / UnityEngine.Random。
//  这里把需要的那一小撮自己实现掉，顺便把"确定性"这件事抓实：
//  所有取整、四舍五入、随机都只有一个实现，不给"换个 Math.Round 重载结果就变了"
//  留空间。
// ============================================================================

namespace WanXiang.Battle.Core
{
    public static class CoreMath
    {
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Abs(float v) => v < 0f ? -v : v;

        /// <summary>
        /// 伤害取整。**全项目只走这一个入口。**
        /// 注意用的是「向下取整 + 0.5 后取整」这套 banker's rounding 的简化版：
        /// 直接 (int)(v + 0.5f) 会让负数处理出错，但伤害恒为正，够用；
        /// 关键是**只有一处实现**，不会出现两处取整方式不同导致对不上账。
        /// </summary>
        public static int RoundDamage(float v)
        {
            if (v <= 0f) return 0;
            return (int)(v + 0.5f);
        }

        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        /// <summary>
        /// FNV-1a 32 位。用来给战斗事件流算指纹 —— 验证"两次跑完全一致"靠它。
        /// 选它是因为实现短、无依赖、结果稳定（不像 string.GetHashCode 在不同运行时有随机化）。
        /// </summary>
        public static uint Fnv1a(string s)
        {
            unchecked
            {
                uint h = 2166136261u;
                if (s == null) return h;
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= (byte)s[i];
                    h *= 16777619u;
                    h ^= (byte)(s[i] >> 8);
                    h *= 16777619u;
                }
                return h;
            }
        }

        /// <summary>把两个指纹合成一个（用于逐条事件滚动累积）。</summary>
        public static uint Combine(uint h, uint v)
        {
            unchecked
            {
                h ^= v + 0x9E3779B9u + (h << 6) + (h >> 2);
                return h;
            }
        }
    }
}
