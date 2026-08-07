using System;

namespace GourmetProject.Game.UI.Battle.States
{
    /// <summary>行动选择态（含事件 n 选一 / 时间轴节点卡，共用中部卡片）：时间轴常驻。</summary>
    internal sealed class ActionSelectState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.ActionSelect;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            host.RebuildActionAxis();
            buildCenter?.Invoke();
        }
    }

    /// <summary>商店态：时间轴常驻。</summary>
    internal sealed class ShopState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.Shop;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            host.RebuildActionAxis();
            host.OpenShopPanel();
        }
    }

    /// <summary>食谱选择态：中部交给 RecipeReadonlyBookView，时间轴隐藏。</summary>
    internal sealed class RecipeSelectionState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.RecipeSelection;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            host.OpenRecipeBookPanel();
        }
    }

    /// <summary>事件页态：时间轴常驻，中部由 EventPagePanel 构建。</summary>
    internal sealed class EventState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.Event;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            host.RebuildActionAxis();
            buildCenter?.Invoke();
        }
    }

    /// <summary>经营挑战态：世界空间餐桌透出，并初始化出菜口等经营挑战控件。</summary>
    internal sealed class FoodState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.Food;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            buildCenter?.Invoke();
            host.BuildBattleControls();
        }
    }

}
