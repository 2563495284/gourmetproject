using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;
using GourmetProject.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 日常行动生成：行动数洗牌袋 → 逐卡抽类别 → 营业类别逐卡抽奖励 → 定位唯一行动。
    /// 类别与奖励均按本周累计候选卡次数执行下保底、上保底和常规权重；重掷会生成全新行动组。
    /// </summary>
    public static class ActionScheduleService
    {
        private const string LogTag = "ActionSchedule";
        private static readonly HashSet<string> LoggedConfigurationErrors =
            new HashSet<string>(StringComparer.Ordinal);

        public static List<ActionChoice> GenerateChoices(GameRun run, IRandomStream rng)
        {
            var result = new List<ActionChoice>();
            if (run == null || rng == null)
            {
                return result;
            }

            cfg.Tables tables = run.Tables ?? GameApp.Config?.Tables;
            if (tables == null)
            {
                LogConfigurationError("missing_tables", "行动随机失败：配置表未加载。");
                return result;
            }

            ActionCatalog catalog = BuildCatalog(run, tables);
            List<cfg.ActionCategoryRule> categoryRules = BuildCategoryRules(tables);
            List<cfg.ActionRewardRule> rewardRules = BuildRewardRules(tables);
            ValidateRequiredMappings(catalog, categoryRules, rewardRules);

            if (!TryTakeChoiceCount(run, tables, rng, out int choiceCount))
            {
                return result;
            }

            int groupSerial = run.BeginActionRandomGroup();
            string actionGroupId = $"action_w{run.WeekIndex}_g{groupSerial}";
            var usedRewards = new Dictionary<cfg.ActionRandomCategory, HashSet<cfg.RewardKind>>();
            bool eventUsed = false;

            for (int slot = 0; slot < choiceCount; slot++)
            {
                int candidateIndex = run.ActionRandomCandidateIndex;
                List<cfg.ActionCategoryRule> legalCategories = FindLegalCategories(
                    run,
                    catalog,
                    categoryRules,
                    rewardRules,
                    usedRewards,
                    eventUsed);
                if (legalCategories.Count == 0)
                {
                    LogConfigurationError(
                        "no_legal_category",
                        "行动随机提前结束：当前行动组已没有可用的类别或奖励组合。");
                    break;
                }

                cfg.ActionCategoryRule categoryRule = PickCategoryRule(
                    run,
                    legalCategories,
                    candidateIndex,
                    rng);
                if (categoryRule == null)
                {
                    break;
                }

                cfg.GameAction action;
                cfg.RewardKind? rewardKind = null;
                if (categoryRule.Category == cfg.ActionRandomCategory.Event)
                {
                    action = catalog.EventAction;
                    if (!ActionRandomService.IsAvailable(run, action))
                    {
                        LogConfigurationError(
                            "event_became_unavailable",
                            "事件行动在类别抽取后变为不可用，本张候选卡未计数。");
                        break;
                    }

                    eventUsed = true;
                }
                else
                {
                    List<cfg.ActionRewardRule> legalRewards = FindLegalRewards(
                        run,
                        catalog,
                        categoryRule.Category,
                        rewardRules,
                        usedRewards);
                    cfg.ActionRewardRule rewardRule = PickRewardRule(
                        run,
                        legalRewards,
                        candidateIndex,
                        rng);
                    if (rewardRule == null
                        || !catalog.TryGetAction(categoryRule.Category, rewardRule.RewardKind, out action)
                        || !ActionRandomService.IsAvailable(run, action))
                    {
                        LogConfigurationError(
                            $"missing_selected_action_{categoryRule.Category}_{rewardRule?.RewardKind}",
                            $"类别 {categoryRule.Category} 的已选奖励无法定位可用行动，本张候选卡未计数。");
                        break;
                    }

                    rewardKind = rewardRule.RewardKind;
                    GetOrCreateUsedRewardSet(usedRewards, categoryRule.Category).Add(rewardRule.RewardKind);
                }

                float costDays = run.SnapshotDailyActionCost(RollCostDays(action, rng));
                result.Add(new ActionChoice(
                    action,
                    actionGroupId,
                    run.ActionStepIndex,
                    run.RunActionStepIndex,
                    costDays,
                    timelineStopChance: run.SnapshotTimelineStopChance()));
                run.RecordRandomAction(categoryRule.Category, rewardKind);
                FlashCategoryBonus(run, categoryRule.Category);
            }

            return result;
        }

        /// <summary>行动调整单重掷：不保留旧候选，直接生成并累计一组全新候选。</summary>
        public static List<ActionChoice> RerollChoices(GameRun run, IRandomStream rng)
        {
            return GenerateChoices(run, rng);
        }

        private static bool TryTakeChoiceCount(
            GameRun run,
            cfg.Tables tables,
            IRandomStream rng,
            out int choiceCount)
        {
            if (run.TryTakeActionChoiceCount(out choiceCount))
            {
                return choiceCount > 0;
            }

            var bag = new List<int>();
            var seenChoiceCounts = new HashSet<int>();
            foreach (cfg.ActionChoiceCountRule rule in tables.TbActionChoiceCountRule.DataList)
            {
                if (rule == null || !seenChoiceCounts.Add(rule.ChoiceCount))
                {
                    LogConfigurationError(
                        $"duplicate_choice_count_{rule?.ChoiceCount}",
                        $"行动数规则重复：{rule?.ChoiceCount}。");
                    continue;
                }

                if (rule.ChoiceCount < 2 || rule.ChoiceCount > 3)
                {
                    LogConfigurationError(
                        $"invalid_choice_count_{rule.ChoiceCount}",
                        $"行动数只能配置为 2 或 3，当前为 {rule.ChoiceCount}。");
                    continue;
                }

                int tickets = WeeklyInt(rule.WeeklyWeights, run.WeekIndex, 0);
                if (tickets < 0)
                {
                    LogConfigurationError(
                        $"negative_choice_weight_{rule.Id}_{run.WeekIndex}",
                        $"行动数规则 {rule.Id} 的第 {run.WeekIndex} 周票数不能为负数。");
                    continue;
                }

                for (int i = 0; i < tickets; i++)
                {
                    bag.Add(rule.ChoiceCount);
                }
            }

            if (bag.Count == 0)
            {
                LogConfigurationError(
                    $"empty_choice_bag_week_{run.WeekIndex}",
                    $"第 {run.WeekIndex} 周行动数洗牌袋没有任何票。");
                choiceCount = 0;
                return false;
            }

            rng.Shuffle(bag);
            run.ReplaceActionChoiceCountBag(bag);
            return run.TryTakeActionChoiceCount(out choiceCount) && choiceCount > 0;
        }

        private static ActionCatalog BuildCatalog(GameRun run, cfg.Tables tables)
        {
            var catalog = new ActionCatalog();
            foreach (cfg.GameAction action in tables.TbAction.DataList)
            {
                if (action == null)
                {
                    continue;
                }

                if (action.Behavior == cfg.ActionBehavior.Event)
                {
                    catalog.EventShellCount++;
                    if (catalog.EventAction == null)
                    {
                        catalog.EventAction = action;
                    }
                    continue;
                }

                if (action.Behavior != cfg.ActionBehavior.Food)
                {
                    continue;
                }

                cfg.Food food = FoodService.Resolve(tables, action);
                if (food == null || food.ActionKind == cfg.FoodActionKind.Feast)
                {
                    continue;
                }

                cfg.ActionRandomCategory category;
                switch (food.ActionKind)
                {
                    case cfg.FoodActionKind.Normal:
                        category = cfg.ActionRandomCategory.Daily;
                        break;
                    case cfg.FoodActionKind.Super:
                        category = cfg.ActionRandomCategory.Hot;
                        break;
                    default:
                        continue;
                }

                catalog.Add(category, food.RewardKind, action);
            }

            if (catalog.EventShellCount != 1)
            {
                LogConfigurationError(
                    $"event_shell_count_{catalog.EventShellCount}",
                    $"事件随机池必须恰好有一个行动壳，当前为 {catalog.EventShellCount} 个。");
                catalog.EventAction = null;
            }
            else if (!ActionRandomService.IsAvailable(run, catalog.EventAction))
            {
                // 事件池可能因前置条件暂时为空；行动壳配置仍然有效，只在本次生成中屏蔽。
            }

            return catalog;
        }

        private static List<cfg.ActionCategoryRule> BuildCategoryRules(cfg.Tables tables)
        {
            var result = new List<cfg.ActionCategoryRule>();
            var seen = new HashSet<cfg.ActionRandomCategory>();
            foreach (cfg.ActionCategoryRule rule in tables.TbActionCategoryRule.DataList)
            {
                if (rule == null || !seen.Add(rule.Category))
                {
                    LogConfigurationError(
                        $"duplicate_category_rule_{rule?.Category}",
                        $"行动类别规则重复：{rule?.Category}。");
                    continue;
                }

                if (rule.Category != cfg.ActionRandomCategory.Daily
                    && rule.Category != cfg.ActionRandomCategory.Hot
                    && rule.Category != cfg.ActionRandomCategory.Event)
                {
                    LogConfigurationError(
                        $"invalid_category_rule_{rule.Category}",
                        $"行动类别规则包含未知类别：{rule.Category}。");
                    continue;
                }

                ValidateWeights(rule.Id, rule.FallbackWeights);
                result.Add(rule);
            }

            return result;
        }

        private static List<cfg.ActionRewardRule> BuildRewardRules(cfg.Tables tables)
        {
            var result = new List<cfg.ActionRewardRule>();
            var seen = new HashSet<cfg.RewardKind>();
            foreach (cfg.ActionRewardRule rule in tables.TbActionRewardRule.DataList)
            {
                if (rule == null || !seen.Add(rule.RewardKind))
                {
                    LogConfigurationError(
                        $"duplicate_reward_rule_{rule?.RewardKind}",
                        $"行动奖励规则重复：{rule?.RewardKind}。");
                    continue;
                }

                if (!IsDailyReward(rule.RewardKind))
                {
                    LogConfigurationError(
                        $"invalid_reward_rule_{rule.RewardKind}",
                        $"行动奖励规则只允许五种营业奖励，当前为 {rule.RewardKind}。");
                    continue;
                }

                ValidateWeights(rule.Id, rule.FallbackWeights);
                result.Add(rule);
            }

            return result;
        }

        private static void ValidateRequiredMappings(
            ActionCatalog catalog,
            IReadOnlyList<cfg.ActionCategoryRule> categoryRules,
            IReadOnlyList<cfg.ActionRewardRule> rewardRules)
        {
            var configuredCategories = new HashSet<cfg.ActionRandomCategory>();
            foreach (cfg.ActionCategoryRule rule in categoryRules)
            {
                configuredCategories.Add(rule.Category);
            }

            ValidateRequiredCategory(configuredCategories, cfg.ActionRandomCategory.Daily);
            ValidateRequiredCategory(configuredCategories, cfg.ActionRandomCategory.Hot);
            ValidateRequiredCategory(configuredCategories, cfg.ActionRandomCategory.Event);

            var configuredRewards = new HashSet<cfg.RewardKind>();
            foreach (cfg.ActionRewardRule rule in rewardRules)
            {
                configuredRewards.Add(rule.RewardKind);
            }

            ValidateRequiredReward(configuredRewards, cfg.RewardKind.PassiveItemChoice);
            ValidateRequiredReward(configuredRewards, cfg.RewardKind.FragmentChoice);
            ValidateRequiredReward(configuredRewards, cfg.RewardKind.ActiveItemStrengthen);
            ValidateRequiredReward(configuredRewards, cfg.RewardKind.ActiveItemAdjust);
            ValidateRequiredReward(configuredRewards, cfg.RewardKind.Gold);

            foreach (cfg.ActionCategoryRule categoryRule in categoryRules)
            {
                if (categoryRule.Category == cfg.ActionRandomCategory.Event)
                {
                    continue;
                }

                foreach (cfg.ActionRewardRule rewardRule in rewardRules)
                {
                    int count = catalog.GetMappingCount(categoryRule.Category, rewardRule.RewardKind);
                    if (count != 1)
                    {
                        LogConfigurationError(
                            $"action_mapping_{categoryRule.Category}_{rewardRule.RewardKind}_{count}",
                            $"{categoryRule.Category} + {rewardRule.RewardKind} 必须恰好映射一个行动，当前为 {count} 个。");
                    }
                }
            }
        }

        private static void ValidateRequiredCategory(
            ISet<cfg.ActionRandomCategory> configured,
            cfg.ActionRandomCategory category)
        {
            if (!configured.Contains(category))
            {
                LogConfigurationError(
                    $"missing_category_rule_{category}",
                    $"行动类别规则缺少 {category}。");
            }
        }

        private static void ValidateRequiredReward(
            ISet<cfg.RewardKind> configured,
            cfg.RewardKind rewardKind)
        {
            if (!configured.Contains(rewardKind))
            {
                LogConfigurationError(
                    $"missing_reward_rule_{rewardKind}",
                    $"行动奖励规则缺少 {rewardKind}。");
            }
        }

        private static List<cfg.ActionCategoryRule> FindLegalCategories(
            GameRun run,
            ActionCatalog catalog,
            IReadOnlyList<cfg.ActionCategoryRule> categoryRules,
            IReadOnlyList<cfg.ActionRewardRule> rewardRules,
            IReadOnlyDictionary<cfg.ActionRandomCategory, HashSet<cfg.RewardKind>> usedRewards,
            bool eventUsed)
        {
            var result = new List<cfg.ActionCategoryRule>();
            foreach (cfg.ActionCategoryRule rule in categoryRules)
            {
                if (rule.Category == cfg.ActionRandomCategory.Event)
                {
                    if (!eventUsed
                        && catalog.EventAction != null
                        && ActionRandomService.IsAvailable(run, catalog.EventAction))
                    {
                        result.Add(rule);
                    }
                    continue;
                }

                if (FindLegalRewards(run, catalog, rule.Category, rewardRules, usedRewards).Count > 0)
                {
                    result.Add(rule);
                }
            }

            return result;
        }

        private static List<cfg.ActionRewardRule> FindLegalRewards(
            GameRun run,
            ActionCatalog catalog,
            cfg.ActionRandomCategory category,
            IReadOnlyList<cfg.ActionRewardRule> rewardRules,
            IReadOnlyDictionary<cfg.ActionRandomCategory, HashSet<cfg.RewardKind>> usedRewards)
        {
            var result = new List<cfg.ActionRewardRule>();
            usedRewards.TryGetValue(category, out HashSet<cfg.RewardKind> used);
            foreach (cfg.ActionRewardRule rule in rewardRules)
            {
                if (used != null && used.Contains(rule.RewardKind))
                {
                    continue;
                }

                if (catalog.GetMappingCount(category, rule.RewardKind) == 1
                    && catalog.TryGetAction(category, rule.RewardKind, out cfg.GameAction action)
                    && ActionRandomService.IsAvailable(run, action))
                {
                    result.Add(rule);
                }
            }

            return result;
        }

        private static cfg.ActionCategoryRule PickCategoryRule(
            GameRun run,
            IReadOnlyList<cfg.ActionCategoryRule> legal,
            int candidateIndex,
            IRandomStream rng)
        {
            List<cfg.ActionCategoryRule> candidates = FindCategoryGuaranteeCandidates(
                run,
                legal,
                candidateIndex,
                useMinimum: true);
            if (candidates.Count == 0)
            {
                candidates = FindCategoryGuaranteeCandidates(
                    run,
                    legal,
                    candidateIndex,
                    useMinimum: false);
            }
            if (candidates.Count == 0)
            {
                candidates.AddRange(legal);
            }

            var weights = new List<float>(candidates.Count);
            foreach (cfg.ActionCategoryRule rule in candidates)
            {
                float weight = WeeklyFloat(rule.FallbackWeights, run.WeekIndex, 0f);
                weights.Add(ApplyCategoryWeightBonus(weight, CategoryBonus(run, rule.Category)));
            }

            return PickWeighted(candidates, weights, rng, "category_zero_weights");
        }

        private static cfg.ActionRewardRule PickRewardRule(
            GameRun run,
            IReadOnlyList<cfg.ActionRewardRule> legal,
            int candidateIndex,
            IRandomStream rng)
        {
            List<cfg.ActionRewardRule> candidates = FindRewardGuaranteeCandidates(
                run,
                legal,
                candidateIndex,
                useMinimum: true);
            if (candidates.Count == 0)
            {
                candidates = FindRewardGuaranteeCandidates(
                    run,
                    legal,
                    candidateIndex,
                    useMinimum: false);
            }
            if (candidates.Count == 0)
            {
                candidates.AddRange(legal);
            }

            var weights = new List<float>(candidates.Count);
            foreach (cfg.ActionRewardRule rule in candidates)
            {
                weights.Add(WeeklyFloat(rule.FallbackWeights, run.WeekIndex, 0f));
            }

            return PickWeighted(candidates, weights, rng, "reward_zero_weights");
        }

        private static List<cfg.ActionCategoryRule> FindCategoryGuaranteeCandidates(
            GameRun run,
            IReadOnlyList<cfg.ActionCategoryRule> rules,
            int candidateIndex,
            bool useMinimum)
        {
            var result = new List<cfg.ActionCategoryRule>();
            foreach (cfg.ActionCategoryRule rule in rules)
            {
                IReadOnlyList<List<int>> bounds = useMinimum
                    ? rule.MinGuaranteeCounts
                    : rule.MaxGuaranteeCounts;
                if (!TryGetGuarantee(bounds, run.WeekIndex, candidateIndex, out int target))
                {
                    continue;
                }

                target = ApplyCategoryGuaranteeBonus(target, CategoryBonus(run, rule.Category));
                if (run.GetActionCategoryCount(rule.Category) < target)
                {
                    result.Add(rule);
                }
            }

            return result;
        }

        private static List<cfg.ActionRewardRule> FindRewardGuaranteeCandidates(
            GameRun run,
            IReadOnlyList<cfg.ActionRewardRule> rules,
            int candidateIndex,
            bool useMinimum)
        {
            var result = new List<cfg.ActionRewardRule>();
            foreach (cfg.ActionRewardRule rule in rules)
            {
                IReadOnlyList<List<int>> bounds = useMinimum
                    ? rule.MinGuaranteeCounts
                    : rule.MaxGuaranteeCounts;
                if (TryGetGuarantee(bounds, run.WeekIndex, candidateIndex, out int target)
                    && run.GetActionRewardCount(rule.RewardKind) < target)
                {
                    result.Add(rule);
                }
            }

            return result;
        }

        internal static bool TryGetGuarantee(
            IReadOnlyList<List<int>> bounds,
            int weekIndex,
            int candidateIndex,
            out int value)
        {
            value = 0;
            if (bounds == null)
            {
                return false;
            }

            int week = weekIndex - 1;
            if (week < 0 || week >= bounds.Count)
            {
                return false;
            }

            IReadOnlyList<int> candidates = bounds[week];
            if (candidates == null || candidateIndex < 0 || candidateIndex >= candidates.Count)
            {
                return false;
            }

            value = candidates[candidateIndex];
            return value >= 0;
        }

        internal static float ApplyCategoryWeightBonus(float baseWeight, float bonus)
        {
            if (!(baseWeight > 0f) || !IsFinite(baseWeight))
            {
                return 0f;
            }

            float multiplier = IsFinite(bonus) ? Math.Max(0f, 1f + bonus) : 1f;
            float adjusted = baseWeight * multiplier;
            return adjusted > 0f && IsFinite(adjusted) ? adjusted : 0f;
        }

        internal static int ApplyCategoryGuaranteeBonus(int target, float bonus)
        {
            if (target < 0)
            {
                return target;
            }

            double multiplier = IsFinite(bonus) ? Math.Max(0d, 1d + bonus) : 1d;
            double adjusted = Math.Round(target * multiplier, MidpointRounding.AwayFromZero);
            return adjusted >= int.MaxValue ? int.MaxValue : (int)Math.Max(0d, adjusted);
        }

        private static T PickWeighted<T>(
            IReadOnlyList<T> candidates,
            IReadOnlyList<float> weights,
            IRandomStream rng,
            string zeroWeightErrorKey)
            where T : class
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            float total = 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                float weight = weights[i];
                if (weight > 0f && IsFinite(weight))
                {
                    total += weight;
                }
            }

            int index;
            if (total > 0f && IsFinite(total))
            {
                index = rng.WeightedPickIndex(weights);
            }
            else
            {
                LogConfigurationError(
                    zeroWeightErrorKey,
                    "行动随机当前合法候选的权重全为 0，已使用等概率兜底。");
                index = rng.Range(0, candidates.Count);
            }

            index = Math.Max(0, Math.Min(index, candidates.Count - 1));
            return candidates[index];
        }

        private static float CategoryBonus(GameRun run, cfg.ActionRandomCategory category)
        {
            var items = new ItemRuntime(run);
            switch (category)
            {
                case cfg.ActionRandomCategory.Event:
                    return items.EventActionLargeGroupWeightBonus();
                case cfg.ActionRandomCategory.Hot:
                    return items.SuperActionLargeGroupWeightBonus();
                default:
                    return 0f;
            }
        }

        private static void FlashCategoryBonus(GameRun run, cfg.ActionRandomCategory category)
        {
            var items = new ItemRuntime(run);
            switch (category)
            {
                case cfg.ActionRandomCategory.Event:
                    items.FlashTriggered(
                        model => Math.Abs(model.EventActionLargeGroupWeightBonus()) > 0.0001f);
                    break;
                case cfg.ActionRandomCategory.Hot:
                    items.FlashTriggered(
                        model => Math.Abs(model.SuperActionLargeGroupWeightBonus()) > 0.0001f);
                    break;
            }
        }

        private static HashSet<cfg.RewardKind> GetOrCreateUsedRewardSet(
            IDictionary<cfg.ActionRandomCategory, HashSet<cfg.RewardKind>> usedRewards,
            cfg.ActionRandomCategory category)
        {
            if (!usedRewards.TryGetValue(category, out HashSet<cfg.RewardKind> result))
            {
                result = new HashSet<cfg.RewardKind>();
                usedRewards[category] = result;
            }

            return result;
        }

        private static bool IsDailyReward(cfg.RewardKind rewardKind)
        {
            return rewardKind == cfg.RewardKind.PassiveItemChoice
                || rewardKind == cfg.RewardKind.FragmentChoice
                || rewardKind == cfg.RewardKind.ActiveItemStrengthen
                || rewardKind == cfg.RewardKind.ActiveItemAdjust
                || rewardKind == cfg.RewardKind.Gold;
        }

        private static float WeeklyFloat(IReadOnlyList<float> values, int weekIndex, float fallback)
        {
            if (values == null || values.Count == 0)
            {
                return fallback;
            }

            int index = Math.Max(0, weekIndex - 1);
            if (index >= values.Count)
            {
                index = values.Count - 1;
            }

            float value = values[index];
            return value >= 0f && IsFinite(value) ? value : 0f;
        }

        private static int WeeklyInt(IReadOnlyList<int> values, int weekIndex, int fallback)
        {
            if (values == null || values.Count == 0)
            {
                return fallback;
            }

            int index = Math.Max(0, weekIndex - 1);
            if (index >= values.Count)
            {
                index = values.Count - 1;
            }

            return values[index];
        }

        private static void ValidateWeights(string id, IReadOnlyList<float> weights)
        {
            if (weights == null)
            {
                return;
            }

            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] < 0f || !IsFinite(weights[i]))
                {
                    LogConfigurationError(
                        $"invalid_weight_{id}_{i}",
                        $"行动随机规则 {id} 的第 {i + 1} 周权重必须有限且非负。");
                }
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float RollCostDays(cfg.GameAction action, IRandomStream rng)
        {
            float min = action != null ? action.MinCostDays : 0f;
            float max = action != null && action.MaxCostDays > 0f ? action.MaxCostDays : min;
            if (max < min)
            {
                (min, max) = (max, min);
            }

            int minTenths = (int)Math.Round(min * 10f, MidpointRounding.AwayFromZero);
            int maxTenths = (int)Math.Round(max * 10f, MidpointRounding.AwayFromZero);
            int tenths = minTenths == maxTenths ? minTenths : rng.Range(minTenths, maxTenths + 1);
            return TimelineMath.Quantize(tenths / 10f);
        }

        private static void LogConfigurationError(string key, string message)
        {
            if (LoggedConfigurationErrors.Add(key))
            {
                Log.Error(message, LogTag);
            }
        }

        private readonly struct ActionKey : IEquatable<ActionKey>
        {
            public ActionKey(cfg.ActionRandomCategory category, cfg.RewardKind rewardKind)
            {
                Category = category;
                RewardKind = rewardKind;
            }

            public cfg.ActionRandomCategory Category { get; }
            public cfg.RewardKind RewardKind { get; }

            public bool Equals(ActionKey other)
            {
                return Category == other.Category && RewardKind == other.RewardKind;
            }

            public override bool Equals(object obj)
            {
                return obj is ActionKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((int)Category * 397) ^ (int)RewardKind;
                }
            }
        }

        private sealed class ActionCatalog
        {
            private readonly Dictionary<ActionKey, cfg.GameAction> _actions =
                new Dictionary<ActionKey, cfg.GameAction>();
            private readonly Dictionary<ActionKey, int> _mappingCounts =
                new Dictionary<ActionKey, int>();

            public cfg.GameAction EventAction { get; set; }
            public int EventShellCount { get; set; }

            public void Add(
                cfg.ActionRandomCategory category,
                cfg.RewardKind rewardKind,
                cfg.GameAction action)
            {
                var key = new ActionKey(category, rewardKind);
                _mappingCounts.TryGetValue(key, out int count);
                _mappingCounts[key] = count + 1;
                if (count == 0)
                {
                    _actions[key] = action;
                }
                else
                {
                    _actions.Remove(key);
                }
            }

            public int GetMappingCount(cfg.ActionRandomCategory category, cfg.RewardKind rewardKind)
            {
                var key = new ActionKey(category, rewardKind);
                return _mappingCounts.TryGetValue(key, out int count) ? count : 0;
            }

            public bool TryGetAction(
                cfg.ActionRandomCategory category,
                cfg.RewardKind rewardKind,
                out cfg.GameAction action)
            {
                return _actions.TryGetValue(new ActionKey(category, rewardKind), out action);
            }
        }
    }
}
