using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Data;
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
    /// <see cref="RecipeWorkspacePanel"/>，点入口时通过回调交回 BattleForm 状态机切换。
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
        [SerializeField] private ShopFoodBuyItemView _foodCardPrefab;
        [SerializeField] private ShopFragmentPackBuyItemView _fragmentCardPrefab;
        [SerializeField] private ShopPassiveItemBuyItemView _passiveCardPrefab;
        [SerializeField] private ShopActiveItemBuyItemView _activeCardPrefab;
        [SerializeField] private TargetArrowView _targetArrowPrefab;

        [Header("Hover Tips")]
        [SerializeField] private FoodTipsView _foodTipsPrefab;
        [SerializeField] private ItemTipView _itemTipPrefab;

        [Header("Recipe Entry")]
        [SerializeField] private Button _editRecipeButton;
        [SerializeField] private Text _recipeLimitText;

        private readonly List<ShopEntry> _stock = new();
        private readonly List<GameObject> _spawned = new();
        private GameRun _run;
        private RecipeView _recipeView;
        private bool _wired;
        private TargetArrowView _activeArrow;
        private ShopEntry _targetingEntry;
        private ShopBuyItemViewBase _targetingCard;
        private FoodTipsView _foodTipsView;
        private ItemTipView _itemTipView;
        private bool _waitingForRecipeClick;
        private int _targetingFrame;

        private Action _onLeave;
        private Action _onOpenRecipeWorkspace;
        private Func<ShopEntry, ShopBuyItemViewBase, bool> _onBuy;
        private Func<ShopEntry, int, ShopBuyItemViewBase, bool> _onBuyDishToBook;

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

        /// <summary>由商店页面协调器进入商店态时调用：渲染给定库存并展示商店购买区。</summary>
        /// <param name="run">当前肉鸽运行。</param>
        /// <param name="stock">页面协调器准备好的库存快照。</param>
        /// <param name="onLeave">点「离开商店」时回调（BattleForm 继续周循环编排）。</param>
        /// <param name="onOpenRecipeWorkspace">点「编辑菜谱」时回调：BattleForm 切到菜谱工作区态。</param>
        public void Open(
            GameRun run,
            IReadOnlyList<ShopEntry> stock,
            Action onLeave,
            Action onOpenRecipeWorkspace = null,
            RecipeView recipeView = null,
            Func<ShopEntry, ShopBuyItemViewBase, bool> onBuy = null,
            Func<ShopEntry, int, ShopBuyItemViewBase, bool> onBuyDishToBook = null)
        {
            EnsureWired();
            _run = run;
            ReplaceStock(stock);
            _onLeave = onLeave;
            _onOpenRecipeWorkspace = onOpenRecipeWorkspace;
            _onBuy = onBuy;
            _onBuyDishToBook = onBuyDishToBook;
            _recipeView = recipeView;

            if (_run == null)
            {
                _onLeave?.Invoke();
                return;
            }

            if (_shopPanel != null)
            {
                _shopPanel.SetActive(true);
            }

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
                _editRecipeButton.onClick.AddListener(() => _onOpenRecipeWorkspace?.Invoke());
            }
        }

        private void ReplaceStock(IReadOnlyList<ShopEntry> stock)
        {
            _stock.Clear();
            if (stock == null)
            {
                return;
            }

            _stock.AddRange(stock);
        }

        private void Rebuild()
        {
            if (_run == null)
            {
                return;
            }

            CancelDishTargeting();
            ClearSpawned();
            EnsureTipViews();

            SetText(_goldText, $"金币 {_run.Gold}");
            BuildBuySection(ShopEntryKind.Dish, _foodContainer, _foodEmptyText, "暂无食物", _foodCardPrefab);
            BuildBuySection(ShopEntryKind.Fragment, _fragmentContainer, _fragmentEmptyText, "暂无碎片包", _fragmentCardPrefab);
            BuildBuySection(ShopEntryKind.PassiveItem, _passiveContainer, _passiveEmptyText, "暂无被动道具", _passiveCardPrefab);
            BuildBuySection(ShopEntryKind.ActiveItem, _activeContainer, _activeEmptyText, "暂无主动道具", _activeCardPrefab);
            SetText(_recipeLimitText, $"{_run.RecipeBookCount}/{GameRun.MaxRecipeBookCount}");
        }

        private void BuildBuySection(
            ShopEntryKind kind,
            RectTransform container,
            Text emptyText,
            string emptyMessage,
            ShopBuyItemViewBase prefab)
        {
            if (container == null || prefab == null)
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

                ShopBuyItemViewBase card = Instantiate(prefab, container);
                card.gameObject.name = $"ShopBuy_{kind}_{count}";
                ShopEntry captured = entry;
                bool affordable = _run.Gold >= entry.Price;
                Sprite icon = LoadEntryIcon(entry);
                DishDef dish = entry.Kind == ShopEntryKind.Dish ? _run.Database.GetDish(entry.Id) : null;
                Action<ShopBuyItemViewBase, ShopEntry> onTargetDown = null;
                Action<ShopBuyItemViewBase, ShopEntry, Vector2> onTargetUp = null;
                if (entry.Kind == ShopEntryKind.Dish)
                {
                    onTargetDown = BeginDishTargeting;
                    onTargetUp = EndDishTargeting;
                }

                card.Bind(new ShopBuyItemViewContext(
                    _run,
                    captured,
                    affordable,
                    icon,
                    dish,
                    BuyImmediate,
                    onTargetDown,
                    onTargetUp));

                BindBuyCardTip(card, captured);
                _spawned.Add(card.gameObject);
                count++;
            }

            SetEmpty(emptyText, count == 0, emptyMessage);
            container.gameObject.SetActive(count > 0);
        }

        /// <summary>供页面协调器在库存或金币变化后回调：重建商店购买区与上限文本。</summary>
        public void RefreshShop(GameRun run, IReadOnlyList<ShopEntry> stock)
        {
            _run = run;
            ReplaceStock(stock);
            Rebuild();
        }

        private bool BuyImmediate(ShopEntry entry, ShopBuyItemViewBase card)
        {
            return _onBuy != null && _onBuy.Invoke(entry, card);
        }

        private void BeginDishTargeting(ShopBuyItemViewBase card, ShopEntry entry)
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

        private void EndDishTargeting(ShopBuyItemViewBase card, ShopEntry entry, Vector2 screenPoint)
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
            ShopBuyItemViewBase card = _targetingCard;
            if (_onBuyDishToBook == null || !_onBuyDishToBook.Invoke(entry, bookIndex, card))
            {
                card?.PlayPurchaseFailed();
                CancelDishTargeting();
                return;
            }

            CancelDishTargeting();
        }

        private TargetArrowView CreateTargetArrow(Vector2 startScreenPoint)
        {
            Canvas canvas = _recipeView != null ? _recipeView.GetComponentInParent<Canvas>() : GetComponentInParent<Canvas>();
            Transform parent = canvas != null ? canvas.transform : transform;
            if (_targetArrowPrefab == null)
            {
                Debug.LogError($"{nameof(ShopForm)} 缺少 TargetArrowView prefab。", this);
                return null;
            }

            TargetArrowView arrow = Instantiate(_targetArrowPrefab, parent);
            arrow.transform.SetAsLastSibling();
            arrow.SetupArrow(startScreenPoint);
            return arrow;
        }

        private void EnsureTipViews()
        {
            if (_foodTipsView == null)
            {
                _foodTipsView = CreateTipView(_foodTipsPrefab, "FoodTipsView_Runtime")
                    ?? FindExistingTip<FoodTipsView>("FoodTipsView_Runtime");
            }

            if (_itemTipView == null)
            {
                _itemTipView = CreateTipView(_itemTipPrefab, "ItemTipView_Runtime")
                    ?? FindExistingTip<ItemTipView>("ItemTipView_Runtime");
            }

            MoveTipToTopLayer(_foodTipsView);
            MoveTipToTopLayer(_itemTipView);
        }

        private void BindBuyCardTip(ShopBuyItemViewBase card, ShopEntry entry)
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
            trigger.SetFollowPointer(true);

            if (entry.Kind == ShopEntryKind.Dish)
            {
                BindDishTip(trigger, entry, card.transform as RectTransform);
                return;
            }

            if (entry.Kind == ShopEntryKind.PassiveItem || entry.Kind == ShopEntryKind.ActiveItem)
            {
                BindItemTip(trigger, entry);
                return;
            }

            trigger.ClearTip();
        }

        private void BindDishTip(TipHoverTrigger trigger, ShopEntry entry, RectTransform target)
        {
            DishDef dish = _run?.Database.GetDish(entry.Id);
            if (trigger == null || _foodTipsView == null || dish == null || _run?.Database == null)
            {
                trigger?.ClearTip();
                return;
            }

            trigger.SetFollowPointer(false);
            trigger.SetTip(
                _foodTipsView,
                _foodTipsView.Show,
                _foodTipsView.Hide,
                () =>
                {
                    _foodTipsView.Bind(BuildShopFoodTipsData(dish, _run.Database));
                    _foodTipsView.PlaceAroundRectTransform(target, GetComponentInParent<Canvas>());
                });
        }

        private FoodTipsData BuildShopFoodTipsData(DishDef dish, GameplayDatabase db)
        {
            return new FoodTipsData(
                new FoodSummaryTipsData(
                    dish.Name,
                    BuildShopFoodSkills(dish, db),
                    BuildShopFlavorNames(dish, db)),
                new FoodScoreTipsData(dish.Deliciousness, 1f),
                Array.Empty<FoodMaterialTipsEntry>(),
                BuildShopFlavorDetails(dish, db),
                Array.Empty<FoodInfoEntry>(),
                BuildShopSpecialTags(dish, db));
        }

        private IReadOnlyList<FoodInfoEntry> BuildShopFoodSkills(DishDef dish, GameplayDatabase db)
        {
            if (dish.SkillIds == null || db == null)
            {
                return Array.Empty<FoodInfoEntry>();
            }

            var entries = new List<FoodInfoEntry>();
            foreach (string skillId in dish.SkillIds)
            {
                SkillDef skill = db.GetSkill(skillId);
                if (skill == null)
                {
                    continue;
                }

                entries.Add(new FoodInfoEntry(skill.Name, skill.Desc));
            }

            return entries;
        }

        private IReadOnlyList<string> BuildShopFlavorNames(DishDef dish, GameplayDatabase db)
        {
            if (!dish.HasFlavor || db == null)
            {
                return Array.Empty<string>();
            }

            FlavorDef flavor = db.GetFlavor(dish.FlavorId);
            return flavor != null && !string.IsNullOrEmpty(flavor.Name)
                ? new[] { flavor.Name }
                : Array.Empty<string>();
        }

        private IReadOnlyList<FoodInfoEntry> BuildShopFlavorDetails(DishDef dish, GameplayDatabase db)
        {
            if (!dish.HasFlavor || db == null)
            {
                return Array.Empty<FoodInfoEntry>();
            }

            FlavorDef flavor = db.GetFlavor(dish.FlavorId);
            return flavor != null
                ? new[] { new FoodInfoEntry(flavor.Name, flavor.Desc) }
                : Array.Empty<FoodInfoEntry>();
        }

        private IReadOnlyList<FoodInfoEntry> BuildShopSpecialTags(DishDef dish, GameplayDatabase db)
        {
            if (dish.SkillIds == null || db == null)
            {
                return Array.Empty<FoodInfoEntry>();
            }

            var termIds = new List<string>();
            foreach (string skillId in dish.SkillIds)
            {
                SkillDef skill = db.GetSkill(skillId);
                AddUniqueRange(termIds, skill?.TermIds);
            }

            if (termIds.Count == 0)
            {
                return Array.Empty<FoodInfoEntry>();
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
            if (values == null)
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

        private void BindItemTip(TipHoverTrigger trigger, ShopEntry entry)
        {
            cfg.ItemKind kind = entry.Kind == ShopEntryKind.ActiveItem ? cfg.ItemKind.Active : cfg.ItemKind.Passive;
            ItemDefinition item = ItemDefinition.Get(GameApp.Config.Tables, entry.Id, kind);
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
                case FoodTipsView foodTip:
                    foodTip.Hide();
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
                    cfg.ItemKind kind = entry.Kind == ShopEntryKind.ActiveItem ? cfg.ItemKind.Active : cfg.ItemKind.Passive;
                    ItemDefinition item = ItemDefinition.Get(GameApp.Config.Tables, entry.Id, kind);
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
            if (_foodTipsView != null)
            {
                _foodTipsView.Hide();
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
