using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Library
{
    /// <summary>
    /// 菜品库的隐藏分加权随机：给定一个「要求隐藏分」，在隐藏分范围覆盖它的候选菜品中按权重随机取一个。
    ///
    /// 权重公式遵循策划文档：w = 基础权重 / max(|a - b|, c)，其中 a = 要求隐藏分，
    /// b = 菜品隐藏分均值，c 为距离下限，避免均值完全命中时除零。
    /// </summary>
    public sealed class DishLibrary
    {
        private readonly List<DishDef> _dishes;

        /// <summary>未显式指定时使用的距离下限 c。调用方应优先按进度从配置传入。</summary>
        public const int DefaultDistanceFloor = 5;

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
            return ComputeWeight(dish, requiredHidden, DefaultDistanceFloor);
        }

        public static float ComputeWeight(DishDef dish, int requiredHidden, int distanceFloor)
        {
            if (requiredHidden == 0)
            {
                return dish.BaseWeight;
            }

            float a = requiredHidden;
            float b = dish.HiddenMean;
            float distance = Math.Abs(a - b);
            float divisor = Math.Max(distance, Math.Max(1, distanceFloor));
            return Math.Max(0f, dish.BaseWeight) / divisor;
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
        /// <param name="distanceFloor">权重公式中的距离下限 c，按当前进度由调用方传入；缺省回退到 <see cref="DefaultDistanceFloor"/>。</param>
        public DishDef Roll(IRandomStream stream, int requiredHidden, Func<DishDef, bool> filter = null, int distanceFloor = DefaultDistanceFloor)
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
                float w = ComputeWeight(d, requiredHidden, distanceFloor);
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
