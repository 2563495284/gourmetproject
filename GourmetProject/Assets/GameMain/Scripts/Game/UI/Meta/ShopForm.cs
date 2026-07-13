using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 商店「中部态」面板：作为 <c>BattleForm</c> 常驻壳的中部内容之一（不再是独立弹层）。
    /// 常驻壳（左列信息 / 行动轴 / 右列道具 / 底部扇形菜谱条）由 BattleForm 提供，本面板只负责中部四区
    /// （食物 / 碎片包 / 被动 / 主动）与「编辑菜谱」入口。编辑菜谱已抽出为独立状态
    /// <see cref="RecipeEditPanel"/>，点入口时通过回调交回 BattleForm 状态机切换。
    /// </summary>
    public sealed class ShopForm : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private GameObject _shopPanel;

        [Header("Top")]
        [SerializeField] private Text _goldText;
        [SerializeField] private Button _leaveButton;

        [Header("Shop Sections")]
        [SerializeField] private RectTransform _foodContainer;
        [SerializeField] private RectTransform _fragmentContainer;
        [SerializeField] private RectTransform _passiveContainer;
        [SerializeField] private RectTransform _activeContainer;
        [SerializeField] private Text _foodEmptyText;
        [SerializeField] private Text _fragmentEmptyText;
        [SerializeField] private Text _passiveEmptyText;
        [SerializeField] private Text _activeEmptyText;
        [SerializeField] private ShopBuyCardView _buyCardPrefab;
        [SerializeField] private TargetArrowView _targetArrowPrefab;

        [Header("Hover Tips")]
        [SerializeField] private DishTooltipView _dishTooltipPrefab;
        [SerializeField] private ItemTipView _itemTipPrefab;

        [Header("Recipe Entry")]
        [SerializeField] private Button _editRecipeButton;
        [SerializeField] private Text _recipeLimitText;

        private readonly List<ShopEntry> _stock = new();
        private readonly List<GameObject> _spawned = new();
        private GameRun _run;
        private RecipeView _recipeView;
        private string _shopKey;
        private bool _wired;
        private TargetArrowView _activeArrow;
        private ShopEntry _targetingEntry;
        private ShopBuyCardView _targetingCard;
        private DishTooltipView _dishTooltipView;
        private ItemTipView _itemTipView;
        private bool _waitingForRecipeClick;
        private int _targetingFrame;

        private Action _onLeave;
        private Action _onChanged;
        private Action _onOpenRecipeEdit;
        private Action _onOpenTableEdit;
        private Action<ShopEntry, ShopBuyCardView> _onItemPurchased;

        private void Awake()
        {
            EnsureWired();
        }

        private void OnDisable()
        {
            CancelDishTargeting(restoreRecipeState: false);
            ClearSpawned();
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

        /// <summary>由 BattleForm 进入商店态时调用：刷新库存并展示商店购买区。</summary>
        /// <param name="onLeave">点「离开商店」时回调（BattleForm 继续周循环编排）。</param>
        /// <param name="onChanged">商店内数据变化（买卖）后回调，用于刷新常驻壳金币/道具与底部菜谱条。</param>
        /// <param name="onOpenRecipeEdit">点「编辑菜谱」时回调：BattleForm 切到编辑菜谱态（独立状态）。</param>
        /// <param name="onOpenTableEdit">购买碎片包后回调：BattleForm 切到餐桌编辑页手动拼贴。</param>
        public void Open(
            Action onLeave,
            Action onChanged,
            Action onOpenRecipeEdit = null,
            Action onOpenTableEdit = null,
            RecipeView recipeView = null,
            Action<ShopEntry, ShopBuyCardView> onItemPurchased = null)
        {
            EnsureWired();
            _onLeave = onLeave;
            _onChanged = onChanged;
            _onOpenRecipeEdit = onOpenRecipeEdit;
            _onOpenTableEdit = onOpenTableEdit;
            _onItemPurchased = onItemPurchased;
            _recipeView = recipeView;

            _run = GameRunContext.Current;
            if (_run == null)
            {
                _onLeave?.Invoke();
                return;
            }

            if (_shopPanel != null)
            {
                _shopPanel.SetActive(true);
            }

            RollStock();
            Rebuild();
        }

        private void EnsureWired()
        {
            if (_wired)
            {
                return;
            }

            _wired = true;

            if (_leaveButton != null)
            {
                _leaveButton.onClick.RemoveAllListeners();
                _leaveButton.onClick.AddListener(OnLeaveClicked);
            }

            if (_editRecipeButton != null)
            {
                _editRecipeButton.onClick.RemoveAllListeners();
                _editRecipeButton.onClick.AddListener(() => _onOpenRecipeEdit?.Invoke());
            }
        }

        private void RollStock()
        {
            _stock.Clear();
            _shopKey = GameRun.BuildShopKey(_run.WeekIndex, _run.CurrentDay);
            if (_run.HasPendingShopStock(_shopKey))
            {
                _stock.AddRange(_run.GetPendingShopStock(_shopKey));
                return;
            }

            IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Shop, _shopKey);
            IRandomStream lootRng = GameApp.Random.DomainStream(SeedDomains.Loot, $"shop_{_shopKey}");
            _stock.AddRange(ShopService.RollStock(GameApp.Config.Tables, _run, rng, lootRng));
            // 掷库存只写内存 pending（同一天重开商店复用同一份）；不存档，退出商店结算时才统一存。
            _run.SetPendingShopStock(_shopKey, _stock);
        }

        private void Rebuild()
        {
            CancelDishTargeting();
            ClearSpawned();
            EnsureTipViews();

            SetText(_goldText, $"金币 {_run.Gold}");
            BuildBuySection(ShopEntryKind.Dish, _foodContainer, _foodEmptyText, "暂无食物");
            BuildBuySection(ShopEntryKind.Fragment, _fragmentContainer, _fragmentEmptyText, "暂无碎片包");
            BuildBuySection(ShopEntryKind.PassiveItem, _passiveContainer, _passiveEmptyText, "暂无被动道具");
            BuildBuySection(ShopEntryKind.ActiveItem, _activeContainer, _activeEmptyText, "暂无主动道具");
            SetText(_recipeLimitText, $"{_run.RecipeBookCount}/{GameRun.MaxRecipeBookCount}");

            _onChanged?.Invoke();
        }

        private void BuildBuySection(ShopEntryKind kind, RectTransform container, Text emptyText, string emptyMessage)
        {
            if (container == null || _buyCardPrefab == null)
            {
                SetEmpty(emptyText, true, emptyMessage);
                return;
            }

            int count = 0;
            foreach (ShopEntry entry in _stock)
            {
                if (entry.Kind != kind)
                {
                    continue;
                }

                ShopBuyCardView card = Instantiate(_buyCardPrefab, container);
                card.gameObject.name = $"ShopBuy_{kind}_{count}";
                ShopEntry captured = entry;
                bool affordable = _run.Gold >= entry.Price;
                Sprite icon = LoadEntryIcon(entry);
                if (entry.Kind == ShopEntryKind.Dish)
                {
                    DishDef dish = _run.Database.GetDish(entry.Id);
                    card.BindDish(
                        entry.Name,
                        entry.Desc,
                        entry.Price,
                        affordable,
                        icon,
                        dish,
                        null,
                        view => BeginDishTargeting(view, captured),
                        (view, screenPoint) => EndDishTargeting(view, captured, screenPoint));
                }
                else
                {
                    card.Bind(
                        entry.Name,
                        entry.Desc,
                        entry.Price,
                        affordable,
                        icon,
                        view => BuyImmediate(captured, view));
                }

                BindBuyCardTip(card, captured);
                _spawned.Add(card.gameObject);
                count++;
            }

            SetEmpty(emptyText, count == 0, emptyMessage);
            container.gameObject.SetActive(count > 0);
        }

        /// <summary>供 BattleForm 在底部扇形「购买空菜谱」后回调：重建商店购买区与上限文本。</summary>
        public void RefreshShop()
        {
            if (_run != null)
            {
                Rebuild();
            }
        }

        private bool BuyImmediate(ShopEntry entry, ShopBuyCardView card)
        {
            if (!ShopService.Purchase(_run, entry))
            {
                return false;
            }

            if (entry.Kind == ShopEntryKind.PassiveItem || entry.Kind == ShopEntryKind.ActiveItem)
            {
                _onItemPurchased?.Invoke(entry, card);
            }

            FinishPurchasedEntry(entry);
            return true;
        }

        private void FinishPurchasedEntry(ShopEntry entry)
        {
            _stock.Remove(entry);
            _run.SetPendingShopStock(_shopKey, _stock);
            Rebuild();

            // 碎片包：购买后进入餐桌编辑页手动拼贴（金币已扣，待开包状态已置）。
            if (entry.Kind == ShopEntryKind.Fragment && _run.HasPendingFragmentPack && _onOpenTableEdit != null)
            {
                _onOpenTableEdit.Invoke();
            }
        }

        private void BeginDishTargeting(ShopBuyCardView card, ShopEntry entry)
        {
            if (_run == null || card == null || entry == null)
            {
                return;
            }

            if (_run.Gold < entry.Price)
            {
                card.PlayPurchaseFailed();
                return;
            }

            CancelDishTargeting();
            _targetingEntry = entry;
            _targetingCard = card;
            _waitingForRecipeClick = false;
            _targetingFrame = Time.frameCount;

            _recipeView?.SetState(RecipeView.RecipeState.Shown);
            _activeArrow = CreateTargetArrow(card.IconScreenCenter());
            UpdateTargetingHighlight(Mouse.current != null ? Mouse.current.position.ReadValue() : card.IconScreenCenter());
        }

        private void EndDishTargeting(ShopBuyCardView card, ShopEntry entry, Vector2 screenPoint)
        {
            if (_activeArrow == null || _targetingCard != card || _targetingEntry != entry)
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
            ShopEntry entry = _targetingEntry;
            ShopBuyCardView card = _targetingCard;
            if (!ShopService.PurchaseDishToBook(_run, entry, bookIndex))
            {
                card?.PlayPurchaseFailed();
                CancelDishTargeting();
                return;
            }

            CancelDishTargeting();
            FinishPurchasedEntry(entry);
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

        private void EnsureTipViews()
        {
            if (_dishTooltipView == null)
            {
                _dishTooltipView = CreateTipView(_dishTooltipPrefab, "DishTooltipView_Runtime")
                    ?? FindExistingTip<DishTooltipView>("DishTooltipView_Runtime");
            }

            if (_itemTipView == null)
            {
                _itemTipView = CreateTipView(_itemTipPrefab, "ItemTipView_Runtime")
                    ?? FindExistingTip<ItemTipView>("ItemTipView_Runtime");
            }

            MoveTipToTopLayer(_dishTooltipView);
            MoveTipToTopLayer(_itemTipView);
        }

        private void BindBuyCardTip(ShopBuyCardView card, ShopEntry entry)
        {
            if (card == null || entry == null)
            {
                return;
            }

            TipHoverTrigger trigger = card.GetComponent<TipHoverTrigger>();
            if (trigger == null)
            {
                trigger = card.gameObject.AddComponent<TipHoverTrigger>();
            }

            trigger.SetTarget(card.transform as RectTransform);

            if (entry.Kind == ShopEntryKind.Dish)
            {
                BindDishTip(trigger, entry);
                return;
            }

            if (entry.Kind == ShopEntryKind.PassiveItem || entry.Kind == ShopEntryKind.ActiveItem)
            {
                BindItemTip(trigger, entry);
                return;
            }

            trigger.ClearTip();
        }

        private void BindDishTip(TipHoverTrigger trigger, ShopEntry entry)
        {
            DishDef dish = _run?.Database.GetDish(entry.Id);
            if (trigger == null || _dishTooltipView == null || dish == null || _run?.Database == null)
            {
                trigger?.ClearTip();
                return;
            }

            trigger.SetTip(
                _dishTooltipView,
                _dishTooltipView.Show,
                _dishTooltipView.Hide,
                () => _dishTooltipView.Bind(dish, dish.SkillIds, dish.FlavorId, _run.Database));
        }

        private void BindItemTip(TipHoverTrigger trigger, ShopEntry entry)
        {
            cfg.Item item = GameApp.Config.Tables.TbItem.GetOrDefault(entry.Id);
            if (trigger == null || _itemTipView == null || item == null)
            {
                trigger?.ClearTip();
                return;
            }

            trigger.SetTip(_itemTipView, () => _itemTipView.Bind(item));
        }

        private T FindExistingTip<T>(string preferredName) where T : MonoBehaviour
        {
            Transform root = transform.root != null ? transform.root : transform;
            T[] tips = root.GetComponentsInChildren<T>(true);
            if (tips == null || tips.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < tips.Length; i++)
            {
                if (tips[i] != null && tips[i].gameObject.name == preferredName)
                {
                    return tips[i];
                }
            }

            return tips[0];
        }

        private T CreateTipView<T>(T prefab, string viewName) where T : MonoBehaviour
        {
            if (prefab == null)
            {
                return null;
            }

            T view = Instantiate(prefab, TipLayerParent(), false);
            view.gameObject.name = viewName;
            HideTipView(view);
            return view;
        }

        private Transform TipLayerParent()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            return canvas != null ? canvas.transform : transform;
        }

        private void MoveTipToTopLayer(MonoBehaviour view)
        {
            if (view == null)
            {
                return;
            }

            Transform parent = TipLayerParent();
            if (view.transform.parent != parent)
            {
                view.transform.SetParent(parent, false);
            }

            view.transform.SetAsLastSibling();
        }

        private static void HideTipView(MonoBehaviour view)
        {
            switch (view)
            {
                case ActionTipView actionTip:
                    actionTip.Hide();
                    break;
                case DishTooltipView dishTip:
                    dishTip.Hide();
                    break;
                default:
                    view.gameObject.SetActive(false);
                    break;
            }
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

            _targetingEntry = null;
            _targetingCard = null;
            _waitingForRecipeClick = false;
            _targetingFrame = -1;
        }

        private Sprite LoadEntryIcon(ShopEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            switch (entry.Kind)
            {
                case ShopEntryKind.PassiveItem:
                case ShopEntryKind.ActiveItem:
                {
                    cfg.Item item = GameApp.Config.Tables.TbItem.GetOrDefault(entry.Id);
                    Sprite sprite = RunItemSlotView.LoadIcon(item);
                    if (sprite != null)
                    {
                        return sprite;
                    }

                    return Resources.Load<Sprite>(entry.Kind == ShopEntryKind.ActiveItem
                        ? "Sprites/UI/ui_icon_shop_active"
                        : "Sprites/UI/ui_icon_shop_passive");
                }
                case ShopEntryKind.Dish:
                    return LoadDishIcon(entry.Id);
                case ShopEntryKind.Fragment:
                    return Resources.Load<Sprite>("Sprites/UI/white");
                default:
                    return null;
            }
        }

        private Sprite LoadDishIcon(string dishId)
        {
            DishDef dish = _run?.Database.GetDish(dishId);
            Sprite sprite = ContentIconLoader.LoadDish(dish);
            if (sprite != null)
            {
                return sprite;
            }

            return Resources.Load<Sprite>("Sprites/UI/ui_icon_shop_food");
        }

        private void OnLeaveClicked()
        {
            // 商店内买卖只改内存，不即时存档；离开商店 = 结算，由编排层（WeekLoopController）
            // 在 OnShopClosed 续接里 Commit（推进步数）并统一存最终态。这里只负责继续编排。
            CancelDishTargeting();
            _onLeave?.Invoke();
        }

        private void ClearSpawned()
        {
            if (_dishTooltipView != null)
            {
                _dishTooltipView.Hide();
            }

            if (_itemTipView != null)
            {
                _itemTipView.Hide();
            }

            foreach (GameObject go in _spawned)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }

            _spawned.Clear();
        }

        private static void SetEmpty(Text emptyText, bool visible, string message)
        {
            if (emptyText == null)
            {
                return;
            }

            emptyText.gameObject.SetActive(visible);
            emptyText.text = message ?? string.Empty;
        }

        private static void SetText(Text text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }
    }
}
