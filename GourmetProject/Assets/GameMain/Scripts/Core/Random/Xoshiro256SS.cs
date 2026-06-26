using System;
using System.Collections.Generic;
using GourmetProject.Core.Diagnostics;

namespace GourmetProject.Core.Rng
{
    /// <summary>
    /// xoshiro256** 确定性伪随机数发生器。算法固定、跨平台跨运行时一致，质量高、速度快，
    /// 256 位状态可整体序列化。区间取值使用 Lemire 无偏算法避免取模偏差。
    /// </summary>
    public sealed class Xoshiro256SS : IRandomStream
    {
        private ulong _s0;
        private ulong _s1;
        private ulong _s2;
        private ulong _s3;

        /// <summary>用单个 64 位种子构造，内部经 SplitMix64 展开为 256 位状态。</summary>
        public Xoshiro256SS(ulong seed)
        {
            Seed(seed);
        }

        /// <summary>用一份完整状态构造（用于读档还原）。</summary>
        public Xoshiro256SS(RngState state)
        {
            State = state;
        }

        public void Seed(ulong seed)
        {
            ulong sm = seed;
            _s0 = SplitMix64.Next(ref sm);
            _s1 = SplitMix64.Next(ref sm);
            _s2 = SplitMix64.Next(ref sm);
            _s3 = SplitMix64.Next(ref sm);
            // 避免极小概率的全零状态（全零会使 xoshiro 退化为恒零序列）。
            if ((_s0 | _s1 | _s2 | _s3) == 0UL)
            {
                _s0 = 0x9E3779B97F4A7C15UL;
            }
        }

        public RngState State
        {
            get => new RngState(_s0, _s1, _s2, _s3);
            set
            {
                _s0 = value.S0;
                _s1 = value.S1;
                _s2 = value.S2;
                _s3 = value.S3;
            }
        }

        private static ulong Rotl(ulong x, int k)
        {
            return (x << k) | (x >> (64 - k));
        }

        public ulong NextULong()
        {
            unchecked
            {
                ulong result = Rotl(_s1 * 5UL, 7) * 9UL;
                ulong t = _s1 << 17;
                _s2 ^= _s0;
                _s3 ^= _s1;
                _s1 ^= _s2;
                _s0 ^= _s3;
                _s2 ^= t;
                _s3 = Rotl(_s3, 45);
                return result;
            }
        }

        public uint NextUInt()
        {
            // 取高 32 位，质量优于低位。
            return (uint)(NextULong() >> 32);
        }

        /// <summary>Lemire 无偏：返回 [0, bound) 的均匀整数。bound 为 0 时返回 0。</summary>
        private uint NextBoundedUInt(uint bound)
        {
            if (bound == 0u)
            {
                return 0u;
            }

            uint x = NextUInt();
            ulong m = (ulong)x * bound;
            uint low = (uint)m;
            if (low < bound)
            {
                uint threshold = (uint)(-(int)bound) % bound; // (2^32 - bound) % bound
                while (low < threshold)
                {
                    x = NextUInt();
                    m = (ulong)x * bound;
                    low = (uint)m;
                }
            }

            return (uint)(m >> 32);
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                // 倒置区间多半是配置写错，开发期告警暴露问题；空区间（==）是合法用法，不告警。
                if (maxExclusive < minInclusive)
                {
                    Log.Warning(
                        $"Range 收到倒置区间 [{minInclusive}, {maxExclusive})，已兜底返回下界 {minInclusive}，请检查配置。",
                        "Rng");
                }
#endif
                return minInclusive;
            }

            uint span = (uint)((long)maxExclusive - minInclusive);
            return minInclusive + (int)NextBoundedUInt(span);
        }

        public float Range(float minInclusive, float maxExclusive)
        {
            return minInclusive + NextFloat() * (maxExclusive - minInclusive);
        }

        public float NextFloat()
        {
            // 取高 24 位映射到 [0,1)。
            return (NextULong() >> 40) * (1.0f / (1u << 24));
        }

        public double NextDouble()
        {
            // 取高 53 位映射到 [0,1)。
            return (NextULong() >> 11) * (1.0 / (1UL << 53));
        }

        public bool NextBool(double probability = 0.5)
        {
            if (probability <= 0.0)
            {
                return false;
            }

            if (probability >= 1.0)
            {
                return true;
            }

            return NextDouble() < probability;
        }

        public void Shuffle<T>(IList<T> list)
        {
            if (list == null)
            {
                return;
            }

            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        public T Pick<T>(IReadOnlyList<T> list)
        {
            if (list == null || list.Count == 0)
            {
                throw new InvalidOperationException("Cannot pick from an empty list.");
            }

            return list[Range(0, list.Count)];
        }

        public int WeightedPickIndex(IReadOnlyList<float> weights)
        {
            if (weights == null || weights.Count == 0)
            {
                throw new InvalidOperationException("Weights must not be empty.");
            }

            double total = 0.0;
            for (int i = 0; i < weights.Count; i++)
            {
                float w = weights[i];
                if (w > 0f)
                {
                    total += w;
                }
            }

            if (total <= 0.0)
            {
                throw new InvalidOperationException("Sum of weights must be positive.");
            }

            double r = NextDouble() * total;
            double acc = 0.0;
            for (int i = 0; i < weights.Count; i++)
            {
                float w = weights[i];
                if (w <= 0f)
                {
                    continue;
                }

                acc += w;
                if (r < acc)
                {
                    return i;
                }
            }

            return weights.Count - 1; // 浮点累积误差兜底
        }
    }
}
