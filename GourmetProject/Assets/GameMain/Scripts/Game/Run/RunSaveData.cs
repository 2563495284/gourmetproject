using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Save;

namespace GourmetProject.Game.Run
{
    /// <summary>
    /// 一次肉鸽运行的存档数据（可被 JsonSaveService 序列化）。
    /// 随机系统保存完整快照，读档后可从已消费的位置继续。
    /// </summary>
    [Serializable]
    public sealed class RunSaveData
    {
        /// <summary>
        /// 日常行动随机规则版本。旧存档缺少该字段时为 0，由 <see cref="RunPersistence"/> 拒绝继续。
        /// </summary>
        public int ActionRandomRuleVersion;

        /// <summary>匿名统计使用的单局稳定 GUID；旧档读取时自动补齐。</summary>
        public string RunId;

        public string CharacterId;
        public string SeedText;
        /// <summary>该单局是否为首次且唯一一次的核心教程局。</summary>
        public bool IsTutorialRun;
        public RandomSnapshot RandomSnapshot;
        public int WeekIndex;
        public int Gold;

        /// <summary>本局爱心上限。</summary>
        public int HeartCapacity;

        /// <summary>本局当前剩余爱心。</summary>
        public int HeartsRemaining;

        /// <summary>已经失去红心、等待播放或恢复的碎心演出。</summary>
        public PendingHeartBreakSaveData PendingHeartBreak;

        /// <summary>六星评鉴进度是否已经初始化；旧存档缺少该字段时执行一次迁移。</summary>
        public bool StarProgressInitialized;

        /// <summary>本局已经获得的星级评鉴星数，范围 0-6。</summary>
        public int RatingStarsEarned;

        /// <summary>已经发放过星星的 BattleKey；用于保证结算回调幂等。</summary>
        public List<string> AwardedRatingStarBattleKeys = new List<string>();

        /// <summary>已经发星、等待播放或恢复的获星演出。</summary>
        public PendingStarAwardSaveData PendingStarAward;

        /// <summary>本局利息节点金币阈值。</summary>
        public int InterestThreshold = -1;

        /// <summary>本局利息每档金币收益。</summary>
        public int InterestGoldPer = -1;

        /// <summary>本局利息节点单次最高收益基础值。</summary>
        public int InterestCap = -1;

        /// <summary>蛋糕保鲜保留到下一场经营挑战的初始层数。</summary>
        public int RetainedHappyCakeLayers;

        /// <summary>消耗品累计使用次数（单调递增）。随机类主动效果按此序号派生随机流以保证可复现。</summary>
        public int ActiveUseIndex;

        /// <summary>剩余半日券层数；每次完成普通行动消费一层。</summary>
        public int NextDailyActionHalfCostStacks;

        /// <summary>旧实验版本字段；读取时迁移到 NextBusinessRewardDoubleStacks，之后不再写入。</summary>
        public int NextBusinessSpecificRewardDoubleStacks;

        /// <summary>待生效的营业奖励翻倍层数；每场符合条件的日常或火热营业消费一层。</summary>
        public int NextBusinessRewardDoubleStacks;

        /// <summary>最近一次食物多选一记录的有效流派 ID（0/1/2）；旧档或无有效流派为空。</summary>
        public string LastDishChoiceArchetypeId;

        /// <summary>当前流派连续未出现目标倾向食物的食物多选一次数。</summary>
        public int DishChoiceArchetypeMissStreak;

        /// <summary>星级评鉴装饰品流派保底状态所属周。</summary>
        public int BossPassiveArchetypePityWeekIndex;

        /// <summary>本周已生成的星级评鉴装饰品候选次数。</summary>
        public int BossPassiveArchetypeRewardCount;

        /// <summary>本周第一次候选未出现流派装饰品，第二次需要执行保底。</summary>
        public bool BossPassiveArchetypePityArmed;

        /// <summary>行动选择页剩余刷新次数。</summary>
        public int ActionRerollCount = -1;

        /// <summary>高利贷待扣债务（下一周结算时扣除）。</summary>
        public int LoanDebt;

        /// <summary>「食物分红」剩余生效局数（GoldMealBonus）。</summary>
        public int MealBonusRemaining;

