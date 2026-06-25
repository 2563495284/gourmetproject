using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Model;
using Log = GourmetProject.Core.Diagnostics.Log;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 胜利奖励池。负责把配置槽位解析为本次可展示的奖励候选。
    /// </summary>
    public static class RewardPoolService
    {
        private const string Tag = "RewardPool";

        public static List<RewardChoice> RollChoices(RewardContext context, cfg.RewardSlot slot)
        {
            var result = new List<RewardChoice>();
            if (context.Tables == null || context.Run == null || context.Rng == null || slot == null)
            {
                return result;
            }

            int count = Math.Max(1, slot.ChoiceCount);
            int hidden = Math.Max(0, HiddenForSlot(context, slot) + slot.HiddenOffset);
            if (slot.Kind == cfg.RewardKind.Gold)
            {
                result.Add(RewardChoice.Gold(GetFallbackGold(context, slot), "额外金币"));
                return result;
            }

            cfg.RewardPool pool = context.Tables.TbRewardPool.GetOrDefault(slot.PoolId);
            if (pool == null)
            {
                Log.Warning($"Reward slot '{slot.Id}' references missing pool '{slot.PoolId}'.", Tag);
                result.Add(RewardChoice.Gold(GetFallbackGold(context, slot), "折算金币", isFallback: true));
                return result;
            }

            switch (slot.Kind)
            {
                case cfg.RewardKind.DishChoice:
                    RollDishChoices(context, pool, hidden, count, result);
                    break;
                case cfg.RewardKind.PassiveItemChoice:
                    RollItemChoices(context, pool, cfg.ItemKind.Passive, hidden, count, result);
                    break;
                case cfg.RewardKind.ActiveItemGrant:
                    RollItemChoices(context, pool, cfg.ItemKind.Active, hidden, count, result);
                    break;
                case cfg.RewardKind.FragmentChoice:
                    RollFragmentChoices(context, pool, hidden, count, result);
                    break;
            }

            if (result.Count == 0)
            {
                Log.Warning($"Reward slot '{slot.Id}' produced no candidates. Converted to gold.", Tag);
                result.Add(RewardChoice.Gold(GetFallbackGold(context, slot), "折算金币", isFallback: true));
            }

            return result;
        }

        public static float HiddenScoreWeight(float baseWeight, float hiddenMean, int requiredHidden, int distanceFloor)
        {
            float weight = Math.Max(0f, baseWeight);
            if (requiredHidden <= 0)
            {
                return weight;
            }

            float distance = Math.Abs(requiredHidden - hiddenMean);
            float divisor = Math.Max(distance, Math.Max(1, distanceFloor));
            return weight / divisor;
        }

        private static void RollDishChoices(
            RewardContext context,
            cfg.RewardPool pool,
            int hidden,
            int count,
            List<RewardChoice> result)
        {
            var candidates = new List<DishDef>();
            foreach (DishDef dish in context.Run.Library.Dishes)
            {
                if (dish.CoversHiddenScore(hidden))
                {
                    candidates.Add(dish);
                }
            }

            if (candidates.Count == 0 && pool.AllowFallback)
            {
                candidates.AddRange(context.Run.Library.Dishes);
            }

            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                int index = PickHiddenWeighted(context, candidates, hidden, pool.DistanceFloor);
                DishDef dish = candidates[index];
                candidates.RemoveAt(index);
                result.Add(new RewardChoice(
                    cfg.RewardKind.DishChoice,
                    dish.Id,
                    dish.Name,
                    $"加入菜谱池，美味度 {dish.Deliciousness}"));
            }
        }

        private static void RollItemChoices(
            RewardContext context,
            cfg.RewardPool pool,
            cfg.ItemKind kind,
            int hidden,
            int count,
            List<RewardChoice> result)
        {
            Dictionary<cfg.ItemQuality, float> qualityWeights = ParseQualityWeights(pool.QualityWeights);
            List<cfg.Item> candidates = BuildItemCandidates(context, pool, kind, hidden, qualityWeights, strictHidden: true, strictTags: true, strictQuality: true);
            if (candidates.Count == 0 && pool.AllowFallback)
            {
                candidates = BuildItemCandidates(context, pool, kind, hidden, qualityWeights, strictHidden: false, strictTags: true, strictQuality: true);
            }

            if (candidates.Count == 0 && pool.AllowFallback)
            {
                candidates = BuildItemCandidates(context, pool, kind, hidden, qualityWeights, strictHidden: false, strictTags: false, strictQuality: true);
            }

            if (candidates.Count == 0 && pool.AllowFallback)
            {
                candidates = BuildItemCandidates(context, pool, kind, hidden, qualityWeights, strictHidden: false, strictTags: false, strictQuality: false);
            }

            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                var weights = new List<float>(candidates.Count);
                foreach (cfg.Item item in candidates)
                {
                    weights.Add(GetItemWeight(context.Run, item, qualityWeights, hidden, pool.DistanceFloor));
                }

                int index = PickWeightedOrUniform(context, weights, candidates.Count);
                cfg.Item chosen = candidates[index];
                candidates.RemoveAt(index);
                string desc = chosen.Kind == cfg.ItemKind.Passive ? $"被动道具 · {chosen.Quality}" : "主动道具";
                result.Add(new RewardChoice(
                    kind == cfg.ItemKind.Passive ? cfg.RewardKind.PassiveItemChoice : cfg.RewardKind.ActiveItemGrant,
                    chosen.Id,
                    chosen.Name,
                    desc));
            }
        }

        private static void RollFragmentChoices(
            RewardContext context,
            cfg.RewardPool pool,
            int hidden,
            int count,
            List<RewardChoice> result)
        {
            List<StomachFragmentDef> candidates = BuildFragmentCandidates(context, hidden, strictHidden: true);
            if (candidates.Count == 0 && pool.AllowFallback)
            {
                candidates = BuildFragmentCandidates(context, hidden, strictHidden: false);
            }

            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                int index = PickHiddenWeighted(context, candidates, hidden, pool.DistanceFloor);
                StomachFragmentDef fragment = candidates[index];
                candidates.RemoveAt(index);
                result.Add(new RewardChoice(
                    cfg.RewardKind.FragmentChoice,
                    fragment.Id,
                    fragment.Id,
                    $"扩展胃部，价格参考 {fragment.Price}",
                    HiddenScoreService.FragmentFallbackGold(context.Run, context.ActionContext)));
            }
        }

        private static List<cfg.Item> BuildItemCandidates(
            RewardContext context,
            cfg.RewardPool pool,
            cfg.ItemKind kind,
            int hidden,
            Dictionary<cfg.ItemQuality, float> qualityWeights,
            bool strictHidden,
            bool strictTags,
            bool strictQuality)
        {
            var candidates = new List<cfg.Item>();
            foreach (cfg.Item item in context.Tables.TbItem.DataList)
            {
                if (item.Kind != kind ||
                    !ItemPoolService.CanEnterPool(context.Run, item) ||
                    !PreconditionEvaluator.IsSatisfied(context.Run, item.UnlockCondition))
                {
                    continue;
                }

                if (strictHidden && !ItemCoversHidden(item, hidden))
                {
                    continue;
                }

                if (strictTags && !MatchesAnyTag(item.SpecialTags, pool.SpecialTags))
                {
                    continue;
                }

                if (strictQuality && qualityWeights.Count > 0 && !qualityWeights.ContainsKey(item.Quality))
                {
                    continue;
                }

                candidates.Add(item);
            }

            return candidates;
        }

        private static List<StomachFragmentDef> BuildFragmentCandidates(RewardContext context, int hidden, bool strictHidden)
        {
            var candidates = new List<StomachFragmentDef>();
            foreach (StomachFragmentDef fragment in context.Run.Database.AllFragments)
            {
                if (fragment.BaseWeight <= 0f || HasFragment(context.Run, fragment.Id))
                {
                    continue;
                }

                if (strictHidden && (hidden < fragment.HiddenMin || hidden > fragment.HiddenMax))
                {
                    continue;
                }

                if (!context.Run.CanAttachStomachFragment(fragment))
                {
                    continue;
                }

                candidates.Add(fragment);
            }

            return candidates;
        }

        private static bool HasFragment(GameRun run, string fragmentId)
        {
            for (int i = 0; i < run.StomachFragmentIds.Count; i++)
            {
                if (run.StomachFragmentIds[i] == fragmentId)
                {
                    return true;
                }
            }

            return false;
        }

        private static int PickHiddenWeighted(RewardContext context, IReadOnlyList<DishDef> candidates, int hidden, int distanceFloor)
        {
            var weights = new List<float>(candidates.Count);
            foreach (DishDef dish in candidates)
            {
                weights.Add(HiddenScoreWeight(dish.BaseWeight, dish.HiddenMean, hidden, distanceFloor));
            }

            return PickWeightedOrUniform(context, weights, candidates.Count);
        }

        private static int PickHiddenWeighted(RewardContext context, IReadOnlyList<StomachFragmentDef> candidates, int hidden, int distanceFloor)
        {
            var weights = new List<float>(candidates.Count);
            foreach (StomachFragmentDef fragment in candidates)
            {
                weights.Add(HiddenScoreWeight(fragment.BaseWeight, fragment.HiddenMean, hidden, distanceFloor));
            }

            return PickWeightedOrUniform(context, weights, candidates.Count);
        }

        private static int PickWeightedOrUniform(RewardContext context, IReadOnlyList<float> weights, int count)
        {
            float total = 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                total += Math.Max(0f, weights[i]);
            }

            return total > 0f ? context.Rng.WeightedPickIndex(weights) : context.Rng.Range(0, count);
        }

        private static float GetItemWeight(GameRun run, cfg.Item item, Dictionary<cfg.ItemQuality, float> qualityWeights, int hidden, int distanceFloor)
        {
            float weight = item.LevelWeightParams.BaseWeight > 0f ? item.LevelWeightParams.BaseWeight : 1f;
            weight = HiddenScoreWeight(weight, HiddenMean(item), hidden, distanceFloor);
            if (qualityWeights.Count > 0)
            {
                weight *= qualityWeights.TryGetValue(item.Quality, out float qualityWeight) ? Math.Max(0f, qualityWeight) : 0f;
            }

            RunItemState state = run.GetItemState(item.Id);
            if (item.Kind == cfg.ItemKind.Passive && state != null)
            {
                weight *= item.LevelWeightParams.NextLevelWeightMultiplier > 0f ? item.LevelWeightParams.NextLevelWeightMultiplier : 1f;
            }

            return weight;
        }

        private static int HiddenForSlot(RewardContext context, cfg.RewardSlot slot)
        {
            switch (slot.Kind)
            {
                case cfg.RewardKind.DishChoice:
                    return context.DishHiddenScore;
                case cfg.RewardKind.PassiveItemChoice:
                    return context.PassiveItemHiddenScore;
                case cfg.RewardKind.ActiveItemGrant:
                    return context.ActiveItemHiddenScore;
                case cfg.RewardKind.FragmentChoice:
                    return context.FragmentHiddenScore;
                default:
                    return context.RewardHiddenScore;
            }
        }

        private static bool ItemCoversHidden(cfg.Item item, int hidden)
        {
            if (item.HiddenRange.Min == 0 && item.HiddenRange.Max == 0)
            {
                return true;
            }

            return hidden >= item.HiddenRange.Min && hidden <= item.HiddenRange.Max;
        }

        private static float HiddenMean(cfg.Item item)
        {
            if (item.HiddenRange.Min == 0 && item.HiddenRange.Max == 0)
            {
                return 0f;
            }

            return (item.HiddenRange.Min + item.HiddenRange.Max) * 0.5f;
        }

        private static bool MatchesAnyTag(string itemTags, string requiredTags)
        {
            if (string.IsNullOrEmpty(requiredTags))
            {
                return true;
            }

            if (string.IsNullOrEmpty(itemTags))
            {
                return false;
            }

            string[] required = requiredTags.Split('|');
            for (int i = 0; i < required.Length; i++)
            {
                string tag = required[i].Trim();
                if (tag.Length == 0)
                {
                    continue;
                }

                string[] actual = itemTags.Split('|');
                for (int j = 0; j < actual.Length; j++)
                {
                    if (string.Equals(actual[j].Trim(), tag, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static Dictionary<cfg.ItemQuality, float> ParseQualityWeights(string spec)
        {
            var result = new Dictionary<cfg.ItemQuality, float>();
            if (string.IsNullOrEmpty(spec))
            {
                return result;
            }

            string[] entries = spec.Split('|');
            for (int i = 0; i < entries.Length; i++)
            {
                string[] parts = entries[i].Split(':');
                if (parts.Length != 2)
                {
                    continue;
                }

                if (!Enum.TryParse(parts[0].Trim(), out cfg.ItemQuality quality))
                {
                    continue;
                }

                if (float.TryParse(parts[1].Trim(), out float weight))
                {
                    result[quality] = weight;
                }
            }

            return result;
        }

        private static int GetFallbackGold(RewardContext context, cfg.RewardSlot slot)
        {
            if (slot.FallbackGold > 0)
            {
                return slot.FallbackGold;
            }

            return context.Package != null && context.Package.FallbackGold > 0 ? context.Package.FallbackGold : 20;
        }
    }
}
