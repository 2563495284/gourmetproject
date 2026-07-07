using System;
using System.Collections.Generic;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Runtime;
using UnityEngine;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 常驻壳右栏道具组件：被动道具网格（2 列，最多 10 个）+ 固定 2 个主动道具槽（同 id 聚合、角标 xN）。
    /// 战斗中满足触发时机的主动道具可点击使用，否则点击看信息；hover 显示道具 Tip。
    /// </summary>
    public sealed class BattleItemsColumn : MonoBehaviour
    {
        private const int PassiveSlotCapacity = 10;
        private const int PassiveSlotColumns = 2;

        [SerializeField] private RectTransform _passiveItemsContainer;
        [SerializeField] private RunItemSlotView _itemSlotPrefab;
        [SerializeField] private RunItemSlotView[] _activeItemSlots;

        private readonly List<RunItemSlotView> _passiveSlots = new List<RunItemSlotView>();

        /// <summary>刷新右栏道具：被动网格 + 主动槽。onActiveItemClicked 用于战斗中使用主动道具，onShowItemInfo 用于查看信息。</summary>
        public void Refresh(
            GameRun run,
            BattleSession session,
            bool inBattle,
            ItemTipView tipView,
            Action<string> onActiveItemClicked,
            Action<cfg.Item, RunItemState> onShowItemInfo)
        {
            ClearPassiveSlots();
            if (run == null)
            {
                return;
            }

            cfg.Tables tables = GameApp.Config.Tables;
            RefreshPassive(run, tables, tipView, onShowItemInfo);
            RefreshActive(run, session, inBattle, tables, tipView, onActiveItemClicked, onShowItemInfo);
        }

        private void RefreshPassive(
            GameRun run,
            cfg.Tables tables,
            ItemTipView tipView,
            Action<cfg.Item, RunItemState> onShowItemInfo)
        {
            if (_passiveItemsContainer == null || _itemSlotPrefab == null)
            {
                return;
            }

            var passive = new List<RunItemState>();
            foreach (RunItemState state in run.Items)
            {
                cfg.Item item = tables.TbItem.GetOrDefault(state.ItemId);
                if (item != null && item.Kind == cfg.ItemKind.Passive)
                {
                    passive.Add(state);
                }
            }

            int shown = Mathf.Min(PassiveSlotCapacity, passive.Count);
            int rows = Mathf.Max(1, Mathf.CeilToInt(PassiveSlotCapacity / (float)PassiveSlotColumns));
            float cellW = 1f / PassiveSlotColumns;
            float cellH = 1f / rows;
            for (int i = 0; i < shown; i++)
            {
                RunItemState state = passive[i];
                cfg.Item item = tables.TbItem.GetOrDefault(state.ItemId);
                RunItemSlotView slot = Instantiate(_itemSlotPrefab, _passiveItemsContainer);
                slot.gameObject.name = $"PassiveSlot_{i}";
                int col = i % PassiveSlotColumns;
                int row = i / PassiveSlotColumns;
                var rect = (RectTransform)slot.transform;
                rect.anchorMin = new Vector2(col * cellW, 1f - (row + 1) * cellH);
                rect.anchorMax = new Vector2((col + 1) * cellW, 1f - row * cellH);
                rect.offsetMin = new Vector2(2f, 2f);
                rect.offsetMax = new Vector2(-2f, -2f);
                rect.localScale = Vector3.one;

                string badge = state.Level > 1 ? $"Lv{state.Level}" : string.Empty;
                cfg.Item captured = item;
                RunItemState capturedState = state;
                slot.Bind(
                    RunItemSlotView.LoadIcon(item),
                    RunItemSlotView.ShortName(item.Name),
                    badge,
                    RunItemSlotView.QualityColor(item.Quality),
                    true,
                    () => onShowItemInfo?.Invoke(captured, capturedState));
                slot.SetTip(tipView, captured);
                _passiveSlots.Add(slot);
            }
        }

        private void RefreshActive(
            GameRun run,
            BattleSession session,
            bool inBattle,
            cfg.Tables tables,
            ItemTipView tipView,
            Action<string> onActiveItemClicked,
            Action<cfg.Item, RunItemState> onShowItemInfo)
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
                cfg.Item item = tables.TbItem.GetOrDefault(state.ItemId);
                if (item == null || item.Kind != cfg.ItemKind.Active)
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
                    cfg.Item item = tables.TbItem.GetOrDefault(state.ItemId);
                    int held = activeCounts[state.ItemId];
                    string badge = held > 1 ? $"x{held}" : string.Empty;
                    cfg.Item captured = item;
                    RunItemState capturedState = state;

                    // 战斗中：满足触发时机的主动道具可点击使用；否则（含非战斗态）点击看信息。
                    bool usableNow = inBattle && session != null && !session.IsSettled
                        && item.TriggerTiming == cfg.ItemTriggerTiming.BeforeEat;
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
        }
    }
}
