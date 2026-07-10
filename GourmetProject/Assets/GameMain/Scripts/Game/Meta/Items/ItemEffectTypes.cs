using System.Collections.Generic;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 道具 <c>effectType</c> 字符串常量集中处（对应 item.xlsx 的 effectType 列）。
    /// 新增道具效果时在这里登记常量，再在对应派发器/宿主系统里接入，避免字符串散落。
    /// 分组注释标明该效果由哪个子系统消费；触发时机由代码按 effectType 推导，不在配置表单独声明。
    /// </summary>
    public static class ItemEffectTypes
    {
        // —— 结算/倍率族（被动，走 ItemScoreEffectAdapter → ItemScoreEffectSource；Final* 走 BattleSession 快路径）——
        public const string FinalAddFlat = "FinalAddFlat";
        public const string FinalAddMult = "FinalAddMult";
        public const string TagBonus = "TagBonus";
        public const string PermanentAddFlatAll = "PermanentAddFlatAll";
        public const string PermanentAddMultAll = "PermanentAddMultAll";
        public const string CountThresholdFinalMult = "CountThresholdFinalMult";
        public const string PerDishSettledMultFlat = "PerDishSettledMultFlat";
        public const string PerSkillMultFlat = "PerSkillMultFlat";
        public const string NthServeMult = "NthServeMult";
        public const string EveryNthServeMult = "EveryNthServeMult";
        public const string CountAsBonusAll = "CountAsBonusAll";

        // —— 主动道具（走 ActiveItemEffectRegistry）——
        public const string ClearBoard = "ClearBoard";
        public const string ExtraServe = "ExtraServe";

        // —— 金币/利息族（走 ItemRuntime gold hook；ProcedureMain/RewardGranter 消费）——
        public const string GoldNow = "GoldNow";
        public const string GoldMealBonus = "GoldMealBonus";
        public const string GoldOnBossComplete = "GoldOnBossComplete";
        public const string GoldMealPercent = "GoldMealPercent";
        public const string GoldPerUnusedAdjust = "GoldPerUnusedAdjust";
        public const string GoldOnShopEnter = "GoldOnShopEnter";
        public const string ExtraInterest = "ExtraInterest";
        public const string MinGoldGuarantee = "MinGoldGuarantee";
        public const string InterestCapBonus = "InterestCapBonus";
        public const string Loan = "Loan";
        public const string GoldOnActiveUse = "GoldOnActiveUse";
        public const string GoldOnEventComplete = "GoldOnEventComplete";
        public const string GoldOnTransferCount = "GoldOnTransferCount";
        public const string GoldOnCakeLayers = "GoldOnCakeLayers";
        public const string GoldMealPenalty = "GoldMealPenalty";
        public const string GoldWeekClear = "GoldWeekClear";

        // —— 商店/删牌价格族（走 ItemRuntime shop hook；ShopService 消费）——
        public const string ShopDiscountFood = "ShopDiscountFood";
        public const string ShopDiscountFragment = "ShopDiscountFragment";
        public const string ShopDiscountActive = "ShopDiscountActive";
        public const string ShopDiscountPassive = "ShopDiscountPassive";
        public const string ShopDiscountRemove = "ShopDiscountRemove";
        public const string RemovePriceFixed = "RemovePriceFixed";
        public const string ShopDiscountRecipe = "ShopDiscountRecipe";
        public const string ShopRestock = "ShopRestock";
        public const string ShopPriceUp = "ShopPriceUp";
        public const string NoRemoveDish = "NoRemoveDish";

        // —— 目标分修正族（走 ItemRuntime requiredScore hook；GameRun.RequiredScore 消费）——
        public const string RequiredScoreToOne = "RequiredScoreToOne";
        public const string RequiredScoreNormalPct = "RequiredScoreNormalPct";
        public const string RequiredScoreSuperPct = "RequiredScoreSuperPct";
        public const string RequiredScoreFeastPct = "RequiredScoreFeastPct";

        // —— 不死 ——
        public const string Undying = "Undying";

        // —— 上菜/调整族 ——
        public const string StarGazeEveryN = "StarGazeEveryN";
        public const string StarGazeFirstN = "StarGazeFirstN";
        public const string FreeMoveFirst = "FreeMoveFirst";
        public const string AdjustCountBonus = "AdjustCountBonus";
        public const string AdjustToMult = "AdjustToMult";
        public const string FoodConvert = "FoodConvert";

        // —— 蛋糕层数族 ——
        public const string CakeLayerRetain = "CakeLayerRetain";
        public const string CakeLayerInitBonus = "CakeLayerInitBonus";
        public const string CakeLayerAccel = "CakeLayerAccel";
        public const string CakeLayerReqMinus = "CakeLayerReqMinus";

        // —— 时间轴族 ——
        public const string TimelineRandomize = "TimelineRandomize";
        public const string TimelineExtraDay = "TimelineExtraDay";
        public const string TimelineWeekMinus = "TimelineWeekMinus";
        public const string TimelineAddRewardNode = "TimelineAddRewardNode";
        public const string TimelineAddInterestNode = "TimelineAddInterestNode";
        public const string TimelineSkipNode = "TimelineSkipNode";

        // —— 奖励/获得/选择族 ——
        public const string GrantRandomPassive = "GrantRandomPassive";
        public const string ChooseOnePassive = "ChooseOnePassive";
        public const string GrantRandomActive = "GrantRandomActive";
        public const string ChooseOneActive = "ChooseOneActive";
        public const string ChooseOneFood = "ChooseOneFood";
        public const string ChooseOneFragment = "ChooseOneFragment";
        public const string GrantRecipe = "GrantRecipe";
        public const string FamilyPack = "FamilyPack";
        public const string RandomizeItems = "RandomizeItems";
        public const string RerollAction = "RerollAction";
        public const string ChoiceCountBonus = "ChoiceCountBonus";
        public const string ChoiceTimesBonus = "ChoiceTimesBonus";
        public const string ExtraFoodChoice = "ExtraFoodChoice";
        public const string ExtraItemChoice = "ExtraItemChoice";
        public const string CopyFood = "CopyFood";
        public const string ChoiceCountPenalty = "ChoiceCountPenalty";
        public const string DiscardNegative = "DiscardNegative";
        public const string DiscardNegativeForGold = "DiscardNegativeForGold";
        public const string ExtraActiveSlot = "ExtraActiveSlot";
        public const string BlockActive = "BlockActive";

        // —— 风味族 ——
        public const string FlavorEnhance = "FlavorEnhance";
        public const string FlavorRemoveForGold = "FlavorRemoveForGold";
        public const string FlavorRemoveCopySkill = "FlavorRemoveCopySkill";
        public const string FlavorRemoveDoubleScore = "FlavorRemoveDoubleScore";
        public const string FlavorDoubleSlot = "FlavorDoubleSlot";
        public const string FlavorContagion = "FlavorContagion";

        // —— 标签族 ——
        public const string CellTagEnhance = "CellTagEnhance";
        public const string CellTagContagion = "CellTagContagion";

        // —— 事件/行动概率族 ——
        public const string LuckyEventGuarantee = "LuckyEventGuarantee";
        public const string LuckyEventChance = "LuckyEventChance";
        public const string MoreEvents = "MoreEvents";

        // —— 专有：传递族（走 BattleSession 传递 hook）——
        public const string TransferTargetMult = "TransferTargetMult";
        public const string TransferSourceMult = "TransferSourceMult";

        /// <summary>获得瞬间结算一次的效果（走 <see cref="PassiveOnAcquireEffects"/>）。</summary>
        private static readonly HashSet<string> OnAcquireEffects = new HashSet<string>
        {
            GoldNow,
            Loan,
            DiscardNegative,
            DiscardNegativeForGold,
            GrantRandomPassive,
            FamilyPack,
            GoldMealBonus,
            RequiredScoreToOne,
            ChooseOnePassive,
            ChooseOneActive,
            ChooseOneFood,
            ChooseOneFragment,
            GrantRandomActive,
            RandomizeItems,
            GrantRecipe,
            CopyFood,
            RerollAction,
            FoodConvert,
            FlavorEnhance,
            FlavorRemoveForGold,
            FlavorRemoveCopySkill,
            FlavorRemoveDoubleScore,
            FlavorContagion,
            CellTagEnhance,
            CellTagContagion,
            TimelineRandomize,
            TimelineExtraDay,
            TimelineWeekMinus,
            TimelineAddRewardNode,
            TimelineAddInterestNode,
        };

        public static bool IsOnAcquireEffect(string effectType)
        {
            return !string.IsNullOrEmpty(effectType) && OnAcquireEffects.Contains(effectType);
        }
    }
}
