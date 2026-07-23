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
            public virtual string PanelTitle => "编辑菜谱   拖拽移动 / 拖入垃圾桶删除";

            public virtual string ExitButtonText => "取消";

            public virtual bool ShowTrash => false;

            public virtual bool EnableDishDrag => false;

            public virtual bool CanClickDish => false;

            public virtual bool CanDropDishToBook => false;

            public virtual int BookIndexFilter => -1;

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

            public virtual bool OnDishDroppedToBook(RecipeWorkspacePanel panel, RecipeEditDishView dish, int targetBookIndex, int targetDishIndex)
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

            public override bool OnDishDroppedToBook(RecipeWorkspacePanel panel, RecipeEditDishView dish, int targetBookIndex, int targetDishIndex)
            {
                return panel._run != null && targetBookIndex == 0
                    && ShopService.MoveDish(panel._run, dish.DishIndex, targetDishIndex);
            }

            public override bool OnDishDroppedToTrash(RecipeWorkspacePanel panel, RecipeEditDishView dish)
            {
                return panel._run != null && ShopService.DeleteDishAt(panel._run, dish.DishIndex);
            }
        }

        private sealed class ReadonlyRecipeBookState : RecipeWorkspacePanelState
        {
            private readonly int _bookIndex;

            public ReadonlyRecipeBookState(int bookIndex)
            {
                _bookIndex = bookIndex;
            }

            public override string PanelTitle => "查看菜谱";

            public override string ExitButtonText => "返回";

            public override int BookIndexFilter => _bookIndex;

            public override void Enter(RecipeWorkspacePanel panel)
            {
                panel.RebuildBooksForCurrentState();
            }

            public override void OnExitClicked(RecipeWorkspacePanel panel)
            {
                panel._onExit?.Invoke();
            }
        }

        private sealed class ActiveRecipeDishSelectState : RecipeWorkspacePanelState
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

                _onTargetConfirmed?.Invoke(target, null);
            }
        }

        private sealed class EventRecipeDishDeleteState : RecipeWorkspacePanelState
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
                if (!panel.TryBuildRecipeTarget(dish, out ActiveTarget target))
                {
                    return;
                }

                panel.ShowEventDeleteConfirm(_title, target, () => { }, _onTargetConfirmed);
            }
        }

        private sealed class ActiveRecipeDishCompareState : RecipeWorkspacePanelState
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

            public override void Enter(RecipeWorkspacePanel panel)
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
