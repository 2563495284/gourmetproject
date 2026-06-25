using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Meta
{
    /// <summary>整局行动组序列与组内候选生成。</summary>
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
            string groupId = EnsureCurrentGroup(run, rng);
            cfg.ActionGroup group = string.IsNullOrEmpty(groupId) ? null : tables.TbActionGroup.GetOrDefault(groupId);
            if (group == null)
            {
                return result;
            }

            var candidates = new List<cfg.ActionGroupMember>();
            foreach (cfg.ActionGroupMember member in tables.TbActionGroupMember.DataList)
            {
                if (member.GroupId != group.Id)
                {
                    continue;
                }

                cfg.GameAction action = tables.TbAction.GetOrDefault(member.ActionId);
                if (ActionRandomService.IsAvailable(run, action))
                {
                    candidates.Add(member);
                }
            }

            int choiceCount = Math.Min(count, ActionRandomService.MaxChoiceCount);
            for (int i = 0; i < choiceCount && candidates.Count > 0; i++)
            {
                var weights = new List<float>(candidates.Count);
                foreach (cfg.ActionGroupMember member in candidates)
                {
                    weights.Add(member.Weight > 0f ? member.Weight : 1f);
                }

                int index = rng.WeightedPickIndex(weights);
                cfg.ActionGroupMember chosen = candidates[index];
                candidates.RemoveAt(index);

                cfg.GameAction action = tables.TbAction.GetOrDefault(chosen.ActionId);
                int costDays = RollCostDays(chosen, action, rng);
                result.Add(new ActionChoice(action, group, run.ActionStepIndex, run.RunActionStepIndex, costDays));
            }

            return result;
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
            var groups = new List<cfg.ActionGroup>();
            foreach (string groupId in ParseIds(rule.GroupIds))
            {
                cfg.ActionGroup group = run.Tables.TbActionGroup.GetOrDefault(groupId);
                if (IsGroupAvailable(run, group, runStep))
                {
                    groups.Add(group);
                }
            }

            return PickWeighted(run, groups, rng, avoidRepeat: true);
        }

        private static string PickWeightedGroup(GameRun run, int runStep, IRandomStream rng)
        {
            var groups = new List<cfg.ActionGroup>();
            foreach (cfg.ActionGroup group in run.Tables.TbActionGroup.DataList)
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
            foreach (cfg.ActionGroup group in run.Tables.TbActionGroup.DataList)
            {
                if (IsGroupAvailable(run, group, runStep, ignoreRuleMax: true))
                {
                    groups.Add(group);
                }
            }

            return PickWeighted(run, groups, rng, avoidRepeat: false);
        }

        private static string PickWeighted(GameRun run, List<cfg.ActionGroup> groups, IRandomStream rng, bool avoidRepeat)
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
            foreach (cfg.ActionGroup group in groups)
            {
                weights.Add(group.Weight > 0f ? group.Weight : 1f);
            }

            return groups[rng.WeightedPickIndex(weights)].Id;
        }

        private static bool IsGroupAvailable(GameRun run, cfg.ActionGroup group, int runStep, bool ignoreRuleMax = false)
        {
            if (run == null || group == null)
            {
                return false;
            }

            if (!TimelineService.MatchesWeek(group.WeekFilter, run.WeekIndex, run.IsBossWeek))
            {
                return false;
            }

            if (!PreconditionEvaluator.IsSatisfied(run, group.Preconditions))
            {
                return false;
            }

            return ignoreRuleMax || !WouldExceedRuleMax(run, group.Id, runStep);
        }

        private static bool WouldExceedRuleMax(GameRun run, string groupId, int runStep)
        {
            foreach (cfg.ActionScheduleRule rule in run.Tables.TbActionScheduleRule.DataList)
            {
                if (!IsRuleActive(run, rule, runStep) || !ContainsId(rule.GroupIds, groupId) || rule.MaxCount <= 0)
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
                if (step >= min && step <= max && ContainsId(rule.GroupIds, run.ActionGroupSequence[i]))
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

        private static int RollCostDays(cfg.ActionGroupMember member, cfg.GameAction action, IRandomStream rng)
        {
            int fallback = action?.CostDays ?? 0;
            int min = member.MinCostDays > 0 ? member.MinCostDays : fallback;
            int max = member.MaxCostDays > 0 ? member.MaxCostDays : min;
            if (max < min)
            {
                int temp = min;
                min = max;
                max = temp;
            }

            return min == max ? min : rng.Range(min, max + 1);
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
            if (string.IsNullOrEmpty(ids))
            {
                yield break;
            }

            string[] parts = ids.Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string id = part.Trim();
                if (id.Length > 0)
                {
                    yield return id;
                }
            }
        }
    }
}
