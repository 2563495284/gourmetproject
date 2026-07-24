using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Game.Presentation.Battle
{
    public enum DishDragCellState
    {
        Valid,
        Missing,
        Blocked,
    }

    public readonly struct DishDragCellFeedback
    {
        public DishDragCellFeedback(GridPos position, DishDragCellState state)
        {
            Position = position;
            State = state;
        }

        public GridPos Position { get; }

        public DishDragCellState State { get; }
    }

    public sealed class DishDragPlacementResult
    {
        public DishDragPlacementResult(
            Placement placement,
            GridPos centerCell,
            DishDragCellState overallState,
            IReadOnlyList<DishDragCellFeedback> cells)
        {
            Placement = placement;
            CenterCell = centerCell;
            OverallState = overallState;
            Cells = cells ?? Array.Empty<DishDragCellFeedback>();
        }

        public Placement Placement { get; }

        public GridPos Origin => Placement.Origin;

        public GridPos CenterCell { get; }

        public DishDragCellState OverallState { get; }

        public IReadOnlyList<DishDragCellFeedback> Cells { get; }

        public bool CanCommit => OverallState == DishDragCellState.Valid;
    }

    /// <summary>
    /// 把始终跟随鼠标的食物视觉重心映射到无限延伸的餐桌网格。
    /// 映射只产生候选位置和反馈，不会移动拖拽物体。
    /// </summary>
    public static class DishDragPlacementEvaluator
    {
        public static DishDragPlacementResult Evaluate(
            GpTable table,
            DiningTableCoordinateMapper mapper,
            DishShape shape,
            int rotationIndex,
            Vector3 visualCenterWorld)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            if (mapper == null)
            {
                throw new ArgumentNullException(nameof(mapper));
            }

            if (shape == null)
            {
                throw new ArgumentNullException(nameof(shape));
            }

            GridPos centerCell = mapper.NearestCell(visualCenterWorld);
            GridPos origin = NearestOriginForOccupiedCellCenter(mapper, shape, visualCenterWorld);
            var placement = new Placement(shape, rotationIndex, origin);
            var cells = new List<DishDragCellFeedback>(shape.CellCount);
            bool hasMissing = false;
            bool hasBlocked = false;

            foreach (GridPos relative in shape.Cells)
            {
                GridPos absolute = relative.Offset(origin.X, origin.Y);
                DishDragCellState state;
                if (!table.Exists(absolute))
                {
                    state = DishDragCellState.Missing;
                    hasMissing = true;
                }
                else if (table.IsDisabled(absolute) || table.DishAt(absolute) != null)
                {
                    state = DishDragCellState.Blocked;
                    hasBlocked = true;
                }
                else
                {
                    state = DishDragCellState.Valid;
                }

                cells.Add(new DishDragCellFeedback(absolute, state));
            }

            DishDragCellState overall = hasBlocked
                ? DishDragCellState.Blocked
                : hasMissing
                    ? DishDragCellState.Missing
                    : DishDragCellState.Valid;
            return new DishDragPlacementResult(placement, centerCell, overall, cells);
        }

        private static GridPos NearestOriginForOccupiedCellCenter(
            DiningTableCoordinateMapper mapper,
            DishShape shape,
            Vector3 centerWorld)
        {
            Vector3 localCenter = mapper.Root != null
                ? mapper.Root.InverseTransformPoint(centerWorld)
                : centerWorld;
            Vector3 originCenterLocal = localCenter - OccupiedCellCenterOffsetLocal(shape, mapper.Pitch);
            Vector3 originCenterWorld = mapper.Root != null
                ? mapper.Root.TransformPoint(originCenterLocal)
                : originCenterLocal;
            return mapper.NearestCell(originCenterWorld);
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
    }
}
