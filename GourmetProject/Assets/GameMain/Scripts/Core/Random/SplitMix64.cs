namespace GourmetProject.Core.Rng
{
    /// <summary>
    /// SplitMix64：用于从单个 64 位种子快速、确定性地派生出多个互不相关的子种子。
    /// 算法固定、与平台/运行时无关，常用于初始化其它 PRNG 的状态，避免相邻种子产生相关序列。
    /// </summary>
    public static class SplitMix64
    {
        /// <summary>推进一步并返回输出值，同时更新引用传入的状态。</summary>
        public static ulong Next(ref ulong state)
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15UL;
                ulong z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>由一个种子直接生成一个混淆后的 64 位值（不修改入参）。</summary>
        public static ulong Mix(ulong seed)
        {
            return Next(ref seed);
        }
    }
}
