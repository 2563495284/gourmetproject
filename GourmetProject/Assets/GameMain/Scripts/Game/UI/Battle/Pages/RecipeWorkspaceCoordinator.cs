using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Battle;
using UnityEngine;

namespace GourmetProject.Game.UI.Battle.Pages
{
    internal interface IRecipeWorkspaceHost
    {
        GameRun Run { get; }

        BattleSession Session { get; }

        GameplayView CurrentView { get; }

        RecipeWorkspacePanel RecipeWorkspacePanel { get; }

        void SwitchTo(GameplayView view, Action buildCenter = null, Action onShown = null);

        void SetCenterTitle(string text);

        void RefreshPersistent();

        void RefreshShopPersistent();

        ActionSelectSnapshot CaptureActionSelection();

        void RestoreActionSelection(ActionSelectSnapshot snapshot);

        void ShowActionSelection();

        void PlayShowCardsWhenReady();

        FoodTipsView FoodTips();
    }

    internal sealed class RecipeWorkspaceCoordinator
    {
        private readonly IRecipeWorkspaceHost _host;
        private ItemDefinition _activeItemTargetItem;
        private GameplayView _activeItemReturnView = GameplayView.None;
        private Action _activeItemTargetCancel;
        private Action<ActiveTarget, Action> _activeItemTargetConfirmed;
        private string _eventDeleteTitle;
        private Action _eventDeleteCancel;
        private Action<ActiveTarget> _eventDeleteConfirmed;
        private Action _eventDeleteChanged;
        private int _inspectBookIndex = -1;
        private GameplayView _inspectReturnView = GameplayView.None;
        private ActionSelectSnapshot _inspectActionSnapshot;
        private bool _inspectShowsActionAxis;
        private bool _inspectUsesBattleRecipe;

        public RecipeWorkspaceCoordinator(IRecipeWorkspaceHost host)
        {
            _host = host;
        }

        public bool InspectShowsActionAxis => _inspectBookIndex >= 0 && _inspectShowsActionAxis;

        public bool InspectUsesBattleRecipe => _inspectBookIndex >= 0 && _inspectUsesBattleRecipe;

        public int InspectBookIndex => _inspectBookIndex;

        public void OnLeavingPage(GameplayView current, GameplayView next)
        {
            if (current == GameplayView.RecipeInspect && next != GameplayView.RecipeInspect)
            {
                ClearInspectRequest();
            }
        }

        public void OpenPanel()
        {
            RecipeWorkspacePanel panel = _host.RecipeWorkspacePanel;
            if (panel == null)
            {
                return;
            }

            if (_activeItemTargetItem != null)
            {
                _host.SetCenterTitle(_activeItemTargetItem.Desc);
                panel.Open(
                    _host.Run,
                    RecipeWorkspaceRequest.ActiveItemTarget(
                        _activeItemTargetItem,
                        CancelActiveItemTarget,
                        ConfirmActiveItemTarget,
                        _host.RefreshShopPersistent),
                    _host.FoodTips);
                return;
            }

            if (_inspectBookIndex >= 0)
            {
                int bookIndex = _inspectBookIndex;
                _host.SetCenterTitle("查看菜谱");
                panel.Open(
                    _host.Run,
                    RecipeWorkspaceRequest.ReadonlyBook(
                        bookIndex,
                        CloseInspect,
                        _host.RefreshPersistent,
                        _inspectUsesBattleRecipe ? BuildBattleReadonlyEntries(bookIndex) : null),
                    _host.FoodTips);
                return;
            }

            if (_eventDeleteConfirmed != null)
            {
                _host.SetCenterTitle(string.IsNullOrWhiteSpace(_eventDeleteTitle) ? "选择要删除的菜品" : _eventDeleteTitle);
                panel.Open(
                    _host.Run,
                    RecipeWorkspaceRequest.EventDeleteDish(
                        _eventDeleteTitle,
                        CancelEventDelete,
                        ConfirmEventDelete,
                        _eventDeleteChanged ?? _host.RefreshShopPersistent),
                    _host.FoodTips);
                return;
            }

            panel.Open(
                _host.Run,
                RecipeWorkspaceRequest.Edit(OpenShopFromEdit, _host.RefreshShopPersistent),
                _host.FoodTips);
        }

        public void OpenEdit()
        {
            ClearInspectRequest();
            ClearActiveItemTargetRequest();
            ClearEventDeleteRequest();
            _host.SwitchTo(GameplayView.RecipeWorkspace);
        }

        public void OpenShopFromEdit()
        {
            _host.SwitchTo(GameplayView.Shop);
        }

