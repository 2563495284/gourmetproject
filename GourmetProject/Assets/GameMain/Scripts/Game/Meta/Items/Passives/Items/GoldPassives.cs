using UnityEngine.Scripting;

namespace GourmetProject.Game.Meta.Passives
{
    /// <summary>美食奖励金币百分比（利润提成 +/克扣工钱 -）。</summary>
    [Preserve]
    [PassiveItemModel("item_gold_percent")]
    [PassiveItemModel("item_gold_meal_penalty")]
    public sealed class MealRewardGoldPctModel : PassiveItemModel
    {
        public override float MealRewardGoldPct() => Value;
    }

    [Preserve]
    [PassiveItemModel("item_gold_boss")]
    public sealed class BossGoldModel : PassiveItemModel
    {
        public override int BossCompleteGold() => (int)Value;
    }

    [Preserve]
    [PassiveItemModel("item_gold_on_event")]
    public sealed class EventGoldModel : PassiveItemModel
    {
        public override int EventCompleteGold() => (int)Value;
    }

    [Preserve]
    [PassiveItemModel("item_gold_on_shop")]
    public sealed class ShopEnterGoldModel : PassiveItemModel
    {
        public override int ShopEnterGold() => (int)Value;
    }

    [Preserve]
    [PassiveItemModel("item_gold_on_active")]
    public sealed class ActiveUseGoldModel : PassiveItemModel
    {
        public override int ActiveUseGold() => (int)Value;
    }

    [Preserve]
    [PassiveItemModel("item_min_gold")]
    public sealed class MinGoldGuaranteeModel : PassiveItemModel
    {
        public override bool TryGetMinGoldGuarantee(out int value)
        {
            value = (int)Value;
            return true;
        }
    }

    [Preserve]
    [PassiveItemModel("item_gold_week_clear")]
    public sealed class GoldWeekClearModel : PassiveItemModel
    {
        public override bool ClearsGoldOnWeekEnd() => true;
    }

    [Preserve]
    [PassiveItemModel("item_interest_cap")]
    public sealed class InterestCapModel : PassiveItemModel
    {
        public override bool TryGetInterestCapOverride(out int value)
        {
            value = (int)Value;
            return true;
        }
    }

    [Preserve]
    [PassiveItemModel("item_extra_interest")]
    public sealed class ExtraInterestModel : PassiveItemModel
    {
        public override bool HasExtraInterest() => true;
    }

    [Preserve]
    [PassiveItemModel("item_adjust_to_gold")]
    public sealed class GoldPerUnusedAdjustModel : PassiveItemModel
    {
        public override int GoldPerUnusedAdjust() => (int)Value;
    }

    /// <summary>美食分红：获得时登记生效局数；每局额外金币由 MealBonusGoldPerMeal 提供。</summary>
    [Preserve]
    [PassiveItemModel("item_gold_meal_bonus")]
    public sealed class MealBonusGoldModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            Run?.AddMealBonusMeals(PassiveParam.ParseInt(Param, "meals", 0));
        }

        public override bool IsIconUsed => Run != null && Run.MealBonusRemaining <= 0;

        public override int MealBonusGoldPerMeal() => (int)Value;
    }

    /// <summary>获得时随机金币（range:min,max）。</summary>
    [Preserve]
    [PassiveItemModel("item_gold_random")]
    public sealed class GoldNowModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            PassiveOnAcquireEffects.ApplyGoldNow(Run, Definition);
            MarkIconUsed();
        }
    }

    /// <summary>高利贷：获得时发钱并登记债务。</summary>
    [Preserve]
    [PassiveItemModel("item_loan")]
    public sealed class LoanModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            PassiveOnAcquireEffects.ApplyLoan(Run, Definition);
            MarkIconUsed();
        }
    }

    // —— TODO(passive-item): 以下缺消费方，保留模型占位（行为与现状一致，无副作用）——

    [Preserve]
    [PassiveItemModel("item_gold_on_transfer")]
    public sealed class GoldOnTransferCountModel : PassiveItemModel
    {
    }

    [Preserve]
    [PassiveItemModel("item_cake_to_gold")]
    public sealed class GoldOnCakeLayersModel : PassiveItemModel
    {
    }
}
