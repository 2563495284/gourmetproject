using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 常驻壳右栏道具组件：被动道具滚动网格（2 列）+ 固定 2 个主动道具槽（同 id 聚合、角标 xN）。
    /// 战斗中满足 targetKind 可用性的主动道具可点击使用，否则点击看信息；hover 显示道具 Tip。
    /// </summary>
    public sealed class BattleItemsColumn : MonoBehaviour
    {
        private const int PassiveSlotColumns = 2;
        private const int PassiveVisibleRows = 5;
        private const float PassiveSlotPadding = 2f;
        private const float PassiveSlotSpacing = 4f;
        private const float PassiveScrollEpsilon = 0.5f;

        [SerializeField] private RectTransform _passiveItemsContainer;
        [SerializeField] private RectTransform _passiveItemsContent;
        [SerializeField] private ScrollRect _passiveItemsScrollRect;
        [SerializeField] private RunItemSlotView _itemSlotPrefab;
        [SerializeField] private RunItemSlotView[] _activeItemSlots;

        private readonly List<RunItemSlotView> _passiveSlots = new List<RunItemSlotView>();
        private readonly Dictionary<string, RunItemSlotView> _passiveSlotByItemId = new Dictionary<string, RunItemSlotView>();
        private readonly Dictionary<string, RunItemSlotView> _activeSlotByItemId = new Dictionary<string, RunItemSlotView>();

        /// <summary>刷新右栏道具：被动网格 + 主动槽。onActiveItemClicked 用于战斗中使用主动道具，onShowItemInfo 用于查看信息。</summary>
        public void Refresh(
            GameRun run,
            BattleSession session,
            bool inBattle,
            ItemTipView tipView,
            Action<string> onActiveItemClicked,
            Action<ItemDefinition, RunItemState> onShowItemInfo)
        {
            ClearPassiveSlots();
            _activeSlotByItemId.Clear();
            if (run == null)
            {
                return;
            }

            cfg.Tables tables = GameApp.Config.Tables;
            RefreshPassive(run, tables, tipView, onShowItemInfo);
            RefreshActive(run, session, inBattle, tables, tipView, onActiveItemClicked, onShowItemInfo);
        }

        public RunItemSlotView GetItemSlot(string itemId, cfg.ItemKind kind, bool revealPassive = true)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return null;
            }

            Dictionary<string, RunItemSlotView> map = kind == cfg.ItemKind.Passive ? _passiveSlotByItemId : _activeSlotByItemId;
            if (!map.TryGetValue(itemId, out RunItemSlotView slot) || slot == null)
            {
                return null;
            }

            if (revealPassive && kind == cfg.ItemKind.Passive)
            {
                RevealPassiveSlot(slot);
            }

            return slot;
        }

        public bool TryGetItemFlyTarget(
            GameRun run,
            string itemId,
            cfg.ItemKind kind,
            RectTransform layer,
            out Vector2 center,
            out Vector2 size)
        {
            center = Vector2.zero;
            size = Vector2.zero;
            if (run == null || string.IsNullOrEmpty(itemId) || layer == null)
            {
                return false;
            }

            return kind == cfg.ItemKind.Passive
                ? TryGetPassiveFlyTarget(run, itemId, layer, out center, out size)
                : TryGetActiveFlyTarget(run, itemId, layer, out center, out size);
        }

        private void RefreshPassive(
            GameRun run,
            cfg.Tables tables,
            ItemTipView tipView,
            Action<ItemDefinition, RunItemState> onShowItemInfo)
        {
            RectTransform content = EnsurePassiveItemsContent();
            if (content == null || _itemSlotPrefab == null)
            {
                return;
            }

            var passive = new List<RunItemState>();
            foreach (RunItemState state in run.Items)
            {
                ItemDefinition item = ItemDefinition.Get(tables, state.ItemId, cfg.ItemKind.Passive);
                if (item != null)
                {
                    passive.Add(state);
                }
            }

            Vector2 slotSize = CalculatePassiveSlotSize();
            int rows = Mathf.Max(1, Mathf.CeilToInt(passive.Count / (float)PassiveSlotColumns));
            float contentHeight = CalculatePassiveContentHeight(rows, slotSize.y);
            ConfigurePassiveContent(content, contentHeight);
            ConfigurePassiveScroll(content, contentHeight);

            for (int i = 0; i < passive.Count; i++)
            {
                RunItemState state = passive[i];
                ItemDefinition item = ItemDefinition.Get(tables, state.ItemId, cfg.ItemKind.Passive);
                RunItemSlotView slot = Instantiate(_itemSlotPrefab, content);
                slot.gameObject.name = $"PassiveSlot_{i}";
                var rect = (RectTransform)slot.transform;
                LayoutPassiveSlot(rect, i, slotSize);

                ItemDefinition captured = item;
                RunItemState capturedState = state;
                slot.Bind(
                    RunItemSlotView.LoadIcon(item),
                    RunItemSlotView.ShortName(item.Name),
                    string.Empty,
                    RunItemSlotView.QualityColor(item.Quality),
                    true,
                    () => onShowItemInfo?.Invoke(captured, capturedState));
                slot.SetTip(tipView, captured);
                _passiveSlots.Add(slot);
                _passiveSlotByItemId[state.ItemId] = slot;
            }
        }

        private RectTransform EnsurePassiveItemsContent()
        {
            if (_passiveItemsContainer == null)
            {
                return null;
            }

            RemoveGridGuide();
            EnsurePassiveScrollComponents();

            if (_passiveItemsContent == null || _passiveItemsContent.parent != _passiveItemsContainer)
            {
                Transform content = _passiveItemsContainer.Find("Content");
                if (content == null)
                {
                    var contentObject = new GameObject("Content", typeof(RectTransform));
                    contentObject.transform.SetParent(_passiveItemsContainer, false);
                    _passiveItemsContent = contentObject.GetComponent<RectTransform>();
                }
                else
                {
                    _passiveItemsContent = content as RectTransform;
                }
            }

            _passiveItemsContent.gameObject.SetActive(true);
            if (_passiveItemsScrollRect != null)
            {
                _passiveItemsScrollRect.content = _passiveItemsContent;
                _passiveItemsScrollRect.viewport = _passiveItemsContainer;
            }

            return _passiveItemsContent;
        }

        private void RemoveGridGuide()
        {
            Transform guide = _passiveItemsContainer.Find("GridGuide");
            if (guide == null)
            {
                return;
            }

            Destroy(guide.gameObject);
        }

        private void EnsurePassiveScrollComponents()
        {
            if (_passiveItemsScrollRect == null)
            {
                _passiveItemsScrollRect = _passiveItemsContainer.GetComponent<ScrollRect>();
                if (_passiveItemsScrollRect == null)
                {
                    _passiveItemsScrollRect = _passiveItemsContainer.gameObject.AddComponent<ScrollRect>();
                }
            }

            Image raycastTarget = _passiveItemsContainer.GetComponent<Image>();
            if (raycastTarget == null)
            {
                raycastTarget = _passiveItemsContainer.gameObject.AddComponent<Image>();
                raycastTarget.color = Color.clear;
            }

            raycastTarget.raycastTarget = true;

            if (_passiveItemsContainer.GetComponent<RectMask2D>() == null)
            {
                _passiveItemsContainer.gameObject.AddComponent<RectMask2D>();
            }

            _passiveItemsScrollRect.horizontal = false;
            _passiveItemsScrollRect.movementType = ScrollRect.MovementType.Clamped;
            _passiveItemsScrollRect.scrollSensitivity = 24f;
        }

        private Vector2 CalculatePassiveSlotSize()
        {
            Rect viewportRect = _passiveItemsContainer.rect;
            float viewportWidth = viewportRect.width > 0f ? viewportRect.width : 180f;
            float viewportHeight = viewportRect.height > 0f ? viewportRect.height : 480f;
            float width = Mathf.Max(1f, viewportWidth - PassiveSlotPadding * 2f - PassiveSlotSpacing * (PassiveSlotColumns - 1));
            float height = Mathf.Max(1f, viewportHeight - PassiveSlotPadding * 2f - PassiveSlotSpacing * (PassiveVisibleRows - 1));
            return new Vector2(width / PassiveSlotColumns, height / PassiveVisibleRows);
        }

        private float CalculatePassiveContentHeight(int rows, float slotHeight)
        {
            float viewportHeight = GetPassiveViewportHeight();
            float requiredHeight = PassiveSlotPadding * 2f + rows * slotHeight + Mathf.Max(0, rows - 1) * PassiveSlotSpacing;
            return Mathf.Max(viewportHeight, requiredHeight);
        }

        private float GetPassiveViewportHeight()
        {
            float height = _passiveItemsContainer.rect.height;
            return height > 0f ? height : 480f;
        }

        private void ConfigurePassiveContent(RectTransform content, float contentHeight)
        {
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, contentHeight);
            content.localScale = Vector3.one;
        }

        private void ConfigurePassiveScroll(RectTransform content, float contentHeight)
        {
            if (_passiveItemsScrollRect == null)
            {
                return;
            }

            bool canScroll = contentHeight > GetPassiveViewportHeight() + PassiveScrollEpsilon;
            _passiveItemsScrollRect.vertical = canScroll;
            if (canScroll)
            {
                _passiveItemsScrollRect.verticalNormalizedPosition = 1f;
            }
            else
            {
                _passiveItemsScrollRect.StopMovement();
                content.anchoredPosition = Vector2.zero;
            }
        }

        private void RevealPassiveSlot(RunItemSlotView slot)
        {
            if (slot == null || _passiveItemsContent == null || _passiveItemsScrollRect == null)
            {
                return;
            }

            RectTransform slotRect = slot.transform as RectTransform;
            if (slotRect == null)
            {
                return;
            }

            float viewportHeight = GetPassiveViewportHeight();
            float scrollableHeight = Mathf.Max(0f, _passiveItemsContent.rect.height - viewportHeight);
            if (scrollableHeight <= PassiveScrollEpsilon)
            {
                _passiveItemsContent.anchoredPosition = Vector2.zero;
                return;
            }

            float currentTop = _passiveItemsContent.anchoredPosition.y;
            float slotTop = -slotRect.anchoredPosition.y;
            float slotBottom = slotTop + slotRect.rect.height;
            float nextTop = currentTop;

            if (slotTop < currentTop + PassiveSlotPadding)
            {
                nextTop = slotTop - PassiveSlotPadding;
            }
            else if (slotBottom > currentTop + viewportHeight - PassiveSlotPadding)
            {
                nextTop = slotBottom - viewportHeight + PassiveSlotPadding;
            }

            nextTop = Mathf.Clamp(nextTop, 0f, scrollableHeight);
            _passiveItemsScrollRect.StopMovement();
            _passiveItemsContent.anchoredPosition = new Vector2(_passiveItemsContent.anchoredPosition.x, nextTop);
            _passiveItemsScrollRect.verticalNormalizedPosition = scrollableHeight > 0f ? 1f - nextTop / scrollableHeight : 1f;
            Canvas.ForceUpdateCanvases();
        }

        private bool TryGetPassiveFlyTarget(GameRun run, string itemId, RectTransform layer, out Vector2 center, out Vector2 size)
        {
            center = Vector2.zero;
            size = Vector2.zero;

            RectTransform content = EnsurePassiveItemsContent();
            if (content == null)
            {
                return false;
            }

            int index = -1;
            int passiveCount = 0;
            cfg.Tables tables = GameApp.Config.Tables;
            foreach (RunItemState state in run.Items)
            {
                ItemDefinition item = ItemDefinition.Get(tables, state.ItemId, cfg.ItemKind.Passive);
                if (item == null)
                {
                    continue;
                }

                if (state.ItemId == itemId)
                {
                    index = passiveCount;
                }

                passiveCount++;
            }

            if (index < 0)
            {
                return false;
            }

            Vector2 slotSize = CalculatePassiveSlotSize();
            int rows = Mathf.Max(1, Mathf.CeilToInt(passiveCount / (float)PassiveSlotColumns));
            float contentHeight = CalculatePassiveContentHeight(rows, slotSize.y);
            ConfigurePassiveContent(content, contentHeight);
            ConfigurePassiveScroll(content, contentHeight);
            RevealProjectedPassiveSlot(index, slotSize, contentHeight);

            var probeObject = new GameObject("PassiveItemFlyTargetProbe", typeof(RectTransform));
            var probe = probeObject.GetComponent<RectTransform>();
            probe.SetParent(content, false);
            LayoutPassiveSlot(probe, index, slotSize);
            Canvas.ForceUpdateCanvases();

            bool ok = TryGetRectInLayer(probe, layer, out center, out size);
            Destroy(probeObject);
            return ok;
        }

        private void RevealProjectedPassiveSlot(int index, Vector2 slotSize, float contentHeight)
        {
            if (_passiveItemsContent == null || _passiveItemsScrollRect == null)
            {
                return;
            }

            float viewportHeight = GetPassiveViewportHeight();
            float scrollableHeight = Mathf.Max(0f, contentHeight - viewportHeight);
            if (scrollableHeight <= PassiveScrollEpsilon)
            {
                _passiveItemsContent.anchoredPosition = Vector2.zero;
                return;
            }

            int row = index / PassiveSlotColumns;
            float slotTop = PassiveSlotPadding + row * (slotSize.y + PassiveSlotSpacing);
            float slotBottom = slotTop + slotSize.y;
            float nextTop = Mathf.Clamp(slotBottom - viewportHeight + PassiveSlotPadding, 0f, scrollableHeight);

            _passiveItemsScrollRect.StopMovement();
            _passiveItemsContent.anchoredPosition = new Vector2(_passiveItemsContent.anchoredPosition.x, nextTop);
            _passiveItemsScrollRect.verticalNormalizedPosition = scrollableHeight > 0f ? 1f - nextTop / scrollableHeight : 1f;
        }

        private bool TryGetActiveFlyTarget(GameRun run, string itemId, RectTransform layer, out Vector2 center, out Vector2 size)
        {
            center = Vector2.zero;
            size = Vector2.zero;
            if (_activeItemSlots == null)
            {
                return false;
            }

            int index = -1;
            var seen = new HashSet<string>();
            cfg.Tables tables = GameApp.Config.Tables;
            foreach (RunItemState state in run.Items)
            {
                ItemDefinition item = ItemDefinition.Get(tables, state.ItemId, cfg.ItemKind.Active);
                if (item == null || !seen.Add(state.ItemId))
                {
                    continue;
                }

                int nextIndex = seen.Count - 1;
                if (state.ItemId == itemId)
                {
                    index = nextIndex;
                    break;
                }
            }

            if (index < 0 || index >= _activeItemSlots.Length || _activeItemSlots[index] == null)
            {
                return false;
            }

            RectTransform target = _activeItemSlots[index].transform as RectTransform;
            return TryGetRectInLayer(target, layer, out center, out size);
        }

        private static bool TryGetRectInLayer(RectTransform rect, RectTransform layer, out Vector2 center, out Vector2 size)
        {
            center = Vector2.zero;
            size = Vector2.zero;
            if (rect == null || layer == null)
            {
                return false;
            }

            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector3 first = layer.InverseTransformPoint(corners[0]);
            float minX = first.x;
            float maxX = first.x;
            float minY = first.y;
            float maxY = first.y;

            for (int i = 1; i < corners.Length; i++)
            {
                Vector3 local = layer.InverseTransformPoint(corners[i]);
                minX = Mathf.Min(minX, local.x);
                maxX = Mathf.Max(maxX, local.x);
                minY = Mathf.Min(minY, local.y);
                maxY = Mathf.Max(maxY, local.y);
            }

            size = new Vector2(Mathf.Max(1f, maxX - minX), Mathf.Max(1f, maxY - minY));
            center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            return true;
        }

        private static void LayoutPassiveSlot(RectTransform rect, int index, Vector2 slotSize)
        {
            int col = index % PassiveSlotColumns;
            int row = index / PassiveSlotColumns;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = slotSize;
            rect.anchoredPosition = new Vector2(
                PassiveSlotPadding + col * (slotSize.x + PassiveSlotSpacing),
                -PassiveSlotPadding - row * (slotSize.y + PassiveSlotSpacing));
            rect.localScale = Vector3.one;
        }

        private void RefreshActive(
            GameRun run,
            BattleSession session,
            bool inBattle,
            cfg.Tables tables,
            ItemTipView tipView,
            Action<string> onActiveItemClicked,
            Action<ItemDefinition, RunItemState> onShowItemInfo)
        {
            if (_activeItemSlots == null)
            {
                return;
            }

            // 主动道具多实例：按 id 聚合成一个槽，份数用角标 xN 展示。
            var activeStates = new List<RunItemState>();
            var activeCounts = new Dictionary<string, int>();
            foreach (RunItemState state in run.Items)
            {
                ItemDefinition item = ItemDefinition.Get(tables, state.ItemId, cfg.ItemKind.Active);
                if (item == null)
                {
                    continue;
                }

                if (activeCounts.TryGetValue(state.ItemId, out int held))
                {
                    activeCounts[state.ItemId] = held + 1;
                }
                else
                {
                    activeCounts[state.ItemId] = 1;
                    activeStates.Add(state);
                }
            }

            for (int i = 0; i < _activeItemSlots.Length; i++)
            {
                RunItemSlotView slot = _activeItemSlots[i];
                if (slot == null)
                {
                    continue;
                }

                if (i < activeStates.Count)
                {
                    RunItemState state = activeStates[i];
                    ItemDefinition item = ItemDefinition.Get(tables, state.ItemId, cfg.ItemKind.Active);
                    int held = activeCounts[state.ItemId];
                    string badge = held > 1 ? $"x{held}" : string.Empty;
                    ItemDefinition captured = item;
                    RunItemState capturedState = state;

                    // 战斗中：满足 targetKind 可用性的主动道具可点击使用；否则（含非战斗态）点击看信息。
                    bool usableNow = inBattle && session != null && !session.IsSettled
                        && ItemActiveUsage.CanUse(item, ActiveUseContextKind.Battle);
                    string capturedId = state.ItemId;
                    Action onClick = usableNow
                        ? (Action)(() => onActiveItemClicked?.Invoke(capturedId))
                        : () => onShowItemInfo?.Invoke(captured, capturedState);

                    slot.Bind(
                        RunItemSlotView.LoadIcon(item),
                        RunItemSlotView.ShortName(item.Name),
                        badge,
                        RunItemSlotView.QualityColor(item.Quality),
                        true,
                        onClick);
                    slot.SetTip(tipView, captured);
                    _activeSlotByItemId[state.ItemId] = slot;
                }
                else
                {
                    slot.SetEmpty();
                }
            }
        }

        private void ClearPassiveSlots()
        {
            foreach (RunItemSlotView slot in _passiveSlots)
            {
                if (slot != null)
                {
                    slot.ClearTip();
                    Destroy(slot.gameObject);
                }
            }

            _passiveSlots.Clear();
            _passiveSlotByItemId.Clear();
        }
    }
}
