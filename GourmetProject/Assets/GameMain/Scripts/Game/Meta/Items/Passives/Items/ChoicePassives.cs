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
    }

    // —— TODO(passive-item): 缺消费方，占位无副作用 ——

    [Preserve]
    [PassiveItemModel("item_extra_food_choice")]
    public sealed class ExtraFoodChoiceModel : PassiveItemModel
    {
    }

    [Preserve]
    [PassiveItemModel("item_extra_item_choice")]
    public sealed class ExtraItemChoiceModel : PassiveItemModel
    {
    }
}
