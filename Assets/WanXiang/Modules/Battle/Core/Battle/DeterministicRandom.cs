// ============================================================================
//  万相 · 战斗核心 · 确定性随机
//  ---------------------------------------------------------------------------
//  绝不用 UnityEngine.Random：它是全局状态，两个战斗同时跑就会互相扰动，
//  而且它的序列随 Unity 版本变。
//
//  这里用 SplitMix64 播种 + xorshift128 产出：
//    - 纯整数运算，不依赖平台浮点行为
//    - 内部状态完全由 seed 决定，拷贝一个实例就能"冻结"随机流
//    - 同一 seed 在任何机器、任何 Unity 版本上产出同一串数
//
//  ⚠ 不要给这个类加"从系统时间播种"的构造函数。
//    默认播种一旦存在，就会有人顺手用它，然后可复现性就没了。
//    要非确定性，请调用方显式传一个别处来的 seed。
// ============================================================================

namespace WanXiang.Battle.Core
{
    public sealed class DeterministicRandom
    {
        private ulong _s0;
        private ulong _s1;

        public ulong Seed { get; }

        public DeterministicRandom(ulong seed)
        {
            Seed = seed;
            // SplitMix64 播种，把单个 seed 摊开成两个非零状态
            ulong z = seed + 0x9E3779B97F4A7C15UL;
            _s0 = SplitMix64(ref z);
            _s1 = SplitMix64(ref z);
            if (_s0 == 0 && _s1 == 0) _s1 = 0x9E3779B97F4A7C15UL;   // 全零状态会卡死
        }

        private static ulong SplitMix64(ref ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                ulong z = x;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public ulong NextULong()
        {
            unchecked
            {
                ulong s1 = _s0;
                ulong s0 = _s1;
                _s0 = s0;
                s1 ^= s1 << 23;
                _s1 = s1 ^ s0 ^ (s1 >> 17) ^ (s0 >> 26);
                return _s1 + s0;
            }
        }

        /// <summary>[0,1) 的浮点。用 24 位尾数（单精度能表达的位数），
        /// 这样 float 与 double 两条路径结果一致。</summary>
        public float NextFloat()
        {
            return (NextULong() >> 40) * (1.0f / 16777216.0f);   // 2^24
        }

        /// <summary>[min, max) 的整数。</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            uint range = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextULong() % range);
        }

        /// <summary>以 p 的概率返回 true。</summary>
        public bool Chance(float p)
        {
            if (p <= 0f) return false;
            if (p >= 1f) return true;
            return NextFloat() < p;
        }

        /// <summary>
        /// 派生子流。用途：让"每个单位自己的随机"与"全局随机"互不干扰 ——
        /// 否则给一只怪加一次随机判定，整局后续所有随机结果全部错位，
        /// 复盘时极难定位。派生流用单位身份做 salt。
        /// </summary>
        public DeterministicRandom Derive(string salt)
        {
            return new DeterministicRandom(Seed ^ CoreMath.Fnv1a(salt));
        }

        /// <summary>快照/还原内部状态，供"回放同一回合"这类调试用。</summary>
        public void Capture(out ulong s0, out ulong s1) { s0 = _s0; s1 = _s1; }
        public void Restore(ulong s0, ulong s1) { _s0 = s0; _s1 = s1; }
    }
}
