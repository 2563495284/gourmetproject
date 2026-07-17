using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using DG.Tweening;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 菜谱工作区面板：作为 <c>BattleForm</c> 常驻壳中部内容区的复用状态容器。
    /// 当前承载编辑菜谱、主动道具选菜与确认预览等流程；具体交互由内部状态机切换。
    /// </summary>
    public sealed partial class RecipeWorkspacePanel : MonoBehaviour
    {
        private const float DesiredBookGap = 24f;
        private const float MinBookScale = 0.1f;
        private const float DishFlyDuration = 0.28f;
        private static readonly Color CompareOverlayColor = new Color(0f, 0f, 0f, 0.58f);
        private static readonly Color ComparePanelColor = new Color(0.96f, 0.91f, 0.82f, 1f);
        private static readonly Color CompareCardColor = new Color(1f, 0.97f, 0.9f, 1f);
        private static Font s_defaultFont;

        [Header("Books")]
        [SerializeField] private RectTransform _editBooksContainer;
        [SerializeField] private RecipeEditBookView _editBookPrefab;
        [SerializeField] private RecipeEditDishView _editDishPrefab;

        [Header("Trash / Exit")]
        [SerializeField] private RecipeTrashDropZone _trashZone;
        [SerializeField] private Text _trashPriceText;
        [SerializeField] private Button _exitEditButton;

        private readonly List<GameObject> _spawned = new();
        private readonly List<RecipeEditDishView> _spawnedDishes = new();
        private readonly List<RecipeEditBookView> _spawnedBooks = new();
        private GameRun _run;
        private CancellationTokenSource _pendingRebuildCts;
        private Action _onExit;
        private Action _onChanged;
        private Func<FoodTipsView> _getFoodTips;
        private RecipeEditDishView _hoveredTipDish;
        private int _pendingRestoreBookIndex = -1;
        private int _pendingRestoreDishIndex = -1;
        private GameObject _compareOverlay;
        private RecipeWorkspacePanelStateMachine _stateMachine;
        private bool _wired;

        private void Awake()
        {
            EnsureWired();
        }

        private void OnDisable()
        {
            _stateMachine?.Clear();
            ClearCompareOverlay();
            HideRecipeDishTips();
            ClearSpawned();
            CancelPendingRebuild();
            ClearPendingRestoreScroll();
        }

        /// <summary>由 BattleForm 进入编辑菜谱态时调用：绑定运行数据与回调并铺出工作区。</summary>
        /// <param name="run">当前肉鸽运行。</param>
        /// <param name="onExit">点「离开编辑」按钮时回调（BattleForm 返回商店态）。</param>
        /// <param name="onChanged">编辑（移动 / 删除）后回调，用于刷新常驻壳金币与底部菜谱条。</param>
        public void Open(GameRun run, Action onExit, Action onChanged, Func<FoodTipsView> getFoodTips = null)
        {
            EnsureWired();
            _run = run;
            _onExit = onExit;
            _onChanged = onChanged;
            _getFoodTips = getFoodTips;
            _stateMachine.Switch(new RecipeEditState());
        }

        /// <summary>以主动道具选择态打开菜谱面板：禁用拖拽/删除，只允许点击菜品进入确认。</summary>
        public void OpenForActiveRecipeDish(
            GameRun run,
            ItemDefinition item,
            Action onCancel,
            Action<ActiveTarget> onTargetConfirmed,
            Action onChanged,
            Func<FoodTipsView> getFoodTips = null)
        {
            EnsureWired();
            _run = run;
            _onChanged = onChanged;
            _getFoodTips = getFoodTips;
            _stateMachine.Switch(new ActiveRecipeDishSelectState(item, onCancel, onTargetConfirmed));
        }

        public void OpenForEventRecipeDishDelete(
            GameRun run,
            string title,
            Action onCancel,
            Action<ActiveTarget> onTargetConfirmed,
            Action onChanged,
            Func<FoodTipsView> getFoodTips = null)
        {
            EnsureWired();
            _run = run;
            _onChanged = onChanged;
            _getFoodTips = getFoodTips;
            _stateMachine.Switch(new EventRecipeDishDeleteState(title, onCancel, onTargetConfirmed));
        }

        /// <summary>供外部（如金币变化）请求刷新当前工作区状态。</summary>
        public void Refresh()
        {
            if (_run != null && isActiveAndEnabled)
            {
                _stateMachine?.Refresh();
            }
        }

        public bool TryPointerRecipeDishTarget(Vector2 screenPoint, out ActiveTarget target)
        {
            target = default;
            if (_run == null)
            {
                return false;
            }

            for (int i = _spawnedDishes.Count - 1; i >= 0; i--)
            {
                RecipeEditDishView dish = _spawnedDishes[i];
                if (dish == null || !dish.ContainsScreenPoint(screenPoint))
                {
                    continue;
                }

                if (TryBuildRecipeTarget(dish, out target))
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsureWired()
        {
            if (_wired)
            {
                return;
            }

            _wired = true;
            _stateMachine ??= new RecipeWorkspacePanelStateMachine(this);
            if (_exitEditButton != null)
            {
                _exitEditButton.onClick.RemoveAllListeners();
                _exitEditButton.onClick.AddListener(() => _stateMachine?.OnExitClicked());
            }
        }

        private void RebuildBooksForCurrentState()
        {
            ClearCompareOverlay();
            ClearSpawned();
            RecipeWorkspacePanelState state = _stateMachine?.Current;
            if (state == null || _run == null || _editBooksContainer == null || _editBookPrefab == null || _editDishPrefab == null)
            {
                return;
            }

            ConfigureBooksLayoutGroup();

            if (_trashZone != null)
            {
                _trashZone.gameObject.SetActive(state.ShowTrash);
                if (state.ShowTrash)
                {
                    _trashZone.Bind(OnDishDroppedToTrash);
                }
            }

            if (_trashPriceText != null)
            {
                _trashPriceText.gameObject.SetActive(state.ShowTrash);
                SetText(_trashPriceText, state.ShowTrash ? $"-{ShopService.DeleteCost(_run)}" : string.Empty);
            }

            SetButtonText(_exitEditButton, state.ExitButtonText);

            var books = new List<RecipeEditBookView>(_run.RecipeBookCount);
            for (int i = 0; i < _run.RecipeBookCount; i++)
            {
                RecipeEditBookView book = Instantiate(_editBookPrefab, _editBooksContainer);
                book.gameObject.name = $"RecipeEditBook_{i + 1}";
                book.Bind(i, state.CanDropDishToBook ? OnDishDroppedToBook : null);
                _spawned.Add(book.gameObject);
                _spawnedBooks.Add(book);
                books.Add(book);

                RectTransform dishContainer = book.DishContainer;
                if (dishContainer == null)
                {
                    continue;
                }

                IReadOnlyList<RecipeBookSlot> entries = _run.GetRecipeBookEntries(i);
                for (int k = 0; k < entries.Count; k++)
                {
                    RecipeBookSlot slot = entries[k];
                    string dishId = slot.DishId;
                    DishDef def = _run.Database.GetDish(dishId);
                    RecipeEditDishView dish = Instantiate(_editDishPrefab, dishContainer);
                    dish.gameObject.name = $"RecipeDish_{i + 1}_{k + 1}";
                    dish.Bind(
                        DishName(GameApp.Config.Tables, dishId),
                        DishShapeText(dishId),
                        i,
                        k,
                        state.EnableDishDrag,
                        state.CanClickDish ? OnRecipeDishClicked : null,
                        def,
                        OnRecipeDishBeginDrag,
                        OnRecipeDishDragCancelled,
                        ShowRecipeDishTips,
                        HideRecipeDishTips);
                    _spawned.Add(dish.gameObject);
                    _spawnedDishes.Add(dish);
                }

                book.ApplyImmediateLayout();
            }

            FitBooksToContainer(books);
            RestorePendingScroll(books);
            _onChanged?.Invoke();
        }

        private void ConfigureBooksLayoutGroup()
        {
            HorizontalLayoutGroup layout = _editBooksContainer.GetComponent<HorizontalLayoutGroup>();
            if (layout == null)
            {
                return;
            }

            layout.enabled = false;
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

        private void OnDishDroppedToBook(RecipeEditDishView dish, int targetBookIndex, int targetDishIndex)
        {
            if (dish == null || _stateMachine?.Current == null)
            {
                return;
            }

            RecipeEditBookView targetBook = FindBook(targetBookIndex);
            targetDishIndex = Mathf.Max(0, targetDishIndex);
            targetBook?.AnimateInsertionGap(targetDishIndex, dish);
            if (_stateMachine.Current.OnDishDroppedToBook(this, dish, targetBookIndex, targetDishIndex))
            {
                dish.MarkDropHandled();
                HideRecipeDishTips();
                if (targetBook == null)
                {
                    QueueRebuild();
                    return;
                }

                targetBook.ScrollToIndex(targetDishIndex, DishFlyDuration);
                PlayDishFlyToSlot(dish, targetBook, targetDishIndex, () =>
                {
                    RequestRestoreScroll(targetBookIndex, targetDishIndex);
                    RebuildBooksForCurrentState();
                });
            }
        }

        private void OnDishDroppedToTrash(RecipeEditDishView dish)
        {
            if (dish == null || _stateMachine?.Current == null)
            {
                return;
            }

            if (_stateMachine.Current.OnDishDroppedToTrash(this, dish))
            {
                dish.MarkDropHandled();
                HideRecipeDishTips();
                RectTransform rect = (RectTransform)dish.transform;
                DOTween.Kill(rect);
                DOTween.To(() => rect.localScale, value => rect.localScale = value, Vector3.zero, 0.16f)
                    .SetEase(Ease.InCubic)
                    .SetUpdate(true)
                    .SetTarget(rect)
                    .SetLink(dish.gameObject)
                    .OnComplete(RebuildBooksForCurrentState);
            }
        }

        private void OnRecipeDishClicked(RecipeEditDishView dish)
        {
            _stateMachine?.Current?.OnDishClicked(this, dish);
        }

        private void OnRecipeDishBeginDrag(RecipeEditDishView dish)
        {
            HideRecipeDishTips();
            FindBook(dish.BookIndex)?.AnimateCompaction(dish);
        }

        private bool OnRecipeDishDragCancelled(RecipeEditDishView dish)
        {
            if (_run == null || dish == null)
            {
                return false;
            }

            RecipeEditBookView book = FindBook(dish.BookIndex);
            if (book == null)
            {
                return false;
            }

            int targetIndex = book.CurrentDishCount(dish);
            if (!ShopService.MoveDish(_run, dish.BookIndex, dish.DishIndex, dish.BookIndex, targetIndex))
            {
                return false;
            }

            dish.MarkDropHandled();
            book.ScrollToIndex(targetIndex, DishFlyDuration);
            PlayDishFlyToSlot(dish, book, targetIndex, () =>
            {
                RequestRestoreScroll(dish.BookIndex, targetIndex);
                RebuildBooksForCurrentState();
            });
            return true;
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

        private RecipeEditBookView FindBook(int bookIndex)
        {
            return bookIndex >= 0 && bookIndex < _spawnedBooks.Count ? _spawnedBooks[bookIndex] : null;
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

        private void ShowRecipeDishTips(RecipeEditDishView dish)
        {
            if (dish == null || _run == null)
            {
                return;
            }

            FoodTipsView tips = _getFoodTips?.Invoke();
            if (tips == null)
            {
                return;
            }

            RecipeBookSlot slot = RecipeSlot(new ActiveTarget(string.Empty, dish.BookIndex, dish.DishIndex, cfg.ItemTargetKind.RecipeDish));
            DishDef def = slot == null ? null : _run.Database.GetDish(slot.DishId);
            if (slot == null || def == null)
            {
                return;
            }

            _hoveredTipDish = dish;
            tips.Bind(BuildRecipeDishTipsData(def, slot));
            tips.Show();
            tips.transform.SetAsLastSibling();
            tips.PlaceAroundRectTransform((RectTransform)dish.transform, GetComponentInParent<Canvas>());
        }

        private void HideRecipeDishTips(RecipeEditDishView dish = null)
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

        private bool TryBuildRecipeTarget(RecipeEditDishView dish, out ActiveTarget target)
        {
            target = default;
            if (_run == null || dish == null)
            {
                return false;
            }

            IReadOnlyList<RecipeBookSlot> entries = _run.GetRecipeBookEntries(dish.BookIndex);
            if (dish.DishIndex < 0 || dish.DishIndex >= entries.Count)
            {
                return false;
            }

            RecipeBookSlot slot = entries[dish.DishIndex];
            target = new ActiveTarget(slot.DishId, dish.BookIndex, dish.DishIndex, cfg.ItemTargetKind.RecipeDish);
            return true;
        }

        private void ShowActiveItemCompare(ItemDefinition item, ActiveTarget target, Action onBack, Action onConfirm)
        {
            ClearCompareOverlay();
            RecipeBookSlot slot = RecipeSlot(target);
            DishDef def = slot == null ? null : _run.Database.GetDish(slot.DishId);
            if (def == null)
            {
                return;
            }

            _compareOverlay = CreateStretchChild("ActiveItemDishCompare_Runtime", transform);
            Image overlayImage = _compareOverlay.AddComponent<Image>();
            overlayImage.color = CompareOverlayColor;
            overlayImage.raycastTarget = true;
            _compareOverlay.transform.SetAsLastSibling();

            GameObject panel = CreateRectChild("Panel", _compareOverlay.transform, new Vector2(960f, 520f));
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = ComparePanelColor;

            CreateText(
                "Title",
                panel.transform,
                $"{item.Name}：确认目标菜品",
                new Vector2(0f, 216f),
                new Vector2(860f, 44f),
                24,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);

            CreateDishInfoCard(
                panel.transform,
                "原菜品",
                BuildDishInfo(def, ComposeFlavorIds(def, slot.ExtraFlavorIds), slot),
                new Vector2(-260f, 28f));

            CreateText(
                "Arrow",
                panel.transform,
                "=>",
                new Vector2(0f, 40f),
                new Vector2(92f, 60f),
                32,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);

            List<string> previewExtraFlavors = PreviewExtraFlavors(slot.ExtraFlavorIds, item.EffectParam);
            CreateDishInfoCard(
                panel.transform,
                "使用后",
                BuildDishInfo(def, ComposeFlavorIds(def, previewExtraFlavors), slot),
                new Vector2(260f, 28f));

            CreateButton(
                "BackButton",
                panel.transform,
                "返回",
                new Vector2(-260f, -214f),
                new Vector2(180f, 48f),
                onBack);

            CreateButton(
                "ConfirmButton",
                panel.transform,
                "确认",
                new Vector2(260f, -214f),
                new Vector2(180f, 48f),
                () =>
                {
                    onConfirm?.Invoke();
                });
        }

        private void ShowEventDeleteConfirm(string title, ActiveTarget target, Action onCancel, Action<ActiveTarget> onConfirm)
        {
            RecipeBookSlot slot = RecipeSlot(target);
            DishDef def = slot == null ? null : _run.Database.GetDish(slot.DishId);
            string dishName = def != null ? def.Name : target.Id;
            var data = new ConfirmDialogData
            {
                Title = string.IsNullOrWhiteSpace(title) ? "确认删除菜品" : title,
                Message = $"确定要从菜谱中删除「{dishName}」吗？",
                ConfirmText = "删除",
                CancelText = "返回",
                OnConfirm = () => onConfirm?.Invoke(target),
                OnCancel = onCancel,
            };
            GameApp.UI.OpenUIForm(UIForms.ConfirmDialog, UIForms.GroupDialog, data);
        }

        private RecipeBookSlot RecipeSlot(ActiveTarget target)
        {
            if (_run == null || target.X < 0 || target.X >= _run.RecipeBookCount)
            {
                return null;
            }

            IReadOnlyList<RecipeBookSlot> entries = _run.GetRecipeBookEntries(target.X);
            return target.Y >= 0 && target.Y < entries.Count ? entries[target.Y] : null;
        }

        private void CreateDishInfoCard(Transform parent, string title, string info, Vector2 center)
        {
            GameObject card = CreateRectChild(title, parent, new Vector2(350f, 330f), center);
            Image image = card.AddComponent<Image>();
            image.color = CompareCardColor;

            CreateText(
                "Title",
                card.transform,
                title,
                new Vector2(0f, 132f),
                new Vector2(310f, 36f),
                21,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);

            Text body = CreateText(
                "Body",
                card.transform,
                info,
                new Vector2(0f, -28f),
                new Vector2(300f, 250f),
                17,
                FontStyle.Normal,
                TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private string BuildDishInfo(DishDef def, IReadOnlyList<string> flavorIds, RecipeBookSlot slot)
        {
            var sb = new StringBuilder();
            sb.AppendLine(def.Name);
            float score = def.Deliciousness + (slot != null ? slot.ScoreFlatBonus : 0f);
            sb.AppendLine($"美味度 {score}　形状 {def.Shape.Width}x{def.Shape.Height}");
            if (slot != null && Math.Abs(slot.ScoreMultiplier - 1f) > 0.0001f)
            {
                sb.AppendLine($"倍率 x{slot.ScoreMultiplier:0.##}");
            }

            List<string> skillIds = ComposeSkillIds(def, slot?.ExtraSkillIds);
            List<string> lines = DishInfoText.TagLines(skillIds, flavorIds, _run.Database, out List<string> terms);
            foreach (string line in lines)
            {
                sb.AppendLine(line);
            }

            string termText = DishInfoText.TermBlock(terms);
            if (!string.IsNullOrEmpty(termText))
            {
                sb.AppendLine();
                sb.Append(termText);
            }

            return sb.ToString().TrimEnd();
        }

        private FoodTipsData BuildRecipeDishTipsData(DishDef def, RecipeBookSlot slot)
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

        private List<string> PreviewExtraFlavors(IReadOnlyList<string> current, string flavorId)
        {
            var ids = new List<string>();
            if (current != null)
            {
                ids.AddRange(current);
            }

            if (string.IsNullOrEmpty(flavorId))
            {
                return ids;
            }

            int flavorLimit = Math.Max(1, _run != null ? _run.FoodFlavorLimit : 1);
            if (ids.Count < flavorLimit)
            {
                ids.Add(flavorId);
            }
            else
            {
                ids[ids.Count - 1] = flavorId;
            }

            return ids;
        }

        private void QueueRebuild()
        {
            CancelPendingRebuild();

            RebuildNextFrameAsync();
        }

        private async void RebuildNextFrameAsync()
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
            if (!isActiveAndEnabled)
            {
                return;
            }

            RebuildBooksForCurrentState();
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

        private void ClearSpawned()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }

            _spawned.Clear();
            _spawnedDishes.Clear();
            _spawnedBooks.Clear();
        }

        private void ClearCompareOverlay()
        {
            if (_compareOverlay != null)
            {
                Destroy(_compareOverlay);
                _compareOverlay = null;
            }
        }

        private static void SetText(Text text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }

        private static void SetButtonText(Button button, string value)
        {
            Text text = button != null ? button.GetComponentInChildren<Text>(true) : null;
            SetText(text, value);
        }

        private static GameObject CreateStretchChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            return go;
        }

        private static GameObject CreateRectChild(string name, Transform parent, Vector2 size, Vector2 anchoredPosition = default)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            return go;
        }

        private static Text CreateText(
            string name,
            Transform parent,
            string text,
            Vector2 anchoredPosition,
            Vector2 size,
            int fontSize,
            FontStyle style,
            TextAnchor alignment)
        {
            GameObject go = CreateRectChild(name, parent, size, anchoredPosition);
            Text label = go.AddComponent<Text>();
            label.font = ResolveFont();
            label.text = text ?? string.Empty;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.alignment = alignment;
            label.color = Color.black;
            label.raycastTarget = false;
            return label;
        }

        private static Button CreateButton(
            string name,
            Transform parent,
            string label,
            Vector2 anchoredPosition,
            Vector2 size,
            Action onClick)
        {
            GameObject go = CreateRectChild(name, parent, size, anchoredPosition);
            Image image = go.AddComponent<Image>();
            image.color = Color.white;
            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick?.Invoke());

            CreateText("Label", go.transform, label, Vector2.zero, size, 20, FontStyle.Bold, TextAnchor.MiddleCenter);
            return button;
        }

        private static Font ResolveFont()
        {
            if (s_defaultFont == null)
            {
                s_defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (s_defaultFont == null)
                {
                    s_defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
            }

            return s_defaultFont;
        }
    }
}
