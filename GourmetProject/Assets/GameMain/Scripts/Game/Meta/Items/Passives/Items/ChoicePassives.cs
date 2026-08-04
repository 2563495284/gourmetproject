using System.Globalization;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;
using UnityEngine.Scripting;

namespace GourmetProject.Game.Meta.Passives
{
    /// <summary>多选一可选数量增减（正面 +/负面 -，配置值自带符号）。</summary>
    [Preserve]
    [PassiveItemModel("item_choice_count_plus1")]
    [PassiveItemModel("item_choice_minus1")]
    public sealed class ChoiceCountDeltaModel : PassiveItemModel
    {
        public override int ChoiceCountDelta() => (int)Value;
    }

    [Preserve]
    [PassiveItemModel("item_choice_times_plus1")]
    public sealed class ChoiceTimesModel : PassiveItemModel
    {
        public override int ChoiceTimesBonus() => (int)Value;

        public override int ChoiceCountDelta() => -(int)Value;
    }

    public abstract class NormalFoodExtraChoiceModel : PassiveItemModel
    {
        private const int DefaultEvery = 6;

        private readonly string _slotGroupId;
        private readonly string _title;
        private int _normalFoodCount;

        protected NormalFoodExtraChoiceModel(string slotGroupId, string title)
        {
            _slotGroupId = slotGroupId;
            _title = title;
        }

        public override string InfoText => _normalFoodCount.ToString(CultureInfo.InvariantCulture);

        public override string FoodBattleSettlementRewardTitle => _title;

        public override RewardOffer OnFoodBattleSettled(
            ActionExecutionContext actionContext,
            bool survived,
            IRandomStream rng)
        {
            if (!IsNormalFoodAction(actionContext))
            {
                return null;
            }

            _normalFoodCount++;
            int every = System.Math.Max(1, PassiveParam.ParseInt(Param, "every", DefaultEvery));
            if (_normalFoodCount < every)
            {
                RefreshInfoText();
                return null;
            }

            _normalFoodCount -= every;
            RewardOffer offer = survived && rng != null
                ? RewardGranter.BuildConfigOffer(Run, rng, _slotGroupId, actionContext)
                : null;
            if (offer != null)
            {
                Flash();
            }

            RefreshInfoText();
            return offer;
        }

        public override string CaptureState()
            => JoinState(CaptureIconState(), $"count:{_normalFoodCount.ToString(CultureInfo.InvariantCulture)}");

        public override void RestoreState(string data)
        {
            RestoreIconState(data);
            _normalFoodCount = System.Math.Max(0, ParseStateInt(data, "count", 0));
        }

        private bool IsNormalFoodAction(ActionExecutionContext actionContext)
        {
            cfg.Food food = FoodService.Resolve(Run?.Tables, actionContext?.Action);
            return food != null && food.ActionKind == cfg.FoodActionKind.Normal;
        }
    }

    [Preserve]
    [PassiveItemModel("item_extra_food_choice")]
    public sealed class ExtraFoodChoiceModel : NormalFoodExtraChoiceModel
    {
        public ExtraFoodChoiceModel()
            : base("dish_choice_3", "额外食物")
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_extra_item_choice")]
    public sealed class ExtraItemChoiceModel : PassiveItemModel
    {
        private const int DefaultEvery = 4;

        private int _superFoodCount;

        public override string InfoText => _superFoodCount.ToString(CultureInfo.InvariantCulture);

        public override string FoodBattleSettlementRewardTitle => "额外装饰品和消耗品";

        public override RewardOffer OnFoodBattleSettled(
            ActionExecutionContext actionContext,
            bool survived,
            IRandomStream rng)
        {
            cfg.Food food = FoodService.Resolve(Run?.Tables, actionContext?.Action);
            if (food == null || food.ActionKind != cfg.FoodActionKind.Super)
            {
                return null;
            }

            _superFoodCount++;
            int every = System.Math.Max(1, PassiveParam.ParseInt(Param, "every", DefaultEvery));
            if (_superFoodCount < every)
            {
                RefreshInfoText();
                return null;
            }

            _superFoodCount -= every;
            RewardOffer offer = survived && rng != null
                ? RewardGranter.BuildConfigOffer(Run, rng, "passive_choice_3", actionContext)
                : null;
            if (offer != null)
            {
                Flash();
            }

            RefreshInfoText();
            return offer;
        }

        public override string CaptureState()
            => JoinState(CaptureIconState(), $"count:{_superFoodCount.ToString(CultureInfo.InvariantCulture)}");

        public override void RestoreState(string data)
        {
            RestoreIconState(data);
            _superFoodCount = System.Math.Max(0, ParseStateInt(data, "count", 0));
        }
    }
}
