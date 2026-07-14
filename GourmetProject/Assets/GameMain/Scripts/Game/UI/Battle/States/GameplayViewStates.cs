using System;

namespace GourmetProject.Game.UI.Battle.States
{
    /// <summary>行动选择态（含事件 n 选一 / 行动轴节点卡，共用中部卡片）：行动轴常驻，菜谱抽屉折叠。</summary>
    internal sealed class ActionSelectState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.ActionSelect;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            host.RebuildActionAxis();
            host.Recipe.BuildPersistent(host.Run, showAdd: false, onAdd: null);
            buildCenter?.Invoke();
        }
    }

    /// <summary>商店态：行动轴常驻，菜谱抽屉可购买空菜谱本。</summary>
    internal sealed class ShopState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.Shop;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            host.RebuildActionAxis();
            host.Recipe.BuildShop(host.Run, host.BuyRecipeBook);
            host.OpenShopPanel();
        }
    }

    /// <summary>编辑菜谱态：中部交给 RecipeWorkspacePanel，行动轴隐藏。</summary>
    internal sealed class RecipeEditState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.RecipeEdit;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            host.OpenRecipeWorkspacePanel();
        }
    }

    /// <summary>菜品包发奖态：菜谱抽屉展示，中部由 buildCenter 构建菜品包三选一。</summary>
    internal sealed class RewardDishPackState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.RewardDishPack;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            host.Recipe.BuildPersistent(host.Run, showAdd: false, onAdd: null);
            buildCenter?.Invoke();
        }
    }

    /// <summary>道具获得 n 选一态：中部由道具选择面板构建，底部菜谱隐藏。</summary>
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

    /// <summary>美食战斗态：世界空间餐桌透出，菜谱抽屉切成上菜条。</summary>
    internal sealed class FoodState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.Food;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            buildCenter?.Invoke();
            host.BuildBattleRecipe();
        }
    }

    /// <summary>餐桌编辑态：世界空间碎片拖拽，中部无标题，菜谱抽屉移除购买卡。</summary>
    internal sealed class TableEditState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.TableEdit;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            host.SetCenterTitle(string.Empty);
            host.Recipe.RemoveAddCard();
            buildCenter?.Invoke();
        }
    }

    /// <summary>查看餐桌态：只读餐桌视图，中部无标题，菜谱抽屉移除购买卡。</summary>
    internal sealed class TableViewState : IGameplayViewState
    {
        public GameplayView Kind => GameplayView.TableView;

        public void Enter(IBattleViewHost host, Action buildCenter)
        {
            host.SetCenterTitle(string.Empty);
            host.Recipe.RemoveAddCard();
            buildCenter?.Invoke();
        }
    }
}
