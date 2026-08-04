using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 整局行动组序列（大组）与本次 n 选一（大组→小组）生成。
    /// - 大组间：逐行动检查本周累计次数。先从未达到 minGuaranteeCounts 的大组中按当周权重抽取；
    ///   全部达到下限后，再从未达到 maxGuaranteeCounts 的大组中抽取；均无候选时完全按权重放回随机。
    /// - 上下限按「周 → 周内行动序号」配置，未配置、负数或越界均表示不限制；累计次数每周独立。
    /// - 大组→小组：按 <see cref="cfg.ActionSmallGroup.Weight"/> 选 1 个小组。
    /// - 小组：固定成员，经可用性过滤后即本次 n 选一。
    /// </summary>
    public static class ActionScheduleService
    {
        public static List<ActionChoice> GenerateChoices(GameRun run, IRandomStream rng, int count = 0)
        {
            var result = new List<ActionChoice>();
            if (run == null || rng == null)
            {
                return result;
            }

            // LuckyEventChance/LuckyEventGuarantee 作用于「抽事件」层；MoreEvents 作用于大组层，
            // 在下方完成 min/max 保底候选筛选后再修正包含 Event 行动的大组权重。

            cfg.Tables tables = run.Tables ?? GameApp.Config.Tables;
            int maxChoiceCount = ActionRandomService.ChoiceCount(run);
            if (count <= 0)
            {
                count = maxChoiceCount;
            }

            string largeId = EnsureCurrentGroup(run, rng);
            cfg.ActionLargeGroup large = string.IsNullOrEmpty(largeId) ? null : tables.TbActionLargeGroup.GetOrDefault(largeId);
            if (large == null)
            {
                return result;
            }

            cfg.ActionSmallGroup small = PickSmall(tables, large, rng);
            if (small == null)
            {
                return result;
            }

            int limit = Math.Min(count, maxChoiceCount);
            foreach (string actionId in small.ActionIds)
            {
                if (result.Count >= limit)
                {
                    break;
                }

                cfg.GameAction action = tables.TbAction.GetOrDefault(actionId);
                if (!ActionRandomService.IsAvailable(run, action))
                {
                    continue;
                }

                float costDays = run.SnapshotDailyActionCost(RollCostDays(action, rng));
                result.Add(new ActionChoice(
                    action,
                    large.Id,
                    run.ActionStepIndex,
                    run.RunActionStepIndex,
                    costDays,
                    timelineStopChance: run.SnapshotTimelineStopChance()));
            }

            return result;
        }

        /// <summary>
        /// 「行动调整单」重掷：保留上一批里的 Boss 行动（Boss 不可重掷），其余用新 rng 重新生成填满。
        /// 当前小组池不含 Boss 行动，Boss 过滤为防御性逻辑（未来若加 Boss 小组仍正确）。
        /// </summary>
        public static List<ActionChoice> RerollChoices(GameRun run, IRandomStream rng, IReadOnlyList<ActionChoice> previous)
        {
            var result = new List<ActionChoice>();
            if (run == null || rng == null)
            {
                return result;
            }

            cfg.Tables tables = run.Tables ?? GameApp.Config.Tables;
            int maxChoiceCount = ActionRandomService.ChoiceCount(run);

            if (previous != null)
            {
                foreach (ActionChoice choice in previous)
                {
                    if (choice != null && choice.IsValid && IsBossAction(tables, choice.Action))
                    {
                        result.Add(choice);
                    }
                }
            }

            if (result.Count >= maxChoiceCount)
            {
                return result;
            }

            foreach (ActionChoice choice in GenerateChoices(run, rng, maxChoiceCount))
            {
                if (result.Count >= maxChoiceCount)
                {
                    break;
                }

                if (choice == null || !choice.IsValid || IsBossAction(tables, choice.Action))
                {
                    continue;
                }

                result.Add(choice);
            }

            return result;
        }

        private static bool IsBossAction(cfg.Tables tables, cfg.GameAction action)
        {
            return FoodService.IsBossAction(tables, action);
        }

        private static cfg.ActionSmallGroup PickSmall(cfg.Tables tables, cfg.ActionLargeGroup large, IRandomStream rng)
        {
            var smalls = new List<cfg.ActionSmallGroup>();
            var weights = new List<float>();
            foreach (string smallGroupId in large.SmallGroupIds)
            {
                cfg.ActionSmallGroup small = tables.TbActionSmallGroup.GetOrDefault(smallGroupId);
                if (small != null)
                {
                    smalls.Add(small);
                    float defaultWeight = Math.Max(float.Epsilon, tables.TbGameBase.DefaultRandomWeight);
                    weights.Add(small.Weight > 0f ? small.Weight : defaultWeight);
                }
            }

            if (smalls.Count == 0)
            {
                return null;
            }

            return smalls[rng.WeightedPickIndex(weights)];
        }

        /// <summary>
        /// 保证当前整局行动步（<see cref="GameRun.RunActionStepIndex"/>）对应的大组已生成并返回。
        /// 同一步重复进入或重掷只读取已保存序列，不会重复抽取或重复计数。
        /// </summary>
        public static string EnsureCurrentGroup(GameRun run, IRandomStream rng)
        {
            if (run == null || rng == null)
            {
                return string.Empty;
            }

            while (run.ActionGroupSequence.Count <= run.RunActionStepIndex)
            {
                int nextRunStep = run.ActionGroupSequence.Count;
                run.AppendActionGroup(PickLargeGroup(run, rng, nextRunStep));
            }

            return run.ActionGroupSequence[run.RunActionStepIndex];
        }

        private static string PickLargeGroup(GameRun run, IRandomStream rng, int nextRunStep)
        {
            cfg.Tables tables = run.Tables ?? GameApp.Config.Tables;
            var groups = new List<cfg.ActionLargeGroup>();
            foreach (cfg.ActionLargeGroup group in tables.TbActionLargeGroup.DataList)
            {
                if (group != null)
                {
                    groups.Add(group);
                }
            }

            if (groups.Count == 0)
            {
                return string.Empty;
            }

            int weekStartRunStep = Math.Max(0, run.RunActionStepIndex - run.ActionStepIndex);
            int weekActionIndex = Math.Max(0, nextRunStep - weekStartRunStep);
            Dictionary<string, int> counts = CountGroups(
                run.ActionGroupSequence,
                weekStartRunStep,
                nextRunStep);

            List<cfg.ActionLargeGroup> candidates = FindBelowGuarantee(
                groups,
                counts,
                run.WeekIndex,
                weekActionIndex,
                useMinimum: true);
            if (candidates.Count == 0)
            {
                candidates = FindBelowGuarantee(
                    groups,
                    counts,
                    run.WeekIndex,
                    weekActionIndex,
                    useMinimum: false);
            }

            if (candidates.Count == 0)
            {
                candidates = groups;
            }

            return PickWeightedByFallback(run, candidates, rng);
        }

        private static Dictionary<string, int> CountGroups(
            IReadOnlyList<string> sequence,
            int startInclusive,
            int endExclusive)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (sequence == null)
            {
                return counts;
            }

            int start = Math.Max(0, startInclusive);
            int end = Math.Min(Math.Max(start, endExclusive), sequence.Count);
            for (int index = start; index < end; index++)
            {
                string groupId = sequence[index];
                if (string.IsNullOrEmpty(groupId))
                {
                    continue;
                }

                counts.TryGetValue(groupId, out int count);
                counts[groupId] = count + 1;
            }

            return counts;
        }

        private static List<cfg.ActionLargeGroup> FindBelowGuarantee(
            IReadOnlyList<cfg.ActionLargeGroup> groups,
            IReadOnlyDictionary<string, int> counts,
            int weekIndex,
            int weekActionIndex,
            bool useMinimum)
        {
            var candidates = new List<cfg.ActionLargeGroup>();
            foreach (cfg.ActionLargeGroup group in groups)
            {
                IReadOnlyList<List<int>> bounds = useMinimum
                    ? group.MinGuaranteeCounts
                    : group.MaxGuaranteeCounts;
                if (!TryGetGuarantee(bounds, weekIndex, weekActionIndex, out int target))
                {
                    continue;
                }

                counts.TryGetValue(group.Id, out int current);
                if (current < target)
                {
                    candidates.Add(group);
                }
            }

            return candidates;
        }

        /// <summary>周或行动索引越界、值为负数都表示该位置未配置；上下限不沿用末项。</summary>
        private static bool TryGetGuarantee(
            IReadOnlyList<List<int>> bounds,
            int weekIndex,
            int weekActionIndex,
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

            IReadOnlyList<int> actions = bounds[week];
            if (actions == null || weekActionIndex < 0 || weekActionIndex >= actions.Count)
            {
                return false;
            }

            value = actions[weekActionIndex];
            return value >= 0;
        }

        private static string PickWeightedByFallback(GameRun run, List<cfg.ActionLargeGroup> groups, IRandomStream rng)
        {
            if (groups.Count == 0)
            {
                return string.Empty;
            }

            if (groups.Count == 1)
            {
                return groups[0].Id;
            }

            var weights = new List<float>(groups.Count);
            var eventBonusApplied = new List<bool>(groups.Count);
            float total = 0f;
            cfg.Tables tables = run.Tables ?? GameApp.Config.Tables;
            var itemRuntime = new ItemRuntime(run);
            float superActionBonus = itemRuntime.SuperActionLargeGroupWeightBonus();
            float eventActionBonus = itemRuntime.EventActionLargeGroupWeightBonus();
            foreach (cfg.ActionLargeGroup group in groups)
            {
                float w = FallbackWeight(group, run.WeekIndex);
                w = ApplySuperActionWeightBonus(
                    w,
                    ContainsSuperAction(tables, group),
                    superActionBonus);
                float beforeEventBonus = w;
                w = ApplyEventActionWeightBonus(
                    w,
                    ContainsEventAction(tables, group),
                    eventActionBonus);
                if (!(w > 0f) || float.IsNaN(w) || float.IsInfinity(w))
                {
                    w = 0f;
                }
                weights.Add(w);
                eventBonusApplied.Add(
                    beforeEventBonus > 0f
                    && w > 0f
                    && Math.Abs(w - beforeEventBonus) > 0.0001f);
                total += w;
            }

            // 仅当保底候选全部为 0 权重时做等概率兜底，保证强制保底仍能落地。
            int index = total > 0f
                ? rng.WeightedPickIndex(weights)
                : rng.Range(0, groups.Count);
            index = Math.Max(0, Math.Min(index, groups.Count - 1));
            if (eventBonusApplied[index])
            {
                itemRuntime.FlashTriggered(
                    model => Math.Abs(model.EventActionLargeGroupWeightBonus()) > 0.0001f);
            }

            return groups[index].Id;
        }

        /// <summary>
        /// 大组保底候选集确定之后，仅对其中包含 Super 行动的大组乘以 (1 + bonus)。
        /// </summary>
        internal static float ApplySuperActionWeightBonus(float baseWeight, bool containsSuper, float bonus)
        {
            if (!containsSuper || !(baseWeight > 0f) || float.IsNaN(bonus))
            {
                return baseWeight;
            }

            float multiplier = Math.Max(0f, 1f + bonus);
            return baseWeight * multiplier;
        }

        /// <summary>
        /// 大组保底候选集确定之后，仅对其中包含 Event 行动的大组乘以 (1 + bonus)。
        /// 原始/前序权重为 0 时保持 0，不允许概率装饰品和消耗品复活零权重大组。
        /// </summary>
        internal static float ApplyEventActionWeightBonus(float baseWeight, bool containsEvent, float bonus)
        {
            if (!containsEvent || !(baseWeight > 0f) || float.IsNaN(bonus))
            {
                return baseWeight;
            }

            float multiplier = Math.Max(0f, 1f + bonus);
            return baseWeight * multiplier;
        }

        internal static bool ContainsSuperAction(cfg.Tables tables, cfg.ActionLargeGroup group)
        {
            if (tables == null || group?.SmallGroupIds == null)
            {
                return false;
            }

            foreach (string smallGroupId in group.SmallGroupIds)
            {
                cfg.ActionSmallGroup small = tables.TbActionSmallGroup.GetOrDefault(smallGroupId);
                if (small?.ActionIds == null)
                {
                    continue;
                }

                foreach (string actionId in small.ActionIds)
                {
                    cfg.GameAction action = tables.TbAction.GetOrDefault(actionId);
                    cfg.Food food = FoodService.Resolve(tables, action);
                    if (food?.ActionKind == cfg.FoodActionKind.Super)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static bool ContainsEventAction(cfg.Tables tables, cfg.ActionLargeGroup group)
        {
            if (tables == null || group?.SmallGroupIds == null)
            {
                return false;
            }

            foreach (string smallGroupId in group.SmallGroupIds)
            {
                cfg.ActionSmallGroup small = tables.TbActionSmallGroup.GetOrDefault(smallGroupId);
                if (small?.ActionIds == null)
                {
                    continue;
                }

                foreach (string actionId in small.ActionIds)
                {
                    cfg.GameAction action = tables.TbAction.GetOrDefault(actionId);
                    if (action?.Behavior == cfg.ActionBehavior.Event)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>大组保底权重：按当前周(1-based)取 fallbackWeights[周-1]，越界取最后一个；空列表按 1。</summary>
        private static float FallbackWeight(cfg.ActionLargeGroup group, int weekIndex)
        {
            IReadOnlyList<float> weights = group.FallbackWeights;
            if (weights == null || weights.Count == 0)
            {
                return 1f;
            }

            int idx = Math.Max(0, weekIndex - 1);
            if (idx >= weights.Count)
            {
                idx = weights.Count - 1;
            }

            float w = weights[idx];
            return w > 0f ? w : 0f;
        }

        private static float RollCostDays(cfg.GameAction action, IRandomStream rng)
        {
            float min = action != null ? action.MinCostDays : 0f;
            float max = action != null && action.MaxCostDays > 0f ? action.MaxCostDays : min;
            if (max < min)
            {
                (min, max) = (max, min);
            }

            // 以 0.1 天为粒度在 [min, max] 闭区间内随机（换算成十分之一天的整数步再取回），保证确定性与粒度对齐。
            int minTenths = (int)Math.Round(min * 10f, MidpointRounding.AwayFromZero);
            int maxTenths = (int)Math.Round(max * 10f, MidpointRounding.AwayFromZero);
            int tenths = minTenths == maxTenths ? minTenths : rng.Range(minTenths, maxTenths + 1);
            return TimelineMath.Quantize(tenths / 10f);
        }

    }
}
