using System;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle.States;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Hud;
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

        string CenterTitle { get; }

        GameObject HudFrame { get; }

        GameObject Backdrop { get; }

        GameObject ActionSelectionPanel { get; }

        ActionCardDeck Deck { get; }

        ShopForm ShopPanel { get; }

        RecipeWorkspacePanel RecipeWorkspacePanel { get; }

        RewardDishPackPanel RewardDishPackPanel { get; }

        RewardItemChoicePanel RewardItemChoicePanel { get; }

        RandomizedItemsPanel RandomizedItemsPanel { get; }

        EventPagePanel EventPagePanel { get; }

        Button BoardEditSkipButton { get; }

        RecipeView RecipeView { get; }

        RecipeBooksPresenter RecipePresenter { get; }

        bool RecipeInspectShowsActionAxis { get; }

        void OnLeavingPage(GameplayView current, GameplayView next);

        void OnBeforeApplyPage(GameplayView view);

        void SetActionAxisVisible(bool visible);

        void SetFoodActionsVisible(bool visible);

        void SetCenterTitle(string text);

        void RebuildActionAxis();

        void OpenShopPanel();

        void OpenRecipeWorkspacePanel();

        void OpenRecipeInspect(int bookIndex);

        void BuildBattleRecipe();

        void BuildRecipeInspectCards();


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

        GameRun IBattleViewHost.Run => _host.Run;

        RecipeBooksPresenter IBattleViewHost.Recipe => _host.RecipePresenter;

        bool IBattleViewHost.RecipeInspectShowsActionAxis => _host.RecipeInspectShowsActionAxis;

        public void SwitchTo(GameplayView next, Action buildCenter = null, Action onShown = null)
        {
            if (_host.Run == null)
            {
                return;
            }

            _host.Deck?.KillPendingShow();
            _host.OnLeavingPage(Current, next);

            Current = next;
            InBattle = next == GameplayView.Food;
            UITransition.FadeSwap(_host.Center, () => _states.Apply(next, buildCenter), onDone: onShown);
        }

        public void HideHud()
        {
            InBattle = false;
            Current = GameplayView.None;

            SetActive(_host.ActionSelectionPanel, false);
            SetActive(_host.ShopPanel, false);
            SetActive(_host.RecipeWorkspacePanel, false);
            SetActive(_host.RewardDishPackPanel, false);
            _host.RewardItemChoicePanel?.Close();
            _host.RandomizedItemsPanel?.Close();
            SetActive(_host.EventPagePanel, false);

            if (_host.BoardEditSkipButton != null)
            {
                _host.BoardEditSkipButton.gameObject.SetActive(false);
            }

            _host.RecipeView?.SetState(RecipeView.RecipeState.Hidden);
            _host.SetFoodActionsVisible(false);
            _host.SetCenterTitle(string.Empty);

            if (_host.HudFrame != null)
            {
                _host.HudFrame.SetActive(false);
            }
        }

        public ActionSelectSnapshot CaptureActionSelection()
        {
            if (Current != GameplayView.ActionSelect)
            {
                return ActionSelectSnapshot.None;
            }

            bool cardsActive = _host.Deck != null && _host.Deck.CardsActive;
            bool skipActive = _host.Deck != null && _host.Deck.SkipActive;
            return new ActionSelectSnapshot(_host.CenterTitle, cardsActive, skipActive);
        }

        public void RestoreActionSelection(ActionSelectSnapshot snapshot)
        {
            if (!snapshot.HasSnapshot)
            {
                _host.SetCenterTitle("选择行动");
                _host.BuildActionCards();
                return;
            }

            _host.SetCenterTitle(string.IsNullOrWhiteSpace(snapshot.Title) ? "选择行动" : snapshot.Title);
            _host.Deck?.SetCardsActive(snapshot.CardsActive);
            _host.Deck?.SetSkipActive(snapshot.SkipActive);
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
            bool recipeEdit = view == GameplayView.RecipeWorkspace;
            bool rewardDishPack = view == GameplayView.RewardDishPack;
            bool rewardItemChoice = view == GameplayView.RewardItemChoice;
            bool randomizedItems = view == GameplayView.RandomizedItems;
            bool eventPage = view == GameplayView.Event;
            bool recipeInspect = view == GameplayView.RecipeInspect;
            bool worldView = view == GameplayView.Food || view == GameplayView.TableEdit || view == GameplayView.TableView;

            SetActive(_host.ActionSelectionPanel, actionSelect);
            SetActive(_host.ShopPanel, shop);
            SetActive(_host.RecipeWorkspacePanel, recipeEdit || recipeInspect);
            SetActive(_host.RewardDishPackPanel, rewardDishPack);
            SetActive(_host.RewardItemChoicePanel, rewardItemChoice);
            SetActive(_host.RandomizedItemsPanel, randomizedItems);
            SetActive(_host.EventPagePanel, eventPage);

            if (_host.BoardEditSkipButton != null)
            {
                _host.BoardEditSkipButton.gameObject.SetActive(view == GameplayView.TableEdit);
            }

            _host.SetActionAxisVisible(actionSelect || shop || eventPage || (recipeInspect && _host.RecipeInspectShowsActionAxis));
            _host.SetFoodActionsVisible(view == GameplayView.Food);

            if (_host.Backdrop != null)
            {
                _host.Backdrop.SetActive(!worldView);
            }

            _host.RecipeView?.SetState(RecipeStateFor(view));
            _host.RefreshPersistent();
        }

        void IBattleViewHost.SetCenterTitle(string text) => _host.SetCenterTitle(text);

        void IBattleViewHost.RebuildActionAxis() => _host.RebuildActionAxis();

        void IBattleViewHost.OpenShopPanel() => _host.OpenShopPanel();

        void IBattleViewHost.OpenRecipeWorkspacePanel() => _host.OpenRecipeWorkspacePanel();

        void IBattleViewHost.OpenRecipeInspect(int bookIndex) => _host.OpenRecipeInspect(bookIndex);

        void IBattleViewHost.BuildBattleRecipe() => _host.BuildBattleRecipe();

        void IBattleViewHost.BuildRecipeInspectCards() => _host.BuildRecipeInspectCards();


        private static RecipeView.RecipeState RecipeStateFor(GameplayView view)
        {
            return view switch
            {
                GameplayView.ActionSelect => RecipeView.RecipeState.Collapsed,
                GameplayView.Shop => RecipeView.RecipeState.Shown,
                GameplayView.RewardDishPack => RecipeView.RecipeState.Hidden,
                GameplayView.RewardItemChoice => RecipeView.RecipeState.Hidden,
                GameplayView.RandomizedItems => RecipeView.RecipeState.Hidden,
                GameplayView.Event => RecipeView.RecipeState.Collapsed,
                GameplayView.RecipeInspect => RecipeView.RecipeState.Shown,
                GameplayView.Food => RecipeView.RecipeState.Shown,
                GameplayView.TableEdit => RecipeView.RecipeState.Collapsed,
                _ => RecipeView.RecipeState.Hidden,
            };
        }

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
