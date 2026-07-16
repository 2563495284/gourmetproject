using System;
using System.Collections.Generic;
using System.Threading;
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
    /// 菜品奖励选择页。上方铺出当前菜谱，支持已有菜跨菜谱移动；下方候选菜可拖入目标菜谱完成领取。
    /// </summary>
    public sealed class RewardDishPackPanel : MonoBehaviour
    {
        private const float DesiredBookGap = 24f;
        private const float MinBookScale = 0.1f;

        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private Text _promptText;
        [SerializeField] private RectTransform _editBooksContainer;
        [SerializeField] private RecipeEditBookView _editBookPrefab;
        [SerializeField] private RecipeEditDishView _editDishPrefab;
        [SerializeField] private RectTransform _choiceContainer;
        [SerializeField] private RewardDishChoiceCardView _cardTemplate;
        [SerializeField] private Button _skipButton;

        private readonly List<RewardDishChoiceCardView> _spawnedCards = new();
        private readonly List<GameObject> _spawnedBooks = new();
        private readonly List<RewardChoice> _choices = new();
        private GameRun _run;
        private Func<int, int, bool> _onChoiceDropped;
        private Action _onSkip;
        private CancellationTokenSource _pendingRebuildCts;
        private bool _wired;

        private void Awake()
        {
            EnsureWired();
        }

        private void OnDisable()
        {
            CancelPendingRebuild();
            ClearBooks();
            ClearCards();
        }

        public void Open(
            GameRun run,
            IReadOnlyList<RewardChoice> choices,
            RecipeView recipeView,
            Func<int, int, bool> onChoiceDropped,
            Action onSkip)
        {
            EnsureWired();
            CancelPendingRebuild();
            ClearBooks();
            ClearCards();

            _run = run;
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
                _promptText.text = "拖拽下方一个菜品到上方菜谱中，或移动已有菜品调整菜谱。";
            }

            RebuildBooks();
            BuildCards();
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
                card.Bind(choice, LoadDishIcon(choice.Id), index, null, null);
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

        private void RebuildBooks()
        {
            ClearBooks();
            if (_run == null || _editBooksContainer == null || _editBookPrefab == null || _editDishPrefab == null)
            {
                return;
            }

            HorizontalLayoutGroup layout = _editBooksContainer.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
            {
                layout.enabled = false;
            }

            var books = new List<RecipeEditBookView>(_run.RecipeBookCount);
            for (int i = 0; i < _run.RecipeBookCount; i++)
            {
                RecipeEditBookView book = Instantiate(_editBookPrefab, _editBooksContainer);
                book.gameObject.name = $"RewardRecipeBook_{i + 1}";
                book.Bind(i, OnDishDroppedToBook, OnChoiceDroppedToBook);
                _spawnedBooks.Add(book.gameObject);
                books.Add(book);

                RectTransform dishContainer = book.DishContainer;
                if (dishContainer == null)
                {
                    continue;
                }

                IReadOnlyList<RecipeBookSlot> entries = _run.GetRecipeBookEntries(i);
                for (int k = 0; k < entries.Count; k++)
                {
                    string dishId = entries[k].DishId;
                    DishDef def = _run.Database.GetDish(dishId);
                    RecipeEditDishView dish = Instantiate(_editDishPrefab, dishContainer);
                    dish.gameObject.name = $"RewardRecipeDish_{i + 1}_{k + 1}";
                    dish.Bind(DishName(GameApp.Config.Tables, dishId), DishShapeText(dishId), i, k, true, null, def);
                    _spawnedBooks.Add(dish.gameObject);
                }

                book.ApplyImmediateLayout();
            }

            FitBooksToContainer(books);
        }

        private void ClearBooks()
        {
            for (int i = 0; i < _spawnedBooks.Count; i++)
            {
                if (_spawnedBooks[i] != null)
                {
                    Destroy(_spawnedBooks[i]);
                }
            }

            _spawnedBooks.Clear();
        }

        private void OnDishDroppedToBook(RecipeEditDishView dish, int targetBookIndex, int targetDishIndex)
        {
            if (dish == null)
            {
                return;
            }

            if (ShopService.MoveDish(_run, dish.BookIndex, dish.DishIndex, targetBookIndex, targetDishIndex))
            {
                dish.MarkDropHandled();
                QueueRebuildBooks();
            }
        }

        private void OnChoiceDroppedToBook(RewardDishChoiceCardView card, int targetBookIndex)
        {
            if (card == null || card.ChoiceIndex < 0 || card.ChoiceIndex >= _choices.Count || _onChoiceDropped == null)
            {
                return;
            }

            if (!_onChoiceDropped.Invoke(card.ChoiceIndex, targetBookIndex))
            {
                card.PlayTargetFailed();
                return;
            }

            card.SetResolved(true);
        }

        private void OnSkipClicked()
        {
            _onSkip?.Invoke();
        }

        private void QueueRebuildBooks()
        {
            CancelPendingRebuild();
            RebuildBooksNextFrameAsync();
        }

        private async void RebuildBooksNextFrameAsync()
        {
            _pendingRebuildCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            CancellationToken token = _pendingRebuildCts.Token;
            try
            {
                await Awaitable.NextFrameAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            CancelPendingRebuild();
            if (isActiveAndEnabled)
            {
                RebuildBooks();
            }
        }

        private void CancelPendingRebuild()
        {
            if (_pendingRebuildCts == null)
            {
                return;
            }

            _pendingRebuildCts.Cancel();
            _pendingRebuildCts.Dispose();
            _pendingRebuildCts = null;
        }

        private void FitBooksToContainer(IReadOnlyList<RecipeEditBookView> books)
        {
            if (books == null || books.Count == 0)
            {
                return;
            }

            RectTransform firstBookRect = (RectTransform)books[0].transform;
            Vector2 bookSize = firstBookRect.sizeDelta;
            if (bookSize.x <= 0f || bookSize.y <= 0f)
            {
                bookSize = firstBookRect.rect.size;
            }

            if (bookSize.x <= 0f || bookSize.y <= 0f)
            {
                return;
            }

            float availableWidth = _editBooksContainer.rect.width;
            float availableHeight = _editBooksContainer.rect.height;
            if (availableWidth <= 0f || availableHeight <= 0f)
            {
                return;
            }

            float totalDesiredGap = DesiredBookGap * (books.Count + 1);
            float widthScale = (availableWidth - totalDesiredGap) / (bookSize.x * books.Count);
            float heightScale = availableHeight / bookSize.y;
            float scale = Mathf.Clamp(Mathf.Min(widthScale, heightScale, 1f), MinBookScale, 1f);
            float scaledBookWidth = bookSize.x * scale;
            float gap = books.Count == 1
                ? (availableWidth - scaledBookWidth) * 0.5f
                : (availableWidth - scaledBookWidth * books.Count) / (books.Count + 1);
            gap = Mathf.Max(0f, gap);
            float x = -availableWidth * 0.5f + gap + scaledBookWidth * 0.5f;

            foreach (RecipeEditBookView book in books)
            {
                RectTransform rect = (RectTransform)book.transform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = bookSize;
                rect.localScale = new Vector3(scale, scale, 1f);
                rect.anchoredPosition = new Vector2(x, 0f);
                x += scaledBookWidth + gap;
            }
        }

        private static string DishName(cfg.Tables tables, string dishId)
        {
            cfg.DishVariant variant = tables.TbDishVariant.GetOrDefault(dishId);
            if (variant != null)
            {
                cfg.DishBase baseDish = tables.TbDishBase.GetOrDefault(variant.BaseId);
                if (baseDish != null)
                {
                    return baseDish.Name;
                }
            }

            return dishId;
        }

        private string DishShapeText(string dishId)
        {
            DishDef dish = _run?.Database.GetDish(dishId);
            return dish?.Shape == null ? string.Empty : $"{dish.Shape.Width}x{dish.Shape.Height}";
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
