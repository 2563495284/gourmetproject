using System;
using System.Collections.Generic;
using GourmetProject.Game;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 战斗胜利后的菜品三选一领奖页。上方候选菜品通过箭头拖到下方 RecipeView 的某本菜谱。
    /// </summary>
    public sealed class RewardDishPackPanel : MonoBehaviour
    {
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private Text _promptText;
        [SerializeField] private RectTransform _choiceContainer;
        [SerializeField] private RewardDishChoiceCardView _cardTemplate;
        [SerializeField] private Button _skipButton;
        [SerializeField] private TargetArrowView _targetArrowPrefab;

        private readonly List<RewardDishChoiceCardView> _spawnedCards = new();
        private readonly List<RewardChoice> _choices = new();
        private GameRun _run;
        private RecipeView _recipeView;
        private Func<int, int, bool> _onChoiceDropped;
        private Action _onSkip;
        private TargetArrowView _activeArrow;
        private RewardDishChoiceCardView _targetingCard;
        private int _targetingChoiceIndex = -1;
        private bool _waitingForRecipeClick;
        private int _targetingFrame;
        private bool _wired;

        private void Awake()
        {
            EnsureWired();
        }

        private void OnDisable()
        {
            CancelDishTargeting(restoreRecipeState: false);
            ClearCards();
        }

        private void Update()
        {
            if (_activeArrow == null || Mouse.current == null)
            {
                return;
            }

            Vector2 pointer = Mouse.current.position.ReadValue();
            int hovered = TryGetRecipeBookAt(pointer, out int bookIndex) ? bookIndex : -1;
            _recipeView?.SetDishTargetingHighlights(true, hovered);

            if (Mouse.current.rightButton.wasPressedThisFrame
                || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
            {
                CancelDishTargeting();
                return;
            }

            if (!_waitingForRecipeClick || Time.frameCount <= _targetingFrame || !Mouse.current.leftButton.wasPressedThisFrame)
            {
                return;
            }

            if (hovered >= 0)
            {
                CompleteDishTargeting(hovered);
            }
            else
            {
                CancelDishTargeting();
            }
        }

        public void Open(
            GameRun run,
            IReadOnlyList<RewardChoice> choices,
            RecipeView recipeView,
            Func<int, int, bool> onChoiceDropped,
            Action onSkip)
        {
            EnsureWired();
            CancelDishTargeting(restoreRecipeState: false);
            ClearCards();

            _run = run;
            _recipeView = recipeView;
            _onChoiceDropped = onChoiceDropped;
            _onSkip = onSkip;
            _choices.Clear();

            if (choices != null)
            {
                for (int i = 0; i < choices.Count; i++)
                {
                    RewardChoice choice = choices[i];
                    if (choice != null && choice.Kind == cfg.RewardKind.DishChoice)
                    {
                        _choices.Add(choice);
                    }
                }
            }

            if (_panelRoot != null)
            {
                _panelRoot.SetActive(true);
            }

            if (_promptText != null)
            {
                _promptText.text = "拖拽一个菜品到下方菜谱中，或点击跳过。";
            }

            BuildCards();
            _recipeView?.SetState(RecipeView.RecipeState.Shown);
            _recipeView?.SetDishTargetingHighlights(false, -1);
        }

        private void EnsureWired()
        {
            if (_wired)
            {
                return;
            }

            _wired = true;
            if (_skipButton != null)
            {
                _skipButton.onClick.RemoveAllListeners();
                _skipButton.onClick.AddListener(OnSkipClicked);
            }
        }

        private void BuildCards()
        {
            if (_cardTemplate == null || _choiceContainer == null)
            {
                return;
            }

            _cardTemplate.gameObject.SetActive(false);
            for (int i = 0; i < _choices.Count; i++)
            {
                int index = i;
                RewardChoice choice = _choices[i];
                RewardDishChoiceCardView card = Instantiate(_cardTemplate, _choiceContainer);
                card.gameObject.name = $"RewardDishChoice_{index + 1}";
                card.gameObject.SetActive(true);
                card.Bind(choice, LoadDishIcon(choice.Id), index, BeginDishTargeting, EndDishTargeting);
                _spawnedCards.Add(card);
            }
        }

        private void ClearCards()
        {
            for (int i = 0; i < _spawnedCards.Count; i++)
            {
                if (_spawnedCards[i] != null)
                {
                    Destroy(_spawnedCards[i].gameObject);
                }
            }

            _spawnedCards.Clear();
        }

        private void BeginDishTargeting(RewardDishChoiceCardView card, int choiceIndex)
        {
            if (_run == null || card == null || choiceIndex < 0 || choiceIndex >= _choices.Count)
            {
                return;
            }

            CancelDishTargeting();
            _targetingChoiceIndex = choiceIndex;
            _targetingCard = card;
            _waitingForRecipeClick = false;
            _targetingFrame = Time.frameCount;

            _recipeView?.SetState(RecipeView.RecipeState.Shown);
            _activeArrow = CreateTargetArrow(card.IconScreenCenter());
            UpdateTargetingHighlight(Mouse.current != null ? Mouse.current.position.ReadValue() : card.IconScreenCenter());
        }

        private void EndDishTargeting(RewardDishChoiceCardView card, int choiceIndex, Vector2 screenPoint)
        {
            if (_activeArrow == null || _targetingCard != card || _targetingChoiceIndex != choiceIndex)
            {
                return;
            }

            if (TryGetRecipeBookAt(screenPoint, out int bookIndex))
            {
                CompleteDishTargeting(bookIndex);
                return;
            }

            if (card.ContainsScreenPoint(screenPoint))
            {
                _waitingForRecipeClick = true;
                _targetingFrame = Time.frameCount;
                return;
            }

            CancelDishTargeting();
        }

        private void CompleteDishTargeting(int bookIndex)
        {
            int choiceIndex = _targetingChoiceIndex;
            RewardDishChoiceCardView card = _targetingCard;
            if (choiceIndex < 0 || choiceIndex >= _choices.Count || _onChoiceDropped == null)
            {
                CancelDishTargeting();
                return;
            }

            if (!_onChoiceDropped.Invoke(choiceIndex, bookIndex))
            {
                card?.PlayTargetFailed();
                CancelDishTargeting();
                return;
            }

            card?.SetResolved(true);
            CancelDishTargeting(restoreRecipeState: false);
        }

        private void OnSkipClicked()
        {
            CancelDishTargeting();
            _onSkip?.Invoke();
        }

        private TargetArrowView CreateTargetArrow(Vector2 startScreenPoint)
        {
            Canvas canvas = _recipeView != null ? _recipeView.GetComponentInParent<Canvas>() : GetComponentInParent<Canvas>();
            Transform parent = canvas != null ? canvas.transform : transform;
            TargetArrowView arrow = _targetArrowPrefab != null
                ? Instantiate(_targetArrowPrefab, parent)
                : new GameObject("TargetArrowView", typeof(RectTransform), typeof(TargetArrowView)).GetComponent<TargetArrowView>();
            if (arrow.transform.parent == null)
            {
                arrow.transform.SetParent(parent, false);
            }

            arrow.transform.SetAsLastSibling();
            arrow.SetupArrow(startScreenPoint);
            return arrow;
        }

        private bool TryGetRecipeBookAt(Vector2 screenPoint, out int bookIndex)
        {
            if (_recipeView != null && _recipeView.TryGetRecipeBookAtScreenPoint(screenPoint, out bookIndex))
            {
                return true;
            }

            bookIndex = -1;
            return false;
        }

        private void UpdateTargetingHighlight(Vector2 screenPoint)
        {
            int hovered = TryGetRecipeBookAt(screenPoint, out int bookIndex) ? bookIndex : -1;
            _recipeView?.SetDishTargetingHighlights(true, hovered);
        }

        private void CancelDishTargeting(bool restoreRecipeState = true)
        {
            if (_activeArrow != null)
            {
                Destroy(_activeArrow.gameObject);
                _activeArrow = null;
            }

            _recipeView?.SetDishTargetingHighlights(false, -1);
            if (restoreRecipeState)
            {
                _recipeView?.SetState(RecipeView.RecipeState.Shown);
            }

            _targetingCard = null;
            _targetingChoiceIndex = -1;
            _waitingForRecipeClick = false;
            _targetingFrame = -1;
        }

        private Sprite LoadDishIcon(string dishId)
        {
            DishDef dish = _run?.Database.GetDish(dishId);
            Sprite icon = ContentIconLoader.LoadDish(dish);
            if (icon != null)
            {
                return icon;
            }

            return Resources.Load<Sprite>("Sprites/UI/ui_icon_shop_food")
                ?? Resources.Load<Sprite>("Sprites/UI/card_action_food_dish");
        }
    }
}
