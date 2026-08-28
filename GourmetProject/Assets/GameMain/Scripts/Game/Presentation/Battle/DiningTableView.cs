using System;
using System.Collections.Generic;
using System.Threading;
using DG.Tweening;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Runtime.Pooling;
using Unity.Profiling;

namespace GourmetProject.Game.Presentation.Battle
{
    public sealed class DiningTableView : MonoBehaviour
    {
        private static readonly ProfilerMarker BuildMarker = new("Gourmet.Table.Build");
        private const int CellPoolPrewarm = 16;
        private const int CellPoolMaxInactive = 192;
        private const int OutlinePoolMaxInactive = 8;
        // 空格直接露出格子贴图本色（白色不染色）；虚格压暗。
        private static readonly Color EmptyColor = Color.white;
        private static readonly Color VoidColor = new Color(0.07f, 0.04f, 0.03f, 0.0f);

        // 餐桌编辑页：把「胃外虚格」显示为浅色占位（原型里的虚线格），让玩家看到可扩展的最大网格范围。
        private static readonly Color VoidPlaceholderColor = new Color(0.85f, 0.85f, 0.85f, 0.22f);
        private const int DragFeedbackSortingOrder = -80;
        private const int TransientRegionOutlineLayer = 999;
        private const float SettlementOrderPreferredStagger = 0.075f;
        private const float SettlementOrderMaximumStartSpan = 1.35f;
        private const float SettlementOrderPulseDuration = 0.24f;

        private bool _voidAsPlaceholder;

        [SerializeField] private DiningTableCellView _cellPrefab;
        [SerializeField] private BattleScopeRegionOutlineView _scopeRegionOutlinePrefab;

        private readonly Dictionary<GridPos, DiningTableCellView> _cells = new Dictionary<GridPos, DiningTableCellView>();
        private readonly Dictionary<int, BattleScopeRegionOutlineView> _scopeRegionOutlines = new Dictionary<int, BattleScopeRegionOutlineView>();
        private readonly List<DiningTableCellView> _dragFeedbackCells = new List<DiningTableCellView>();
        private readonly HashSet<GridPos> _presentationHiddenCells = new HashSet<GridPos>();
        private readonly HashSet<GridPos> _presentationSuppressedDisabledCells = new HashSet<GridPos>();
        private readonly HashSet<GridPos> _presentationRemovedTombstones = new HashSet<GridPos>();
        private readonly HashSet<GridPos> _presentationNormalRemovedCells = new HashSet<GridPos>();
        private readonly List<GridPos> _staleCellPositions = new List<GridPos>();
        private readonly Dictionary<GridPos, GridPlacementFeedbackState> _dragFeedbackStates = new Dictionary<GridPos, GridPlacementFeedbackState>();
        private GameObjectPool _cellPool;
        private GameObjectPool _scopeOutlinePool;
        private DiningTableCellSprites _cellSprites;
        private float _cellSize;
        private GpTable _board;
        private Action<GridPos> _clicked;
        private Action<DiningTableCellView> _cellHoverEntered;
        private Action<DiningTableCellView> _cellHoverExited;
        private Action<DiningTableCellView> _cellHoverEnterHandler;
        private Action<DiningTableCellView> _cellHoverExitHandler;
        private DiningTableCellView _hoveredCell;
        private Camera _hoverCamera;

        public DiningTableCoordinateMapper Mapper { get; private set; }

