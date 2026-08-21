using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 可复用食谱视图。BattleForm 的功能型实例承载商店删除、事件删除和消耗品选菜；
    /// 独立查看层拥有另一只实例，仅用于只读查看，二者不共享页面生命周期。
    /// </summary>
    public sealed partial class RecipeReadonlyBookView : MonoBehaviour
    {
        private static readonly Vector2 BookChromeReferenceSize =
            new(1286.4f, 714.6667f);

        [Header("Chrome")]
        [SerializeField] private RectTransform _bookChrome;

        [Header("Header")]
        [SerializeField] private TMP_Text _titleText;

        [Header("Warehouse")]
        [FormerlySerializedAs("_bookContainer")]
        [SerializeField] private RectTransform _warehouseContainer;
        [FormerlySerializedAs("_bookPrefab")]
        [SerializeField] private RecipeWarehouseView _warehousePrefab;
        [SerializeField] private RecipeEditDishView _dishPrefab;

        [Header("Back")]
        [SerializeField] private Button _backButton;

        private readonly List<RecipeEditDishView> _spawnedDishes = new();
        private GameRun _run;
        private GameplayDatabase _database;
        private Action _onExit;
        private Action _onChanged;
        private Func<FoodTipsView> _getFoodTips;
        private RecipeEditDishView _hoveredTipDish;
        private RecipeWarehouseView _warehouse;
        private RecipeReadonlyBookSession _session;
        private int _readonlyEntriesBookIndex = -1;
        private IReadOnlyList<RecipeReadonlyDishEntry> _readonlyEntries;
        private IReadOnlyList<RecipeBookSlot> _readonlySlots;
        private bool _wired;
        private bool _liveMutationPlaying;

        private void Awake()
        {
            EnsureWired();
            UpdateBookChromeScale();
        }

        private void OnEnable()
        {
            Canvas.willRenderCanvases -= UpdateBookChromeScale;
            Canvas.willRenderCanvases += UpdateBookChromeScale;
            UpdateBookChromeScale();
        }

        private void OnRectTransformDimensionsChange()
        {
            UpdateBookChromeScale();
        }

        internal void UpdateBookChromeScale()
        {
            if (_bookChrome == null)
            {
                return;
            }

            Vector2 availableSize = ((RectTransform)transform).rect.size;
            if (availableSize.x <= 0f || availableSize.y <= 0f)
            {
                return;
            }

            float scale = Mathf.Clamp01(Mathf.Min(
                availableSize.x / BookChromeReferenceSize.x,
                availableSize.y / BookChromeReferenceSize.y));
            _bookChrome.sizeDelta = BookChromeReferenceSize;
            _bookChrome.localScale = new Vector3(scale, scale, 1f);
        }

        private void OnDisable()
        {
            Canvas.willRenderCanvases -= UpdateBookChromeScale;
            HideRecipeDishTips();
            ReleaseWarehouseContent();
            _session = null;
            _run = null;
            _database = null;
            _onExit = null;
            _onChanged = null;
            _getFoodTips = null;
            _readonlyEntries = null;
            _readonlySlots = null;
            _readonlyEntriesBookIndex = -1;
            _liveMutationPlaying = false;
        }

        private void OnDestroy()
        {
            if (_wired && _backButton != null)
            {
                _backButton.onClick.RemoveListener(OnBackButtonClicked);
            }
        }

        /// <summary>商店删除食物：点击食物后通过通用确认弹窗二次确认。</summary>
        public void OpenForShopDelete(
            GameRun run,
            Action onExit,
            Action onChanged,
            Func<FoodTipsView> getFoodTips = null)
        {
            Open(
                run,
                RecipeReadonlyBookRequest.ShopDeleteDish(onExit, onChanged),
                getFoodTips);
        }

        /// <summary>只读查看单本食谱：禁拖拽，食物只响应悬停 tips。</summary>
        public void OpenForReadonlyBook(
            GameRun run,
            int bookIndex,
            Action onExit,
            Action onChanged,
            Func<FoodTipsView> getFoodTips = null)
        {
            Open(
                run,
                RecipeReadonlyBookRequest.ReadonlyBook(
                    bookIndex,
                    onExit,
                    onChanged),
                getFoodTips);
        }

        /// <summary>
        /// 不依赖运行中局面的只读候选池预览：禁用拖拽/点击，并隐藏返回按钮。
        /// </summary>
        public void OpenForReadonlyDishPool(
            GameplayDatabase database,
            IReadOnlyList<string> dishIds,
            string title,
            Func<FoodTipsView> getFoodTips = null)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            EnsureWired();
            _run = null;
            _database = database;
            _onExit = null;
            _onChanged = null;
            _getFoodTips = getFoodTips;
            _readonlyEntriesBookIndex = 0;

            var entries = new List<RecipeReadonlyDishEntry>(
                dishIds?.Count ?? 0);
            if (dishIds != null)
            {
                foreach (string dishId in dishIds)
                {
                    if (!string.IsNullOrEmpty(dishId))
                    {
                        entries.Add(new RecipeReadonlyDishEntry(
                            new RecipeBookSlot(dishId)));
                    }
                }
            }

            _readonlyEntries = entries;
            _readonlySlots = BuildReadonlySlots(entries);
            _session = new RecipeReadonlyBookSession(
                RecipeReadonlyBookRequest.ReadonlyDishPool(title));
            RenderSession(false);
        }

        /// <summary>
        /// 以消耗品选择态打开食谱面板：禁用拖拽/删除，只允许点击食物进入确认。
        /// </summary>
        public void OpenForActiveRecipeDish(
            GameRun run,
            ItemDefinition item,
            Action onCancel,
            Action<ActiveTarget, Action> onTargetConfirmed,
            Action onChanged,
            Func<FoodTipsView> getFoodTips = null)
        {
            Open(
                run,
                RecipeReadonlyBookRequest.ActiveItemTarget(
                    item,
                    onCancel,
                    onTargetConfirmed,
                    onChanged),
                getFoodTips);
        }

        public void OpenForEventRecipeDishDelete(
            GameRun run,
            string title,
            Action onCancel,
            Action<ActiveTarget> onTargetConfirmed,
            Action onChanged,
            Func<FoodTipsView> getFoodTips = null)
        {
            Open(
                run,
                RecipeReadonlyBookRequest.EventDeleteDish(
                    title,
                    onCancel,
                    onTargetConfirmed,
                    onChanged),
                getFoodTips);
        }

        internal void Open(
            GameRun run,
            RecipeReadonlyBookRequest request,
            Func<FoodTipsView> getFoodTips = null)
        {
            EnsureWired();
            _run = run;
            _database = run?.Database;
            _onExit = request.OnExit;
            _onChanged = request.OnChanged;
            _getFoodTips = getFoodTips;
            _readonlyEntriesBookIndex =
                request.Mode == RecipeReadonlyBookMode.ReadonlyBook
                    ? request.BookIndex
                    : -1;
            _readonlyEntries =
                request.Mode == RecipeReadonlyBookMode.ReadonlyBook
                    ? request.ReadonlyEntries
                    : null;
            _readonlySlots = BuildReadonlySlots(_readonlyEntries);
            _session = new RecipeReadonlyBookSession(request);
            RenderSession(false);
        }

        /// <summary>供外部（如金币变化）请求刷新当前工作区状态。</summary>
        public void Refresh()
        {
            if (_run != null && _session != null && isActiveAndEnabled)
            {
                RenderSession(true);
            }
        }

        public bool PlayActiveItemRecipeFlavorApplied(
            ActiveTarget target,
            Action onComplete)
        {
            if (target.TargetKind != cfg.ItemTargetKind.RecipeDish || _run == null)
            {
                return false;
            }

            RecipeEditDishView dish = FindDish(target.X, target.Y);
            RecipeBookSlot slot = RecipeSlot(target);
            DishDef def = slot == null
                ? null
                : _run.Database.GetDish(slot.DishId);
            if (dish == null || def == null)
            {
                return false;
            }

            HideRecipeDishTips(dish);
            List<string> flavorIds =
                ComposeFlavorIds(def, slot.ExtraFlavorIds);
            dish.PlayFlavorTransform(def, flavorIds, onComplete);
            return true;
        }

        internal void PlayPassiveMutation(
            RecipeMutationResult result,
            IReadOnlyList<RecipeReadonlyDishEntry> afterEntries,
            Action onComplete)
        {
            if (result == null || !result.HasChanges)
            {
                ApplyPassiveMutationEntries(afterEntries);
                onComplete?.Invoke();
                return;
            }

            var animations = new List<Action<Action>>();
            foreach (RecipeMutationEntry entry in result.Entries)
            {
                RecipeEditDishView dish = FindDish(entry.BookIndex, entry.DishIndex);
                if (dish == null)
                {
                    continue;
                }

                if (entry.IsRemove)
                {
                    animations.Add(done => dish.PlayPassiveMutationDissolve(done));
                    continue;
                }

                if (entry.IsAdd)
                {
                    animations.Add(done => dish.PlayPassiveMutationAppear(done));
                    continue;
                }

                if (!entry.HasAfterDish)
                {
                    continue;
                }

                DishDef def = Database?.GetDish(entry.After.DishId);
                if (def != null)
                {
                    animations.Add(done => dish.PlayFlavorTransform(def, entry.After.FlavorIds, done));
                }
            }

            if (animations.Count == 0)
            {
                ApplyPassiveMutationEntries(afterEntries);
                onComplete?.Invoke();
                return;
            }

            int remaining = animations.Count;
            foreach (Action<Action> animation in animations)
            {
                animation(() =>
                {
                    remaining--;
                    if (remaining == 0)
                    {
                        ApplyPassiveMutationEntries(afterEntries);
                        onComplete?.Invoke();
                    }
                });
            }
        }

        internal RecipeEditDishView CreatePassiveMutationPreview(
            RectTransform parent,
            GameRun run,
            RecipeDishSnapshot snapshot,
            int displayIndex)
        {
            if (parent == null
                || run == null
                || snapshot == null
                || string.IsNullOrEmpty(snapshot.DishId)
                || _dishPrefab == null)
            {
                return null;
            }

            DishDef def = run.Database?.GetDish(snapshot.DishId);
            if (def == null)
            {
                return null;
            }

            var scoreSlot = new RecipeBookSlot(snapshot.DishId);
            scoreSlot.RestoreScoreFlatBonus(snapshot.ScoreFlatBonus);
            scoreSlot.RestoreScoreMultiplier(snapshot.ScoreMultiplier);

            RecipeEditDishView dish = Instantiate(_dishPrefab, parent);
            dish.gameObject.name = $"PassiveFlavorDish_{displayIndex + 1}";
            dish.Bind(
                def.Name,
                def.Shape == null
                    ? string.Empty
                    : $"{def.Shape.Width}x{def.Shape.Height}",
                0,
                displayIndex,
                false,
                null,
                def,
                null,
                null,
                null,
                null,
                snapshot.FlavorIds,
                DishIconPreviewMode.Warehouse,
                null,
                ResolveRecipeDishDisplayValue(def, scoreSlot));
            return dish;
        }

        private void ApplyPassiveMutationEntries(IReadOnlyList<RecipeReadonlyDishEntry> entries)
        {
            _readonlyEntries = entries;
            _readonlySlots = BuildReadonlySlots(entries);
            RenderSession(true);
        }

        private void EnsureWired()
        {
            if (_wired)
            {
                return;
            }

            _wired = true;
            if (_bookChrome == null)
            {
                _bookChrome = transform.Find("BookChrome") as RectTransform;
            }

            if (_titleText == null)
            {
                Transform title = transform.Find("BookChrome/TitleTab/Title")
                    ?? transform.Find("TitleTab/Title");
                _titleText = title != null
                    ? title.GetComponent<TMP_Text>()
                    : null;
            }

            if (_backButton != null)
            {
                _backButton.onClick.AddListener(OnBackButtonClicked);
            }
        }

        private void OnBackButtonClicked()
        {
            if (!_liveMutationPlaying)
            {
                _session?.OnExitClicked();
            }
        }

        private void RenderSession(bool preserveScroll)
        {
            UpdateBookChromeScale();
            HideRecipeDishTips();
            float previousScroll = preserveScroll
                && _warehouse != null
                ? _warehouse.VerticalNormalizedPosition
                : 1f;

            if (_session == null || Database == null || !ValidateBindings())
            {
                return;
            }

            DisableLegacyWarehouseLayout();
            if (_session.UsesSemanticTitle)
            {
                SemanticDescriptionFormatter.Set(
                    _titleText,
                    _session.PanelTitle);
            }
            else
            {
                SetText(_titleText, _session.PanelTitle);
            }

            _backButton.gameObject.SetActive(_session.ShowExitButton);
            SetButtonText(_backButton, _session.ExitButtonText);

            const int bookIndex = 0;
            if (_session.BookIndexFilter >= 0
                && _session.BookIndexFilter != bookIndex)
            {
                ReleaseWarehouseContent();
                _onChanged?.Invoke();
                return;
            }

            EnsureWarehouse();
            RectTransform dishContainer = _warehouse.DishContainer;
            IReadOnlyList<RecipeBookSlot> entries = EntriesForBook(bookIndex);
            List<int> displayOrder = BuildDishDisplayOrder(entries);
            for (int displayIndex = 0;
                 displayIndex < displayOrder.Count;
                 displayIndex++)
            {
                int dishIndex = displayOrder[displayIndex];
                RecipeEditDishView dish = DishForDisplayIndex(
                    displayIndex,
                    dishContainer);
                BindDish(
                    dish,
                    entries[dishIndex],
                    bookIndex,
                    dishIndex);
            }

            ReleaseDishesFrom(displayOrder.Count);
            _warehouse.RefreshLayout();
            _warehouse.SetVerticalNormalizedPosition(previousScroll);
            _onChanged?.Invoke();
        }

        private void BindDish(
            RecipeEditDishView dish,
            RecipeBookSlot slot,
            int bookIndex,
            int dishIndex)
        {
            string dishId = slot.DishId;
            DishDef def = Database.GetDish(dishId);
            dish.gameObject.name =
                $"RecipeDish_{bookIndex + 1}_{dishIndex + 1}";
            bool shopDeleteLimitExhausted = _session.IsShopDelete
                && ShopService.DeleteDishRemaining(_run) <= 0;
            bool canClickDish = _session.CanClickDish
                && (!_session.IsShopDelete
                    || ShopService.CanDeleteDish(_run)
                    || shopDeleteLimitExhausted);
            dish.Bind(
                def?.Name ?? dishId,
                DishShapeText(dishId),
                bookIndex,
                dishIndex,
                false,
                canClickDish ? OnRecipeDishClicked : null,
                def,
                null,
                null,
                ShowRecipeDishTips,
                HideRecipeDishTips,
                ComposeFlavorIds(def, slot.ExtraFlavorIds),
                DishIconPreviewMode.Warehouse,
                BattleStatusFor(bookIndex, dishIndex),
                ResolveRecipeDishDisplayValue(def, slot));
            if (ReadonlyEntryFor(bookIndex, dishIndex)?.InitiallyHidden == true)
            {
                dish.PreparePassiveMutationHidden();
            }
        }

        private void EnsureWarehouse()
        {
            if (_warehouse == null)
            {
                _warehouse = Instantiate(
                    _warehousePrefab,
                    _warehouseContainer);
                _warehouse.gameObject.name = "RecipeWarehouse";
                StretchWarehouseToContainer(_warehouse);
            }

            if (!_warehouse.gameObject.activeSelf)
            {
                _warehouse.gameObject.SetActive(true);
            }
        }

        private RecipeEditDishView DishForDisplayIndex(
            int displayIndex,
            RectTransform dishContainer)
        {
            while (_spawnedDishes.Count <= displayIndex)
            {
                RecipeEditDishView created = Instantiate(
                    _dishPrefab,
                    dishContainer);
                created.gameObject.SetActive(false);
                _spawnedDishes.Add(created);
            }

            RecipeEditDishView dish = _spawnedDishes[displayIndex];
            if (dish.transform.parent != dishContainer)
            {
                dish.transform.SetParent(dishContainer, false);
            }

            if (!dish.gameObject.activeSelf)
            {
                dish.gameObject.SetActive(true);
            }

            dish.transform.SetSiblingIndex(displayIndex + 1);
            return dish;
        }

        private void ReleaseDishesFrom(int firstUnusedIndex)
        {
            for (int i = Mathf.Max(0, firstUnusedIndex);
                 i < _spawnedDishes.Count;
                 i++)
            {
                RecipeEditDishView dish = _spawnedDishes[i];
                if (dish == null || !dish.gameObject.activeSelf)
                {
                    continue;
                }

                dish.ReleaseForReuse();
                dish.gameObject.SetActive(false);
            }
        }

        private IReadOnlyList<RecipeBookSlot> EntriesForBook(int bookIndex)
        {
            if (_readonlySlots != null
                && _readonlyEntriesBookIndex == bookIndex)
            {
                return _readonlySlots;
            }

            return _run != null && bookIndex == 0
                ? _run.RecipeEntries
                : Array.Empty<RecipeBookSlot>();
        }

        private BattleRecipeEntryStatus? BattleStatusFor(
            int bookIndex,
            int dishIndex)
        {
            RecipeReadonlyDishEntry entry = ReadonlyEntryFor(
                bookIndex,
                dishIndex);
            return entry?.BattleStatus;
        }

        private RecipeReadonlyDishEntry ReadonlyEntryFor(
            int bookIndex,
            int dishIndex)
        {
            if (_readonlyEntries == null
                || _readonlyEntriesBookIndex != bookIndex
                || dishIndex < 0
                || dishIndex >= _readonlyEntries.Count)
            {
                return null;
            }

            return _readonlyEntries[dishIndex];
        }

        private static IReadOnlyList<RecipeBookSlot> BuildReadonlySlots(
            IReadOnlyList<RecipeReadonlyDishEntry> entries)
        {
            if (entries == null)
            {
                return null;
            }

            var slots = new List<RecipeBookSlot>(entries.Count);
            foreach (RecipeReadonlyDishEntry entry in entries)
            {
                if (entry?.Slot != null)
                {
                    slots.Add(entry.Slot);
                }
            }

            return slots;
        }

        private GameplayDatabase Database => _run?.Database ?? _database;

        private void DisableLegacyWarehouseLayout()
        {
            HorizontalLayoutGroup layout =
                _warehouseContainer.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
            {
                layout.enabled = false;
            }
        }

        private static void StretchWarehouseToContainer(
            RecipeWarehouseView warehouse)
        {
            if (warehouse == null)
            {
                return;
            }

            RectTransform rect = (RectTransform)warehouse.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private void OnRecipeDishClicked(RecipeEditDishView dish)
        {
            if (_liveMutationPlaying)
            {
                return;
            }

            _session?.OnDishClicked(this, dish);
        }

        private RecipeEditDishView FindDish(int bookIndex, int dishIndex)
        {
            for (int i = 0; i < _spawnedDishes.Count; i++)
            {
                RecipeEditDishView dish = _spawnedDishes[i];
                if (dish != null
                    && dish.gameObject.activeSelf
                    && dish.BookIndex == bookIndex
                    && dish.DishIndex == dishIndex)
                {
                    return dish;
                }
            }

            return null;
        }

        private void ShowRecipeDishTips(RecipeEditDishView dish)
        {
            if (dish == null || Database == null)
            {
                return;
            }

            FoodTipsView tips = _getFoodTips?.Invoke();
            if (tips == null)
            {
                return;
            }

            IReadOnlyList<RecipeBookSlot> entries =
                EntriesForBook(dish.BookIndex);
            RecipeBookSlot slot =
                dish.DishIndex >= 0 && dish.DishIndex < entries.Count
                    ? entries[dish.DishIndex]
                    : null;
            DishDef def = slot == null
                ? null
                : Database.GetDish(slot.DishId);
            if (slot == null || def == null)
            {
                return;
            }

            _hoveredTipDish = dish;
            tips.Bind(BuildRecipeDishTipsData(
                def,
                slot,
                ReadonlyEntryFor(dish.BookIndex, dish.DishIndex)));
            tips.Show();
            tips.transform.SetAsLastSibling();
            tips.PlaceAroundRectTransform(
                (RectTransform)dish.transform,
                GetComponentInParent<Canvas>());
        }

        private void HideRecipeDishTips(RecipeEditDishView dish = null)
        {
            if (dish != null
                && _hoveredTipDish != null
                && _hoveredTipDish != dish)
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

        private void ReleaseWarehouseContent()
        {
            ReleaseDishesFrom(0);
            if (_warehouse != null && _warehouse.gameObject.activeSelf)
            {
                _warehouse.gameObject.SetActive(false);
            }
        }

        private bool ValidateBindings()
        {
            if (_bookChrome != null
                && _titleText != null
                && _warehouseContainer != null
                && _warehousePrefab != null
                && _dishPrefab != null
                && _backButton != null)
            {
                return true;
            }

            Debug.LogError(
                $"{nameof(RecipeReadonlyBookView)} prefab bindings are incomplete. "
                + "Book chrome, title, warehouse container/prefab, dish prefab "
                + "and back button "
                + "must all be assigned.",
                this);
            return false;
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }

        private static void SetButtonText(Button button, string value)
        {
            TMP_Text text = button != null
                ? button.GetComponentInChildren<TMP_Text>(true)
                : null;
            SetText(text, value);
        }
    }
}
