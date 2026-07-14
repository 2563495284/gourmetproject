using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 整局行动组序列（大组）与本次 n 选一（大组→小组）生成。
    /// - 大组间：日程规则 <see cref="cfg.ActionScheduleRule"/> 只做「每周保底」——每周开始时把各规则的 minCount
    ///   随机散布进其窗口（窗口按「周内行动步序号」定义），其余空位按大组 fallbackWeights[周-1] 权重随机充填。
    /// - 规则的窗口/计数按周独立，count 每周清空；activeWeeks 决定该规则在哪些周生效。
    /// - 大组→小组：按 <see cref="cfg.ActionSmallGroup.Weight"/> 选 1 个小组。
    /// - 小组：固定成员，经可用性过滤后即本次 n 选一。
    /// </summary>
    public static class ActionScheduleService
    {
        public static List<ActionChoice> GenerateChoices(GameRun run, IRandomStream rng, int count = ActionRandomService.MaxChoiceCount)
        {
            var result = new List<ActionChoice>();
            if (run == null || rng == null || count <= 0)
            {
                return result;
            }

            // 事件概率族被动（LuckyEventChance/MoreEvents/LuckyEventGuarantee）作用于「抽事件」层，
            //   实现见 EventService.RollActionEvent 与 WeekLoopController.ResolveEventAction，不在此大组/小组权重里注入。

            cfg.Tables tables = run.Tables ?? GameApp.Config.Tables;
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

            int limit = Math.Min(count, ActionRandomService.MaxChoiceCount);
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

                float costDays = RollCostDays(action, rng);
                result.Add(new ActionChoice(action, large.Id, run.ActionStepIndex, run.RunActionStepIndex, costDays));
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

            if (result.Count >= ActionRandomService.MaxChoiceCount)
            {
                return result;
            }

            foreach (ActionChoice choice in GenerateChoices(run, rng, ActionRandomService.MaxChoiceCount))
            {
                if (result.Count >= ActionRandomService.MaxChoiceCount)
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
                    weights.Add(small.Weight > 0f ? small.Weight : 1f);
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
        /// 每周首次访问时构建「本周计划」（窗口散布 + 权重充填）；窗口之外按权重遅延生成。
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
                EnsureWeekPlan(run, rng, nextRunStep);

                string groupId = null;
                int weekLocalIndex = nextRunStep - run.ActionWeekPlanStartRunStep;
                IReadOnlyList<string> plan = run.ActionWeekPlan;
                if (weekLocalIndex >= 0 && weekLocalIndex < plan.Count && !string.IsNullOrEmpty(plan[weekLocalIndex]))
                {
                    groupId = plan[weekLocalIndex];
                }

                if (string.IsNullOrEmpty(groupId))
                {
                    // 窗口之外（本周步数超过计划长度）：仅按权重随机，与上一格避免重复。
                    groupId = PickBeyondWindow(run, rng);
                }

                run.AppendActionGroup(groupId);
            }

            return run.ActionGroupSequence[run.RunActionStepIndex];
        }

        /// <summary>本周计划缺失或已过期（周切换）时重建。以 <see cref="GameRun.WeekIndex"/> 作为构建标记。</summary>
        private static void EnsureWeekPlan(GameRun run, IRandomStream rng, int nextRunStep)
        {
            if (run.ActionWeekPlanWeek == run.WeekIndex)
            {
                return;
            }

            List<string> plan = BuildWeekPlan(run, rng, nextRunStep);
            run.SetActionWeekPlan(run.WeekIndex, nextRunStep, plan);
        }

        /// <summary>
        /// 构建本周大组计划：先把每条生效规则的 minCount 散布进其窗口的随机空位（保底），
        /// 再把剩余空位按 fallbackWeights[周-1] 权重充填（尊重每规则 maxCount、避免与相邻大组重复）。
        /// </summary>
        private static List<string> BuildWeekPlan(GameRun run, IRandomStream rng, int planStartRunStep)
        {
            cfg.Tables tables = run.Tables ?? GameApp.Config.Tables;

            var rules = new List<cfg.ActionScheduleRule>();
            foreach (cfg.ActionScheduleRule rule in tables.TbActionScheduleRule.DataList)
            {
                // 约定：maxRunStep 必须为有限上限(>0)；无有限窗口的规则无法参与本周散布，直接忽略。
                if (rule.MaxRunStep > 0 && IsRuleActiveThisWeek(run, rule))
                {
                    rules.Add(rule);
                }
            }

            rules.Sort((a, b) =>
            {
                int priority = b.Priority.CompareTo(a.Priority);
                return priority != 0 ? priority : string.Compare(a.Id, b.Id, StringComparison.Ordinal);
            });

            int horizon = 0;
            foreach (cfg.ActionScheduleRule rule in rules)
            {
                if (rule.MaxRunStep > horizon)
                {
                    horizon = rule.MaxRunStep;
                }
            }

            var plan = new List<string>();
            if (horizon <= 0)
            {
                return plan;
            }

            var slots = new string[horizon];

            // 1) 保底散布：优先级降序依次为每条规则挑选窗口内的随机空位并落大组。
            foreach (cfg.ActionScheduleRule rule in rules)
            {
                int lo = Math.Max(1, rule.MinRunStep);
                int hi = Math.Min(rule.MaxRunStep, horizon);
                if (lo > hi)
                {
                    continue;
                }

                int maxCount = rule.MaxCount > 0 ? rule.MaxCount : int.MaxValue;
                int need = Math.Min(Math.Max(0, rule.MinCount), maxCount);
                if (need <= 0)
                {
                    continue;
                }

                var free = new List<int>();
                for (int s = lo; s <= hi; s++)
                {
                    if (string.IsNullOrEmpty(slots[s - 1]))
                    {
                        free.Add(s);
                    }
                }

                int placed = 0;
                while (placed < need && free.Count > 0)
                {
                    int pickIndex = free.Count == 1 ? 0 : rng.Range(0, free.Count);
                    int slot = free[pickIndex];
                    free.RemoveAt(pickIndex);

                    string groupId = PickRuleGroup(run, rule, rng);
                    if (string.IsNullOrEmpty(groupId))
                    {
                        // 该规则没有可用大组，放弃剩余保底名额。
                        break;
                    }

                    slots[slot - 1] = groupId;
                    placed++;
                }
            }

            // 2) 权重充填：剩余空位按顺序取权重随机，避免与前/后已定大组重复，且不越过任何规则 maxCount。
            string prevGroup = planStartRunStep > 0 && planStartRunStep - 1 < run.ActionGroupSequence.Count
                ? run.ActionGroupSequence[planStartRunStep - 1]
                : string.Empty;

            for (int i = 0; i < horizon; i++)
            {
                if (!string.IsNullOrEmpty(slots[i]))
                {
                    prevGroup = slots[i];
                    continue;
                }

                string nextGroup = i + 1 < horizon ? slots[i + 1] : string.Empty;
                string groupId = PickWeightedFill(run, rng, i + 1, prevGroup, nextGroup, rules, slots);
                slots[i] = groupId;
                prevGroup = groupId;
            }

            plan.AddRange(slots);
            return plan;
        }

        private static bool IsRuleActiveThisWeek(GameRun run, cfg.ActionScheduleRule rule)
        {
            if (run == null || rule == null || !PreconditionEvaluator.IsSatisfied(run, rule.Preconditions))
            {
                return false;
            }

            return IsRuleWeekActive(rule, run.WeekIndex);
        }

        /// <summary>从「声明了该规则」的可选大组里按保底权重挑一个（供散布落位）。</summary>
        private static string PickRuleGroup(GameRun run, cfg.ActionScheduleRule rule, IRandomStream rng)
        {
            var groups = new List<cfg.ActionLargeGroup>();
            foreach (cfg.ActionLargeGroup group in run.Tables.TbActionLargeGroup.DataList)
            {
                if (ContainsId(group.RuleIds, rule.Id) && IsGroupSelectable(run, group))
                {
                    groups.Add(group);
                }
            }

            return PickWeightedByFallback(run, groups, rng);
        }

        /// <summary>权重充填单个空位：排除超 maxCount 的大组，并避免与前/后相邻大组重复；候选枯竭时逐步放宽。</summary>
        private static string PickWeightedFill(GameRun run, IRandomStream rng, int weekStep, string prevGroup, string nextGroup, List<cfg.ActionScheduleRule> rules, string[] slots)
        {
            var groups = new List<cfg.ActionLargeGroup>();
            foreach (cfg.ActionLargeGroup group in run.Tables.TbActionLargeGroup.DataList)
            {
                if (IsGroupSelectable(run, group) && !WouldExceedMaxCountAt(run, group, slots, weekStep, rules))
                {
                    groups.Add(group);
                }
            }

            if (groups.Count == 0)
            {
                // maxCount 把候选清空了：退回「仅 selectable」，宁可略过 maxCount 也要产出一个大组。
                foreach (cfg.ActionLargeGroup group in run.Tables.TbActionLargeGroup.DataList)
                {
                    if (IsGroupSelectable(run, group))
                    {
                        groups.Add(group);
                    }
                }
            }

            RemoveAvoid(groups, prevGroup);
            RemoveAvoid(groups, nextGroup);

            return PickWeightedByFallback(run, groups, rng);
        }

        /// <summary>窗口之外的遅延生成：仅按权重随机，与上一格避免重复。</summary>
        private static string PickBeyondWindow(GameRun run, IRandomStream rng)
        {
            string prevGroup = run.ActionGroupSequence.Count > 0
                ? run.ActionGroupSequence[run.ActionGroupSequence.Count - 1]
                : string.Empty;

            var groups = new List<cfg.ActionLargeGroup>();
            foreach (cfg.ActionLargeGroup group in run.Tables.TbActionLargeGroup.DataList)
            {
                if (IsGroupSelectable(run, group))
                {
                    groups.Add(group);
                }
            }

            RemoveAvoid(groups, prevGroup);
            return PickWeightedByFallback(run, groups, rng);
        }

        private static void RemoveAvoid(List<cfg.ActionLargeGroup> groups, string avoidId)
        {
            if (groups.Count > 1 && !string.IsNullOrEmpty(avoidId))
            {
                groups.RemoveAll(group => group.Id == avoidId);
            }
        }

        private static string PickWeightedByFallback(GameRun run, List<cfg.ActionLargeGroup> groups, IRandomStream rng)
        {
            if (groups.Count == 0)
            {
                return string.Empty;
            }

            var weights = new List<float>(groups.Count);
            foreach (cfg.ActionLargeGroup group in groups)
            {
                // 保底权重 0 但参与规则的大组，给一个极小正值保证仍可被选中（也避免总权重为 0）。
                float w = FallbackWeight(group, run.WeekIndex);
                weights.Add(w > 0f ? w : 0.0001f);
            }

            return groups[rng.WeightedPickIndex(weights)].Id;
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

        /// <summary>大组是否可能出现：纯保底权重为 0 且不参与任何规则的大组永不进入序列。</summary>
        private static bool IsGroupSelectable(GameRun run, cfg.ActionLargeGroup group)
        {
            if (run == null || group == null)
            {
                return false;
            }

            if (FallbackWeight(group, run.WeekIndex) <= 0f && !ContainsAnyRule(group))
            {
                return false;
            }

            return true;
        }

        private static bool ContainsAnyRule(cfg.ActionLargeGroup group)
        {
            foreach (string _ in ParseIds(group.RuleIds))
            {
                return true;
            }

            return false;
        }

        /// <summary>候选大组落在 weekStep 位置时，是否会让某条覆盖该位置的规则在其窗口内超过 maxCount。</summary>
        private static bool WouldExceedMaxCountAt(GameRun run, cfg.ActionLargeGroup candidate, string[] slots, int weekStep, List<cfg.ActionScheduleRule> rules)
        {
            foreach (cfg.ActionScheduleRule rule in rules)
            {
                if (rule.MaxCount <= 0 || !ContainsId(candidate.RuleIds, rule.Id))
                {
                    continue;
                }

                int lo = Math.Max(1, rule.MinRunStep);
                int hi = Math.Min(rule.MaxRunStep, slots.Length);
                if (weekStep < lo || weekStep > hi)
                {
                    continue;
                }

                int count = 0;
                for (int s = lo; s <= hi; s++)
                {
                    string id = slots[s - 1];
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    cfg.ActionLargeGroup group = run.Tables.TbActionLargeGroup.GetOrDefault(id);
                    if (group != null && ContainsId(group.RuleIds, rule.Id))
                    {
                        count++;
                    }
                }

                if (count >= rule.MaxCount)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsRuleWeekActive(cfg.ActionScheduleRule rule, int weekIndex)
        {
            IReadOnlyList<int> activeWeeks = rule.ActiveWeeks;
            if (activeWeeks == null || activeWeeks.Count == 0)
            {
                return true;
            }

            int currentWeek = Math.Max(1, weekIndex);
            foreach (int activeWeek in activeWeeks)
            {
                if (activeWeek == currentWeek)
                {
                    return true;
                }
            }

            return false;
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

        private static bool ContainsId(string ids, string value)
        {
            foreach (string id in ParseIds(ids))
            {
                if (string.Equals(id, value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> ParseIds(string ids)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(ids))
            {
                return result;
            }

            string[] parts = ids.Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string id = part.Trim();
                if (id.Length > 0)
                {
                    result.Add(id);
                }
            }

            return result;
        }
    }
}