        public void OpenInspect(int bookIndex)
        {
            GameRun run = _host.Run;
            if (run == null || bookIndex != 0)
            {
                return;
            }

            if (_host.CurrentView == GameplayView.RecipeInspect && _inspectBookIndex == bookIndex)
            {
                CloseInspect();
                return;
            }

            _inspectBookIndex = bookIndex;
            bool alreadyInspecting = _host.CurrentView == GameplayView.RecipeInspect;
            _inspectReturnView = alreadyInspecting ? _inspectReturnView : _host.CurrentView;
            if (_host.CurrentView != GameplayView.RecipeInspect)
            {
                _inspectActionSnapshot = _host.CurrentView == GameplayView.ActionSelect
                    ? _host.CaptureActionSelection()
                    : ActionSelectSnapshot.None;
                _inspectShowsActionAxis = ShouldShowActionAxis(_host.CurrentView);
                _inspectUsesBattleRecipe = _host.CurrentView == GameplayView.Food;
            }

            _host.SwitchTo(GameplayView.RecipeInspect);
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
            _activeItemReturnView = _host.CurrentView;
            _activeItemTargetCancel = onCancel;
            _activeItemTargetConfirmed = onTargetConfirmed;
            ClearInspectRequest();
            _host.SwitchTo(GameplayView.RecipeWorkspace, onShown: onOpened);
        }

        public bool TryPointerActiveItemTarget(Vector2 screenPoint, out ActiveTarget target)
        {
            target = default;
            return _host.CurrentView == GameplayView.RecipeWorkspace
                && _activeItemTargetItem != null
                && _host.RecipeWorkspacePanel != null
                && _host.RecipeWorkspacePanel.TryPointerRecipeDishTarget(screenPoint, out target);
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
            _eventDeleteCancel = onCancel;
            _eventDeleteConfirmed = onTargetConfirmed;
            _eventDeleteChanged = onChanged;
            ClearInspectRequest();
            _host.SwitchTo(GameplayView.RecipeWorkspace);
        }

        public void RefreshPanel()
        {
            _host.RecipeWorkspacePanel?.Refresh();
        }

        public bool PlayActiveItemRecipeFlavorApplied(ActiveTarget target, Action onComplete)
        {
            return _host.CurrentView == GameplayView.RecipeWorkspace
                && _host.RecipeWorkspacePanel != null
                && _host.RecipeWorkspacePanel.PlayActiveItemRecipeFlavorApplied(target, onComplete);
        }

        private void CloseInspect()
        {
            GameplayView returnView = _inspectReturnView;
            ActionSelectSnapshot actionSnapshot = _inspectActionSnapshot;
            bool fromBattleRecipe = _inspectUsesBattleRecipe;
            ClearInspectRequest();
            RestoreInspectReturnView(returnView, actionSnapshot, fromBattleRecipe);
        }

        private void RestoreInspectReturnView(GameplayView returnView, ActionSelectSnapshot actionSnapshot, bool fromBattleRecipe)
        {
            switch (returnView)
            {
                case GameplayView.ActionSelect:
                    _host.SwitchTo(
                        GameplayView.ActionSelect,
                        () => _host.RestoreActionSelection(actionSnapshot),
                        _host.PlayShowCardsWhenReady);
                    break;
                case GameplayView.Shop:
                case GameplayView.Event:
                case GameplayView.RewardDishPack:
                case GameplayView.RewardItemChoice:
                case GameplayView.RandomizedItems:
                case GameplayView.Food:
                case GameplayView.TableEdit:
                case GameplayView.TableView:
                    _host.SwitchTo(returnView);
                    break;
                default:
                    if (fromBattleRecipe)
                    {
                        _host.SwitchTo(GameplayView.Food);
                    }
                    else
                    {
                        _host.ShowActionSelection();
                    }
                    break;
            }
        }

        private void RestoreActiveItemReturnView(GameplayView returnView)
        {
            switch (returnView)
            {
                case GameplayView.ActionSelect:
                case GameplayView.Shop:
                case GameplayView.Food:
                case GameplayView.TableEdit:
                case GameplayView.TableView:
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

        private void ClearInspectRequest()
        {
            _inspectBookIndex = -1;
            _inspectReturnView = GameplayView.None;
            _inspectActionSnapshot = ActionSelectSnapshot.None;
            _inspectShowsActionAxis = false;
            _inspectUsesBattleRecipe = false;
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

        private IReadOnlyList<RecipeBookSlot> BuildBattleReadonlyEntries(int bookIndex)
        {
            BattleSession session = _host.Session;
            if (session == null || bookIndex < 0 || bookIndex >= session.Slots.Count)
            {
                return null;
            }

            IReadOnlyList<RecipeSlotEntry> source = session.Slots[bookIndex].Entries;
            var entries = new List<RecipeBookSlot>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                RecipeSlotEntry entry = source[i];
                if (entry == null)
                {
                    continue;
                }

                var slot = new RecipeBookSlot(entry.DishId);
                AddRange(value => slot.AddFlavor(value), entry.ExtraFlavorIds);
                AddRange(value => slot.AddExtraSkill(value), entry.ExtraSkillIds);
                slot.RestoreScoreFlatBonus(entry.ScoreFlatBonus);
                slot.RestoreScoreMultiplier(entry.ScoreMultiplier);
                entries.Add(slot);
            }

            return entries;
        }

        private static bool ShouldShowActionAxis(GameplayView view)
        {
            return view == GameplayView.ActionSelect
                || view == GameplayView.Shop
                || view == GameplayView.Event;
        }

        private static void AddRange(Action<string> add, IReadOnlyList<string> values)
        {
            if (add == null || values == null)
            {
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                add(values[i]);
            }
        }
    }
}
