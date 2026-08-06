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

    /// <summary>食物包发奖态：中部由 buildCenter 构建食物包三选一。</summary>
    internal sealed class RewardDishPackState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.RewardDishPack;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            buildCenter?.Invoke();
        }
    }

    /// <summary>装饰品和消耗品获得 n 选一态：中部由装饰品和消耗品选择面板构建。</summary>
    internal sealed class RewardItemChoiceState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.RewardItemChoice;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            buildCenter?.Invoke();
        }
    }

    /// <summary>随机化装饰品和消耗品结果态：中部展示结果列表，等待玩家继续后播放飞入。</summary>
    internal sealed class RandomizedItemsState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.RandomizedItems;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            buildCenter?.Invoke();
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

    /// <summary>只读查看唯一食谱：中部交给 RecipeReadonlyBookView。</summary>
    internal sealed class RecipeInspectState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.RecipeInspect;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            buildCenter?.Invoke();
            if (host.RecipeInspectShowsActionAxis)
            {
                host.RebuildActionAxis();
            }

            host.OpenRecipeBookPanel();
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

    /// <summary>餐桌编辑态：世界空间碎片拖拽，中部无标题。</summary>
    internal sealed class TableEditState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.TableEdit;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            buildCenter?.Invoke();
        }
    }

    /// <summary>查看餐桌态：只读餐桌视图，中部无标题。</summary>
    internal sealed class TableViewState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.TableView;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            buildCenter?.Invoke();
        }
    }
}
