using System;
using System.Collections.Generic;
using System.Threading;
using DG.Tweening;
using GourmetProject.Game;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Tooltips;
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
        private const float DishFlyDuration = 0.28f;
        private static readonly Vector2 ChoiceDishSize = new(140f, 140f);

        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private Text _promptText;
        [SerializeField] private RectTransform _editBooksContainer;
        [SerializeField] private RecipeEditBookView _editBookPrefab;
        [SerializeField] private RecipeEditDishView _editDishPrefab;
        [SerializeField] private RectTransform _choiceContainer;
        [SerializeField] private RewardDishChoiceCardView _cardTemplate;
        [SerializeField] private Button _skipButton;

        private readonly List<RecipeEditDishView> _spawnedChoices = new();
        private readonly List<GameObject> _spawnedBooks = new();
        private readonly List<RecipeEditBookView> _spawnedBookViews = new();
        private readonly List<RewardChoice> _choices = new();
        private GameRun _run;
        private Func<int, int, bool> _onChoiceDropped;
        private Action _onSkip;
        private Func<FoodTipsView> _getFoodTips;
        private RecipeEditDishView _hoveredTipDish;
        private CancellationTokenSource _pendingRebuildCts;
        private int _pendingRestoreBookIndex = -1;
        private int _pendingRestoreDishIndex = -1;
        private bool _wired;

        private void Awake()
        {
            EnsureWired();
        }

        private void OnDisable()
        {
            CancelPendingRebuild();
            HideDishTips();
            ClearBooks();
            ClearCards();
            ClearPendingRestoreScroll();
        }

        public void Open(
            GameRun run,
            IReadOnlyList<RewardChoice> choices,
            RecipeView recipeView,
            Func<int, int, bool> onChoiceDropped,
            Action onSkip,
            Func<FoodTipsView> getFoodTips = null)
        {
            EnsureWired();
            CancelPendingRebuild();
            HideDishTips();
            ClearBooks();
            ClearCards();

            _run = run;
            _onChoiceDropped = onChoiceDropped;
            _onSkip = onSkip;
            _getFoodTips = getFoodTips;
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
            if (_editDishPrefab == null || _choiceContainer == null)
            {
                return;
            }

            if (_cardTemplate != null)
            {
                _cardTemplate.gameObject.SetActive(false);
            }

            for (int i = 0; i < _choices.Count; i++)
            {
                int index = i;
                RewardChoice choice = _choices[i];
                DishDef def = _run?.Database.GetDish(choice.Id);
                RecipeEditDishView dish = Instantiate(_editDishPrefab, _choiceContainer);
                dish.gameObject.name = $"RewardDishChoice_{index + 1}";
                dish.gameObject.SetActive(true);
                ConfigureChoiceDishRect((RectTransform)dish.transform);
                dish.Bind(
                    choice.Name,
                    DishShapeText(choice.Id),
                    -1,
                    index,
                    true,
                    null,
                    def,
                    null,
                    null,
                    ShowDishTips,
                    HideDishTips,
                    ComposeFlavorIds(
                        def,
                        string.IsNullOrEmpty(choice.FlavorId) ? null : new[] { choice.FlavorId }));
                _spawnedChoices.Add(dish);
            }
        }

        private void ClearCards()
        {
            for (int i = 0; i < _spawnedChoices.Count; i++)
            {
                if (_spawnedChoices[i] != null)
                {
                    Destroy(_spawnedChoices[i].gameObject);
                }
            }

            _spawnedChoices.Clear();
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
                _spawnedBookViews.Add(book);
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
                    dish.Bind(
                        DishName(GameApp.Config.Tables, dishId),
                        DishShapeText(dishId),
                        i,
                        k,
                        true,
                        null,
                        def,
                        null,
                        null,
                        ShowDishTips,
                        HideDishTips,
                        ComposeFlavorIds(def, entries[k].ExtraFlavorIds));
                    _spawnedBooks.Add(dish.gameObject);
                }

                book.ApplyImmediateLayout();
            }

            FitBooksToContainer(books);
            RestorePendingScroll(books);
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
            _spawnedBookViews.Clear();
        }

        private void OnDishDroppedToBook(RecipeEditDishView dish, int targetBookIndex, int targetDishIndex)
        {
            if (dish == null)
            {
                return;
            }

            if (dish.BookIndex < 0)
            {
                OnChoiceDroppedToBook(dish, targetBookIndex);
                return;
            }

            if (ShopService.MoveDish(_run, dish.BookIndex, dish.DishIndex, targetBookIndex, targetDishIndex))
            {
                dish.MarkDropHandled();
                RequestRestoreScroll(targetBookIndex, targetDishIndex);
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

        private void OnChoiceDroppedToBook(RecipeEditDishView dish, int targetBookIndex)
        {
            if (dish == null || dish.DishIndex < 0 || dish.DishIndex >= _choices.Count || _onChoiceDropped == null)
            {
                return;
            }

            RecipeEditBookView targetBook = FindBook(targetBookIndex);
            if (targetBook == null)
            {
                return;
            }

            int targetDishIndex = targetBook.CurrentDishCount();
            dish.MarkDropHandled();
            HideDishTips();
            targetBook.ScrollToIndex(targetDishIndex, DishFlyDuration);
            PlayDishFlyToSlot(dish, targetBook, targetDishIndex, () =>
            {
                bool dropped = _onChoiceDropped.Invoke(dish.DishIndex, targetBookIndex);
                if (!dropped)
                {
                    if (dish != null)
                    {
                        PlayTargetFailed(dish);
                        dish.SetInteractableAfterAnimation(true);
                    }

                    return;
                }

                if (dish != null)
                {
                    dish.gameObject.SetActive(false);
                }

                if (this != null && isActiveAndEnabled)
                {
                    RequestRestoreScroll(targetBookIndex, targetDishIndex);
                    QueueRebuildBooks();
                }
            });
        }

        private RecipeEditBookView FindBook(int bookIndex)
        {
            return bookIndex >= 0 && bookIndex < _spawnedBookViews.Count
                ? _spawnedBookViews[bookIndex]
                : null;
        }

        private void RequestRestoreScroll(int bookIndex, int dishIndex)
        {
            _pendingRestoreBookIndex = bookIndex;
            _pendingRestoreDishIndex = dishIndex;
        }

        private void ClearPendingRestoreScroll()
        {
            _pendingRestoreBookIndex = -1;
            _pendingRestoreDishIndex = -1;
        }

        private void RestorePendingScroll(IReadOnlyList<RecipeEditBookView> books)
        {
            if (_pendingRestoreBookIndex < 0 || books == null)
            {
                return;
            }

            int bookIndex = _pendingRestoreBookIndex;
            int dishIndex = _pendingRestoreDishIndex;
            _pendingRestoreBookIndex = -1;
            _pendingRestoreDishIndex = -1;
            if (bookIndex < 0 || bookIndex >= books.Count || books[bookIndex] == null)
            {
                return;
            }

            books[bookIndex].ScrollToIndex(Mathf.Max(0, dishIndex), 0f);
        }

        private void PlayDishFlyToSlot(RecipeEditDishView dish, RecipeEditBookView targetBook, int targetDishIndex, Action onComplete)
        {
            if (dish == null || targetBook == null)
            {
                onComplete?.Invoke();
                return;
            }

            RectTransform rect = (RectTransform)dish.transform;
            dish.PrepareAsFloating();
            Vector3 start = rect.position;
            Vector3 startScale = rect.localScale;
            RectTransform targetScaleSource = targetBook.ViewportRect != null
                ? targetBook.ViewportRect
                : (RectTransform)targetBook.transform;
            Vector3 targetScale = FloatingScaleForTarget(rect.parent as RectTransform, targetScaleSource);
            Vector3 targetPosition = targetBook.SlotWorldCenterAfterScrollToIndex(targetDishIndex);
            DOTween.Kill(rect);
            DOTween.To(
                    () => 0f,
                    t =>
                    {
                        rect.position = Vector3.LerpUnclamped(start, targetPosition, t);
                        rect.localScale = Vector3.LerpUnclamped(startScale, targetScale, t);
                    },
                    1f,
                    DishFlyDuration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .SetTarget(rect)
                .SetLink(dish.gameObject)
                .OnComplete(() =>
                {
                    dish.SetInteractableAfterAnimation(false);
                    onComplete?.Invoke();
                });
        }

        private static Vector3 FloatingScaleForTarget(RectTransform floatingParent, RectTransform target)
        {
            if (target == null)
            {
                return Vector3.one;
            }

            Vector3 parentScale = floatingParent != null ? floatingParent.lossyScale : Vector3.one;
            Vector3 targetScale = target.lossyScale;
            return new Vector3(
                SafeScaleDiv(targetScale.x, parentScale.x),
                SafeScaleDiv(targetScale.y, parentScale.y),
                SafeScaleDiv(targetScale.z, parentScale.z));
        }

        private static float SafeScaleDiv(float value, float divisor)
        {
            return Mathf.Abs(divisor) <= 0.0001f ? value : value / divisor;
        }

        private static void ConfigureChoiceDishRect(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = ChoiceDishSize;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private static void PlayTargetFailed(RecipeEditDishView dish)
        {
            if (dish == null)
            {
                return;
            }

            RectTransform rect = (RectTransform)dish.transform;
            Vector3 origin = rect.localPosition;
            DOTween.Kill(rect);
            DOVirtual.Float(0f, 1f, 0.25f, t =>
                {
                    if (rect == null)
                    {
                        return;
                    }

                    float offset = Mathf.Sin(t * Mathf.PI * 12f) * 9f * (1f - t);
                    rect.localPosition = origin + new Vector3(offset, 0f, 0f);
                })
                .SetEase(Ease.Linear)
                .SetUpdate(true)
                .SetTarget(rect)
                .SetLink(dish.gameObject)
                .OnComplete(() =>
                {
                    if (rect != null)
                    {
                        rect.localPosition = origin;
                    }
                });
        }

        private void OnSkipClicked()
        {
            _onSkip?.Invoke();
        }

        private void ShowDishTips(RecipeEditDishView dish)
        {
            if (dish == null || _run == null)
            {
                return;
            }

            if (!TryGetTipsContext(dish, out DishDef def, out RecipeBookSlot slot) || def == null)
            {
                return;
            }

            FoodTipsView tips = _getFoodTips?.Invoke();
            if (tips == null)
            {
                return;
            }

            _hoveredTipDish = dish;
            tips.Bind(BuildDishTipsData(def, slot));
            tips.Show();
            tips.transform.SetAsLastSibling();
            tips.PlaceAroundRectTransform((RectTransform)dish.transform, GetComponentInParent<Canvas>());
        }

        private void HideDishTips(RecipeEditDishView dish = null)
        {
            if (dish != null && _hoveredTipDish != null && _hoveredTipDish != dish)
            {
                return;
            }

            _hoveredTipDish = null;
            FoodTipsView tips = _getFoodTips?.Invoke();
            if (tips != null)
            {
                tips.Hide();
            }
        }

        private bool TryGetTipsContext(RecipeEditDishView dish, out DishDef def, out RecipeBookSlot slot)
        {
            def = null;
            slot = null;
            if (dish == null || _run?.Database == null)
            {
                return false;
            }

            if (dish.BookIndex >= 0)
            {
                IReadOnlyList<RecipeBookSlot> entries = _run.GetRecipeBookEntries(dish.BookIndex);
                if (dish.DishIndex < 0 || dish.DishIndex >= entries.Count)
                {
                    return false;
                }

                slot = entries[dish.DishIndex];
                def = _run.Database.GetDish(slot.DishId);
                return def != null;
            }

            if (dish.DishIndex < 0 || dish.DishIndex >= _choices.Count)
            {
                return false;
            }

            RewardChoice choice = _choices[dish.DishIndex];
            if (choice == null || choice.Kind != cfg.RewardKind.DishChoice)
            {
                return false;
            }

            def = _run.Database.GetDish(choice.Id);
            if (def == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(choice.FlavorId))
            {
                slot = new RecipeBookSlot(choice.Id);
                slot.AddFlavor(choice.FlavorId);
            }

            return true;
        }

        private FoodTipsData BuildDishTipsData(DishDef def, RecipeBookSlot slot)
        {
            List<string> skillIds = ComposeSkillIds(def, slot?.ExtraSkillIds);
            List<string> flavorIds = ComposeFlavorIds(def, slot?.ExtraFlavorIds);
            var skills = new List<FoodInfoEntry>();
            foreach (string skillId in skillIds)
            {
                SkillDef skill = _run.Database.GetSkill(skillId);
                if (skill != null)
                {
                    skills.Add(new FoodInfoEntry(skill.Name, skill.Desc));
                }
            }

            var flavorNames = new List<string>();
            var flavorDetails = new List<FoodInfoEntry>();
            foreach (string flavorId in flavorIds)
            {
                FlavorDef flavor = _run.Database.GetFlavor(flavorId);
                if (flavor == null)
                {
                    continue;
                }

                flavorNames.Add(flavor.Name);
                flavorDetails.Add(new FoodInfoEntry(flavor.Name, flavor.Desc));
            }

            var summary = new FoodSummaryTipsData(def.Name, skills, flavorNames);
            float multiplier = slot != null ? slot.ScoreMultiplier : 1f;
            float score = def.Deliciousness + (slot != null ? slot.ScoreFlatBonus : 0f);
            return new FoodTipsData(
                summary,
                new FoodScoreTipsData(score, multiplier),
                Array.Empty<FoodMaterialTipsEntry>(),
                flavorDetails,
                Array.Empty<FoodInfoEntry>(),
                BuildRecipeSpecialTags(skillIds));
        }

        private IReadOnlyList<FoodInfoEntry> BuildRecipeSpecialTags(IReadOnlyList<string> skillIds)
        {
            if (skillIds == null || _run?.Database == null)
            {
                return Array.Empty<FoodInfoEntry>();
            }

            var termIds = new List<string>();
            foreach (string skillId in skillIds)
            {
                SkillDef skill = _run.Database.GetSkill(skillId);
                AddUniqueRange(termIds, skill?.TermIds);
            }

            var tags = new List<FoodInfoEntry>(termIds.Count);
            foreach (string termId in termIds)
            {
                cfg.Term term = GameApp.Config?.Tables?.TbTerm?.GetOrDefault(termId);
                tags.Add(term != null
                    ? new FoodInfoEntry(term.Name, term.Desc)
                    : new FoodInfoEntry(termId, string.Empty));
            }

            return tags;
        }

        private static void AddUniqueRange(List<string> list, IReadOnlyList<string> values)
        {
            if (list == null || values == null)
            {
                return;
            }

            foreach (string value in values)
            {
                if (!string.IsNullOrEmpty(value) && !list.Contains(value))
                {
                    list.Add(value);
                }
            }
        }

        private static List<string> ComposeSkillIds(DishDef def, IReadOnlyList<string> extraSkillIds)
        {
            var ids = new List<string>();
            if (def?.SkillIds != null)
            {
                ids.AddRange(def.SkillIds);
            }

            if (extraSkillIds != null)
            {
                ids.AddRange(extraSkillIds);
            }

            return ids;
        }

        private static List<string> ComposeFlavorIds(DishDef def, IReadOnlyList<string> extraFlavorIds)
        {
            var ids = new List<string>();
            if (def != null && !string.IsNullOrEmpty(def.FlavorId))
            {
                ids.Add(def.FlavorId);
            }

            if (extraFlavorIds != null)
            {
                ids.AddRange(extraFlavorIds);
            }

            return ids;
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

                book.FitSlotsWithinView(GameRun.RecipeBookCapacity);
                book.ApplyImmediateLayout();
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

    }
}
