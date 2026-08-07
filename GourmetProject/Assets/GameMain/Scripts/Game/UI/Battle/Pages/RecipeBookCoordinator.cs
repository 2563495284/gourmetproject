using System;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Battle;

namespace GourmetProject.Game.UI.Battle.Pages
{
    /// <summary>
    /// 食谱操作页宿主。只读查看已迁移到 BattleInspectionCoordinator；这里仅保留
    /// 删除食物和主动道具选菜等会改变运行状态的工作流。
    /// </summary>
    internal interface IRecipeBookHost
    {
        GameRun Run { get; }

        GameplayView CurrentView { get; }

        RecipeReadonlyBookView RecipeReadonlyBookView { get; }

        void SwitchTo(GameplayView view, Action buildCenter = null, Action onShown = null);

        void RefreshShopPersistent();

        FoodTipsView FoodTips();
    }

    internal sealed class RecipeBookCoordinator
    {
        private readonly IRecipeBookHost _host;
        private ItemDefinition _activeItemTargetItem;
        private GameplayView _activeItemReturnView = GameplayView.None;
        private Action _activeItemTargetCancel;
        private Action<ActiveTarget, Action> _activeItemTargetConfirmed;
        private bool _shopDeleteRequested;
        private string _eventDeleteTitle;
        private Action _eventDeleteCancel;
        private Action<ActiveTarget> _eventDeleteConfirmed;
        private Action _eventDeleteChanged;

        public RecipeBookCoordinator(IRecipeBookHost host)
        {
            _host = host;
        }

        public void OpenPanel()
        {
            RecipeReadonlyBookView panel = _host.RecipeReadonlyBookView;
            if (panel == null)
            {
                return;
            }

            if (_activeItemTargetItem != null)
            {
                panel.Open(
                    _host.Run,
                    RecipeReadonlyBookRequest.ActiveItemTarget(
                        _activeItemTargetItem,
                        CancelActiveItemTarget,
                        ConfirmActiveItemTarget,
                        _host.RefreshShopPersistent),
                    _host.FoodTips);
                return;
            }

            if (_eventDeleteConfirmed != null)
            {
                panel.Open(
                    _host.Run,
                    RecipeReadonlyBookRequest.EventDeleteDish(
                        _eventDeleteTitle,
                        CancelEventDelete,
                        ConfirmEventDelete,
                        _eventDeleteChanged ?? _host.RefreshShopPersistent),
                    _host.FoodTips);
                return;
            }

            if (_shopDeleteRequested)
            {
                panel.Open(
                    _host.Run,
                    RecipeReadonlyBookRequest.ShopDeleteDish(
                        CloseShopDelete,
                        _host.RefreshShopPersistent),
                    _host.FoodTips);
            }
        }

        public void OpenShopDelete()
        {
            ClearActiveItemTargetRequest();
            ClearEventDeleteRequest();
            _shopDeleteRequested = true;
            _host.SwitchTo(GameplayView.RecipeSelection);
        }

        private void CloseShopDelete()
        {
            _shopDeleteRequested = false;
            _host.SwitchTo(GameplayView.Shop);
        }

        public void OpenActiveItemTarget(
            ItemDefinition item,
            Action onCancel,
            Action<ActiveTarget, Action> onTargetConfirmed,
            Action onOpened = null)
        {
            if (item == null)
            {
                onOpened?.Invoke();
                return;
            }

            _activeItemTargetItem = item;
            _shopDeleteRequested = false;
            _activeItemReturnView = _host.CurrentView;
            _activeItemTargetCancel = onCancel;
            _activeItemTargetConfirmed = onTargetConfirmed;
            _host.SwitchTo(GameplayView.RecipeSelection, onShown: onOpened);
        }

        public void CancelActiveItemTarget()
        {
            if (_activeItemTargetItem == null)
            {
                return;
            }

            GameplayView returnView = _activeItemReturnView;
            Action onCancel = _activeItemTargetCancel;
            ClearActiveItemTargetRequest();
            onCancel?.Invoke();
            RestoreActiveItemReturnView(returnView);
        }

        public void ConfirmActiveItemTarget(ActiveTarget target, Action onDone = null)
        {
            if (_activeItemTargetItem == null)
            {
                onDone?.Invoke();
                return;
            }

            GameplayView returnView = _activeItemReturnView;
            Action<ActiveTarget, Action> onConfirmed = _activeItemTargetConfirmed;
            ClearActiveItemTargetRequest();
            if (onConfirmed == null)
            {
                RestoreActiveItemReturnView(returnView);
                onDone?.Invoke();
                return;
            }

            bool completed = false;
            onConfirmed.Invoke(target, Finish);

            void Finish()
            {
                if (completed)
                {
                    return;
                }

                completed = true;
                RestoreActiveItemReturnView(returnView);
                onDone?.Invoke();
            }
        }

        public void OpenEventDelete(
            GameRun run,
            string title,
            Action onCancel,
            Action<ActiveTarget> onTargetConfirmed,
            Action onChanged)
        {
            _eventDeleteTitle = title;
            _shopDeleteRequested = false;
            _eventDeleteCancel = onCancel;
            _eventDeleteConfirmed = onTargetConfirmed;
            _eventDeleteChanged = onChanged;
            _host.SwitchTo(GameplayView.RecipeSelection);
        }

        public void RefreshPanel()
        {
            _host.RecipeReadonlyBookView?.Refresh();
        }

        public bool PlayActiveItemRecipeFlavorApplied(ActiveTarget target, Action onComplete)
        {
            return _host.CurrentView == GameplayView.RecipeSelection
                && _host.RecipeReadonlyBookView != null
                && _host.RecipeReadonlyBookView.PlayActiveItemRecipeFlavorApplied(target, onComplete);
        }

        private void RestoreActiveItemReturnView(GameplayView returnView)
        {
            switch (returnView)
            {
                case GameplayView.ActionSelect:
                case GameplayView.Shop:
                case GameplayView.Food:
                    _host.SwitchTo(returnView);
                    break;
                default:
                    _host.SwitchTo(GameplayView.Shop);
                    break;
            }
        }

        private void CancelEventDelete()
        {
            Action onCancel = _eventDeleteCancel;
            ClearEventDeleteRequest();
            onCancel?.Invoke();
        }

        private void ConfirmEventDelete(ActiveTarget target)
        {
            Action<ActiveTarget> onConfirmed = _eventDeleteConfirmed;
            ClearEventDeleteRequest();
            onConfirmed?.Invoke(target);
        }

        private void ClearActiveItemTargetRequest()
        {
            _activeItemTargetItem = null;
            _activeItemReturnView = GameplayView.None;
            _activeItemTargetCancel = null;
            _activeItemTargetConfirmed = null;
        }

        private void ClearEventDeleteRequest()
        {
            _eventDeleteTitle = null;
            _eventDeleteCancel = null;
            _eventDeleteConfirmed = null;
            _eventDeleteChanged = null;
        }
    }
}
