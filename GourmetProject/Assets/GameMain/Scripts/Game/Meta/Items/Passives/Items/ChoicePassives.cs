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

        public override RewardOffer ModifyBattleRewardOffer(
            RewardOffer offer,
            ActionExecutionContext actionContext,
            IRandomStream rng)
        {
            if (offer == null || rng == null || !IsNormalFoodAction(actionContext))
            {
                return offer;
            }

            _normalFoodCount++;
            int every = DefaultEvery;
            if (_normalFoodCount < every)
            {
                RefreshInfoText();
                return offer;
            }

            _normalFoodCount -= every;
            RewardChoiceGroup group = RewardGranter.BuildConfigChoiceGroup(Run, rng, _slotGroupId, _title, actionContext);
            if (group != null && group.HasChoices)
            {
                offer.AddFixedGroup(group);
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
            : base("dish_choice_3", "额外美食")
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_extra_item_choice")]
    public sealed class ExtraItemChoiceModel : NormalFoodExtraChoiceModel
    {
        public ExtraItemChoiceModel()
            : base("passive_choice_3", "额外道具")
        {
        }
    }
}
