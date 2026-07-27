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
            public virtual string PanelTitle => "查看菜谱";

            public virtual string ExitButtonText => "取消";

            public virtual bool CanClickDish => false;

            public virtual int BookIndexFilter => -1;

            public virtual void Enter(RecipeReadonlyBookView panel)
            {
            }

            public virtual void Exit(RecipeReadonlyBookView panel)
            {
                panel.ClearCompareOverlay();
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
                if (panel.TryBuildRecipeTarget(dish, out ActiveTarget target))
                {
                    panel.ShowShopDeleteConfirm(target);
                }
            }
        }

        private sealed class ReadonlyRecipeBookState : RecipeReadonlyBookState
        {
            private readonly int _bookIndex;

            public ReadonlyRecipeBookState(int bookIndex)
            {
                _bookIndex = bookIndex;
            }

            public override string PanelTitle => "查看菜谱";

            public override string ExitButtonText => "返回";

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

            public override string PanelTitle => _item != null ? _item.Desc : "选择菜品";

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

                panel._stateMachine.Switch(new ActiveRecipeDishCompareState(
                    this,
                    _item,
                    target,
                    _onTargetConfirmed));
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

            public override string PanelTitle => string.IsNullOrWhiteSpace(_title) ? "选择要删除的菜品" : _title;

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

        private sealed class ActiveRecipeDishCompareState : RecipeReadonlyBookState
        {
            private readonly ActiveRecipeDishSelectState _selectState;
            private readonly ItemDefinition _item;
            private readonly ActiveTarget _target;
            private readonly Action<ActiveTarget, Action> _onTargetConfirmed;

            public ActiveRecipeDishCompareState(
                ActiveRecipeDishSelectState selectState,
                ItemDefinition item,
                ActiveTarget target,
                Action<ActiveTarget, Action> onTargetConfirmed)
            {
                _selectState = selectState;
                _item = item;
                _target = target;
                _onTargetConfirmed = onTargetConfirmed;
            }

            public override void Enter(RecipeReadonlyBookView panel)
            {
                panel.ShowActiveItemCompare(
                    _item,
                    _target,
                    () => panel._stateMachine.Switch(_selectState),
                    () =>
                    {
                        panel.ClearCompareOverlay();
                        _onTargetConfirmed?.Invoke(_target, null);
                    });
            }

            public override void Refresh(RecipeReadonlyBookView panel)
            {
                // 对比确认态保持当前预览，避免外部刷新把确认弹层冲掉。
            }

            public override void OnExitClicked(RecipeReadonlyBookView panel)
            {
                panel._stateMachine.Switch(_selectState);
            }
        }
    }
}
