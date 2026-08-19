using System;
using System.Collections.Generic;
using GourmetProject.Game.Analytics;
using GourmetProject.Gameplay.Model;
using Log = GourmetProject.Core.Diagnostics.Log;
using GourmetProject.Game.Run;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 胜利奖励池。负责把配置槽位解析为本次可展示的奖励候选。
    /// </summary>
    public static class RewardPoolService
    {
        private const string Tag = "RewardPool";
        private const string FlavoredDishPoolId = "pool_flavored_dish";
        private const string BossPassiveSlotId = "slot_boss_passive";

        public static List<RewardChoice> RollChoices(RewardContext context, cfg.RewardSlot slot)
        {
            var result = new List<RewardChoice>();
            if (context.Tables == null || context.Run == null || context.Rng == null || slot == null)
            {
                return result;
            }

            bool pityEligible = IsDishChoiceArchetypePityEligible(slot);
            ArchetypeVector pityArchetype = pityEligible
                ? ArchetypeService.Capture(context.Run)
                : ArchetypeVector.Mixed;
            int pityMissThreshold = pityEligible
                ? context.Tables.TbGameBase.DishChoiceArchetypePityCount
                : 0;
            bool pityArmed = pityEligible
                && context.Run.BeginDishChoiceArchetypePity(pityArchetype.Id, pityMissThreshold);
            bool bossPassivePityEligible = IsBossPassiveArchetypePityEligible(slot);
            bool bossPassivePityArmed = bossPassivePityEligible
                && context.Run.BeginBossPassiveArchetypePity();

            int count = Math.Max(1, slot.ChoiceCount);
            // 多选一「可选数量」增减（琳琅满目 / 选择困难 / 少选择）：仅作用于食物多选一。
            if (context.ApplyChoiceCountModifiers
                && slot.Kind == cfg.RewardKind.DishChoice
                && context.Run != null)
            {
                count = Math.Max(1, count + new ItemRuntime(context.Run).ChoiceCountDelta());
            }

            int hidden = ResolveHiddenScoreForSlot(context, slot);
            if (slot.Kind == cfg.RewardKind.Gold)
            {
                result.Add(RewardChoice.Gold(
                    RollGoldRewardAmount(context, slot),
                    string.IsNullOrWhiteSpace(slot.Name) ? "额外金币" : slot.Name));
                return result;
            }

            if (context.ApplyChoiceCountModifiers
                && context.Run != null
                && context.ConsumeEventChoiceCountDelta)
            {
                count = Math.Max(1, count + context.Run.ConsumeEventChoiceCountDelta());
            }

            cfg.RewardPool pool = context.Tables.TbRewardPool.GetOrDefault(slot.PoolId);
            if (pool == null)
            {
                Log.Warning($"Reward slot '{slot.Id}' references missing pool '{slot.PoolId}'.", Tag);
                result.Add(RewardChoice.Gold(RollGoldRewardAmount(context, slot), "折算金币", isFallback: true));
                CompleteDishChoiceArchetypePity(
                    context,
                    pityEligible,
                    pityArchetype,
                    pityMissThreshold,
                    result);
                CompleteBossPassiveArchetypePity(context, bossPassivePityEligible, result);
                return result;
            }

            switch (slot.Kind)
            {
                case cfg.RewardKind.DishChoice:
                    RollDishChoices(context, pool, hidden, count, result);
                    break;
                case cfg.RewardKind.PassiveItemChoice:
                    float itemLuck = ResolveItemLuckForSlot(context, slot);
                    if (bossPassivePityArmed)
                    {
                        RollBossPassiveChoicesWithPity(context, pool, itemLuck, count, result);
                    }
                    else
                    {
                        RollItemChoices(
                            context,
                            pool,
                            cfg.ItemKind.Passive,
                            slot.Kind,
                            itemLuck,
                            count,
                            result);
                    }
                    break;
                case cfg.RewardKind.ActiveItemGrant:
                case cfg.RewardKind.ActiveItemStrengthen:
                case cfg.RewardKind.ActiveItemAdjust:
                    RollItemChoices(context, pool, cfg.ItemKind.Active, slot.Kind, 0f, count, result);
                    break;
                case cfg.RewardKind.FragmentChoice:
                    RollFragmentChoices(context, pool, hidden, count, result);
                    break;
            }

            if (pityArmed
                && TryResolveArchetypeIndex(pityArchetype.Id, out int pityTargetIndex)
                && !ContainsArchetypeDish(context, result, pityTargetIndex))
            {
                ApplyDishChoiceArchetypePity(
                    context,
                    pool,
                    hidden,
                    pityTargetIndex,
                    result);
            }

            if (result.Count == 0)
            {
                Log.Warning($"Reward slot '{slot.Id}' produced no candidates. Converted to gold.", Tag);
                result.Add(RewardChoice.Gold(RollGoldRewardAmount(context, slot), "折算金币", isFallback: true));
            }

            CompleteDishChoiceArchetypePity(
                context,
                pityEligible,
                pityArchetype,
                pityMissThreshold,
                result);
            CompleteBossPassiveArchetypePity(context, bossPassivePityEligible, result);

            return result;
        }

        private static bool IsBossPassiveArchetypePityEligible(cfg.RewardSlot slot)
        {
            return slot.Kind == cfg.RewardKind.PassiveItemChoice
                && string.Equals(slot.Id, BossPassiveSlotId, StringComparison.Ordinal);
        }

        private static void CompleteBossPassiveArchetypePity(
            RewardContext context,
            bool eligible,
            IReadOnlyList<RewardChoice> result)
        {
            if (eligible)
            {
                context.Run.CompleteBossPassiveArchetypePity(
                    ContainsArchetypePassive(context, result));
            }
        }

        private static bool ContainsArchetypePassive(
            RewardContext context,
            IReadOnlyList<RewardChoice> choices)
        {
            for (int i = 0; i < choices.Count; i++)
            {
                RewardChoice choice = choices[i];
                if (choice?.Kind == cfg.RewardKind.PassiveItemChoice
                    && HasAnyArchetypeTag(ItemDefinition.Get(
                        context.Tables,
                        choice.Id,
                        cfg.ItemKind.Passive)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAnyArchetypeTag(ItemDefinition item)
        {
            return item?.ArchetypeTags != null && item.ArchetypeTags.Count > 0;
        }

        private static bool IsDishChoiceArchetypePityEligible(cfg.RewardSlot slot)
        {
            return slot.Kind == cfg.RewardKind.DishChoice
                && slot.ChoiceCount > 1
                && slot.RequiredPickCount == 1;
        }

        private static void CompleteDishChoiceArchetypePity(
            RewardContext context,
            bool eligible,
            ArchetypeVector archetype,
            int missThreshold,
            IReadOnlyList<RewardChoice> result)
        {
            if (!eligible)
            {
                return;
            }

            bool targetOffered = TryResolveArchetypeIndex(archetype.Id, out int targetIndex)
                && ContainsArchetypeDish(context, result, targetIndex);
            context.Run.CompleteDishChoiceArchetypePity(
                archetype.Id,
                targetOffered,
                missThreshold);
        }

        private static void ApplyDishChoiceArchetypePity(
            RewardContext context,
            cfg.RewardPool pool,
            int hidden,
            int targetIndex,
            List<RewardChoice> result)
        {
            bool requireFlavor = RequiresFlavor(pool);
            List<DishDef> candidates = BuildArchetypePityCandidates(
                context,
                result,
                targetIndex,
                requireFlavor,
                hidden,
                requireHiddenCoverage: true);
            if (candidates.Count == 0)
            {
                candidates = BuildArchetypePityCandidates(
                    context,
                    result,
                    targetIndex,
                    requireFlavor,
                    hidden,
                    requireHiddenCoverage: false);
            }

            if (candidates.Count == 0)
            {
                Log.Warning(
                    $"Dish choice archetype pity could not find a candidate for archetype '{targetIndex}' in pool '{pool.Id}'.",
                    Tag);
                return;
            }

            int candidateIndex = PickHiddenWeighted(
                context,
                candidates,
                hidden,
                HiddenScoreDistanceFloor(context));
            DishDef dish = candidates[candidateIndex];
            var guaranteedChoice = new RewardChoice(
                cfg.RewardKind.DishChoice,
                dish.Id,
                dish.Name,
                string.Empty);
            if (result.Count == 0)
            {
                result.Add(guaranteedChoice);
                return;
            }

            result[context.Rng.Range(0, result.Count)] = guaranteedChoice;
        }

        private static List<DishDef> BuildArchetypePityCandidates(
            RewardContext context,
            IReadOnlyList<RewardChoice> result,
            int targetIndex,
            bool requireFlavor,
            int hidden,
            bool requireHiddenCoverage)
        {
            var selectedIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < result.Count; i++)
            {
                RewardChoice choice = result[i];
                if (choice != null && choice.Kind == cfg.RewardKind.DishChoice)
                {
                    selectedIds.Add(choice.Id);
                }
            }

            var candidates = new List<DishDef>();
            foreach (DishDef dish in context.Run.Library.Dishes)
            {
                if (selectedIds.Contains(dish.Id)
                    || (requireFlavor && !dish.HasFlavor)
                    || (requireHiddenCoverage && !dish.CoversHiddenScore(hidden))
                    || !BelongsToArchetype(dish, targetIndex))
                {
                    continue;
                }

                candidates.Add(dish);
            }

            return candidates;
        }

        private static bool ContainsArchetypeDish(
            RewardContext context,
            IReadOnlyList<RewardChoice> choices,
            int targetIndex)
        {
            for (int i = 0; i < choices.Count; i++)
            {
                RewardChoice choice = choices[i];
                if (choice?.Kind == cfg.RewardKind.DishChoice
                    && BelongsToArchetype(context.Run.Database.GetDish(choice.Id), targetIndex))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool BelongsToArchetype(DishDef dish, int targetIndex)
        {
            return dish?.ArchetypeWeights != null
                && targetIndex >= 0
                && targetIndex < dish.ArchetypeWeights.Count
                && dish.ArchetypeWeights[targetIndex] > 0f;
        }

        private static bool TryResolveArchetypeIndex(string archetypeId, out int targetIndex)
        {
            if (int.TryParse(archetypeId, out targetIndex)
                && targetIndex >= 0
                && targetIndex < 3)
            {
                return true;
            }

            targetIndex = -1;
            return false;
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
            bool requireFlavor = RequiresFlavor(pool);
            var candidates = new List<DishDef>();
            foreach (DishDef dish in context.Run.Library.Dishes)
            {
                if ((!requireFlavor || dish.HasFlavor) && dish.CoversHiddenScore(hidden))
                {
                    candidates.Add(dish);
                }
            }

            if (candidates.Count == 0 && pool.AllowFallback)
            {
                foreach (DishDef dish in context.Run.Library.Dishes)
                {
                    if (!requireFlavor || dish.HasFlavor)
                    {
                        candidates.Add(dish);
                    }
                }
            }

            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                int index = PickHiddenWeighted(context, candidates, hidden, HiddenScoreDistanceFloor(context));
                DishDef dish = candidates[index];
                candidates.RemoveAt(index);

                result.Add(new RewardChoice(
                    cfg.RewardKind.DishChoice,
                    dish.Id,
                    dish.Name,
                    string.Empty));
            }
        }

        private static bool RequiresFlavor(cfg.RewardPool pool)
        {
            return string.Equals(pool.Id, FlavoredDishPoolId, StringComparison.Ordinal);
        }

        private static void RollItemChoices(
            RewardContext context,
            cfg.RewardPool pool,
            cfg.ItemKind kind,
            cfg.RewardKind rewardKind,
            float itemLuck,
            int count,
            List<RewardChoice> result)
        {
            bool activeItem = kind == cfg.ItemKind.Active;
            List<ItemDefinition> candidates = BuildItemCandidates(context, pool, kind, rewardKind);
            if (!activeItem)
            {
                foreach (ItemDefinition chosen in PassiveItemRandomService.Roll(
                             context.Tables,
                             candidates,
                             context.Rng,
                             count,
                             itemLuck))
                {
                    result.Add(BuildItemChoice(kind, rewardKind, chosen));
                }

                return;
            }

            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                var weights = new List<float>(candidates.Count);
                foreach (ItemDefinition item in candidates)
                {
                    weights.Add(GetItemWeight(item, context.Tables));
                }

                int index = PickWeightedOrUniform(context, weights, candidates.Count);
                ItemDefinition chosen = candidates[index];
                candidates.RemoveAt(index);
                result.Add(BuildItemChoice(kind, rewardKind, chosen));
            }
        }

        private static void RollBossPassiveChoicesWithPity(
            RewardContext context,
            cfg.RewardPool pool,
            float itemLuck,
            int count,
            List<RewardChoice> result)
        {
            List<ItemDefinition> candidates = BuildItemCandidates(
                context,
                pool,
                cfg.ItemKind.Passive,
                cfg.RewardKind.PassiveItemChoice);
            var archetypeCandidates = new List<ItemDefinition>();
            foreach (ItemDefinition item in candidates)
            {
                if (HasAnyArchetypeTag(item))
                {
                    archetypeCandidates.Add(item);
                }
            }

            List<ItemDefinition> guaranteed = PassiveItemRandomService.Roll(
                context.Tables,
                archetypeCandidates,
                context.Rng,
                1,
                itemLuck);
            if (guaranteed.Count > 0)
            {
                ItemDefinition chosen = guaranteed[0];
                result.Add(BuildItemChoice(
                    cfg.ItemKind.Passive,
                    cfg.RewardKind.PassiveItemChoice,
                    chosen));
                candidates.RemoveAll(item => string.Equals(item.Id, chosen.Id, StringComparison.Ordinal));
            }

            int remainingCount = count - result.Count;
            foreach (ItemDefinition chosen in PassiveItemRandomService.Roll(
                         context.Tables,
                         candidates,
                         context.Rng,
                         remainingCount,
                         itemLuck))
            {
                result.Add(BuildItemChoice(
                    cfg.ItemKind.Passive,
                    cfg.RewardKind.PassiveItemChoice,
                    chosen));
            }
        }

        private static RewardChoice BuildItemChoice(cfg.ItemKind kind, cfg.RewardKind rewardKind, ItemDefinition item)
        {
            return new RewardChoice(
                kind == cfg.ItemKind.Passive ? cfg.RewardKind.PassiveItemChoice : rewardKind,
                item.Id,
                item.Name,
                item.Desc);
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

            for (int i = 0; i < count && candidates.Count > 0;)
            {
                int index = PickHiddenWeighted(context, candidates, hidden, HiddenScoreDistanceFloor(context));
                TableFragmentDef fragment = candidates[index];
                candidates.RemoveAt(index);
                List<int> attachableRotations = context.Run.GetAttachableFragmentRotations(fragment);
                if (attachableRotations.Count == 0)
                {
                    continue;
                }

                int rotation = attachableRotations[context.Rng.Range(0, attachableRotations.Count)];
                result.Add(new RewardChoice(
                    cfg.RewardKind.FragmentChoice,
                    fragment.Id,
                    fragment.Id,
                    $"扩展餐桌",
                    HiddenScoreService.FragmentFallbackGold(context.Run, context.ActionContext),
                    fragmentRotation: rotation));
                i++;
            }
        }

        private static List<ItemDefinition> BuildItemCandidates(
            RewardContext context,
            cfg.RewardPool pool,
            cfg.ItemKind kind,
            cfg.RewardKind rewardKind)
        {
            var candidates = new List<ItemDefinition>();
            MetaProgressSaveData progress = context.Progress
                ?? context.Run?.MetaProgress
                ?? new MetaProgressSaveData();
            foreach (ItemDefinition item in ItemDefinition.All(context.Tables, kind))
            {
                if (!ItemPoolService.CanEnterPool(context.Run, item) ||
                    !MetaProgressService.IsItemUnlockedForPool(context.Tables, item, progress) ||
                    !MatchesPoolCategory(item, pool, rewardKind))
                {
                    continue;
                }

                if (kind == cfg.ItemKind.Passive && item.IsNegative)
                {
                    continue;
                }

                if (kind == cfg.ItemKind.Passive
                    && !PassiveItemRandomService.IsNormalQuality(context.Tables, item.Quality))
                {
                    continue;
                }

                candidates.Add(item);
            }

            return candidates;
        }

        private static bool MatchesPoolCategory(
            ItemDefinition item,
            cfg.RewardPool pool,
            cfg.RewardKind rewardKind)
        {
            if (item == null || pool == null)
            {
                return false;
            }

            if (item.IsPassive
                && pool.QualityFilters.Count > 0
                && !pool.QualityFilters.Contains(item.Quality))
            {
                return false;
            }

            if (rewardKind == cfg.RewardKind.ActiveItemStrengthen &&
                (!item.IsActive || item.ActiveItemCategory != cfg.ActiveItemCategory.Strengthen))
            {
                return false;
            }

            if (rewardKind == cfg.RewardKind.ActiveItemAdjust &&
                (!item.IsActive || item.ActiveItemCategory != cfg.ActiveItemCategory.Adjust))
            {
                return false;
            }

            return pool.Kind switch
            {
                cfg.RewardPoolKind.ActiveItemStrengthen =>
                    item.IsActive && item.ActiveItemCategory == cfg.ActiveItemCategory.Strengthen,
                cfg.RewardPoolKind.ActiveItemAdjust =>
                    item.IsActive && item.ActiveItemCategory == cfg.ActiveItemCategory.Adjust,
                _ => true,
            };
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

        private static int HiddenScoreDistanceFloor(RewardContext context)
        {
            return Math.Max(1, context.Tables.TbGameBase.HiddenScoreDistanceFloor);
        }

        private static float DefaultRandomWeight(cfg.Tables tables)
        {
            tables ??= GameApp.Config.Tables;
            return Math.Max(float.Epsilon, tables.TbGameBase.DefaultRandomWeight);
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

        private static float GetItemWeight(ItemDefinition item, cfg.Tables tables)
        {
            return item.BaseWeight > 0f ? item.BaseWeight : DefaultRandomWeight(tables);
        }

        /// <summary>消耗品抽取完全不读取隐藏分或奖励槽隐藏分修正。</summary>
        internal static int ResolveHiddenScoreForSlot(RewardContext context, cfg.RewardSlot slot)
        {
            return slot == null
                ? 0
                : Math.Max(0, HiddenForSlot(context, slot) + HiddenOffsetForSlot(context, slot));
        }

        private static int HiddenForSlot(RewardContext context, cfg.RewardSlot slot)
        {
            switch (slot.Kind)
            {
                case cfg.RewardKind.DishChoice:
                    return context.DishHiddenScore;
                case cfg.RewardKind.PassiveItemChoice:
                case cfg.RewardKind.ActiveItemGrant:
                case cfg.RewardKind.ActiveItemStrengthen:
                case cfg.RewardKind.ActiveItemAdjust:
                    return 0;
                case cfg.RewardKind.FragmentChoice:
                    return context.FragmentHiddenScore;
                default:
                    return context.RewardHiddenScore;
            }
        }

        private static int HiddenOffsetForSlot(RewardContext context, cfg.RewardSlot slot)
        {
            int tierIndex = ResolveFoodTierIndex(context);
            return slot.Kind switch
            {
                cfg.RewardKind.DishChoice => ListOffset(slot.DishHiddenOffset, tierIndex),
                cfg.RewardKind.PassiveItemChoice => 0,
                cfg.RewardKind.ActiveItemGrant => 0,
                cfg.RewardKind.ActiveItemStrengthen => 0,
                cfg.RewardKind.ActiveItemAdjust => 0,
                cfg.RewardKind.FragmentChoice => ListOffset(slot.FragmentHiddenOffset, tierIndex),
                cfg.RewardKind.Gold => ListOffset(slot.GoldHiddenOffset, tierIndex),
                _ => 0,
            };
        }

        private static int ResolveFoodTierIndex(RewardContext context)
        {
            cfg.Food food = FoodService.Resolve(context.Tables, context.ActionContext?.Action);
            return food?.ActionKind == cfg.FoodActionKind.Super ? 1 : 0;
        }

        private static int ListOffset(System.Collections.Generic.IReadOnlyList<int> offsets, int index)
        {
            if (offsets == null || offsets.Count == 0)
            {
                return 0;
            }

            return index < offsets.Count ? offsets[index] : offsets[0];
        }

        private static float ResolveItemLuckForSlot(RewardContext context, cfg.RewardSlot slot)
        {
            int tierIndex = ResolveFoodTierIndex(context);
            float slotOffset = slot == null ? 0f : ListOffset(slot.ItemLuckOffset, tierIndex);
            return ItemLuckService.GetLuck(context.Run, context.ActionContext, slotOffset);
        }

        private static int RollGoldRewardAmount(RewardContext context, cfg.RewardSlot slot)
        {
            GoldRange range = HiddenScoreService.GoldRewardRange(context.Run, context.ActionContext, HiddenOffsetForSlot(context, slot));
            return context.Rng.Range(range.Min, range.Max + 1);
        }
    }
}
