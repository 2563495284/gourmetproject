using UnityEngine.Scripting;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.Meta.Passives
{
    /// <summary>获得时随机发放若干被动道具。</summary>
    [Preserve]
    [PassiveItemModel("item_grant_two_passive")]
    public sealed class GrantRandomPassiveModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            PassiveOnAcquireEffects.GrantRandomPassives(Run, (int)Value, ItemId);
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
        }
    }

    /// <summary>获得时丢弃若干负面道具。</summary>
    [Preserve]
    [PassiveItemModel("item_discard_negative")]
    public sealed class DiscardNegativeModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            PassiveOnAcquireEffects.DiscardNegatives(Run, System.Math.Max(0, (int)Value), goldPer: 0);
        }
    }

    /// <summary>获得时丢弃全部负面道具，每个换金币。</summary>
    [Preserve]
    [PassiveItemModel("item_discard_negative_gold")]
    public sealed class DiscardNegativeForGoldModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            PassiveOnAcquireEffects.DiscardNegatives(Run, int.MaxValue, goldPer: System.Math.Max(0, (int)Value));
        }
    }

    // —— TODO(passive-item): 需选目标 / UI / 未就绪子系统；获得时占位（与现状一致，仅日志）——

    /// <summary>获得时占位日志基类。</summary>
    public abstract class TodoOnAcquireModel : PassiveItemModel
    {
        protected abstract string EffectName { get; }

        public override void OnAcquired()
        {
            Log.Info($"OnAcquire 效果 {EffectName}({ItemId}) 尚未实装，已忽略。", "Item");
        }
    }

    [Preserve]
    [PassiveItemModel("item_choose_one_passive")]
    public sealed class ChooseOnePassiveModel : TodoOnAcquireModel
    {
        protected override string EffectName => "ChooseOnePassive";
    }

    [Preserve]
    [PassiveItemModel("item_grant_two_active")]
    public sealed class GrantRandomActiveModel : TodoOnAcquireModel
    {
        protected override string EffectName => "GrantRandomActive";
    }

    [Preserve]
    [PassiveItemModel("item_choose_one_active")]
    public sealed class ChooseOneActiveModel : TodoOnAcquireModel
    {
        protected override string EffectName => "ChooseOneActive";
    }

    [Preserve]
    [PassiveItemModel("item_choose_one_food")]
    public sealed class ChooseOneFoodModel : TodoOnAcquireModel
    {
        protected override string EffectName => "ChooseOneFood";
    }

    [Preserve]
    [PassiveItemModel("item_choose_one_fragment")]
    public sealed class ChooseOneFragmentModel : TodoOnAcquireModel
    {
        protected override string EffectName => "ChooseOneFragment";
    }

    [Preserve]
    [PassiveItemModel("item_grant_recipe")]
    public sealed class GrantRecipeModel : TodoOnAcquireModel
    {
        protected override string EffectName => "GrantRecipe";
    }

    [Preserve]
    [PassiveItemModel("item_randomize_items")]
    public sealed class RandomizeItemsModel : TodoOnAcquireModel
    {
        protected override string EffectName => "RandomizeItems";
    }

    [Preserve]
    [PassiveItemModel("item_reroll_action")]
    public sealed class RerollActionModel : TodoOnAcquireModel
    {
        protected override string EffectName => "RerollAction";
    }

    [Preserve]
    [PassiveItemModel("item_copy_food")]
    public sealed class CopyFoodModel : TodoOnAcquireModel
    {
        protected override string EffectName => "CopyFood";
    }
}
