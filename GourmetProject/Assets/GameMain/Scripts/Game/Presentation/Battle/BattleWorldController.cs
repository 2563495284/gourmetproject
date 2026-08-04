using System;
using System.Collections.Generic;
using System.Threading;
using DG.Tweening;
using GourmetProject.Game;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using GourmetProject.Runtime;
using UnityEngine;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 经营挑战内场景表现根控制器：餐桌、后厨、手牌区与拖拽放置。
    /// 现以场景内组件存在：背景/餐桌根/各锚点/分数文本/固定按钮均在 Battle.unity 摆好并通过 SerializeField 注入，
    /// 运行时只生成数据驱动内容（餐桌格随胃尺寸、食物、装饰品和消耗品槽、结算特效）。
    /// </summary>
    public sealed class BattleWorldController : MonoBehaviour
    {
        public const float Gap = DiningTableLayout.Gap;
        private const float MaxCellSize = DiningTableLayout.MaxCellSize;
        private const float MinCellSize = DiningTableLayout.MinCellSize;

        // 餐桌居中定位的底部边距：Food 态给出菜口让 2.7，编辑态还要给候选托盘条让到 3.6。
        private const float FoodTableBottomMargin = 2.7f;
        private const float EditTableBottomMargin = 3.6f;
        private const float TemporaryAreaPadding = 0.12f;
        private const float TemporaryAreaPiecePaddingRatio = 0.84f;
        private const float TemporaryAreaVisibleRatio = 0.7f;
        private const float TemporaryAreaLayoutDuration = 0.18f;
        private const float TemporaryAreaFadeDuration = 0.18f;
        private const float TemporaryAreaFlyDuration = 0.38f;
        private const int TemporaryAreaSortingStride = 20;
        private const int PassiveSlotCapacity = 10;
        private const int PassiveSlotColumns = 2;
        // 回退视口半宽/半高（16:9 参考：orthographicSize 5.4）。
        private const float FallbackHalfW = 9.6f;
        private const float FallbackHalfH = 5.4f;

        // —— 场景内摆好的静态引用 ——
        [Header("Scene Refs")]
        [SerializeField] private Camera _camera;
        [SerializeField] private DiningTableView _boardView;
        [SerializeField] private Transform _piecesRoot;
        [SerializeField] private Transform _fxRoot;
        [SerializeField] private Transform _passiveItemsRoot;
        [SerializeField] private Transform _activeItemsRoot;
        [SerializeField] private SettlementSequencer _sequencer;
        [SerializeField] private BattleScopeHighlightController _scopeHighlights;
        [SerializeField] private BattleDoodleController _doodle;
        [Tooltip("Battle 场景内可直接移动和缩放的餐桌布局区域；存在时优先于 HUD BoardArea。")]
        [SerializeField] private RectTransform _sceneBoardArea;
        [Tooltip("麻风味旋转后的食物暂存区。")]
        [SerializeField] private RectTransform _temporaryArea;

        // —— 运行时实例化用的 prefab ——
        [Header("Prefabs")]
        [SerializeField] private DiningTableCellView _boardCellPrefab;
        [SerializeField] private DishPieceView _dishPiecePrefab;
        [SerializeField] private WorldTargetArrow _worldTargetArrowPrefab;
        [SerializeField] private DishDropDustView _dishDropDustPrefab;

        // 餐桌优先锁定到 Battle 场景的布局区域；场景未配置时才使用 BattleForm 的 HUD BoardArea。
        private const float BoardAreaMinCellSize = 0.12f;
        private RectTransform _hudBoardArea;

        // 按餐桌尺寸自适应的单格世界尺寸与餐桌中心，BuildTable 中计算。
        private float _cellSize = MaxCellSize;
        private Vector3 _boardCenter = new Vector3(0f, 0.15f, 0f);
        private float _halfW = FallbackHalfW;
        private float _halfH = FallbackHalfH;

        private CakeLayerWorldFx _cakeLayerFx;

        // 餐桌编辑 / 只读餐桌视图的表现与交互拆到协作组件；本类只做 Food 态与世界互斥态调度（外壳）。
        private DiningTableEditController _boardEdit;

        private readonly DishSpriteProvider _spriteProvider = new DishSpriteProvider();
        private readonly List<DishPieceView> _placedPieces = new List<DishPieceView>();
        private readonly Dictionary<int, DishPieceView> _dishViewsById = new Dictionary<int, DishPieceView>();
        private readonly List<DishPieceView> _temporaryAreaPieces = new List<DishPieceView>();
        private readonly HashSet<DishPieceView> _dishValueBadgeRefreshSet = new();
        private readonly Dictionary<int, DishPieceView> _temporaryAreaViewsById = new Dictionary<int, DishPieceView>();
        private readonly Dictionary<int, float> _pendingServeMultiplierFlat = new Dictionary<int, float>();

        private enum WorldMode
        {
            Hidden,
            Food,
            TableEdit,
            TableView,
            TableCellTargeting,
        }

        private GameRun _run;
        private BattleSession _session;
        private bool _settling;
        private DishPieceView _outletDragPiece;
        private Placement? _outletHoverPlacement;
        private int _movableDishId = -1;
        private DishPieceView _movingPiece;
        private Placement _movingOriginalPlacement;
        private Placement? _movingHoverPlacement;
        private DishPieceView _temporaryAreaDragPiece;
        private Placement? _temporaryAreaHoverPlacement;
        private bool _activeItemTransitioning;
        private Vector3 _dragPointerPreviousWorld;
        private float _dragPointerPreviousTime;
        private Vector2 _dragPointerVelocity;
        private bool _dragPointerSampled;
        private WorldMode _worldMode = WorldMode.Hidden;

        private Action<string> _messageSink;
        private Action<int> _settlementScoreSink;
        private Action<string> _activeItemClicked;
        private Action<DishInstance> _dishClicked;
        private Action<DishPieceView> _dishHoverEntered;
        private Action<DishPieceView> _dishHoverExited;
        private Action<DiningTableCellView> _cellHoverEntered;
        private Action<DiningTableCellView> _cellHoverExited;
        private Action<TableFragmentHoverInfo> _tableFragmentHoverEntered;
        private Action<TableFragmentHoverInfo> _tableFragmentHoverExited;
        private Func<Vector2, bool> _preparedDishDiscardHitTest;
        private Action<bool> _preparedDishDiscardHoverChanged;
        private bool _outletHoveringDiscard;
        private bool _activeItemDishesDimmed;
        private Action _stateChanged;
        private CancellationTokenSource _presentationCts;
        private Tween _tableViewFadeTween;
        private CanvasGroup _temporaryAreaCanvasGroup;
        private Tween _temporaryAreaFadeTween;
        private readonly Dictionary<SpriteRenderer, float> _tableViewRendererBaseAlphas = new Dictionary<SpriteRenderer, float>();
        private float _tableViewTransitionAlpha = 1f;

        /// <summary>当前已加载经营挑战场景里的控制器实例（由经营挑战 UI/流程取用）。</summary>
        public static BattleWorldController Instance { get; private set; }

        public Camera WorldCamera => _camera != null ? _camera : Camera.main;

        public bool CanEnterTableView
            => _worldMode != WorldMode.TableView
                && _worldMode != WorldMode.TableCellTargeting
                && (_worldMode != WorldMode.Food
                    || (!_settling
                        && !_activeItemTransitioning
                        && _outletDragPiece == null
                        && _movingPiece == null
                        && _temporaryAreaDragPiece == null
                        && _session?.PreparedServe == null));

        public bool IsFoodInteractionBusy
            => _settling
                || _activeItemTransitioning
                || _outletDragPiece != null
                || _movingPiece != null
                || _temporaryAreaDragPiece != null;

        private void Awake()
        {
            Instance = this;

            EnsureTableEdit();
            EnsureScopeHighlights();

            // 默认非食物态：世界餐桌与其专属按钮（总览/吃/涂鸦）默认隐藏，只有 StartBattle→Initialize 才显示。
            // 场景里 BattleSceneRoot 默认 active，若不在此处收起，行动选择等非食物态一进场景就会露出这堆食物专属按钮。
            HideWorld();
        }

        /// <summary>确保餐桌编辑协作组件存在并注入共享场景引用（运行时挂到同一经营挑战场景根上）。</summary>
        private void EnsureTableEdit()
        {
            if (_boardEdit == null)
            {
                _boardEdit = GetComponent<DiningTableEditController>();
                if (_boardEdit == null)
                {
                    _boardEdit = gameObject.AddComponent<DiningTableEditController>();
                }
            }

            _boardEdit.Configure(this, _boardView, _boardCellPrefab, _piecesRoot, _camera);
        }

        /// <summary>供 <see cref="DiningTableEditController"/> 在编辑/餐桌视图结束时通知外壳复位世界互斥态。</summary>
        internal void ClearTableMode()
        {
            if (_worldMode == WorldMode.TableEdit
                || _worldMode == WorldMode.TableView
                || _worldMode == WorldMode.TableCellTargeting)
            {
                _worldMode = WorldMode.Hidden;
            }
        }

        /// <summary>是否正处于可拖拽的餐桌编辑态。</summary>
        public bool IsEditingTable => _boardEdit != null && _boardEdit.IsEditing;
        public bool IsTableEditDragging => _boardEdit != null && _boardEdit.IsDragging;
        public GpTable ActiveTable => _session?.DiningTable ?? _boardEdit?.CurrentTable;

        private void EnsureScopeHighlights()
        {
            if (_scopeHighlights == null)
            {
                _scopeHighlights = GetComponent<BattleScopeHighlightController>();
                if (_scopeHighlights == null)
                {
                    _scopeHighlights = gameObject.AddComponent<BattleScopeHighlightController>();
                }
            }
        }

        private void SetPlacedPiecesClickEnabled(bool enabled)
        {
            foreach (DishPieceView piece in _placedPieces)
            {
                piece?.SetClickEnabled(enabled);
            }
        }

        internal DishPieceView GetPieceView(int id)
        {
            return _dishViewsById.TryGetValue(id, out DishPieceView view) ? view : null;
        }

        internal Transform ActiveTargetRoot => _piecesRoot != null ? _piecesRoot : transform;

        internal Camera ActiveTargetCamera => _camera;

        internal float ActiveTargetCellSize => _cellSize;

        internal WorldTargetArrow ActiveTargetArrowPrefab => _worldTargetArrowPrefab;

        public bool TryGetExistingGridScreenRect(float paddingPixels, out Rect screenRect)
        {
            screenRect = default;
            GpTable table = _session?.DiningTable;
            if (table == null || _boardView?.Mapper == null || _camera == null || !table.TryGetExistingBounds(out _, out _, out _, out _))
            {
                return false;
            }

            bool hasPoint = false;
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;

            foreach (GridPos cell in table.ExistingCells())
            {
                if (_boardView.TryGetCellView(cell, out DiningTableCellView view) && view != null)
                {
                    EncapsulateWorldBounds(view.WorldBounds, ref minX, ref maxX, ref minY, ref maxY, ref hasPoint);
                }
            }

            if (!hasPoint)
            {
                EncapsulateExistingGridMapperBounds(table, ref minX, ref maxX, ref minY, ref maxY, ref hasPoint);
            }

            if (!hasPoint || maxX <= minX || maxY <= minY)
            {
                return false;
            }

            float padding = Mathf.Max(0f, paddingPixels);
            screenRect = Rect.MinMaxRect(minX - padding, minY - padding, maxX + padding, maxY + padding);
            return screenRect.width > 0f && screenRect.height > 0f;
        }

        internal Vector3 ScreenToWorld(Vector2 screenPoint)
        {
            Camera cam = _camera != null ? _camera : Camera.main;
            if (cam == null)
            {
                return Vector3.zero;
            }

            var p = new Vector3(screenPoint.x, screenPoint.y, -cam.transform.position.z);
            Vector3 world = cam.ScreenToWorldPoint(p);
            world.z = 0f;
            return world;
        }

        private void EncapsulateWorldBounds(
            Bounds bounds,
            ref float minX,
            ref float maxX,
            ref float minY,
            ref float maxY,
            ref bool hasPoint)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            EncapsulateWorldPoint(new Vector3(min.x, min.y, bounds.center.z), ref minX, ref maxX, ref minY, ref maxY, ref hasPoint);
            EncapsulateWorldPoint(new Vector3(min.x, max.y, bounds.center.z), ref minX, ref maxX, ref minY, ref maxY, ref hasPoint);
            EncapsulateWorldPoint(new Vector3(max.x, min.y, bounds.center.z), ref minX, ref maxX, ref minY, ref maxY, ref hasPoint);
            EncapsulateWorldPoint(new Vector3(max.x, max.y, bounds.center.z), ref minX, ref maxX, ref minY, ref maxY, ref hasPoint);
        }

        private void EncapsulateExistingGridMapperBounds(
            GpTable table,
            ref float minX,
            ref float maxX,
            ref float minY,
            ref float maxY,
            ref bool hasPoint)
        {
            if (table == null || _boardView?.Mapper == null || !table.TryGetExistingBounds(out int minCellX, out int minCellY, out int maxCellX, out int maxCellY))
            {
                return;
            }

            DiningTableCoordinateMapper mapper = _boardView.Mapper;
            float halfCell = mapper.CellSize * 0.5f;
            Vector3 topLeft = mapper.CellCenter(new GridPos(minCellX, minCellY)) + new Vector3(-halfCell, halfCell, 0f);
            Vector3 topRight = mapper.CellCenter(new GridPos(maxCellX, minCellY)) + new Vector3(halfCell, halfCell, 0f);
            Vector3 bottomLeft = mapper.CellCenter(new GridPos(minCellX, maxCellY)) + new Vector3(-halfCell, -halfCell, 0f);
            Vector3 bottomRight = mapper.CellCenter(new GridPos(maxCellX, maxCellY)) + new Vector3(halfCell, -halfCell, 0f);

            EncapsulateWorldPoint(topLeft, ref minX, ref maxX, ref minY, ref maxY, ref hasPoint);
            EncapsulateWorldPoint(topRight, ref minX, ref maxX, ref minY, ref maxY, ref hasPoint);
            EncapsulateWorldPoint(bottomLeft, ref minX, ref maxX, ref minY, ref maxY, ref hasPoint);
            EncapsulateWorldPoint(bottomRight, ref minX, ref maxX, ref minY, ref maxY, ref hasPoint);
        }

        private void EncapsulateWorldPoint(
            Vector3 world,
            ref float minX,
            ref float maxX,
            ref float minY,
            ref float maxY,
            ref bool hasPoint)
        {
            Vector3 screen = _camera.WorldToScreenPoint(world);
            minX = Mathf.Min(minX, screen.x);
            maxX = Mathf.Max(maxX, screen.x);
            minY = Mathf.Min(minY, screen.y);
            maxY = Mathf.Max(maxY, screen.y);
            hasPoint = true;
        }

        internal bool TryPointerCellTarget(out ActiveTarget target)
        {
            target = default;
            GpTable table = ActiveCellTargetTable();
            if (table == null || _boardView?.Mapper == null || _camera == null || WorldInput.PointerOverUi)
            {
                return false;
            }

            Vector3 mouseWorld = WorldInput.MouseWorld(_camera);
            GridPos cell = _boardView.Mapper.NearestCell(mouseWorld);
            if (!table.Exists(cell) || !PointerInsideCell(cell, mouseWorld))
            {
                return false;
            }

            target = new ActiveTarget(string.Empty, cell.X, cell.Y, cfg.ItemTargetKind.DiningTableCell);
            return true;
        }

        internal bool TryPointerDishTarget(out ActiveTarget target)
        {
            target = default;
            if (_session?.DiningTable == null || _boardView?.Mapper == null || _camera == null || WorldInput.PointerOverUi)
            {
                return false;
            }

            GridPos cell = _boardView.Mapper.NearestCell(WorldInput.MouseWorld(_camera));
            if (!_session.DiningTable.InBounds(cell))
            {
                return false;
            }

            DishInstance dish = _session.DiningTable.DishAt(cell);
            if (dish == null)
            {
                return false;
            }

            GridPos origin = dish.Placement.Origin;
            target = new ActiveTarget(dish.Id.ToString(), origin.X, origin.Y, cfg.ItemTargetKind.DiningTableDish);
            return true;
        }

        internal void BeginActiveItemWorldTargeting(bool dimPlacedDishes)
        {
            SetPlacedPiecesClickEnabled(false);
            _activeItemDishesDimmed = dimPlacedDishes;
            RefreshActiveItemPlacedDishDimming();
        }

        internal void EndActiveItemWorldTargeting()
        {
            ClearActiveItemTargetHighlights();
            _activeItemDishesDimmed = false;
            RefreshActiveItemPlacedDishDimming();
            SetPlacedPiecesClickEnabled(true);
        }

        internal void SetActiveItemTargetHighlights(
            cfg.ItemTargetKind kind,
            IReadOnlyList<ActiveTarget> candidates,
            IReadOnlyList<ActiveTarget> selected,
            ActiveTarget? hovered)
        {
            if (kind == cfg.ItemTargetKind.DiningTableCell)
            {
                RefreshActiveItemPlacedDishDimming();
                _boardView?.ClearTargetHighlights();
                if (candidates != null)
                {
                    foreach (ActiveTarget candidate in candidates)
                    {
                        var cell = new GridPos(candidate.X, candidate.Y);
                        _boardView?.SetTargetHighlight(
                            cell,
                            ContainsTarget(selected, candidate),
                            TargetEquals(hovered, candidate));
                    }
                }

                return;
            }

            RefreshActiveItemPlacedDishDimming();
            if (kind == cfg.ItemTargetKind.DiningTableDish)
            {
                foreach (DishPieceView piece in _placedPieces)
                {
                    if (piece?.Instance == null)
                    {
                        continue;
                    }

                    GridPos origin = piece.Instance.Placement.Origin;
                    var candidate = new ActiveTarget(piece.Instance.Id.ToString(), origin.X, origin.Y, cfg.ItemTargetKind.DiningTableDish);
                    bool active = ContainsTarget(candidates, candidate)
                        && (ContainsTarget(selected, candidate) || TargetEquals(hovered, candidate));
                    piece.SetPlacementGlow(active, true);
                }
            }
        }

        private void RefreshActiveItemPlacedDishDimming()
        {
            foreach (DishPieceView piece in _placedPieces)
            {
                piece?.SetActiveItemTargetDimmed(_activeItemDishesDimmed);
            }
        }

        private GpTable ActiveCellTargetTable()
        {
            return _session?.DiningTable ?? _boardEdit?.CurrentTable;
        }

        private bool PointerInsideCell(GridPos cell, Vector3 mouseWorld)
        {
            if (_boardView == null || !_boardView.TryGetCellView(cell, out DiningTableCellView view) || view == null)
            {
                return true;
            }

            Bounds bounds = view.WorldBounds;
            return mouseWorld.x >= bounds.min.x
                && mouseWorld.x <= bounds.max.x
                && mouseWorld.y >= bounds.min.y
                && mouseWorld.y <= bounds.max.y;
        }

        internal void ClearActiveItemTargetHighlights()
        {
            RefreshActiveItemPlacedDishDimming();
            _boardView?.ClearTargetHighlights();
            foreach (DishPieceView piece in _placedPieces)
            {
                piece?.SetPlacementGlow(false, false);
            }
        }

        private static bool ContainsTarget(IReadOnlyList<ActiveTarget> targets, ActiveTarget candidate)
        {
            if (targets == null)
            {
                return false;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                if (TargetEquals(targets[i], candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TargetEquals(ActiveTarget? a, ActiveTarget b)
        {
            return a.HasValue && TargetEquals(a.Value, b);
        }

        private static bool TargetEquals(ActiveTarget a, ActiveTarget b)
        {
            return a.TargetKind == b.TargetKind && a.Id == b.Id && a.X == b.X && a.Y == b.Y;
        }

        /// <summary>
        /// 进入餐桌编辑页：外壳先收起 Food 态表现并切到编辑互斥态，再把编辑页构建交给协作组件。
        /// </summary>
        public void BeginTableEdit(
            GameRun run,
            IReadOnlyList<string> candidateIds,
            Action<bool> onDone)
        {
            BeginTableFragmentChoice(new TableFragmentChoiceRequest(
                run,
                candidateIds,
                onDone,
                null,
                run?.PendingFragmentPackRotations));
        }

        public void BeginTableFragmentChoice(TableFragmentChoiceRequest request)
        {
            GameRun run = request?.Run;
            if (run == null)
            {
                request?.Completed?.Invoke(false);
                return;
            }

            EnsureTableEdit();
            EndTableView();

            gameObject.SetActive(true);
            CancelPresentationTasks();
            _worldMode = WorldMode.TableEdit;
            _settling = false;
            CancelServeInteractions();
            _session = null;
            SetFoodWorldElementsVisible(false);
            SetPendingRewardPresentationVisible(false);
            ClearPlacedPieces();

            _boardEdit.BeginTableFragmentChoice(request);
            _boardEdit.SetCandidateHoverCallbacks(_tableFragmentHoverEntered, _tableFragmentHoverExited);
            _boardView?.SetCellHoverCallbacks(OnCellHoverEntered, OnCellHoverExited);
        }

        /// <summary>进入只读餐桌视图：外壳收起 Food 态并切到餐桌视图互斥态，交由协作组件复用餐桌布局渲染。</summary>
        public void BeginTableView(GameRun run, GpTable tableOverride = null)
        {
            if (run == null || !CanEnterTableView)
            {
                return;
            }

            EnsureTableEdit();
            ResetTableViewFade();
            if (_boardEdit.IsEditing)
            {
                _boardEdit.EndTableEdit();
            }

            _run = run;
            _session = null;
            gameObject.SetActive(true);
            CancelPresentationTasks();
            _worldMode = WorldMode.TableView;
            _settling = false;
            CancelServeInteractions();
            SetFoodWorldElementsVisible(false);
            HideWorldPanels();
            SetPendingRewardPresentationVisible(false);
            ClearPlacedPieces();

            _boardEdit.BeginTableView(run, tableOverride);
            _boardView?.SetCellHoverCallbacks(OnCellHoverEntered, OnCellHoverExited);
        }

        /// <summary>进入消耗品餐桌选格态：布局同只读餐桌视图，但外层会用世界箭头接管点击确认/取消。</summary>
        public void BeginTableCellTargeting(GameRun run, GpTable tableOverride = null)
        {
            if (run == null)
            {
                return;
            }

            EnsureTableEdit();
            ResetTableViewFade();
            if (_boardEdit.IsEditing)
            {
                _boardEdit.EndTableEdit();
            }

            _run = run;
            _session = null;
            gameObject.SetActive(true);
            CancelPresentationTasks();
            _worldMode = WorldMode.TableCellTargeting;
            _settling = false;
            CancelServeInteractions();
            SetFoodWorldElementsVisible(false);
            HideWorldPanels();
            SetPendingRewardPresentationVisible(false);
            ClearPlacedPieces();

            _boardEdit.BeginCellTargeting(run, tableOverride);
            _boardView?.SetCellHoverCallbacks(OnCellHoverEntered, OnCellHoverExited);
        }

        public void EndTableView()
        {
            ResetTableViewFade();
            if (_worldMode == WorldMode.TableView || _worldMode == WorldMode.TableCellTargeting)
            {
                _worldMode = WorldMode.Hidden;
            }

            _boardEdit?.EndTableView();
        }

        public void FadeTableViewIn(float duration, Action onComplete = null)
        {
            CancelTableViewFade();
            _tableViewRendererBaseAlphas.Clear();
            CaptureTableViewRenderers();
            ApplyTableViewAlpha(0f);
            FadeTableViewTo(1f, duration, onComplete, clearOnComplete: true);
        }

        public void FadeTableViewOut(float duration, Action onComplete = null)
        {
            CancelTableViewFade();
            _tableViewRendererBaseAlphas.Clear();
            CaptureTableViewRenderers();
            FadeTableViewTo(0f, duration, onComplete, clearOnComplete: false);
        }

        public bool PlayActiveItemCellMaterialApplied(GridPos pos, string materialId, Action onComplete)
        {
            if (_worldMode == WorldMode.TableView || _worldMode == WorldMode.TableCellTargeting)
            {
                return _boardEdit != null && _boardEdit.ApplyCellMaterialVisual(pos, materialId, onComplete);
            }

            GpTable table = ActiveCellTargetTable();
            if (table == null || !table.Exists(pos))
            {
                return false;
            }

            if (_boardView != null && _boardView.TryGetCellView(pos, out DiningTableCellView cell) && cell != null)
            {
                cell.PlayMaterialTransform(() => _boardView?.Sync(), onComplete);
                return true;
            }

            _boardView?.Sync();
            onComplete?.Invoke();
            return true;
        }

        public bool PlayActiveItemDishFlavorApplied(ActiveTarget target, Action onComplete)
        {
            if (target.TargetKind != cfg.ItemTargetKind.DiningTableDish
                || !int.TryParse(target.Id, out int dishId)
                || !_dishViewsById.TryGetValue(dishId, out DishPieceView piece)
                || piece == null)
            {
                return false;
            }

            DishInstance temporaryDish = _session?.FindTemporaryAreaDishById(dishId);
            if (temporaryDish != null)
            {
                _activeItemTransitioning = true;
                piece.SetClickEnabled(false);
                piece.SetMoveCallbacks(null, null, null);
                RefreshTemporaryAreaVisibility(animated: true);
                int index = TemporaryAreaDishIndex(dishId);
                TemporaryAreaStackSlot[] targetSlots = CalculateTemporaryAreaSlots();
                LayoutTemporaryAreaPieces(animated: true, slotsOverride: targetSlots);
                TemporaryAreaStackSlot targetSlot = index >= 0 && index < targetSlots.Length
                    ? targetSlots[index]
                    : new TemporaryAreaStackSlot(
                        _temporaryArea != null
                            ? (Vector2)_temporaryArea.position
                            : (Vector2)transform.position,
                        1f);

                piece.PlayActiveItemNumbTransform(temporaryDish.Placement, () =>
                {
                    if (piece == null)
                    {
                        CompleteTemporaryAreaArrival(null, temporaryDish, index, onComplete);
                        return;
                    }

                    piece.SetFlying(true);
                    piece.SetSortingOrderOffset(index * TemporaryAreaSortingStride);
                    Vector3 slotCenter = new Vector3(
                        targetSlot.Center.x,
                        targetSlot.Center.y,
                        _temporaryArea != null ? _temporaryArea.position.z : transform.position.z);
                    Vector3 targetPosition = TemporaryAreaPieceRootPosition(
                        piece,
                        slotCenter,
                        targetSlot.Scale);
                    DOTween.Sequence()
                        .SetLink(piece.gameObject)
                        .Append(piece.transform.DOMove(targetPosition, TemporaryAreaFlyDuration).SetEase(Ease.InOutCubic))
                        .Join(piece.transform.DOScale(Vector3.one * targetSlot.Scale, TemporaryAreaFlyDuration).SetEase(Ease.InOutCubic))
                        .OnComplete(() =>
                        {
                            CompleteTemporaryAreaArrival(piece, temporaryDish, index, onComplete);
                        });
                });
                return true;
            }

            piece.PlayActiveItemFlavorTransform(onVisualSwitch: null, onComplete);
            return true;
        }

        private void CompleteTemporaryAreaArrival(
            DishPieceView piece,
            DishInstance dish,
            int index,
            Action onComplete)
        {
            _activeItemTransitioning = false;
            if (piece == null || dish == null)
            {
                RebuildPlacedPieces();
                RefreshAll();
            }
            else
            {
                _placedPieces.Remove(piece);
                _dishViewsById.Remove(dish.Id);
                piece.gameObject.name = $"TemporaryAreaDish_{dish.Id}_{dish.Def.Id}";
                piece.SetGhost(false);
                piece.SetPlacementGlow(false, false);
                piece.SetHoverCallbacks(OnDishHoverEntered, OnDishHoverExited);
                piece.SetMoveCallbacks(
                    BeginTemporaryAreaDishDrag,
                    UpdateTemporaryAreaDishDrag,
                    EndTemporaryAreaDishDrag);
                piece.SetPointerHitFilter(IsTemporaryAreaPointerHitAccepted);
                piece.SetFlying(true);
                piece.SetSortingOrderOffset(index * TemporaryAreaSortingStride);
                piece.SetClickEnabled(true);

                int insertIndex = Mathf.Clamp(index, 0, _temporaryAreaPieces.Count);
                _temporaryAreaPieces.Insert(insertIndex, piece);
                _temporaryAreaViewsById[dish.Id] = piece;
                _boardView?.Sync();
            }

            _stateChanged?.Invoke();
            onComplete?.Invoke();
        }

        public void SkipTableEditPack()
        {
            if (_worldMode != WorldMode.TableEdit || _boardEdit == null || !_boardEdit.IsEditing)
            {
                return;
            }

            _boardEdit.SkipTableEditPack();
        }

        public void ConfirmTableEditPlacement()
        {
            if (_worldMode != WorldMode.TableEdit || _boardEdit == null || !_boardEdit.IsEditing)
            {
                return;
            }

            _boardEdit.ConfirmTableEditPlacement();
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                _session.ServeMultiplierFlatApplied -= OnServeMultiplierFlatApplied;
            }

            if (Instance == this)
            {
                Instance = null;
            }

            CancelTemporaryAreaFade();
            CancelPresentationTasks();
        }

        public void Initialize(
            GameRun run,
            BattleSession session,
            Action<string> messageSink,
            Action<int> settlementScoreSink,
            Action stateChanged,
            Action<string> activeItemClicked,
            Action<DishInstance> dishClicked,
            bool resetDoodle = true)
        {
            if (_session != null)
            {
                _session.ServeMultiplierFlatApplied -= OnServeMultiplierFlatApplied;
            }

            CancelServeInteractions();
            _run = run;
            _session = session;
            if (_session != null)
            {
                _session.ServeMultiplierFlatApplied += OnServeMultiplierFlatApplied;
            }

            _pendingServeMultiplierFlat.Clear();
            _messageSink = messageSink;
            _settlementScoreSink = settlementScoreSink;
            _stateChanged = stateChanged;
            _activeItemClicked = activeItemClicked;
            _dishClicked = dishClicked;
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            gameObject.SetActive(true);
            CancelPresentationTasks();
            ClearPendingRewardPresentation();
            _worldMode = WorldMode.Food;
            _settling = false;
            _activeItemTransitioning = false;
            ComputeViewport();
            BuildTable(session.DiningTable);
            EnsureSequencer();
            // 装饰品和消耗品（被动/主动）与右下角食谱仍在屏幕空间 HUD；
            // 出菜口是 World Space Canvas，和餐桌、食物、上菜/结算演出、涂鸦一起由经营挑战世界承载。
            HideWorldPanels();
            SetFoodWorldElementsVisible(true);
            RebuildPlacedPieces();
            if (resetDoodle)
            {
                ResetDoodle();
            }
            else
            {
                _doodle?.SetVisible(true);
            }
            RefreshAll();
            EnsureCakeLayerFx();
            // 初始/继承层数只更新 HUD，不生成世界蛋糕；世界表现只响应本局实际加层事件。
            _cakeLayerFx?.Clear();
        }

        public void SetDishHoverCallbacks(Action<DishPieceView> entered, Action<DishPieceView> exited)
        {
            _dishHoverEntered = entered;
            _dishHoverExited = exited;
            foreach (DishPieceView piece in _placedPieces)
            {
                if (piece != null)
                {
                    piece.SetHoverCallbacks(OnDishHoverEntered, OnDishHoverExited);
                }
            }

            foreach (DishPieceView piece in _temporaryAreaPieces)
            {
                if (piece != null)
                {
                    piece.SetHoverCallbacks(OnDishHoverEntered, OnDishHoverExited);
                }
            }
        }

        public void SetCellHoverCallbacks(Action<DiningTableCellView> entered, Action<DiningTableCellView> exited)
        {
            _cellHoverEntered = entered;
            _cellHoverExited = exited;
            _boardView?.SetCellHoverCallbacks(OnCellHoverEntered, OnCellHoverExited);
        }

        public void SetTableFragmentHoverCallbacks(
            Action<TableFragmentHoverInfo> entered,
            Action<TableFragmentHoverInfo> exited)
        {
            _tableFragmentHoverEntered = entered;
            _tableFragmentHoverExited = exited;
            EnsureTableEdit();
            _boardEdit.SetCandidateHoverCallbacks(entered, exited);
        }

        public void SetPreparedDishDiscardTarget(
            Func<Vector2, bool> hitTest,
            Action<bool> hoverChanged)
        {
            if (_outletHoveringDiscard)
            {
                _preparedDishDiscardHoverChanged?.Invoke(false);
                _outletHoveringDiscard = false;
            }

            _preparedDishDiscardHitTest = hitTest;
            _preparedDishDiscardHoverChanged = hoverChanged;
        }

        public void HideWorld()
        {
            SuspendWorld();
            ClearPendingRewardPresentation();
        }

        /// <summary>页面临时离开 Battle 世界；保留待领奖蛋糕与食物分数标签，供返回 Food 时恢复。</summary>
        public void SuspendWorld()
        {
            CancelPresentationTasks();
            ResetTableViewFade();
            if (_boardEdit != null && _boardEdit.IsEditing)
            {
                _boardEdit.EndTableEdit();
            }

            EndTableView();
            _settling = false;
            _activeItemTransitioning = false;
            CancelServeInteractions();
            SetFoodWorldElementsVisible(false);
            _worldMode = WorldMode.Hidden;
            gameObject.SetActive(false);
        }

        public List<CakeLayerVisualState> CapturePendingRewardCakeVisuals()
        {
            EnsureCakeLayerFx();
            return _cakeLayerFx != null
                ? _cakeLayerFx.CaptureState()
                : new List<CakeLayerVisualState>();
        }

        /// <summary>世界重建后，无动画恢复待领奖的蛋糕与食物贡献标签。</summary>
        public void RestorePendingRewardPresentation(
            IReadOnlyList<CakeLayerVisualState> cakes,
            IReadOnlyList<DishScore> dishScores)
        {
            EnsureCakeLayerFx();
            _cakeLayerFx?.RestoreState(cakes);
            EnsureSequencer();
            if (_sequencer != null && _boardView?.Mapper != null)
            {
                _sequencer.RestoreDishValueBadges(
                    dishScores,
                    _dishViewsById);
            }
        }

        public void SetPendingRewardPresentationVisible(bool visible)
        {
            _cakeLayerFx?.SetVisible(visible);
        }

        /// <summary>新经营挑战、战败、继续行动或退出玩法时最终销毁待领奖表现。</summary>
        public void ClearPendingRewardPresentation()
        {
            _cakeLayerFx?.Clear();
            _sequencer?.ClearRetainedDishValueBadges();
            ClearDishValueBadgeOverrides();
        }

        private void ClearDishValueBadgeOverrides()
        {
            for (int i = 0; i < _placedPieces.Count; i++)
            {
                _placedPieces[i]?.ClearDishValueBadgeOverride();
            }

            for (int i = 0; i < _temporaryAreaPieces.Count; i++)
            {
                _temporaryAreaPieces[i]?.ClearDishValueBadgeOverride();
            }
        }

        /// <summary>经营挑战结束后清理本场运行时餐桌表现，避免已摆食物残留到后续非经营挑战状态。</summary>
        public void ClearBattleTable()
        {
            CancelPresentationTasks();
            _settling = false;
            CancelServeInteractions();
            _session = null;
            ClearPlacedPieces();
            SetTemporaryAreaVisible(false, animated: false);
            _doodle?.Clear();
            ClearPendingRewardPresentation();
        }

        private void FadeTableViewTo(float targetAlpha, float duration, Action onComplete, bool clearOnComplete)
        {
            targetAlpha = Mathf.Clamp01(targetAlpha);
            if (_tableViewRendererBaseAlphas.Count == 0 || duration <= 0f)
            {
                ApplyTableViewAlpha(targetAlpha);
                if (clearOnComplete)
                {
                    _tableViewRendererBaseAlphas.Clear();
                }

                onComplete?.Invoke();
                return;
            }

            _tableViewFadeTween = DOVirtual.Float(
                    _tableViewTransitionAlpha,
                    targetAlpha,
                    duration,
                    ApplyTableViewAlpha)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    _tableViewFadeTween = null;
                    ApplyTableViewAlpha(targetAlpha);
                    if (clearOnComplete)
                    {
                        _tableViewRendererBaseAlphas.Clear();
                    }

                    onComplete?.Invoke();
                });
        }

        private void CaptureTableViewRenderers()
        {
            if (_boardView == null)
            {
                return;
            }

            float divisor = _tableViewTransitionAlpha > 0.001f ? _tableViewTransitionAlpha : 1f;
            SpriteRenderer[] renderers = _boardView.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (SpriteRenderer renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                _tableViewRendererBaseAlphas[renderer] = Mathf.Clamp01(renderer.color.a / divisor);
            }
        }

        private void ApplyTableViewAlpha(float alpha)
        {
            _tableViewTransitionAlpha = Mathf.Clamp01(alpha);
            foreach (KeyValuePair<SpriteRenderer, float> kv in _tableViewRendererBaseAlphas)
            {
                SpriteRenderer renderer = kv.Key;
                if (renderer == null)
                {
                    continue;
                }

                Color color = renderer.color;
                color.a = kv.Value * _tableViewTransitionAlpha;
                renderer.color = color;
            }
        }

        private void ResetTableViewFade()
        {
            CancelTableViewFade();
            ApplyTableViewAlpha(1f);
            _tableViewRendererBaseAlphas.Clear();
            _tableViewTransitionAlpha = 1f;
        }

        private void CancelTableViewFade()
        {
            if (_tableViewFadeTween == null)
            {
                return;
            }

            _tableViewFadeTween.Kill();
            _tableViewFadeTween = null;
        }

        private void SetFoodWorldElementsVisible(bool visible)
        {
            if (visible)
            {
                RefreshTemporaryAreaVisibility(animated: false);
            }
            else
            {
                SetTemporaryAreaVisible(false, animated: false);
            }

            if (!visible)
            {
                _doodle?.SetVisible(false);
            }
        }

        private void RefreshTemporaryAreaVisibility(bool animated)
        {
            bool shouldShow = _worldMode == WorldMode.Food
                && _session != null
                && _session.TemporaryAreaDishes.Count > 0;
            SetTemporaryAreaVisible(shouldShow, animated);
        }

        private void SetTemporaryAreaVisible(bool visible, bool animated)
        {
            if (_temporaryArea == null)
            {
                return;
            }

            EnsureTemporaryAreaCanvasGroup();
            CancelTemporaryAreaFade();
            if (_temporaryAreaCanvasGroup == null)
            {
                _temporaryArea.gameObject.SetActive(visible);
                return;
            }

            _temporaryAreaCanvasGroup.interactable = visible;
            _temporaryAreaCanvasGroup.blocksRaycasts = visible;
            if (!animated)
            {
                _temporaryAreaCanvasGroup.alpha = visible ? 1f : 0f;
                _temporaryArea.gameObject.SetActive(visible);
                return;
            }

            if (visible)
            {
                if (!_temporaryArea.gameObject.activeSelf)
                {
                    _temporaryAreaCanvasGroup.alpha = 0f;
                    _temporaryArea.gameObject.SetActive(true);
                }

                if (_temporaryAreaCanvasGroup.alpha >= 0.999f)
                {
                    return;
                }

                _temporaryAreaFadeTween = DOTween
                    .To(
                        () => _temporaryAreaCanvasGroup.alpha,
                        alpha => _temporaryAreaCanvasGroup.alpha = alpha,
                        1f,
                        TemporaryAreaFadeDuration)
                    .SetEase(Ease.OutQuad)
                    .SetLink(_temporaryArea.gameObject)
                    .OnComplete(() => _temporaryAreaFadeTween = null);
                return;
            }

            if (!_temporaryArea.gameObject.activeSelf)
            {
                _temporaryAreaCanvasGroup.alpha = 0f;
                return;
            }

            _temporaryAreaFadeTween = DOTween
                .To(
                    () => _temporaryAreaCanvasGroup.alpha,
                    alpha => _temporaryAreaCanvasGroup.alpha = alpha,
                    0f,
                    TemporaryAreaFadeDuration)
                .SetEase(Ease.InQuad)
                .SetLink(_temporaryArea.gameObject)
                .OnComplete(() =>
                {
                    _temporaryAreaFadeTween = null;
                    if (_temporaryArea != null)
                    {
                        _temporaryArea.gameObject.SetActive(false);
                    }
                });
        }

        private void EnsureTemporaryAreaCanvasGroup()
        {
            if (_temporaryAreaCanvasGroup == null && _temporaryArea != null)
            {
                _temporaryAreaCanvasGroup = _temporaryArea.GetComponent<CanvasGroup>();
            }
        }

        private void CancelTemporaryAreaFade()
        {
            if (_temporaryAreaFadeTween == null)
            {
                return;
            }

            _temporaryAreaFadeTween.Kill();
            _temporaryAreaFadeTween = null;
        }

        private CancellationToken GetPresentationToken()
        {
            if (_presentationCts == null || _presentationCts.IsCancellationRequested)
            {
                _presentationCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            }

            return _presentationCts.Token;
        }

        private void CancelPresentationTasks()
        {
            _scopeHighlights?.ClearAll();
            if (_presentationCts == null)
            {
                return;
            }

            _presentationCts.Cancel();
            _presentationCts.Dispose();
            _presentationCts = null;
        }

        public void RefreshAll()
        {
            if (_session == null)
            {
                return;
            }

            if (_session.PreparedServe != null)
            {
                LockMovableDish();
            }

            _boardView.Sync();
            LayoutTemporaryAreaPieces();
            RefreshDishValueBadges();
        }

        private void RefreshDishValueBadges()
        {
            _dishValueBadgeRefreshSet.Clear();
            RefreshDishValueBadges(_placedPieces, _dishValueBadgeRefreshSet);
            RefreshDishValueBadges(_temporaryAreaPieces, _dishValueBadgeRefreshSet);
            RefreshDishValueBadge(_outletDragPiece, _dishValueBadgeRefreshSet);
            RefreshDishValueBadge(_movingPiece, _dishValueBadgeRefreshSet);
            RefreshDishValueBadge(_temporaryAreaDragPiece, _dishValueBadgeRefreshSet);
        }

        private static void RefreshDishValueBadges(
            IReadOnlyList<DishPieceView> pieces,
            ISet<DishPieceView> refreshed)
        {
            if (pieces == null)
            {
                return;
            }

            for (int i = 0; i < pieces.Count; i++)
            {
                RefreshDishValueBadge(pieces[i], refreshed);
            }
        }

        private static void RefreshDishValueBadge(
            DishPieceView piece,
            ISet<DishPieceView> refreshed)
        {
            if (piece != null && refreshed.Add(piece))
            {
                piece.RefreshDishValueBadge();
            }
        }

        public void PlayCakeLayerChange(int before, int after)
        {
            EnsureCakeLayerFx();
            _cakeLayerFx?.PlayChange(before, after);
        }

        private void EnsureCakeLayerFx()
        {
            if (_cakeLayerFx == null)
            {
                _cakeLayerFx = GetComponent<CakeLayerWorldFx>();
                if (_cakeLayerFx == null)
                {
                    Debug.LogError($"{nameof(BattleWorldController)} scene is missing {nameof(CakeLayerWorldFx)}.", this);
                    return;
                }
            }

            _cakeLayerFx.Configure(this, _camera);
        }

        /// <summary>隐藏已迁到 HUD 的旧世界空间面板：被动/消耗品槽与旧食谱书。</summary>
        private void HideWorldPanels()
        {
            if (_passiveItemsRoot != null)
            {
                _passiveItemsRoot.gameObject.SetActive(false);
            }

            if (_activeItemsRoot != null)
            {
                _activeItemsRoot.gameObject.SetActive(false);
            }
        }

        public void SyncTableFromSession()
        {
            if (_session == null)
            {
                return;
            }

            RebuildPlacedPieces();
            if (_activeItemDishesDimmed)
            {
                SetPlacedPiecesClickEnabled(false);
            }

            RefreshAll();
        }

        public bool TryPrepareServeDish(int slotIndex)
        {
            if (_session == null
                || _session.IsSettled
                || _settling
                || _activeItemTransitioning
                || _outletDragPiece != null
                || _movingPiece != null
                || _temporaryAreaDragPiece != null)
            {
                return false;
            }

            ServePrepareResult result = _session.PrepareServeFromBell(slotIndex);
            if (!result.Success)
            {
                SetMessage(PrepareServeMessage(result.Outcome, slotIndex));
                RefreshAll();
                _stateChanged?.Invoke();
                return false;
            }

            LockMovableDish();
            SetMessage($"已出菜：{result.PreparedDish.Definition.Name}，拖到餐桌上摆放。");
            RefreshAll();
            _stateChanged?.Invoke();
            return true;
        }

        public void BeginServingOutletDrag(Vector2 screenPoint)
        {
            if (_session?.PreparedServe == null
                || _settling
                || _activeItemTransitioning
                || _outletDragPiece != null
                || _movingPiece != null
                || _temporaryAreaDragPiece != null)
            {
                return;
            }

            ClearDishScopeHighlights();
            PreparedServeDish prepared = _session.PreparedServe;
            DishPieceView piece = InstantiateLoosePiece(prepared.Dish, "ServingOutletDragPreview");
            if (piece == null)
            {
                return;
            }

            _outletDragPiece = piece;
            _outletHoverPlacement = null;
            piece.SetClickEnabled(false);
            piece.SetDragPresentation(true);
            BeginDragPointerTracking(ScreenToWorld(screenPoint));
            UpdateServingOutletDrag(screenPoint);
        }

        public void UpdateServingOutletDrag(Vector2 screenPoint)
        {
            if (_outletDragPiece == null || _session?.PreparedServe == null)
            {
                return;
            }

            Vector3 world = ScreenToWorld(screenPoint);
            SampleDragPointer(world);
            _outletDragPiece.MoveVisualCenterToWorld(world);
            bool hoveringDiscard = _session.FoodDiscardsRemaining > 0
                && _preparedDishDiscardHitTest?.Invoke(screenPoint) == true;
            SetOutletDiscardHover(hoveringDiscard);
            if (hoveringDiscard)
            {
                _outletHoverPlacement = null;
                _boardView.ClearDragPlacementFeedback();
                ClearDishScopeHighlights();
                return;
            }

            DishDragPlacementResult result = EvaluateDragPlacement(_outletDragPiece, world);
            if (result != null
                && result.CanCommit
                && !_session.PreparedServe.Contains(result.Placement))
            {
                result = WithOverallState(result, DishDragCellState.Blocked);
            }

            _outletHoverPlacement = result != null && result.CanCommit
                ? result.Placement
                : null;
            _boardView.ShowDragPlacementFeedback(result);
            ShowDishScopeHighlightsAtPlacement(
                _outletDragPiece.Instance,
                result != null ? result.Placement : (Placement?)null);
        }

        public bool EndServingOutletDrag(Vector2 screenPoint)
        {
            if (_outletDragPiece == null || _session?.PreparedServe == null)
            {
                ClearOutletDragPreview();
                return false;
            }

            UpdateServingOutletDrag(screenPoint);
            if (_outletHoveringDiscard)
            {
                string dishName = _session.PreparedServe.Dish.Def.Name;
                ClearOutletDragPreview();
                if (_session.TryDiscardPreparedServe())
                {
                    SetMessage($"已丢弃：{dishName}");
                    RefreshAll();
                    _stateChanged?.Invoke();
                    return true;
                }

                SetMessage("本局已经不能再丢弃食物。");
                RefreshAll();
                _stateChanged?.Invoke();
                return false;
            }

            if (!_outletHoverPlacement.HasValue)
            {
                ClearOutletDragPreview();
                SetMessage("请拖到餐桌空位，或拖进可用的垃圾桶。");
                return false;
            }

            Placement placement = _outletHoverPlacement.Value;
            IReadOnlyList<int> affectedDishIds = BuildScopeAffectedDishIdsAtPlacement(
                _outletDragPiece.Instance,
                placement);
            Vector2 footprintSize = _outletDragPiece.FootprintWorldSize;
            Vector2 releaseVelocity = _dragPointerVelocity;
            ClearOutletDragPreview();
            ServeResult result = _session.CommitPreparedServe(placement);
            if (!result.Success)
            {
                SetMessage("摆放位置已失效，请重新拖动。");
                RefreshAll();
                _stateChanged?.Invoke();
                return false;
            }

            _movableDishId = !result.RemovedAfterServe && _session.PreparedServe == null
                ? result.Dish.Id
                : -1;
            RebuildPlacedPieces();
            _boardView.Sync();
            PlayDropDust(placement, footprintSize, releaseVelocity);
            PlayScopeAffectedDishFeedback(affectedDishIds);
            SetMessage(result.RemovedAfterServe
                ? $"开胃菜消化了：{result.Dish.Def.Name}"
                : $"上菜：{result.Dish.Def.Name}");
            PlayPendingServeMultiplierTexts();
            FlashServeScopeHighlights(result.Dish, GetPresentationToken());
            _stateChanged?.Invoke();
            return true;
        }

        private DishPieceView InstantiateLoosePiece(DishInstance dish, string objectName)
        {
            if (_dishPiecePrefab == null || dish == null)
            {
                Debug.LogError($"{nameof(BattleWorldController)} 缺少 DishPiece prefab。", this);
                return null;
            }

            DishPieceView piece = Instantiate(_dishPiecePrefab, _piecesRoot);
            piece.gameObject.name = objectName;
            piece.BuildPlaced(dish, _spriteProvider.Get(dish.Def), _cellSize, _cellSize + Gap, null);
            piece.SetHoverCallbacks(null, null);
            return piece;
        }

        private void ClearOutletDragPreview()
        {
            ClearDishScopeHighlights();
            _boardView?.ClearDragPlacementFeedback();
            SetOutletDiscardHover(false);
            if (_outletDragPiece != null)
            {
                _outletDragPiece.gameObject.SetActive(false);
                Destroy(_outletDragPiece.gameObject);
            }

            _outletDragPiece = null;
            _outletHoverPlacement = null;
            ResetDragPointerTracking();
        }

        private void SetOutletDiscardHover(bool hovered)
        {
            if (_outletHoveringDiscard == hovered)
            {
                return;
            }

            _outletHoveringDiscard = hovered;
            _preparedDishDiscardHoverChanged?.Invoke(hovered);
        }

        private void OnServeMultiplierFlatApplied(DishInstance dish, float value)
        {
            if (dish == null || Math.Abs(value) < 0.0001f)
            {
                return;
            }

            _pendingServeMultiplierFlat.TryGetValue(dish.Id, out float current);
            _pendingServeMultiplierFlat[dish.Id] = current + value;
        }

        private void PlayPendingServeMultiplierTexts()
        {
            if (_pendingServeMultiplierFlat.Count == 0)
            {
                return;
            }

            var dishIds = new List<int>(_pendingServeMultiplierFlat.Keys);
            foreach (int dishId in dishIds)
            {
                PlayPendingServeMultiplierText(dishId);
            }
        }

        private void PlayPendingServeMultiplierText(int dishId)
        {
            if (!_pendingServeMultiplierFlat.TryGetValue(dishId, out float value))
            {
                return;
            }

            _pendingServeMultiplierFlat.Remove(dishId);
            if (Math.Abs(value) < 0.0001f || _sequencer == null)
            {
                return;
            }

            if (!_dishViewsById.TryGetValue(dishId, out DishPieceView view) || view == null)
            {
                return;
            }

            _sequencer.PlayFloatingText(
                _fxRoot != null ? _fxRoot : transform,
                view.WorldBounds.center + new Vector3(0f, _cellSize * 0.35f, 0f),
                $"倍率 +{value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}",
                0.55f,
                0.75f);
        }

        private void BeginMovableDishDrag(DishPieceView piece, Vector2 screenPoint)
        {
            DishInstance dish = piece?.Instance;
            if (dish == null
                || dish.Id != _movableDishId
                || _session == null
                || _session.PreparedServe != null
                || _settling
                || _activeItemTransitioning
                || _outletDragPiece != null
                || _temporaryAreaDragPiece != null)
            {
                return;
            }

            _movingPiece = piece;
            _movingOriginalPlacement = dish.Placement;
            _movingHoverPlacement = null;
            SetOutletDiscardHover(false);
            _session.DiningTable.RemoveDish(dish);
            ClearDishScopeHighlights();
            piece.SetDragPresentation(true);
            BeginDragPointerTracking(ScreenToWorld(screenPoint));
            UpdateMovableDishDrag(screenPoint);
        }

        private void UpdateMovableDishDrag(Vector2 screenPoint)
        {
            if (_movingPiece?.Instance == null || _session == null)
            {
                return;
            }

            Vector3 world = ScreenToWorld(screenPoint);
            SampleDragPointer(world);
            _movingPiece.MoveVisualCenterToWorld(world);
            bool hoveringDiscard = _session.FoodDiscardsRemaining > 0
                && _preparedDishDiscardHitTest?.Invoke(screenPoint) == true;
            SetOutletDiscardHover(hoveringDiscard);
            if (hoveringDiscard)
            {
                _movingHoverPlacement = null;
                _boardView.ClearDragPlacementFeedback();
                ClearDishScopeHighlights();
                return;
            }

            DishDragPlacementResult result = EvaluateDragPlacement(_movingPiece, world);
            if (result != null
                && result.CanCommit
                && !ContainsPlacement(_session.FindMovableDishPlacements(_movingPiece.Instance), result.Placement))
            {
                result = WithOverallState(result, DishDragCellState.Blocked);
            }

            _movingHoverPlacement = result != null && result.CanCommit
                ? result.Placement
                : null;
            _boardView.ShowDragPlacementFeedback(result);
            ShowDishScopeHighlightsAtPlacement(
                _movingPiece.Instance,
                result != null ? result.Placement : (Placement?)null);
        }

        private void EndMovableDishDrag(Vector2 screenPoint)
        {
            if (_movingPiece?.Instance == null || _session == null)
            {
                _movingPiece = null;
                _movingHoverPlacement = null;
                return;
            }

            UpdateMovableDishDrag(screenPoint);
            ClearDishScopeHighlights();
            _boardView?.ClearDragPlacementFeedback();
            DishPieceView piece = _movingPiece;
            DishInstance dish = piece.Instance;
            if (_outletHoveringDiscard)
            {
                dish.Relocate(_movingOriginalPlacement);
                _session.DiningTable.Place(dish);
                bool discarded = _session.TryDiscardPlacedDish(dish);
                SetOutletDiscardHover(false);
                if (discarded)
                {
                    string dishName = dish.Def.Name;
                    piece.gameObject.SetActive(false);
                    _movingPiece = null;
                    _movingHoverPlacement = null;
                    ResetDragPointerTracking();
                    LockMovableDish();
                    RebuildPlacedPieces();
                    _boardView.Sync();
                    SetMessage($"已丢弃：{dishName}");
                    RefreshAll();
                    _stateChanged?.Invoke();
                    return;
                }

                // 命中后状态若已失效，仍按原有移动逻辑把菜安全放回餐桌。
                _session.DiningTable.RemoveDish(dish);
                _movingHoverPlacement = null;
            }

            bool placedAtHoveredPosition = _movingHoverPlacement.HasValue;
            Placement placement = _movingHoverPlacement ?? _movingOriginalPlacement;
            IReadOnlyList<int> affectedDishIds = placedAtHoveredPosition
                ? BuildScopeAffectedDishIdsAtPlacement(dish, placement)
                : Array.Empty<int>();
            Vector2 footprintSize = piece.FootprintWorldSize;
            Vector2 releaseVelocity = _dragPointerVelocity;
            dish.Relocate(placement);
            _session.DiningTable.Place(dish);
            piece.UpdatePlacement(placement);
            piece.transform.localPosition = _boardView.Mapper.CellCenterLocal(placement.Origin);
            piece.SetDragPresentation(false);
            piece.SetGhost(false);
            piece.SetPlacementGlow(true, true);
            _movingPiece = null;
            _movingHoverPlacement = null;
            ResetDragPointerTracking();
            _boardView.Sync();
            if (placedAtHoveredPosition)
            {
                PlayDropDust(placement, footprintSize, releaseVelocity);
                PlayScopeAffectedDishFeedback(affectedDishIds);
                FlashServeScopeHighlights(dish, GetPresentationToken());
            }

            _stateChanged?.Invoke();
        }

        private void BeginTemporaryAreaDishDrag(DishPieceView piece, Vector2 screenPoint)
        {
            DishInstance dish = piece?.Instance;
            if (dish == null
                || _session?.FindTemporaryAreaDishById(dish.Id) == null
                || _settling
                || _activeItemTransitioning
                || _outletDragPiece != null
                || _movingPiece != null
                || _temporaryAreaDragPiece != null)
            {
                return;
            }

            _temporaryAreaDragPiece = piece;
            _temporaryAreaHoverPlacement = null;
            ClearDishScopeHighlights();

            Vector3 currentCenter = piece.OccupiedCellCenterWorld();
            piece.transform.DOKill();
            piece.transform.localScale = Vector3.one;
            piece.MoveVisualCenterToWorld(currentCenter);
            piece.SetDragPresentation(true);
            BeginDragPointerTracking(ScreenToWorld(screenPoint));
            UpdateTemporaryAreaDishDrag(screenPoint);
        }

        private bool IsTemporaryAreaPointerHitAccepted(
            DishPieceView candidate,
            Vector2 world)
        {
            return TemporaryAreaPointerHit.IsTopmostAt(
                candidate,
                _temporaryAreaPieces,
                world,
                _temporaryAreaDragPiece);
        }

        private void UpdateTemporaryAreaDishDrag(Vector2 screenPoint)
        {
            DishInstance dish = _temporaryAreaDragPiece?.Instance;
            if (dish == null || _session == null)
            {
                return;
            }

            Vector3 world = ScreenToWorld(screenPoint);
            SampleDragPointer(world);
            _temporaryAreaDragPiece.MoveVisualCenterToWorld(world);
            DishDragPlacementResult result = EvaluateDragPlacement(_temporaryAreaDragPiece, world);
            if (result != null
                && result.CanCommit
                && !ContainsPlacement(
                    _session.FindTemporaryAreaDishPlacements(dish.Id),
                    result.Placement))
            {
                result = WithOverallState(result, DishDragCellState.Blocked);
            }

            _temporaryAreaHoverPlacement = result != null && result.CanCommit
                ? result.Placement
                : null;
            _boardView.ShowDragPlacementFeedback(result);
            ShowDishScopeHighlightsAtPlacement(
                dish,
                result != null ? result.Placement : (Placement?)null);
        }

        private void EndTemporaryAreaDishDrag(Vector2 screenPoint)
        {
            DishInstance dish = _temporaryAreaDragPiece?.Instance;
            if (dish == null || _session == null)
            {
                _temporaryAreaDragPiece = null;
                _temporaryAreaHoverPlacement = null;
                return;
            }

            UpdateTemporaryAreaDishDrag(screenPoint);
            ClearDishScopeHighlights();
            _boardView?.ClearDragPlacementFeedback();

            DishPieceView piece = _temporaryAreaDragPiece;
            Placement? hovered = _temporaryAreaHoverPlacement;
            Vector2 footprintSize = piece.FootprintWorldSize;
            Vector2 releaseVelocity = _dragPointerVelocity;
            _temporaryAreaDragPiece = null;
            _temporaryAreaHoverPlacement = null;
            ResetDragPointerTracking();

            if (!hovered.HasValue || !_session.CommitTemporaryAreaDish(dish.Id, hovered.Value))
            {
                piece.SetDragPresentation(false);
                piece.SetGhost(false);
                piece.SetClickEnabled(true);
                LayoutTemporaryAreaPieces(animated: true);
                SetMessage("临时桌食物需要拖到餐桌合法空位。");
                _stateChanged?.Invoke();
                return;
            }

            Placement placement = hovered.Value;
            PromoteTemporaryPieceToPlaced(piece, dish, placement);
            _boardView.Sync();
            LayoutTemporaryAreaPieces(animated: true);
            RefreshTemporaryAreaVisibility(animated: true);
            PlayDropDust(placement, footprintSize, releaseVelocity);
            FlashServeScopeHighlights(dish, GetPresentationToken());
            SetMessage($"重新摆上餐桌：{dish.Def.Name}");
            _stateChanged?.Invoke();
        }

        private void PromoteTemporaryPieceToPlaced(
            DishPieceView piece,
            DishInstance dish,
            Placement placement)
        {
            _temporaryAreaPieces.Remove(piece);
            _temporaryAreaViewsById.Remove(dish.Id);

            piece.transform.DOKill();
            piece.UpdatePlacement(placement);
            piece.transform.localScale = Vector3.one;
            piece.transform.localPosition = _boardView.Mapper.CellCenterLocal(placement.Origin);
            piece.SetDragPresentation(false);
            piece.SetSortingOrderOffset(0);
            piece.SetPointerHitFilter(null);
            piece.SetGhost(false);
            piece.SetClickEnabled(true);
            piece.SetHoverCallbacks(OnDishHoverEntered, OnDishHoverExited);
            ConfigurePlacedPieceInteraction(piece, dish);

            _placedPieces.Add(piece);
            _dishViewsById[dish.Id] = piece;
        }

        private void LayoutTemporaryAreaPieces(
            bool animated = false,
            IReadOnlyList<TemporaryAreaStackSlot> slotsOverride = null)
        {
            if (_session == null || _temporaryAreaPieces.Count == 0)
            {
                return;
            }

            int count = _session.TemporaryAreaDishes.Count;
            IReadOnlyList<TemporaryAreaStackSlot> slots =
                slotsOverride ?? CalculateTemporaryAreaSlots();
            for (int i = 0; i < count; i++)
            {
                DishInstance dish = _session.TemporaryAreaDishes[i];
                if (dish == null
                    || !_temporaryAreaViewsById.TryGetValue(dish.Id, out DishPieceView piece)
                    || piece == null
                    || piece == _temporaryAreaDragPiece)
                {
                    continue;
                }

                TemporaryAreaStackSlot slot = i < slots.Count
                    ? slots[i]
                    : new TemporaryAreaStackSlot(
                        _temporaryArea != null
                            ? (Vector2)_temporaryArea.position
                            : (Vector2)transform.position,
                        1f);
                Vector3 slotCenter = new Vector3(
                    slot.Center.x,
                    slot.Center.y,
                    _temporaryArea != null ? _temporaryArea.position.z : transform.position.z);
                Vector3 targetPosition = TemporaryAreaPieceRootPosition(piece, slotCenter, slot.Scale);
                piece.transform.DOKill();
                piece.SetDragPresentation(false);
                // TemporaryArea 是 WorldUI Canvas；临时菜需保持在 PiecesFlying 层才能显示在桌面背景之上。
                piece.SetFlying(true);
                piece.SetSortingOrderOffset(i * TemporaryAreaSortingStride);
                piece.SetClickEnabled(true);

                if (animated && piece.gameObject.activeInHierarchy)
                {
                    piece.transform
                        .DOMove(targetPosition, TemporaryAreaLayoutDuration)
                        .SetEase(Ease.OutCubic)
                        .SetLink(piece.gameObject);
                    piece.transform
                        .DOScale(Vector3.one * slot.Scale, TemporaryAreaLayoutDuration)
                        .SetEase(Ease.OutCubic)
                        .SetLink(piece.gameObject);
                }
                else
                {
                    piece.transform.position = targetPosition;
                    piece.transform.localScale = Vector3.one * slot.Scale;
                }
            }
        }

        private int TemporaryAreaDishIndex(int dishId)
        {
            if (_session == null)
            {
                return 0;
            }

            for (int i = 0; i < _session.TemporaryAreaDishes.Count; i++)
            {
                if (_session.TemporaryAreaDishes[i]?.Id == dishId)
                {
                    return i;
                }
            }

            return 0;
        }

        private TemporaryAreaStackSlot[] CalculateTemporaryAreaSlots()
        {
            int count = _session?.TemporaryAreaDishes.Count ?? 0;
            if (count <= 0)
            {
                return Array.Empty<TemporaryAreaStackSlot>();
            }

            Vector2 fallbackCenter = _temporaryArea != null
                ? (Vector2)_temporaryArea.position
                : (Vector2)transform.position;
            if (!TryGetTemporaryAreaContentRect(out Rect rect))
            {
                var fallback = new TemporaryAreaStackSlot[count];
                for (int i = 0; i < count; i++)
                {
                    fallback[i] = new TemporaryAreaStackSlot(fallbackCenter, 1f);
                }

                return fallback;
            }

            var footprints = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                DishInstance dish = _session.TemporaryAreaDishes[i];
                // 数据层已在动画开始前写入旋转后的 Placement；始终使用它，
                // 避免飞入表现仍持有旧 CurrentShape 时算出另一套临时槽位。
                footprints[i] = TemporaryAreaFootprint(dish);
            }

            return TemporaryAreaStackLayout.Calculate(
                rect,
                footprints,
                TemporaryAreaPiecePaddingRatio,
                TemporaryAreaVisibleRatio);
        }

        private Vector2 TemporaryAreaFootprint(DishInstance dish)
        {
            DishShape shape = dish?.Placement.Orientation;
            if (shape == null)
            {
                return Vector2.one * Mathf.Max(_cellSize, 0.01f);
            }

            float pitch = _cellSize + Gap;
            return new Vector2(
                Mathf.Max(_cellSize, (shape.Width - 1) * pitch + _cellSize),
                Mathf.Max(_cellSize, (shape.Height - 1) * pitch + _cellSize));
        }

        private bool TryGetTemporaryAreaContentRect(out Rect rect)
        {
            rect = default;
            if (_temporaryArea == null)
            {
                return false;
            }

            var corners = new Vector3[4];
            _temporaryArea.GetWorldCorners(corners);
            float minX = Mathf.Min(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            float maxX = Mathf.Max(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            float minY = Mathf.Min(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
            float maxY = Mathf.Max(corners[0].y, corners[1].y, corners[2].y, corners[3].y);

            RectTransform title = _temporaryArea.Find("Title") as RectTransform;
            if (title != null)
            {
                var titleCorners = new Vector3[4];
                title.GetWorldCorners(titleCorners);
                float titleMinY = Mathf.Min(
                    titleCorners[0].y,
                    titleCorners[1].y,
                    titleCorners[2].y,
                    titleCorners[3].y);
                maxY = Mathf.Min(maxY, titleMinY);
            }

            rect = Rect.MinMaxRect(
                minX + TemporaryAreaPadding,
                minY + TemporaryAreaPadding,
                maxX - TemporaryAreaPadding,
                maxY - TemporaryAreaPadding);
            return rect.width > 0.01f && rect.height > 0.01f;
        }

        private Vector3 TemporaryAreaPieceRootPosition(
            DishPieceView piece,
            Vector3 desiredCenter,
            float scale)
        {
            if (piece == null)
            {
                return desiredCenter;
            }

            Transform parent = piece.transform.parent;
            Vector3 localOffset = piece.OccupiedCellCenterLocal() * scale;
            Vector3 worldOffset = parent != null
                ? parent.TransformVector(localOffset)
                : localOffset;
            return desiredCenter - worldOffset;
        }

        private DishDragPlacementResult EvaluateDragPlacement(DishPieceView piece, Vector3 visualCenterWorld)
        {
            if (piece?.CurrentShape == null || _session?.DiningTable == null || _boardView?.Mapper == null)
            {
                return null;
            }

            return DishDragPlacementEvaluator.Evaluate(
                _session.DiningTable,
                _boardView.Mapper,
                piece.CurrentShape,
                piece.RotationIndex,
                visualCenterWorld);
        }

        private static DishDragPlacementResult WithOverallState(
            DishDragPlacementResult source,
            DishDragCellState state)
        {
            return source == null
                ? null
                : new DishDragPlacementResult(
                    source.Placement,
                    source.CenterCell,
                    state,
                    source.Cells);
        }

        private static bool ContainsPlacement(IReadOnlyList<Placement> placements, Placement placement)
        {
            if (placements == null)
            {
                return false;
            }

            for (int i = 0; i < placements.Count; i++)
            {
                Placement candidate = placements[i];
                if (candidate.RotationIndex == placement.RotationIndex
                    && candidate.Origin.Equals(placement.Origin))
                {
                    return true;
                }
            }

            return false;
        }

        private void BeginDragPointerTracking(Vector3 world)
        {
            _dragPointerPreviousWorld = world;
            _dragPointerPreviousTime = Time.unscaledTime;
            _dragPointerVelocity = Vector2.zero;
            _dragPointerSampled = true;
        }

        private void SampleDragPointer(Vector3 world)
        {
            float now = Time.unscaledTime;
            if (!_dragPointerSampled)
            {
                BeginDragPointerTracking(world);
                return;
            }

            float delta = now - _dragPointerPreviousTime;
            if (delta > 0.0001f)
            {
                Vector2 instantaneous = (world - _dragPointerPreviousWorld) / delta;
                float maxSpeed = Mathf.Max(_cellSize, 0.1f) * 30f;
                instantaneous = Vector2.ClampMagnitude(instantaneous, maxSpeed);
                _dragPointerVelocity = Vector2.Lerp(_dragPointerVelocity, instantaneous, 0.65f);
            }

            _dragPointerPreviousWorld = world;
            _dragPointerPreviousTime = now;
        }

        private void ResetDragPointerTracking()
        {
            _dragPointerSampled = false;
            _dragPointerVelocity = Vector2.zero;
            _dragPointerPreviousTime = 0f;
            _dragPointerPreviousWorld = Vector3.zero;
        }

        private void PlayDropDust(
            Placement placement,
            Vector2 footprintWorldSize,
            Vector2 pointerVelocityWorld)
        {
            DiningTableCoordinateMapper mapper = _boardView?.Mapper;
            if (mapper == null)
            {
                return;
            }

            Vector3 centerLocal = mapper.CellCenterLocal(placement.Origin)
                + OccupiedCellCenterOffsetLocal(placement.Orientation, mapper.Pitch);
            Vector3 centerWorld = mapper.Root != null
                ? mapper.Root.TransformPoint(centerLocal)
                : centerLocal;
            DishDropDustView.Play(
                _dishDropDustPrefab,
                _fxRoot != null ? _fxRoot : transform,
                centerWorld,
                footprintWorldSize,
                placement.Orientation.CellCount,
                pointerVelocityWorld);
        }

        private static Vector3 OccupiedCellCenterOffsetLocal(DishShape shape, float pitch)
        {
            if (shape == null || shape.CellCount <= 0)
            {
                return Vector3.zero;
            }

            Vector3 sum = Vector3.zero;
            foreach (GridPos cell in shape.Cells)
            {
                sum += new Vector3(cell.X * pitch, -cell.Y * pitch, 0f);
            }

            return sum / shape.CellCount;
        }

        private void LockMovableDish()
        {
            if (_movableDishId > 0 && _dishViewsById.TryGetValue(_movableDishId, out DishPieceView piece) && piece != null)
            {
                piece.SetMoveCallbacks(null, null, null);
                piece.SetPlacementGlow(false, false);
            }

            _movableDishId = -1;
        }

        private void CancelServeInteractions()
        {
            ClearDishScopeHighlights();
            _boardView?.ClearDragPlacementFeedback();
            ClearOutletDragPreview();
            if (_movingPiece?.Instance != null && _session?.DiningTable != null)
            {
                DishInstance dish = _movingPiece.Instance;
                dish.Relocate(_movingOriginalPlacement);
                if (!IsDishOnTable(dish.Id))
                {
                    _session.DiningTable.Place(dish);
                }

                _movingPiece.UpdatePlacement(_movingOriginalPlacement);
                _movingPiece.transform.localPosition = _boardView.Mapper.CellCenterLocal(_movingOriginalPlacement.Origin);
                _movingPiece.SetDragPresentation(false);
                _movingPiece.SetGhost(false);
            }

            _movingPiece = null;
            _movingHoverPlacement = null;
            if (_temporaryAreaDragPiece != null)
            {
                _temporaryAreaDragPiece.SetDragPresentation(false);
                _temporaryAreaDragPiece.SetGhost(false);
            }

            _temporaryAreaDragPiece = null;
            _temporaryAreaHoverPlacement = null;
            ResetDragPointerTracking();
            LockMovableDish();
            LayoutTemporaryAreaPieces();
        }

        private bool IsDishOnTable(int dishId)
        {
            if (_session?.DiningTable == null)
            {
                return false;
            }

            foreach (DishInstance dish in _session.DiningTable.Dishes)
            {
                if (dish.Id == dishId)
                {
                    return true;
                }
            }

            return false;
        }

        private void ComputeViewport()
        {
            if (_camera != null && _camera.orthographic && _camera.orthographicSize > 0f)
            {
                _halfH = _camera.orthographicSize;
                _halfW = _camera.orthographicSize * _camera.aspect;
            }
            else
            {
                _halfH = FallbackHalfH;
                _halfW = FallbackHalfW;
            }
        }

        private void EnsureSequencer()
        {
            if (_sequencer == null)
            {
                _sequencer = GetComponent<SettlementSequencer>();
            }

            EnsureScopeHighlights();
        }

        private Transform EnsureChildRoot(string childName)
        {
            Transform child = transform.Find(childName);
            if (child != null)
            {
                return child;
            }

            Debug.LogError($"{nameof(BattleWorldController)} 缺少预置子节点 {childName}。", this);
            return null;
        }

        private Transform EnsurePiecesRoot()
        {
            if (_piecesRoot != null)
            {
                return _piecesRoot;
            }

            _piecesRoot = EnsureChildRoot("PiecesRoot");
            return _piecesRoot;
        }

        /// <summary>
        /// 记录 BattleForm 的 HUD 空区作为兼容回退；场景中配置了 _sceneBoardArea 时不会覆盖设计师布局。
        /// </summary>
        public void SetTableArea(RectTransform area)
        {
            _hudBoardArea = area;
        }

        internal bool TryComputeTableAreaPlacement(GpTable board, out BoardPlacement placement)
        {
            if (board != null && TryComputeTableAreaRect(out float left, out float right, out float bottom, out float top))
            {
                placement = DiningTableLayout.ComputeInRect(left, right, bottom, top, board, BoardAreaMinCellSize);
                return true;
            }

            placement = default;
            return false;
        }

        private void BuildTable(GpTable board)
        {
            // 餐桌按胃包围盒适配到场景 TableLayoutArea；场景未配置时回退到 HUD/视口区域。
            BoardPlacement placement = TryComputeTableAreaPlacement(board, out BoardPlacement boardAreaPlacement)
                ? boardAreaPlacement
                : DiningTableLayout.Compute(_halfW, _halfH, board, FoodTableBottomMargin);
            _cellSize = placement.CellSize;
            _boardCenter = placement.Position;

            // 局部空间：餐桌以 DiningTableView.transform 为局部帧（BoardRoot），世界摆放/居中由其 transform 决定。
            // 保持 scale 恒等、rotation 恒等，避免子级格子/食物被二次缩放或旋转。
            _boardView.transform.rotation = Quaternion.identity;
            _boardView.transform.localScale = Vector3.one;
            _boardView.transform.position = _boardCenter;

            _boardView.Build(board, _cellSize, Gap, OnCellClicked, _boardCellPrefab);
            _boardView.SetCellHoverCallbacks(OnCellHoverEntered, OnCellHoverExited);

            // DiningTableView.Build 只重建格子；PiecesRoot 仍挂回 BoardRoot，共用餐桌局部坐标系。
            Transform piecesRoot = EnsurePiecesRoot();
            if (piecesRoot != null && piecesRoot.parent != _boardView.transform)
            {
                piecesRoot.SetParent(_boardView.transform, worldPositionStays: false);
                piecesRoot.localPosition = Vector3.zero;
                piecesRoot.localRotation = Quaternion.identity;
                piecesRoot.localScale = Vector3.one;
            }
        }

        /// <summary>读取场景或 HUD 布局区域，得到餐桌可用区的世界矩形边界。</summary>
        private bool TryComputeTableAreaRect(out float left, out float right, out float bottom, out float top)
        {
            left = right = bottom = top = 0f;
            RectTransform boardArea = _sceneBoardArea != null ? _sceneBoardArea : _hudBoardArea;
            if (boardArea == null || _camera == null)
            {
                return false;
            }

            var corners = new Vector3[4];
            boardArea.GetWorldCorners(corners);

            Canvas canvas = boardArea.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                left = Mathf.Min(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
                right = Mathf.Max(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
                bottom = Mathf.Min(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
                top = Mathf.Max(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
                return right > left && top > bottom;
            }

            Camera uiCam = canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            float depth = -_camera.transform.position.z;
            for (int i = 0; i < 4; i++)
            {
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(uiCam, corners[i]);
                Vector3 world = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth));
                minX = Mathf.Min(minX, world.x);
                maxX = Mathf.Max(maxX, world.x);
                minY = Mathf.Min(minY, world.y);
                maxY = Mathf.Max(maxY, world.y);
            }

            left = minX;
            right = maxX;
            bottom = minY;
            top = maxY;
            return maxX > minX && maxY > minY;
        }

        private void RebuildPlacedPieces()
        {
            ClearPlacedPieces();
            if (_session == null)
            {
                return;
            }

            foreach (DishInstance dish in _session.DiningTable.Dishes)
            {
                CreatePlacedPiece(dish);
            }

            RebuildTemporaryAreaPieces();
        }

        private void ClearPlacedPieces()
        {
            _scopeHighlights?.ClearAll();
            foreach (DishPieceView piece in _placedPieces)
            {
                if (piece != null)
                {
                    Destroy(piece.gameObject);
                }
            }

            _placedPieces.Clear();
            _dishViewsById.Clear();

            foreach (DishPieceView piece in _temporaryAreaPieces)
            {
                if (piece != null)
                {
                    Destroy(piece.gameObject);
                }
            }

            _temporaryAreaPieces.Clear();
            _temporaryAreaViewsById.Clear();
        }

        private DishPieceView CreatePlacedPiece(DishInstance dish)
        {
            DishPieceView piece;
            if (_dishPiecePrefab != null)
            {
                piece = Instantiate(_dishPiecePrefab, _piecesRoot);
            }
            else
            {
                Debug.LogError($"{nameof(BattleWorldController)} 缺少 DishPiece prefab。", this);
                return null;
            }

            piece.gameObject.name = $"Dish_{dish.Id}_{dish.Def.Id}";
            // 食物挂在 BoardRoot 下，用局部坐标贴格（与餐桌共享局部帧）。
            piece.transform.localPosition = _boardView.Mapper.CellCenterLocal(dish.Placement.Origin);
            piece.BuildPlaced(dish, _spriteProvider.Get(dish.Def), _cellSize, _cellSize + Gap, _dishClicked);
            piece.SetHoverCallbacks(OnDishHoverEntered, OnDishHoverExited);
            ConfigurePlacedPieceInteraction(piece, dish);
            _placedPieces.Add(piece);
            _dishViewsById[dish.Id] = piece;
            return piece;
        }

        private void ConfigurePlacedPieceInteraction(DishPieceView piece, DishInstance dish)
        {
            if (dish.Id == _movableDishId && _session?.PreparedServe == null)
            {
                piece.SetMoveCallbacks(BeginMovableDishDrag, UpdateMovableDishDrag, EndMovableDishDrag);
                piece.SetPlacementGlow(true, true);
            }
            else
            {
                piece.SetMoveCallbacks(null, null, null);
                piece.SetPlacementGlow(false, false);
            }
        }

        private void RebuildTemporaryAreaPieces()
        {
            if (_session == null || _temporaryArea == null || _dishPiecePrefab == null)
            {
                return;
            }

            for (int i = 0; i < _session.TemporaryAreaDishes.Count; i++)
            {
                DishInstance dish = _session.TemporaryAreaDishes[i];
                DishPieceView piece = Instantiate(_dishPiecePrefab, _piecesRoot);
                piece.gameObject.name = $"TemporaryAreaDish_{dish.Id}_{dish.Def.Id}";
                piece.BuildPlaced(dish, _spriteProvider.Get(dish.Def), _cellSize, _cellSize + Gap, _dishClicked);
                piece.SetHoverCallbacks(OnDishHoverEntered, OnDishHoverExited);
                piece.SetMoveCallbacks(
                    BeginTemporaryAreaDishDrag,
                    UpdateTemporaryAreaDishDrag,
                    EndTemporaryAreaDishDrag);
                piece.SetPointerHitFilter(IsTemporaryAreaPointerHitAccepted);
                piece.SetPlacementGlow(false, false);
                _temporaryAreaPieces.Add(piece);
                _temporaryAreaViewsById[dish.Id] = piece;
            }

            LayoutTemporaryAreaPieces();
        }

        public void ShowDishScopeHighlights(DishInstance dish)
        {
            if (_settling
                || _session == null
                || dish == null
                || _session.FindDishById(dish.Id) == null)
            {
                ClearDishScopeHighlights();
                return;
            }

            EnsureScopeHighlights();
            IReadOnlyList<SkillExecutionTrace> traces = BuildHoverScopeTraces(dish);
            _scopeHighlights.ShowPersistent(_boardView, traces);
        }

        private void ShowDishScopeHighlightsAtPlacement(DishInstance dish, Placement? placement)
        {
            if (_settling || _session == null || dish == null || !placement.HasValue)
            {
                ClearDishScopeHighlights();
                return;
            }

            EnsureScopeHighlights();
            IReadOnlyList<SkillExecutionTrace> traces = BuildHoverScopeTracesAtPlacement(
                dish,
                placement.Value,
                SkillScopeVisualMode.CandidateScope);
            _scopeHighlights.ShowPersistent(_boardView, traces);
        }

        public void ClearDishScopeHighlights()
        {
            _scopeHighlights?.ClearPersistent();
        }

        private void FlashServeScopeHighlights(DishInstance dish, CancellationToken cancellationToken)
        {
            if (_session == null || dish == null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            EnsureScopeHighlights();
            IReadOnlyList<SkillExecutionTrace> traces = BuildHoverScopeTraces(dish);
            _scopeHighlights.Flash(_boardView, traces, cancellationToken);
        }

        private IReadOnlyList<SkillExecutionTrace> BuildHoverScopeTracesAtPlacement(
            DishInstance dish,
            Placement placement,
            SkillScopeVisualMode visualMode)
        {
            if (dish == null)
            {
                return Array.Empty<SkillExecutionTrace>();
            }

            Placement originalPlacement = dish.Placement;
            dish.Relocate(placement);
            try
            {
                return BuildHoverScopeTraces(dish, visualMode);
            }
            finally
            {
                dish.Relocate(originalPlacement);
            }
        }

        private IReadOnlyList<int> BuildScopeAffectedDishIdsAtPlacement(
            DishInstance dish,
            Placement placement)
        {
            IReadOnlyList<SkillExecutionTrace> traces = BuildHoverScopeTracesAtPlacement(
                dish,
                placement,
                SkillScopeVisualMode.ResolvedTargets);
            var result = new List<int>();
            var seen = new HashSet<int>();
            foreach (SkillExecutionTrace trace in traces)
            {
                foreach (int instanceId in trace.VisualTargetDishInstanceIds)
                {
                    if (instanceId > 0 && instanceId != dish.Id && seen.Add(instanceId))
                    {
                        result.Add(instanceId);
                    }
                }
            }

            return result;
        }

        private void PlayScopeAffectedDishFeedback(IReadOnlyList<int> dishIds)
        {
            if (dishIds == null)
            {
                return;
            }

            foreach (int dishId in dishIds)
            {
                if (_dishViewsById.TryGetValue(dishId, out DishPieceView view) && view != null)
                {
                    view.PlayScopeAffectedShake();
                }
            }
        }

        private IReadOnlyList<SkillExecutionTrace> BuildHoverScopeTraces(
            DishInstance dish,
            SkillScopeVisualMode visualMode = SkillScopeVisualMode.CandidateScope)
        {
            var traces = new List<SkillExecutionTrace>();
            if (dish == null || _session?.Database == null || _session.DiningTable == null)
            {
                return traces;
            }

            int visualIndex = 0;
            foreach (string skillId in dish.SkillIds)
            {
                SkillDef skill = _session.Database.GetSkill(skillId);
                if (skill == null || !skill.HasRules)
                {
                    continue;
                }

                string sourceLabel = dish.GetSkillSource(skillId);
                SkillExecutionKind kind = !string.IsNullOrEmpty(sourceLabel)
                                          && sourceLabel.IndexOf("技能复制", StringComparison.OrdinalIgnoreCase) >= 0
                    ? SkillExecutionKind.CopiedSkill
                    : SkillExecutionKind.NativeSkill;

                foreach (SkillRuleDef rule in skill.Rules)
                {
                    if (!ShouldShowHoverScope(rule))
                    {
                        continue;
                    }

                    SkillExecutionTrace trace = SkillExecutionTrace.Create(
                        _session.Database,
                        _session.DiningTable,
                        dish,
                        dish,
                        skill,
                        rule,
                        kind,
                        sourceLabel,
                        visualMode);
                    if (trace != null)
                    {
                        traces.Add(trace.WithVisualIndex(visualIndex++));
                    }
                }
            }

            foreach (TransferredSkill transferred in dish.TransferredSkills)
            {
                SkillRuleDef rule = transferred.Rule;
                if (!ShouldShowHoverScope(rule))
                {
                    continue;
                }

                SkillDef skill = _session.Database.GetSkill(rule.SkillId);
                DishInstance owner = FindDish(transferred.SourceInstanceId);
                SkillExecutionTrace trace = owner != null
                    ? SkillExecutionTrace.Create(
                        _session.Database,
                        _session.DiningTable,
                        owner,
                        dish,
                        skill,
                        rule,
                        SkillExecutionKind.SweetTransfer,
                        transferred.SourceLabel,
                        visualMode)
                    : SkillExecutionTrace.CreateWithOwnerFallback(
                        _session.Database,
                        _session.DiningTable,
                        transferred.SourceInstanceId,
                        SourceNameWithoutTag(transferred.SourceLabel),
                        dish,
                        skill,
                        rule,
                        SkillExecutionKind.SweetTransfer,
                        transferred.SourceLabel,
                        visualMode);
                if (trace != null)
                {
                    traces.Add(trace.WithVisualIndex(visualIndex++));
                }
            }

            return traces;
        }

        private static bool ShouldShowHoverScope(SkillRuleDef rule)
        {
            if (rule == null || rule.ActionType == SkillActionType.None)
            {
                return false;
            }

            if (rule.Trigger == SkillTrigger.OnSettle)
            {
                return true;
            }

            return rule.ActionType == SkillActionType.TransferSkills
                || rule.ActionType == SkillActionType.TriggerSweetTransfer
                || rule.ActionType == SkillActionType.CopySkill;
        }

        private DishInstance FindDish(int instanceId)
        {
            if (_session?.DiningTable == null || instanceId <= 0)
            {
                return null;
            }

            foreach (DishInstance dish in _session.DiningTable.Dishes)
            {
                if (dish.Id == instanceId)
                {
                    return dish;
                }
            }

            return null;
        }

        private static string SourceNameWithoutTag(string sourceLabel)
        {
            if (string.IsNullOrEmpty(sourceLabel))
            {
                return string.Empty;
            }

            int index = sourceLabel.IndexOf('<');
            return index > 0 ? sourceLabel.Substring(0, index) : sourceLabel;
        }

        private void OnDishHoverEntered(DishPieceView piece)
        {
            _dishHoverEntered?.Invoke(piece);
        }

        private void OnDishHoverExited(DishPieceView piece)
        {
            _dishHoverExited?.Invoke(piece);
        }

        private void OnCellHoverEntered(DiningTableCellView cell)
        {
            _cellHoverEntered?.Invoke(cell);
        }

        private void OnCellHoverExited(DiningTableCellView cell)
        {
            _cellHoverExited?.Invoke(cell);
        }

        public string DoodleToggleLabel => _doodle != null && _doodle.IsVisible ? "隐藏涂鸦" : "显示涂鸦";

        /// <summary>每次进入经营挑战时清空笔迹，并把涂鸦层复位为可见。</summary>
        public void ResetDoodle()
        {
            if (_doodle == null)
            {
                return;
            }

            _doodle.Clear();
            _doodle.SetVisible(true);
        }

        public void ClearDoodle()
        {
            _doodle?.Clear();
        }

        public bool ToggleDoodleVisible()
        {
            if (_doodle == null)
            {
                return false;
            }

            bool next = !_doodle.IsVisible;
            _doodle.SetVisible(next);
            return next;
        }

        private void OnCellClicked(GridPos pos)
        {
            if (_session == null)
            {
                return;
            }

            DishInstance dish = _session.DiningTable.DishAt(pos);
            if (dish != null)
            {
                _dishClicked?.Invoke(dish);
            }
        }

        private void SetMessage(string message)
        {
            _messageSink?.Invoke(message);
        }

        public void ShowMessage(string message)
        {
            SetMessage(message);
        }

        private string PrepareServeMessage(ServePrepareOutcome outcome, int slotIndex)
        {
            switch (outcome)
            {
                case ServePrepareOutcome.SlotEmpty:
                    return $"食谱{slotIndex + 1} 已空。";
                case ServePrepareOutcome.NoFittingDish:
                    return "剩余食物都无法摆入当前餐桌。";
                case ServePrepareOutcome.LimitReached:
                    return $"限量供应：本局最多上 {_session.MaxServes} 道菜。";
                case ServePrepareOutcome.AlreadyPrepared:
                    return "先把出菜口的食物摆上餐桌。";
                default:
                    return "现在不能出菜。";
            }
        }

        /// <summary>播放背包乱斗式逐菜结算演出，完成后回调上层决定过关/失败 UI。</summary>
        public async void PlaySettlement(
            ScoreResult result,
            SettlementBaselineSnapshot baselineSnapshot,
            SettlementScoreFireView scoreFire,
            Action<SettlementRevealSignal> onReveal,
            Action<string> onPassiveTriggered,
            Action<SettlementBeatSignal> onBeat,
            Action onComplete)
        {
            if (_sequencer == null || _session == null || result == null)
            {
                onComplete?.Invoke();
                return;
            }

            _settling = true;
            SetTemporaryAreaVisible(false, animated: false);

            foreach (DishPieceView piece in _temporaryAreaPieces)
            {
                if (piece != null)
                {
                    piece.gameObject.SetActive(false);
                }
            }

            _scopeHighlights?.ClearAll();
            CancellationToken token = GetPresentationToken();
            try
            {
                await _sequencer.PlayAsync(
                    _session,
                    result,
                    _dishViewsById,
                    _boardView.Mapper,
                    _fxRoot,
                    scoreFire,
                    RenderSettlementScore,
                    onReveal,
                    OnSettlementScope,
                    onPassiveTriggered,
                    onBeat,
                    baselineSnapshot,
                    token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }
            if (!token.IsCancellationRequested)
            {
                _settling = false;
                RefreshAll();
                onComplete?.Invoke();
            }
        }

        private void RenderSettlementScore(int score)
        {
            _settlementScoreSink?.Invoke(score);
        }

        private void OnSettlementScope(SettlementScopeSignal signal)
        {
            EnsureScopeHighlights();
            if (signal.IsEmpty || signal.Trace == null)
            {
                _scopeHighlights?.ClearSettlement();
                return;
            }

            _scopeHighlights?.ShowSettlement(_boardView, signal.Trace);
        }

    }
}