        /// <summary>「分数变1」剩余生效局数（RequiredScoreToOne，非星级评鉴）。</summary>
        public int ScoreToOneRemaining;

        /// <summary>事件带来的目标美味值隐藏分偏移。</summary>
        public float EventTargetScoreHiddenOffset;

        /// <summary>事件带来的食物奖励隐藏分偏移。</summary>
        public float EventDishHiddenOffset;

        /// <summary>事件带来的装饰品运气偏移。</summary>
        public float EventItemLuckOffset;

        /// <summary>事件带来的餐桌格奖励隐藏分偏移。</summary>
        public float EventFragmentHiddenOffset;

        /// <summary>事件带来的金币奖励隐藏分偏移。</summary>
        public float EventGoldHiddenOffset;

        /// <summary>事件造成的商店价格百分比修正（0.25 表示 +25%）。</summary>
        public float EventShopPricePct;

        /// <summary>后续奖励候选数修正剩余次数。</summary>
        public int EventChoicePenaltyRemaining;

        /// <summary>后续奖励候选数单次变化，通常为 -1。</summary>
        public int EventChoiceCountDelta;

        /// <summary>下一场经营挑战目标美味值隐藏分偏移，生成下一场经营挑战时消费。</summary>
        public float NextFoodTargetScoreHiddenOffset;

        /// <summary>下一场经营挑战领奖额外金币，领取基础金币时消费。</summary>
        public int NextMealRewardGold;

        /// <summary>下一次营业基础金币倍率；旧存档缺失时为 1。</summary>
        public float NextBusinessGoldMultiplier = 1f;

        /// <summary>后续逐场营业基础金币倍率；索引 0 表示下一场。</summary>
        public List<float> NextBusinessGoldMultipliers = new List<float>();

        /// <summary>本周后续营业基础金币倍率；旧存档缺失时为 1。</summary>
        public float CurrentWeekBusinessGoldMultiplier = 1f;

        /// <summary>本周营业倍率所属周；与当前周不一致时倍率视为 1。</summary>
        public int CurrentWeekBusinessGoldWeek;

        /// <summary>后续星级评鉴目标美味值永久百分比修正。</summary>
        public float BossTargetScorePct;

        /// <summary>后续星级评鉴基础金币永久百分比修正。</summary>
        public float BossBaseGoldPct;

        /// <summary>整局累计进入 act_event 行动的次数。</summary>
        public int ActEventActionCount;

        /// <summary>事件计数器，如许愿砂锅解锁目标行动序号。</summary>
        public Dictionary<string, int> EventCounters = new Dictionary<string, int>();

        /// <summary>下次事件行动优先触发的事件 id 队列。</summary>
        public List<string> ForcedEventIds = new List<string>();

        public List<RunItemSaveData> Items = new List<RunItemSaveData>();
        public List<string> BonusDishIds = new List<string>();
        public RunRecipeBookSaveData Recipe = new RunRecipeBookSaveData();

        /// <summary>奖励获得、自动附着的餐桌格 id（无手动位置）。</summary>
        public List<string> TableFragmentIds = new List<string>();

        /// <summary>玩家在餐桌编辑页手动拼贴的碎片放置（id + 旋转 + 原点），用于可复现地重建胃形。</summary>
        public List<TableFragmentPlacementSaveData> FragmentPlacements = new List<TableFragmentPlacementSaveData>();

        /// <summary>已购买但尚未拼贴的碎片包内容（rolled 出的候选碎片 id）；拼贴或跳过后清空。</summary>
        public List<string> PendingFragmentPackIds = new List<string>();

        /// <summary>与 PendingFragmentPackIds 同下标的顺时针旋转次数（0..3）；旧档缺省 → 0。</summary>
        public List<int> PendingFragmentPackRotations = new List<int>();

        /// <summary>本局商店碎片包成功购买次数，用于递增定价。</summary>
        public int FragmentPackPurchaseCount;

        /// <summary>本局商店/食谱编辑成功删除食物次数，用于递增定价。</summary>
        public int DeleteDishCount;

        /// <summary>当前这次商店已删除食物次数；新商店重置，商店内读档保留。</summary>
        public int CurrentShopDeleteDishCount;

