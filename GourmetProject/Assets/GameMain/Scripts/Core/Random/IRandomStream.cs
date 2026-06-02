using System.Collections.Generic;

namespace GourmetProject.Core.Rng
{
    /// <summary>
    /// 一条确定性随机流。所有玩法随机都应通过它产生，禁止使用 UnityEngine.Random / System.Random，
    /// 以保证同一种子在任意平台、任意运行时版本下结果逐位一致，并可序列化续档。
    /// </summary>
    public interface IRandomStream
    {
        /// <summary>当前内部状态，序列化/读档时用于精确还原随机序列。</summary>
        RngState State { get; set; }

        /// <summary>返回 [0, uint.MaxValue] 区间的随机值。</summary>
        uint NextUInt();

        /// <summary>返回 [0, ulong.MaxValue] 区间的随机值。</summary>
        ulong NextULong();

        /// <summary>返回 [minInclusive, maxExclusive) 区间的整数；当区间为空或非法时返回 minInclusive。</summary>
        int Range(int minInclusive, int maxExclusive);

        /// <summary>返回 [minInclusive, maxExclusive) 区间的浮点。</summary>
        float Range(float minInclusive, float maxExclusive);

        /// <summary>返回 [0.0, 1.0) 区间的单精度浮点。</summary>
        float NextFloat();

        /// <summary>返回 [0.0, 1.0) 区间的双精度浮点。</summary>
        double NextDouble();

        /// <summary>以概率 probability 返回 true（probability 自动裁剪到 [0,1]）。</summary>
        bool NextBool(double probability = 0.5);

        /// <summary>原地 Fisher-Yates 洗牌。</summary>
        void Shuffle<T>(IList<T> list);

        /// <summary>从列表中等概率取一个元素（列表为空时抛出）。</summary>
        T Pick<T>(IReadOnlyList<T> list);

        /// <summary>按权重取一个下标；weights 长度需与候选数一致，权重之和需为正。</summary>
        int WeightedPickIndex(IReadOnlyList<float> weights);
    }
}
