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

        /// <summary>本局利息节点金币阈值。</summary>
        public int InterestThreshold = -1;

        /// <summary>本局利息每档金币收益。</summary>
        public int InterestGoldPer = -1;

        /// <summary>本局利息节点单次最高收益基础值。</summary>
        public int InterestCap = -1;

        /// <summary>「食物调整」行动外基础次数快照；被动道具加成由持有道具在运行时叠加。</summary>
        public int FoodAdjustCount = -1;

        /// <summary>主动道具累计使用次数（单调递增）。随机类主动效果按此序号派生随机流以保证可复现。</summary>
        public int ActiveUseIndex;

        /// <summary>高利贷待扣债务（下一周结算时扣除）。</summary>
        public int LoanDebt;

        /// <summary>「美食分红」剩余生效局数（GoldMealBonus）。</summary>
        public int MealBonusRemaining;

        /// <summary>「分数变1」剩余生效局数（RequiredScoreToOne，非盛宴）。</summary>
        public int ScoreToOneRemaining;

        public List<RunItemSaveData> Items = new List<RunItemSaveData>();
        public List<string> BonusDishIds = new List<string>();
        public List<RunRecipeBookSaveData> RecipeBooks = new List<RunRecipeBookSaveData>();

        /// <summary>奖励获得、自动附着的餐桌碎片 id（无手动位置）。</summary>
        public List<string> TableFragmentIds = new List<string>();

        /// <summary>玩家在餐桌编辑页手动拼贴的碎片放置（id + 旋转 + 原点），用于可复现地重建胃形。</summary>
        public List<TableFragmentPlacementSaveData> FragmentPlacements = new List<TableFragmentPlacementSaveData>();

        /// <summary>已购买但尚未拼贴的碎片包内容（rolled 出的候选碎片 id）；拼贴或跳过后清空。</summary>
        public List<string> PendingFragmentPackIds = new List<string>();

        /// <summary>整局累计已结算的菜品 BaseId 次数（技能「大局相同检测」）。</summary>
        public Dictionary<string, int> RunSettledCounts = new Dictionary<string, int>();

        // —— 行动轴状态（局外核心循环）——
        /// <summary>本周行动轴 id（用于读档时按配置重建节点）。</summary>
        public string CurrentTimelineId;

        /// <summary>本周行动轴长度（天，0.1 粒度）。</summary>
        public float TimelineLengthDays;

        /// <summary>当前天数游标（0..TimelineLengthDays，0.1 粒度）。旧档为整数天，JSON 数字可直接读入。</summary>
        public float CurrentDay;

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

        /// <summary>最近一次行动的耗时快照（天，0.1 粒度）。</summary>
        public float LastActionCostDays;

        /// <summary>已生成的整局行动组序列。</summary>
        public List<string> ActionGroupSequence = new List<string>();

        /// <summary>本周大组计划（周开始预排；空=无计划，读档后按需重建）。</summary>
        public List<string> ActionWeekPlan = new List<string>();

        /// <summary>本周计划所属周（0=未构建）。</summary>
        public int ActionWeekPlanWeek;

        /// <summary>本周计划对应的整局行动步起点。</summary>
        public int ActionWeekPlanStartRunStep;

        /// <summary>本周已结算的节点 id。</summary>
        public List<string> TriggeredNodeIds = new List<string>();

        /// <summary>已触发事件 id（整局，供结算统计与跨局进度）。</summary>
        public List<string> UsedEventIds = new List<string>();

        /// <summary>已通关 Boss id（整局，含最终胜利判定）。</summary>
        public List<string> CompletedBossIds = new List<string>();

        /// <summary>Boss Debuff 不放回随机的已抽取记录；抽光后会重置。</summary>
        public List<string> RolledBossDebuffIds = new List<string>();

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

        /// <summary>被动道具模型的 per-instance 状态（如 LuckyEventGuarantee 计数）；旧档缺省空串。</summary>
        public string StateJson = string.Empty;
    }

    [Serializable]
    public sealed class RunRecipeBookSaveData
    {
        public List<string> DishIds = new List<string>();
    }

    [Serializable]
    public sealed class TableFragmentPlacementSaveData
    {
        public string FragmentId;
        public int Rotation;
        public int OriginX;
        public int OriginY;
    }

    [Serializable]
    public sealed class RunActionChoiceSaveData
    {
        public string ActionId;
        public string ActionGroupId;
        public int WeekStepIndex;
        public int RunStepIndex;
        public float CostDays;
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
        public bool BaseGoldClaimed;
        public int MainChoiceIndex = -1;
        public int ExtraChoiceIndex = -1;
        public bool MainChoiceSkipped;
        public bool ExtraChoiceSkipped;
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