        /// <summary>当前这次商店已成功购买碎片包次数；新商店重置，商店内读档保留。</summary>
        public int CurrentShopFragmentPackPurchaseCount;

        /// <summary>整局累计已结算的食物 BaseId 次数（技能「大局相同检测」）。</summary>
        public Dictionary<string, int> RunSettledCounts = new Dictionary<string, int>();

        /// <summary>已经完成营业结算的 BattleKey；防止结算回调或读档续接重复发放/扣次数。</summary>
        public List<string> SettledFoodBattleKeys = new List<string>();

        // —— 时间轴状态（局外核心循环）——
        /// <summary>本周时间轴 id（用于读档时按配置重建节点）。</summary>
        public string CurrentTimelineId;

        /// <summary>当前时间轴所属周；用于区分同周末节点待处理与换周后旧轴残留。</summary>
        public int CurrentTimelineWeekIndex;

        /// <summary>本周时间轴长度（天，0.1 粒度）。</summary>
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

        /// <summary>最近一次行动的来源 key；时间轴节点恢复奖励续接时使用。</summary>
        public string LastActionSourceKey;

        /// <summary>最近一次行动是否带有目标美味值天数覆盖。</summary>
        public bool LastActionHasTargetScoreDayOverride;

        /// <summary>最近一次行动的目标美味值天数覆盖值。</summary>
        public float LastActionTargetScoreDayOverride;

        public bool LastActionHalfDayBuffApplied;

        public bool LastActionIsExtraTimelineExecution;

        public float LastActionTimelineStopChance;

        public int LastActionNodeRepeatIndex = 1;

        public int LastActionNodeRepeatTotal = 1;

        /// <summary>已进入但尚未结算/提交的行动；用于读档恢复到 food/interest/event/shop 页面。</summary>
        public PendingActionExecutionSaveData PendingActionExecution;

        /// <summary>当前周日常行动随机计数、行动数洗牌袋与组序号。</summary>
        public ActionRandomStateSaveData ActionRandomState = new ActionRandomStateSaveData();

        /// <summary>本周已结算的节点 id。</summary>
        public List<string> TriggeredNodeIds = new List<string>();

        /// <summary>本周由消耗品（奖励单等）动态追加的时间轴节点；换周清空。</summary>
        public List<RuntimeTimelineNodeSaveData> RuntimeTimelineNodes = new List<RuntimeTimelineNodeSaveData>();

        /// <summary>本周动态时间轴节点的单调递增序号；删除节点后不回退，避免 id 重复。</summary>
        public int RuntimeTimelineNodeSerial;

        /// <summary>已触发事件 id（整局，供结算统计与跨局进度）。</summary>
        public List<string> UsedEventIds = new List<string>();

        /// <summary>已通关 Boss id（整局，含最终胜利判定）。</summary>
        public List<string> CompletedBossIds = new List<string>();

        /// <summary>Boss Debuff 不放回随机的已抽取记录；抽光后会重置。</summary>
        public List<string> RolledBossDebuffIds = new List<string>();

        /// <summary>当前时间轴 Boss 节点已展示并锁定的 Debuff；进战必须复用预览结果。</summary>
        public Dictionary<string, string> LockedBossDebuffIdsByNode = new Dictionary<string, string>();

        /// <summary>本周星级评鉴 Debuff 主动重抽所属周；0=未重抽。</summary>
        public int BossDebuffRerollWeekIndex;

        /// <summary>本周星级评鉴 Debuff 主动重抽序号，用于改变当前周 星级评鉴随机 key。</summary>
        public int BossDebuffRerollIndex;

        /// <summary>当前重掷序号只作用于该 星级评鉴节点。</summary>
        public string BossDebuffRerollNodeId;

        /// <summary>本次重掷需从候选池排除的 Debuff id（即重掷前展示的当前 Debuff）。</summary>
        public string BossDebuffRerollExcludedId;

        /// <summary>商店/事件结束后按 FIFO 额外执行的节点 id。</summary>
        public List<string> PendingExtraTimelineNodeIds = new List<string>();

