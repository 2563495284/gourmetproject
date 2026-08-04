using UnityEngine.Scripting;

namespace GourmetProject.Game.Meta.Passives
{
    /// <summary>获得时随机发放若干装饰品（走奖励配置 + RewardForm）。</summary>
    [Preserve]
    [PassiveItemModel("item_grant_two_passive")]
    public sealed class GrantRandomPassiveModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            PassiveOnAcquireEffects.GrantConfigReward(Run, Definition);
            MarkIconUsed();
        }
    }

    /// <summary>全家福：获得时金币 + 随机被动。</summary>
    [Preserve]
    [PassiveItemModel("item_family_pack")]
    public sealed class FamilyPackModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            PassiveOnAcquireEffects.ApplyFamilyPack(Run, Definition);
            MarkIconUsed();
        }
    }

    /// <summary>获得时丢弃若干诅咒装饰品。</summary>
    [Preserve]
    [PassiveItemModel("item_discard_negative")]
    public sealed class DiscardNegativeModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            PassiveOnAcquireEffects.DiscardNegatives(Run, System.Math.Max(0, (int)Value), goldPer: 0);
            MarkIconUsed();
        }
    }

    /// <summary>获得时丢弃全部诅咒装饰品，每个换金币。</summary>
    [Preserve]
    [PassiveItemModel("item_discard_negative_gold")]
    public sealed class DiscardNegativeForGoldModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            PassiveOnAcquireEffects.DiscardNegatives(Run, int.MaxValue, goldPer: System.Math.Max(0, (int)Value));
            MarkIconUsed();
        }
    }

    [Preserve]
    [PassiveItemModel("item_choose_one_passive")]
    public sealed class ChooseOnePassiveModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            PassiveOnAcquireEffects.GrantConfigReward(Run, Definition);
            MarkIconUsed();
        }
    }

    [Preserve]
    [PassiveItemModel("item_grant_two_active")]
    public sealed class GrantRandomActiveModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            PassiveOnAcquireEffects.GrantConfigReward(Run, Definition);
            MarkIconUsed();
        }
    }

    [Preserve]
    [PassiveItemModel("item_choose_one_active")]
    public sealed class ChooseOneActiveModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            PassiveOnAcquireEffects.GrantConfigReward(Run, Definition);
            MarkIconUsed();
        }
    }

    [Preserve]
    [PassiveItemModel("item_choose_one_food")]
    public sealed class ChooseOneFoodModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            PassiveOnAcquireEffects.GrantConfigReward(Run, Definition);
            MarkIconUsed();
        }
    }

    [Preserve]
    [PassiveItemModel("item_choose_one_fragment")]
    public sealed class ChooseOneFragmentModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            PassiveOnAcquireEffects.GrantConfigReward(Run, Definition);
            MarkIconUsed();
        }
    }

    [Preserve]
    [PassiveItemModel("item_randomize_items")]
    public sealed class RandomizeItemsModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            PassiveOnAcquireEffects.RandomizeItems(Run, Definition);
            MarkIconUsed();
        }
    }

    [Preserve]
    [PassiveItemModel("item_reroll_action")]
    public sealed class RerollActionModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            PassiveOnAcquireEffects.AddActionRerolls(Run, System.Math.Max(1, (int)Value));
            MarkIconUsed();
        }
    }

    [Preserve]
    [PassiveItemModel("item_copy_food")]
    public sealed class CopyFoodModel : FlavorTagOnAcquireModel
    {
        public override void OnAcquired()
        {
            FinishRecipe(PassiveRecipeMutationService.CopyRandomFood(
                Run,
                Def.Name,
                System.Math.Max(1, (int)Value),
                Rng()));
        }
    }
}
