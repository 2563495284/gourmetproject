using System;
using System.Collections.Generic;
using System.Threading;
using DG.Tweening;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using UnityEngine;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Game.Presentation.Battle
{
    internal readonly struct BoardEditTrayHitRegion
    {
        public BoardEditTrayHitRegion(int candidateIndex, Bounds worldBounds)
        {
            CandidateIndex = candidateIndex;
            WorldBounds = worldBounds;
        }

        public int CandidateIndex { get; }

        public Bounds WorldBounds { get; }
    }

    internal static class BoardEditTrayHitTester
    {
        private const float DistanceEpsilon = 0.000001f;

        public static bool TryPick(
            Vector2 worldPoint,
            IReadOnlyList<BoardEditTrayHitRegion> regions,
            float padding,
            out int candidateIndex)
        {
            if (TryPickAtPadding(worldPoint, regions, 0f, out candidateIndex))
            {
                return true;
            }

            return padding > 0f && TryPickAtPadding(worldPoint, regions, padding, out candidateIndex);
        }

        private static bool TryPickAtPadding(
            Vector2 worldPoint,
            IReadOnlyList<BoardEditTrayHitRegion> regions,
            float padding,
            out int candidateIndex)
        {
            candidateIndex = -1;
            float closestDistance = float.PositiveInfinity;
            if (regions == null)
            {
                return false;
            }

            for (int i = 0; i < regions.Count; i++)
            {
                BoardEditTrayHitRegion region = regions[i];
                Bounds bounds = region.WorldBounds;
                bounds.Expand(new Vector3(padding * 2f, padding * 2f, 0f));
                if (worldPoint.x < bounds.min.x
                    || worldPoint.x > bounds.max.x
                    || worldPoint.y < bounds.min.y
                    || worldPoint.y > bounds.max.y)
                {
                    continue;
                }

                Vector2 center = bounds.center;
                float distance = (worldPoint - center).sqrMagnitude;
                if (distance + DistanceEpsilon < closestDistance
                    || (Mathf.Abs(distance - closestDistance) <= DistanceEpsilon
                        && (candidateIndex < 0 || region.CandidateIndex < candidateIndex)))
                {
                    candidateIndex = region.CandidateIndex;
                    closestDistance = distance;
                }
            }

            return candidateIndex >= 0;
        }
    }

    internal static class BoardEditGhostPalette
    {
        public const float Alpha = 0.5f;
        public static readonly Color BaseColor = new Color(1f, 1f, 1f, Alpha);

        public static Color PlateColor(
            GridPlacementFeedbackState overallState,
            GridPlacementFeedbackState cellState)
        {
            Color color = GridPlacementFeedbackPalette.DishColorFor(overallState, cellState);
            // Ghost 的整体透明度由 BaseColor 控制，餐盘反馈只负责 RGB，避免 Alpha 被重复相乘。
            color.a = 1f;
            return color;
        }
    }

    internal static class TableFragmentPlacementAnimationOrder
    {
        private static readonly GridPos[] Neighbors =
        {
            new GridPos(-1, 0),
            new GridPos(1, 0),
            new GridPos(0, -1),
            new GridPos(0, 1),
        };

        /// <summary>从与现有餐桌接触的碎片格开始，按碎片内部连通距离向外排序。</summary>
        public static List<GridPos> Build(
            HashSet<GridPos> existing,
            IReadOnlyList<GridPos> localCells,
            GridPos origin)
        {
            var ordered = new List<GridPos>();
            if (localCells == null || localCells.Count == 0)
            {
                return ordered;
            }

            var localSet = new HashSet<GridPos>(localCells);
            var distance = new Dictionary<GridPos, int>(localCells.Count);
            var queue = new Queue<GridPos>();
            var seeds = new List<GridPos>();
            foreach (GridPos local in localCells)
            {
                GridPos absolute = local.Offset(origin.X, origin.Y);
                if (!TouchesExisting(existing, absolute))
                {
                    continue;
                }

                distance[local] = 0;
                queue.Enqueue(local);
                seeds.Add(local);
            }

            while (queue.Count > 0)
            {
                GridPos current = queue.Dequeue();
                int nextDistance = distance[current] + 1;
                foreach (GridPos offset in Neighbors)
                {
                    GridPos next = current.Offset(offset.X, offset.Y);
                    if (!localSet.Contains(next) || distance.ContainsKey(next))
                    {
                        continue;
                    }

                    distance[next] = nextDistance;
                    queue.Enqueue(next);
                }
            }

            ordered.AddRange(localCells);
            ordered.Sort((left, right) =>
            {
                int leftDistance = ResolvedDistance(left, distance, seeds, localCells.Count);
                int rightDistance = ResolvedDistance(right, distance, seeds, localCells.Count);
                int compare = leftDistance.CompareTo(rightDistance);
                if (compare != 0)
                {
                    return compare;
                }

                compare = left.Y.CompareTo(right.Y);
                return compare != 0 ? compare : left.X.CompareTo(right.X);
            });
            return ordered;
        }

        private static int ResolvedDistance(
            GridPos cell,
            IReadOnlyDictionary<GridPos, int> distance,
            IReadOnlyList<GridPos> seeds,
            int connectedDistanceOffset)
        {
            if (distance.TryGetValue(cell, out int value))
            {
                return value;
            }

            int nearest = int.MaxValue;
            foreach (GridPos seed in seeds)
            {
                nearest = Math.Min(
                    nearest,
                    Math.Abs(cell.X - seed.X) + Math.Abs(cell.Y - seed.Y));
            }

            return nearest == int.MaxValue
                ? int.MaxValue
                : connectedDistanceOffset + nearest;
        }

        private static bool TouchesExisting(HashSet<GridPos> existing, GridPos cell)
        {
            if (existing == null || existing.Count == 0)
            {
                return false;
            }

            foreach (GridPos offset in Neighbors)
            {
                if (existing.Contains(cell.Offset(offset.X, offset.Y)))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// 餐桌编辑页（世界空间）：复用餐桌渲染，把从碎片包开出的候选形状手动拖拽拼贴到胃上。
    /// 按住底部候选开始拖拽，固定朝向，松开时合法则暂放；确认按钮才正式写入餐桌。
    /// 暂放后可再次拖动，右键取消并返回托盘；空闲时右键或跳过按钮放弃本包。
    /// 亦承载只读「餐桌视图」：复用同一套布局但不显示候选托盘、不接受拖拽输入。
    ///
    /// 从 BattleWorldController 拆出，作为其协作组件挂在同一经营挑战场景根上：
    /// 只持有自身所需的场景引用（餐桌/相机/餐桌格 prefab/食物根），不再触碰 Food 态私有成员；
    /// 世界互斥态（进入/退出）由 BattleWorldController 外壳调度，本类负责编辑/餐桌视图的表现与交互。
    /// </summary>
    public sealed class DiningTableEditController : MonoBehaviour
    {
        private const int EditTraySortingOrder = 150;
        private const int EditProjectionSortingOrder = -200;
        private const int EditDragSortingOrder = -70;
        private const float EditDragGrabDuration = 0.12f;
        private const float EditDragReturnDuration = 0.16f;
        private const float EditTableLayoutTweenDuration = 0.8f;
        private const float EditBoundsWarningWidth = 0.055f;
        private const float EditTrayGroupRotation = 5f;
        private const float EditTrayLockedShakeDuration = 0.25f;
        private const float EditTrayLockedShakeAmplitudeRatio = 0.12f;
        private const float EditStagedSweepDuration = 2.20f;
        private const float EditStagedSweepPause = 0.90f;
        private const float EditStagedSweepBandWidth = 0.42f;
        private const float EditConfirmDuration = 1.10f;
        private const float EditConfirmCellDuration = 0.50f;
        private const float EditConfirmCellStart = 0.18f;

        // 编辑页餐桌定位的底部边距：比 Food 态更大，给候选碎片托盘条让位。
        private const float EditTableBottomMargin = 3.6f;

        private static readonly Color BoundsWarningColor = new Color(1f, 0.05f, 0.02f, 0.42f);
        private static readonly Color EditFragmentFillColor = Color.white;
        private static readonly Color EditStagedFragmentFillColor = Color.white;
        private static readonly Color EditStagedBaseColor = new Color(1f, 0.92f, 0.78f, 1f);
        private static readonly Color EditStagedSweepColor = new Color(1f, 1f, 0.94f, 1f);
        private static readonly Color EditConfirmFlashColor = new Color(1f, 0.90f, 0.68f, 1f);

        private enum TableInteractionState
        {
            None,
            FragmentPlacement,
            ReadOnlyView,
            CellTargeting,
        }

        private enum FragmentChoiceInteractionState
        {
            Idle,
            DraggingFromTray,
            Staged,
            DraggingStaged,
            Returning,
            Completing,
        }

        // —— 由外壳注入的共享场景引用 ——
        private BattleWorldController _owner;
        private DiningTableView _boardView;
        private DiningTableCellView _boardCellPrefab;
        private Transform _piecesRoot;
        private Camera _camera;

        // 编辑页自适应单格尺寸与视口半宽/半高（LayoutEditorTable/ComputeViewport 计算）。
        private float _cellSize = DiningTableLayout.MaxCellSize;
        private float _halfW = DiningTableLayout.FallbackHalfW;
        private float _halfH = DiningTableLayout.FallbackHalfH;

        private TableInteractionState _state = TableInteractionState.None;
        private GameRun _editRun;
        private GpTable _editTable;
        private Action<bool> _editOnDone;
        private readonly List<TableFragmentDef> _editCandidates = new List<TableFragmentDef>();
        private readonly List<int> _editCandidateRotations = new List<int>();
        private int _editSelected = -1;
        private FragmentChoiceInteractionState _fragmentChoiceState;
        private int _choiceSessionVersion;
        private int _hoveredCandidateIndex = -1;
        private Action<TableFragmentHoverInfo> _candidateHoverEntered;
        private Action<TableFragmentHoverInfo> _candidateHoverExited;
        private Action<TableFragmentEditActionState> _editActionStateChanged;
        private TableFragmentPlacementEvaluation _currentPlacementEvaluation;
        private GridPos _stagedOrigin;
        private GridPos _stagedCenterCell;
        private bool _lastPublishedCanConfirm;
        private bool _lastPublishedInteractable;
        private bool _hasPublishedActionState;
        private Vector3 _editDragPointerWorld;
        private Tween _editTableLayoutTween;
        private Vector3 _editTableBasePosition;
        private Vector3 _editTableBaseScale = Vector3.one;
        private Vector3 _editTableLayoutTargetPosition;
        private Vector3 _editTableLayoutTargetScale = Vector3.one;
        private bool _editTableLayoutTargetInitialized;
        private bool _editTablePreviewLayoutActive;

        [Header("编辑态固定结构")]
        [SerializeField] private BoardEditTrayLayoutArea _editLayoutArea;
        [SerializeField] private Transform _editDragRootPrefab;
        [SerializeField] private LineRenderer _editBoundsWarningLinePrefab;

        private readonly List<DiningTableCellView> _editTrayCells = new List<DiningTableCellView>();
        private readonly List<BoardEditTrayHitRegion> _editTrayHitRegions = new List<BoardEditTrayHitRegion>();
        private readonly List<DiningTableCellView> _editDragCells = new List<DiningTableCellView>();
        private readonly List<Vector3> _editDragCellBaseScales = new List<Vector3>();
        private readonly List<DiningTableCellView> _editProjectionCells = new List<DiningTableCellView>();
        private readonly List<TrayCluster> _editTrayClusters = new List<TrayCluster>();
        private readonly Dictionary<int, Tween> _editTrayFailureTweens = new Dictionary<int, Tween>();
        private readonly List<Vector2Int> _editTrayFragmentSizes = new List<Vector2Int>();
        private readonly List<Vector2> _editTrayColumnCenters = new List<Vector2>();
        private readonly Dictionary<string, DiningTableCellSprites> _editMaterialCellSprites =
            new Dictionary<string, DiningTableCellSprites>();
        private DiningTableCellSprites _editCellSprites;
        private float _editTraySize;
        private Transform _editDragRoot;
        private readonly List<LineRenderer> _editBoundsWarningLines = new List<LineRenderer>();
        private Material _editBoundsWarningMaterial;
        private Vector3 _editDragReturnCenter;
        private float _editDragReturnScale = 1f;
        private int _editMaxWidth;
        private int _editMaxHeight;
        private CancellationTokenSource _editDragCts;
        private Sequence _editStagedPlacementSequence;
        private Sequence _editConfirmPlacementSequence;

        /// <summary>是否正处于可拖拽的餐桌编辑态（只读餐桌视图不算）。</summary>
        public bool IsEditing => _state == TableInteractionState.FragmentPlacement;
        public bool IsDragging =>
            _fragmentChoiceState == FragmentChoiceInteractionState.DraggingFromTray
            || _fragmentChoiceState == FragmentChoiceInteractionState.DraggingStaged
            || _fragmentChoiceState == FragmentChoiceInteractionState.Returning;

        public GpTable CurrentTable => _editTable;
        private Transform EditRoot => _editLayoutArea != null ? _editLayoutArea.transform : null;

        private struct TrayCluster
        {
            public int CandidateIndex;
            public Bounds WorldBounds;
            public List<DiningTableCellView> Cells;
            public List<Vector3> BaseLocalPositions;
        }

        private struct BoundsWarningInfo
        {
            public bool OverLeft;
            public bool OverRight;
            public bool OverTop;
            public bool OverBottom;
            public int LeftLimitX;
            public int RightLimitX;
            public int TopLimitY;
            public int BottomLimitY;
            public int MinX;
            public int MinY;
            public int MaxX;
            public int MaxY;
        }

        /// <summary>由外壳注入共享场景引用（餐桌、餐桌格 prefab、食物根、相机）。</summary>
        public void Configure(BattleWorldController owner, DiningTableView boardView, DiningTableCellView boardCellPrefab, Transform piecesRoot, Camera camera)
        {
            _owner = owner;
            _boardView = boardView;
            _boardCellPrefab = boardCellPrefab;
            _piecesRoot = piecesRoot;
            _camera = camera;
        }

        public void SetCandidateHoverCallbacks(
            Action<TableFragmentHoverInfo> entered,
            Action<TableFragmentHoverInfo> exited)
        {
            SetHoveredCandidate(-1);
            _candidateHoverEntered = entered;
            _candidateHoverExited = exited;
        }

        /// <summary>
        /// 进入统一餐桌格选择页。商店与奖励入口只负责传入不同的完成回调。
        /// 外壳已完成 Food 态清场与世界互斥态切换，这里只做编辑页的构建。
        /// </summary>
        public void BeginTableFragmentChoice(TableFragmentChoiceRequest request)
        {
            GameRun run = request?.Run;
            if (run == null)
            {
                request?.Completed?.Invoke(false);
                return;
            }

            CancelPlacementVisualAnimations(resetVisuals: true);
            SetHoveredCandidate(-1);
            _choiceSessionVersion = request.SessionVersion;
            _editRun = run;
            _editOnDone = request.Completed;
            _editActionStateChanged = request.EditActionStateChanged;
            _editSelected = -1;
            _fragmentChoiceState = FragmentChoiceInteractionState.Idle;
            _currentPlacementEvaluation = null;
            _hasPublishedActionState = false;
            ConfigureEditMaxBounds(run);

            _editCandidates.Clear();
            _editCandidateRotations.Clear();
            for (int i = 0; i < request.CandidateIds.Count; i++)
            {
                string id = request.CandidateIds[i];
                TableFragmentDef def = run.GetTableFragmentDef(id);
                if (def != null)
                {
                    int rotation = i < request.CandidateRotations.Count
                        ? request.CandidateRotations[i]
                        : 0;
                    _editCandidates.Add(def.Rotated(rotation));
                    _editCandidateRotations.Add(rotation);
                }
            }

            ResolveCamera();
            CancelDragAnimation();
            ComputeViewport();

            LoadEditCellSprites();
            _editTable = BattleSessionFactory.BuildTablePreview(run);
            LayoutEditorTable(_editTable);
            EnsureEditRoot();
            _state = TableInteractionState.FragmentPlacement;
            BuildCandidateTray();
            PublishEditActionState(canConfirm: false, interactable: true);
        }

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

        /// <summary>进入只读餐桌视图：复用编辑页餐桌布局，但不显示候选碎片托盘，也不启用拖拽输入。</summary>
        public void BeginTableView(GameRun run, GpTable tableOverride = null)
        {
            _editRun = run;
            ClearTray();
            ClearGhost();
            ClearDragVisual();
            HideBoundsWarning();
            ResolveCamera();
            CancelDragAnimation();
            ComputeViewport();

            LoadEditCellSprites();
            _editTable = tableOverride ?? run.BuildTablePreviewFromFragments();
            LayoutEditorTable(_editTable, useBoardArea: true);
            _state = TableInteractionState.ReadOnlyView;
        }

        /// <summary>进入消耗品选格态：复用餐桌查看布局，但允许外层用世界箭头选择格子。</summary>
        public void BeginCellTargeting(GameRun run, GpTable tableOverride = null)
        {
            _editRun = run;
            ClearTray();
            ClearGhost();
            ClearDragVisual();
            HideBoundsWarning();
            ResolveCamera();
            CancelDragAnimation();
            ComputeViewport();

            LoadEditCellSprites();
            _editTable = tableOverride ?? run.BuildTablePreviewFromFragments();
            LayoutEditorTable(_editTable, useBoardArea: true);
            _state = TableInteractionState.CellTargeting;
        }

        public void EndTableView()
        {
            if (_state != TableInteractionState.ReadOnlyView && _state != TableInteractionState.CellTargeting)
            {
                return;
            }

            _boardView?.ShowVoidAsPlaceholders(false);
            _editTable = null;
            _editRun = null;
            _state = TableInteractionState.None;
            _owner?.ClearTableMode();
        }

        public bool ApplyCellMaterialVisual(GridPos pos, string materialId, Action onComplete)
        {
            if (_editTable == null || string.IsNullOrEmpty(materialId) || !_editTable.AddMaterialAt(pos, materialId))
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

        /// <summary>退出菜桌编辑页：清理动态内容，恢复餐桌常规显示。外层负责隐藏世界与返回。</summary>
        public void EndTableEdit()
        {
            CancelPlacementVisualAnimations(resetVisuals: true);
            SetHoveredCandidate(-1);
            _state = TableInteractionState.None;
            _choiceSessionVersion++;
            _editSelected = -1;
            _fragmentChoiceState = FragmentChoiceInteractionState.Idle;
            _currentPlacementEvaluation = null;
            _editCandidates.Clear();
            _editCandidateRotations.Clear();
            ClearGhost();
            _boardView?.ClearTransientGridRegionOutline();
            ClearDragVisual();
            ClearTray();
            HideBoundsWarning();
            KillEditTableLayoutTween(restoreImmediately: true);
            _boardView?.ShowVoidAsPlaceholders(false);
            _editRun = null;
            _editTable = null;
            _editOnDone = null;
            PublishEditActionState(canConfirm: false, interactable: false);
            _editActionStateChanged = null;
            _owner?.ClearTableMode();
        }

        public void SkipTableEditPack()
        {
            if (!IsEditing || _fragmentChoiceState != FragmentChoiceInteractionState.Idle)
            {
                return;
            }

            SkipPack();
        }

        public void ConfirmTableEditPlacement()
        {
            if (!IsEditing
                || _fragmentChoiceState != FragmentChoiceInteractionState.Staged
                || _editSelected < 0
                || _editSelected >= _editCandidates.Count
                || _editRun == null)
            {
                return;
            }

            TableFragmentDef fragment = _editCandidates[_editSelected];
            TableFragmentPlacementEvaluation current = TableFragmentPlacementEvaluator.EvaluateAtOrigin(
                _editTable,
                fragment,
                _stagedOrigin,
                _stagedCenterCell,
                _editMaxWidth,
                _editMaxHeight);
            if (current == null || !current.CanCommit)
            {
                ReturnCandidateDrag();
                return;
            }

            BeginCommitPlacement(_stagedOrigin);
        }

        private void Update()
        {
            if (IsEditing)
            {
                UpdateTableEdit();
            }
        }

        private void OnDisable()
        {
            CancelPlacementVisualAnimations(resetVisuals: true);
            CancelDragAnimation();
        }

        private void ResolveCamera()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
            }
        }

        private void ComputeViewport()
        {
            DiningTableLayout.ResolveViewport(_camera, out _halfW, out _halfH);
        }

        private void UpdateTableEdit()
        {
            if (_editTable == null)
            {
                return;
            }

            Vector3 mouseWorld = WorldInput.MouseWorld(_camera);

            if (_fragmentChoiceState == FragmentChoiceInteractionState.Completing)
            {
                return;
            }

            if (_fragmentChoiceState == FragmentChoiceInteractionState.Returning)
            {
                SetHoveredCandidate(-1);
                ClearGhost();
                RestoreEditTableLayout();
                return;
            }

            if (WorldInput.SecondaryPressedThisFrame)
            {
                if (_fragmentChoiceState == FragmentChoiceInteractionState.Idle)
                {
                    SkipPack();
                }
                else
                {
                    ReturnCandidateDrag();
                }

                return;
            }

            if (_fragmentChoiceState == FragmentChoiceInteractionState.Idle)
            {
                UpdateCandidateHover(mouseWorld);
                if (WorldInput.PrimaryPressedThisFrame && TryPickTray(mouseWorld, out int picked))
                {
                    BeginCandidateDrag(picked, mouseWorld);
                    UpdateDragFeedback(mouseWorld);
                    return;
                }

                ClearGhost();
                HideBoundsWarning();
                RestoreEditTableLayout();
                return;
            }

            if (_fragmentChoiceState == FragmentChoiceInteractionState.Staged)
            {
                UpdateCandidateHover(mouseWorld);
                if (WorldInput.PrimaryPressedThisFrame)
                {
                    if (TryPickStagedFragment(mouseWorld))
                    {
                        BeginStagedDrag(mouseWorld);
                        UpdateDragFeedback(mouseWorld);
                        return;
                    }

                    if (TryPickTray(mouseWorld, out int lockedCandidate))
                    {
                        PlayLockedCandidateShake(lockedCandidate);
                    }
                }

                ClearGhost(restoreDragCellColors: false);
                HideBoundsWarning();
                return;
            }

            if (_fragmentChoiceState != FragmentChoiceInteractionState.DraggingFromTray
                && _fragmentChoiceState != FragmentChoiceInteractionState.DraggingStaged)
            {
                return;
            }

            _editDragPointerWorld = mouseWorld;
            UpdateDragFeedback(mouseWorld);
            if (WorldInput.PrimaryReleasedThisFrame || !WorldInput.PrimaryHeld)
            {
                if (WorldInput.PointerOverUi)
                {
                    ReturnCandidateDrag();
                    return;
                }

                TableFragmentPlacementEvaluation evaluation = _currentPlacementEvaluation;
                if (evaluation != null && evaluation.CanCommit)
                {
                    StagePlacement(evaluation);
                    return;
                }

                ReturnCandidateDrag();
            }
        }

        private void BeginCandidateDrag(int index, Vector3 mouseWorld)
        {
            if (index < 0 || index >= _editCandidates.Count)
            {
                return;
            }

            CancelPlacementVisualAnimations(resetVisuals: true);
            _editSelected = index;
            _fragmentChoiceState = FragmentChoiceInteractionState.DraggingFromTray;
            _currentPlacementEvaluation = null;
            SetHoveredCandidate(-1);
            _editDragReturnCenter = TrayCenterForCandidate(index, mouseWorld);
            _editDragReturnScale = Mathf.Clamp(_editTraySize / Mathf.Max(0.0001f, _cellSize), 0.01f, 1f);
            BuildCandidateTray();
            BuildDragVisual(_editCandidates[index], _editDragReturnCenter, _editDragReturnScale);
            _editDragPointerWorld = mouseWorld;
            StartDragAnimation(mouseWorld, Vector3.one, EditDragGrabDuration, keepFollowing: true);
            ClearGhost();
            PublishEditActionState(canConfirm: false, interactable: false);
            GameApp.Audio.PlayPickup();
        }

        private void BeginStagedDrag(Vector3 mouseWorld)
        {
            if (_fragmentChoiceState != FragmentChoiceInteractionState.Staged
                || _editDragRoot == null
                || EditRoot == null)
            {
                return;
            }

            CancelPlacementVisualAnimations(resetVisuals: true);
            _fragmentChoiceState = FragmentChoiceInteractionState.DraggingStaged;
            _currentPlacementEvaluation = null;
            SetHoveredCandidate(-1);
            _boardView?.ClearTransientGridRegionOutline();
            _editDragRoot.SetParent(EditRoot, worldPositionStays: true);
            RestoreDragCellColors();
            _editDragPointerWorld = mouseWorld;
            StartDragAnimation(mouseWorld, Vector3.one, EditDragGrabDuration, keepFollowing: true);
            ClearGhost();
            PublishEditActionState(canConfirm: true, interactable: false);
            GameApp.Audio.PlayPickup();
        }

        private void ReturnCandidateDrag()
        {
            if (_fragmentChoiceState == FragmentChoiceInteractionState.Returning
                || _fragmentChoiceState == FragmentChoiceInteractionState.Completing)
            {
                return;
            }

            CancelPlacementVisualAnimations(resetVisuals: true);
            _fragmentChoiceState = FragmentChoiceInteractionState.Returning;
            _currentPlacementEvaluation = null;
            _boardView?.ClearTransientGridRegionOutline();
            if (_editDragRoot != null && EditRoot != null && _editDragRoot.parent != EditRoot)
            {
                _editDragRoot.SetParent(EditRoot, worldPositionStays: true);
            }

            BuildCandidateTray();
            ClearGhost();
            HideBoundsWarning();
            RestoreEditTableLayout();
            PublishEditActionState(canConfirm: false, interactable: false);

            if (_editDragRoot == null)
            {
                FinishReturnDrag();
                return;
            }

            StartDragAnimation(_editDragReturnCenter, Vector3.one * _editDragReturnScale, EditDragReturnDuration, keepFollowing: false, FinishReturnDrag);
        }

        private void UpdateDragFeedback(Vector3 mouseWorld)
        {
            if ((_fragmentChoiceState != FragmentChoiceInteractionState.DraggingFromTray
                 && _fragmentChoiceState != FragmentChoiceInteractionState.DraggingStaged)
                || _editSelected < 0
                || _editSelected >= _editCandidates.Count
                || _boardView?.Mapper == null)
            {
                ClearGhost();
                return;
            }

            TableFragmentDef def = _editCandidates[_editSelected];
            _currentPlacementEvaluation = TableFragmentPlacementEvaluator.Evaluate(
                _editTable,
                _boardView.Mapper,
                def,
                mouseWorld,
                _editMaxWidth,
                _editMaxHeight);
            if (_currentPlacementEvaluation == null)
            {
                ClearGhost();
                HideBoundsWarning();
                HoldOrRestoreEditTableLayout();
                return;
            }

            if (_editDragRoot != null)
            {
                _editDragRoot.position = mouseWorld;
            }
            _editDragPointerWorld = mouseWorld;

            ShowGhost(_currentPlacementEvaluation);
            ShowProjection(def, _currentPlacementEvaluation);
            UpdateProjectedTableLayout(_currentPlacementEvaluation);
            if (TryGetBoundsWarning(_currentPlacementEvaluation.Origin, def, out BoundsWarningInfo warning))
            {
                ShowBoundsWarning(warning);
            }
            else
            {
                HideBoundsWarning();
            }
        }

        private void StagePlacement(TableFragmentPlacementEvaluation evaluation)
        {
            if (evaluation == null
                || !evaluation.CanCommit
                || _editSelected < 0
                || _editSelected >= _editCandidates.Count
                || _boardView?.Mapper == null
                || _editDragRoot == null)
            {
                return;
            }

            CancelDragAnimation();
            _fragmentChoiceState = FragmentChoiceInteractionState.Staged;
            _stagedOrigin = evaluation.Origin;
            _stagedCenterCell = evaluation.CenterCell;
            _currentPlacementEvaluation = null;
            SetHoveredCandidate(-1);
            ClearGhost();
            HideBoundsWarning();
            UpdateProjectedTableLayout(evaluation);

            TableFragmentDef fragment = _editCandidates[_editSelected];
            Vector3 localCenter = Vector3.zero;
            List<GridPos> localCells = TableFragmentBuilder.FilledCells(fragment);
            foreach (GridPos local in localCells)
            {
                GridPos absolute = local.Offset(_stagedOrigin.X, _stagedOrigin.Y);
                localCenter += _boardView.Mapper.CellCenterLocal(absolute);
            }

            if (localCells.Count == 0)
            {
                ReturnCandidateDrag();
                return;
            }

            localCenter /= localCells.Count;
            _editDragRoot.SetParent(_boardView.transform, worldPositionStays: false);
            _editDragRoot.localPosition = localCenter;
            _editDragRoot.localRotation = Quaternion.identity;
            _editDragRoot.localScale = Vector3.one;
            SetDragCellColor(EditStagedFragmentFillColor);
            StartStagedPlacementAnimation();
            GameApp.Audio.PlayPlacement();
            PublishEditActionState(canConfirm: true, interactable: true);
        }

        private void BeginCommitPlacement(GridPos origin)
        {
            if (_fragmentChoiceState != FragmentChoiceInteractionState.Staged
                || _editSelected < 0
                || _editSelected >= _editCandidates.Count
                || _editRun == null)
            {
                return;
            }

            CancelPlacementVisualAnimations(resetVisuals: true);
            _fragmentChoiceState = FragmentChoiceInteractionState.Completing;
            PublishEditActionState(canConfirm: true, interactable: false);
            _currentPlacementEvaluation = null;
            _boardView?.ClearTransientGridRegionOutline();
            HideBoundsWarning();
            StartConfirmPlacementAnimation(origin, _choiceSessionVersion);
        }

        private void FinalizeCommitPlacement(GridPos origin, int sessionVersion)
        {
            if (_fragmentChoiceState != FragmentChoiceInteractionState.Completing
                || sessionVersion != _choiceSessionVersion
                || _editSelected < 0
                || _editSelected >= _editCandidates.Count
                || _editRun == null)
            {
                return;
            }

            string fragmentId = _editCandidates[_editSelected].Id;
            int rotation = _editSelected < _editCandidateRotations.Count
                ? _editCandidateRotations[_editSelected]
                : 0;
            if (!_editRun.AddFragmentPlacement(fragmentId, rotation, origin))
            {
                _fragmentChoiceState = FragmentChoiceInteractionState.Staged;
                ReturnCandidateDrag();
                return;
            }

            _editRun.ClearPendingFragmentPack();

            Action<bool> done = _editOnDone;
            EndTableEdit();
            done?.Invoke(true);
        }

        public static bool ExtendsAboveExistingTop(GpTable table, TableFragmentDef fragment, GridPos origin)
        {
            return TableFragmentPlacementEvaluator.ExtendsAboveExistingTop(table, fragment, origin);
        }

        private void SkipPack()
        {
            if (_fragmentChoiceState != FragmentChoiceInteractionState.Idle)
            {
                return;
            }

            CancelPlacementVisualAnimations(resetVisuals: true);
            _fragmentChoiceState = FragmentChoiceInteractionState.Completing;
            PublishEditActionState(canConfirm: false, interactable: false);
            _editRun?.ClearPendingFragmentPack();
            Action<bool> done = _editOnDone;
            EndTableEdit();
            done?.Invoke(false);
        }

        private void LayoutEditorTable(GpTable board, bool useBoardArea = false)
        {
            KillEditTableLayoutTween(restoreImmediately: false);
            // 编辑页按当前实际胃形居中；最大包围盒只参与逻辑限制，不作为背景网格铺出来。
            // 只读查看态复用 Food 态的 HUD 空区，编辑态仍保留底部托盘让位。
            BoardPlacement placement = useBoardArea && _owner != null && _owner.TryComputeTableAreaPlacement(board, out BoardPlacement boardAreaPlacement)
                ? boardAreaPlacement
                : DiningTableLayout.Compute(_halfW, _halfH, board, EditTableBottomMargin);
            _cellSize = placement.CellSize;
            _boardView.transform.rotation = Quaternion.identity;
            _boardView.transform.localScale = Vector3.one;
            _boardView.transform.position = placement.Position;
            _editTableBasePosition = placement.Position;
            _editTableBaseScale = Vector3.one;
            _editTableLayoutTargetPosition = _editTableBasePosition;
            _editTableLayoutTargetScale = _editTableBaseScale;
            _editTableLayoutTargetInitialized = true;

            if (_piecesRoot != null && _piecesRoot.parent != _boardView.transform)
            {
                _piecesRoot.SetParent(_boardView.transform, worldPositionStays: false);
                _piecesRoot.localPosition = Vector3.zero;
                _piecesRoot.localRotation = Quaternion.identity;
                _piecesRoot.localScale = Vector3.one;
            }

            _boardView.Build(board, _cellSize, DiningTableLayout.Gap, null, _boardCellPrefab);
            _boardView.ClearDragPlacementFeedback();
            _boardView.ShowVoidAsPlaceholders(false);
        }

        private void UpdateProjectedTableLayout(TableFragmentPlacementEvaluation evaluation)
        {
            if (evaluation == null
                || !evaluation.CanCommit
                || !evaluation.ExpandsExistingBounds
                || _editTable == null
                || _boardView == null)
            {
                HoldOrRestoreEditTableLayout();
                return;
            }

            BoardPlacement placement = DiningTableLayout.ComputeForBounds(
                _halfW,
                _halfH,
                _editTable.Width,
                _editTable.Height,
                evaluation.ProjectedBounds,
                EditTableBottomMargin);
            float scale = placement.CellSize / Mathf.Max(0.0001f, _cellSize);
            _editTablePreviewLayoutActive = true;
            TweenEditTableLayout(placement.Position, _editTableBaseScale * scale);
        }

        private void RestoreEditTableLayout()
        {
            _editTablePreviewLayoutActive = false;
            if (_boardView == null || !_editTableLayoutTargetInitialized)
            {
                return;
            }

            TweenEditTableLayout(_editTableBasePosition, _editTableBaseScale);
        }

        private void HoldOrRestoreEditTableLayout()
        {
            // 一旦本次拖拽触发过扩 Bounds 预布局，就保持到拖拽结束。
            // 这样鼠标继续按当前布局映射，移开不会因自动回弹再次改变落点。
            if ((_fragmentChoiceState == FragmentChoiceInteractionState.DraggingFromTray
                 || _fragmentChoiceState == FragmentChoiceInteractionState.DraggingStaged)
                && _editTablePreviewLayoutActive)
            {
                return;
            }

            RestoreEditTableLayout();
        }

        private void TweenEditTableLayout(Vector3 targetPosition, Vector3 targetScale)
        {
            if (_boardView == null)
            {
                return;
            }

            if (_editTableLayoutTargetInitialized
                && Vector3.SqrMagnitude(_editTableLayoutTargetPosition - targetPosition) < 0.000001f
                && Vector3.SqrMagnitude(_editTableLayoutTargetScale - targetScale) < 0.000001f)
            {
                return;
            }

            _editTableLayoutTargetInitialized = true;
            _editTableLayoutTargetPosition = targetPosition;
            _editTableLayoutTargetScale = targetScale;
            _editTableLayoutTween?.Kill(false);

            _editTableLayoutTween = DOTween.Sequence()
                .Join(_boardView.transform.DOMove(targetPosition, EditTableLayoutTweenDuration))
                .Join(_boardView.transform.DOScale(targetScale, EditTableLayoutTweenDuration))
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .SetLink(_boardView.gameObject)
                .OnComplete(() =>
                {
                    _editTableLayoutTween = null;
                });
        }

        private void KillEditTableLayoutTween(bool restoreImmediately)
        {
            _editTableLayoutTween?.Kill(false);
            _editTableLayoutTween = null;
            if (restoreImmediately && _boardView != null && _editTableLayoutTargetInitialized)
            {
                _boardView.transform.position = _editTableBasePosition;
                _boardView.transform.localScale = _editTableBaseScale;
            }

            _editTableLayoutTargetInitialized = false;
            _editTablePreviewLayoutActive = false;
        }

        private void EnsureEditRoot()
        {
            if (_editLayoutArea == null)
            {
                Transform root = transform.Find("BoardEditRoot");
                _editLayoutArea = root != null ? root.GetComponent<BoardEditTrayLayoutArea>() : null;
            }

            if (_editLayoutArea == null)
            {
                Debug.LogError($"{nameof(DiningTableEditController)} 缺少带 {nameof(BoardEditTrayLayoutArea)} 的 BoardEditRoot 预置节点。", this);
            }
        }

        private void ConfigureEditMaxBounds(GameRun run)
        {
            cfg.Character character = run.Tables.TbCharacter.GetOrDefault(run.CharacterId);
            _editMaxWidth = character.MaxDiningTableWidth;
            _editMaxHeight = character.MaxDiningTableHeight;
        }

        private void ShowBoundsWarning(BoundsWarningInfo warning)
        {
            if (_boardView == null || _boardView.Mapper == null)
            {
                HideBoundsWarning();
                return;
            }

            if (!warning.OverLeft && !warning.OverRight && !warning.OverTop && !warning.OverBottom)
            {
                HideBoundsWarning();
                return;
            }

            DiningTableCoordinateMapper mapper = _boardView.Mapper;
            float half = _cellSize * 0.5f;
            Vector3 spanMin = mapper.CellCenterLocal(new GridPos(warning.MinX, warning.MinY));
            Vector3 spanMax = mapper.CellCenterLocal(new GridPos(warning.MaxX, warning.MaxY));
            float spanLeft = spanMin.x - half;
            float spanTop = spanMin.y + half;
            float spanRight = spanMax.x + half;
            float spanBottom = spanMax.y - half;

            int lineIndex = 0;
            if (warning.OverLeft)
            {
                float x = mapper.CellCenterLocal(new GridPos(warning.LeftLimitX, warning.MinY)).x - half;
                SetBoundsWarningSegment(lineIndex++, mapper, new Vector3(x, spanTop, 0f), new Vector3(x, spanBottom, 0f));
            }

            if (warning.OverRight)
            {
                float x = mapper.CellCenterLocal(new GridPos(warning.RightLimitX, warning.MinY)).x + half;
                SetBoundsWarningSegment(lineIndex++, mapper, new Vector3(x, spanTop, 0f), new Vector3(x, spanBottom, 0f));
            }

            if (warning.OverTop)
            {
                float y = mapper.CellCenterLocal(new GridPos(warning.MinX, warning.TopLimitY)).y + half;
                SetBoundsWarningSegment(lineIndex++, mapper, new Vector3(spanLeft, y, 0f), new Vector3(spanRight, y, 0f));
            }

            if (warning.OverBottom)
            {
                float y = mapper.CellCenterLocal(new GridPos(warning.MinX, warning.BottomLimitY)).y - half;
                SetBoundsWarningSegment(lineIndex++, mapper, new Vector3(spanLeft, y, 0f), new Vector3(spanRight, y, 0f));
            }

            for (int i = lineIndex; i < _editBoundsWarningLines.Count; i++)
            {
                _editBoundsWarningLines[i].gameObject.SetActive(false);
            }
        }

        private void HideBoundsWarning()
        {
            foreach (LineRenderer line in _editBoundsWarningLines)
            {
                if (line != null)
                {
                    line.gameObject.SetActive(false);
                }
            }
        }

        private void SetBoundsWarningSegment(int index, DiningTableCoordinateMapper mapper, Vector3 localStart, Vector3 localEnd)
        {
            LineRenderer line = EnsureBoundsWarningLine(index);
            if (line == null)
            {
                return;
            }

            line.gameObject.SetActive(true);
            line.positionCount = 2;
            line.SetPosition(0, ToWarningWorld(mapper, localStart));
            line.SetPosition(1, ToWarningWorld(mapper, localEnd));
        }

        private Vector3 ToWarningWorld(DiningTableCoordinateMapper mapper, Vector3 local)
        {
            return mapper.Root != null ? mapper.Root.TransformPoint(local) : local;
        }

        private LineRenderer EnsureBoundsWarningLine(int index)
        {
            if (index < 0)
            {
                return null;
            }

            EnsureEditRoot();
            while (_editBoundsWarningLines.Count <= index)
            {
                if (EditRoot == null || _editBoundsWarningLinePrefab == null)
                {
                    Debug.LogError($"{nameof(DiningTableEditController)} 缺少 BoundsWarningLine prefab。", this);
                    return null;
                }

                LineRenderer line = Instantiate(_editBoundsWarningLinePrefab, EditRoot);
                line.gameObject.name = $"BoundsWarning{_editBoundsWarningLines.Count}";
                line.useWorldSpace = true;
                line.loop = false;
                line.startWidth = EditBoundsWarningWidth;
                line.endWidth = EditBoundsWarningWidth;
                line.startColor = BoundsWarningColor;
                line.endColor = BoundsWarningColor;
                line.sharedMaterial = BoundsWarningMaterial();
                BattleSorting.Apply(line, BattleSorting.Fx, BattleSorting.OrderFloatingText);
                line.gameObject.SetActive(false);
                _editBoundsWarningLines.Add(line);
            }

            return _editBoundsWarningLines[index];
        }

        private bool TryGetBoundsWarning(GridPos origin, TableFragmentDef def, out BoundsWarningInfo warning)
        {
            warning = default;
            if (def == null || _editTable == null || !_editTable.TryGetExistingBounds(out int minX, out int minY, out int maxX, out int maxY))
            {
                return false;
            }

            int candidateMinX = int.MaxValue;
            int candidateMinY = int.MaxValue;
            int candidateMaxX = int.MinValue;
            int candidateMaxY = int.MinValue;
            bool touchesExisting = false;
            foreach (GridPos local in TableFragmentBuilder.FilledCells(def))
            {
                GridPos pos = local.Offset(origin.X, origin.Y);
                if (_editTable.Exists(pos))
                {
                    return false;
                }

                touchesExisting |=
                    _editTable.Exists(pos.Offset(1, 0)) ||
                    _editTable.Exists(pos.Offset(-1, 0)) ||
                    _editTable.Exists(pos.Offset(0, 1)) ||
                    _editTable.Exists(pos.Offset(0, -1));

                if (pos.X < candidateMinX) candidateMinX = pos.X;
                if (pos.Y < candidateMinY) candidateMinY = pos.Y;
                if (pos.X > candidateMaxX) candidateMaxX = pos.X;
                if (pos.Y > candidateMaxY) candidateMaxY = pos.Y;
            }

            if (!touchesExisting || candidateMaxX < candidateMinX || candidateMaxY < candidateMinY)
            {
                return false;
            }

            int mergedMinX = Mathf.Min(minX, candidateMinX);
            int mergedMinY = Mathf.Min(minY, candidateMinY);
            int mergedMaxX = Mathf.Max(maxX, candidateMaxX);
            int mergedMaxY = Mathf.Max(maxY, candidateMaxY);
            bool exceedsWidth = mergedMaxX - mergedMinX + 1 > _editMaxWidth;
            bool exceedsHeight = mergedMaxY - mergedMinY + 1 > _editMaxHeight;
            if (!exceedsWidth && !exceedsHeight)
            {
                return false;
            }

            warning.MinX = mergedMinX;
            warning.MinY = mergedMinY;
            warning.MaxX = mergedMaxX;
            warning.MaxY = mergedMaxY;
            warning.LeftLimitX = maxX - _editMaxWidth + 1;
            warning.RightLimitX = minX + _editMaxWidth - 1;
            warning.TopLimitY = maxY - _editMaxHeight + 1;
            warning.BottomLimitY = minY + _editMaxHeight - 1;
            if (exceedsWidth)
            {
                warning.OverLeft = candidateMinX < minX;
                warning.OverRight = candidateMaxX > maxX;
            }

            if (exceedsHeight)
            {
                warning.OverTop = candidateMinY < minY;
                warning.OverBottom = candidateMaxY > maxY;
            }

            return warning.OverLeft || warning.OverRight || warning.OverTop || warning.OverBottom;
        }

        private Material BoundsWarningMaterial()
        {
            if (_editBoundsWarningMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader != null)
                {
                    _editBoundsWarningMaterial = new Material(shader)
                    {
                        name = "RuntimeTableEditBoundsWarning",
                    };
                }
            }

            return _editBoundsWarningMaterial;
        }

        private void BuildCandidateTray()
        {
            ClearTray();
            EnsureEditRoot();

            int n = _editCandidates.Count;
            if (n == 0 || _editLayoutArea == null || EditRoot == null)
            {
                return;
            }

            _editTrayFragmentSizes.Clear();
            var fragmentCells = new List<List<GridPos>>(n);
            var fragmentBounds = new List<RectInt>(n);
            for (int i = 0; i < n; i++)
            {
                List<GridPos> cells = TableFragmentBuilder.FilledCells(_editCandidates[i]);
                fragmentCells.Add(cells);
                if (TryGetCellBounds(cells, out RectInt bounds))
                {
                    fragmentBounds.Add(bounds);
                    _editTrayFragmentSizes.Add(bounds.size);
                }
                else
                {
                    fragmentBounds.Add(new RectInt(0, 0, 1, 1));
                    _editTrayFragmentSizes.Add(Vector2Int.one);
                }
            }

            if (!_editLayoutArea.TryCalculate(_editTrayFragmentSizes, _editTrayColumnCenters, out _editTraySize))
            {
                Debug.LogError($"{nameof(DiningTableEditController)} 的 BoardEditRoot 布局区域尺寸无效。", _editLayoutArea);
                return;
            }

            for (int i = 0; i < n; i++)
            {
                TableFragmentDef shown = _editCandidates[i];
                List<GridPos> cells = fragmentCells[i];
                if (cells.Count == 0)
                {
                    continue;
                }

                if (_fragmentChoiceState != FragmentChoiceInteractionState.Idle && i == _editSelected)
                {
                    continue;
                }

                RectInt bounds = fragmentBounds[i];
                Vector2 columnCenter = _editTrayColumnCenters[i];
                float centerGridX = (bounds.xMin + bounds.xMax - 1) * 0.5f;
                float centerGridY = (bounds.yMin + bounds.yMax - 1) * 0.5f;
                Quaternion groupRotation = TrayRotationForCandidate(i);
                bool hasWorldBounds = false;
                Bounds worldBounds = default;
                var clusterCells = new List<DiningTableCellView>(cells.Count);
                var baseLocalPositions = new List<Vector3>(cells.Count);
                foreach (GridPos c in cells)
                {
                    DiningTableCellView cell = InstantiateTrayCell();
                    if (cell == null)
                    {
                        continue;
                    }

                    var offset = new Vector3(
                        (c.X - centerGridX) * _editTraySize,
                        -(c.Y - centerGridY) * _editTraySize,
                        0f);
                    Vector3 local = new Vector3(columnCenter.x, columnCenter.y, 0f) + groupRotation * offset;
                    cell.Configure(c, local, _editTraySize, FragmentCellSprites(shown, c), null);
                    cell.transform.localRotation = groupRotation;
                    cell.SetColor(EditFragmentFillColor);
                    cell.SetSortingOrder(EditTraySortingOrder);
                    _editTrayCells.Add(cell);
                    clusterCells.Add(cell);
                    baseLocalPositions.Add(cell.transform.localPosition);
                    _editTrayHitRegions.Add(new BoardEditTrayHitRegion(i, cell.WorldBounds));

                    if (hasWorldBounds)
                    {
                        worldBounds.Encapsulate(cell.WorldBounds);
                    }
                    else
                    {
                        worldBounds = cell.WorldBounds;
                        hasWorldBounds = true;
                    }
                }

                if (hasWorldBounds)
                {
                    _editTrayClusters.Add(new TrayCluster
                    {
                        CandidateIndex = i,
                        WorldBounds = worldBounds,
                        Cells = clusterCells,
                        BaseLocalPositions = baseLocalPositions,
                    });
                }
            }
        }

        private static bool TryGetCellBounds(IReadOnlyList<GridPos> cells, out RectInt bounds)
        {
            bounds = default;
            if (cells == null || cells.Count == 0)
            {
                return false;
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

            bounds = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return true;
        }

        private Vector3 TrayCenterForCandidate(int candidateIndex, Vector3 fallback)
        {
            foreach (TrayCluster cluster in _editTrayClusters)
            {
                if (cluster.CandidateIndex == candidateIndex)
                {
                    return cluster.WorldBounds.center;
                }
            }

            return fallback;
        }

        private void BuildDragVisual(TableFragmentDef def, Vector3 center, float scale)
        {
            BuildDragVisual(def, center, Vector3.one * Mathf.Max(0.0001f, scale));
        }

        private void BuildDragVisual(TableFragmentDef def, Vector3 center, Vector3 scale)
        {
            ClearDragVisual();
            EnsureEditRoot();
            if (EditRoot == null || _editDragRootPrefab == null)
            {
                Debug.LogError($"{nameof(DiningTableEditController)} 缺少 BoardEditDrag prefab。", this);
                return;
            }

            _editDragRoot = Instantiate(_editDragRootPrefab, EditRoot);
            _editDragRoot.gameObject.name = "BoardEditDrag";
            _editDragRoot.position = center;
            _editDragRoot.localRotation = Quaternion.identity;
            _editDragRoot.localScale = scale;

            List<GridPos> cells = TableFragmentBuilder.FilledCells(def);
            if (cells.Count == 0)
            {
                return;
            }

            float sumX = 0f;
            float sumY = 0f;
            foreach (GridPos cell in cells)
            {
                sumX += cell.X;
                sumY += cell.Y;
            }

            float inv = 1f / cells.Count;
            float avgX = sumX * inv;
            float avgY = sumY * inv;
            float pitch = _cellSize + DiningTableLayout.Gap;
            foreach (GridPos cellPos in cells)
            {
                DiningTableCellView cell = InstantiateDragCell();
                if (cell == null)
                {
                    continue;
                }

                var local = new Vector3((cellPos.X - avgX) * pitch, -(cellPos.Y - avgY) * pitch, 0f);
                cell.Configure(cellPos, local, _cellSize, FragmentCellSprites(def, cellPos), null);
                cell.SetColor(EditFragmentFillColor);
                cell.SetSorting(BattleSorting.Fx, EditDragSortingOrder);
                _editDragCells.Add(cell);
                _editDragCellBaseScales.Add(cell.transform.localScale);
            }
        }

        private DiningTableCellSprites FragmentCellSprites(TableFragmentDef def, GridPos localPos)
        {
            string materialId = FragmentCellMaterialId(def, localPos);
            if (string.IsNullOrEmpty(materialId))
            {
                return _editCellSprites;
            }

            if (_editMaterialCellSprites.TryGetValue(materialId, out DiningTableCellSprites cached))
            {
                return cached;
            }

            DiningTableCellSprites sprites =
                DiningTableCellSpriteResources.LoadMaterial(materialId, _editCellSprites);
            _editMaterialCellSprites[materialId] = sprites;
            return sprites;
        }

        private static string FragmentCellMaterialId(TableFragmentDef def, GridPos localPos)
        {
            if (def == null || def.CellMaterials == null)
            {
                return null;
            }

            for (int i = def.CellMaterials.Count - 1; i >= 0; i--)
            {
                CellMaterial material = def.CellMaterials[i];
                if (material.Pos.Equals(localPos))
                {
                    return material.MaterialId;
                }
            }

            return null;
        }

        private void LoadEditCellSprites()
        {
            _editCellSprites = DiningTableCellSpriteResources.LoadDefault();
            _editMaterialCellSprites.Clear();
            if (!_editCellSprites.IsValid)
            {
                throw new InvalidOperationException("编辑态默认餐桌格 Sprite 缺失。");
            }
        }

        private void RestoreDragCellColors()
        {
            SetDragCellColor(
                _fragmentChoiceState == FragmentChoiceInteractionState.Staged
                    ? EditStagedFragmentFillColor
                    : BoardEditGhostPalette.BaseColor);
        }

        private void SetDragCellColor(Color color)
        {
            foreach (DiningTableCellView cell in _editDragCells)
            {
                if (cell != null)
                {
                    cell.ClearPlateFeedbackColor();
                    cell.SetColor(color);
                    cell.SetSorting(BattleSorting.Fx, EditDragSortingOrder);
                }
            }
        }

        private void StartStagedPlacementAnimation()
        {
            CancelPlacementVisualAnimations(resetVisuals: true);
            if (_editDragRoot == null
                || _fragmentChoiceState != FragmentChoiceInteractionState.Staged
                || _editSelected < 0
                || _editSelected >= _editCandidates.Count)
            {
                return;
            }

            Transform animatedRoot = _editDragRoot;
            animatedRoot.localScale = Vector3.one;
            SetDragCellColor(EditStagedBaseColor);

            TableFragmentDef fragment = _editCandidates[_editSelected];
            List<GridPos> localCells = TableFragmentBuilder.FilledCells(fragment);
            var cellByPosition = new Dictionary<GridPos, DiningTableCellView>(_editDragCells.Count);
            foreach (DiningTableCellView cell in _editDragCells)
            {
                if (cell != null)
                {
                    cellByPosition[cell.Position] = cell;
                }
            }

            int minDiagonal = int.MaxValue;
            int maxDiagonal = int.MinValue;
            foreach (GridPos local in localCells)
            {
                int diagonal = local.X + local.Y;
                minDiagonal = Math.Min(minDiagonal, diagonal);
                maxDiagonal = Math.Max(maxDiagonal, diagonal);
            }

            var sweepCells = new List<KeyValuePair<DiningTableCellView, float>>(localCells.Count);
            foreach (GridPos local in localCells)
            {
                if (!cellByPosition.TryGetValue(local, out DiningTableCellView cell) || cell == null)
                {
                    continue;
                }

                float position = minDiagonal == maxDiagonal
                    ? 0.5f
                    : Mathf.InverseLerp(minDiagonal, maxDiagonal, local.X + local.Y);
                sweepCells.Add(new KeyValuePair<DiningTableCellView, float>(cell, position));
            }

            if (sweepCells.Count == 0)
            {
                return;
            }

            Sequence sweep = DOTween.Sequence()
                .Pause()
                .SetUpdate(true)
                .SetLink(animatedRoot.gameObject)
                .Append(DOVirtual.Float(
                        -EditStagedSweepBandWidth,
                        1f + EditStagedSweepBandWidth,
                        EditStagedSweepDuration,
                        progress => ApplyStagedSweepColors(sweepCells, progress))
                    .SetEase(Ease.InOutSine))
                .AppendCallback(() => SetDragCellColor(EditStagedBaseColor))
                .AppendInterval(EditStagedSweepPause)
                .SetLoops(-1, LoopType.Restart);
            _editStagedPlacementSequence = sweep;
            sweep.Play();
        }

        private static void ApplyStagedSweepColors(
            IReadOnlyList<KeyValuePair<DiningTableCellView, float>> sweepCells,
            float progress)
        {
            foreach (KeyValuePair<DiningTableCellView, float> entry in sweepCells)
            {
                DiningTableCellView cell = entry.Key;
                if (cell == null)
                {
                    continue;
                }

                float distance = Mathf.Abs(entry.Value - progress);
                float weight = 1f - Mathf.Clamp01(distance / EditStagedSweepBandWidth);
                weight = weight * weight * (3f - 2f * weight);
                cell.SetColor(Color.Lerp(EditStagedBaseColor, EditStagedSweepColor, weight));
            }
        }

        private void StartConfirmPlacementAnimation(GridPos origin, int sessionVersion)
        {
            if (_editDragRoot == null
                || _editSelected < 0
                || _editSelected >= _editCandidates.Count)
            {
                FinalizeCommitPlacement(origin, sessionVersion);
                return;
            }

            Transform animatedRoot = _editDragRoot;
            animatedRoot.localScale = Vector3.one;
            SetDragCellColor(EditStagedFragmentFillColor);

            TableFragmentDef fragment = _editCandidates[_editSelected];
            List<GridPos> localCells = TableFragmentBuilder.FilledCells(fragment);
            List<GridPos> animationOrder = TableFragmentPlacementAnimationOrder.Build(
                TableFragmentBuilder.ToExistingSet(_editTable),
                localCells,
                origin);
            var cellByPosition = new Dictionary<GridPos, int>(_editDragCells.Count);
            for (int i = 0; i < _editDragCells.Count; i++)
            {
                DiningTableCellView cell = _editDragCells[i];
                if (cell != null)
                {
                    cellByPosition[cell.Position] = i;
                }
            }

            float availableStagger = Mathf.Max(
                0f,
                EditConfirmDuration - EditConfirmCellStart - EditConfirmCellDuration);
            float stagger = animationOrder.Count <= 1
                ? 0f
                : Mathf.Min(0.08f, availableStagger / (animationOrder.Count - 1));

            Sequence confirm = DOTween.Sequence()
                .Pause()
                .SetUpdate(true)
                .SetLink(animatedRoot.gameObject);
            confirm.Insert(0f, animatedRoot
                .DOScale(Vector3.one * 0.96f, 0.16f)
                .SetEase(Ease.InQuad));
            confirm.Insert(0.16f, animatedRoot
                .DOScale(Vector3.one * 1.04f, 0.28f)
                .SetEase(Ease.OutBack));
            confirm.Insert(0.44f, animatedRoot
                .DOScale(Vector3.one, 0.28f)
                .SetEase(Ease.OutSine));

            for (int orderIndex = 0; orderIndex < animationOrder.Count; orderIndex++)
            {
                if (!cellByPosition.TryGetValue(animationOrder[orderIndex], out int cellIndex)
                    || cellIndex < 0
                    || cellIndex >= _editDragCells.Count)
                {
                    continue;
                }

                DiningTableCellView cell = _editDragCells[cellIndex];
                if (cell == null)
                {
                    continue;
                }

                Vector3 baseScale = cellIndex < _editDragCellBaseScales.Count
                    ? _editDragCellBaseScales[cellIndex]
                    : cell.transform.localScale;
                float start = EditConfirmCellStart + orderIndex * stagger;
                Sequence cellSequence = DOTween.Sequence()
                    .Append(cell.transform
                        .DOScale(baseScale * 0.92f, 0.10f)
                        .SetEase(Ease.InQuad))
                    .Append(cell.transform
                        .DOScale(baseScale * 1.08f, 0.16f)
                        .SetEase(Ease.OutBack))
                    .Join(DOVirtual.Color(
                        EditStagedFragmentFillColor,
                        EditConfirmFlashColor,
                        0.16f,
                        cell.SetColor))
                    .Append(cell.transform
                        .DOScale(baseScale, 0.24f)
                        .SetEase(Ease.OutSine))
                    .Join(DOVirtual.Color(
                        EditConfirmFlashColor,
                        EditStagedFragmentFillColor,
                        0.24f,
                        cell.SetColor));
                confirm.Insert(start, cellSequence);
            }

            // 即使是单格碎片也保持统一的 1.1 秒确认节奏。
            confirm.InsertCallback(EditConfirmDuration, () => { });
            confirm.OnComplete(() =>
            {
                if (_editConfirmPlacementSequence == confirm)
                {
                    _editConfirmPlacementSequence = null;
                }

                ResetPlacementVisuals();
                FinalizeCommitPlacement(origin, sessionVersion);
            });
            _editConfirmPlacementSequence = confirm;
            confirm.Play();
        }

        private void CancelPlacementVisualAnimations(bool resetVisuals)
        {
            Sequence staged = _editStagedPlacementSequence;
            _editStagedPlacementSequence = null;
            staged?.Kill(false);

            Sequence confirm = _editConfirmPlacementSequence;
            _editConfirmPlacementSequence = null;
            confirm?.Kill(false);

            if (resetVisuals)
            {
                ResetPlacementVisuals();
            }
        }

        private void ResetPlacementVisuals()
        {
            if (_editDragRoot != null)
            {
                _editDragRoot.localScale = Vector3.one;
            }

            for (int i = 0; i < _editDragCells.Count; i++)
            {
                DiningTableCellView cell = _editDragCells[i];
                if (cell == null)
                {
                    continue;
                }

                if (i < _editDragCellBaseScales.Count)
                {
                    cell.transform.localScale = _editDragCellBaseScales[i];
                }

                cell.ClearPlateFeedbackColor();
                cell.SetColor(EditStagedFragmentFillColor);
                cell.SetSorting(BattleSorting.Fx, EditDragSortingOrder);
            }
        }

        private void ShowGhost(TableFragmentPlacementEvaluation evaluation)
        {
            if (evaluation?.Feedback == null)
            {
                ClearGhost();
                return;
            }

            var feedbackByPosition = new Dictionary<GridPos, GridPlacementFeedbackState>();
            foreach (GridPlacementFeedbackCell cell in evaluation.Feedback.Cells)
            {
                feedbackByPosition[cell.Position] = cell.State;
            }

            foreach (DiningTableCellView ghostCell in _editDragCells)
            {
                if (ghostCell == null)
                {
                    continue;
                }

                GridPos absolute = ghostCell.Position.Offset(evaluation.Origin.X, evaluation.Origin.Y);
                GridPlacementFeedbackState cellState = feedbackByPosition.TryGetValue(absolute, out GridPlacementFeedbackState state)
                    ? state
                    : GridPlacementFeedbackState.Blocked;
                ghostCell.SetColor(BoardEditGhostPalette.BaseColor);
                ghostCell.SetPlateFeedbackColor(BoardEditGhostPalette.PlateColor(
                    evaluation.Feedback.OverallState,
                    cellState));
                ghostCell.SetSorting(BattleSorting.Fx, EditDragSortingOrder);
            }
        }

        private void ShowProjection(
            TableFragmentDef fragment,
            TableFragmentPlacementEvaluation evaluation)
        {
            if (fragment == null || evaluation == null || _boardView?.Mapper == null)
            {
                HideProjection();
                return;
            }

            List<GridPos> localCells = TableFragmentBuilder.FilledCells(fragment);
            int index = 0;
            foreach (GridPos local in localCells)
            {
                DiningTableCellView projection = EnsureProjectionCell(index++);
                if (projection == null)
                {
                    continue;
                }

                GridPos absolute = local.Offset(evaluation.Origin.X, evaluation.Origin.Y);
                projection.gameObject.SetActive(true);
                projection.transform.SetParent(_boardView.transform, worldPositionStays: false);
                projection.transform.localRotation = Quaternion.identity;
                projection.Configure(
                    absolute,
                    _boardView.Mapper.CellCenterLocal(absolute),
                    _cellSize,
                    FragmentCellSprites(fragment, local),
                    null);
                projection.name = "BoardEditProjection";
                projection.SetInteractionEnabled(false);
                projection.ClearPlateFeedbackColor();
                projection.SetColor(Color.white);
                projection.SetSorting(BattleSorting.Fx, EditProjectionSortingOrder);
            }

            for (; index < _editProjectionCells.Count; index++)
            {
                _editProjectionCells[index]?.gameObject.SetActive(false);
            }
        }

        private DiningTableCellView EnsureProjectionCell(int index)
        {
            while (_editProjectionCells.Count <= index)
            {
                _editProjectionCells.Add(null);
            }

            DiningTableCellView projection = _editProjectionCells[index];
            if (projection != null)
            {
                return projection;
            }

            if (_boardCellPrefab == null || _boardView == null)
            {
                Debug.LogError($"{nameof(DiningTableEditController)} 缺少餐桌碎片映射所需的 DiningTableCell prefab。", this);
                return null;
            }

            projection = Instantiate(_boardCellPrefab, _boardView.transform);
            projection.name = "BoardEditProjection";
            projection.SetInteractionEnabled(false);
            projection.gameObject.SetActive(false);
            _editProjectionCells[index] = projection;
            return projection;
        }

        private void HideProjection()
        {
            foreach (DiningTableCellView projection in _editProjectionCells)
            {
                if (projection != null)
                {
                    projection.gameObject.SetActive(false);
                }
            }
        }

        private static Quaternion TrayRotationForCandidate(int candidateIndex)
        {
            int hash = (candidateIndex + 1) * 73856093;
            float angle = HashToSignedUnit(hash) * EditTrayGroupRotation;
            return Quaternion.Euler(0f, 0f, angle);
        }

        private static float HashToSignedUnit(int hash)
        {
            unchecked
            {
                uint mixed = (uint)hash;
                mixed ^= mixed >> 16;
                mixed *= 0x7feb352d;
                mixed ^= mixed >> 15;
                mixed *= 0x846ca68b;
                mixed ^= mixed >> 16;
                return (mixed / (float)uint.MaxValue) * 2f - 1f;
            }
        }

        private async void StartDragAnimation(
            Vector3 targetPosition,
            Vector3 targetScale,
            float duration,
            bool keepFollowing,
            Action onComplete = null)
        {
            CancelDragAnimation();
            if (_editDragRoot == null)
            {
                onComplete?.Invoke();
                return;
            }

            Vector3 startPosition = _editDragRoot.position;
            Vector3 startScale = _editDragRoot.localScale;
            float safeDuration = Mathf.Max(0.0001f, duration);
            CancellationToken token = CreateDragToken();
            Tween tween = DOVirtual.Float(0f, 1f, safeDuration, t =>
                {
                    if (_editDragRoot == null)
                    {
                        return;
                    }

                    float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
                    _editDragRoot.position = keepFollowing
                        ? _editDragPointerWorld
                        : Vector3.Lerp(startPosition, targetPosition, eased);
                    _editDragRoot.localScale = Vector3.Lerp(startScale, targetScale, eased);
                })
                .SetEase(Ease.Linear)
                .SetLink(_editDragRoot.gameObject);

            try
            {
                await PresentationTween.AwaitCompletionAsync(tween, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }

            if (_editDragRoot != null)
            {
                _editDragRoot.position = keepFollowing ? _editDragPointerWorld : targetPosition;
                _editDragRoot.localScale = targetScale;
            }

            DisposeDragToken();
            if (!keepFollowing)
            {
                onComplete?.Invoke();
            }
        }

        private void FinishReturnDrag()
        {
            ClearDragVisual(stopRoutine: false);
            _editSelected = -1;
            _fragmentChoiceState = FragmentChoiceInteractionState.Idle;
            _currentPlacementEvaluation = null;
            BuildCandidateTray();
            PublishEditActionState(canConfirm: false, interactable: true);
        }

        private bool TryPickTray(Vector3 worldPos, out int candidateIndex)
        {
            float padding = _editLayoutArea != null ? _editLayoutArea.HitPadding : 0f;
            return BoardEditTrayHitTester.TryPick(worldPos, _editTrayHitRegions, padding, out candidateIndex);
        }

        private bool TryPickStagedFragment(Vector3 worldPos)
        {
            bool hasBounds = false;
            Bounds bounds = default;
            foreach (DiningTableCellView cell in _editDragCells)
            {
                if (cell == null)
                {
                    continue;
                }

                if (hasBounds)
                {
                    bounds.Encapsulate(cell.WorldBounds);
                }
                else
                {
                    bounds = cell.WorldBounds;
                    hasBounds = true;
                }
            }

            return hasBounds
                && worldPos.x >= bounds.min.x
                && worldPos.x <= bounds.max.x
                && worldPos.y >= bounds.min.y
                && worldPos.y <= bounds.max.y;
        }

        private void PlayLockedCandidateShake(int candidateIndex)
        {
            TrayCluster? target = null;
            foreach (TrayCluster cluster in _editTrayClusters)
            {
                if (cluster.CandidateIndex == candidateIndex)
                {
                    target = cluster;
                    break;
                }
            }

            if (!target.HasValue)
            {
                return;
            }

            TrayCluster captured = target.Value;
            RestoreTrayClusterPositions(captured);
            if (_editTrayFailureTweens.TryGetValue(candidateIndex, out Tween previous))
            {
                previous?.Kill(false);
            }

            float amplitude = Mathf.Max(0.01f, _editTraySize * EditTrayLockedShakeAmplitudeRatio);
            Tween tween = null;
            tween = DOVirtual.Float(0f, 1f, EditTrayLockedShakeDuration, t =>
                {
                    float offset = Mathf.Sin(t * Mathf.PI * 12f) * amplitude * (1f - t);
                    for (int i = 0; i < captured.Cells.Count && i < captured.BaseLocalPositions.Count; i++)
                    {
                        DiningTableCellView cell = captured.Cells[i];
                        if (cell != null)
                        {
                            cell.transform.localPosition =
                                captured.BaseLocalPositions[i] + new Vector3(offset, 0f, 0f);
                        }
                    }
                })
                .SetEase(Ease.Linear)
                .SetUpdate(true)
                .OnKill(() =>
                {
                    RestoreTrayClusterPositions(captured);
                    if (_editTrayFailureTweens.TryGetValue(candidateIndex, out Tween active)
                        && active == tween)
                    {
                        _editTrayFailureTweens.Remove(candidateIndex);
                    }
                });
            _editTrayFailureTweens[candidateIndex] = tween;
        }

        private static void RestoreTrayClusterPositions(TrayCluster cluster)
        {
            if (cluster.Cells == null || cluster.BaseLocalPositions == null)
            {
                return;
            }

            for (int i = 0; i < cluster.Cells.Count && i < cluster.BaseLocalPositions.Count; i++)
            {
                DiningTableCellView cell = cluster.Cells[i];
                if (cell != null)
                {
                    cell.transform.localPosition = cluster.BaseLocalPositions[i];
                }
            }
        }

        private void PublishEditActionState(bool canConfirm, bool interactable)
        {
            if (_hasPublishedActionState
                && _lastPublishedCanConfirm == canConfirm
                && _lastPublishedInteractable == interactable)
            {
                return;
            }

            _hasPublishedActionState = true;
            _lastPublishedCanConfirm = canConfirm;
            _lastPublishedInteractable = interactable;
            _editActionStateChanged?.Invoke(new TableFragmentEditActionState(canConfirm, interactable));
        }

        private void UpdateCandidateHover(Vector3 worldPos)
        {
            SetHoveredCandidate(TryPickTray(worldPos, out int candidateIndex) ? candidateIndex : -1);
        }

        private void SetHoveredCandidate(int candidateIndex)
        {
            if (_hoveredCandidateIndex == candidateIndex)
            {
                return;
            }

            int previous = _hoveredCandidateIndex;
            _hoveredCandidateIndex = candidateIndex;
            if (previous >= 0 && previous < _editCandidates.Count)
            {
                _candidateHoverExited?.Invoke(BuildHoverInfo(previous));
            }

            if (candidateIndex < 0 || candidateIndex >= _editCandidates.Count)
            {
                return;
            }

            foreach (TrayCluster cluster in _editTrayClusters)
            {
                if (cluster.CandidateIndex == candidateIndex)
                {
                    _candidateHoverEntered?.Invoke(new TableFragmentHoverInfo(
                        _choiceSessionVersion,
                        candidateIndex,
                        _editCandidates[candidateIndex],
                        cluster.WorldBounds));
                    return;
                }
            }
        }

        private TableFragmentHoverInfo BuildHoverInfo(int candidateIndex)
        {
            Bounds bounds = default;
            foreach (TrayCluster cluster in _editTrayClusters)
            {
                if (cluster.CandidateIndex == candidateIndex)
                {
                    bounds = cluster.WorldBounds;
                    break;
                }
            }

            return new TableFragmentHoverInfo(
                _choiceSessionVersion,
                candidateIndex,
                candidateIndex >= 0 && candidateIndex < _editCandidates.Count
                    ? _editCandidates[candidateIndex]
                    : null,
                bounds);
        }

        private DiningTableCellView InstantiateDragCell()
        {
            if (_boardCellPrefab != null)
            {
                return Instantiate(_boardCellPrefab, _editDragRoot);
            }

            Debug.LogError($"{nameof(DiningTableEditController)} 缺少 DiningTableCell prefab。", this);
            return null;
        }

        private DiningTableCellView InstantiateTrayCell()
        {
            if (_boardCellPrefab != null && EditRoot != null)
            {
                return Instantiate(_boardCellPrefab, EditRoot);
            }

            Debug.LogError($"{nameof(DiningTableEditController)} 缺少 DiningTableCell prefab。", this);
            return null;
        }

        private void ClearGhost(bool restoreDragCellColors = true)
        {
            if (restoreDragCellColors)
            {
                RestoreDragCellColors();
            }

            HideProjection();
            _boardView?.ClearDragPlacementFeedback();
        }

        private void ClearTray()
        {
            SetHoveredCandidate(-1);
            var failureTweens = new List<Tween>(_editTrayFailureTweens.Values);
            _editTrayFailureTweens.Clear();
            foreach (Tween tween in failureTweens)
            {
                tween?.Kill(false);
            }
            foreach (DiningTableCellView cell in _editTrayCells)
            {
                if (cell != null)
                {
                    Destroy(cell.gameObject);
                }
            }

            _editTrayCells.Clear();
            _editTrayHitRegions.Clear();
            _editTrayClusters.Clear();
        }

        private void ClearDragVisual(bool stopRoutine = true)
        {
            CancelPlacementVisualAnimations(resetVisuals: false);
            if (stopRoutine)
            {
                CancelDragAnimation();
            }

            foreach (DiningTableCellView cell in _editDragCells)
            {
                if (cell != null)
                {
                    Destroy(cell.gameObject);
                }
            }

            _editDragCells.Clear();
            _editDragCellBaseScales.Clear();
            _boardView?.ClearTransientGridRegionOutline();
            if (_editDragRoot != null)
            {
                Destroy(_editDragRoot.gameObject);
                _editDragRoot = null;
            }
        }

        private CancellationToken CreateDragToken()
        {
            _editDragCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            return _editDragCts.Token;
        }

        private void CancelDragAnimation()
        {
            if (_editDragCts == null)
            {
                return;
            }

            _editDragCts.Cancel();
            _editDragCts.Dispose();
            _editDragCts = null;
        }

        private void DisposeDragToken()
        {
            _editDragCts?.Dispose();
            _editDragCts = null;
        }
    }
}
