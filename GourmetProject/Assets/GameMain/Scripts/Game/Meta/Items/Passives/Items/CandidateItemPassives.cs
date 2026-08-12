using System;
using System.Collections.Generic;
using System.Globalization;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using UnityEngine.Scripting;

namespace GourmetProject.Game.Meta.Passives
{
    [Preserve]
    [PassiveItemModel("item_transfer_extra_targets")]
    public sealed class SweetTransferExtraTargetsModel : PassiveItemModel
    {
        public override int SweetTransferExtraTargetCount()
            => Math.Max(0, (int)Math.Round(Value, MidpointRounding.AwayFromZero));
    }

    [Preserve]
    [PassiveItemModel("item_flavored_flat")]
    public sealed class FlavoredDishFlatModel : ScoreSpecModel
    {
        public FlavoredDishFlatModel() : base(ItemScoreEffectType.TagBonus)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_flavored_count_as")]
    public sealed class FlavoredDishCountAsModel : ScoreSpecModel
    {
        public FlavoredDishCountAsModel() : base(ItemScoreEffectType.TagCountAsBonus)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_active_count_flat_all")]
    public sealed class ActiveItemCountFlatAllModel : PassiveItemModel
    {
        public override IEnumerable<ItemScoreSpec> BuildScoreSpecs()
        {
            yield return Spec(ItemScoreEffectType.AllDishFlat, Value * (Run?.ActiveItemCount ?? 0));
        }

        private ItemScoreSpec Spec(ItemScoreEffectType type, float value)
            => new ItemScoreSpec(type, value, Param, ItemId, Def?.Name ?? ItemId);
    }

    [Preserve]
    [PassiveItemModel("item_empty_active_slot_mult_all")]
    public sealed class EmptyActiveSlotMultAllModel : PassiveItemModel
    {
        public override IEnumerable<ItemScoreSpec> BuildScoreSpecs()
        {
            int empty = Run == null ? 0 : Math.Max(0, Run.ActiveSlotCapacity - Run.ActiveItemCount);
            yield return new ItemScoreSpec(
                ItemScoreEffectType.AllDishMultFlat,
                Value * empty,
                Param,
                ItemId,
                Def?.Name ?? ItemId);
        }
    }

    [Preserve]
    [PassiveItemModel("item_edge_flat")]
    public sealed class EdgeDishFlatModel : ScoreSpecModel
    {
        public EdgeDishFlatModel() : base(ItemScoreEffectType.TagBonus)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_non_edge_mult")]
    public sealed class NonEdgeDishMultModel : ScoreSpecModel
    {
        public NonEdgeDishMultModel() : base(ItemScoreEffectType.TagMultFlat)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_gold_per5_flat_all")]
    public sealed class GoldStepFlatAllModel : PassiveItemModel
    {
        public override IEnumerable<ItemScoreSpec> BuildScoreSpecs()
        {
            int every = Math.Max(1, PassiveParam.ParseInt(Param, "every", 5));
            int steps = Math.Max(0, Run?.Gold ?? 0) / every;
            yield return new ItemScoreSpec(
                ItemScoreEffectType.AllDishFlat,
                Value * steps,
                Param,
                ItemId,
                Def?.Name ?? ItemId);
        }
    }

    [Preserve]
    [PassiveItemModel("item_empty_cell_flat_all")]
    public sealed class EmptyCellFlatAllModel : ScoreSpecModel
    {
        public EmptyCellFlatAllModel() : base(ItemScoreEffectType.AllDishFlatPerEmptyCell)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_unused_discard_mult_all")]
    public sealed class UnusedDiscardMultAllModel : ScoreSpecModel
    {
        public UnusedDiscardMultAllModel() : base(ItemScoreEffectType.AllDishMultPerUnusedDiscard)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_unused_discard_flat_all")]
    public sealed class UnusedDiscardFlatAllModel : ScoreSpecModel
    {
        public UnusedDiscardFlatAllModel() : base(ItemScoreEffectType.AllDishFlatPerUnusedDiscard)
        {
        }
    }

    public abstract class RewardAbandonLuckModel : PassiveItemModel
    {
        private readonly HiddenScorePurpose _purpose;
        private int _abandonCount;

        protected RewardAbandonLuckModel(HiddenScorePurpose purpose)
        {
            _purpose = purpose;
        }

        public override string InfoText => _abandonCount.ToString(CultureInfo.InvariantCulture);

        public override void OnRewardAbandoned()
        {
            _abandonCount++;
            Flash();
            RefreshInfoText();
        }

        public override float HiddenScoreOffset(HiddenScorePurpose purpose)
        {
            float configured = base.HiddenScoreOffset(purpose);
            return purpose == _purpose ? configured + Value * _abandonCount : configured;
        }

        public override string CaptureState()
            => JoinState(CaptureIconState(), $"count:{_abandonCount.ToString(CultureInfo.InvariantCulture)}");

        public override void RestoreState(string data)
        {
            RestoreIconState(data);
            _abandonCount = Math.Max(0, ParseStateInt(data, "count", 0));
        }
    }

