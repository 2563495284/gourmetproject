using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 整局行动组序列（大组）与本次 n 选一（大组→中组→小组）生成。
    /// - 大组间：日程规则 <see cref="cfg.ActionScheduleRule"/> 强制窗口 + 大组 fallbackWeights[周-1] 保底加权。
    /// - 大组→中组：按 <see cref="cfg.ActionMediumGroup.Weight"/> 选 1 个中组。
    /// - 中组→小组：按 <see cref="cfg.ActionMediumMember.Weight"/> 选 1 个小组。
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

            cfg.Tables tables = run.Tables ?? GameApp.Config.Tables;
            string largeId = EnsureCurrentGroup(run, rng);
            cfg.ActionLargeGroup large = string.IsNullOrEmpty(largeId) ? null : tables.TbActionLargeGroup.GetOrDefault(largeId);
            if (large == null)
            {
                return result;
            }

            cfg.ActionMediumGroup medium = PickMedium(tables, large, rng);
            cfg.ActionSmallGroup small = medium == null ? null : PickSmall(tables, medium, rng);
            if (small == null)
            {
                return result;
            }

            int limit = Math.Min(count, ActionRandomService.MaxChoiceCount);
            foreach (cfg.ActionSmallMember member in tables.TbActionSmallMember.DataList)
            {
                if (result.Count >= limit)
                {
                    break;
                }

                if (member.SmallGroupId != small.Id)
                {
                    continue;
                }

                cfg.GameAction action = tables.TbAction.GetOrDefault(member.ActionId);
                if (!ActionRandomService.IsAvailable(run, action))
                {
                    continue;
                }

                float costDays = RollCostDays(action, rng);
                result.Add(new ActionChoice(action, large.Id, run.ActionStepIndex, run.RunActionStepIndex, costDays));
            }

            return result;
        }

        private static cfg.ActionMediumGroup PickMedium(cfg.Tables tables, cfg.ActionLargeGroup large, IRandomStream rng)
        {
            var mediums = new List<cfg.ActionMediumGroup>();
            foreach (cfg.ActionLargeMember member in tables.TbActionLargeMember.DataList)
            {
                if (member.LargeGroupId != large.Id)
                {
                    continue;
                }

                cfg.ActionMediumGroup medium = tables.TbActionMediumGroup.GetOrDefault(member.MediumGroupId);
                if (medium != null)
                {
                    mediums.Add(medium);
                }
            }

            if (mediums.Count == 0)
            {
                return null;
            }

            var weights = new List<float>(mediums.Count);
            foreach (cfg.ActionMediumGroup medium in mediums)
            {
                weights.Add(medium.Weight > 0f ? medium.Weight : 1f);
            }

            return mediums[rng.WeightedPickIndex(weights)];
        }

        private static cfg.ActionSmallGroup PickSmall(cfg.Tables tables, cfg.ActionMediumGroup medium, IRandomStream rng)
        {
            var smalls = new List<cfg.ActionSmallGroup>();
            var weights = new List<float>();
            foreach (cfg.ActionMediumMember member in tables.TbActionMediumMember.DataList)
            {
                if (member.MediumGroupId != medium.Id)
                {
                    continue;
                }

                cfg.ActionSmallGroup small = tables.TbActionSmallGroup.GetOrDefault(member.SmallGroupId);
                if (small != null)
                {
                    smalls.Add(small);
                    weights.Add(member.Weight > 0f ? member.Weight : 1f);
                }
            }

            if (smalls.Count == 0)
            {
                return null;
            }

            return smalls[rng.WeightedPickIndex(weights)];
        }

        public static string EnsureCurrentGroup(GameRun run, IRandomStream rng)
        {
            if (run == null || rng == null)
            {
                return string.Empty;
            }

            while (run.ActionGroupSequence.Count <= run.RunActionStepIndex)
            {
                int runStep = run.ActionGroupSequence.Count + 1;
                run.AppendActionGroup(PickGroupForStep(run, runStep, rng));
            }

            return run.ActionGroupSequence[run.RunActionStepIndex];
        }

        private static string PickGroupForStep(GameRun run, int runStep, IRandomStream rng)
        {
            cfg.Tables tables = run.Tables ?? GameApp.Config.Tables;
            var rules = new List<cfg.ActionScheduleRule>(tables.TbActionScheduleRule.DataList);
            rules.Sort((a, b) =>
            {
                int priority = b.Priority.CompareTo(a.Priority);
                return priority != 0 ? priority : string.Compare(a.Id, b.Id, StringComparison.Ordinal);
            });

            foreach (cfg.ActionScheduleRule rule in rules)
            {
                if (!IsRuleActive(run, rule, runStep))
                {
                    continue;
                }

                int count = CountRuleGroupsInWindow(run, rule);
                if (count < Math.Max(0, rule.MinCount))
                {
                    string picked = PickFromRule(run, rule, runStep, rng);
                    if (!string.IsNullOrEmpty(picked))
                    {
                        return picked;
                    }
                }
            }

            return PickWeightedGroup(run, runStep, rng);
        }

        private static string PickFromRule(GameRun run, cfg.ActionScheduleRule rule, int runStep, IRandomStream rng)
        {
            var groups = new List<cfg.ActionLargeGroup>();
            foreach (cfg.ActionLargeGroup group in run.Tables.TbActionLargeGroup.DataList)
            {
                if (ContainsId(group.RuleIds, rule.Id) && IsGroupAvailable(run, group, runStep))
                {
                    groups.Add(group);
                }
            }

            return PickWeighted(run, groups, rng, avoidRepeat: true);
        }

        private static string PickWeightedGroup(GameRun run, int runStep, IRandomStream rng)
        {
            var groups = new List<cfg.ActionLargeGroup>();
            foreach (cfg.ActionLargeGroup group in run.Tables.TbActionLargeGroup.DataList)
            {
                if (IsGroupAvailable(run, group, runStep))
                {
                    groups.Add(group);
                }
            }

            string picked = PickWeighted(run, groups, rng, avoidRepeat: true);
            if (!string.IsNullOrEmpty(picked))
            {
                return picked;
            }

            groups.Clear();
            foreach (cfg.ActionLargeGroup group in run.Tables.TbActionLargeGroup.DataList)
            {
                if (IsGroupAvailable(run, group, runStep, ignoreRuleMax: true))
                {
                    groups.Add(group);
                }
            }

            return PickWeighted(run, groups, rng, avoidRepeat: false);
        }

        private static string PickWeighted(GameRun run, List<cfg.ActionLargeGroup> groups, IRandomStream rng, bool avoidRepeat)
        {
            if (groups.Count == 0)
            {
                return string.Empty;
            }

            string lastGroup = run.ActionGroupSequence.Count > 0 ? run.ActionGroupSequence[run.ActionGroupSequence.Count - 1] : string.Empty;
            if (avoidRepeat && groups.Count > 1)
            {
                groups.RemoveAll(group => group.Id == lastGroup);
            }

            var weights = new List<float>(groups.Count);
            foreach (cfg.ActionLargeGroup group in groups)
            {
                // 保底权重 0 的组通常已被 IsGroupAvailable 排除；能走到这里的 0 权重组是被规则强制选出的，
                // 下限设一个极小正值保证仍可被选中（也避免总权重为 0）。
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

        private static bool IsGroupAvailable(GameRun run, cfg.ActionLargeGroup group, int runStep, bool ignoreRuleMax = false)
        {
            if (run == null || group == null)
            {
                return false;
            }

            if (FallbackWeight(group, run.WeekIndex) <= 0f && !ContainsAnyRule(group))
            {
                // 纯保底权重为 0 且不参与任何规则的大组，永不进入序列。
                return false;
            }

            if (!PreconditionEvaluator.IsSatisfied(run, group.Preconditions))
            {
                return false;
            }

            return ignoreRuleMax || !WouldExceedRuleMax(run, group, runStep);
        }

        private static bool ContainsAnyRule(cfg.ActionLargeGroup group)
        {
            foreach (string _ in ParseIds(group.RuleIds))
            {
                return true;
            }

            return false;
        }

        private static bool WouldExceedRuleMax(GameRun run, cfg.ActionLargeGroup group, int runStep)
        {
            foreach (cfg.ActionScheduleRule rule in run.Tables.TbActionScheduleRule.DataList)
            {
                if (!IsRuleActive(run, rule, runStep) || !ContainsId(group.RuleIds, rule.Id) || rule.MaxCount <= 0)
                {
                    continue;
                }

                if (CountRuleGroupsInWindow(run, rule) >= rule.MaxCount)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountRuleGroupsInWindow(GameRun run, cfg.ActionScheduleRule rule)
        {
            int count = 0;
            int min = Math.Max(1, rule.MinRunStep);
            int max = rule.MaxRunStep > 0 ? rule.MaxRunStep : int.MaxValue;
            for (int i = 0; i < run.ActionGroupSequence.Count; i++)
            {
                int step = i + 1;
                if (step < min || step > max)
                {
                    continue;
                }

                cfg.ActionLargeGroup group = run.Tables.TbActionLargeGroup.GetOrDefault(run.ActionGroupSequence[i]);
                if (group != null && ContainsId(group.RuleIds, rule.Id))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsRuleActive(GameRun run, cfg.ActionScheduleRule rule, int runStep)
        {
            if (rule == null || !PreconditionEvaluator.IsSatisfied(run, rule.Preconditions))
            {
                return false;
            }

            int min = Math.Max(1, rule.MinRunStep);
            int max = rule.MaxRunStep > 0 ? rule.MaxRunStep : int.MaxValue;
            return runStep >= min && runStep <= max;
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
