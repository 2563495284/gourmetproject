using System;
using DG.Tweening;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle.States;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Battle.Pages
{
    internal interface IGameplayPageRouterHost
    {
        GameRun Run { get; }

        BattleWorldController World { get; }

        CanvasGroup Center { get; }

        CanvasGroup CenterTransitionCover { get; }

        GameplayTransitionSettings TransitionSettings { get; }

        GameObject HudFrame { get; }

        GameObject Backdrop { get; }

        GameObject ViewTablePanel { get; }

        GameObject ActionSelectionPanel { get; }

        ActionCardDeck Deck { get; }

        ShopForm ShopPanel { get; }

        RecipeReadonlyBookView RecipeReadonlyBookView { get; }

        EventPagePanel EventPagePanel { get; }

        GameObject BoardEditPanel { get; }

        Button BoardEditActionButton { get; }

        bool RecipeInspectShowsActionAxis { get; }

        bool ActionAxisVisible { get; }

        void OnLeavingPage(GameplayView current, GameplayView next);

        void OnPageCovered(GameplayView current, GameplayView next);

        void OnBeforeApplyPage(GameplayView view);

        void SetActionAxisVisible(bool visible);

        void SetFoodBattlePanelVisible(bool visible);

        void RebuildActionAxis();

        void OpenShopPanel();

        void OpenRecipeBookPanel();

        void BuildBattleControls();

        void BuildActionCards();

        void RefreshPersistent();
    }

    internal sealed class GameplayPageRouter : IBattleViewHost
    {
        private readonly IGameplayPageRouterHost _host;
        private readonly GameplayViewStateMachine _states;

        public GameplayPageRouter(IGameplayPageRouterHost host)
        {
            _host = host;
            _states = new GameplayViewStateMachine(this);
        }

        public GameplayView Current { get; private set; } = GameplayView.None;

        public bool InBattle { get; private set; }

        public bool IsTransitioning => _transitionTween != null && _transitionTween.IsActive();

        private Tween _transitionTween;

        GameRun IBattleViewHost.Run => _host.Run;

        bool IBattleViewHost.RecipeInspectShowsActionAxis => _host.RecipeInspectShowsActionAxis;

        public void SwitchTo(GameplayView next, Action buildCenter = null, Action onShown = null)
        {
            if (_host.Run == null)
            {
                return;
            }

            if (IsTransitioning)
            {
                return;
            }

            _host.Deck?.KillPendingShow();
            GameplayView previous = Current;
            bool previousAxisVisible = _host.ActionAxisVisible;
            bool nextAxisVisible = ShowsActionAxis(next, _host.RecipeInspectShowsActionAxis);
            _host.OnLeavingPage(previous, next);

            Current = next;
            InBattle = next == GameplayView.Food;
            GameplayTransitionSettings settings = _host.TransitionSettings ?? new GameplayTransitionSettings();
            bool requiresCover = RequiresCover(previous, next, previousAxisVisible, nextAxisVisible);
            Action swap = () =>
            {
                if (requiresCover)
                {
                    RestoreCenterForCoveredSwap(_host.Center);
                }

                _host.OnPageCovered(previous, next);
                _states.Apply(next, buildCenter);
            };
            Action done = () =>
            {
                _transitionTween = null;
                onShown?.Invoke();
            };

            _transitionTween = requiresCover
                ? UITransition.CoverSwap(
                    _host.CenterTransitionCover,
                    swap,
                    settings.CoverDuration,
                    settings.CoveredHoldDuration,
                    settings.RevealDuration,
                    done)
                : UITransition.FadeSwapStable(
                    _host.Center,
                    swap,
                    settings.CenterFadeOut,
                    settings.CenterFadeIn,
                    done);
        }

        /// <summary>
        /// CoverSwap 在黑幕完全覆盖时交换页面。目标页揭开前强制收敛 Center 的可见/输入状态，
        /// 避免上一个临时子流程留下的软隐藏状态吞掉查看内容和返回按钮。
        /// </summary>
        internal static void RestoreCenterForCoveredSwap(CanvasGroup center)
        {
            if (center == null)
            {
                return;
            }

            center.alpha = 1f;
            center.interactable = true;
            center.blocksRaycasts = true;
        }

        internal static bool RequiresWorldCover(GameplayView current, GameplayView next)
        {
            return IsWorldView(current) || IsWorldView(next);
        }

        internal static bool RequiresCover(
            GameplayView current,
            GameplayView next,
            bool currentAxisVisible,
            bool nextAxisVisible)
        {
            return RequiresWorldCover(current, next) || currentAxisVisible != nextAxisVisible;
        }

        internal static bool ShowsActionAxis(GameplayView view, bool recipeInspectShowsActionAxis)
        {
            return view == GameplayView.ActionSelect
                || view == GameplayView.Shop
                || view == GameplayView.Event
                || (view == GameplayView.RecipeInspect && recipeInspectShowsActionAxis);
        }

        internal static bool IsWorldView(GameplayView view)
        {
            return view == GameplayView.Food
                || view == GameplayView.TableEdit
                || view == GameplayView.TableView;
        }

        public void HideHud()
        {
            CancelTransition();
            InBattle = false;
            Current = GameplayView.None;

            SetActive(_host.ActionSelectionPanel, false);
            SetActive(_host.ShopPanel, false);
            SetActive(_host.RecipeReadonlyBookView, false);
            SetActive(_host.EventPagePanel, false);
            SetActive(_host.ViewTablePanel, false);

            SetActive(_host.BoardEditPanel, false);

            _host.SetFoodBattlePanelVisible(false);

            if (_host.HudFrame != null)
            {
                _host.HudFrame.SetActive(false);
            }
        }

        private void CancelTransition()
        {
            if (_transitionTween != null && _transitionTween.IsActive())
            {
                _transitionTween.Kill(complete: false);
            }

            _transitionTween = null;
        }

        public ActionSelectSnapshot CaptureActionSelection()
        {
            if (Current != GameplayView.ActionSelect)
            {
                return ActionSelectSnapshot.None;
            }

            bool cardsActive = _host.Deck != null && _host.Deck.CardsActive;
            return new ActionSelectSnapshot(cardsActive);
        }

        public void RestoreActionSelection(ActionSelectSnapshot snapshot)
        {
            if (!snapshot.HasSnapshot)
            {
                _host.BuildActionCards();
                return;
            }

            _host.Deck?.SetCardsActive(snapshot.CardsActive);
        }

        void IBattleViewHost.ApplyShellForView(GameplayView view)
        {
            if (_host.Run == null)
            {
                return;
            }

            _host.OnBeforeApplyPage(view);

            if (_host.HudFrame != null)
            {
                _host.HudFrame.SetActive(true);
            }

            bool actionSelect = view == GameplayView.ActionSelect;
            bool shop = view == GameplayView.Shop;
            bool recipeSelection = view == GameplayView.RecipeSelection;
            bool eventPage = view == GameplayView.Event;
            bool recipeInspect = view == GameplayView.RecipeInspect;
            bool tableView = view == GameplayView.TableView;
            bool worldView = view == GameplayView.Food || view == GameplayView.TableEdit || view == GameplayView.TableView;

            SetActive(_host.ActionSelectionPanel, actionSelect);
            SetActive(_host.ShopPanel, shop);
            SetActive(_host.RecipeReadonlyBookView, recipeSelection || recipeInspect);
            SetActive(_host.EventPagePanel, eventPage);
            SetActive(_host.ViewTablePanel, tableView);

            SetActive(_host.BoardEditPanel, view == GameplayView.TableEdit);

            _host.SetActionAxisVisible(actionSelect || shop || eventPage || (recipeInspect && _host.RecipeInspectShowsActionAxis));
            _host.SetFoodBattlePanelVisible(view == GameplayView.Food);

            if (_host.Backdrop != null)
            {
                _host.Backdrop.SetActive(!worldView);
            }

            _host.RefreshPersistent();
        }

        void IBattleViewHost.RebuildActionAxis() => _host.RebuildActionAxis();

        void IBattleViewHost.OpenShopPanel() => _host.OpenShopPanel();

        void IBattleViewHost.OpenRecipeBookPanel() => _host.OpenRecipeBookPanel();

        void IBattleViewHost.BuildBattleControls() => _host.BuildBattleControls();

        private static void SetActive(Component component, bool active)
        {
            if (component != null)
            {
                component.gameObject.SetActive(active);
            }
        }

        private static void SetActive(GameObject gameObject, bool active)
        {
            if (gameObject != null)
            {
                gameObject.SetActive(active);
            }
        }
    }
}