    [Preserve]
    [PassiveItemModel("item_skip_reward_dish_luck")]
    public sealed class RewardAbandonDishLuckModel : RewardAbandonLuckModel
    {
        public RewardAbandonDishLuckModel() : base(HiddenScorePurpose.Dish)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_skip_reward_passive_luck")]
    public sealed class RewardAbandonPassiveLuckModel : RewardAbandonLuckModel
    {
        public RewardAbandonPassiveLuckModel() : base(HiddenScorePurpose.PassiveItem)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_super_material_spread")]
    public sealed class SuperMaterialSpreadModel : PassiveItemModel
    {
        public override RewardOffer OnFoodBattleSettled(
            ActionExecutionContext actionContext,
            bool survived,
            IRandomStream rng)
        {
            cfg.Food food = FoodService.Resolve(Run?.Tables, actionContext?.Action);
            if (!survived || food?.ActionKind != cfg.FoodActionKind.Super || rng == null)
            {
                return null;
            }

            CellMutationResult result = PassiveRecipeMutationService.SpreadMaterialToAdjacentCell(
                Run,
                Def?.Name,
                rng);
            if (result.HasChanges)
            {
                result.SourceItemId = ItemId;
                Flash();
                PassiveMutationPresenter.ShowCells(Run, result);
            }

            return null;
        }
    }

    [Preserve]
    [PassiveItemModel("item_count_as_cake")]
    public sealed class AllDishCountAsCakeModel : ScoreSpecModel
    {
        public AllDishCountAsCakeModel() : base(ItemScoreEffectType.AllDishTemporaryCategory)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_super_fragment_reward")]
    public sealed class SuperFragmentRewardModel : PassiveItemModel
    {
        public override string FoodBattleSettlementRewardTitle => Def?.Name ?? "额外餐桌格";

        public override RewardOffer OnFoodBattleSettled(
            ActionExecutionContext actionContext,
            bool survived,
            IRandomStream rng)
        {
            cfg.Food food = FoodService.Resolve(Run?.Tables, actionContext?.Action);
            if (!survived || food?.ActionKind != cfg.FoodActionKind.Super || rng == null)
            {
                return null;
            }

            RewardOffer offer = RewardGranter.BuildConfigOffer(
                Run,
                rng,
                string.IsNullOrEmpty(Param) ? "fragment_choice_3" : Param,
                actionContext);
            if (offer != null)
            {
                Flash();
            }

            return offer;
        }
    }

    [Preserve]
    [PassiveItemModel("item_settle_permanent_flat_all")]
    public sealed class SettledPermanentFlatAllModel : ScoreSpecModel
    {
        public SettledPermanentFlatAllModel() : base(ItemScoreEffectType.PerDishPermanentFlat)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_same_base_mult_all")]
    public sealed class SameBaseDishMultAllModel : ScoreSpecModel
    {
        public SameBaseDishMultAllModel() : base(ItemScoreEffectType.SameBaseDishMultFlat)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_remove_arrow_cookie_2")]
    public sealed class RemoveTwoArrowCookiesModel : FlavorTagOnAcquireModel
    {
        public override void OnAcquired()
        {
            FinishRecipe(PassiveRecipeMutationService.RemoveArrowCookies(
                Run,
                Def?.Name,
                Math.Max(0, (int)Value)));
        }
    }

    [Preserve]
    [PassiveItemModel("item_remove_arrow_cookie_all")]
    public sealed class RemoveAllArrowCookiesModel : FlavorTagOnAcquireModel
    {
        public override void OnAcquired()
        {
            FinishRecipe(PassiveRecipeMutationService.RemoveArrowCookies(Run, Def?.Name, int.MaxValue));
        }
    }

    [Preserve]
    [PassiveItemModel("item_randomize_recipe_dishes")]
    public sealed class RandomizeRecipeDishesModel : FlavorTagOnAcquireModel
    {
        public override void OnAcquired()
        {
            FinishRecipe(PassiveRecipeMutationService.RandomizeAllRecipeDishes(Run, Def?.Name, Rng()));
        }
    }

    [Preserve]
    [PassiveItemModel("item_discard_dish_flat")]
    public sealed class DiscardDishPermanentFlatModel : PassiveItemModel
    {
        public override void ApplyToBattle(BattleSession session)
        {
            if (session != null)
            {
                session.DishDiscarded += OnDishDiscarded;
            }
        }

        private void OnDishDiscarded(DishDiscardOccurrence occurrence)
        {
            if (!IsStillHeld || Run == null || occurrence.SourceBookIndex != 0 || occurrence.SourceDishIndex < 0)
            {
                return;
            }

            if (Run.AddRecipeScoreFlat(occurrence.SourceDishIndex, Value))
            {
                Flash();
            }
        }
    }

    [Preserve]
    [PassiveItemModel("item_heart_capacity")]
    [PassiveItemModel("item_heart_capacity_deluxe")]
    public sealed class HeartCapacityBonusModel : PassiveItemModel
    {
        public override int HeartCapacityBonus()
            => Math.Max(0, (int)Math.Round(Value, MidpointRounding.AwayFromZero));
    }

    [Preserve]
    [PassiveItemModel("item_restore_heart")]
    [PassiveItemModel("item_restore_hearts")]
    public sealed class RestoreHeartOnAcquireModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            Run?.RestoreHearts(Math.Max(0, (int)Math.Round(Value, MidpointRounding.AwayFromZero)));
            MarkIconUsed();
        }
    }

    [Preserve]
    [PassiveItemModel("item_slot_cost_discount")]
    public sealed class SlotCostDiscountModel : PassiveItemModel
    {
        public override float ModifySlotSpinCost(float cost)
        {
            if (cost <= 0f || Value <= 0f || Value >= 1f)
            {
                return cost;
            }

            return cost * (1f - Value);
        }
    }
}
