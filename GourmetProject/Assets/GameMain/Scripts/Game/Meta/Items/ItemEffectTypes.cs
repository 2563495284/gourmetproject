using System.Collections.Generic;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 道具 <c>effectType</c> 字符串常量集中处。
    /// 被动道具已改为「itemId → PassiveItemModel」按 id 绑定，不再按 effectType 分发；
    /// 这里的常量当前主要供主动道具（<see cref="ActiveItemEffectRegistry"/>）与词表参考。
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
        public const string NthServeMultFlat = "NthServeMultFlat";
        public const string EveryNthServeMult = "EveryNthServeMult";
        public const string CountAsBonusAll = "CountAsBonusAll";

        // —— 主动道具（走 ActiveItemEffectRegistry）——
        // 无目标（None/Global）战斗/资源操作：
        public const string ClearBoard = "ClearBoard";
        public const string ExtraServe = "ExtraServe";
        // 需选目标（DiningTableDish 等）的目标操作族，对标杀戮尖塔2 药水的 OnUse(target)：
        public const string AddScore = "AddScore";           // 给目标菜永久加分
        public const string AddCountAs = "AddCountAs";        // 给目标菜加「视为食物数」
        public const string AddFlavor = "AddFlavor";          // 给目标菜（菜谱）永久附加风味（effectParam=风味id，调味小票）
        public const string AddMaterial = "AddMaterial";      // 给目标格永久附加材质（effectParam=材质id，铺台小票）
        public const string EnhanceFlavor = "EnhanceFlavor";  // 强化目标菜风味
        public const string ConvertCategory = "ConvertCategory"; // 转换目标菜分类
        public const string ConvertFlavor = "ConvertFlavor";  // 转换目标菜风味
        public const string DuplicateDish = "DuplicateDish";  // 复制目标菜
        public const string GenerateDish = "GenerateDish";    // 生成一道菜（需随机流）
        public const string DestroyDish = "DestroyDish";      // 移除目标菜
        public const string RemoveFlavor = "RemoveFlavor";    // 移除目标菜风味

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
        public const string TimelineAddRewardNode = "TimelineAddRewardNode"; // 主动：奖励单，运行时加一个奖励节点
        public const string TimelineAddInterestNode = "TimelineAddInterestNode";
        public const string TimelineSkipNode = "TimelineSkipNode";
        public const string TimelineExecuteNext = "TimelineExecuteNext"; // 主动：加急单，立即执行行动轴下一个节点
        public const string ResetBossDebuff = "ResetBossDebuff";           // 主动：盛宴调整单，重新随机本周 Boss Debuff

        // —— 奖励/获得/选择族 ——
        public const string GrantRandomPassive = "GrantRandomPassive";
        public const string ChooseOnePassive = "ChooseOnePassive";
        public const string GrantRandomActive = "GrantRandomActive";
        public const string ChooseOneActive = "ChooseOneActive";
        public const string ChooseOneFood = "ChooseOneFood";
        public const string ChooseOneFragment = "ChooseOneFragment";
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

        /// <summary>
        /// 主动道具合法 <c>effectType</c> 词表（配在 <c>TbActiveItem</c> 的值域）。
        /// 用于填表/运行时守卫：不在此集合内的值视为非法主动效果。
        /// 新增主动效果时：在此登记 + 在 <see cref="ActiveItemEffectRegistry"/> 落地。
        /// </summary>
        private static readonly HashSet<string> ActiveEffectTypes = new HashSet<string>
        {
            ClearBoard,
            ExtraServe,
            GoldNow,
            AdjustCountBonus,
            AddScore,
            AddCountAs,
            AddFlavor,
            AddMaterial,
            EnhanceFlavor,
            ConvertCategory,
            ConvertFlavor,
            DuplicateDish,
            GenerateDish,
            DestroyDish,
            RemoveFlavor,
            // —— 排程小票（Global，局外/地图专用，由情境限制）——
            RerollAction,
            ResetBossDebuff,
            TimelineExecuteNext,
            TimelineAddRewardNode,
        };

        /// <summary>该 effectType 是否为合法的主动道具效果（按主动词表校验）。</summary>
        public static bool IsValidActiveEffectType(string effectType)
        {
            return !string.IsNullOrEmpty(effectType) && ActiveEffectTypes.Contains(effectType);
        }
    }
}
