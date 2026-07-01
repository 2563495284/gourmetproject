using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;

namespace GourmetProject.Game.Run
{
    /// <summary>
    /// 一次肉鸽运行的存档数据（可被 JsonSaveService 序列化）。
    /// 随机系统保存完整快照，读档后可从已消费的位置继续。
    /// </summary>
    [Serializable]
    public sealed class RunSaveData
    {
        public string CharacterId;
        public string SeedText;
        public RandomSnapshot RandomSnapshot;
        public int WeekIndex;
        public int Gold;
        public List<RunItemSaveData> Items = new List<RunItemSaveData>();
        public List<string> BonusDishIds = new List<string>();
        public List<string> StomachFragmentIds = new List<string>();

        /// <summary>整局累计已结算的菜品 BaseId 次数（技能「大局相同检测」）。</summary>
        public Dictionary<string, int> RunSettledCounts = new Dictionary<string, int>();

        // —— 行动轴状态（局外核心循环）——
        /// <summary>本周行动轴 id（用于读档时按配置重建节点）。</summary>
        public string CurrentTimelineId;

        /// <summary>本周行动轴长度（天）。</summary>
        public int TimelineLengthDays;

        /// <summary>当前天数游标（0..TimelineLengthDays）。</summary>
        public int CurrentDay;

        /// <summary>本周已执行行动次数。</summary>
        public int ActionStepIndex;

        /// <summary>整局累计已执行行动次数。</summary>
        public int RunActionStepIndex;

        /// <summary>本周要求分临时覆盖；小于 0 表示无覆盖。</summary>
        public int RequiredScoreOverride = -1;

        /// <summary>最近一次行动 id；用于读档后恢复奖励/商店隐藏分上下文。</summary>
        public string LastActionId;

        /// <summary>最近一次行动发生时的本周行动序号。</summary>
        public int LastActionStepIndex;

        /// <summary>最近一次行动发生时的整局行动序号。</summary>
        public int LastRunActionStepIndex;

        /// <summary>最近一次行动所属行动组 id。</summary>
        public string LastActionGroupId;

        /// <summary>最近一次行动的耗时快照。</summary>
        public int LastActionCostDays;

        /// <summary>已生成的整局行动组序列。</summary>
        public List<string> ActionGroupSequence = new List<string>();

        /// <summary>本周已结算的节点 id。</summary>
        public List<string> TriggeredNodeIds = new List<string>();

        /// <summary>不可重复事件命中记录（整局）。</summary>
        public List<string> UsedEventIds = new List<string>();

        /// <summary>不可重复行动命中记录（本周内）。</summary>
        public List<string> UsedActionIds = new List<string>();

        /// <summary>已通关 Boss id（整局，含最终胜利判定）。</summary>
        public List<string> CompletedBossIds = new List<string>();

        /// <summary>当前行动选择快照 key；同一步 UI 重开时沿用已有候选。</summary>
        public string PendingActionChoiceKey;

        public List<RunActionChoiceSaveData> PendingActionChoices = new List<RunActionChoiceSaveData>();

        /// <summary>当前商店实例 key；离开商店后清空。</summary>
        public string PendingShopKey;

        public List<ShopEntrySaveData> PendingShopStock = new List<ShopEntrySaveData>();

        /// <summary>当前待领取奖励 key；领取后清空。</summary>
        public string PendingRewardKey;

        public RewardOfferSaveData PendingRewardOffer;

        /// <summary>旧存档兼容字段：曾经只保存道具 id，读档时会迁移为 Items。</summary>
        public List<string> ItemIds = new List<string>();
    }

    [Serializable]
    public sealed class RunItemSaveData
    {
        public string ItemId;
        public int Level = 1;

        /// <summary>旧存档兼容字段：曾经的主动道具持有数量。新档每份实例单独一条，恒为 1。</summary>
        public int Count = 1;
    }

    [Serializable]
    public sealed class RunActionChoiceSaveData
    {
        public string ActionId;
        public string ActionGroupId;
        public int WeekStepIndex;
        public int RunStepIndex;
        public int CostDays;
    }

    [Serializable]
    public sealed class ShopEntrySaveData
    {
        public ShopEntryKind Kind;
        public string Id;
        public string Name;
        public string Desc;
        public int Price;
    }

    [Serializable]
    public sealed class RewardOfferSaveData
    {
        public int BaseGold;
        public List<RewardChoiceSaveData> MainChoices = new List<RewardChoiceSaveData>();
        public List<RewardChoiceSaveData> ExtraChoices = new List<RewardChoiceSaveData>();
    }

    [Serializable]
    public sealed class RewardChoiceSaveData
    {
        public cfg.RewardKind Kind;
        public string Id;
        public string Name;
        public string Description;
        public int GoldAmount;
        public bool IsFallbackGold;
    }
}
