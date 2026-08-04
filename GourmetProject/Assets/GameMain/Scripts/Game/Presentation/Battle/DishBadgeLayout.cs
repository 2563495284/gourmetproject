using System.Collections.Generic;
using GourmetProject.Gameplay.Model;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    internal static class DishBadgeLayout
    {
        internal readonly struct GridRun
        {
            public GridRun(int row, int startX, int endX)
            {
                Row = row;
                StartX = startX;
                EndX = endX;
            }

            public int Row { get; }

            public int StartX { get; }

            public int EndX { get; }

            public float CenterX => (StartX + EndX) * 0.5f;
        }

        public static GridRun FindTopContinuousRun(IReadOnlyList<GridPos> cells)
        {
            if (cells == null || cells.Count == 0)
            {
                return new GridRun(0, 0, 0);
            }

            int topRow = int.MaxValue;
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            for (int i = 0; i < cells.Count; i++)
            {
                GridPos cell = cells[i];
                if (cell.Y < topRow)
                {
                    topRow = cell.Y;
                    minX = maxX = cell.X;
                }
                else if (cell.Y == topRow)
                {
                    minX = Mathf.Min(minX, cell.X);
                    maxX = Mathf.Max(maxX, cell.X);
                }
            }

            int bestStart = minX;
            int bestEnd = minX;
            int currentStart = int.MinValue;
            int currentEnd = int.MinValue;
            for (int x = minX; x <= maxX; x++)
            {
                if (Contains(cells, x, topRow))
                {
                    if (currentStart == int.MinValue)
                    {
                        currentStart = x;
                    }

                    currentEnd = x;
                    continue;
                }

                if (currentStart != int.MinValue
                    && currentEnd - currentStart > bestEnd - bestStart)
                {
                    bestStart = currentStart;
                    bestEnd = currentEnd;
                }

                currentStart = int.MinValue;
                currentEnd = int.MinValue;
            }

            if (currentStart != int.MinValue
                && currentEnd - currentStart > bestEnd - bestStart)
            {
                bestStart = currentStart;
                bestEnd = currentEnd;
            }

            return new GridRun(topRow, bestStart, bestEnd);
        }

        /// <summary>
        /// 以左上角占用格的中心为原点，统一计算 Badge 中心位置。
        /// Badge 的上边缘始终与食物最上方连续格的上边缘对齐。
        /// </summary>
        public static Vector3 PositionFromTopLeftCellOrigin(
            DishShape shape,
            float cellSize,
            float pitch,
            float scaledBadgeTopExtent)
        {
            if (shape == null)
            {
                return Vector3.zero;
            }

            GridRun run = FindTopContinuousRun(shape.Cells);
            return new Vector3(
                run.CenterX * pitch,
                -run.Row * pitch
                + cellSize * 0.5f
                - Mathf.Abs(scaledBadgeTopExtent),
                0f);
        }

        /// <summary>
        /// 以整个食物包围盒中心为原点，使用与世界食物完全相同的 Badge 锚点。
        /// </summary>
        public static Vector3 PositionFromShapeCenter(
            DishShape shape,
            float cellSize,
            float pitch,
            float scaledBadgeTopExtent)
        {
            if (shape == null)
            {
                return Vector3.zero;
            }

            Vector3 position = PositionFromTopLeftCellOrigin(
                shape,
                cellSize,
                pitch,
                scaledBadgeTopExtent);
            position.x -= (shape.Width - 1) * pitch * 0.5f;
            position.y += (shape.Height - 1) * pitch * 0.5f;
            return position;
        }

        private static bool Contains(
            IReadOnlyList<GridPos> cells,
            int x,
            int y)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].X == x && cells[i].Y == y)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
