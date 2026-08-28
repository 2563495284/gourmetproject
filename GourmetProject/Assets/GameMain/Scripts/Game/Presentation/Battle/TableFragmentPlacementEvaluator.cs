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
            Reset(origin, centerCell, placementStatus, feedback, currentBounds, projectedBounds);
        }

        public GridPos Origin { get; private set; }

        public GridPos CenterCell { get; private set; }

        public TableFragmentBuilder.FragmentPlacementStatus PlacementStatus { get; private set; }

        public GridPlacementFeedback Feedback { get; private set; }

        public TableFragmentBuilder.PlacementBounds CurrentBounds { get; private set; }

        /// <summary>当前餐桌与候选碎片合并后的预测包围盒。</summary>
        public TableFragmentBuilder.PlacementBounds ProjectedBounds { get; private set; }

        /// <summary>逻辑 Y 向下，因此预测最小 Y 变小表示餐桌向屏幕上方扩展。</summary>
        public bool ExtendsAboveExistingTop => ProjectedBounds.MinY < CurrentBounds.MinY;

        /// <summary>候选碎片让餐桌的任意一条外边界发生扩展。</summary>
        public bool ExpandsExistingBounds =>
            ProjectedBounds.MinX < CurrentBounds.MinX
            || ProjectedBounds.MinY < CurrentBounds.MinY
            || ProjectedBounds.MaxX > CurrentBounds.MaxX
            || ProjectedBounds.MaxY > CurrentBounds.MaxY;

        public bool CanCommit => PlacementStatus == TableFragmentBuilder.FragmentPlacementStatus.Valid;

        internal void Reset(
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
    }

    /// <summary>餐桌编辑拖拽期间复用的放置计算缓冲；单次交互只创建一份。</summary>
    internal sealed class TableFragmentPlacementEvaluationBuffer
    {
        private const int MaximumTableCells = 12 * 12;

        internal readonly HashSet<GridPos> ExistingCells =
            new HashSet<GridPos>(MaximumTableCells);
        internal readonly List<GridPos> LocalCells =
            new List<GridPos>(MaximumTableCells);
        internal readonly List<GridPlacementFeedbackCell> FeedbackCells =
            new List<GridPlacementFeedbackCell>(MaximumTableCells);
        internal readonly GridPlacementFeedback Feedback = new GridPlacementFeedback(
            default,
            GridPlacementFeedbackState.Blocked,
            Array.Empty<GridPlacementFeedbackCell>());
        internal readonly TableFragmentPlacementEvaluation Evaluation;

        internal TableFragmentPlacementEvaluationBuffer()
        {
            Evaluation = new TableFragmentPlacementEvaluation(
                default,
                default,
                TableFragmentBuilder.FragmentPlacementStatus.Detached,
                Feedback,
                default,
                default);
        }
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
            return Evaluate(
                table,
                mapper,
                fragment,
                visualCenterWorld,
                maxWidth,
                maxHeight,
                new TableFragmentPlacementEvaluationBuffer());
        }

        internal static TableFragmentPlacementEvaluation Evaluate(
            GpTable table,
            DiningTableCoordinateMapper mapper,
            TableFragmentDef fragment,
            Vector3 visualCenterWorld,
            int maxWidth,
            int maxHeight,
            TableFragmentPlacementEvaluationBuffer buffer)
        {
            if (mapper == null)
            {
                throw new ArgumentNullException(nameof(mapper));
            }

            ValidateInputs(table, fragment, buffer);
            TableFragmentBuilder.FillCells(fragment, buffer.LocalCells);
            GridPos centerCell = mapper.NearestCell(visualCenterWorld);
            GridPos origin = NearestOriginForOccupiedCellCenter(
                mapper,
                buffer.LocalCells,
                visualCenterWorld);
            return EvaluatePrepared(
                table,
                origin,
                centerCell,
                maxWidth,
                maxHeight,
                buffer);
        }

        public static TableFragmentPlacementEvaluation EvaluateAtOrigin(
            GpTable table,
            TableFragmentDef fragment,
            GridPos origin,
            GridPos centerCell,
            int maxWidth,
            int maxHeight)
        {
            var buffer = new TableFragmentPlacementEvaluationBuffer();
            ValidateInputs(table, fragment, buffer);
            TableFragmentBuilder.FillCells(fragment, buffer.LocalCells);
            return EvaluatePrepared(table, origin, centerCell, maxWidth, maxHeight, buffer);
        }

        private static TableFragmentPlacementEvaluation EvaluatePrepared(
            GpTable table,
            GridPos origin,
            GridPos centerCell,
            int maxWidth,
            int maxHeight,
            TableFragmentPlacementEvaluationBuffer buffer)
        {
            TableFragmentBuilder.FillExistingSet(table, buffer.ExistingCells);
            TableFragmentBuilder.FragmentPlacementStatus status =
                TableFragmentBuilder.GetPlacementStatusWithinMaxBounds(
                    buffer.ExistingCells,
                    buffer.LocalCells,
                    origin,
                    maxWidth,
                    maxHeight);
            BuildPlacementBounds(
                table,
                buffer.LocalCells,
                origin,
                out TableFragmentBuilder.PlacementBounds currentBounds,
                out TableFragmentBuilder.PlacementBounds projectedBounds);
            GridPlacementFeedbackState feedbackState = ToFeedbackState(status);
            buffer.FeedbackCells.Clear();
            foreach (GridPos local in buffer.LocalCells)
            {
                GridPos absolute = local.Offset(origin.X, origin.Y);
                buffer.FeedbackCells.Add(new GridPlacementFeedbackCell(
                    absolute,
                    IsCellBlocked(
                        buffer.ExistingCells,
                        absolute,
                        currentBounds,
                        maxWidth,
                        maxHeight)
                        ? GridPlacementFeedbackState.Blocked
                        : GridPlacementFeedbackState.Valid));
            }

            buffer.Feedback.Reset(centerCell, feedbackState, buffer.FeedbackCells);
            buffer.Evaluation.Reset(
                origin,
                centerCell,
                status,
                buffer.Feedback,
                currentBounds,
                projectedBounds);
            return buffer.Evaluation;
        }

        private static void ValidateInputs(
            GpTable table,
            TableFragmentDef fragment,
            TableFragmentPlacementEvaluationBuffer buffer)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            if (fragment == null)
            {
                throw new ArgumentNullException(nameof(fragment));
            }

            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
        }

        private static bool IsCellBlocked(
            HashSet<GridPos> existing,
            GridPos candidate,
            TableFragmentBuilder.PlacementBounds currentBounds,
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

            int projectedMinX = Math.Min(currentBounds.MinX, candidate.X);
            int projectedMinY = Math.Min(currentBounds.MinY, candidate.Y);
            int projectedMaxX = Math.Max(currentBounds.MaxX, candidate.X);
            int projectedMaxY = Math.Max(currentBounds.MaxY, candidate.Y);
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
            for (int i = 0; i < localCells.Count; i++)
            {
                GridPos local = localCells[i];
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
            IReadOnlyList<GridPos> cells,
            Vector3 centerWorld)
        {
            Vector3 localCenter = mapper.Root != null
                ? mapper.Root.InverseTransformPoint(centerWorld)
                : centerWorld;
            Vector3 originCenterLocal = localCenter - OccupiedCellCenterOffsetLocal(cells, mapper.Pitch);
            Vector3 originCenterWorld = mapper.Root != null
                ? mapper.Root.TransformPoint(originCenterLocal)
                : originCenterLocal;
            return mapper.NearestCell(originCenterWorld);
        }

        private static Vector3 OccupiedCellCenterOffsetLocal(
            IReadOnlyList<GridPos> cells,
            float pitch)
        {
            if (cells == null || cells.Count == 0)
            {
                return Vector3.zero;
            }

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < cells.Count; i++)
            {
                GridPos cell = cells[i];
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
