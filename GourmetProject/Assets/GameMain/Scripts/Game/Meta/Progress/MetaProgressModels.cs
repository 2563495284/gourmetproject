using System;
using System.Collections.Generic;

namespace GourmetProject.Game.Meta
{
    /// <summary>一次结算页需要展示的单局统计快照。</summary>
    public sealed class RunStatistics
    {
        public bool Won { get; set; }
        public int WeekIndex { get; set; }
        public int CurrentDay { get; set; }
        public bool IsEndless { get; set; }
        public int LastTotal { get; set; }
        public int LastTarget { get; set; }
        public int Gold { get; set; }
        public int OwnedItemCount { get; set; }
        public int BonusDishCount { get; set; }
        public int StomachFragmentCount { get; set; }
        public int TriggeredEventCount { get; set; }
        public int RunActionStepIndex { get; set; }
        public List<string> CompletedBossIds { get; set; } = new List<string>();
        public List<string> UsedEventIds { get; set; } = new List<string>();
    }

    /// <summary>结算时新解锁内容的归一化展示项。</summary>
    public sealed class UnlockEntry
    {
        public UnlockEntry(string id, string name, string kind, string description)
            : this(string.Empty, cfg.UnlockTargetType.Item, id, name, kind, description)
        {
        }

        public UnlockEntry(string ruleId, cfg.UnlockTargetType targetType, string targetId, string name, string kind, string description)
        {
            RuleId = ruleId ?? string.Empty;
            TargetType = targetType;
            TargetId = targetId ?? string.Empty;
            Id = TargetId;
            Name = string.IsNullOrEmpty(name) ? Id : name;
            Kind = kind ?? string.Empty;
            Description = description ?? string.Empty;
        }

        public string RuleId { get; }
        public cfg.UnlockTargetType TargetType { get; }
        public string TargetId { get; }
        public string Id { get; }
        public string Name { get; }
        public string Kind { get; }
        public string Description { get; }
    }

    /// <summary>一次游戏结束对跨局进度的增量更新。</summary>
    public sealed class MetaProgressUpdate
    {
        public RunStatistics Statistics { get; set; }
        public MetaProgressSaveData Progress { get; set; }
        public List<UnlockEntry> NewUnlocks { get; set; } = new List<UnlockEntry>();

        public bool HasNewUnlocks => NewUnlocks != null && NewUnlocks.Count > 0;
    }

    /// <summary>跨局 Meta 进度档：独立于单局 run 存档，失败删档不会清掉这里。</summary>
    [Serializable]
    public sealed class MetaProgressSaveData
    {
        public int Version = 1;

        /// <summary>跨局已解锁目标，格式为 "TargetType:targetId"。</summary>
        public List<string> UnlockedTargetKeys = new List<string>();

        /// <summary>旧字段兼容：早期只支持装饰品和消耗品解锁。</summary>
        public List<string> UnlockedItemIds = new List<string>();
        public int CompletedRunCount;
        public int WonRunCount;
        public int LostRunCount;
        public int HighestWeekIndex;
        public int HighestCurrentDay;
        public int HighestRunActionStepIndex;
        public int TotalBossDefeats;
        /// <summary>
        /// 旧版开场漫画完成标记，仅用于迁移到应用级 PlayerPrefs；新代码不再写入此字段。
        /// </summary>
        public int OpeningComicCompletedVersion;
        public List<string> DefeatedBossIds = new List<string>();
        public List<string> SeenEventIds = new List<string>();

        public bool IsItemUnlocked(string itemId)
        {
            return IsTargetUnlocked(cfg.UnlockTargetType.Item, itemId);
        }

        public bool AddUnlockedItem(string itemId)
        {
            AddUnique(UnlockedItemIds, itemId);
            return AddUnlockedTarget(cfg.UnlockTargetType.Item, itemId);
        }

        public bool IsTargetUnlocked(cfg.UnlockTargetType targetType, string targetId)
        {
            return !string.IsNullOrEmpty(targetId) && UnlockedTargetKeys.Contains(TargetKey(targetType, targetId));
        }

        public bool AddUnlockedTarget(cfg.UnlockTargetType targetType, string targetId)
        {
            if (string.IsNullOrEmpty(targetId))
            {
                return false;
            }

            if (targetType == cfg.UnlockTargetType.Item)
            {
                AddUnique(UnlockedItemIds, targetId);
            }

            return AddUnique(UnlockedTargetKeys, TargetKey(targetType, targetId));
        }

        public static string TargetKey(cfg.UnlockTargetType targetType, string targetId)
        {
            return $"{targetType}:{targetId ?? string.Empty}";
        }

        public void Normalize()
        {
            UnlockedTargetKeys = Dedupe(UnlockedTargetKeys);
            UnlockedItemIds = Dedupe(UnlockedItemIds);
            foreach (string itemId in UnlockedItemIds)
            {
                AddUnique(UnlockedTargetKeys, TargetKey(cfg.UnlockTargetType.Item, itemId));
            }

            DefeatedBossIds = Dedupe(DefeatedBossIds);
            SeenEventIds = Dedupe(SeenEventIds);
            if (Version <= 0)
            {
                Version = 1;
            }
        }

        private static bool AddUnique(List<string> list, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (list.Contains(value))
            {
                return false;
            }

            list.Add(value);
            return true;
        }

        private static List<string> Dedupe(List<string> source)
        {
            var result = new List<string>();
            if (source == null)
            {
                return result;
            }

            foreach (string value in source)
            {
                AddUnique(result, value);
            }

            return result;
        }
    }
}
