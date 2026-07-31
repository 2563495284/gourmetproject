namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 原子行动的展示分类：UI（卡片、行动轴图标、Tip）用它挑图标/文案。
    /// 与执行无关，只是把 behavior（+ Boss 难度）折成一个展示口径，替代旧的 TimelineNodeType。
    /// </summary>
    public enum ActionDisplayKind
    {
        Food,
        Boss,
        Event,
        Reward,
        Negative,
        Shop,
        Interest,
        Slot,
    }

    public static class ActionDisplay
    {
        public static ActionDisplayKind KindOf(cfg.GameAction action)
        {
            return KindOf(null, action);
        }

        public static ActionDisplayKind KindOf(cfg.Tables tables, cfg.GameAction action)
        {
            if (action == null)
            {
                return ActionDisplayKind.Event;
            }

            switch (action.Behavior)
            {
                case cfg.ActionBehavior.Food:
                    return FoodService.IsBossAction(tables, action)
                        ? ActionDisplayKind.Boss
                        : ActionDisplayKind.Food;
                case cfg.ActionBehavior.Reward:
                    return ActionDisplayKind.Reward;
                case cfg.ActionBehavior.Negative:
                    return ActionDisplayKind.Negative;
                case cfg.ActionBehavior.Shop:
                    return ActionDisplayKind.Shop;
                case cfg.ActionBehavior.Interest:
                    return ActionDisplayKind.Interest;
                case cfg.ActionBehavior.Slot:
                    return ActionDisplayKind.Slot;
                case cfg.ActionBehavior.Effect:
                    return ActionDisplayKind.Negative;
                case cfg.ActionBehavior.Event:
                default:
                    return ActionDisplayKind.Event;
            }
        }
    }
}
