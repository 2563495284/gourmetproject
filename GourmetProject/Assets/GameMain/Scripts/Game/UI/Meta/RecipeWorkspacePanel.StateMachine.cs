using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.UI.Meta
{
    public sealed partial class RecipeWorkspacePanel
    {
        private sealed class RecipeWorkspacePanelStateMachine
        {
            private readonly RecipeWorkspacePanel _panel;

            public RecipeWorkspacePanelStateMachine(RecipeWorkspacePanel panel)
            {
                _panel = panel;
            }

            public RecipeWorkspacePanelState Current { get; private set; }

            public void Switch(RecipeWorkspacePanelState next)
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

        private abstract class RecipeWorkspacePanelState
        {
            public virtual string ExitButtonText => "取消";

            public virtual bool ShowTrash => false;

            public virtual bool EnableDishDrag => false;

            public virtual bool CanClickDish => false;

            public virtual bool CanDropDishToBook => false;

            public virtual void Enter(RecipeWorkspacePanel panel)
            {
            }

            public virtual void Exit(RecipeWorkspacePanel panel)
            {
                panel.ClearCompareOverlay();
            }

            public virtual void Refresh(RecipeWorkspacePanel panel)
            {
                panel.RebuildBooksForCurrentState();
            }

            public virtual void OnExitClicked(RecipeWorkspacePanel panel)
            {
            }

            public virtual bool OnDishDroppedToBook(RecipeWorkspacePanel panel, RecipeEditDishView dish, int targetBookIndex)
            {
                return false;
            }

            public virtual bool OnDishDroppedToTrash(RecipeWorkspacePanel panel, RecipeEditDishView dish)
            {
                return false;
            }

            public virtual void OnDishClicked(RecipeWorkspacePanel panel, RecipeEditDishView dish)
            {
            }
        }

        private sealed class RecipeEditState : RecipeWorkspacePanelState
        {
            public override string ExitButtonText => "离开编辑";

            public override bool ShowTrash => true;

            public override bool EnableDishDrag => true;

            public override bool CanDropDishToBook => true;

            public override void Enter(RecipeWorkspacePanel panel)
            {
                panel.RebuildBooksForCurrentState();
            }

            public override void OnExitClicked(RecipeWorkspacePanel panel)
            {
                panel._onExit?.Invoke();
            }

            public override bool OnDishDroppedToBook(RecipeWorkspacePanel panel, RecipeEditDishView dish, int targetBookIndex)
            {
                return panel._run != null && ShopService.MoveDish(panel._run, dish.BookIndex, dish.DishIndex, targetBookIndex);
            }

            public override bool OnDishDroppedToTrash(RecipeWorkspacePanel panel, RecipeEditDishView dish)
            {
                return panel._run != null && ShopService.DeleteDishAt(panel._run, dish.BookIndex, dish.DishIndex);
            }
        }

        private sealed class ActiveRecipeDishSelectState : RecipeWorkspacePanelState
        {
            private readonly ItemDefinition _item;
            private readonly Action _onCancel;
            private readonly Action<ActiveTarget> _onTargetConfirmed;

            public ActiveRecipeDishSelectState(ItemDefinition item, Action onCancel, Action<ActiveTarget> onTargetConfirmed)
            {
                _item = item;
                _onCancel = onCancel;
                _onTargetConfirmed = onTargetConfirmed;
            }

            public override bool CanClickDish => true;

            public override void Enter(RecipeWorkspacePanel panel)
            {
                panel.RebuildBooksForCurrentState();
            }

            public override void OnExitClicked(RecipeWorkspacePanel panel)
            {
                _onCancel?.Invoke();
            }

            public override void OnDishClicked(RecipeWorkspacePanel panel, RecipeEditDishView dish)
            {
                if (_item == null || !panel.TryBuildRecipeTarget(dish, out ActiveTarget target))
                {
                    return;
                }

                _onTargetConfirmed?.Invoke(target);
            }
        }

        private sealed class ActiveRecipeDishCompareState : RecipeWorkspacePanelState
        {
            private readonly ActiveRecipeDishSelectState _selectState;
            private readonly ItemDefinition _item;
            private readonly ActiveTarget _target;
            private readonly Action<ActiveTarget> _onTargetConfirmed;

            public ActiveRecipeDishCompareState(
                ActiveRecipeDishSelectState selectState,
                ItemDefinition item,
                ActiveTarget target,
                Action<ActiveTarget> onTargetConfirmed)
            {
                _selectState = selectState;
                _item = item;
                _target = target;
                _onTargetConfirmed = onTargetConfirmed;
            }

            public override void Enter(RecipeWorkspacePanel panel)
            {
                panel.ShowActiveItemCompare(
                    _item,
                    _target,
                    () => panel._stateMachine.Switch(_selectState),
                    () =>
                    {
                        panel.ClearCompareOverlay();
                        _onTargetConfirmed?.Invoke(_target);
                    });
            }

            public override void Refresh(RecipeWorkspacePanel panel)
            {
                // 对比确认态保持当前预览，避免外部刷新把确认弹层冲掉。
            }

            public override void OnExitClicked(RecipeWorkspacePanel panel)
            {
                panel._stateMachine.Switch(_selectState);
            }
        }
    }
}
