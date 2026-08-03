namespace GourmetProject.Game.UI.Meta
{
    /// <summary>行动卡与领奖页共享的 Food 奖励徽章命名规则。</summary>
    internal static class RewardBadgeResolver
    {
        internal const string BaseDishSpriteName = "reward_badge_base_dish";

        internal static string SpriteNameFor(
            cfg.FoodActionKind actionKind,
            cfg.RewardKind rewardKind)
        {
            bool isSuper = actionKind == cfg.FoodActionKind.Super;
            switch (rewardKind)
            {
                case cfg.RewardKind.Gold:
                    return isSuper ? "reward_badge_gold_large" : "reward_badge_gold";
                case cfg.RewardKind.FragmentChoice:
                    return isSuper ? "reward_badge_table_cell_large" : "reward_badge_table_cell";
                case cfg.RewardKind.PassiveItemChoice:
                    return isSuper ? "reward_badge_passive_item_4" : "reward_badge_passive_item";
                case cfg.RewardKind.ActiveItemStrengthen:
                    return isSuper ? "reward_badge_active_strengthen_4" : "reward_badge_active_strengthen";
                case cfg.RewardKind.ActiveItemAdjust:
                    return isSuper ? "reward_badge_active_adjust_4" : "reward_badge_active_adjust";
                case cfg.RewardKind.ActiveItemGrant:
                    return "ui_icon_shop_active";
                case cfg.RewardKind.DishChoice:
                    return "ui_icon_shop_food";
                default:
                    return "ui_icon_shop_food";
            }
        }
    }
}