        /// <summary>开发者控制台指定的 Boss Debuff 所属周；0=未指定。</summary>
        public int ForcedBossDebuffWeekIndex;

        /// <summary>开发者控制台指定的本周星级评鉴 Debuff id。</summary>
        public string ForcedBossDebuffId;

        /// <summary>当前行动选择快照 key；同一步 UI 重开时沿用已有候选。</summary>
        public string PendingActionChoiceKey;

        /// <summary>同一行动选择批次的重抽版本，首次为 0。</summary>
        public int PendingActionChoiceRevision;

        public List<RunActionChoiceSaveData> PendingActionChoices = new List<RunActionChoiceSaveData>();

        /// <summary>当前商店实例 key；离开商店后清空。</summary>
        public string PendingShopKey;

        public List<ShopEntrySaveData> PendingShopStock = new List<ShopEntrySaveData>();

        /// <summary>当前待领取奖励 key；领取后清空。</summary>
        public string PendingRewardKey;

        public RewardOfferSaveData PendingRewardOffer;

        /// <summary>经营挑战胜利后待领奖期间的只读 Food 画面快照；用于读档恢复领奖背景和常驻 HUD。</summary>
        public PendingRewardBattleViewSaveData PendingRewardBattleView;

        /// <summary>通用待领奖队列（被动获得时、商店、事件等非过关奖励来源）。</summary>
        public List<GenericRewardSaveData> PendingGenericRewards = new List<GenericRewardSaveData>();

        /// <summary>通用领奖队列结束后是否要续接经营挑战胜利流程。</summary>
        public bool PendingGenericRewardsConfirmBattleAfterDone;

        /// <summary>通用领奖队列结束后的续接目标；旧存档仍由上面的布尔字段恢复 Battle。</summary>
        public PendingGenericRewardContinuationKind PendingGenericRewardContinuation;
    }

    [Serializable]
    public sealed class RunItemSaveData
    {
        public string ItemId;
        public int Level = 1;

        /// <summary>旧存档兼容字段：曾经的消耗品持有数量。新档每份实例单独一条，恒为 1。</summary>
        public int Count = 1;

        /// <summary>装饰品模型的 per-instance 状态（如 LuckyEventGuarantee 计数）；旧档缺省空串。</summary>
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
        public BigNumberSaveData ScoreFlatBonusBig;

        public float ScoreMultiplier = 1f;
        public BigNumberSaveData ScoreMultiplierBig;
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
    public sealed class RuntimeTimelineNodeSaveData
    {
        public string Id;
        public string TimelineId;
        public int Day;
        public string ActionId;
        public string SourceItemId;
        public bool WeekEndAnchored;
    }

    [Serializable]
    public enum SlotExecutionStage
    {
        Ready = 0,
        EmptyResult = 1,
        AwaitingReward = 2,
    }

    [Serializable]
    public enum PendingGenericRewardContinuationKind
    {
        None = 0,
        Battle = 1,
        Slot = 2,
        Event = 3,
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
        public bool HalfDayBuffApplied;
        public bool IsExtraTimelineExecution;
        public float TimelineStopChance;
        public int NodeRepeatIndex = 1;
        public int NodeRepeatTotal = 1;
        public ActionOutcomeKind OutcomeKind;
        public string Feedback;
        public int RequiredScore;
        public string Modifier;
        public string BattleKey;
        public bool IsBoss;
        public string BossId;
        public string BossDebuffId;
        public string EventId;
        /// <summary>本次根事件进入金币是否已经发放；旧存档缺省 false，恢复时补发一次。</summary>
        public bool EventEntryGoldGranted;
        public string SlotEventId;
        public int SlotSpinsUsed;
        public SlotExecutionStage SlotStage;
        public string SlotRewardKey;
        /// <summary>本次抽奖机行动是否已经出现过餐桌格奖励。</summary>
        public bool SlotFragmentRewardGranted;
    }

    [Serializable]
    public sealed class RunActionChoiceSaveData
    {
        public string ActionId;
        public string ActionGroupId;
        public int WeekStepIndex;
        public int RunStepIndex;
        public float CostDays;
        public float TimelineStopChance;
    }

