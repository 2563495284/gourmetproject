using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Library
{
    /// <summary>
    /// 菜品库的隐藏分加权随机：给定一个「要求隐藏分」，在隐藏分范围覆盖它的候选菜品中按权重随机取一个。
    ///
    /// 权重公式遵循策划文档：w = 1000 * |a - b| / a，其中 a = 要求隐藏分，b = 菜品隐藏分均值。
    /// 注意：当某菜品均值恰等于要求隐藏分时权重为 0；当全部候选权重之和为 0 时退化为均匀随机，避免无法取出。
    /// </summary>
    public sealed class DishLibrary
    {
        private readonly List<DishDef> _dishes;

        public DishLibrary(IEnumerable<DishDef> dishes)
        {
            if (dishes == null)
            {
                throw new ArgumentNullException(nameof(dishes));
            }

            _dishes = new List<DishDef>(dishes);
        }

        public IReadOnlyList<DishDef> Dishes => _dishes;

        /// <summary>计算单个菜品在给定要求隐藏分下的权重（含基础权重系数）。</summary>
        public static float ComputeWeight(DishDef dish, int requiredHidden)
        {
            if (requiredHidden == 0)
            {
                return dish.BaseWeight;
            }

            float a = requiredHidden;
            float b = dish.HiddenMean;
            float w = 1000f * Math.Abs(a - b) / Math.Abs(a);
            return w * Math.Max(0f, dish.BaseWeight) / 1000f;
        }

        /// <summary>
        /// 收集隐藏分范围覆盖要求隐藏分、且通过可选过滤的候选菜品。
        /// </summary>
        public List<DishDef> Candidates(int requiredHidden, Func<DishDef, bool> filter = null)
        {
            var result = new List<DishDef>();
            foreach (DishDef d in _dishes)
            {
                if (!d.CoversHiddenScore(requiredHidden))
                {
                    continue;
                }

                if (filter != null && !filter(d))
                {
                    continue;
                }

                result.Add(d);
            }

            return result;
        }

        /// <summary>
        /// 按隐藏分加权随机取一个菜品。无候选时返回 null。
        /// </summary>
        public DishDef Roll(IRandomStream stream, int requiredHidden, Func<DishDef, bool> filter = null)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            List<DishDef> candidates = Candidates(requiredHidden, filter);
            if (candidates.Count == 0)
            {
                return null;
            }

            var weights = new List<float>(candidates.Count);
            float total = 0f;
            foreach (DishDef d in candidates)
            {
                float w = ComputeWeight(d, requiredHidden);
                if (w < 0f)
                {
                    w = 0f;
                }

                weights.Add(w);
                total += w;
            }

            if (total <= 0f)
            {
                // 退化：全部权重为 0（例如均值恰等于要求隐藏分），改为均匀随机。
                return candidates[stream.Range(0, candidates.Count)];
            }

            int index = stream.WeightedPickIndex(weights);
            return candidates[index];
        }
    }
}
