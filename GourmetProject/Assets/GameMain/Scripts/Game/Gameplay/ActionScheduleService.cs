using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.Gameplay
{
    /// <summary>
    /// v2 行动日程：按配置约束预生成行动组序列，再把每个行动组展开为本步可选行动。
    /// </summary>
    public static class ActionScheduleService
    {
        private const string Tag = "ActionSchedule";
        private const int DefaultSequenceLength = 12;

        public static bool HasCurrentStep(GameRun run)
        {
            return run != null && run.ActionStepIndex >= 0 && run.ActionStepIndex < run.ScheduledActionSteps.Count;
        }

        public static ActionScheduleStep CurrentStep(GameRun run)
        {
            return HasCurrentStep(run) ? run.ScheduledActionSteps[run.ActionStepIndex] : null;
        }

        public static bool IsScheduleFinished(GameRun run)
        {
            return run == null || run.ActionStepIndex >= run.ScheduledActionSteps.Count;
        }

        public static void EnsureSchedule(GameRun run, IRandomStream rng)
        {
            if (run == null || rng == null)
            {
                return;
            }

            if (run.ScheduledActionSteps.Count > 0 && run.ActionStepIndex < run.ScheduledActionSteps.Count)
            {
                run.TimelineLengthDays = Math.Max(run.TimelineLengthDays, run.ScheduledActionSteps.Count);
                return;
            }

            RollSchedule(run, rng);
        }

        public static void RollSchedule(GameRun run, IRandomStream rng)
        {
            if (run == null || rng == null)
            {
                return;
            }

            cfg.Tables tables = run.Tables ?? GameApp.Config.Tables;
            int length = DetermineSequenceLength(tables, run);
            var groupIds = new string[length];

            List<cfg.ActionScheduleRule> rules = MatchingRules(tables, run);
            rules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            foreach (cfg.ActionScheduleRule rule in rules)
            {
                ApplyRule(rule, groupIds, rng);
            }

            FillRemainingGroups(tables, run, groupIds, rng);
            List<ActionScheduleStep> steps = ExpandSteps(tables, groupIds, rng);
            run.SetActionSchedule(steps);
            run.TimelineLengthDays = Math.Max(run.TimelineLengthDays, steps.Count);
            Log.Info($"第 {run.WeekIndex} 周行动组序列生成：{steps.Count} 步。", Tag);
        }

        public static void AdvanceStep(GameRun run)
        {
            run?.AdvanceActionStep();
        }

        private static int DetermineSequenceLength(cfg.Tables tables, GameRun run)
        {
            int length = Math.Max(DefaultSequenceLength, run?.TimelineLengthDays ?? 0);
            foreach (cfg.ActionScheduleRule rule in tables.TbActionScheduleRule.DataList)
            {
                if (!MatchesWeek(rule.WeekFilter, run))
                {
                    continue;
                }

                length = Math.Max(length, rule.EndIndex);
                if (rule.WindowSize > 0 && rule.WindowEnd > 0)
                {
                    length = Math.Max(length, rule.WindowSize + rule.WindowEnd);
                }
            }

            return Math.Max(1, length);
        }

        private static List<cfg.ActionScheduleRule> MatchingRules(cfg.Tables tables, GameRun run)
        {
            var result = new List<cfg.ActionScheduleRule>();
            foreach (cfg.ActionScheduleRule rule in tables.TbActionScheduleRule.DataList)
            {
                if (MatchesWeek(rule.WeekFilter, run))
                {
                    result.Add(rule);
                }
            }

            return result;
        }

        private static void ApplyRule(cfg.ActionScheduleRule rule, string[] groupIds, IRandomStream rng)
        {
            if (rule == null || string.IsNullOrEmpty(rule.GroupId) || groupIds == null || groupIds.Length == 0)
            {
                return;
            }

            if (string.Equals(rule.RuleType, "WindowCount", StringComparison.OrdinalIgnoreCase))
            {
                ApplyWindowRule(rule, groupIds, rng);
                return;
            }

            ApplyRangeRule(rule, groupIds, rng, rule.StartIndex, rule.EndIndex);
        }

        private static void ApplyWindowRule(cfg.ActionScheduleRule rule, string[] groupIds, IRandomStream rng)
        {
            int windowSize = Math.Max(1, rule.WindowSize);
            for (int windowStart = 1; windowStart <= groupIds.Length; windowStart += windowSize)
            {
                int start = windowStart + Math.Max(0, rule.WindowStart - 1);
                int end = windowStart + Math.Max(rule.WindowStart, rule.WindowEnd) - 1;
                ApplyRangeRule(rule, groupIds, rng, start, end);
            }
        }

        private static void ApplyRangeRule(cfg.ActionScheduleRule rule, string[] groupIds, IRandomStream rng, int startIndex, int endIndex)
        {
            int start = Math.Max(1, startIndex);
            int end = Math.Min(groupIds.Length, Math.Max(start, endIndex));
            if (start > end)
            {
                return;
            }

            int min = Math.Max(0, rule.MinCount);
            int max = Math.Max(min, rule.MaxCount > 0 ? rule.MaxCount : min);
            int count = max > min ? rng.Range(min, max + 1) : min;
            var available = new List<int>();
            for (int i = start - 1; i <= end - 1; i++)
            {
                if (string.IsNullOrEmpty(groupIds[i]))
                {
                    available.Add(i);
                }
            }

            for (int i = 0; i < count && available.Count > 0; i++)
            {
                int index = rng.Range(0, available.Count);
                int slot = available[index];
                available.RemoveAt(index);
                groupIds[slot] = rule.GroupId;
            }
        }

        private static void FillRemainingGroups(cfg.Tables tables, GameRun run, string[] groupIds, IRandomStream rng)
        {
            List<cfg.ActionGroup> groups = MatchingGroups(tables, run);
            if (groups.Count == 0)
            {
                Log.Warning("没有可用行动组，行动序列将为空。", Tag);
                return;
            }

            for (int i = 0; i < groupIds.Length; i++)
            {
                if (!string.IsNullOrEmpty(groupIds[i]))
                {
                    continue;
                }

                groupIds[i] = PickGroup(groups, PreviousGroup(groupIds, i), rng);
            }

            for (int i = 1; i < groupIds.Length; i++)
            {
                if (groupIds[i] == groupIds[i - 1])
                {
                    string replacement = PickGroup(groups, groupIds[i - 1], rng);
                    if (!string.IsNullOrEmpty(replacement))
                    {
                        groupIds[i] = replacement;
                    }
                }
            }
        }

        private static List<cfg.ActionGroup> MatchingGroups(cfg.Tables tables, GameRun run)
        {
            var result = new List<cfg.ActionGroup>();
            foreach (cfg.ActionGroup group in tables.TbActionGroup.DataList)
            {
                if (MatchesWeek(group.WeekFilter, run))
                {
                    result.Add(group);
                }
            }

            return result;
        }

        private static string PickGroup(List<cfg.ActionGroup> groups, string excludeGroupId, IRandomStream rng)
        {
            var candidates = new List<cfg.ActionGroup>();
            foreach (cfg.ActionGroup group in groups)
            {
                if (groups.Count > 1 && group.PreventRepeat && group.Id == excludeGroupId)
                {
                    continue;
                }

                candidates.Add(group);
            }

            if (candidates.Count == 0)
            {
                return groups.Count > 0 ? groups[0].Id : string.Empty;
            }

            var weights = new List<float>(candidates.Count);
            foreach (cfg.ActionGroup group in candidates)
            {
                weights.Add(group.Weight > 0f ? group.Weight : 1f);
            }

            return candidates[rng.WeightedPickIndex(weights)].Id;
        }

        private static string PreviousGroup(string[] groupIds, int index)
        {
            return index > 0 ? groupIds[index - 1] : string.Empty;
        }

        private static List<ActionScheduleStep> ExpandSteps(cfg.Tables tables, string[] groupIds, IRandomStream rng)
        {
            var result = new List<ActionScheduleStep>();
            for (int i = 0; i < groupIds.Length; i++)
            {
                string groupId = groupIds[i] ?? string.Empty;
                var choices = new List<ScheduledActionChoice>();
                foreach (cfg.ActionGroupMember member in tables.TbActionGroupMember.DataList)
                {
                    if (member.GroupId != groupId)
                    {
                        continue;
                    }

                    cfg.GameAction action = tables.TbAction.GetOrDefault(member.ActionId);
                    if (action == null)
                    {
                        continue;
                    }

                    int min = member.CostDaysMin > 0 ? member.CostDaysMin : action.CostDays;
                    int max = member.CostDaysMax > 0 ? member.CostDaysMax : min;
                    if (max < min)
                    {
                        max = min;
                    }

                    int cost = min == max ? min : rng.Range(min, max + 1);
                    choices.Add(new ScheduledActionChoice(groupId, action.Id, cost, i));
                }

                result.Add(new ActionScheduleStep(i, groupId, choices));
            }

            return result;
        }

        private static bool MatchesWeek(string weekFilter, GameRun run)
        {
            if (run == null)
            {
                return false;
            }

            return TimelineService.MatchesWeek(weekFilter, run.WeekIndex, run.IsBossWeek);
        }
    }
}
