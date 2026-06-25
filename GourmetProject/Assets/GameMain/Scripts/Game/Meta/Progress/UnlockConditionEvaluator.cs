using System.Collections.Generic;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>类型化解锁条件求值器：同一 groupId 内 AND，不同 groupId 之间 OR。</summary>
    public static class UnlockConditionEvaluator
    {
        public sealed class Context
        {
            public Context(GameRun run, RunStatistics statistics, MetaProgressSaveData progress)
            {
                Run = run;
                Statistics = statistics;
                Progress = progress ?? new MetaProgressSaveData();
                Progress.Normalize();
            }

            public GameRun Run { get; }
            public RunStatistics Statistics { get; }
            public MetaProgressSaveData Progress { get; }
        }

        public static bool IsSatisfied(IReadOnlyList<cfg.UnlockCondition> conditions, Context context)
        {
            if (conditions == null || conditions.Count == 0 || context == null)
            {
                return false;
            }

            var groups = new Dictionary<string, List<cfg.UnlockCondition>>();
            foreach (cfg.UnlockCondition condition in conditions)
            {
                if (condition == null)
                {
                    continue;
                }

                string groupId = string.IsNullOrEmpty(condition.GroupId) ? "default" : condition.GroupId;
                if (!groups.TryGetValue(groupId, out List<cfg.UnlockCondition> group))
                {
                    group = new List<cfg.UnlockCondition>();
                    groups[groupId] = group;
                }

                group.Add(condition);
            }

            foreach (List<cfg.UnlockCondition> group in groups.Values)
            {
                if (IsGroupSatisfied(group, context))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsGroupSatisfied(IReadOnlyList<cfg.UnlockCondition> group, Context context)
        {
            if (group == null || group.Count == 0)
            {
                return false;
            }

            foreach (cfg.UnlockCondition condition in group)
            {
                if (!IsConditionSatisfied(condition, context))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsConditionSatisfied(cfg.UnlockCondition condition, Context context)
        {
            RunStatistics statistics = context.Statistics;
            MetaProgressSaveData progress = context.Progress;
            switch (condition.Type)
            {
                case cfg.UnlockConditionType.Always:
                    return true;
                case cfg.UnlockConditionType.RunWon:
                    return statistics.Won;
                case cfg.UnlockConditionType.RunLost:
                    return !statistics.Won;
                case cfg.UnlockConditionType.MinWeek:
                    return statistics.WeekIndex >= condition.IntParam;
                case cfg.UnlockConditionType.MinDay:
                    return statistics.CurrentDay >= condition.IntParam;
                case cfg.UnlockConditionType.MinRunActions:
                    return statistics.RunActionStepIndex >= condition.IntParam;
                case cfg.UnlockConditionType.CompletedBoss:
                    return Contains(statistics.CompletedBossIds, condition.StringParam);
                case cfg.UnlockConditionType.MinCompletedBosses:
                    return statistics.CompletedBossIds.Count >= condition.IntParam;
                case cfg.UnlockConditionType.MinCompletedRuns:
                    return progress.CompletedRunCount >= condition.IntParam;
                case cfg.UnlockConditionType.MinWonRuns:
                    return progress.WonRunCount >= condition.IntParam;
                case cfg.UnlockConditionType.MinLostRuns:
                    return progress.LostRunCount >= condition.IntParam;
                case cfg.UnlockConditionType.UnlockedTarget:
                    return progress.IsTargetUnlocked(condition.TargetTypeParam, condition.TargetIdParam);
                case cfg.UnlockConditionType.DefeatedBoss:
                    return Contains(progress.DefeatedBossIds, condition.StringParam);
                default:
                    return false;
            }
        }

        private static bool Contains(IReadOnlyList<string> values, string value)
        {
            if (values == null || string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == value)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
