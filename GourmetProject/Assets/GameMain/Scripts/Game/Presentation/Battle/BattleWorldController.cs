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
    /// 战斗内场景表现根控制器：餐桌、后厨、手牌区与拖拽放置。
    /// 现以场景内组件存在：背景/餐桌根/各锚点/分数文本/固定按钮均在 Battle.unity 摆好并通过 SerializeField 注入，
    /// 运行时只生成数据驱动内容（餐桌格随胃尺寸、菜品、道具槽、结算特效）。
    /// </summary>
    public sealed class BattleWorldController : MonoBehaviour
    {
        public const float Gap = DiningTableLayout.Gap;
        private const float MaxCellSize = DiningTableLayout.MaxCellSize;
        private const float MinCellSize = DiningTableLayout.MinCellSize;

        // 餐桌居中定位的底部边距：Food 态给底部菜谱抽屉让 2.7，编辑态还要给候选托盘条让到 3.6。
        private const float FoodTableBottomMargin = 2.7f;
        private const float EditTableBottomMargin = 3.6f;
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
        [SerializeField] private BattleDoodleController _doodle;

        // —— 运行时实例化用的 prefab ——
        [Header("Prefabs")]
        [SerializeField] private DiningTableCellView _boardCellPrefab;
        [SerializeField] private DishPieceView _dishPiecePrefab;
        [SerializeField] private ServeHandView _serveHandPrefab;
        [SerializeField] private WorldTargetArrow _worldTargetArrowPrefab;
        [Tooltip("食物调整态的世界按钮（X/勾/撤销）prefab，复用 Prefabs/Battle/WorldButton。")]
        [SerializeField] private WorldButtonView _worldButtonPrefab;

        [Header("上菜动画")]
        [SerializeField] private float _serveCarryScale = 1.22f;
        [SerializeField] private float _serveDescendDuration = 0.34f;
        [SerializeField] private float _serveWithdrawDuration = 0.18f;
        [SerializeField] private float _serveDropDuration = 0.24f;

        // 餐桌锁定在该屏幕矩形内 fit 并居中（由 BattleForm 传入的 HUD 空区 BoardArea）；为空则回落视口边距布局。
        private const float BoardAreaMinCellSize = 0.12f;
        private RectTransform _boardArea;

        // 按餐桌尺寸自适应的单格世界尺寸与餐桌中心，BuildTable 中计算。
        private float _cellSize = MaxCellSize;
        private Vector3 _boardCenter = new Vector3(0f, 0.15f, 0f);
        private float _halfW = FallbackHalfW;
        private float _halfH = FallbackHalfH;

        private ServeAnimator _serveAnimator;

        // 餐桌编辑 / 只读餐桌视图的表现与交互拆到协作组件；本类只做 Food 态与世界互斥态调度（外壳）。
        private DiningTableEditController _boardEdit;

        // 食物调整（局内删除/移动菜品）交互拆到协作组件，仅 Food 态启用。
        private FoodAdjustController _foodAdjust;

        private readonly DishSpriteProvider _spriteProvider = new DishSpriteProvider();
        private readonly List<DishPieceView> _placedPieces = new List<DishPieceView>();
        private readonly Dictionary<int, DishPieceView> _dishViewsById = new Dictionary<int, DishPieceView>();

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
        private bool _serving;
        private WorldMode _worldMode = WorldMode.Hidden;

        private Action<string> _messageSink;
        private Action<int> _settlementScoreSink;
        private Action<string> _activeItemClicked;
        private Action<DishInstance> _dishClicked;
        private Action<DishPieceView> _dishHoverEntered;
        private Action<DishPieceView> _dishHoverExited;
        private Action<DiningTableCellView> _cellHoverEntered;
        private Action<DiningTableCellView> _cellHoverExited;
        private Action _stateChanged;
        private CancellationTokenSource _presentationCts;
        private Tween _tableViewFadeTween;
        private readonly Dictionary<SpriteRenderer, float> _tableViewRendererBaseAlphas = new Dictionary<SpriteRenderer, float>();
        private float _tableViewTransitionAlpha = 1f;

        /// <summary>当前已加载战斗场景里的控制器实例（由战斗 UI/流程取用）。</summary>
        public static BattleWorldController Instance { get; private set; }

        public bool CanEnterTableView
            => _worldMode != WorldMode.TableView
                && _worldMode != WorldMode.TableCellTargeting
                && (_worldMode != WorldMode.Food || (!_settling && !_serving));

        private void Awake()
        {
            Instance = this;

            EnsureTableEdit();

            // 默认非美食态：世界餐桌与其专属按钮（总览/吃/涂鸦）默认隐藏，只有 StartBattle→Initialize 才显示。
            // 场景里 BattleSceneRoot 默认 active，若不在此处收起，行动选择等非美食态一进场景就会露出这堆美食专属按钮。
            HideWorld();
        }

        /// <summary>确保餐桌编辑协作组件存在并注入共享场景引用（运行时挂到同一战斗场景根上）。</summary>
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

        private void EnsureFoodAdjust()
        {
            if (_foodAdjust == null)
            {
                _foodAdjust = GetComponent<FoodAdjustController>();
                if (_foodAdjust == null)
                {
                    _foodAdjust = gameObject.AddComponent<FoodAdjustController>();
                }
            }

            _foodAdjust.Configure(this);
        }

        /// <summary>是否正处于食物调整态。</summary>
        public bool IsFoodAdjusting => _foodAdjust != null && _foodAdjust.IsActive;

        /// <summary>进入食物调整态（仅 Food 态、未结算时可用）。<paramref name="onExited"/> 供确认提交后自动退出时回通知壳更新 UI。</summary>
        public void BeginFoodAdjust(Action onExited)
        {
            if (_worldMode != WorldMode.Food || _session == null || _session.IsSettled)
            {
                return;
            }

            EnsureFoodAdjust();
            _foodAdjust.Begin(onExited);
            SetPlacedPiecesClickEnabled(false);
        }

        /// <summary>退出食物调整态（用户点「返回」或提交后调用）。会取消未提交的移动并复位表现。</summary>
        public void EndFoodAdjust()
        {
            if (_foodAdjust == null || !_foodAdjust.IsActive)
            {
                return;
            }

            _foodAdjust.End();
            RebuildPlacedPieces();
            SetPlacedPiecesClickEnabled(true);
        }

        private void SetPlacedPiecesClickEnabled(bool enabled)
        {
            foreach (DishPieceView piece in _placedPieces)
            {
                piece?.SetClickEnabled(enabled);
            }
        }

        // —— 供 FoodAdjustController 读取的内部引用 ——
        internal GpTable AdjustTable => _session?.DiningTable;
        internal DiningTableView AdjustTableView => _boardView;
        internal Transform AdjustPiecesRoot => _piecesRoot;
        internal Camera AdjustCamera => _camera;
        internal float AdjustCellSize => _cellSize;
        internal DishPieceView AdjustDishPiecePrefab => _dishPiecePrefab;
        internal WorldButtonView AdjustWorldButtonPrefab => _worldButtonPrefab;
        internal DishSpriteProvider AdjustSpriteProvider => _spriteProvider;
        internal GameRun AdjustRun => _run;

        internal DishPieceView GetPieceView(int id)
        {
            return _dishViewsById.TryGetValue(id, out DishPieceView view) ? view : null;
        }

        internal Transform ActiveTargetRoot => _piecesRoot != null ? _piecesRoot : transform;

        internal Camera ActiveTargetCamera => _camera;

        internal float ActiveTargetCellSize => _cellSize;

        internal WorldTargetArrow ActiveTargetArrowPrefab => _worldTargetArrowPrefab;

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

        internal void BeginActiveItemWorldTargeting()
        {
            SetPlacedPiecesClickEnabled(false);
        }

        internal void EndActiveItemWorldTargeting()
        {
            ClearActiveItemTargetHighlights();
            SetPlacedPiecesClickEnabled(true);
        }

        internal void SetActiveItemTargetHighlights(cfg.ItemTargetKind kind, IReadOnlyList<ActiveTarget> selected, ActiveTarget? hovered)
        {
            if (kind == cfg.ItemTargetKind.DiningTableCell)
            {
                _boardView?.ClearTargetHighlights();
                GpTable table = ActiveCellTargetTable();
                if (table != null)
                {
                    foreach (GridPos cell in table.ExistingCells())
                    {
                        var candidate = new ActiveTarget(string.Empty, cell.X, cell.Y, cfg.ItemTargetKind.DiningTableCell);
                        _boardView?.SetTargetHighlight(cell, ContainsTarget(selected, candidate), TargetEquals(hovered, candidate));
                    }
                }

                return;
            }

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
                    bool active = ContainsTarget(selected, candidate) || TargetEquals(hovered, candidate);
                    piece.SetPlacementGlow(active, true);
                }
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

        /// <summary>食物调整删除/撤销后重建餐桌菜品表现，并保持调整态下的点击屏蔽。</summary>
        internal void RebuildAfterAdjust()
        {
            RebuildPlacedPieces();
            RefreshAll();
            _stateChanged?.Invoke();
            if (IsFoodAdjusting)
            {
                SetPlacedPiecesClickEnabled(false);
            }
        }

        /// <summary>
        /// 进入餐桌编辑页：外壳先收起 Food 态表现并切到编辑互斥态，再把编辑页构建交给协作组件。
        /// </summary>
        public void BeginTableEdit(GameRun run, IReadOnlyList<string> candidateIds, Action<bool> onDone)
        {
            if (run == null)
            {
                onDone?.Invoke(false);
                return;
            }

            EnsureTableEdit();
            EndTableView();

            gameObject.SetActive(true);
            CancelPresentationTasks();
            _worldMode = WorldMode.TableEdit;
            _settling = false;
            _serving = false;
            _session = null;
            SetFoodWorldElementsVisible(false);
            ClearPlacedPieces();

            _boardEdit.BeginTableEdit(run, candidateIds, onDone);
        }

        /// <summary>进入只读餐桌视图：外壳收起 Food 态并切到餐桌视图互斥态，交由协作组件复用餐桌布局渲染。</summary>
        public void BeginTableView(GameRun run)
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
            _serving = false;
            SetFoodWorldElementsVisible(false);
            HideWorldPanels();
            ClearPlacedPieces();

            _boardEdit.BeginTableView(run);
            _boardView?.SetCellHoverCallbacks(OnCellHoverEntered, OnCellHoverExited);
        }

        /// <summary>进入主动道具餐桌选格态：布局同只读餐桌视图，但外层会用世界箭头接管点击确认/取消。</summary>
        public void BeginTableCellTargeting(GameRun run)
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
            _serving = false;
            SetFoodWorldElementsVisible(false);
            HideWorldPanels();
            ClearPlacedPieces();

            _boardEdit.BeginCellTargeting(run);
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
            if (table == null || !table.AddMaterialAt(pos, materialId))
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

        public void SkipTableEditPack()
        {
            if (_worldMode != WorldMode.TableEdit || _boardEdit == null || !_boardEdit.IsEditing)
            {
                return;
            }

            _boardEdit.SkipTableEditPack();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

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
            _run = run;
            _session = session;
            _messageSink = messageSink;
            _settlementScoreSink = settlementScoreSink;
            _stateChanged = stateChanged;
            _activeItemClicked = activeItemClicked;
            _dishClicked = dishClicked;
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            EnsureServeAnimator();
            EnsureFoodAdjust();
            if (_foodAdjust != null && _foodAdjust.IsActive)
            {
                _foodAdjust.End();
            }

            gameObject.SetActive(true);
            CancelPresentationTasks();
            _worldMode = WorldMode.Food;
            _settling = false;
            ComputeViewport();
            BuildTable(session.DiningTable);
            EnsureSequencer();
            // 道具（被动/主动）与菜谱面板已迁到常驻屏幕空间 HUD（BattleForm），世界空间不再渲染这些面板；
            // 世界空间只保留餐桌、菜品、上菜/结算演出与涂鸦表现。
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
        }

        public void SetCellHoverCallbacks(Action<DiningTableCellView> entered, Action<DiningTableCellView> exited)
        {
            _cellHoverEntered = entered;
            _cellHoverExited = exited;
            _boardView?.SetCellHoverCallbacks(OnCellHoverEntered, OnCellHoverExited);
        }

        public void HideWorld()
        {
            CancelPresentationTasks();
            ResetTableViewFade();
            if (_foodAdjust != null && _foodAdjust.IsActive)
            {
                _foodAdjust.End();
            }

            if (_boardEdit != null && _boardEdit.IsEditing)
            {
                _boardEdit.EndTableEdit();
            }

            EndTableView();
            _settling = false;
            _serving = false;
            SetFoodWorldElementsVisible(false);
            _worldMode = WorldMode.Hidden;
            gameObject.SetActive(false);
        }

        /// <summary>战斗结束后清理本场运行时餐桌表现，避免已摆菜品残留到后续非战斗状态。</summary>
        public void ClearBattleTable()
        {
            CancelPresentationTasks();
            _settling = false;
            _serving = false;
            _session = null;
            ClearPlacedPieces();
            _doodle?.Clear();
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
            if (!visible)
            {
                _doodle?.SetVisible(false);
            }
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

            _boardView.Sync();
        }

        /// <summary>隐藏迁到 HUD 的世界空间面板：被动/主动道具槽、道具标题、菜谱书。</summary>
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
            RefreshAll();
        }

        public async void TryServeDish(int slotIndex)
        {
            if (_session == null || _session.IsSettled || _settling)
            {
                return;
            }

            if (_serving)
            {
                _serveAnimator?.TrySpeedUpCurrentAnimation();
                return;
            }

            ServeResult result = _session.Serve(slotIndex);
            if (!result.Success)
            {
                SetMessage(ServeMessage(result.Outcome, slotIndex));
                RefreshAll();
                return;
            }

            DishPieceView placed = CreatePlacedPiece(result.Dish);
            Vector3 target = _boardView.Mapper.CellCenter(result.Dish.Placement.Origin);
            _serving = true;
            EnsureServeAnimator();
            _boardView.Sync();
            SetMessage($"上菜：{result.Dish.Def.Name}");
            _stateChanged?.Invoke();

            CancellationToken token = GetPresentationToken();
            try
            {
                await _serveAnimator.AnimateAsync(placed, target, _camera, _cellSize, _halfH, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }

            if (!token.IsCancellationRequested && _serving)
            {
                FinishServing();
            }
        }

        private void EnsureServeAnimator()
        {
            _serveAnimator ??= new ServeAnimator(
                transform,
                _serveHandPrefab,
                new ServeAnimator.Config
                {
                    CarryScale = _serveCarryScale,
                    DescendDuration = _serveDescendDuration,
                    WithdrawDuration = _serveWithdrawDuration,
                    DropDuration = _serveDropDuration,
                });
        }

        private void FinishServing()
        {
            _serving = false;
            RebuildPlacedPieces();
            _stateChanged?.Invoke();
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
        /// 由 BattleForm 传入 HUD 里的空区矩形，餐桌将始终 fit 并居中锁定在该屏幕区域内（超大餐桌继续缩放显示）。
        /// 传 null 回落到按视口边距布局。
        /// </summary>
        public void SetTableArea(RectTransform area)
        {
            _boardArea = area;
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
            // 中央可用区：菜谱/道具面板已迁到常驻 HUD（左右栏 + 底部菜谱抽屉），餐桌居中在中部内容区，
            // 由 DiningTableLayout 统一按胃包围盒铺满可用区并居中（与编辑/餐桌视图态共用同一套定位算法）。
            BoardPlacement placement = TryComputeTableAreaPlacement(board, out BoardPlacement boardAreaPlacement)
                ? boardAreaPlacement
                : DiningTableLayout.Compute(_halfW, _halfH, board, FoodTableBottomMargin);
            _cellSize = placement.CellSize;
            _boardCenter = placement.Position;

            // 局部空间：餐桌以 DiningTableView.transform 为局部帧（BoardRoot），世界摆放/居中由其 transform 决定。
            // 保持 scale 恒等、rotation 恒等，避免子级格子/菜品被二次缩放或旋转。
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

        /// <summary>把 HUD 里的 BoardArea 矩形四角投影到 BattleCamera 世界平面(z=0)，得到餐桌可用区的世界矩形边界。</summary>
        private bool TryComputeTableAreaRect(out float left, out float right, out float bottom, out float top)
        {
            left = right = bottom = top = 0f;
            if (_boardArea == null || _camera == null)
            {
                return false;
            }

            Canvas canvas = _boardArea.GetComponentInParent<Canvas>();
            Camera uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

            var corners = new Vector3[4];
            _boardArea.GetWorldCorners(corners);

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
        }

        private void ClearPlacedPieces()
        {
            foreach (DishPieceView piece in _placedPieces)
            {
                if (piece != null)
                {
                    Destroy(piece.gameObject);
                }
            }

            _placedPieces.Clear();
            _dishViewsById.Clear();
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
            // 菜品挂在 BoardRoot 下，用局部坐标贴格（与餐桌共享局部帧）。
            piece.transform.localPosition = _boardView.Mapper.CellCenterLocal(dish.Placement.Origin);
            piece.BuildPlaced(dish, _spriteProvider.Get(dish.Def), _cellSize, _cellSize + Gap, _dishClicked);
            piece.SetHoverCallbacks(OnDishHoverEntered, OnDishHoverExited);
            _placedPieces.Add(piece);
            _dishViewsById[dish.Id] = piece;
            return piece;
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

        /// <summary>每次进入战斗时清空笔迹，并把涂鸦层复位为可见。</summary>
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
            if (_session == null || IsFoodAdjusting)
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

        private string ServeMessage(ServeOutcome outcome, int slotIndex)
        {
            switch (outcome)
            {
                case ServeOutcome.SlotEmpty:
                    return $"菜谱{slotIndex + 1} 已空。";
                case ServeOutcome.NoFittingDish:
                    return "这本菜谱里没有能放下的菜了。";
                case ServeOutcome.LimitReached:
                    return $"限量供应：本局最多上 {_session.MaxServes} 道菜。";
                default:
                    return "现在不能上菜。";
            }
        }

        /// <summary>播放背包乱斗式逐菜结算演出，完成后回调上层决定过关/失败 UI。</summary>
        public async void PlaySettlement(ScoreResult result, SettlementScoreFireView scoreFire, Action onComplete)
        {
            if (_sequencer == null || _session == null || result == null)
            {
                onComplete?.Invoke();
                return;
            }

            _settling = true;
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
    }
}
