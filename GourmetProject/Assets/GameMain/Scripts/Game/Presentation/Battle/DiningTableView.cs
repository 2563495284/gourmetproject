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
        // 稳定桌面已由单 Mesh 承载；对象池只服务拖拽/编辑等少量临时格，无需常驻预热对象。
        private const int CellPoolPrewarm = 0;
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
        private const float SettlementOrderPulseRiseDuration = 0.09f;

        private bool _voidAsPlaceholder;

        [SerializeField] private DiningTableCellView _cellPrefab;
        [SerializeField] private BattleScopeRegionOutlineView _scopeRegionOutlinePrefab;

        private readonly HashSet<GridPos> _renderedCells = new HashSet<GridPos>();
        private readonly List<GridPos> _orderedRenderedCells = new List<GridPos>();
        private readonly Dictionary<int, BattleScopeRegionOutlineView> _scopeRegionOutlines = new Dictionary<int, BattleScopeRegionOutlineView>();
        private readonly List<DiningTableCellView> _dragFeedbackCells = new List<DiningTableCellView>();
        private readonly HashSet<GridPos> _presentationHiddenCells = new HashSet<GridPos>();
        private readonly HashSet<GridPos> _presentationSuppressedDisabledCells = new HashSet<GridPos>();
        private readonly HashSet<GridPos> _presentationRemovedTombstones = new HashSet<GridPos>();
        private readonly HashSet<GridPos> _presentationNormalRemovedCells = new HashSet<GridPos>();
        private readonly Dictionary<GridPos, GridPlacementFeedbackState> _dragFeedbackStates = new Dictionary<GridPos, GridPlacementFeedbackState>();
        private readonly List<Tween> _settlementPulseTweens = new List<Tween>();
        private GameObjectPool _cellPool;
        private GameObjectPool _scopeOutlinePool;
        private DiningTableBatchRenderer _batchRenderer;
        private DiningTableCellSprites _cellSprites;
        private float _cellSize;
        private GpTable _board;
        private Action<GridPos> _clicked;
        private Action<GridPos> _cellHoverEntered;
        private Action<GridPos> _cellHoverExited;
        private GridPos? _hoveredCell;
        private Camera _hoverCamera;

        public DiningTableCoordinateMapper Mapper { get; private set; }

        /// <summary>
        /// 生成餐桌批次。餐桌以本组件 transform 为局部帧（BoardRoot），
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

            ReconcileRenderedCells();
            foreach (GridPos pos in _orderedRenderedCells)
            {
                Color color;
                bool debuffed;
                if (_presentationHiddenCells.Contains(pos))
                {
                    color = VoidColor;
                    debuffed = false;
                }
                else if (_presentationRemovedTombstones.Contains(pos))
                {
                    color = EmptyColor;
                    debuffed = !_presentationNormalRemovedCells.Contains(pos);
                }
                else if (!_board.Exists(pos))
                {
                    color = _voidAsPlaceholder ? VoidPlaceholderColor : VoidColor;
                    debuffed = false;
                }
                else
                {
                    color = EmptyColor;
                    debuffed = _board.IsDisabled(pos)
                        && !_presentationSuppressedDisabledCells.Contains(pos);
                }

                _batchRenderer.SetCellState(pos, color, debuffed);
            }

            _batchRenderer.FlushPendingChanges();
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
            positions.RemoveAll(position => !_renderedCells.Contains(position));

            if (positions.Count == 0)
            {
                return;
            }

            CancelSettlementPulses();
            float stagger = positions.Count > 1
                ? Mathf.Min(
                    SettlementOrderPreferredStagger,
                    SettlementOrderMaximumStartSpan / (positions.Count - 1))
                : 0f;
            for (int i = 0; i < positions.Count; i++)
            {
                StartSettlementPulse(positions[i], i * stagger);
            }

            float totalDuration = (positions.Count - 1) * stagger + SettlementOrderPulseDuration;
            Tween timer = DOVirtual.DelayedCall(totalDuration, () => { })
                .SetUpdate(true)
                .SetLink(gameObject);
            try
            {
                await PresentationTween.AwaitCompletionAsync(timer, cancellationToken);
            }
            finally
            {
                CancelSettlementPulses();
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
            if (Mapper != null && _renderedCells.Contains(pos))
            {
                worldPosition = Mapper.CellCenter(pos);
                return true;
            }

            worldPosition = Vector3.zero;
            return false;
        }

        public bool TryGetCellWorldBounds(GridPos pos, out Bounds worldBounds)
        {
            if (Mapper != null && _renderedCells.Contains(pos))
            {
                worldBounds = Mapper.CellWorldBounds(pos);
                return true;
            }

            worldBounds = default;
            return false;
        }

        public void SetCellHoverCallbacks(Action<GridPos> entered, Action<GridPos> exited)
        {
            ClearHoveredCell();
            _cellHoverEntered = entered;
            _cellHoverExited = exited;
        }

        public void ClearTargetHighlights()
        {
            _batchRenderer?.ClearAllFeedback();
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
            if (_batchRenderer == null || !_renderedCells.Contains(pos))
            {
                return;
            }

            if (!selected && !hovered)
            {
                _batchRenderer.ClearCellFeedback(pos);
                return;
            }

            _batchRenderer.SetCellFeedback(pos, GridPlacementFeedbackPalette.Valid);
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
                if (!_renderedCells.Contains(cell) || _board == null || !_board.Exists(cell))
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
            _hoverCamera = Camera.main;
            EnsureBatchRenderer();
            EnsureCellPool();
            EnsureScopeOutlinePool();
        }

        private void Update()
        {
            if (Mapper == null
                || _board == null
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
            GridPos? next = _renderedCells.Contains(position)
                && _board.Exists(position)
                && Mapper.ContainsWorldPoint(position, mouseWorld)
                    ? position
                    : null;

            if (!_hoveredCell.Equals(next))
            {
                if (_hoveredCell.HasValue)
                {
                    _cellHoverExited?.Invoke(_hoveredCell.Value);
                }

                _hoveredCell = next;
                if (_hoveredCell.HasValue)
                {
                    _cellHoverEntered?.Invoke(_hoveredCell.Value);
                }
            }

            if (next.HasValue && _clicked != null && WorldInput.PrimaryPressedThisFrame)
            {
                _clicked.Invoke(next.Value);
            }
        }

        private void OnDisable()
        {
            ClearHoveredCell();
            CancelSettlementPulses();
        }

        private void OnDestroy()
        {
            ClearHoveredCell();
            CancelSettlementPulses();
            _cellPool?.Clear();
            _scopeOutlinePool?.Clear();
        }

        private void ClearHoveredCell()
        {
            GridPos? hovered = _hoveredCell;
            _hoveredCell = null;
            if (hovered.HasValue)
            {
                _cellHoverExited?.Invoke(hovered.Value);
            }
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

        private void EnsureBatchRenderer()
        {
            if (_batchRenderer == null)
            {
                _batchRenderer = GetComponent<DiningTableBatchRenderer>();
                if (_batchRenderer == null)
                {
                    _batchRenderer = gameObject.AddComponent<DiningTableBatchRenderer>();
                }
            }
        }

        private void ReconcileRenderedCells()
        {
            _renderedCells.Clear();
            if (_voidAsPlaceholder)
            {
                for (int y = 0; y < _board.Height; y++)
                {
                    for (int x = 0; x < _board.Width; x++)
                    {
                        _renderedCells.Add(new GridPos(x, y));
                    }
                }
            }
            else
            {
                foreach (GridPos position in _board.ExistingCells())
                {
                    _renderedCells.Add(position);
                }
            }

            foreach (GridPos position in _presentationRemovedTombstones)
            {
                if (_board.InBounds(position))
                {
                    _renderedCells.Add(position);
                }
            }

            _orderedRenderedCells.Clear();
            _orderedRenderedCells.AddRange(_renderedCells);
            _orderedRenderedCells.Sort(CompareGridPositions);
            EnsureBatchRenderer();
            Matrix4x4 visualMatrix = _cellPrefab != null
                ? _cellPrefab.BatchVisualLocalMatrix
                : Matrix4x4.identity;
            _batchRenderer.SetLayout(
                _renderedCells,
                Mapper,
                _cellSprites.Plate,
                visualMatrix);
        }

        private void StartSettlementPulse(GridPos position, float delaySeconds)
        {
            float pulse = 0f;
            Sequence sequence = DOTween.Sequence()
                .SetDelay(Mathf.Max(0f, delaySeconds))
                .SetUpdate(true)
                .SetLink(gameObject)
                .Append(DOTween.To(
                        () => pulse,
                        value =>
                        {
                            pulse = value;
                            _batchRenderer?.SetPulse(position, value);
                        },
                        1f,
                        SettlementOrderPulseRiseDuration)
                    .SetEase(Ease.OutCubic))
                .Append(DOTween.To(
                        () => pulse,
                        value =>
                        {
                            pulse = value;
                            _batchRenderer?.SetPulse(position, value);
                        },
                        0f,
                        SettlementOrderPulseDuration - SettlementOrderPulseRiseDuration)
                    .SetEase(Ease.InOutSine));
            sequence.OnComplete(() => _batchRenderer?.SetPulse(position, 0f));
            sequence.OnKill(() => _batchRenderer?.SetPulse(position, 0f));
            _settlementPulseTweens.Add(sequence);
        }

        internal void CancelSettlementPulses()
        {
            for (int i = 0; i < _settlementPulseTweens.Count; i++)
            {
                _settlementPulseTweens[i]?.Kill(false);
            }

            _settlementPulseTweens.Clear();
            _batchRenderer?.ClearPulses();
            _batchRenderer?.FlushPendingChanges();
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
            CancelSettlementPulses();
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
            _renderedCells.Clear();
            _orderedRenderedCells.Clear();
            _batchRenderer?.Clear();
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

        private static int CompareGridPositions(GridPos left, GridPos right)
        {
            int row = left.Y.CompareTo(right.Y);
            return row != 0 ? row : left.X.CompareTo(right.X);
        }

    }
}
