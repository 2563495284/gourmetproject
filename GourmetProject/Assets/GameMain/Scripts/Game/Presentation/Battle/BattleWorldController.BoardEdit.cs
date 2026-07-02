using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using GpBoard = GourmetProject.Gameplay.Board.Board;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 棋盘编辑页（世界空间）：复用棋盘渲染，在最大网格（虚格占位）上把从碎片包开出的候选形状手动拼贴到胃上。
    /// 选中候选后可用鼠标滚轮旋转，悬停高亮可放/不可放，左键落位，右键放弃（折算金币）。
    /// </summary>
    public sealed partial class BattleWorldController
    {
        private const int EditGhostSortingOrder = 200;
        private const int EditTraySortingOrder = 150;
        private static readonly Color GhostValidColor = new Color(0.35f, 0.9f, 0.4f, 0.85f);
        private static readonly Color GhostInvalidColor = new Color(0.95f, 0.35f, 0.3f, 0.7f);
        private static readonly Color TraySelectedColor = new Color(1f, 0.95f, 0.6f, 1f);
        private static readonly Color TrayNormalColor = new Color(0.85f, 0.85f, 0.85f, 0.9f);

        private bool _editing;
        private GameRun _editRun;
        private GpBoard _editBoard;
        private Action _editOnDone;
        private readonly List<StomachFragmentDef> _editCandidates = new List<StomachFragmentDef>();
        private int _editSelected = -1;
        private int _editRotation;

        private Transform _editRoot;
        private readonly List<BoardCellView> _editGhostCells = new List<BoardCellView>();
        private readonly List<BoardCellView> _editTrayCells = new List<BoardCellView>();
        private readonly List<TrayCluster> _editTrayClusters = new List<TrayCluster>();
        private Sprite _editCellSprite;
        private float _editTraySize;

        public bool IsEditingBoard => _editing;

        private struct TrayCluster
        {
            public int CandidateIndex;
            public Vector3 Center;
            public Vector2 HalfExtents;
        }

        /// <summary>
        /// 进入棋盘编辑页。<paramref name="candidateIds"/> 为碎片包开出的候选碎片 id（三选一）；
        /// <paramref name="onDone"/> 在拼贴完成或放弃后回调（由外层负责存档与返回商店）。
        /// </summary>
        public void BeginBoardEdit(GameRun run, IReadOnlyList<string> candidateIds, Action onDone)
        {
            if (run == null)
            {
                onDone?.Invoke();
                return;
            }

            _editRun = run;
            _editOnDone = onDone;
            _editSelected = -1;
            _editRotation = 0;

            _editCandidates.Clear();
            if (candidateIds != null)
            {
                foreach (string id in candidateIds)
                {
                    StomachFragmentDef def = run.Database.GetFragment(id);
                    if (def != null)
                    {
                        _editCandidates.Add(def);
                    }
                }
            }

            if (_camera == null)
            {
                _camera = Camera.main;
            }

            gameObject.SetActive(true);
            StopAllCoroutines();
            _settling = false;
            _serving = false;
            _session = null;
            ClearPlacedPieces();
            SetGourmetHudVisible(false, animated: false);
            ComputeViewport();

            _editCellSprite = Resources.Load<Sprite>("Sprites/UI/board_cell");
            _editBoard = BattleSessionFactory.BuildBoardPreview(run);
            LayoutEditorBoard(_editBoard);
            EnsureEditRoot();
            BuildCandidateTray();

            // 默认选中第一个候选，方便直接拖放。
            if (_editCandidates.Count > 0)
            {
                SelectCandidate(0);
            }

            _editing = true;
        }

        /// <summary>退出棋盘编辑页：清理动态内容，恢复棋盘常规显示。外层负责隐藏世界与返回。</summary>
        public void EndBoardEdit()
        {
            _editing = false;
            _editSelected = -1;
            _editRotation = 0;
            _editCandidates.Clear();
            ClearGhost();
            ClearTray();
            _boardView?.ShowVoidAsPlaceholders(false);
            _editRun = null;
            _editBoard = null;
            _editOnDone = null;
        }

        private void Update()
        {
            if (_editing)
            {
                UpdateBoardEdit();
            }
        }

        private void UpdateBoardEdit()
        {
            if (_editBoard == null)
            {
                return;
            }

            // 滚轮旋转选中候选。
            float scroll = WorldInput.ScrollDelta;
            if (_editSelected >= 0 && Mathf.Abs(scroll) > 0.01f)
            {
                _editRotation = ((_editRotation + (scroll > 0f ? 1 : 3)) % 4 + 4) % 4;
                BuildCandidateTray();
            }

            Vector3 mouseWorld = WorldInput.MouseWorld(_camera);

            // 左键：优先命中底部候选托盘选择；否则若已选中且悬停位置合法则落位。
            if (WorldInput.PrimaryPressedThisFrame)
            {
                if (TryPickTray(mouseWorld, out int picked))
                {
                    SelectCandidate(picked);
                    return;
                }

                if (_editSelected >= 0 && TryGetHoverPlacement(mouseWorld, out GridPos origin, out bool valid) && valid)
                {
                    PlaceAt(origin);
                    return;
                }
            }

            // 右键：放弃碎片包（折算金币兜底）。
            if (WorldInput.SecondaryPressedThisFrame)
            {
                SkipPack();
                return;
            }

            // 悬停预览。
            UpdateHoverGhost(mouseWorld);
        }

        private void SelectCandidate(int index)
        {
            if (index < 0 || index >= _editCandidates.Count)
            {
                return;
            }

            _editSelected = index;
            _editRotation = 0;
            BuildCandidateTray();
            ClearGhost();
        }

        private void UpdateHoverGhost(Vector3 mouseWorld)
        {
            if (_editSelected < 0)
            {
                ClearGhost();
                return;
            }

            if (!TryGetHoverPlacement(mouseWorld, out GridPos origin, out bool valid))
            {
                ClearGhost();
                return;
            }

            StomachFragmentDef def = _editCandidates[_editSelected].Rotated(_editRotation);
            List<GridPos> cells = StomachBuilder.FilledCells(def);
            Color color = valid ? GhostValidColor : GhostInvalidColor;

            EnsureGhostCount(cells.Count);
            for (int i = 0; i < _editGhostCells.Count; i++)
            {
                BoardCellView ghost = _editGhostCells[i];
                if (i < cells.Count)
                {
                    GridPos abs = cells[i].Offset(origin.X, origin.Y);
                    ghost.gameObject.SetActive(true);
                    ghost.transform.localPosition = _boardView.Mapper.CellCenterLocal(abs);
                    ghost.SetColor(color);
                    ghost.SetSortingOrder(EditGhostSortingOrder);
                }
                else
                {
                    ghost.gameObject.SetActive(false);
                }
            }
        }

        private bool TryGetHoverPlacement(Vector3 mouseWorld, out GridPos origin, out bool valid)
        {
            origin = _boardView.Mapper.NearestCell(mouseWorld);
            valid = false;
            if (_editSelected < 0)
            {
                return false;
            }

            StomachFragmentDef def = _editCandidates[_editSelected].Rotated(_editRotation);
            valid = StomachBuilder.CanPlaceFragmentAt(_editBoard, def, 0, origin);
            return true;
        }

        private void PlaceAt(GridPos origin)
        {
            if (_editSelected < 0)
            {
                return;
            }

            string fragmentId = _editCandidates[_editSelected].Id;
            _editRun.AddFragmentPlacement(fragmentId, _editRotation, origin);
            _editRun.ClearPendingFragmentPack();

            Action done = _editOnDone;
            EndBoardEdit();
            done?.Invoke();
        }

        private void SkipPack()
        {
            if (_editRun != null)
            {
                int gold = GourmetProject.Game.Meta.HiddenScoreService.FragmentFallbackGold(_editRun, _editRun.LastActionContext);
                _editRun.Gold += Mathf.Max(0, gold);
                _editRun.ClearPendingFragmentPack();
            }

            Action done = _editOnDone;
            EndBoardEdit();
            done?.Invoke();
        }

        private void LayoutEditorBoard(GpBoard board)
        {
            float boardLeft = -_halfW + 2.6f;
            float boardRight = _halfW - 2.6f;
            float boardTop = _halfH - 1.7f;
            // 底部留出候选托盘条。
            float boardBottom = -_halfH + 3.6f;
            float availW = Mathf.Max(1f, boardRight - boardLeft);
            float availH = Mathf.Max(1f, boardTop - boardBottom);

            // 编辑页按「完整最大网格」定尺寸与居中：所有可扩展的虚格都要能看到。
            _cellSize = Mathf.Clamp(Mathf.Min(availW / board.Width, availH / board.Height), MinCellSize, MaxCellSize);

            var areaCenter = new Vector3((boardLeft + boardRight) * 0.5f, (boardTop + boardBottom) * 0.5f, 0f);
            _boardView.transform.rotation = Quaternion.identity;
            _boardView.transform.localScale = Vector3.one;
            _boardView.transform.position = areaCenter;

            if (_piecesRoot != null && _piecesRoot.parent != _boardView.transform)
            {
                _piecesRoot.SetParent(_boardView.transform, worldPositionStays: false);
                _piecesRoot.localPosition = Vector3.zero;
                _piecesRoot.localRotation = Quaternion.identity;
                _piecesRoot.localScale = Vector3.one;
            }

            _boardView.Build(board, _cellSize, Gap, null, _boardCellPrefab);
            _boardView.ShowVoidAsPlaceholders(true);

            _editTraySize = Mathf.Clamp(_cellSize * 0.62f, 0.28f, 0.6f);
        }

        private void EnsureEditRoot()
        {
            if (_editRoot == null)
            {
                var go = new GameObject("BoardEditRoot");
                go.transform.SetParent(transform, false);
                _editRoot = go.transform;
            }

            _editRoot.localPosition = Vector3.zero;
            _editRoot.localScale = Vector3.one;
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
                int rot = i == _editSelected ? _editRotation : 0;
                StomachFragmentDef shown = rot == 0 ? _editCandidates[i] : _editCandidates[i].Rotated(rot);
                List<GridPos> cells = StomachBuilder.FilledCells(shown);
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

                Color color = i == _editSelected ? TraySelectedColor : TrayNormalColor;
                foreach (GridPos c in cells)
                {
                    BoardCellView cell = InstantiateTrayCell();
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

        private BoardCellView InstantiateTrayCell()
        {
            if (_boardCellPrefab != null)
            {
                return Instantiate(_boardCellPrefab, _editRoot);
            }

            var go = new GameObject("TrayCell");
            go.transform.SetParent(_editRoot, false);
            return go.AddComponent<BoardCellView>();
        }

        private void EnsureGhostCount(int count)
        {
            while (_editGhostCells.Count < count)
            {
                BoardCellView ghost;
                if (_boardCellPrefab != null)
                {
                    ghost = Instantiate(_boardCellPrefab, _boardView.transform);
                }
                else
                {
                    var go = new GameObject("GhostCell");
                    go.transform.SetParent(_boardView.transform, false);
                    ghost = go.AddComponent<BoardCellView>();
                }

                ghost.Configure(new GridPos(0, 0), Vector3.zero, _cellSize, _editCellSprite, null);
                ghost.gameObject.SetActive(false);
                _editGhostCells.Add(ghost);
            }
        }

        private void ClearGhost()
        {
            foreach (BoardCellView ghost in _editGhostCells)
            {
                if (ghost != null)
                {
                    ghost.gameObject.SetActive(false);
                }
            }
        }

        private void ClearTray()
        {
            foreach (BoardCellView cell in _editTrayCells)
            {
                if (cell != null)
                {
                    Destroy(cell.gameObject);
                }
            }

            _editTrayCells.Clear();
            _editTrayClusters.Clear();
        }
    }
}
