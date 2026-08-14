using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 装饰品和消耗品随机池与默认规则。装饰品不可重复进入随机池；消耗品按持有上限控制。
    /// </summary>
    public static class ItemPoolService
    {
        public static ItemAcquireResult GrantRandom(
            cfg.Tables tables,
            GameRun run,
            cfg.ItemKind kind,
            IRandomStream rng,
            int fallbackGold,
            MetaProgressSaveData progress = null)
        {
            List<string> picked = Roll(tables, run, kind, rng, 1, progress);
            if (picked.Count == 0)
            {
                run.Gold += fallbackGold;
                return new ItemAcquireResult(ItemAcquireOutcome.ConvertedToGold, null, null, 0, 0, fallbackGold);
            }

            return run.AcquireItem(picked[0], fallbackGold);
        }

        public static List<string> Roll(
            cfg.Tables tables,
            GameRun run,
            cfg.ItemKind kind,
            IRandomStream rng,
            int count,
            MetaProgressSaveData progress = null)
        {
            int hidden = HiddenScoreService.PassiveItemHiddenScore(run, run?.LastActionContext);
            int distanceFloor = System.Math.Max(1, tables.TbGameBase.HiddenScoreDistanceFloor);
            return Roll(tables, run, kind, rng, count, hidden, distanceFloor, requiredTag: cfg.ItemSpecialTag.None, progress);
        }

        public static List<string> Roll(
            cfg.Tables tables,
            GameRun run,
            cfg.ItemKind kind,
            IRandomStream rng,
            int count,
            int hidden,
            int distanceFloor,
            cfg.ItemSpecialTag requiredTag = cfg.ItemSpecialTag.None,
            MetaProgressSaveData progress = null)
        {
            var result = new List<string>();
            if (tables == null || run == null || rng == null || count <= 0)
            {
                return result;
            }

            bool activeItem = kind == cfg.ItemKind.Active;
            progress ??= run.MetaProgress;
            List<ItemDefinition> candidates = BuildCandidates(tables, run, kind, hidden, strictHidden: !activeItem, requiredTag, progress);
            if (candidates.Count == 0 && !activeItem)
            {
                candidates = BuildCandidates(tables, run, kind, hidden, strictHidden: false, requiredTag, progress);
            }

            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                var weights = new List<float>(candidates.Count);
                foreach (ItemDefinition item in candidates)
                {
                    weights.Add(GetWeight(item, hidden, distanceFloor, tables));
                }

                int index = rng.WeightedPickIndex(weights);
                result.Add(candidates[index].Id);
                if (!activeItem)
                {
                    candidates.RemoveAt(index);
                }
            }

            return result;
        }

        public static bool CanEnterPool(GameRun run, ItemDefinition item)
        {
            if (run == null || item == null)
            {
                return false;
            }

            if (item.Kind == cfg.ItemKind.Passive)
            {
                return !run.HasItem(item.Id);
            }

            return true;
        }

        /// <summary>
        /// 事件专用的定向物品抽取。仍沿用正常解锁、唯一性、隐藏分和基础权重规则；
        /// 消耗品可按指定 ID 或分类筛选，并按配置决定是否放回。
        /// </summary>
        public static List<ItemDefinition> RollFiltered(
            cfg.Tables tables,
            GameRun run,
            cfg.ItemKind kind,
            IRandomStream rng,
            int count,
            IReadOnlyCollection<string> allowedIds,
            cfg.ActiveItemCategory? activeCategory,
            bool? requireNegative,
            bool withReplacement,
            MetaProgressSaveData progress = null)
        {
            var result = new List<ItemDefinition>();
            if (tables == null || run == null || rng == null || count <= 0)
            {
                return result;
            }

            progress ??= run.MetaProgress;
            int hidden = HiddenScoreService.PassiveItemHiddenScore(run, run.LastActionContext);
            int distanceFloor = System.Math.Max(1, tables.TbGameBase.HiddenScoreDistanceFloor);
            List<ItemDefinition> candidates = BuildFilteredCandidates(
                tables,
                run,
                kind,
                allowedIds,
                activeCategory,
                requireNegative,
                hidden,
                strictHidden: kind == cfg.ItemKind.Passive,
                progress);
            if (candidates.Count == 0 && kind == cfg.ItemKind.Passive)
            {
                candidates = BuildFilteredCandidates(
                    tables,
                    run,
                    kind,
                    allowedIds,
                    activeCategory,
                    requireNegative,
                    hidden,
                    strictHidden: false,
                    progress);
            }

            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                var weights = new List<float>(candidates.Count);
                foreach (ItemDefinition item in candidates)
                {
                    weights.Add(GetWeight(item, hidden, distanceFloor, tables));
                }

                int index = rng.WeightedPickIndex(weights);
                result.Add(candidates[index]);
                if (!withReplacement)
                {
                    candidates.RemoveAt(index);
                }
            }

            return result;
        }

        public static float GetEffectValue(ItemDefinition item)
        {
            return item != null ? item.EffectValue : 0f;
        }

        private static List<ItemDefinition> BuildCandidates(
            cfg.Tables tables,
            GameRun run,
            cfg.ItemKind kind,
            int hidden,
            bool strictHidden,
            cfg.ItemSpecialTag requiredTag,
            MetaProgressSaveData progress)
        {
            var candidates = new List<ItemDefinition>();
            foreach (ItemDefinition item in ItemDefinition.All(tables, kind))
            {
                if (!CanEnterPool(run, item) || !MetaProgressService.IsItemUnlockedForPool(tables, item, progress))
                {
                    continue;
                }

                if (kind == cfg.ItemKind.Passive && !ItemTagFilter.MatchesFilter(item.SpecialTags, requiredTag))
                {
                    continue;
                }

                if (strictHidden && !CoversHidden(item, hidden))
                {
                    continue;
                }

                candidates.Add(item);
            }

            return candidates;
        }

        private static List<ItemDefinition> BuildFilteredCandidates(
            cfg.Tables tables,
            GameRun run,
            cfg.ItemKind kind,
            IReadOnlyCollection<string> allowedIds,
            cfg.ActiveItemCategory? activeCategory,
            bool? requireNegative,
            int hidden,
            bool strictHidden,
            MetaProgressSaveData progress)
        {
            var candidates = new List<ItemDefinition>();
            foreach (ItemDefinition item in ItemDefinition.All(tables, kind))
            {
                if (!CanEnterPool(run, item)
                    || !MetaProgressService.IsItemUnlockedForPool(tables, item, progress)
                    || !ContainsId(allowedIds, item.Id)
                    || (activeCategory.HasValue && (!item.IsActive || item.ActiveItemCategory != activeCategory.Value))
                    || (requireNegative.HasValue && (!item.IsPassive || item.IsNegative != requireNegative.Value))
                    || (strictHidden && !CoversHidden(item, hidden)))
                {
                    continue;
                }

                candidates.Add(item);
            }

            return candidates;
        }

        private static bool ContainsId(IReadOnlyCollection<string> allowedIds, string id)
        {
            if (allowedIds == null || allowedIds.Count == 0)
            {
                return true;
            }

            foreach (string allowedId in allowedIds)
            {
                if (allowedId == id)
                {
                    return true;
                }
            }

            return false;
        }

        private static float GetWeight(ItemDefinition item, int hidden, int distanceFloor, cfg.Tables tables)
        {
            tables ??= GameApp.Config.Tables;
            float defaultWeight = System.Math.Max(float.Epsilon, tables.TbGameBase.DefaultRandomWeight);
            float baseWeight = item.BaseWeight > 0f ? item.BaseWeight : defaultWeight;
            if (item.IsActive)
            {
                return baseWeight;
            }

            return RewardPoolService.HiddenScoreWeight(baseWeight, HiddenMean(item), hidden, distanceFloor);
        }

        private static bool CoversHidden(ItemDefinition item, int hidden)
        {
            if (item.HiddenRange == null || (item.HiddenRange.Min == 0 && item.HiddenRange.Max == 0))
            {
                return true;
            }

            return hidden >= item.HiddenRange.Min && hidden <= item.HiddenRange.Max;
        }

        private static float HiddenMean(ItemDefinition item)
        {
            if (item.HiddenRange == null || (item.HiddenRange.Min == 0 && item.HiddenRange.Max == 0))
            {
                return 0f;
            }

            return (item.HiddenRange.Min + item.HiddenRange.Max) * 0.5f;
        }
    }
}