        /// <summary>
        /// 生成餐桌格。餐桌以本组件 transform 为局部帧（BoardRoot）：格子挂在其下并以 localPosition 摆放，
        /// 世界摆放/居中/缩放由调用方设置本 transform 的 position/scale 决定。
        /// </summary>
        public void Build(GpTable board, float cellSize, float gap, Action<GridPos> clicked, DiningTableCellView cellPrefab = null)
        {
            using (BuildMarker.Auto())
            {
                GpTable nextBoard = board ?? throw new ArgumentNullException(nameof(board));
                ClearHoveredCell();
                ReleaseTransientViews();
                if (cellPrefab != null && cellPrefab != _cellPrefab)
                {
                    ReleaseAllCells();
                    _cellPool?.Clear();
                    _cellPool = null;
                    _cellPrefab = cellPrefab;
                }

                _board = nextBoard;
                _clicked = clicked;
                EnsureCellPool();

                if (Mapper == null)
                {
                    Mapper = new DiningTableCoordinateMapper(
                        nextBoard.Width,
                        nextBoard.Height,
                        cellSize,
                        gap,
                        transform);
                }
                else
                {
                    Mapper.Configure(nextBoard.Width, nextBoard.Height, cellSize, gap, transform);
                }
                _cellSize = cellSize;
                _cellSprites = DiningTableCellSpriteResources.LoadDefault();
                if (!_cellSprites.IsValid)
                {
                    throw new InvalidOperationException("默认餐桌格 Sprite 缺失。");
                }

                _staleCellPositions.Clear();
                foreach (GridPos existingPosition in _cells.Keys)
                {
                    if (!nextBoard.InBounds(existingPosition))
                    {
                        _staleCellPositions.Add(existingPosition);
                    }
                }

                for (int i = 0; i < _staleCellPositions.Count; i++)
                {
                    GridPos stalePosition = _staleCellPositions[i];
                    ReturnCell(_cells[stalePosition]);
                    _cells.Remove(stalePosition);
                }

                for (int y = 0; y < nextBoard.Height; y++)
                {
                    for (int x = 0; x < nextBoard.Width; x++)
                    {
                        var pos = new GridPos(x, y);
                        if (!_cells.TryGetValue(pos, out DiningTableCellView cell) || cell == null)
                        {
                            cell = RentCell(transform);
                            if (cell == null)
                            {
                                continue;
                            }

                            _cells[pos] = cell;
                        }
                        else
                        {
                            cell.ResetTransientStateForBuild();
                        }

                        cell.Configure(pos, Mapper.CellCenterLocal(pos), cellSize, _cellSprites, _clicked);
                        cell.SetHoverCallbacks(_cellHoverEnterHandler, _cellHoverExitHandler);
                    }
                }

                Sync();
            }
        }

        private DiningTableCellView InstantiateCell()
        {
            return RentCell(transform);
        }

        /// <summary>餐桌编辑页开关：把胃外虚格显示为浅色占位（最大网格提示）。需再次 Sync 生效。</summary>
        public void ShowVoidAsPlaceholders(bool enabled)
        {
            _voidAsPlaceholder = enabled;
            Sync();
        }

        public void Sync()
        {
            if (_board == null)
            {
                return;
            }

            foreach (KeyValuePair<GridPos, DiningTableCellView> kv in _cells)
            {
                GridPos pos = kv.Key;
                DiningTableCellView view = kv.Value;
                view.SetSprites(CellSpritesFor(pos), _cellSize);
                if (_presentationHiddenCells.Contains(pos))
                {
                    view.SetColor(VoidColor);
                    view.SetDebuffed(false);
                }
                else if (_presentationRemovedTombstones.Contains(pos))
                {
                    view.SetColor(EmptyColor);
                    view.SetDebuffed(!_presentationNormalRemovedCells.Contains(pos));
                }
                else if (!_board.Exists(pos))
                {
                    view.SetColor(_voidAsPlaceholder ? VoidPlaceholderColor : VoidColor);
                    view.SetDebuffed(false);
                }
                else
                {
                    view.SetColor(EmptyColor);
                    view.SetDebuffed(
                        _board.IsDisabled(pos)
                        && !_presentationSuppressedDisabledCells.Contains(pos));
                }
            }
        }

        public void StageBossPresentation(BossDebuffPresentationPlan plan)
        {
            _presentationHiddenCells.Clear();
            _presentationSuppressedDisabledCells.Clear();
            _presentationRemovedTombstones.Clear();
            _presentationNormalRemovedCells.Clear();
            if (plan != null)
            {
                _presentationHiddenCells.UnionWith(plan.AddedCells);
                _presentationSuppressedDisabledCells.UnionWith(plan.DisabledCells);
                _presentationRemovedTombstones.UnionWith(plan.RemovedCells);
                _presentationNormalRemovedCells.UnionWith(plan.RemovedCells);
            }

            Sync();
        }

        public void RevealAddedCell(GridPos pos)
        {
            _presentationHiddenCells.Remove(pos);
            Sync();
        }

        public void RevealDisabledCell(GridPos pos)
        {
            _presentationSuppressedDisabledCells.Remove(pos);
            Sync();
        }

