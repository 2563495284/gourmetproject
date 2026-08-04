using System.Collections.Generic;
using GourmetProject.Game.Run;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Meta
{
    /// <summary>跨局进度累计与游戏结束解锁评估。</summary>
    public static class MetaProgressService
    {
        public static MetaProgressUpdate EvaluateRunEnd(GameRun run, bool won, int lastTotal, int lastTarget, MetaProgressSaveData progress = null)
        {
            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            return EvaluateRunEnd(tables, run, won, lastTotal, lastTarget, progress);
        }

        public static MetaProgressUpdate EvaluateRunEnd(
            cfg.Tables tables,
            GameRun run,
            bool won,
            int lastTotal,
            int lastTarget,
            MetaProgressSaveData progress)
        {
            RunStatistics statistics = RunStatisticsService.Build(run, won, lastTotal, lastTarget);
            MetaProgressSaveData next = Clone(progress);
            ApplyStatistics(next, statistics);

            var update = new MetaProgressUpdate
            {
                Statistics = statistics,
                Progress = next,
            };

            if (tables == null || run == null)
            {
                return update;
            }

            MetaProgressSaveData conditionSnapshot = Clone(next);
            var context = new UnlockConditionEvaluator.Context(run, statistics, conditionSnapshot);
            Dictionary<string, List<cfg.UnlockCondition>> conditionsByRule = BuildConditionsByRule(tables);
            List<cfg.UnlockRule> rules = SortedRules(tables);
            foreach (cfg.UnlockRule rule in rules)
            {
                if (rule == null ||
                    !rule.Enabled ||
                    string.IsNullOrEmpty(rule.TargetId) ||
                    next.IsTargetUnlocked(rule.TargetType, rule.TargetId) ||
                    !conditionsByRule.TryGetValue(rule.Id, out List<cfg.UnlockCondition> conditions) ||
                    !UnlockConditionEvaluator.IsSatisfied(conditions, context))
                {
                    continue;
                }

                if (next.AddUnlockedTarget(rule.TargetType, rule.TargetId))
                {
                    update.NewUnlocks.Add(new UnlockEntry(
                        rule.Id,
                        rule.TargetType,
                        rule.TargetId,
                        ResolveTitle(tables, rule),
                        ResolveKind(tables, rule),
                        ResolveDescription(tables, rule)));
                }
            }

            return update;
        }

        public static bool IsItemUnlockedForPool(ItemDefinition item, MetaProgressSaveData progress)
        {
            return IsItemUnlockedForPool(GameApp.Config.Tables, item, progress);
        }

        public static bool IsItemUnlockedForPool(cfg.Tables tables, ItemDefinition item, MetaProgressSaveData progress)
        {
            return IsTargetAvailable(tables, cfg.UnlockTargetType.Item, item?.Id, progress);
        }

        public static bool IsTargetAvailable(cfg.Tables tables, cfg.UnlockTargetType targetType, string targetId, MetaProgressSaveData progress)
        {
            if (tables == null || string.IsNullOrEmpty(targetId))
            {
                return false;
            }

            bool hasRule = false;
            foreach (cfg.UnlockRule rule in tables.TbUnlockRule.DataList)
            {
                if (rule.Enabled && rule.TargetType == targetType && rule.TargetId == targetId)
                {
                    hasRule = true;
                    break;
                }
            }

            if (!hasRule)
            {
                return true;
            }

            progress ??= MetaProgressPersistence.Load();
            progress.Normalize();
            return progress.IsTargetUnlocked(targetType, targetId);
        }

        private static void ApplyStatistics(MetaProgressSaveData progress, RunStatistics statistics)
        {
            progress.CompletedRunCount++;
            if (statistics.Won)
            {
                progress.WonRunCount++;
            }
            else
            {
                progress.LostRunCount++;
            }

            if (statistics.WeekIndex > progress.HighestWeekIndex)
            {
                progress.HighestWeekIndex = statistics.WeekIndex;
            }

            if (statistics.CurrentDay > progress.HighestCurrentDay)
            {
                progress.HighestCurrentDay = statistics.CurrentDay;
            }

            if (statistics.RunActionStepIndex > progress.HighestRunActionStepIndex)
            {
                progress.HighestRunActionStepIndex = statistics.RunActionStepIndex;
            }

            progress.TotalBossDefeats += statistics.CompletedBossIds.Count;
            foreach (string bossId in statistics.CompletedBossIds)
            {
                AddUnique(progress.DefeatedBossIds, bossId);
            }

            foreach (string eventId in statistics.UsedEventIds)
            {
                AddUnique(progress.SeenEventIds, eventId);
            }
        }

        private static MetaProgressSaveData Clone(MetaProgressSaveData source)
        {
            var clone = new MetaProgressSaveData();
            if (source == null)
            {
                return clone;
            }

            source.Normalize();
            clone.Version = source.Version;
            clone.UnlockedTargetKeys = new List<string>(source.UnlockedTargetKeys);
            clone.UnlockedItemIds = new List<string>(source.UnlockedItemIds);
            clone.CompletedRunCount = source.CompletedRunCount;
            clone.WonRunCount = source.WonRunCount;
            clone.LostRunCount = source.LostRunCount;
            clone.HighestWeekIndex = source.HighestWeekIndex;
            clone.HighestCurrentDay = source.HighestCurrentDay;
            clone.HighestRunActionStepIndex = source.HighestRunActionStepIndex;
            clone.TotalBossDefeats = source.TotalBossDefeats;
            clone.DefeatedBossIds = new List<string>(source.DefeatedBossIds);
            clone.SeenEventIds = new List<string>(source.SeenEventIds);
            return clone;
        }

        private static bool AddUnique(List<string> list, string value)
        {
            if (string.IsNullOrEmpty(value) || list.Contains(value))
            {
                return false;
            }

            list.Add(value);
            return true;
        }

        private static Dictionary<string, List<cfg.UnlockCondition>> BuildConditionsByRule(cfg.Tables tables)
        {
            var result = new Dictionary<string, List<cfg.UnlockCondition>>();
            foreach (cfg.UnlockCondition condition in tables.TbUnlockCondition.DataList)
            {
                if (condition == null || string.IsNullOrEmpty(condition.RuleId))
                {
                    continue;
                }

                if (!result.TryGetValue(condition.RuleId, out List<cfg.UnlockCondition> list))
                {
                    list = new List<cfg.UnlockCondition>();
                    result[condition.RuleId] = list;
                }

                list.Add(condition);
            }

            return result;
        }

        private static List<cfg.UnlockRule> SortedRules(cfg.Tables tables)
        {
            var rules = new List<cfg.UnlockRule>(tables.TbUnlockRule.DataList);
            rules.Sort((a, b) =>
            {
                int order = a.SortOrder.CompareTo(b.SortOrder);
                return order != 0 ? order : string.CompareOrdinal(a.Id, b.Id);
            });

            return rules;
        }

        private static string ResolveTitle(cfg.Tables tables, cfg.UnlockRule rule)
        {
            if (!string.IsNullOrEmpty(rule.Title))
            {
                return rule.Title;
            }

            ItemDefinition item = rule.TargetType == cfg.UnlockTargetType.Item ? ItemDefinition.Get(tables, rule.TargetId) : null;
            return item != null ? item.Name : rule.TargetId;
        }

        private static string ResolveDescription(cfg.Tables tables, cfg.UnlockRule rule)
        {
            if (!string.IsNullOrEmpty(rule.Desc))
            {
                return rule.Desc;
            }

            ItemDefinition item = rule.TargetType == cfg.UnlockTargetType.Item ? ItemDefinition.Get(tables, rule.TargetId) : null;
            return item != null ? item.Desc : string.Empty;
        }

        private static string ResolveKind(cfg.Tables tables, cfg.UnlockRule rule)
        {
            if (rule.TargetType == cfg.UnlockTargetType.Item)
            {
                ItemDefinition item = ItemDefinition.Get(tables, rule.TargetId);
                if (item != null)
                {
                    return item.Kind == cfg.ItemKind.Passive ? "装饰品" : "消耗品";
                }
            }

            switch (rule.TargetType)
            {
                case cfg.UnlockTargetType.Dish:
                    return "食物";
                case cfg.UnlockTargetType.Boss:
                    return "星级评鉴";
                case cfg.UnlockTargetType.Character:
                    return "经营方向";
                case cfg.UnlockTargetType.Mode:
                    return "模式";
                default:
                    return "内容";
            }
        }
    }
}
