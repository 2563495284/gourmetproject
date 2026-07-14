using System;
using System.Collections.Generic;
using System.Threading;
using DG.Tweening;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 餐桌编辑页（世界空间）：复用餐桌渲染，把从碎片包开出的候选形状手动拖拽拼贴到胃上。
    /// 按住底部候选开始拖拽，固定朝向，松开时合法则落位；右键或跳过按钮放弃本包。
    /// 亦承载只读「餐桌视图」：复用同一套布局但不显示候选托盘、不接受拖拽输入。
    ///
    /// 从 BattleWorldController 拆出，作为其协作组件挂在同一战斗场景根上：
    /// 只持有自身所需的场景引用（餐桌/相机/餐桌格 prefab/菜品根），不再触碰 Food 态私有成员；
    /// 世界互斥态（进入/退出）由 BattleWorldController 外壳调度，本类负责编辑/餐桌视图的表现与交互。
    /// </summary>
    public sealed class DiningTableEditController : MonoBehaviour
    {
        private const int EditGhostSortingOrder = 200;
        private const int EditTraySortingOrder = 150;
        private const int EditDragSortingOrder = 260;
        private const float EditDragGrabDuration = 0.12f;
        private const float EditDragReturnDuration = 0.16f;
        private const float EditGhostOutlineWidth = 0.075f;
        private const float EditBoundsWarningWidth = 0.055f;

        // 编辑页餐桌定位的底部边距：比 Food 态更大，给候选碎片托盘条让位。
        private const float EditTableBottomMargin = 3.6f;

        private static readonly Color GhostValidColor = new Color(0.35f, 0.9f, 0.4f, 0.85f);
        private static readonly Color GhostInvalidColor = new Color(0.95f, 0.35f, 0.3f, 0.7f);
        private static readonly Color BoundsWarningColor = new Color(1f, 0.05f, 0.02f, 0.42f);
        private static readonly Color TraySelectedColor = new Color(1f, 0.95f, 0.6f, 1f);
        private static readonly Color TrayNormalColor = new Color(0.85f, 0.85f, 0.85f, 0.9f);

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

        private bool _editing;
        private GameRun _editRun;
        private GpTable _editTable;
        private Action<bool> _editOnDone;
        private readonly List<TableFragmentDef> _editCandidates = new List<TableFragmentDef>();
        private int _editSelected = -1;
        private int _editRotation;
        private bool _editDragging;
        private bool _editDragAnimating;
        private bool _editDragReturning;

        [Header("编辑态固定结构")]
        [SerializeField] private Transform _editRoot;
        [SerializeField] private Transform _editDragRootPrefab;
        [SerializeField] private LineRenderer _editBoundsWarningLinePrefab;

        private readonly List<DiningTableCellView> _editGhostCells = new List<DiningTableCellView>();
        private readonly List<DiningTableCellView> _editTrayCells = new List<DiningTableCellView>();
        private readonly List<DiningTableCellView> _editDragCells = new List<DiningTableCellView>();
        private readonly List<TrayCluster> _editTrayClusters = new List<TrayCluster>();
        private Sprite _editCellSprite;
        private float _editTraySize;
        private Transform _editDragRoot;
        private readonly List<LineRenderer> _editBoundsWarningLines = new List<LineRenderer>();
        private Material _editBoundsWarningMaterial;
        private Vector3 _editDragReturnCenter;
        private float _editDragReturnScale = 1f;
        private int _editMaxWidth;
        private int _editMaxHeight;
        private CancellationTokenSource _editDragCts;

        /// <summary>是否正处于可拖拽的餐桌编辑态（只读餐桌视图不算）。</summary>
        public bool IsEditing => _editing;

        private struct TrayCluster
        {
            public int CandidateIndex;
            public Vector3 Center;
            public Vector2 HalfExtents;
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

        /// <summary>由外壳注入共享场景引用（餐桌、餐桌格 prefab、菜品根、相机）。</summary>
        public void Configure(BattleWorldController owner, DiningTableView boardView, DiningTableCellView boardCellPrefab, Transform piecesRoot, Camera camera)
        {
            _owner = owner;
            _boardView = boardView;
            _boardCellPrefab = boardCellPrefab;
            _piecesRoot = piecesRoot;
            _camera = camera;
        }

        /// <summary>
        /// 进入餐桌编辑页。<paramref name="candidateIds"/> 为碎片包开出的候选碎片 id（三选一）；
        /// <paramref name="onDone"/> 在拼贴完成或放弃后回调（由外层负责存档与返回商店）。
        /// 外壳已完成 Food 态清场与世界互斥态切换，这里只做编辑页的构建。
        /// </summary>
        public void BeginTableEdit(GameRun run, IReadOnlyList<string> candidateIds, Action<bool> onDone)
        {
            _editRun = run;
            _editOnDone = onDone;
            _editSelected = -1;
            _editRotation = 0;
            _editDragging = false;
            _editDragAnimating = false;
            _editDragReturning = false;
            ConfigureEditMaxBounds(run);

            _editCandidates.Clear();
            if (candidateIds != null)
            {
                foreach (string id in candidateIds)
                {
                    TableFragmentDef def = run.Database.GetFragment(id);
                    if (def != null)
                    {
                        _editCandidates.Add(def);
                    }
                }
            }

            ResolveCamera();
            CancelDragAnimation();
            ComputeViewport();

            _editCellSprite = Resources.Load<Sprite>("Sprites/UI/board_cell");
            _editTable = BattleSessionFactory.BuildTablePreview(run);
            LayoutEditorTable(_editTable);
            EnsureEditRoot();
            BuildCandidateTray();

            _editing = true;
        }

        /// <summary>进入只读餐桌视图：复用编辑页餐桌布局，但不显示候选碎片托盘，也不启用拖拽输入。</summary>
        public void BeginTableView(GameRun run)
        {
            _editRun = run;
            ClearTray();
            ClearGhost();
            ClearDragVisual();
            HideBoundsWarning();
            ResolveCamera();
            CancelDragAnimation();
            ComputeViewport();

            _editCellSprite = Resources.Load<Sprite>("Sprites/UI/board_cell");
            _editTable = run.BuildTablePreviewFromFragments(run.WeekModifier);
            LayoutEditorTable(_editTable, useBoardArea: true);
        }

        public void EndTableView()
        {
            _boardView?.ShowVoidAsPlaceholders(false);
            _editTable = null;
            _owner?.ClearTableMode();
        }

        /// <summary>退出餐桌编辑页：清理动态内容，恢复餐桌常规显示。外层负责隐藏世界与返回。</summary>
        public void EndTableEdit()
        {
            _editing = false;
            _editSelected = -1;
            _editRotation = 0;
            _editDragging = false;
            _editDragAnimating = false;
            _editDragReturning = false;
            _editCandidates.Clear();
            ClearGhost();
            ClearDragVisual();
            ClearTray();
            HideBoundsWarning();
            _boardView?.ShowVoidAsPlaceholders(false);
            _editRun = null;
            _editTable = null;
            _editOnDone = null;
            _owner?.ClearTableMode();
        }

        public void SkipTableEditPack()
        {
            if (!_editing)
            {
                return;
            }

            SkipPack();
        }

        private void Update()
        {
            if (_editing)
            {
                UpdateTableEdit();
            }
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

            if (_editDragReturning)
            {
                ClearGhost();
                return;
            }

            if (WorldInput.SecondaryPressedThisFrame)
            {
                SkipPack();
                return;
            }

            if (!_editDragging)
            {
                if (WorldInput.PrimaryPressedThisFrame && TryPickTray(mouseWorld, out int picked))
                {
                    BeginCandidateDrag(picked, mouseWorld);
                    UpdateDragFeedback(mouseWorld);
                    return;
                }

                ClearGhost();
                HideBoundsWarning();
                return;
            }

            if (!_editDragAnimating && _editDragRoot != null)
            {
                UpdateDragFeedback(mouseWorld);
            }

            if (WorldInput.PrimaryReleasedThisFrame || !WorldInput.PrimaryHeld)
            {
                if (_editSelected >= 0 && TryGetHoverPlacement(mouseWorld, out GridPos origin, out bool valid) && valid)
                {
                    PlaceAt(origin);
                    return;
                }

                ReturnCandidateDrag();
                return;
            }

            UpdateDragFeedback(mouseWorld);
        }

        private void BeginCandidateDrag(int index, Vector3 mouseWorld)
        {
            if (index < 0 || index >= _editCandidates.Count)
            {
                return;
            }

            _editSelected = index;
            _editRotation = 0;
            _editDragging = true;
            _editDragReturning = false;
            _editDragReturnCenter = TrayCenterForCandidate(index, mouseWorld);
            _editDragReturnScale = Mathf.Clamp(_editTraySize / Mathf.Max(0.0001f, _cellSize), 0.01f, 1f);
            BuildCandidateTray();
            BuildDragVisual(_editCandidates[index], _editDragReturnCenter, _editDragReturnScale);
            Vector3 target = TryGetDragPlacement(mouseWorld, out _, out _, out _, out Vector3 snappedCenter)
                ? snappedCenter
                : mouseWorld;
            StartDragAnimation(target, Vector3.one, EditDragGrabDuration, keepFollowing: true);
            ClearGhost();
        }

        private void ReturnCandidateDrag()
        {
            _editDragging = false;
            _editDragReturning = true;
            BuildCandidateTray();
            ClearGhost();
            HideBoundsWarning();

            if (_editDragRoot == null)
            {
                FinishReturnDrag();
                return;
            }

            StartDragAnimation(_editDragReturnCenter, Vector3.one * _editDragReturnScale, EditDragReturnDuration, keepFollowing: false, FinishReturnDrag);
        }

        private void UpdateDragFeedback(Vector3 mouseWorld)
        {
            if (!_editDragging || _editSelected < 0)
            {
                ClearGhost();
                return;
            }

            if (!TryGetDragPlacement(mouseWorld, out GridPos origin, out bool valid, out _, out Vector3 snappedCenter))
            {
                ClearGhost();
                HideBoundsWarning();
                return;
            }

            Color color = valid ? GhostValidColor : GhostInvalidColor;
            if (_editDragRoot != null && !_editDragAnimating)
            {
                _editDragRoot.position = snappedCenter;
            }

            SetDragOutline(color);
            TableFragmentDef def = _editCandidates[_editSelected];
            if (TryGetBoundsWarning(origin, def, out BoundsWarningInfo warning))
            {
                ShowBoundsWarning(warning);
            }
            else
            {
                HideBoundsWarning();
            }

            ClearGhost();
        }

        private bool TryGetHoverPlacement(Vector3 mouseWorld, out GridPos origin, out bool valid)
        {
            origin = default;
            valid = false;
            if (_editSelected < 0 || _editSelected >= _editCandidates.Count)
            {
                return false;
            }

            TableFragmentDef def = _editCandidates[_editSelected];
            origin = NearestOriginForFragmentCenter(mouseWorld, def);
            TableFragmentBuilder.FragmentPlacementStatus status = TableFragmentBuilder.GetFragmentPlacementStatusWithinMaxBounds(
                TableFragmentBuilder.ToExistingSet(_editTable),
                def,
                origin,
                _editMaxWidth,
                _editMaxHeight);
            valid = status == TableFragmentBuilder.FragmentPlacementStatus.Valid;
            return true;
        }

        private bool TryGetDragPlacement(
            Vector3 mouseWorld,
            out GridPos origin,
            out bool valid,
            out TableFragmentBuilder.FragmentPlacementStatus status,
            out Vector3 centerWorld)
        {
            centerWorld = mouseWorld;
            status = TableFragmentBuilder.FragmentPlacementStatus.Detached;
            if (!TryGetHoverPlacement(mouseWorld, out origin, out valid))
            {
                return false;
            }

            TableFragmentDef def = _editCandidates[_editSelected];
            status = TableFragmentBuilder.GetFragmentPlacementStatusWithinMaxBounds(
                TableFragmentBuilder.ToExistingSet(_editTable),
                def,
                origin,
                _editMaxWidth,
                _editMaxHeight);
            valid = status == TableFragmentBuilder.FragmentPlacementStatus.Valid;
            centerWorld = FragmentCenterWorld(origin, def);
            return true;
        }

        private GridPos NearestOriginForFragmentCenter(Vector3 mouseWorld, TableFragmentDef def)
        {
            DiningTableCoordinateMapper mapper = _boardView.Mapper;
            if (mapper == null)
            {
                return default;
            }

            Vector3 localMouse = mapper.Root != null ? mapper.Root.InverseTransformPoint(mouseWorld) : mouseWorld;
            Vector3 originCenterLocal = localMouse - FragmentCenterOffsetLocal(def);
            Vector3 originCenterWorld = mapper.Root != null ? mapper.Root.TransformPoint(originCenterLocal) : originCenterLocal;
            return mapper.NearestCell(originCenterWorld);
        }

        private Vector3 FragmentCenterOffsetLocal(TableFragmentDef def)
        {
            List<GridPos> cells = TableFragmentBuilder.FilledCells(def);
            if (cells.Count == 0 || _boardView.Mapper == null)
            {
                return Vector3.zero;
            }

            float sumX = 0f;
            float sumY = 0f;
            foreach (GridPos cell in cells)
            {
                sumX += cell.X;
                sumY += cell.Y;
            }

            float inv = 1f / cells.Count;
            float pitch = _boardView.Mapper.Pitch;
            return new Vector3(sumX * inv * pitch, -sumY * inv * pitch, 0f);
        }

        private Vector3 FragmentCenterWorld(GridPos origin, TableFragmentDef def)
        {
            DiningTableCoordinateMapper mapper = _boardView.Mapper;
            if (mapper == null)
            {
                return Vector3.zero;
            }

            Vector3 local = mapper.CellCenterLocal(origin) + FragmentCenterOffsetLocal(def);
            return mapper.Root != null ? mapper.Root.TransformPoint(local) : local;
        }

        private void PlaceAt(GridPos origin)
        {
            if (_editSelected < 0)
            {
                return;
            }

            string fragmentId = _editCandidates[_editSelected].Id;
            _editRun.AddFragmentPlacement(fragmentId, 0, origin);
            _editRun.ClearPendingFragmentPack();
            _editDragging = false;
            HideBoundsWarning();

            Action<bool> done = _editOnDone;
            EndTableEdit();
            done?.Invoke(true);
        }

        private void SkipPack()
        {
            _editRun?.ClearPendingFragmentPack();
            Action<bool> done = _editOnDone;
            EndTableEdit();
            done?.Invoke(false);
        }

        private void LayoutEditorTable(GpTable board, bool useBoardArea = false)
        {
            // 编辑页按当前实际胃形居中；最大包围盒只参与逻辑限制，不作为背景网格铺出来。
            // 只读查看态复用 Food 态的 HUD 空区，编辑态仍保留底部托盘让位。
            BoardPlacement placement = useBoardArea && _owner != null && _owner.TryComputeTableAreaPlacement(board, out BoardPlacement boardAreaPlacement)
                ? boardAreaPlacement
                : DiningTableLayout.Compute(_halfW, _halfH, board, EditTableBottomMargin);
            _cellSize = placement.CellSize;
            _boardView.transform.rotation = Quaternion.identity;
            _boardView.transform.localScale = Vector3.one;
            _boardView.transform.position = placement.Position;

            if (_piecesRoot != null && _piecesRoot.parent != _boardView.transform)
            {
                _piecesRoot.SetParent(_boardView.transform, worldPositionStays: false);
                _piecesRoot.localPosition = Vector3.zero;
                _piecesRoot.localRotation = Quaternion.identity;
                _piecesRoot.localScale = Vector3.one;
            }

            _boardView.Build(board, _cellSize, DiningTableLayout.Gap, null, _boardCellPrefab);
            _editGhostCells.Clear();
            _boardView.ShowVoidAsPlaceholders(false);

            _editTraySize = Mathf.Clamp(_cellSize * 0.62f, 0.28f, 0.6f);
        }

        private void EnsureEditRoot()
        {
            if (_editRoot == null)
            {
                Transform root = transform.Find("BoardEditRoot");
                _editRoot = root;
            }

            if (_editRoot == null)
            {
                Debug.LogError($"{nameof(DiningTableEditController)} 缺少 BoardEditRoot 预置节点。", this);
                return;
            }

            _editRoot.localPosition = Vector3.zero;
            _editRoot.localScale = Vector3.one;
        }

        private void ConfigureEditMaxBounds(GameRun run)
        {
            cfg.Character character = run.Tables.TbCharacter.GetOrDefault(run.CharacterId);
            _editMaxWidth = character != null && character.MaxDiningTableWidth > 0 ? character.MaxDiningTableWidth : GameRun.BoardWidth;
            _editMaxHeight = character != null && character.MaxDiningTableHeight > 0 ? character.MaxDiningTableHeight : GameRun.BoardHeight;
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
                if (_editRoot == null || _editBoundsWarningLinePrefab == null)
                {
                    Debug.LogError($"{nameof(DiningTableEditController)} 缺少 BoundsWarningLine prefab。", this);
                    return null;
                }

                LineRenderer line = Instantiate(_editBoundsWarningLinePrefab, _editRoot);
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
            if (n == 0)
            {
                return;
            }

            float trayY = -_halfH + 1.9f;
            float spacing = Mathf.Min(3.2f, (2f * (_halfW - 2.6f)) / n);
            float startX = -(n - 1) * 0.5f * spacing;

            for (int i = 0; i < n; i++)
            {
                TableFragmentDef shown = _editCandidates[i];
                List<GridPos> cells = TableFragmentBuilder.FilledCells(shown);
                if (cells.Count == 0)
                {
                    continue;
                }

                int maxX = 0;
                int maxY = 0;
                foreach (GridPos c in cells)
                {
                    if (c.X > maxX) maxX = c.X;
                    if (c.Y > maxY) maxY = c.Y;
                }

                int w = maxX + 1;
                int h = maxY + 1;
                float clusterCenterX = startX + i * spacing;
                float leftX = clusterCenterX - (w - 1) * 0.5f * _editTraySize;
                float topY = trayY + (h - 1) * 0.5f * _editTraySize;

                if ((_editDragging || _editDragReturning) && i == _editSelected)
                {
                    continue;
                }

                Color color = i == _editSelected ? TraySelectedColor : TrayNormalColor;
                foreach (GridPos c in cells)
                {
                    DiningTableCellView cell = InstantiateTrayCell();
                    if (cell == null)
                    {
                        continue;
                    }

                    var world = new Vector3(leftX + c.X * _editTraySize, topY - c.Y * _editTraySize, 0f);
                    cell.Configure(c, world, _editTraySize, _editCellSprite, null);
                    cell.SetColor(color);
                    cell.SetSortingOrder(EditTraySortingOrder);
                    _editTrayCells.Add(cell);
                }

                _editTrayClusters.Add(new TrayCluster
                {
                    CandidateIndex = i,
                    Center = new Vector3(clusterCenterX, trayY, 0f),
                    HalfExtents = new Vector2(w * _editTraySize * 0.5f + 0.15f, h * _editTraySize * 0.5f + 0.15f),
                });
            }
        }

        private Vector3 TrayCenterForCandidate(int candidateIndex, Vector3 fallback)
        {
            foreach (TrayCluster cluster in _editTrayClusters)
            {
                if (cluster.CandidateIndex == candidateIndex)
                {
                    return cluster.Center;
                }
            }

            return fallback;
        }

        private void RebuildDragVisualAt(Vector3 mouseWorld)
        {
            if (!_editDragging || _editSelected < 0 || _editSelected >= _editCandidates.Count)
            {
                return;
            }

            Vector3 center = _editDragRoot != null ? _editDragRoot.position : mouseWorld;
            Vector3 scale = _editDragRoot != null ? _editDragRoot.localScale : Vector3.one;
            BuildDragVisual(_editCandidates[_editSelected], center, scale);
        }

        private void BuildDragVisual(TableFragmentDef def, Vector3 center, float scale)
        {
            BuildDragVisual(def, center, Vector3.one * Mathf.Max(0.0001f, scale));
        }

        private void BuildDragVisual(TableFragmentDef def, Vector3 center, Vector3 scale)
        {
            ClearDragVisual();
            EnsureEditRoot();
            if (_editRoot == null || _editDragRootPrefab == null)
            {
                Debug.LogError($"{nameof(DiningTableEditController)} 缺少 BoardEditDrag prefab。", this);
                return;
            }

            _editDragRoot = Instantiate(_editDragRootPrefab, _editRoot);
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
                cell.Configure(cellPos, local, _cellSize, _editCellSprite, null);
                cell.SetColor(Color.white);
                cell.SetSortingOrder(EditDragSortingOrder);
                _editDragCells.Add(cell);
            }
        }

        private void SetDragOutline(Color color)
        {
            foreach (DiningTableCellView cell in _editDragCells)
            {
                if (cell != null)
                {
                    cell.SetOutline(color, EditGhostOutlineWidth, fillAlpha: 1f);
                    cell.SetSortingOrder(EditDragSortingOrder);
                }
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

            _editDragAnimating = true;
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
                    _editDragRoot.position = Vector3.Lerp(startPosition, targetPosition, eased);
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
                _editDragRoot.position = targetPosition;
                _editDragRoot.localScale = targetScale;
            }

            _editDragAnimating = false;
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
            _editRotation = 0;
            _editDragging = false;
            _editDragReturning = false;
            _editDragAnimating = false;
            BuildCandidateTray();
        }

        private bool TryPickTray(Vector3 worldPos, out int candidateIndex)
        {
            candidateIndex = -1;
            foreach (TrayCluster cluster in _editTrayClusters)
            {
                if (Mathf.Abs(worldPos.x - cluster.Center.x) <= cluster.HalfExtents.x &&
                    Mathf.Abs(worldPos.y - cluster.Center.y) <= cluster.HalfExtents.y)
                {
                    candidateIndex = cluster.CandidateIndex;
                    return true;
                }
            }

            return false;
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
            if (_boardCellPrefab != null)
            {
                return Instantiate(_boardCellPrefab, _editRoot);
            }

            Debug.LogError($"{nameof(DiningTableEditController)} 缺少 DiningTableCell prefab。", this);
            return null;
        }

        private void EnsureGhostCount(int count)
        {
            for (int i = _editGhostCells.Count - 1; i >= 0; i--)
            {
                if (_editGhostCells[i] == null)
                {
                    _editGhostCells.RemoveAt(i);
                }
            }

            while (_editGhostCells.Count < count)
            {
                DiningTableCellView ghost;
                if (_boardCellPrefab != null)
                {
                    ghost = Instantiate(_boardCellPrefab, _boardView.transform);
                }
                else
                {
                    Debug.LogError($"{nameof(DiningTableEditController)} 缺少 DiningTableCell prefab。", this);
                    return;
                }

                ghost.Configure(new GridPos(0, 0), Vector3.zero, _cellSize, _editCellSprite, null);
                ghost.gameObject.SetActive(false);
                _editGhostCells.Add(ghost);
            }
        }

        private void ClearGhost()
        {
            foreach (DiningTableCellView ghost in _editGhostCells)
            {
                if (ghost != null)
                {
                    ghost.gameObject.SetActive(false);
                }
            }
        }

        private void ClearTray()
        {
            foreach (DiningTableCellView cell in _editTrayCells)
            {
                if (cell != null)
                {
                    Destroy(cell.gameObject);
                }
            }

            _editTrayCells.Clear();
            _editTrayClusters.Clear();
        }

        private void ClearDragVisual(bool stopRoutine = true)
        {
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
            _editDragAnimating = false;
        }

        private void DisposeDragToken()
        {
            _editDragCts?.Dispose();
            _editDragCts = null;
        }
    }
}