        public void RevealRemovedCell(GridPos pos)
        {
            _presentationNormalRemovedCells.Remove(pos);
            Sync();
        }

        public void FinishBossPresentation()
        {
            _presentationHiddenCells.Clear();
            _presentationSuppressedDisabledCells.Clear();
            _presentationNormalRemovedCells.Clear();
            Sync();
        }

        /// <summary>
        /// 用最终餐桌状态播放基础结算顺序。不存在格和 Boss 禁用格不会进入提示；反转规则下
        /// 整体倒序，因此演出始终和本场基础棋盘顺序一致。
        /// </summary>
        public async Awaitable PlaySettlementOrderHintAsync(
            bool reverseOrder,
            CancellationToken cancellationToken)
        {
            List<GridPos> positions = BuildSettlementOrderHintCells(_board, reverseOrder);
            var views = new List<DiningTableCellView>(positions.Count);
            foreach (GridPos position in positions)
            {
                if (_cells.TryGetValue(position, out DiningTableCellView view) && view != null)
                {
                    views.Add(view);
                }
            }

            if (views.Count == 0)
            {
                return;
            }

            float stagger = views.Count > 1
                ? Mathf.Min(
                    SettlementOrderPreferredStagger,
                    SettlementOrderMaximumStartSpan / (views.Count - 1))
                : 0f;
            for (int i = 0; i < views.Count; i++)
            {
                views[i].PlaySettlementOrderHintPulse(i * stagger);
            }

            float totalDuration = (views.Count - 1) * stagger + SettlementOrderPulseDuration;
            Tween timer = DOVirtual.DelayedCall(totalDuration, () => { })
                .SetUpdate(true)
                .SetLink(gameObject);
            try
            {
                await PresentationTween.AwaitCompletionAsync(timer, cancellationToken);
            }
            finally
            {
                foreach (DiningTableCellView view in views)
                {
                    view?.CancelSettlementOrderHintPulse();
                }
            }
        }

        internal static List<GridPos> BuildSettlementOrderHintCells(GpTable board, bool reverseOrder)
        {
            var ordered = new List<GridPos>();
            if (board == null)
            {
                return ordered;
            }

            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    var position = new GridPos(x, y);
                    if (board.Exists(position) && !board.IsDisabled(position))
                    {
                        ordered.Add(position);
                    }
                }
            }

            if (reverseOrder)
            {
                ordered.Reverse();
            }

