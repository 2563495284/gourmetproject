using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 「食物调整」态的世界空间交互协作组件（仅 Food 态启用，由 <see cref="BattleWorldController"/> 驱动）。
    /// 操作1 删除：鼠标悬浮菜品→其「右上角占格」出现 X 按钮，点 X 删除（消耗 1 次），留在态内。
    /// 操作2 移动：点选菜品→原位留半透明 ghost + 世界箭头指向鼠标 + 光标菜品(网格吸附/外轮廓发光判定可否放)，
    /// 点合法格暂放→新位右上角出现「勾」(确认并退出态) 与其下「撤销」(回原位、停留态内)。
    /// 移动进行中屏蔽其它菜品的悬浮 X 与点击。
    /// </summary>
    public sealed class FoodAdjustController : MonoBehaviour
    {
        private enum State
        {
            Idle,
            Moving,
            Placed,
        }

        private BattleWorldController _world;
        private bool _active;
        private Action _onExited;
        private State _state = State.Idle;
        private const float DragStartScreenThreshold = 8f;

        private DishInstance _movingDish;
        private Placement _originalPlacement;
        private Vector2 _moveStartScreen;
        private bool _dragReleaseArmed;
        private DishPieceView _ghostView;
        private DishPieceView _cursorView;
        private WorldTargetArrow _arrow;

        private WorldButtonView _deleteButton;
        private DishInstance _hoverDish;
        private WorldButtonView _confirmButton;
        private WorldButtonView _undoButton;

        public bool IsActive => _active;

        public void Configure(BattleWorldController world)
        {
            _world = world;
        }

        public void Begin(Action onExited)
        {
            _onExited = onExited;
            _active = true;
            _state = State.Idle;
            _hoverDish = null;
        }

        /// <summary>退出态：取消未提交的移动并回收全部临时表现（不触发 onExited，UI 复位由调用方处理）。</summary>
        public void End()
        {
            if (_state != State.Idle)
            {
                RestoreMovingDishToOriginal();
            }

            CleanupTransient();
            HideDeleteButton();
            _state = State.Idle;
            _active = false;
            _hoverDish = null;
        }

        private GpTable DiningTable => _world != null ? _world.AdjustTable : null;
        private DiningTableCoordinateMapper Mapper => _world != null && _world.AdjustTableView != null ? _world.AdjustTableView.Mapper : null;
        private Camera Cam => _world != null ? _world.AdjustCamera : null;

        private void Update()
        {
            if (!_active || _world == null || DiningTable == null || Mapper == null || Cam == null)
            {
                return;
            }

            switch (_state)
            {
                case State.Idle:
                    UpdateIdle();
                    break;
                case State.Moving:
                    UpdateMoving();
                    break;
                case State.Placed:
                    // 勾 / 撤销 由 WorldButtonView 自行响应点击，无需逐帧处理。
                    break;
            }
        }

        // —— 空闲态：悬浮显示删除 X + 点选进入移动 ——

        private void UpdateIdle()
        {
            Vector3 world = WorldInput.MouseWorld(Cam);
            DishInstance dish = null;
            if (!WorldInput.PointerOverUi)
            {
                GridPos cell = Mapper.NearestCell(world);
                if (DiningTable.InBounds(cell))
                {
                    dish = DiningTable.DishAt(cell);
                }
            }

            if (dish != _hoverDish)
            {
                _hoverDish = dish;
                if (dish != null)
                {
                    ShowDeleteButton(dish);
                }
                else
                {
                    HideDeleteButton();
                }
            }

            if (WorldInput.PrimaryPressedThisFrame)
            {
                // 命中 X 按钮时交给它删除，避免同帧又触发移动。
                if (_deleteButton != null && _deleteButton.gameObject.activeSelf && _deleteButton.ContainsWorldPoint(world))
                {
                    return;
                }

                if (dish != null)
                {
                    StartMove(dish);
                }
            }
        }

        private void ShowDeleteButton(DishInstance dish)
        {
            float size = ButtonSize;
            _deleteButton ??= CreateButton("FoodAdjustDelete");
            if (_deleteButton == null)
            {
                return;
            }

            _deleteButton.gameObject.SetActive(true);
            _deleteButton.Configure(new Vector2(size, size), "×", new Color(0.9f, 0.24f, 0.22f, 1f), () => DeleteDish(dish));
            PositionAtCellCorner(_deleteButton, TopRightCell(dish.OccupiedCells), size, Vector3.zero);
        }

        private void HideDeleteButton()
        {
            if (_deleteButton != null)
            {
                _deleteButton.gameObject.SetActive(false);
            }
        }

        private void DeleteDish(DishInstance dish)
        {
            if (dish == null || _world.AdjustRun == null)
            {
                return;
            }

            if (!_world.AdjustRun.TrySpendFoodAdjust())
            {
                _world.ShowMessage("没有可用的食物调整次数了。");
                return;
            }

            DiningTable.RemoveDish(dish);
            HideDeleteButton();
            _hoverDish = null;
            _world.RebuildAfterAdjust();
        }

        // —— 移动态：ghost + 箭头 + 光标菜品 ——

        private void StartMove(DishInstance dish)
        {
            _movingDish = dish;
            _originalPlacement = dish.Placement;
            _moveStartScreen = WorldInput.MouseScreen;
            _dragReleaseArmed = false;

            DiningTable.RemoveDish(dish);
            HideDeleteButton();
            _hoverDish = null;

            _ghostView = _world.GetPieceView(dish.Id);
            if (_ghostView != null)
            {
                _ghostView.SetGhost(true);
                _ghostView.SetClickEnabled(false);
            }

            _cursorView = CreateLoosePiece(dish);
            if (_cursorView == null)
            {
                AbortFailedMoveStart();
                return;
            }

            _arrow = WorldTargetArrow.Create(_world.ActiveTargetArrowPrefab, _world.AdjustPiecesRoot, Mapper.CellSize);
            if (_arrow == null)
            {
                Destroy(_cursorView.gameObject);
                _cursorView = null;
                AbortFailedMoveStart();
                return;
            }

            _state = State.Moving;
        }

        private void AbortFailedMoveStart()
        {
            RestoreMovingDishToOriginal();
            if (_ghostView != null)
            {
                _ghostView.SetGhost(false);
                _ghostView = null;
            }

            _movingDish = null;
            _state = State.Idle;
        }

        private void UpdateMoving()
        {
            DishShape shape = _movingDish.Placement.Orientation;
            Vector3 world = WorldInput.MouseWorld(Cam);
            GridPos origin = SnapOrigin(world, shape);
            bool canPlace = DiningTable.CanPlace(shape, origin);
            bool originalOrigin = IsOriginalOrigin(origin);

            if (_cursorView != null)
            {
                _cursorView.transform.localPosition = Mapper.CellCenterLocal(origin);
                _cursorView.SetPlacementGlow(true, canPlace && !originalOrigin);
            }

            if (_arrow != null)
            {
                Vector3 start = CellsCenterWorld(_movingDish.OccupiedCells);
                Vector3 end = OriginShapeCenterWorld(shape, origin);
                _arrow.SetEndpoints(start, end);
            }

            if (WorldInput.PrimaryHeld)
            {
                Vector2 delta = WorldInput.MouseScreen - _moveStartScreen;
                if (delta.sqrMagnitude >= DragStartScreenThreshold * DragStartScreenThreshold)
                {
                    _dragReleaseArmed = true;
                }
            }

            if (WorldInput.PrimaryReleasedThisFrame)
            {
                if (_dragReleaseArmed)
                {
                    TryPlaceOrCancel(shape, origin, canPlace);
                }

                return;
            }

            if (WorldInput.PrimaryPressedThisFrame)
            {
                TryPlaceOrCancel(shape, origin, canPlace);
            }
        }

        private void TryPlaceOrCancel(DishShape shape, GridPos origin, bool canPlace)
        {
            if (!canPlace || IsOriginalOrigin(origin))
            {
                CancelMoveToOriginal();
                return;
            }

            TentativePlace(shape, origin);
        }

        private void TentativePlace(DishShape shape, GridPos origin)
        {
            var placement = new Placement(shape, _movingDish.Placement.RotationIndex, origin);
            _movingDish.Relocate(placement);
            DiningTable.Place(_movingDish);

            if (_cursorView != null)
            {
                _cursorView.transform.localPosition = Mapper.CellCenterLocal(origin);
                _cursorView.SetPlacementGlow(false, false);
                _cursorView.SetGhost(false);
            }

            ShowConfirmUndoButtons();
            _state = State.Placed;
        }

        private void ShowConfirmUndoButtons()
        {
            float size = ButtonSize;
            GridPos topRight = TopRightCell(_movingDish.OccupiedCells);

            _confirmButton ??= CreateButton("FoodAdjustConfirm");
            if (_confirmButton == null)
            {
                return;
            }

            _confirmButton.gameObject.SetActive(true);
            _confirmButton.Configure(new Vector2(size, size), "✓", new Color(0.28f, 0.8f, 0.36f, 1f), ConfirmMove);
            PositionAtCellCorner(_confirmButton, topRight, size, Vector3.zero);

            _undoButton ??= CreateButton("FoodAdjustUndo");
            if (_undoButton == null)
            {
                return;
            }

            _undoButton.gameObject.SetActive(true);
            _undoButton.Configure(new Vector2(size, size), "↩", new Color(0.55f, 0.55f, 0.6f, 1f), UndoMove);
            PositionAtCellCorner(_undoButton, topRight, size, new Vector3(0f, -size * 1.15f, 0f));
        }

        private void ConfirmMove()
        {
            if (_world.AdjustRun == null || !_world.AdjustRun.TrySpendFoodAdjust())
            {
                _world.ShowMessage("没有可用的食物调整次数了。");
                return;
            }

            CleanupTransient();
            _movingDish = null;
            _state = State.Idle;
            _active = false;

            _world.RebuildAfterAdjust();
            _onExited?.Invoke();
        }

        private void UndoMove()
        {
            CancelMoveToOriginal();
        }

        /// <summary>把正在移动/暂放的菜品还原到原始摆放（餐桌状态复位）。</summary>
        private void RestoreMovingDishToOriginal()
        {
            if (_movingDish == null)
            {
                return;
            }

            DiningTable.RemoveDish(_movingDish);
            _movingDish.Relocate(_originalPlacement);
            DiningTable.Place(_movingDish);
        }

        private void CancelMoveToOriginal()
        {
            RestoreMovingDishToOriginal();
            CleanupTransient();

            if (_ghostView != null)
            {
                _ghostView.SetGhost(false);
                _ghostView = null;
            }

            _movingDish = null;
            _state = State.Idle;
            _world.RebuildAfterAdjust();
        }

        private void CleanupTransient()
        {
            if (_cursorView != null)
            {
                Destroy(_cursorView.gameObject);
                _cursorView = null;
            }

            _arrow?.Destroy();
            _arrow = null;

            if (_confirmButton != null)
            {
                _confirmButton.gameObject.SetActive(false);
            }

            if (_undoButton != null)
            {
                _undoButton.gameObject.SetActive(false);
            }
        }

        // —— 世界坐标与占格辅助 ——

        private GridPos SnapOrigin(Vector3 world, DishShape shape)
        {
            Transform root = Mapper.Root;
            float pitch = Mapper.Pitch;
            Vector3 local = root != null ? root.InverseTransformPoint(world) : world;
            local.x -= (shape.Width - 1) * pitch * 0.5f;
            local.y += (shape.Height - 1) * pitch * 0.5f;
            Vector3 shifted = root != null ? root.TransformPoint(local) : local;
            return Mapper.NearestCell(shifted);
        }

        private bool IsOriginalOrigin(GridPos origin)
        {
            return origin.Equals(_originalPlacement.Origin);
        }

        private Vector3 CellsCenterWorld(IReadOnlyList<GridPos> cells)
        {
            if (cells == null || cells.Count == 0)
            {
                return Vector3.zero;
            }

            Vector3 sum = Vector3.zero;
            foreach (GridPos cell in cells)
            {
                sum += Mapper.CellCenter(cell);
            }

            return sum / cells.Count;
        }

        private Vector3 OriginShapeCenterWorld(DishShape shape, GridPos origin)
        {
            if (shape == null || shape.CellCount == 0)
            {
                return Mapper.CellCenter(origin);
            }

            Vector3 sum = Vector3.zero;
            foreach (GridPos cell in shape.Cells)
            {
                sum += Mapper.CellCenter(cell.Offset(origin.X, origin.Y));
            }

            return sum / shape.CellCount;
        }

        private static GridPos TopRightCell(IReadOnlyList<GridPos> cells)
        {
            // 逻辑 Y 向下：「上」= Y 更小。右上角 = 最小 Y 那一行里 X 最大的占格。
            int minY = int.MaxValue;
            foreach (GridPos c in cells)
            {
                if (c.Y < minY)
                {
                    minY = c.Y;
                }
            }

            int maxX = int.MinValue;
            foreach (GridPos c in cells)
            {
                if (c.Y == minY && c.X > maxX)
                {
                    maxX = c.X;
                }
            }

            return new GridPos(maxX, minY);
        }

        private float ButtonSize => Mathf.Max(0.3f, Mapper.CellSize * 0.55f);

        private void PositionAtCellCorner(WorldButtonView button, GridPos cell, float size, Vector3 extraOffset)
        {
            Vector3 center = Mapper.CellCenter(cell);
            Vector3 pos = center + new Vector3(size * 0.5f, size * 0.5f, 0f) + extraOffset;
            pos.z = -0.1f;
            button.transform.position = pos;
        }

        private WorldButtonView CreateButton(string name)
        {
            WorldButtonView button;
            if (_world.AdjustWorldButtonPrefab != null)
            {
                button = Instantiate(_world.AdjustWorldButtonPrefab, _world.AdjustPiecesRoot);
            }
            else
            {
                Debug.LogError($"{nameof(FoodAdjustController)} 缺少 WorldButton prefab。");
                return null;
            }

            button.gameObject.name = name;
            return button;
        }

        private DishPieceView CreateLoosePiece(DishInstance dish)
        {
            DishPieceView piece;
            if (_world.AdjustDishPiecePrefab != null)
            {
                piece = Instantiate(_world.AdjustDishPiecePrefab, _world.AdjustPiecesRoot);
            }
            else
            {
                Debug.LogError($"{nameof(FoodAdjustController)} 缺少 DishPiece prefab。");
                return null;
            }

            piece.gameObject.name = "FoodAdjustCursorDish";
            piece.transform.localPosition = Mapper.CellCenterLocal(dish.Placement.Origin);
            piece.BuildPlaced(dish, _world.AdjustSpriteProvider.Get(dish.Def), Mapper.CellSize, Mapper.Pitch, null);
            piece.SetClickEnabled(false);
            return piece;
        }
    }
}
