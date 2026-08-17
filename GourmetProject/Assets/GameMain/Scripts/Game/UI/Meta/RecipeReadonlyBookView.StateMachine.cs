using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.UI.Meta
{
    public sealed partial class RecipeReadonlyBookView
    {
        private sealed class RecipeReadonlyBookStateMachine
        {
            private readonly RecipeReadonlyBookView _panel;

            public RecipeReadonlyBookStateMachine(RecipeReadonlyBookView panel)
            {
                _panel = panel;
            }

            public RecipeReadonlyBookState Current { get; private set; }

            public void Switch(RecipeReadonlyBookState next)
            {
                Current?.Exit(_panel);
                Current = next;
                Current?.Enter(_panel);
            }

            public void Refresh()
            {
                Current?.Refresh(_panel);
            }

            public void OnExitClicked()
            {
                if (_panel._liveMutationPlaying)
                {
                    return;
                }

                Current?.OnExitClicked(_panel);
            }

            public void Clear()
            {
                Current?.Exit(_panel);
                Current = null;
            }
        }

        private abstract class RecipeReadonlyBookState
        {
            public virtual string PanelTitle => "查看食谱";

            public virtual string ExitButtonText => "取消";

            public virtual bool CanClickDish => false;

            public virtual bool ShowExitButton => true;

            public virtual int BookIndexFilter => -1;

            public virtual void Enter(RecipeReadonlyBookView panel)
            {
            }

            public virtual void Exit(RecipeReadonlyBookView panel)
            {
            }

            public virtual void Refresh(RecipeReadonlyBookView panel)
            {
                panel.RebuildWarehouseForCurrentState();
            }

            public virtual void OnExitClicked(RecipeReadonlyBookView panel)
            {
            }

            public virtual void OnDishClicked(RecipeReadonlyBookView panel, RecipeEditDishView dish)
            {
            }
        }

        private sealed class ShopDeleteDishState : RecipeReadonlyBookState
        {
            private readonly Action _onExit;

            public ShopDeleteDishState(Action onExit)
            {
                _onExit = onExit;
            }

            public override bool CanClickDish => true;

            public override string PanelTitle => "删除食物";

            public override string ExitButtonText => "返回商店";

            public override void Enter(RecipeReadonlyBookView panel)
            {
                panel.RebuildWarehouseForCurrentState();
            }

            public override void OnExitClicked(RecipeReadonlyBookView panel)
            {
                _onExit?.Invoke();
            }

            public override void OnDishClicked(RecipeReadonlyBookView panel, RecipeEditDishView dish)
            {
                if (ShopService.DeleteDishRemaining(panel._run) <= 0)
                {
                    dish?.PlayInteractionFailed();
                    return;
                }

                if (panel.TryBuildRecipeTarget(dish, out ActiveTarget target))
                {
                    panel.ShowShopDeleteConfirm(target);
                }
            }
        }

        private sealed class ReadonlyRecipeBookState : RecipeReadonlyBookState
        {
            private readonly int _bookIndex;
            private readonly bool _hideExitButton;

            public ReadonlyRecipeBookState(int bookIndex, bool hideExitButton = false)
            {
                _bookIndex = bookIndex;
                _hideExitButton = hideExitButton;
            }

            public override string PanelTitle => "查看食谱";

            public override string ExitButtonText => "返回";

            public override bool ShowExitButton => !_hideExitButton;

            public override int BookIndexFilter => _bookIndex;

            public override void Enter(RecipeReadonlyBookView panel)
            {
                panel.RebuildWarehouseForCurrentState();
            }

            public override void OnExitClicked(RecipeReadonlyBookView panel)
            {
                panel._onExit?.Invoke();
            }
        }

        private sealed class ReadonlyDishPoolState : RecipeReadonlyBookState
        {
            private readonly string _title;

            public ReadonlyDishPoolState(string title)
            {
                _title = title;
            }

            public override string PanelTitle =>
                string.IsNullOrWhiteSpace(_title)
                    ? "可能获得的食物"
                    : _title;

            public override bool ShowExitButton => false;

            public override int BookIndexFilter => 0;

            public override void Enter(RecipeReadonlyBookView panel)
            {
                panel.RebuildWarehouseForCurrentState();
            }
        }

        private sealed class ActiveRecipeDishSelectState : RecipeReadonlyBookState
        {
            private readonly ItemDefinition _item;
            private readonly Action _onCancel;
            private readonly Action<ActiveTarget, Action> _onTargetConfirmed;

            public ActiveRecipeDishSelectState(ItemDefinition item, Action onCancel, Action<ActiveTarget, Action> onTargetConfirmed)
            {
                _item = item;
                _onCancel = onCancel;
                _onTargetConfirmed = onTargetConfirmed;
            }

            public override bool CanClickDish => true;

            public override string PanelTitle => _item != null ? _item.Desc : "选择食物";

            public override void Enter(RecipeReadonlyBookView panel)
            {
                panel.RebuildWarehouseForCurrentState();
            }

            public override void OnExitClicked(RecipeReadonlyBookView panel)
            {
                _onCancel?.Invoke();
            }

            public override void OnDishClicked(RecipeReadonlyBookView panel, RecipeEditDishView dish)
            {
                if (_item == null || !panel.TryBuildRecipeTarget(dish, out ActiveTarget target))
                {
                    return;
                }

                panel.ShowActiveItemConfirm(
                    _item,
                    target,
                    _onTargetConfirmed);
            }
        }

        private sealed class EventRecipeDishDeleteState : RecipeReadonlyBookState
        {
            private readonly string _title;
            private readonly Action _onCancel;
            private readonly Action<ActiveTarget> _onTargetConfirmed;

            public EventRecipeDishDeleteState(string title, Action onCancel, Action<ActiveTarget> onTargetConfirmed)
            {
                _title = title;
                _onCancel = onCancel;
                _onTargetConfirmed = onTargetConfirmed;
            }

            public override string ExitButtonText => "返回事件";

            public override bool CanClickDish => true;

            public override string PanelTitle => string.IsNullOrWhiteSpace(_title) ? "选择要删除的食物" : _title;

            public override void Enter(RecipeReadonlyBookView panel)
            {
                panel.RebuildWarehouseForCurrentState();
            }

            public override void OnExitClicked(RecipeReadonlyBookView panel)
            {
                _onCancel?.Invoke();
            }

            public override void OnDishClicked(RecipeReadonlyBookView panel, RecipeEditDishView dish)
            {
                if (!panel.TryBuildRecipeTarget(dish, out ActiveTarget target))
                {
                    return;
                }

                panel.ShowEventDeleteConfirm(_title, target, () => { }, _onTargetConfirmed);
            }
        }

    }
}
