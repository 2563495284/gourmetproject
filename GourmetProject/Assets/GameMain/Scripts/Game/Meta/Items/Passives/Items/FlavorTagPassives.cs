using UnityEngine.Scripting;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Meta.Passives
{
    // 风味 / 标签族：领取时自动随机结算，并把前后变化交给 BattleForm 展示。

    public abstract class FlavorTagOnAcquireModel : PassiveItemModel
    {
        protected IRandomStream Rng()
        {
            string key = $"onacq_{ItemId}_w{Run.WeekIndex}_d{Run.CurrentDay:0.0}_s{Run.RunActionStepIndex}";
            return GameApp.Random?.DomainStream(SeedDomains.Item, key);
        }

        protected void FinishRecipe(RecipeMutationResult result)
        {
            MarkIconUsed();
            RunPersistence.Save(Run);
            PassiveMutationPresenter.ShowRecipe(Run, result);
        }

        protected void FinishCells(CellMutationResult result)
        {
            MarkIconUsed();
            RunPersistence.Save(Run);
            PassiveMutationPresenter.ShowCells(Run, result);
        }
    }

    [Preserve]
    [PassiveItemModel("item_flavor_enhance")]
    public sealed class FlavorEnhanceModel : FlavorTagOnAcquireModel
    {
        public override void OnAcquired()
        {
            FinishRecipe(PassiveRecipeMutationService.AddRandomFlavors(Run, Def.Name, System.Math.Max(1, (int)Value), Rng()));
        }
    }

    [Preserve]
    [PassiveItemModel("item_flavor_remove_gold")]
    public sealed class FlavorRemoveForGoldModel : FlavorTagOnAcquireModel
    {
        public override void OnAcquired()
        {
            FinishRecipe(PassiveRecipeMutationService.RemoveFlavorForGold(Run, Def.Name, System.Math.Max(0, (int)Value), Rng()));
        }
    }

    [Preserve]
    [PassiveItemModel("item_flavor_remove_copyskill")]
    public sealed class FlavorRemoveCopySkillModel : FlavorTagOnAcquireModel
    {
        public override void OnAcquired()
        {
            FinishRecipe(PassiveRecipeMutationService.RemoveFlavorCopySkill(Run, Def.Name, Rng()));
        }
    }

    [Preserve]
    [PassiveItemModel("item_flavor_remove_double")]
    public sealed class FlavorRemoveDoubleScoreModel : FlavorTagOnAcquireModel
    {
        public override void OnAcquired()
        {
            FinishRecipe(PassiveRecipeMutationService.RemoveFlavorDoubleScore(Run, Def.Name, Value > 0f ? Value : 2f, Rng()));
        }
    }

    [Preserve]
    [PassiveItemModel("item_flavor_contagion")]
    public sealed class FlavorContagionModel : FlavorTagOnAcquireModel
    {
        public override void OnAcquired()
        {
            FinishRecipe(PassiveRecipeMutationService.ContagionFlavor(Run, Def.Name, Rng()));
        }
    }

    [Preserve]
    [PassiveItemModel("item_celltag_enhance")]
    public sealed class CellTagEnhanceModel : FlavorTagOnAcquireModel
    {
        public override void OnAcquired()
        {
            FinishCells(PassiveRecipeMutationService.AddRandomMaterials(Run, Def.Name, System.Math.Max(1, (int)Value), Rng()));
        }
    }

    [Preserve]
    [PassiveItemModel("item_celltag_contagion")]
    public sealed class CellTagContagionModel : FlavorTagOnAcquireModel
    {
        public override void OnAcquired()
        {
            FinishCells(PassiveRecipeMutationService.ContagionMaterial(Run, Def.Name, Rng()));
        }
    }

    [Preserve]
    [PassiveItemModel("item_flavor_double_slot")]
    public sealed class FlavorDoubleSlotModel : PassiveItemModel
    {
        public override int FoodFlavorLimitBonus() => System.Math.Max(1, (int)Value);
    }
}
