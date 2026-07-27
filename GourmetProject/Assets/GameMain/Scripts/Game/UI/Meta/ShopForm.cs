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
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 商店「中部态」面板：作为 <c>BattleForm</c> 常驻壳的中部内容之一（不再是独立弹层）。
    /// 常驻壳（左列信息 / 行动轴 / 右列道具 / 固定菜谱）由 BattleForm 提供，本面板只负责中部四区
    /// （食物 / 碎片包 / 被动 / 主动）与「删除食物」入口。删除时切换到
    /// <see cref="RecipeReadonlyBookView"/> 选择目标并二次确认。
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
        [Header("Hover Tips")]
        [SerializeField] private FoodTipsView _foodTipsPrefab;
        [SerializeField] private ItemTipView _itemTipPrefab;

        [Header("Delete Dish")]
        [SerializeField] private Button _deleteDishButton;

        private readonly List<ShopEntry> _stock = new();
        private readonly List<GameObject> _spawned = new();
        private readonly List<ShopCardSlot> _buySlots = new();
        private GameRun _run;
        private bool _wired;
        private FoodTipsView _foodTipsView;
        private ItemTipView _itemTipView;

        private Action _onLeave;
        private Action _onOpenDeleteDish;
        private Func<ShopEntry, ShopBuyItemViewBase, bool> _onBuy;

        private sealed class ShopCardSlot
        {
            public ShopEntryKind Kind;
            public ShopEntry Entry;
            public ShopBuyItemViewBase Card;
        }

        private void Awake()
        {
            EnsureWired();
        }

        private void OnDisable()
        {
            ClearSpawned();
        }

        /// <summary>由商店页面协调器进入商店态时调用：渲染给定库存并展示商店购买区。</summary>
        /// <param name="run">当前肉鸽运行。</param>
        /// <param name="stock">页面协调器准备好的库存快照。</param>
        /// <param name="onLeave">点「离开商店」时回调（BattleForm 继续周循环编排）。</param>
        /// <param name="onOpenDeleteDish">点「删除食物」时回调：BattleForm 打开菜谱选择页。</param>
        public void Open(
            GameRun run,
            IReadOnlyList<ShopEntry> stock,
            Action onLeave,
            Action onOpenDeleteDish = null,
            Func<ShopEntry, ShopBuyItemViewBase, bool> onBuy = null)
        {
            EnsureWired();
            _run = run;
            ReplaceStock(stock);
            _onLeave = onLeave;
            _onOpenDeleteDish = onOpenDeleteDish;
            _onBuy = onBuy;

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

            if (_deleteDishButton != null)
            {
                _deleteDishButton.onClick.RemoveAllListeners();
                _deleteDishButton.onClick.AddListener(() => _onOpenDeleteDish?.Invoke());
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

            // UI 与购买共用 ShopService.CurrentPrice：重建卡片前刷新，避免被动道具/事件改价后仍显示旧表价。
            ShopService.RefreshStockPrices(_run, _stock);
            ClearSpawned();
            EnsureTipViews();

            SetText(_goldText, $"金币 {_run.Gold}");
            RefreshDeleteDishButton();
            BuildBuySection(ShopEntryKind.Dish, _foodContainer, _foodEmptyText, "暂无食物", _foodCardPrefab);
            BuildBuySection(ShopEntryKind.Fragment, _fragmentContainer, _fragmentEmptyText, "暂无碎片包", _fragmentCardPrefab);
            BuildBuySection(ShopEntryKind.PassiveItem, _passiveContainer, _passiveEmptyText, "暂无被动道具", _passiveCardPrefab);
            BuildBuySection(ShopEntryKind.ActiveItem, _activeContainer, _activeEmptyText, "暂无主动道具", _activeCardPrefab);
        }

        private void RefreshDeleteDishButton()
        {
            if (_deleteDishButton == null || _run == null)
            {
                return;
            }

            int cost = ShopService.DeleteCost(_run);
            Text label = _deleteDishButton.GetComponentInChildren<Text>(true);
            SetText(label, $"删除食物 -{cost}");
            _deleteDishButton.interactable = _run.RecipeEntries.Count > 0
                && _run.Gold >= cost
                && !new ItemRuntime(_run).BlockRemoveDish();
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

                CreateSlot(kind, container, prefab, entry, count);
                count++;
            }

            SetEmpty(emptyText, count == 0, emptyMessage);
            container.gameObject.SetActive(count > 0);
        }

        /// <summary>供页面协调器在库存或金币变化后回调：原槽位刷新商店购买区与上限文本。</summary>
        public void RefreshShop(GameRun run, IReadOnlyList<ShopEntry> stock)
        {
            _run = run;
            ReplaceStock(stock);
            if (_buySlots.Count == 0)
            {
                Rebuild();
                return;
            }

            RefreshWithoutReflow();
        }

        private void RefreshWithoutReflow()
        {
            if (_run == null)
            {
                return;
            }

            ShopService.RefreshStockPrices(_run, _stock);
            EnsureTipViews();
            _foodTipsView?.Hide();
            _itemTipView?.Hide();

            SetText(_goldText, $"金币 {_run.Gold}");
            RefreshDeleteDishButton();
            RefreshBuySection(
                ShopEntryKind.Dish,
                _foodContainer,
                _foodEmptyText,
                "暂无食物",
                _foodCardPrefab);
            RefreshBuySection(
                ShopEntryKind.Fragment,
                _fragmentContainer,
                _fragmentEmptyText,
                "暂无碎片包",
                _fragmentCardPrefab);
            RefreshBuySection(
                ShopEntryKind.PassiveItem,
                _passiveContainer,
                _passiveEmptyText,
                "暂无被动道具",
                _passiveCardPrefab);
            RefreshBuySection(
                ShopEntryKind.ActiveItem,
                _activeContainer,
                _activeEmptyText,
                "暂无主动道具",
                _activeCardPrefab);
        }

        private void RefreshBuySection(
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

            var slots = new List<ShopCardSlot>();
            var previousEntries = new List<ShopEntry>();
            foreach (ShopCardSlot slot in _buySlots)
            {
                if (slot.Kind != kind)
                {
                    continue;
                }

                slots.Add(slot);
                previousEntries.Add(slot.Entry);
            }

            List<ShopEntry> assignments = ReconcileSlotEntries(previousEntries, _stock, kind);
            for (int i = 0; i < assignments.Count; i++)
            {
                ShopEntry entry = assignments[i];
                if (i < slots.Count)
                {
                    if (entry != null)
                    {
                        BindSlot(slots[i], entry);
                    }
                    else
                    {
                        HideSlot(slots[i]);
                    }

                    continue;
                }

                if (entry != null)
                {
                    slots.Add(CreateSlot(kind, container, prefab, entry, i));
                }
            }

            int occupiedCount = 0;
            foreach (ShopEntry entry in assignments)
            {
                if (entry != null)
                {
                    occupiedCount++;
                }
            }

            // 已经出现过的槽位始终保留在布局中。售罄只清空视觉，不让同区剩余卡片重新居中。
            bool hasReservedSlots = slots.Count > 0;
            SetEmpty(emptyText, occupiedCount == 0 && !hasReservedSlots, emptyMessage);
            container.gameObject.SetActive(hasReservedSlots || occupiedCount > 0);
        }

        private ShopCardSlot CreateSlot(
            ShopEntryKind kind,
            RectTransform container,
            ShopBuyItemViewBase prefab,
            ShopEntry entry,
            int slotIndex)
        {
            ShopBuyItemViewBase card = Instantiate(prefab, container);
            card.gameObject.name = $"ShopBuy_{kind}_{slotIndex}";
            var slot = new ShopCardSlot
            {
                Kind = kind,
                Card = card,
            };

            _buySlots.Add(slot);
            _spawned.Add(card.gameObject);
            BindSlot(slot, entry);
            return slot;
        }

        private void BindSlot(ShopCardSlot slot, ShopEntry entry)
        {
            if (slot?.Card == null || entry == null || _run == null)
            {
                return;
            }

            slot.Entry = entry;
            Sprite icon = LoadEntryIcon(entry);
            DishDef dish = entry.Kind == ShopEntryKind.Dish
                ? _run.Database.GetDish(entry.Id)
                : null;
            slot.Card.Bind(new ShopBuyItemViewContext(
                _run,
                entry,
                _run.Gold >= entry.Price,
                icon,
                dish,
                BuyImmediate));
            BindBuyCardTip(slot.Card, entry);
            SetSlotVisible(slot.Card, true);
        }

        private static void HideSlot(ShopCardSlot slot)
        {
            if (slot?.Card == null)
            {
                return;
            }

            slot.Entry = null;
            SetSlotVisible(slot.Card, false);
        }

        private static void SetSlotVisible(ShopBuyItemViewBase card, bool visible)
        {
            if (card == null)
            {
                return;
            }

            CanvasGroup group = card.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = card.gameObject.AddComponent<CanvasGroup>();
            }

            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        private static List<ShopEntry> ReconcileSlotEntries(
            IReadOnlyList<ShopEntry> previousSlots,
            IReadOnlyList<ShopEntry> currentStock,
            ShopEntryKind kind)
        {
            var currentEntries = new List<ShopEntry>();
            if (currentStock != null)
            {
                foreach (ShopEntry entry in currentStock)
                {
                    if (entry != null && entry.Kind == kind)
                    {
                        currentEntries.Add(entry);
                    }
                }
            }

            int previousCount = previousSlots?.Count ?? 0;
            int slotCount = Mathf.Max(previousCount, currentEntries.Count);
            var result = new List<ShopEntry>(slotCount);
            for (int i = 0; i < slotCount; i++)
            {
                result.Add(null);
            }

            var assigned = new HashSet<ShopEntry>();
            for (int i = 0; i < previousCount; i++)
            {
                ShopEntry previous = previousSlots[i];
                if (previous != null
                    && currentEntries.Contains(previous)
                    && assigned.Add(previous))
                {
                    result[i] = previous;
                }
            }

            foreach (ShopEntry entry in currentEntries)
            {
                if (!assigned.Add(entry))
                {
                    continue;
                }

                int emptyIndex = result.IndexOf(null);
                if (emptyIndex >= 0)
                {
                    result[emptyIndex] = entry;
                }
                else
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        private bool BuyImmediate(ShopEntry entry, ShopBuyItemViewBase card)
        {
            return _onBuy != null && _onBuy.Invoke(entry, card);
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

            RectTransform hoverReceiver = entry.Kind == ShopEntryKind.Dish
                ? card.PurchaseFlySource
                : card.transform as RectTransform;
            GameObject triggerObject = hoverReceiver != null
                ? hoverReceiver.gameObject
                : card.gameObject;
            TipHoverTrigger trigger = triggerObject.GetComponent<TipHoverTrigger>();
            if (trigger == null)
            {
                trigger = triggerObject.AddComponent<TipHoverTrigger>();
            }

            trigger.SetTarget(card.TipPlacementTarget);
            trigger.SetFollowPointer(true);

            if (entry.Kind == ShopEntryKind.Dish)
            {
                BindDishTip(trigger, entry, card.TipPlacementTarget);
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
                    go.SetActive(false);
                    Destroy(go);
                }
            }

            _spawned.Clear();
            _buySlots.Clear();
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