    [Serializable]
    public sealed class ActionRandomStateSaveData
    {
        /// <summary>状态所属周；与当前周不一致时整份状态重置。</summary>
        public int WeekIndex;

        /// <summary>本周已成功生成的候选卡数量，包含被重掷覆盖的旧行动组。</summary>
        public int CandidateIndex;

        /// <summary>本周已生成的行动组数量，包含重掷。</summary>
        public int GroupSerial;

        /// <summary>当前周行动数洗牌袋的剩余抽取顺序；从列表尾部消费。</summary>
        public List<int> RemainingChoiceCounts = new List<int>();

        /// <summary>本周类别累计次数；key 为 ActionRandomCategory 的整数值。</summary>
        public Dictionary<int, int> CategoryCounts = new Dictionary<int, int>();

        /// <summary>本周营业奖励累计次数；key 为 RewardKind 的整数值。</summary>
        public Dictionary<int, int> RewardCounts = new Dictionary<int, int>();
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
        public int RawBaseGold;
        public int RawBonusGold;
        public int BonusGold;
        public RewardDoubleTarget DoubleRewardTarget;
        public bool GoldAmountsResolved;
        public bool BaseGoldClaimed;
        public bool BonusGoldClaimed = true;
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
        public string Description;
        public string RuleText;
        public string SourceSlotId;
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
    public sealed class PendingHeartBreakSaveData
    {
        public int BeforeHeartCount;
        public int AfterHeartCount;
        public int BattleTotal;
        public BigNumberSaveData BattleTotalBig;
        public bool IsTerminal;
    }

    [Serializable]
    public sealed class PendingStarAwardSaveData
    {
        public string BattleKey;
        public int BeforeStars;
        public int AfterStars;
    }

    [Serializable]
    public sealed class PendingRewardBattleViewSaveData
    {
        public int RequiredScore;
        public int RawRequiredScore;
        public string Modifier;
        public string BossDebuffId;
        public string BattleKey;
        public bool IsBoss;
        public int LastTotal;
        public BigNumberSaveData LastTotalBig;
        // -1 表示旧存档未保存最终层数；不可用运行时默认值冒充已确认的结算结果。
        public int FinalHappyCakeLayers = -1;
        public bool HasDetailedScore;
        public float RawSum;
        public BigNumberSaveData RawSumBig;
        public float FinalFlat;
        public BigNumberSaveData FinalFlatBig;
        public float FinalMultiplier = 1f;
        public BigNumberSaveData FinalMultiplierBig;
        public List<PendingRewardBattleDishSaveData> Dishes = new List<PendingRewardBattleDishSaveData>();
        public List<PendingRewardCakeVisualSaveData> Cakes = new List<PendingRewardCakeVisualSaveData>();
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
        public BigNumberSaveData PermanentFlatBonusBig;
        public float PermanentMultBonus = 1f;
        public BigNumberSaveData PermanentMultBonusBig;
        public float TemporaryBaseMultiplier = 1f;
        public BigNumberSaveData TemporaryBaseMultiplierBig;
        public float ServeMultiplier = 1f;
        public BigNumberSaveData ServeMultiplierBig;
        public float ServeMultiplierFlatBonus;
        public BigNumberSaveData ServeMultiplierFlatBonusBig;
        public bool SkillsDisabled;
        public bool ExcludedFromScore;
        public bool IsTemporary;
        public bool HasDishScore;
        public float ScoreBaseValue;
        public BigNumberSaveData ScoreBaseValueBig;
        public float ScoreFlatBonus;
        public BigNumberSaveData ScoreFlatBonusBig;
        public float ScoreMultiplier = 1f;
        public BigNumberSaveData ScoreMultiplierBig;
        public int ScoreEffectiveCountAs = 1;
        public float ScoreExtraSettlementContribution;
        public BigNumberSaveData ScoreExtraSettlementContributionBig;
        public int ScoreExtraSettlementCount;
    }

    [Serializable]
    public sealed class PendingRewardCakeVisualSaveData
    {
        public float ViewportX;
        public float ViewportY;
        public float RotationZ;
        public float ScaleX = 1f;
        public float ScaleY = 1f;
        public float ScaleZ = 1f;
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
        public int FragmentRotation;
    }
}
