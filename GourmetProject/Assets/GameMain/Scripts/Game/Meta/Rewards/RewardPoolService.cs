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
            // 多选一「可选数量」增减（琳琅满目 / 选择困难 / 少选择）：仅作用于食物多选一。
            if (slot.Kind == cfg.RewardKind.DishChoice && context.Run != null)
            {
                count = Math.Max(1, count + new ItemRuntime(context.Run).ChoiceCountDelta());
            }

            int hidden = Math.Max(0, HiddenForSlot(context, slot) + HiddenOffsetForSlot(context, slot));
            if (slot.Kind == cfg.RewardKind.Gold)
            {
                result.Add(RewardChoice.Gold(RollGoldRewardAmount(context, slot), "额外金币"));
                return result;
            }

            if (context.Run != null)
            {
                count = Math.Max(1, count + context.Run.ConsumeEventChoiceCountDelta());
            }

            cfg.RewardPool pool = context.Tables.TbRewardPool.GetOrDefault(slot.PoolId);
            if (pool == null)
            {
                Log.Warning($"Reward slot '{slot.Id}' references missing pool '{slot.PoolId}'.", Tag);
                result.Add(RewardChoice.Gold(RollGoldRewardAmount(context, slot), "折算金币", isFallback: true));
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
                result.Add(RewardChoice.Gold(RollGoldRewardAmount(context, slot), "折算金币", isFallback: true));
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

                string flavorId = pool.WithRandomFlavor ? PickRandomFlavorId(context) : string.Empty;
                string desc = string.IsNullOrEmpty(flavorId)
                    ? $"加入菜谱池，美味度 {dish.Deliciousness}"
                    : $"加入菜谱池并附带随机风味，美味度 {dish.Deliciousness}";
                result.Add(new RewardChoice(
                    cfg.RewardKind.DishChoice,
                    dish.Id,
                    dish.Name,
                    desc,
                    flavorId: flavorId));
            }
        }

        private static string PickRandomFlavorId(RewardContext context)
        {
            IReadOnlyCollection<FlavorDef> all = context.Run?.Database?.AllFlavors;
            if (all == null || all.Count == 0 || context.Rng == null)
            {
                return string.Empty;
            }

            IReadOnlyList<FlavorDef> flavors = all as IReadOnlyList<FlavorDef> ?? new List<FlavorDef>(all);
            return flavors[context.Rng.Range(0, flavors.Count)].Id;
        }

        private static void RollItemChoices(
            RewardContext context,
            cfg.RewardPool pool,
            cfg.ItemKind kind,
            int hidden,
            int count,
            List<RewardChoice> result)
        {
            // 显式 id 池（如「从指定道具列表随机」）：直接按 id 建候选，不走隐藏分/标签/解锁筛选。
            if (!string.IsNullOrEmpty(pool.ExplicitIds))
            {
                List<ItemDefinition> explicitCandidates = BuildExplicitCandidates(context, pool, kind);
                for (int i = 0; i < count && explicitCandidates.Count > 0; i++)
                {
                    int idx = context.Rng.Range(0, explicitCandidates.Count);
                    ItemDefinition picked = explicitCandidates[idx];
                    explicitCandidates.RemoveAt(idx);
                    result.Add(BuildItemChoice(kind, picked));
                }

                return;
            }

            bool activeItem = kind == cfg.ItemKind.Active;
            List<ItemDefinition> candidates = BuildItemCandidates(context, pool, kind, hidden, strictHidden: !activeItem, strictTags: true, strictQuality: true);
            if (candidates.Count == 0 && pool.AllowFallback)
            {
                candidates = BuildItemCandidates(context, pool, kind, hidden, strictHidden: false, strictTags: true, strictQuality: true);
            }

            if (candidates.Count == 0 && pool.AllowFallback)
            {
                candidates = BuildItemCandidates(context, pool, kind, hidden, strictHidden: false, strictTags: false, strictQuality: true);
            }

            // 品质下限兜底：如传奇缺失时回退到较低品质，避免「奖励池为空」。
            if (candidates.Count == 0 && pool.AllowFallback && pool.MinQuality > 0)
            {
                candidates = BuildItemCandidates(context, pool, kind, hidden, strictHidden: false, strictTags: false, strictQuality: false);
            }

            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                var weights = new List<float>(candidates.Count);
                foreach (ItemDefinition item in candidates)
                {
                    weights.Add(GetItemWeight(item, hidden, pool.DistanceFloor));
                }

                int index = PickWeightedOrUniform(context, weights, candidates.Count);
                ItemDefinition chosen = candidates[index];
                candidates.RemoveAt(index);
                result.Add(BuildItemChoice(kind, chosen));
            }
        }

        private static RewardChoice BuildItemChoice(cfg.ItemKind kind, ItemDefinition item)
        {
            string desc = item.IsPassive ? $"被动道具 · {item.Quality}" : "主动道具";
            return new RewardChoice(
                kind == cfg.ItemKind.Passive ? cfg.RewardKind.PassiveItemChoice : cfg.RewardKind.ActiveItemGrant,
                item.Id,
                item.Name,
                desc);
        }

        private static List<ItemDefinition> BuildExplicitCandidates(RewardContext context, cfg.RewardPool pool, cfg.ItemKind kind)
        {
            var candidates = new List<ItemDefinition>();
            string[] ids = pool.ExplicitIds.Split('|', ';', ',');
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i].Trim();
                if (id.Length == 0)
                {
                    continue;
                }

                ItemDefinition item = ItemDefinition.Get(context.Tables, id, kind);
                if (item != null)
                {
                    candidates.Add(item);
                }
            }

            return candidates;
        }

        private static void RollFragmentChoices(
            RewardContext context,
            cfg.RewardPool pool,
            int hidden,
            int count,
            List<RewardChoice> result)
        {
            List<TableFragmentDef> candidates = BuildFragmentCandidates(context, hidden, strictHidden: true);
            if (candidates.Count == 0 && pool.AllowFallback)
            {
                candidates = BuildFragmentCandidates(context, hidden, strictHidden: false);
            }

            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                int index = PickHiddenWeighted(context, candidates, hidden, pool.DistanceFloor);
                TableFragmentDef fragment = candidates[index];
                candidates.RemoveAt(index);
                result.Add(new RewardChoice(
                    cfg.RewardKind.FragmentChoice,
                    fragment.Id,
                    fragment.Id,
                    $"扩展餐桌",
                    HiddenScoreService.FragmentFallbackGold(context.Run, context.ActionContext)));
            }
        }

        private static List<ItemDefinition> BuildItemCandidates(
            RewardContext context,
            cfg.RewardPool pool,
            cfg.ItemKind kind,
            int hidden,
            bool strictHidden,
            bool strictTags,
            bool strictQuality)
        {
            var candidates = new List<ItemDefinition>();
            MetaProgressSaveData progress = context.Progress ?? MetaProgressPersistence.Load();
            foreach (ItemDefinition item in ItemDefinition.All(context.Tables, kind))
            {
                if (!ItemPoolService.CanEnterPool(context.Run, item) ||
                    !MetaProgressService.IsItemUnlockedForPool(context.Tables, item, progress))
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

                if (strictQuality && pool.MinQuality > 0 && (int)item.Quality < pool.MinQuality)
                {
                    continue;
                }

                candidates.Add(item);
            }

            return candidates;
        }

        private static List<TableFragmentDef> BuildFragmentCandidates(RewardContext context, int hidden, bool strictHidden)
        {
            var candidates = new List<TableFragmentDef>();
            foreach (TableFragmentDef fragment in context.Run.Database.AllFragments)
            {
                if (fragment.BaseWeight <= 0f || HasFragment(context.Run, fragment.Id))
                {
                    continue;
                }

                if (strictHidden && (hidden < fragment.HiddenMin || hidden > fragment.HiddenMax))
                {
                    continue;
                }

                if (!context.Run.CanAttachTableFragment(fragment))
                {
                    continue;
                }

                candidates.Add(fragment);
            }

            return candidates;
        }

        private static bool HasFragment(GameRun run, string fragmentId)
        {
            for (int i = 0; i < run.TableFragmentIds.Count; i++)
            {
                if (run.TableFragmentIds[i] == fragmentId)
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

        private static int PickHiddenWeighted(RewardContext context, IReadOnlyList<TableFragmentDef> candidates, int hidden, int distanceFloor)
        {
            var weights = new List<float>(candidates.Count);
            foreach (TableFragmentDef fragment in candidates)
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

        private static float GetItemWeight(ItemDefinition item, int hidden, int distanceFloor)
        {
            float weight = item.BaseWeight > 0f ? item.BaseWeight : 1f;
            if (item.IsPassive)
            {
                weight = HiddenScoreWeight(weight, HiddenMean(item), hidden, distanceFloor);
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
                    return context.PassiveItemHiddenScore;
                case cfg.RewardKind.FragmentChoice:
                    return context.FragmentHiddenScore;
                default:
                    return context.RewardHiddenScore;
            }
        }

        private static int HiddenOffsetForSlot(RewardContext context, cfg.RewardSlot slot)
        {
            return slot.NormalHiddenOffset;
        }

        private static bool ItemCoversHidden(ItemDefinition item, int hidden)
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

        private static int RollGoldRewardAmount(RewardContext context, cfg.RewardSlot slot)
        {
            GoldRange range = HiddenScoreService.GoldRewardRange(context.Run, context.ActionContext, HiddenOffsetForSlot(context, slot));
            return context.Rng.Range(range.Min, range.Max + 1);
        }
    }
}
