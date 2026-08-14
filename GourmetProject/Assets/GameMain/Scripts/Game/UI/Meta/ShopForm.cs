using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.Tutorial;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 商店「中部态」面板：作为 <c>BattleForm</c> 常驻壳的中部内容之一（不再是独立弹层）。
    /// 常驻壳（左列信息 / 时间轴 / 右列装饰品和消耗品）由 BattleForm 提供，本面板负责中部购买区
    /// （食物 / 碎片包 / 装饰品/消耗品）与「删除食物」商店服务。购买该服务时切换到
    /// <see cref="RecipeReadonlyBookView"/> 选择目标并二次确认。
    /// </summary>
    public sealed class ShopForm : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private GameObject _shopPanel;

        [Header("Top")]
        [SerializeField] private TMP_Text _goldText;
        [SerializeField] private Button _leaveButton;

        [Header("Shop Sections")]
        [SerializeField] private RectTransform _foodContainer;
        [SerializeField] private RectTransform _passiveContainer;
        [SerializeField] private RectTransform _activeContainer;
        [SerializeField] private RectTransform _fragmentCardRoot;
        [Header("Hover Tips")]
        [SerializeField] private FoodTipsView _foodTipsPrefab;
        [SerializeField] private ItemTipView _itemTipPrefab;

        [Header("Delete Food Service")]
        [SerializeField] private RectTransform _deleteFoodCardRoot;

        private readonly List<ShopEntry> _stock = new();
        private readonly List<ShopCardSlot> _buySlots = new();
        private readonly List<ShopBuyItemViewBase> _foodCards = new();
        private readonly List<ShopBuyItemViewBase> _passiveCards = new();
        private readonly List<ShopBuyItemViewBase> _activeCards = new();
        private GameRun _run;
        private bool _wired;
        private FoodTipsView _foodTipsView;
        private ItemTipView _itemTipView;
        private ShopFragmentPackBuyItemView _fragmentCard;
        private ShopBuyItemViewBase _deleteFoodCard;

        private Action _onLeave;
        private Action _onOpenDeleteDish;
        private Func<ShopEntry, ShopBuyItemViewBase, bool> _onBuy;

        internal IReadOnlyList<ShopEntry> CurrentStock => _stock;

        private sealed class ShopCardSlot
        {
            public ShopEntry Entry;
            public ShopBuyItemViewBase Card;
        }

        private void Awake()
        {
            EnsureWired();
        }

        private void OnDisable()
        {
            ClearBoundSlots();
        }

        /// <summary>由商店页面协调器进入商店态时调用：渲染给定库存并展示商店购买区。</summary>
        /// <param name="run">当前肉鸽运行。</param>
        /// <param name="stock">页面协调器准备好的库存快照。</param>
        /// <param name="onLeave">点「离开商店」时回调（BattleForm 继续周循环编排）。</param>
        /// <param name="onOpenDeleteDish">点「删除食物」时回调：BattleForm 打开食谱选择页。</param>
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

            // UI 与购买共用 ShopService.CurrentPrice：重建卡片前刷新，避免装饰品/事件改价后仍显示旧表价。
            ShopService.RefreshStockPrices(_run, _stock);
            ClearBoundSlots();
            EnsureTipViews();

            SetText(_goldText, $"金币 {_run.Gold}");
            RefreshDeleteFoodSection();
            BindFixedSlotSection(ShopEntryKind.Dish, _foodContainer, _foodCards);
            BindFixedBuySection(ShopEntryKind.Fragment, ResolveFragmentCard());
            BindFixedSlotSection(ShopEntryKind.PassiveItem, _passiveContainer, _passiveCards);
            BindFixedSlotSection(ShopEntryKind.ActiveItem, _activeContainer, _activeCards);
        }

        private void RefreshDeleteFoodSection()
        {
            if (_deleteFoodCard == null && _deleteFoodCardRoot != null)
            {
                _deleteFoodCard = _deleteFoodCardRoot.GetComponent<ShopBuyItemViewBase>();
            }

            if (_deleteFoodCard == null || _run == null)
            {
                return;
            }

            _deleteFoodCard.gameObject.SetActive(true);
            int cost = ShopService.DeleteCost(_run);
            int remaining = ShopService.DeleteDishRemaining(_run);
            if (remaining <= 0)
            {
                _deleteFoodCard.gameObject.SetActive(false);
                return;
            }
            var entry = new ShopEntry(
                ShopEntryKind.Dish,
                "__delete_food_service__",
                "删除食物",
                "从食谱中选择一道食物删除。",
                cost,
                cost);
            bool canUse = _onOpenDeleteDish != null
                && ShopService.CanDeleteDish(_run);
            Sprite icon = Resources.Load<Sprite>("Sprites/Items/discount_remove");
            _deleteFoodCard.Bind(new ShopBuyItemViewContext(
                _run,
                entry,
                canUse,
                icon,
                null,
                OpenDeleteFoodService));
            _deleteFoodCard.SetPurchaseEnabled(canUse);
        }

        private bool OpenDeleteFoodService(ShopEntry entry, ShopBuyItemViewBase card)
        {
            if (_run == null
                || _onOpenDeleteDish == null
                || !ShopService.CanDeleteDish(_run))
            {
                return false;
            }

            _onOpenDeleteDish.Invoke();
            return true;
        }

        private ShopFragmentPackBuyItemView ResolveFragmentCard()
        {
            if (_fragmentCard == null && _fragmentCardRoot != null)
            {
                _fragmentCard = _fragmentCardRoot.GetComponent<ShopFragmentPackBuyItemView>();
            }

            return _fragmentCard;
        }

        private void BindFixedSlotSection(
            ShopEntryKind kind,
            RectTransform container,
            List<ShopBuyItemViewBase> cards)
        {
            if (container == null)
            {
                return;
            }

            cards.Clear();
            container.GetComponentsInChildren(true, cards);
            cards.Sort((left, right) =>
                left.transform.GetSiblingIndex().CompareTo(right.transform.GetSiblingIndex()));

            List<ShopEntry> entries = EntriesForKind(kind);
            container.gameObject.SetActive(entries.Count > 0);

            for (int i = 0; i < cards.Count; i++)
            {
                ShopBuyItemViewBase card = cards[i];
                bool hasEntry = i < entries.Count;
                card.gameObject.SetActive(hasEntry);
                if (hasEntry)
                {
                    BindFixedCard(entries[i], card);
                }
                else
                {
                    ClearBuyCardTip(card);
                }
            }

            if (entries.Count > cards.Count)
            {
                Debug.LogWarning(
                    $"{kind} 固定槽位不足：库存 {entries.Count}，槽位 {cards.Count}。",
                    this);
            }
        }

        private void BindFixedBuySection(ShopEntryKind kind, ShopBuyItemViewBase card)
        {
            if (card == null)
            {
                return;
            }

            List<ShopEntry> entries = EntriesForKind(kind);
            if (entries.Count == 0)
            {
                card.gameObject.SetActive(false);
                return;
            }

            card.gameObject.SetActive(true);
            BindFixedCard(entries[0], card);
        }

        private void BindFixedCard(ShopEntry entry, ShopBuyItemViewBase card)
        {
            var slot = new ShopCardSlot
            {
                Entry = entry,
                Card = card,
            };
            _buySlots.Add(slot);
            entry.StockChanged += OnEntryStockChanged;
            BindSlot(slot);
        }

        /// <summary>供页面协调器在库存或金币变化后回调：原槽位刷新商店购买区与上限文本。</summary>
        public void RefreshShop(GameRun run, IReadOnlyList<ShopEntry> stock)
        {
            _run = run;
            ReplaceStock(stock);
            if (_run == null)
            {
                return;
            }

            if (!TryReuseCurrentSlots())
            {
                Rebuild();
                return;
            }

            ShopService.RefreshStockPrices(_run, _stock);
            EnsureTipViews();
            _foodTipsView?.Hide();
            _itemTipView?.Hide();

            SetText(_goldText, $"金币 {_run.Gold}");
            RefreshDeleteFoodSection();
            foreach (ShopCardSlot slot in _buySlots)
            {
                BindSlot(slot);
            }
        }

        private bool TryReuseCurrentSlots()
        {
            var entries = new List<ShopEntry>();
            foreach (ShopEntry entry in _stock)
            {
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }

            if (entries.Count != _buySlots.Count)
            {
                return false;
            }

            var replacements = new List<ShopEntry>(_buySlots.Count);
            var used = new bool[entries.Count];
            foreach (ShopCardSlot slot in _buySlots)
            {
                int matchIndex = -1;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (used[i])
                    {
                        continue;
                    }

                    ShopEntry candidate = entries[i];
                    bool sameStableSlot = slot.Entry.SlotIndex >= 0
                        && candidate.SlotIndex == slot.Entry.SlotIndex
                        && candidate.Kind == slot.Entry.Kind;
                    if (sameStableSlot || ReferenceEquals(slot.Entry, candidate))
                    {
                        matchIndex = i;
                        break;
                    }
                }

                if (matchIndex < 0)
                {
                    return false;
                }

                used[matchIndex] = true;
                replacements.Add(entries[matchIndex]);
            }

            for (int i = 0; i < _buySlots.Count; i++)
            {
                ShopCardSlot slot = _buySlots[i];
                ShopEntry replacement = replacements[i];
                if (ReferenceEquals(slot.Entry, replacement))
                {
                    continue;
                }

                slot.Entry.StockChanged -= OnEntryStockChanged;
                slot.Entry = replacement;
                slot.Entry.StockChanged += OnEntryStockChanged;
            }

            return true;
        }

        private void BindSlot(ShopCardSlot slot)
        {
            if (slot?.Card == null || slot.Entry == null || _run == null)
            {
                return;
            }

            ShopEntry entry = slot.Entry;
            ClearBuyCardTip(slot.Card);
            Sprite icon = entry.IsStocked ? LoadEntryIcon(entry) : null;
            DishDef dish = entry.IsStocked && entry.Kind == ShopEntryKind.Dish
                ? _run.Database.GetDish(entry.Id)
                : null;
            bool canPurchase = entry.IsStocked
                && _run.Gold >= entry.Price
                && (entry.Kind != ShopEntryKind.Fragment || ShopService.CanPurchaseFragmentPack(_run));
            slot.Card.Bind(new ShopBuyItemViewContext(
                _run,
                entry,
                canPurchase,
                icon,
                dish,
                BuyImmediate));
            if (entry.IsStocked)
            {
                BindBuyCardTip(slot.Card, entry);
            }
        }

        private void OnEntryStockChanged(ShopEntry entry)
        {
            foreach (ShopCardSlot slot in _buySlots)
            {
                if (ReferenceEquals(slot.Entry, entry))
                {
                    BindSlot(slot);
                    return;
                }
            }
        }

        private List<ShopEntry> EntriesForKind(ShopEntryKind kind)
        {
            var entries = new List<ShopEntry>();
            foreach (ShopEntry entry in _stock)
            {
                if (entry != null && entry.Kind == kind)
                {
                    entries.Add(entry);
                }
            }

            bool allHaveStableSlot = true;
            foreach (ShopEntry entry in entries)
            {
                if (entry.SlotIndex < 0)
                {
                    allHaveStableSlot = false;
                    break;
                }
            }

            if (allHaveStableSlot)
            {
                entries.Sort((left, right) => left.SlotIndex.CompareTo(right.SlotIndex));
            }

            return entries;
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

        private static void ClearBuyCardTip(ShopBuyItemViewBase card)
        {
            if (card == null)
            {
                return;
            }

            TipHoverTrigger[] triggers = card.GetComponentsInChildren<TipHoverTrigger>(true);
            foreach (TipHoverTrigger trigger in triggers)
            {
                trigger?.ClearTip();
            }
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
                    BuildShopFlavorNames(dish, db),
                    countAs: FoodTipsDataFactory.ResolveIntrinsicCountAs(dish, db)),
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

                FoodTipsDataFactory.AppendSkillEntries(entries, skill);
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
            TutorialRuntime.ObserveItemShown(item);
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
            UGuiForm form = GetComponentInParent<UGuiForm>();
            Transform fallback = form != null ? form.UIGroupTransform : transform;
            return UIForms.ResolveTooltipLayer(fallback);
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
                    return Resources.Load<Sprite>("Sprites/UI/ui_icon_shop_fragment");
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

        private void ClearBoundSlots()
        {
            if (_foodTipsView != null)
            {
                _foodTipsView.Hide();
            }

            if (_itemTipView != null)
            {
                _itemTipView.Hide();
            }

            foreach (ShopCardSlot slot in _buySlots)
            {
                if (slot?.Entry != null)
                {
                    slot.Entry.StockChanged -= OnEntryStockChanged;
                }
            }

            _buySlots.Clear();
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }
    }
}
