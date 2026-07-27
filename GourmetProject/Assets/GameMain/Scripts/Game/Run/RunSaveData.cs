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

        /// <summary>蛋糕保鲜保留到下一次美食的初始层数。</summary>
        public int RetainedHappyCakeLayers;

        /// <summary>主动道具累计使用次数（单调递增）。随机类主动效果按此序号派生随机流以保证可复现。</summary>
        public int ActiveUseIndex;

        /// <summary>行动选择页剩余刷新次数。</summary>
        public int ActionRerollCount = -1;

        /// <summary>高利贷待扣债务（下一周结算时扣除）。</summary>
        public int LoanDebt;

        /// <summary>「美食分红」剩余生效局数（GoldMealBonus）。</summary>
        public int MealBonusRemaining;

        /// <summary>「分数变1」剩余生效局数（RequiredScoreToOne，非盛宴）。</summary>
        public int ScoreToOneRemaining;

        /// <summary>事件带来的目标分隐藏分偏移。</summary>
        public float EventTargetScoreHiddenOffset;

        /// <summary>事件带来的菜品奖励隐藏分偏移。</summary>
        public float EventDishHiddenOffset;

        /// <summary>事件带来的被动道具奖励隐藏分偏移。</summary>
        public float EventPassiveItemHiddenOffset;

        /// <summary>事件带来的餐桌碎片奖励隐藏分偏移。</summary>
        public float EventFragmentHiddenOffset;

        /// <summary>事件带来的金币奖励隐藏分偏移。</summary>
        public float EventGoldHiddenOffset;

        /// <summary>事件造成的商店价格百分比修正（0.25 表示 +25%）。</summary>
        public float EventShopPricePct;

        /// <summary>后续奖励候选数修正剩余次数。</summary>
        public int EventChoicePenaltyRemaining;

        /// <summary>后续奖励候选数单次变化，通常为 -1。</summary>
        public int EventChoiceCountDelta;

        /// <summary>下一场美食目标分隐藏分偏移，生成下一场美食时消费。</summary>
        public float NextFoodTargetScoreHiddenOffset;

        /// <summary>下一次美食领奖额外金币，领取基础金币时消费。</summary>
        public int NextMealRewardGold;

        /// <summary>整局累计进入 act_event 行动的次数。</summary>
        public int ActEventActionCount;

        /// <summary>事件计数器，如许愿砂锅解锁目标行动序号。</summary>
        public Dictionary<string, int> EventCounters = new Dictionary<string, int>();

        /// <summary>下次事件行动优先触发的事件 id 队列。</summary>
        public List<string> ForcedEventIds = new List<string>();

        public List<RunItemSaveData> Items = new List<RunItemSaveData>();
        public List<string> BonusDishIds = new List<string>();
        public RunRecipeBookSaveData Recipe = new RunRecipeBookSaveData();

        /// <summary>奖励获得、自动附着的餐桌碎片 id（无手动位置）。</summary>
        public List<string> TableFragmentIds = new List<string>();

        /// <summary>玩家在餐桌编辑页手动拼贴的碎片放置（id + 旋转 + 原点），用于可复现地重建胃形。</summary>
        public List<TableFragmentPlacementSaveData> FragmentPlacements = new List<TableFragmentPlacementSaveData>();

        /// <summary>餐桌碎片开包时已随机好的局部材质落点；随候选/已拼贴碎片保存。</summary>
        public List<TableFragmentMaterialRollSaveData> FragmentMaterialRolls = new List<TableFragmentMaterialRollSaveData>();

        /// <summary>玩家用「铺台小票」永久附加的格子材质覆盖（坐标 + 材质 id）；旧档缺省 → 空。</summary>
        public List<CellMaterialSaveData> CellMaterialOverrides = new List<CellMaterialSaveData>();

        /// <summary>已购买但尚未拼贴的碎片包内容（rolled 出的候选碎片 id）；拼贴或跳过后清空。</summary>
        public List<string> PendingFragmentPackIds = new List<string>();

        /// <summary>本局商店碎片包成功购买次数，用于递增定价。</summary>
        public int FragmentPackPurchaseCount;

        /// <summary>本局商店/菜谱编辑成功删除菜品次数，用于递增定价。</summary>
        public int DeleteDishCount;

        /// <summary>整局累计已结算的菜品 BaseId 次数（技能「大局相同检测」）。</summary>
        public Dictionary<string, int> RunSettledCounts = new Dictionary<string, int>();

        // —— 行动轴状态（局外核心循环）——
        /// <summary>本周行动轴 id（用于读档时按配置重建节点）。</summary>
        public string CurrentTimelineId;

        /// <summary>当前行动轴所属周；用于区分同周末节点待处理与换周后旧轴残留。</summary>
        public int CurrentTimelineWeekIndex;

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

        /// <summary>最近一次行动的来源 key；行动轴节点恢复奖励续接时使用。</summary>
        public string LastActionSourceKey;

        /// <summary>最近一次行动是否带有目标分天数覆盖。</summary>
        public bool LastActionHasTargetScoreDayOverride;

        /// <summary>最近一次行动的目标分天数覆盖值。</summary>
        public float LastActionTargetScoreDayOverride;

        /// <summary>已进入但尚未结算/提交的行动；用于读档恢复到 food/interest/event/shop 页面。</summary>
        public PendingActionExecutionSaveData PendingActionExecution;

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

        /// <summary>本周由主动道具（奖励单等）动态追加的行动轴节点；换周清空。</summary>
        public List<RuntimeTimelineNodeSaveData> RuntimeTimelineNodes = new List<RuntimeTimelineNodeSaveData>();

        /// <summary>已触发事件 id（整局，供结算统计与跨局进度）。</summary>
        public List<string> UsedEventIds = new List<string>();

        /// <summary>已通关 Boss id（整局，含最终胜利判定）。</summary>
        public List<string> CompletedBossIds = new List<string>();

        /// <summary>Boss Debuff 不放回随机的已抽取记录；抽光后会重置。</summary>
        public List<string> RolledBossDebuffIds = new List<string>();

        /// <summary>本周 Boss Debuff 主动重抽所属周；0=未重抽。</summary>
        public int BossDebuffRerollWeekIndex;

        /// <summary>本周 Boss Debuff 主动重抽序号，用于改变当前周 Boss 随机 key。</summary>
        public int BossDebuffRerollIndex;

        /// <summary>开发者控制台指定的 Boss Debuff 所属周；0=未指定。</summary>
        public int ForcedBossDebuffWeekIndex;

        /// <summary>开发者控制台指定的本周 Boss Debuff id。</summary>
        public string ForcedBossDebuffId;

        /// <summary>当前行动选择快照 key；同一步 UI 重开时沿用已有候选。</summary>
        public string PendingActionChoiceKey;

        public List<RunActionChoiceSaveData> PendingActionChoices = new List<RunActionChoiceSaveData>();

        /// <summary>当前商店实例 key；离开商店后清空。</summary>
        public string PendingShopKey;

        public List<ShopEntrySaveData> PendingShopStock = new List<ShopEntrySaveData>();

        /// <summary>当前待领取奖励 key；领取后清空。</summary>
        public string PendingRewardKey;

        public RewardOfferSaveData PendingRewardOffer;

        /// <summary>战斗胜利后待领奖期间的只读 Food 画面快照；用于读档恢复领奖背景和常驻 HUD。</summary>
        public PendingRewardBattleViewSaveData PendingRewardBattleView;

        /// <summary>通用待领奖队列（被动获得时、商店、事件等非过关奖励来源）。</summary>
        public List<GenericRewardSaveData> PendingGenericRewards = new List<GenericRewardSaveData>();

        /// <summary>通用领奖队列结束后是否要续接战斗胜利流程。</summary>
        public bool PendingGenericRewardsConfirmBattleAfterDone;
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

        /// <summary>与 DishIds 同 index 对齐的玩家永久附加风味（调味小票）；旧档缺省 → 视为无。</summary>
        public List<RunRecipeDishFlavorSaveData> DishExtraFlavors = new List<RunRecipeDishFlavorSaveData>();
    }

    [Serializable]
    public sealed class RunRecipeDishFlavorSaveData
    {
        public List<string> FlavorIds = new List<string>();

        public List<string> ExtraSkillIds = new List<string>();

        public float ScoreFlatBonus;

        public float ScoreMultiplier = 1f;
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
    public sealed class CellMaterialSaveData
    {
        public int X;
        public int Y;
        public string MaterialId;
    }

    [Serializable]
    public sealed class TableFragmentMaterialRollSaveData
    {
        public string FragmentId;
        public List<CellMaterialSaveData> Materials = new List<CellMaterialSaveData>();
    }

    [Serializable]
    public sealed class RuntimeTimelineNodeSaveData
    {
        public string Id;
        public string TimelineId;
        public int Day;
        public string ActionId;
    }

    [Serializable]
    public sealed class PendingActionExecutionSaveData
    {
        public string ActionId;
        public int StepIndex;
        public int RunStepIndex;
        public string ActionGroupId;
        public float CostDays;
        public string SourceKey;
        public bool HasTargetScoreDayOverride;
        public float TargetScoreDayOverride;
        public ActionOutcomeKind OutcomeKind;
        public string Feedback;
        public int RequiredScore;
        public string Modifier;
        public string BattleKey;
        public bool IsBoss;
        public string BossId;
        public string BossDebuffId;
        public string EventId;
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
        public int SlotIndex = -1;
        public string Id;
        public string Name;
        public string Desc;
        public int BasePrice;
        public int Price;
    }

    [Serializable]
    public sealed class RewardOfferSaveData
    {
        public int BaseGold;
        public bool BaseGoldClaimed;
        public int MainChoiceIndex = -1;
        public int ExtraChoiceIndex = -1;
        public int BonusChoiceIndex = -1;
        public bool MainChoiceSkipped;
        public bool ExtraChoiceSkipped;
        public bool BonusChoiceSkipped;
        public int MainRequiredChoiceCount = 1;
        public int ExtraRequiredChoiceCount = 1;
        public int BonusRequiredChoiceCount = 1;
        public List<int> MainChoiceIndices = new List<int>();
        public List<int> ExtraChoiceIndices = new List<int>();
        public List<int> BonusChoiceIndices = new List<int>();
        public List<RewardChoiceSaveData> MainChoices = new List<RewardChoiceSaveData>();
        public List<RewardChoiceSaveData> ExtraChoices = new List<RewardChoiceSaveData>();
        public List<RewardChoiceSaveData> BonusChoices = new List<RewardChoiceSaveData>();
        public List<RewardChoiceGroupSaveData> FixedGroups = new List<RewardChoiceGroupSaveData>();
        public RewardChoiceGroupSaveData SpecificGroup;
    }

    [Serializable]
    public sealed class RewardChoiceGroupSaveData
    {
        public string Title;
        public int RequiredChoiceCount = 1;
        public bool Skipped;
        public List<int> ClaimedIndices = new List<int>();
        public List<RewardChoiceSaveData> Choices = new List<RewardChoiceSaveData>();
    }

    [Serializable]
    public sealed class GenericRewardSaveData
    {
        public string Key;
        public string Title;
        public RewardOfferSaveData Offer;
    }

    [Serializable]
    public sealed class PendingRewardBattleViewSaveData
    {
        public int RequiredScore;
        public int RawRequiredScore;
        public string Modifier;
        public string BattleKey;
        public bool IsBoss;
        public int LastTotal;
        public List<PendingRewardBattleDishSaveData> Dishes = new List<PendingRewardBattleDishSaveData>();
    }

    [Serializable]
    public sealed class PendingRewardBattleDishSaveData
    {
        public int Id;
        public string DishId;
        public int OriginX;
        public int OriginY;
        public int Rotation;
        public int SourceSlotIndex = -1;
        public int SourceDishIndex = -1;
        public List<string> SkillIds = new List<string>();
        public List<string> FlavorIds = new List<string>();
        public int RuntimeCountAsBonus;
        public float PermanentFlatBonus;
        public float PermanentMultBonus = 1f;
        public float TemporaryBaseMultiplier = 1f;
        public float ServeMultiplier = 1f;
        public float ServeMultiplierFlatBonus;
        public bool SkillsDisabled;
        public bool ExcludedFromScore;
        public bool IsTemporary;
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
