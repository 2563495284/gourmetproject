using System;

namespace GourmetProject.Game.UI.Battle.States
{
    /// <summary>行动选择态（含事件 n 选一 / 行动轴节点卡，共用中部卡片）：行动轴与固定菜谱常驻。</summary>
    internal sealed class ActionSelectState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.ActionSelect;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            host.RebuildActionAxis();
            buildCenter?.Invoke();
        }
    }

    /// <summary>商店态：行动轴常驻，底部展示唯一菜谱。</summary>
    internal sealed class ShopState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.Shop;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            host.RebuildActionAxis();
            host.OpenShopPanel();
        }
    }

    /// <summary>菜谱选择态：中部交给 RecipeReadonlyBookView，行动轴隐藏。</summary>
    internal sealed class RecipeSelectionState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.RecipeSelection;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            host.OpenRecipeBookPanel();
        }
    }

    /// <summary>菜品包发奖态：固定菜谱常驻，中部由 buildCenter 构建菜品包三选一。</summary>
    internal sealed class RewardDishPackState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.RewardDishPack;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            buildCenter?.Invoke();
        }
    }

    /// <summary>道具获得 n 选一态：固定菜谱常驻，中部由道具选择面板构建。</summary>
    internal sealed class RewardItemChoiceState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.RewardItemChoice;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            buildCenter?.Invoke();
        }
    }

    /// <summary>随机化道具结果态：中部展示结果列表，等待玩家继续后播放飞入。</summary>
    internal sealed class RandomizedItemsState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.RandomizedItems;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            buildCenter?.Invoke();
        }
    }

    /// <summary>事件页态：行动轴与固定菜谱常驻，中部由 EventPagePanel 构建。</summary>
    internal sealed class EventState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.Event;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            host.RebuildActionAxis();
            buildCenter?.Invoke();
        }
    }

    /// <summary>只读查看唯一菜谱：中部交给 RecipeReadonlyBookView，固定菜谱同步高亮。</summary>
    internal sealed class RecipeInspectState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.RecipeInspect;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            if (host.RecipeInspectShowsActionAxis)
            {
                host.RebuildActionAxis();
            }

            host.BuildRecipeInspectCards();
            host.OpenRecipeBookPanel();
        }
    }

    /// <summary>美食战斗态：世界空间餐桌透出，固定菜谱展示战斗内容与上餐铃。</summary>
    internal sealed class FoodState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.Food;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            buildCenter?.Invoke();
            host.BuildBattleRecipe();
        }
    }

    /// <summary>餐桌编辑态：世界空间碎片拖拽，中部无标题，固定菜谱常驻。</summary>
    internal sealed class TableEditState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.TableEdit;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            host.SetCenterTitle(string.Empty);
            buildCenter?.Invoke();
        }
    }

    /// <summary>查看餐桌态：只读餐桌视图，中部无标题，固定菜谱常驻。</summary>
    internal sealed class TableViewState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.TableView;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            host.SetCenterTitle(string.Empty);
            buildCenter?.Invoke();
        }
    }
}
