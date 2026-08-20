using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 装饰品统一两阶段随机：先按运气抽品质，再按该品质内的单品基础权重抽具体装饰品。
    /// </summary>
    public static class PassiveItemRandomService
    {
        public static bool IsNormalQuality(cfg.Tables tables, cfg.ItemQuality quality)
        {
            tables ??= GameApp.Config.Tables;
            return tables?.TbItemQualityLuck?.GetOrDefault(quality) != null;
        }

        public static double QualityWeight(cfg.Tables tables, cfg.ItemQuality quality, float luck)
        {
            tables ??= GameApp.Config.Tables;
            cfg.ItemQualityLuck config = tables?.TbItemQualityLuck?.GetOrDefault(quality);
            return QualityWeight(config, ItemLuckService.ClampLuck(tables, luck));
        }

        public static double QualityWeight(cfg.ItemQualityLuck config, float luck)
        {
            if (config == null)
            {
                return 0d;
            }

            double raw = config.Constant + config.BaseWeight * Math.Exp(luck * config.LuckGrowth);
            float min = Math.Min(config.MinWeight, config.MaxWeight);
            float max = Math.Max(config.MinWeight, config.MaxWeight);
            double weight = Math.Max(min, Math.Min(max, raw));
            return weight > 0d ? weight : 0d;
        }

        public static List<ItemDefinition> Roll(
            cfg.Tables tables,
            IReadOnlyList<ItemDefinition> source,
            IRandomStream rng,
            int count,
            float luck,
            bool withReplacement = false,
            bool bypassQualityRoll = false)
        {
            var result = new List<ItemDefinition>();
            if (source == null || rng == null || count <= 0)
            {
                return result;
            }

            var candidates = new List<ItemDefinition>(source);
            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                int index = bypassQualityRoll
                    ? PickItemIndex(tables, candidates, rng)
                    : PickTwoStageIndex(tables, candidates, rng, luck);
                if (index < 0)
                {
                    break;
                }

                result.Add(candidates[index]);
                if (!withReplacement)
                {
                    candidates.RemoveAt(index);
                }
            }

            return result;
        }

        private static int PickTwoStageIndex(
            cfg.Tables tables,
            IReadOnlyList<ItemDefinition> candidates,
            IRandomStream rng,
            float luck)
        {
            var qualities = new List<cfg.ItemQuality>();
            var qualityWeights = new List<float>();
            foreach (cfg.ItemQualityLuck config in tables.TbItemQualityLuck.DataList)
            {
                if (!ContainsQuality(candidates, config.Quality))
                {
                    continue;
                }

                double weight = QualityWeight(tables, config.Quality, luck);
                if (weight <= 0d)
                {
                    continue;
                }

                qualities.Add(config.Quality);
                qualityWeights.Add((float)Math.Min(float.MaxValue, weight));
            }

            if (qualities.Count == 0)
            {
                return -1;
            }

            cfg.ItemQuality selectedQuality = qualities.Count == 1
                ? qualities[0]
                : qualities[rng.WeightedPickIndex(qualityWeights)];
            return PickItemIndex(tables, candidates, rng, selectedQuality);
        }

        private static int PickItemIndex(
            cfg.Tables tables,
            IReadOnlyList<ItemDefinition> candidates,
            IRandomStream rng,
            cfg.ItemQuality? quality = null)
        {
            tables ??= GameApp.Config.Tables;
            float defaultWeight = Math.Max(float.Epsilon, tables.TbGameBase.DefaultRandomWeight);
            var candidateIndices = new List<int>();
            var weights = new List<float>();
            for (int i = 0; i < candidates.Count; i++)
            {
                ItemDefinition item = candidates[i];
                if (quality.HasValue && item.Quality != quality.Value)
                {
                    continue;
                }

                candidateIndices.Add(i);
                weights.Add(item.BaseWeight > 0f ? item.BaseWeight : defaultWeight);
            }

            if (candidateIndices.Count == 0)
            {
                return -1;
            }

            return candidateIndices.Count == 1
                ? candidateIndices[0]
                : candidateIndices[rng.WeightedPickIndex(weights)];
        }

        private static bool ContainsQuality(
            IReadOnlyList<ItemDefinition> candidates,
            cfg.ItemQuality quality)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Quality == quality)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
