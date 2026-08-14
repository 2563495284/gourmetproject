using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Game.Presentation.Battle
{
    public sealed class TableFragmentPlacementEvaluation
    {
        public TableFragmentPlacementEvaluation(
            GridPos origin,
            GridPos centerCell,
            TableFragmentBuilder.FragmentPlacementStatus placementStatus,
            GridPlacementFeedback feedback,
            TableFragmentBuilder.PlacementBounds currentBounds,
            TableFragmentBuilder.PlacementBounds projectedBounds)
        {
            Origin = origin;
            CenterCell = centerCell;
            PlacementStatus = placementStatus;
            Feedback = feedback;
            CurrentBounds = currentBounds;
            ProjectedBounds = projectedBounds;
        }

        public GridPos Origin { get; }

        public GridPos CenterCell { get; }

        public TableFragmentBuilder.FragmentPlacementStatus PlacementStatus { get; }

        public GridPlacementFeedback Feedback { get; }

        public TableFragmentBuilder.PlacementBounds CurrentBounds { get; }

        /// <summary>当前餐桌与候选碎片合并后的预测包围盒。</summary>
        public TableFragmentBuilder.PlacementBounds ProjectedBounds { get; }

        /// <summary>逻辑 Y 向下，因此预测最小 Y 变小表示餐桌向屏幕上方扩展。</summary>
        public bool ExtendsAboveExistingTop => ProjectedBounds.MinY < CurrentBounds.MinY;

        /// <summary>候选碎片让餐桌的任意一条外边界发生扩展。</summary>
        public bool ExpandsExistingBounds =>
            ProjectedBounds.MinX < CurrentBounds.MinX
            || ProjectedBounds.MinY < CurrentBounds.MinY
            || ProjectedBounds.MaxX > CurrentBounds.MaxX
            || ProjectedBounds.MaxY > CurrentBounds.MaxY;

        public bool CanCommit => PlacementStatus == TableFragmentBuilder.FragmentPlacementStatus.Valid;
    }

    /// <summary>
    /// 把始终跟随鼠标的碎片视觉重心映射到餐桌网格。只计算落点和反馈，不移动任何表现对象。
    /// </summary>
    public static class TableFragmentPlacementEvaluator
    {
        public static TableFragmentPlacementEvaluation Evaluate(
            GpTable table,
            DiningTableCoordinateMapper mapper,
            TableFragmentDef fragment,
            Vector3 visualCenterWorld,
            int maxWidth,
            int maxHeight)
        {
            if (mapper == null)
            {
                throw new ArgumentNullException(nameof(mapper));
            }

            GridPos centerCell = mapper.NearestCell(visualCenterWorld);
            GridPos origin = NearestOriginForOccupiedCellCenter(mapper, fragment, visualCenterWorld);
            return EvaluateAtOrigin(table, fragment, origin, centerCell, maxWidth, maxHeight);
        }

        public static TableFragmentPlacementEvaluation EvaluateAtOrigin(
            GpTable table,
            TableFragmentDef fragment,
            GridPos origin,
            GridPos centerCell,
            int maxWidth,
            int maxHeight)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            if (fragment == null)
            {
                throw new ArgumentNullException(nameof(fragment));
            }

            TableFragmentBuilder.FragmentPlacementStatus status =
                TableFragmentBuilder.GetFragmentPlacementStatusWithinMaxBounds(
                    TableFragmentBuilder.ToExistingSet(table),
                    fragment,
                    origin,
                    maxWidth,
                    maxHeight);
            List<GridPos> localCells = TableFragmentBuilder.FilledCells(fragment);
            HashSet<GridPos> existing = TableFragmentBuilder.ToExistingSet(table);
            GridPlacementFeedbackState feedbackState = ToFeedbackState(status);
            var cells = new List<GridPlacementFeedbackCell>(localCells.Count);
            foreach (GridPos local in localCells)
            {
                GridPos absolute = local.Offset(origin.X, origin.Y);
                cells.Add(new GridPlacementFeedbackCell(
                    absolute,
                    IsCellBlocked(existing, absolute, maxWidth, maxHeight)
                        ? GridPlacementFeedbackState.Blocked
                        : GridPlacementFeedbackState.Valid));
            }

            BuildPlacementBounds(table, localCells, origin, out TableFragmentBuilder.PlacementBounds currentBounds, out TableFragmentBuilder.PlacementBounds projectedBounds);
            return new TableFragmentPlacementEvaluation(
                origin,
                centerCell,
                status,
                new GridPlacementFeedback(centerCell, feedbackState, cells),
                currentBounds,
                projectedBounds);
        }

        private static bool IsCellBlocked(
            HashSet<GridPos> existing,
            GridPos candidate,
            int maxWidth,
            int maxHeight)
        {
            if (existing == null || existing.Count == 0 || maxWidth <= 0 || maxHeight <= 0)
            {
                return true;
            }

            if (existing.Contains(candidate))
            {
                return true;
            }

            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;
            foreach (GridPos cell in existing)
            {
                minX = Math.Min(minX, cell.X);
                minY = Math.Min(minY, cell.Y);
                maxX = Math.Max(maxX, cell.X);
                maxY = Math.Max(maxY, cell.Y);
            }

            int projectedMinX = Math.Min(minX, candidate.X);
            int projectedMinY = Math.Min(minY, candidate.Y);
            int projectedMaxX = Math.Max(maxX, candidate.X);
            int projectedMaxY = Math.Max(maxY, candidate.Y);
            return projectedMaxX - projectedMinX + 1 > maxWidth
                || projectedMaxY - projectedMinY + 1 > maxHeight;
        }

        public static bool ExtendsAboveExistingTop(GpTable table, TableFragmentDef fragment, GridPos origin)
        {
            if (table == null || fragment == null)
            {
                return false;
            }

            List<GridPos> cells = TableFragmentBuilder.FilledCells(fragment);
            if (cells.Count == 0)
            {
                return false;
            }

            BuildPlacementBounds(table, cells, origin, out TableFragmentBuilder.PlacementBounds currentBounds, out TableFragmentBuilder.PlacementBounds projectedBounds);
            return projectedBounds.MinY < currentBounds.MinY;
        }

        private static void BuildPlacementBounds(
            GpTable table,
            IReadOnlyList<GridPos> localCells,
            GridPos origin,
            out TableFragmentBuilder.PlacementBounds currentBounds,
            out TableFragmentBuilder.PlacementBounds projectedBounds)
        {
            if (!table.TryGetExistingBounds(out int minX, out int minY, out int maxX, out int maxY))
            {
                minX = minY = 0;
                maxX = Mathf.Max(0, table.Width - 1);
                maxY = Mathf.Max(0, table.Height - 1);
            }

            currentBounds = new TableFragmentBuilder.PlacementBounds(minX, minY, maxX, maxY);
            foreach (GridPos local in localCells)
            {
                GridPos cell = local.Offset(origin.X, origin.Y);
                minX = Mathf.Min(minX, cell.X);
                minY = Mathf.Min(minY, cell.Y);
                maxX = Mathf.Max(maxX, cell.X);
                maxY = Mathf.Max(maxY, cell.Y);
            }

            projectedBounds = new TableFragmentBuilder.PlacementBounds(minX, minY, maxX, maxY);
        }

        private static GridPos NearestOriginForOccupiedCellCenter(
            DiningTableCoordinateMapper mapper,
            TableFragmentDef fragment,
            Vector3 centerWorld)
        {
            Vector3 localCenter = mapper.Root != null
                ? mapper.Root.InverseTransformPoint(centerWorld)
                : centerWorld;
            Vector3 originCenterLocal = localCenter - OccupiedCellCenterOffsetLocal(fragment, mapper.Pitch);
            Vector3 originCenterWorld = mapper.Root != null
                ? mapper.Root.TransformPoint(originCenterLocal)
                : originCenterLocal;
            return mapper.NearestCell(originCenterWorld);
        }

        private static Vector3 OccupiedCellCenterOffsetLocal(TableFragmentDef fragment, float pitch)
        {
            List<GridPos> cells = TableFragmentBuilder.FilledCells(fragment);
            if (cells.Count == 0)
            {
                return Vector3.zero;
            }

            Vector3 sum = Vector3.zero;
            foreach (GridPos cell in cells)
            {
                sum += new Vector3(cell.X * pitch, -cell.Y * pitch, 0f);
            }

            return sum / cells.Count;
        }

        private static GridPlacementFeedbackState ToFeedbackState(
            TableFragmentBuilder.FragmentPlacementStatus status)
        {
            return status switch
            {
                TableFragmentBuilder.FragmentPlacementStatus.Valid => GridPlacementFeedbackState.Valid,
                TableFragmentBuilder.FragmentPlacementStatus.Detached => GridPlacementFeedbackState.Missing,
                _ => GridPlacementFeedbackState.Blocked,
            };
        }
    }
}
