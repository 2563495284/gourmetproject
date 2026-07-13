using UnityEngine.Scripting;

namespace GourmetProject.Game.Meta.Passives
{
    // 风味 / 标签族：均为复杂交互，尚未实装。获得时类走占位日志，非获得时类为无副作用占位。

    [Preserve]
    [PassiveItemModel("item_flavor_enhance")]
    public sealed class FlavorEnhanceModel : TodoOnAcquireModel
    {
        protected override string EffectName => "FlavorEnhance";
    }

    [Preserve]
    [PassiveItemModel("item_flavor_remove_gold")]
    public sealed class FlavorRemoveForGoldModel : TodoOnAcquireModel
    {
        protected override string EffectName => "FlavorRemoveForGold";
    }

    [Preserve]
    [PassiveItemModel("item_flavor_remove_copyskill")]
    public sealed class FlavorRemoveCopySkillModel : TodoOnAcquireModel
    {
        protected override string EffectName => "FlavorRemoveCopySkill";
    }

    [Preserve]
    [PassiveItemModel("item_flavor_remove_double")]
    public sealed class FlavorRemoveDoubleScoreModel : TodoOnAcquireModel
    {
        protected override string EffectName => "FlavorRemoveDoubleScore";
    }

    [Preserve]
    [PassiveItemModel("item_flavor_contagion")]
    public sealed class FlavorContagionModel : TodoOnAcquireModel
    {
        protected override string EffectName => "FlavorContagion";
    }

    [Preserve]
    [PassiveItemModel("item_celltag_enhance")]
    public sealed class CellTagEnhanceModel : TodoOnAcquireModel
    {
        protected override string EffectName => "CellTagEnhance";
    }

    [Preserve]
    [PassiveItemModel("item_celltag_contagion")]
    public sealed class CellTagContagionModel : TodoOnAcquireModel
    {
        protected override string EffectName => "CellTagContagion";
    }

    /// <summary>TODO(passive-item): 风味双槽，缺子系统；占位无副作用。</summary>
    [Preserve]
    [PassiveItemModel("item_flavor_double_slot")]
    public sealed class FlavorDoubleSlotModel : PassiveItemModel
    {
    }
}
