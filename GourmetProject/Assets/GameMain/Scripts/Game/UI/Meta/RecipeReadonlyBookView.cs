using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 唯一菜谱页面：作为 <c>BattleForm</c> 中部内容区的复用状态视图。
    /// 承载普通查看、商店删除、事件删除和主动道具选菜流程。
    /// </summary>
    public sealed partial class RecipeReadonlyBookView : MonoBehaviour
    {
        [Header("Header")]
        [SerializeField] private Text _titleText;

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
        private Action _onExit;
        private Action _onChanged;
        private Func<FoodTipsView> _getFoodTips;
        private RecipeEditDishView _hoveredTipDish;
        private RecipeWarehouseView _warehouse;
        private RecipeReadonlyBookStateMachine _stateMachine;
        private int _readonlyEntriesBookIndex = -1;
        private IReadOnlyList<RecipeBookSlot> _readonlyEntries;
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
            ClearWarehouse();
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

        /// <summary>只读查看单本菜谱：禁拖拽，菜品只响应悬停 tips。</summary>
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
        /// 以主动道具选择态打开菜谱面板：禁用拖拽/删除，只允许点击菜品进入确认。
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

            switch (request.Mode)
            {
                case RecipeReadonlyBookMode.ReadonlyBook:
                    _stateMachine.Switch(
                        new ReadonlyRecipeBookState(request.BookIndex));
                    break;
                case RecipeReadonlyBookMode.ShopDeleteDish:
                    _stateMachine.Switch(
                        new ShopDeleteDishState(request.OnExit));
                    break;
                case RecipeReadonlyBookMode.ActiveItemTarget:
                    _stateMachine.Switch(
                        new ActiveRecipeDishSelectState(
                            request.Item,
                            request.OnCancel,
                            request.OnTargetConfirmed));
                    break;
                case RecipeReadonlyBookMode.EventDeleteDish:
                    _stateMachine.Switch(
                        new EventRecipeDishDeleteState(
                            request.Title,
                            request.OnCancel,
                            target => request.OnTargetConfirmed?.Invoke(
                                target,
                                null)));
                    break;
            }
        }

        /// <summary>供外部（如金币变化）请求刷新当前工作区状态。</summary>
        public void Refresh()
        {
            if (_run != null && isActiveAndEnabled)
            {
                _stateMachine?.Refresh();
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

        private void EnsureWired()
        {
            if (_wired)
            {
                return;
            }

            _wired = true;
            _stateMachine ??= new RecipeReadonlyBookStateMachine(this);
            if (_titleText == null)
            {
                Transform title = transform.Find("Title");
                _titleText = title != null
                    ? title.GetComponent<Text>()
                    : null;
            }

            if (_backButton != null)
            {
                _backButton.onClick.RemoveAllListeners();
                _backButton.onClick.AddListener(
                    () => _stateMachine?.OnExitClicked());
            }
        }

        private void RebuildWarehouseForCurrentState()
        {
            ClearCompareOverlay();
            bool preserveScroll = _warehouse != null;
            float previousScroll = preserveScroll
                ? _warehouse.VerticalNormalizedPosition
                : 1f;
            ClearWarehouse();

            RecipeReadonlyBookState state = _stateMachine?.Current;
            if (state == null
                || _run == null
                || _warehouseContainer == null
                || _warehousePrefab == null
                || _dishPrefab == null)
            {
                return;
            }

            DisableLegacyWarehouseLayout();
            string title = state is ShopDeleteDishState
                ? $"删除食物　花费 {ShopService.DeleteCost(_run)} 金币"
                : state.PanelTitle;
            SetText(_titleText, title);
            SetButtonText(_backButton, state.ExitButtonText);

            const int bookIndex = 0;
            if (state.BookIndexFilter < 0
                || state.BookIndexFilter == bookIndex)
            {
                _warehouse = Instantiate(
                    _warehousePrefab,
                    _warehouseContainer);
                _warehouse.gameObject.name = "RecipeWarehouse";
                StretchWarehouseToContainer(_warehouse);

                RectTransform dishContainer = _warehouse.DishContainer;
                if (dishContainer != null)
                {
                    IReadOnlyList<RecipeBookSlot> entries =
                        EntriesForBook(bookIndex);
                    for (int dishIndex = 0;
                         dishIndex < entries.Count;
                         dishIndex++)
                    {
                        SpawnDish(
                            dishContainer,
                            entries[dishIndex],
                            bookIndex,
                            dishIndex,
                            state);
                    }
                }

                _warehouse.RefreshLayout();
                _warehouse.SetVerticalNormalizedPosition(
                    preserveScroll ? previousScroll : 1f);
            }

            _onChanged?.Invoke();
        }

        private void SpawnDish(
            RectTransform dishContainer,
            RecipeBookSlot slot,
            int bookIndex,
            int dishIndex,
            RecipeReadonlyBookState state)
        {
            string dishId = slot.DishId;
            DishDef def = _run.Database.GetDish(dishId);
            RecipeEditDishView dish = Instantiate(_dishPrefab, dishContainer);
            dish.gameObject.name =
                $"RecipeDish_{bookIndex + 1}_{dishIndex + 1}";
            dish.Bind(
                DishName(GameApp.Config.Tables, dishId),
                DishShapeText(dishId),
                bookIndex,
                dishIndex,
                false,
                state.CanClickDish ? OnRecipeDishClicked : null,
                def,
                null,
                null,
                ShowRecipeDishTips,
                HideRecipeDishTips,
                ComposeFlavorIds(def, slot.ExtraFlavorIds),
                DishIconPreviewMode.Warehouse);
            _spawnedDishes.Add(dish);
        }

        private IReadOnlyList<RecipeBookSlot> EntriesForBook(int bookIndex)
        {
            if (_readonlyEntries != null
                && _readonlyEntriesBookIndex == bookIndex)
            {
                return _readonlyEntries;
            }

            return _run != null && bookIndex == 0
                ? _run.RecipeEntries
                : Array.Empty<RecipeBookSlot>();
        }

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
            _stateMachine?.Current?.OnDishClicked(this, dish);
        }

        private RecipeEditDishView FindDish(int bookIndex, int dishIndex)
        {
            for (int i = 0; i < _spawnedDishes.Count; i++)
            {
                RecipeEditDishView dish = _spawnedDishes[i];
                if (dish != null
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
            if (dish == null || _run == null)
            {
                return;
            }

            FoodTipsView tips = _getFoodTips?.Invoke();
            if (tips == null)
            {
                return;
            }

            var target = new ActiveTarget(
                string.Empty,
                dish.BookIndex,
                dish.DishIndex,
                cfg.ItemTargetKind.RecipeDish);
            RecipeBookSlot slot = RecipeSlot(target);
            DishDef def = slot == null
                ? null
                : _run.Database.GetDish(slot.DishId);
            if (slot == null || def == null)
            {
                return;
            }

            _hoveredTipDish = dish;
            tips.Bind(BuildRecipeDishTipsData(def, slot));
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

        private void ClearWarehouse()
        {
            if (_warehouse != null)
            {
                Destroy(_warehouse.gameObject);
                _warehouse = null;
            }

            _spawnedDishes.Clear();
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
            Text text = button != null
                ? button.GetComponentInChildren<Text>(true)
                : null;
            SetText(text, value);
        }
    }
}
