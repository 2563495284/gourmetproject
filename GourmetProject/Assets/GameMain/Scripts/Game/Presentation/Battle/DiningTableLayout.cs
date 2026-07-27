using UnityEngine;
using GourmetProject.Gameplay.Board;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>一次餐桌居中定位的结果：自适应单格世界尺寸 + 餐桌根世界坐标。</summary>
    public readonly struct BoardPlacement
    {
        public readonly float CellSize;
        public readonly Vector3 Position;

        public BoardPlacement(float cellSize, Vector3 position)
        {
            CellSize = cellSize;
            Position = position;
        }
    }

    /// <summary>
    /// 餐桌在世界空间的居中定位计算（Food 态与餐桌编辑/餐桌视图态共用）。
    /// 只按「实际存在的格子」(胃) 求包围盒铺满可用区并居中，不论胃多大、落在 8×8 哪个角都居中。
    /// </summary>
    public static class DiningTableLayout
    {
        public const float Gap = 0f;
        public const float MaxCellSize = 1.2f;
        public const float MinCellSize = 0.42f;

        // 回退视口半宽/半高（16:9 参考：orthographicSize 5.4）。
        public const float FallbackHalfW = 9.6f;
        public const float FallbackHalfH = 5.4f;

        // 左右各让出 2.6 给常驻 HUD 左右栏，顶部让出 1.7；底部边距按态传入（Food 略高、编辑态要给候选托盘条留位）。
        private const float SideMargin = 2.6f;
        private const float TopMargin = 1.7f;

        /// <summary>按正交相机求视口半宽/半高，无有效相机时回退到 16:9 参考值。</summary>
        public static void ResolveViewport(Camera camera, out float halfW, out float halfH)
        {
            if (camera != null && camera.orthographic && camera.orthographicSize > 0f)
            {
                halfH = camera.orthographicSize;
                halfW = camera.orthographicSize * camera.aspect;
            }
            else
            {
                halfH = FallbackHalfH;
                halfW = FallbackHalfW;
            }
        }

        /// <summary>按视口半宽/半高与胃包围盒，算出铺满可用区且居中的单格尺寸与餐桌根位置。</summary>
        public static BoardPlacement Compute(float halfW, float halfH, GpTable board, float bottomMargin)
        {
            float boardLeft = -halfW + SideMargin;
            float boardRight = halfW - SideMargin;
            float boardTop = halfH - TopMargin;
            float boardBottom = -halfH + bottomMargin;
            return ComputeInRect(boardLeft, boardRight, boardBottom, boardTop, board, MinCellSize);
        }

        /// <summary>按预测存在格包围盒计算餐桌布局，不必先构造一张临时餐桌。</summary>
        public static BoardPlacement ComputeForBounds(
            float halfW,
            float halfH,
            int boardWidth,
            int boardHeight,
            TableFragmentBuilder.PlacementBounds bounds,
            float bottomMargin)
        {
            float boardLeft = -halfW + SideMargin;
            float boardRight = halfW - SideMargin;
            float boardTop = halfH - TopMargin;
            float boardBottom = -halfH + bottomMargin;
            return ComputeInRectForBounds(
                boardLeft,
                boardRight,
                boardBottom,
                boardTop,
                boardWidth,
                boardHeight,
                bounds,
                MinCellSize);
        }

        /// <summary>
        /// 在给定「世界矩形可用区」内把胃包围盒铺满并居中（用于把餐桌锁定在屏幕固定区域）。
        /// <paramref name="minCellSize"/> 传更小或 0 可让超大餐桌继续缩放以完整显示。
        /// </summary>
        public static BoardPlacement ComputeInRect(
            float boardLeft,
            float boardRight,
            float boardBottom,
            float boardTop,
            GpTable board,
            float minCellSize)
        {
            if (!board.TryGetExistingBounds(out int minX, out int minY, out int maxX, out int maxY))
            {
                minX = minY = 0;
                maxX = board.Width - 1;
                maxY = board.Height - 1;
            }

            return ComputeInRectForBounds(
                boardLeft,
                boardRight,
                boardBottom,
                boardTop,
                board.Width,
                board.Height,
                new TableFragmentBuilder.PlacementBounds(minX, minY, maxX, maxY),
                minCellSize);
        }

        public static BoardPlacement ComputeInRectForBounds(
            float boardLeft,
            float boardRight,
            float boardBottom,
            float boardTop,
            int boardWidth,
            int boardHeight,
            TableFragmentBuilder.PlacementBounds bounds,
            float minCellSize)
        {
            float availW = Mathf.Max(1f, boardRight - boardLeft);
            float availH = Mathf.Max(1f, boardTop - boardBottom);
            int minX = bounds.MinX;
            int minY = bounds.MinY;
            int maxX = bounds.MaxX;
            int maxY = bounds.MaxY;
            int boxW = Mathf.Max(1, maxX - minX + 1);
            int boxH = Mathf.Max(1, maxY - minY + 1);
            float lowerBound = Mathf.Min(minCellSize, MaxCellSize);
            float cellSize = Mathf.Clamp(Mathf.Min(availW / boxW, availH / boxH), lowerBound, MaxCellSize);

            // mapper 仍按完整 Width×Height 排布；这里反推 Position，使胃包围盒的几何中心落在可用区中心。
            var areaCenter = new Vector3((boardLeft + boardRight) * 0.5f, (boardTop + boardBottom) * 0.5f, 0f);
            float pitch = cellSize + Gap;
            float fullWorldWidth = boardWidth * cellSize + Mathf.Max(0, boardWidth - 1) * Gap;
            float fullWorldHeight = boardHeight * cellSize + Mathf.Max(0, boardHeight - 1) * Gap;
            float boxCenterIndexX = (minX + maxX) * 0.5f;
            float boxCenterIndexY = (minY + maxY) * 0.5f;
            var position = new Vector3(
                areaCenter.x + fullWorldWidth * 0.5f - boxCenterIndexX * pitch - cellSize * 0.5f,
                areaCenter.y - fullWorldHeight * 0.5f + boxCenterIndexY * pitch + cellSize * 0.5f,
                0f);

            return new BoardPlacement(cellSize, position);
        }
    }
}