            return ordered;
        }

        public bool TryGetCellWorldPosition(GridPos pos, out Vector3 worldPosition)
        {
            if (_cells.TryGetValue(pos, out DiningTableCellView view) && view != null)
            {
                worldPosition = view.transform.position;
                return true;
            }

            worldPosition = Vector3.zero;
            return false;
        }

        private DiningTableCellSprites CellSpritesFor(GridPos pos)
        {
            return _cellSprites;
        }

        public bool TryGetCellView(GridPos pos, out DiningTableCellView view)
        {
            return _cells.TryGetValue(pos, out view) && view != null;
        }

        public void SetCellHoverCallbacks(Action<DiningTableCellView> entered, Action<DiningTableCellView> exited)
        {
            ClearHoveredCell();
            _cellHoverEntered = entered;
            _cellHoverExited = exited;
            foreach (DiningTableCellView cell in _cells.Values)
            {
                if (cell != null)
                {
                    cell.SetHoverCallbacks(OnCellHoverEntered, OnCellHoverExited);
                }
            }
        }

        public void ClearTargetHighlights()
        {
            foreach (DiningTableCellView view in _cells.Values)
            {
                view?.ClearPlateFeedbackColor();
            }

            Sync();
        }

        public void ShowDragPlacementFeedback(DishDragPlacementResult result)
        {
            ShowGridPlacementFeedback(result?.ToGridPlacementFeedback(), dishPlacement: true);
        }

        public void ShowGridPlacementFeedback(GridPlacementFeedback result)
        {
            ShowGridPlacementFeedback(result, dishPlacement: false);
        }

        private void ShowGridPlacementFeedback(
            GridPlacementFeedback result,
            bool dishPlacement)
        {
            if (result == null || Mapper == null)
            {
                ClearDragPlacementFeedback();
                return;
            }

            _dragFeedbackStates.Clear();
            IReadOnlyList<GridPlacementFeedbackCell> resultCells = result.Cells;
            for (int i = 0; i < resultCells.Count; i++)
            {
                GridPlacementFeedbackCell cell = resultCells[i];
                _dragFeedbackStates[cell.Position] = cell.State;
            }

            // 餐桌格仍显示中心格的整体状态；食物只映射其实际占用格，
            // 避免中心格覆盖单格反馈或在不规则形状的空洞中多画一格。
            if (!dishPlacement)
            {
                _dragFeedbackStates[result.CenterCell] = result.OverallState;
            }

            EnsureDragFeedbackCount(_dragFeedbackStates.Count);
            if (_dragFeedbackCells.Count < _dragFeedbackStates.Count)
            {
                ClearDragPlacementFeedback();
                return;
            }

            int index = 0;
            foreach (KeyValuePair<GridPos, GridPlacementFeedbackState> entry in _dragFeedbackStates)
            {
                DiningTableCellView overlay = _dragFeedbackCells[index++];
                Color color = dishPlacement
                    ? GridPlacementFeedbackPalette.DishColorFor(result.OverallState, entry.Value)
                    : GridPlacementFeedbackPalette.ColorFor(entry.Value);
                overlay.gameObject.SetActive(true);
                overlay.transform.localRotation = Quaternion.identity;
                overlay.Configure(
                    entry.Key,
                    Mapper.CellCenterLocal(entry.Key),
                    _cellSize,
                    _cellSprites,
                    null,
                    updateName: false);
                overlay.SetInteractionEnabled(false);
                overlay.SetPlateFeedbackColor(color);
                overlay.SetSorting(BattleSorting.Fx, DragFeedbackSortingOrder);
            }

            for (; index < _dragFeedbackCells.Count; index++)
            {
                _dragFeedbackCells[index]?.gameObject.SetActive(false);
            }
        }

        public void ClearDragPlacementFeedback()
        {
            foreach (DiningTableCellView overlay in _dragFeedbackCells)
            {
                if (overlay != null)
                {
                    overlay.gameObject.SetActive(false);
                }
            }
        }

        public void SetTargetHighlight(GridPos pos, bool selected, bool hovered)
        {
            if (!TryGetCellView(pos, out DiningTableCellView view))
            {
                return;
            }

            if (!selected && !hovered)
            {
                view.ClearPlateFeedbackColor();
                return;
            }

            view.SetPlateFeedbackColor(GridPlacementFeedbackPalette.Valid);
        }

        public void ClearScopeHighlights(BattleScopeHighlightChannel channel)
        {
            foreach (KeyValuePair<int, BattleScopeRegionOutlineView> pair in _scopeRegionOutlines)
            {
                if (ChannelFromScopeKey(pair.Key) == channel)
                {
                    pair.Value?.Hide();
                }
            }
        }

        public void ClearAllScopeHighlights()
        {
            foreach (BattleScopeRegionOutlineView outline in _scopeRegionOutlines.Values)
            {
                outline?.Hide();
            }
        }

        /// <summary>
        /// 显示一组任意网格坐标的不规则外轮廓。餐桌格暂放时目标格尚未写入餐桌，
        /// 因此这里不要求坐标已经是 ExistingCell。
        /// </summary>
        public void ShowTransientGridRegionOutline(IReadOnlyList<GridPos> cells, Color color, float width)
        {
            ShowRegionOutline(
                cells,
                BattleScopeHighlightChannel.Persistent,
                TransientRegionOutlineLayer,
                color,
                width,
                null);
        }

        public void ClearTransientGridRegionOutline()
        {
            int key = ScopeLayerKey(
                BattleScopeHighlightChannel.Persistent,
                TransientRegionOutlineLayer);
            if (_scopeRegionOutlines.TryGetValue(key, out BattleScopeRegionOutlineView outline))
            {
                outline?.Hide();
            }
        }

        public void SetScopeRegionHighlight(
            IReadOnlyList<GridPos> cells,
            BattleScopeHighlightChannel channel,
            int layer,
            Color color,
            float width,
            Material materialOverride = null)
        {
            if (cells == null || cells.Count == 0 || Mapper == null)
            {
                return;
            }

            bool hasCell = false;
            var validCells = new List<GridPos>();
            foreach (GridPos cell in cells)
            {
                if (!_cells.ContainsKey(cell) || _board == null || !_board.Exists(cell))
                {
                    continue;
                }

                hasCell = true;
                validCells.Add(cell);
            }

            if (!hasCell)
            {
                return;
            }

            ShowRegionOutline(validCells, channel, layer, color, width, materialOverride);
        }

        private void ShowRegionOutline(
            IReadOnlyList<GridPos> cells,
            BattleScopeHighlightChannel channel,
            int layer,
            Color color,
            float width,
            Material materialOverride)
        {
            if (cells == null || cells.Count == 0 || Mapper == null)
            {
                return;
            }

            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;
            foreach (GridPos cell in cells)
            {
                minX = Mathf.Min(minX, cell.X);
                minY = Mathf.Min(minY, cell.Y);
                maxX = Mathf.Max(maxX, cell.X);
                maxY = Mathf.Max(maxY, cell.Y);
            }

            BattleScopeRegionOutlineView outline = EnsureScopeRegionOutline(channel, layer);
            if (outline == null)
            {
                return;
            }

            Vector3 firstCenter = Mapper.CellCenterLocal(new GridPos(minX, minY));
            Vector3 lastCenter = Mapper.CellCenterLocal(new GridPos(maxX, maxY));
            Vector3 localCenter = (firstCenter + lastCenter) * 0.5f;
            Vector2 localSize = new Vector2(
                Mathf.Abs(lastCenter.x - firstCenter.x) + _cellSize,
                Mathf.Abs(lastCenter.y - firstCenter.y) + _cellSize);
            outline.Show(
                channel,
                layer,
                cells,
                minX,
                minY,
                maxX,
                maxY,
                localCenter,
                localSize,
                color,
                width,
                materialOverride);
        }

        public void SetAllExistingScopeHighlight(
            BattleScopeHighlightChannel channel,
            int layer,
            Color color,
            float width,
            Material materialOverride = null)
        {
            if (_board == null)
            {
                return;
            }

            SetScopeRegionHighlight(
                _board.ExistingCells(),
                channel,
                layer,
                color,
                width,
                materialOverride);
        }

        private void Awake()
        {
            _cellHoverEnterHandler = OnCellHoverEntered;
            _cellHoverExitHandler = OnCellHoverExited;
            _hoverCamera = Camera.main;
            EnsureCellPool();
            EnsureScopeOutlinePool();
        }

        private void Update()
        {
            if ((_cellHoverEntered == null && _cellHoverExited == null)
                || Mapper == null
                || WorldInput.PointerOverUi)
            {
                ClearHoveredCell();
                return;
            }

            if (_hoverCamera == null)
            {
                _hoverCamera = Camera.main;
            }

            if (_hoverCamera == null)
            {
                ClearHoveredCell();
                return;
            }

            Vector3 mouseWorld = WorldInput.MouseWorld(_hoverCamera);
            GridPos position = Mapper.NearestCell(mouseWorld);
            DiningTableCellView next = null;
            if (_cells.TryGetValue(position, out DiningTableCellView candidate)
                && candidate != null
                && candidate.ContainsWorldPoint(mouseWorld))
            {
                next = candidate;
            }

            if (!ReferenceEquals(_hoveredCell, next))
            {
                _hoveredCell?.SetHoveredFromTable(false);
                _hoveredCell = next;
            }

            // 子层级被禁用时格子会自行清 hover；同一个候选再次启用后需要重新进入。
            _hoveredCell?.SetHoveredFromTable(true);
        }

        private void OnDisable()
        {
            ClearHoveredCell();
        }

        private void OnDestroy()
        {
            ClearHoveredCell();
            _cellPool?.Clear();
            _scopeOutlinePool?.Clear();
        }

        private void ClearHoveredCell()
        {
            DiningTableCellView hovered = _hoveredCell;
            _hoveredCell = null;
            hovered?.SetHoveredFromTable(false);
        }

        internal DiningTableCellView RentCell(Transform parent)
        {
            EnsureCellPool();
            if (_cellPool == null)
            {
                Debug.LogError($"{nameof(DiningTableView)} 缺少 DiningTableCell prefab。", this);
                return null;
            }

            return _cellPool.Get<DiningTableCellView>(parent != null ? parent : transform);
        }

        internal void ReturnCell(DiningTableCellView cell)
        {
            if (cell == null)
            {
                return;
            }

            EnsureCellPool();
            if (_cellPool != null)
            {
                _cellPool.Release(cell);
            }
            else if (Application.isPlaying)
            {
                Destroy(cell.gameObject);
            }
            else
            {
                DestroyImmediate(cell.gameObject);
            }
        }

        private void EnsureCellPool()
        {
            if (_cellPool != null || _cellPrefab == null)
            {
                return;
            }

            _cellPool = new GameObjectPool(
                _cellPrefab.gameObject,
                transform,
                CellPoolPrewarm,
                CellPoolMaxInactive,
                onGet: go => go.GetComponent<DiningTableCellView>()?.PrepareForReuse(),
                onRelease: go => go.GetComponent<DiningTableCellView>()?.ResetForPool());
        }

        private void EnsureScopeOutlinePool()
        {
            if (_scopeOutlinePool != null || _scopeRegionOutlinePrefab == null)
            {
                return;
            }

            _scopeOutlinePool = new GameObjectPool(
                _scopeRegionOutlinePrefab.gameObject,
                transform,
                prewarm: 0,
                maxInactive: OutlinePoolMaxInactive,
                onRelease: go => go.GetComponent<BattleScopeRegionOutlineView>()?.ResetForPool());
        }

        private void ReleaseTransientViews()
        {
            _presentationHiddenCells.Clear();
            _presentationSuppressedDisabledCells.Clear();
            _presentationRemovedTombstones.Clear();
            _presentationNormalRemovedCells.Clear();
            foreach (DiningTableCellView overlay in _dragFeedbackCells)
            {
                if (overlay != null)
                {
                    ReturnCell(overlay);
                }
            }

            _dragFeedbackCells.Clear();
            foreach (BattleScopeRegionOutlineView outline in _scopeRegionOutlines.Values)
            {
                if (outline != null)
                {
                    _scopeOutlinePool?.Release(outline);
                }
            }

            _scopeRegionOutlines.Clear();
        }

        private void ReleaseAllCells()
        {
            foreach (DiningTableCellView cell in _cells.Values)
            {
                if (cell != null)
                {
                    ReturnCell(cell);
                }
            }

            _cells.Clear();
        }

        private void EnsureDragFeedbackCount(int count)
        {
            while (_dragFeedbackCells.Count < count)
            {
                DiningTableCellView overlay = InstantiateCell();
                if (overlay == null)
                {
                    return;
                }

                overlay.name = "DragPlacementFeedback";
                overlay.SetInteractionEnabled(false);
                overlay.gameObject.SetActive(false);
                _dragFeedbackCells.Add(overlay);
            }
        }

        private BattleScopeRegionOutlineView EnsureScopeRegionOutline(
            BattleScopeHighlightChannel channel,
            int layer)
        {
            int key = ScopeLayerKey(channel, layer);
            if (_scopeRegionOutlines.TryGetValue(key, out BattleScopeRegionOutlineView existing)
                && existing != null)
            {
                return existing;
            }

            if (_scopeRegionOutlinePrefab == null)
            {
                Debug.LogError($"{nameof(DiningTableView)} 缺少 ScopeRegionOutline prefab。", this);
                return null;
            }

            EnsureScopeOutlinePool();
            BattleScopeRegionOutlineView outline = _scopeOutlinePool != null
                ? _scopeOutlinePool.Get<BattleScopeRegionOutlineView>(transform)
                : null;
            if (outline == null)
            {
                return null;
            }

            outline.name = $"ScopeRegion_{channel}_{layer}";
            outline.Hide();
            _scopeRegionOutlines[key] = outline;
            return outline;
        }

        private static int ScopeLayerKey(BattleScopeHighlightChannel channel, int layer)
        {
            return ((int)channel * 1000) + Mathf.Clamp(layer, 0, 999);
        }

        private static BattleScopeHighlightChannel ChannelFromScopeKey(int key)
        {
            return (BattleScopeHighlightChannel)Mathf.Max(0, key / 1000);
        }

        private void OnCellHoverEntered(DiningTableCellView cell)
        {
            _cellHoverEntered?.Invoke(cell);
        }

        private void OnCellHoverExited(DiningTableCellView cell)
        {
            _cellHoverExited?.Invoke(cell);
        }

    }
}
