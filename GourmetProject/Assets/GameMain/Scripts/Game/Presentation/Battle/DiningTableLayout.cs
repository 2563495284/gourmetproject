using System.Collections.Generic;
using UnityEngine;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
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
    /// 默认按「实际存在的格子」(胃) 求包围盒；Boss 演出可额外传入持续显示的移除格，
    /// 使视觉上的整张餐桌都会铺满可用区并居中。
    /// </summary>
    public static class DiningTableLayout
    {
        public const float Gap = 0f;
        public const float MaxCellSize = 1.2f;
        public const float MinCellSize = 0.42f;
        /// <summary>4×4 胃时的默认单格世界尺寸，也是出餐口/临时桌食物的单格大小。</summary>
        public const float DefaultFoodCellSize = MaxCellSize;
        /// <summary>无相机时的画布换算：16:9 参考视口全宽 19.2 ↔ 画布 1920。</summary>
        public const float FallbackCanvasPixelsPerWorldUnit = 100f;

        // 回退视口半宽/半高（16:9 参考：orthographicSize 5.4）。
        public const float FallbackHalfW = 9.6f;
        public const float FallbackHalfH = 5.4f;

        // 左右各让出 2.6 给常驻 HUD 左右栏，顶部让出 1.7；底部边距按态传入（Food 略高、编辑态要给候选托盘条留位）。
        private const float SideMargin = 2.6f;
        private const float TopMargin = 1.7f;

        /// <summary>
        /// 餐桌附属世界表现的统一缩放比例。最大单格尺寸为 1，超大餐桌随单格尺寸同比缩小。
        /// </summary>
        public static float VisualScaleForCellSize(float cellSize)
        {
            return Mathf.Clamp01(cellSize / MaxCellSize);
        }

        public static Vector2Int FoodGridSize(int width, int height)
        {
            return new Vector2Int(Mathf.Max(1, width), Mathf.Max(1, height));
        }

        /// <summary>
        /// 把世界尺寸换成当前 Canvas 的本地像素。正交相机按 Screen.height / 视口高度换算，
        /// 再除以 Canvas.scaleFactor；无相机时回退到 100 像素/世界单位。
        /// </summary>
        public static Vector2 CanvasPixelsForWorldSize(
            Vector2 worldSize,
            Camera worldCamera,
            Canvas canvas)
        {
            float pixelsPerWorld = FallbackCanvasPixelsPerWorldUnit;
            if (worldCamera != null && worldCamera.orthographic && worldCamera.orthographicSize > 0.01f)
            {
                pixelsPerWorld = Screen.height / (worldCamera.orthographicSize * 2f);
            }

            float canvasScale = canvas != null && canvas.scaleFactor > 0.01f
                ? canvas.scaleFactor
                : 1f;
            return worldSize * (pixelsPerWorld / canvasScale);
        }

        public static Vector2 DefaultFoodCanvasSize(Vector2Int grid, Vector2 defaultCellCanvasSize)
        {
            Vector2Int safeGrid = FoodGridSize(grid.x, grid.y);
            return new Vector2(
                defaultCellCanvasSize.x * safeGrid.x,
                defaultCellCanvasSize.y * safeGrid.y);
        }

        /// <summary>按格封顶：整盘可以按 footprint 变大，但单格不超过 4×4 默认格。</summary>
        public static Vector2 CapToDefaultFoodCanvasSize(
            Vector2 canvasSize,
            Vector2Int grid,
            Vector2 defaultCellCanvasSize)
        {
            Vector2 maxSize = DefaultFoodCanvasSize(grid, defaultCellCanvasSize);
            return FitInside(canvasSize, maxSize);
        }

        /// <summary>
        /// 把抓取到的画布尺寸从源格子 / 拖拽放大，换算成餐桌落地时的实际大小。
        /// </summary>
        public static Vector2 CanvasSizeForTableFood(
            Vector2 capturedCanvasSize,
            float sourceCellSize,
            float tableCellSize,
            float visualScale = 1f)
        {
            float source = Mathf.Max(0.0001f, sourceCellSize)
                * Mathf.Max(0.0001f, visualScale);
            float table = Mathf.Max(0.0001f, tableCellSize);
            return capturedCanvasSize * (table / source);
        }

        /// <summary>
        /// 把已按目标格重建的食物，缩回与源格相同的世界大小。
        /// 麻风味飞入临时桌前先 Rebuild 成默认格，再用此比例保住桌上观感，随后 tween 到目标缩放。
        /// </summary>
        public static float ScaleToMatchSourceCell(float sourceCellSize, float destinationCellSize)
        {
            return Mathf.Max(0.0001f, sourceCellSize) / Mathf.Max(0.0001f, destinationCellSize);
        }

        /// <summary>只缩小、不放大，把尺寸限制在父框内。</summary>
        public static Vector2 FitInside(Vector2 size, Vector2 parentSize)
        {
            if (parentSize.x <= 0.01f || parentSize.y <= 0.01f)
            {
                return size;
            }

            float fit = Mathf.Min(
                1f,
                parentSize.x / Mathf.Max(0.0001f, size.x),
                parentSize.y / Mathf.Max(0.0001f, size.y));
            return size * Mathf.Max(0.0001f, fit);
        }

        public static Vector2 CapToDefaultFoodAndFitParent(
            Vector2 canvasSize,
            Vector2Int grid,
            Vector2 defaultCellCanvasSize,
            Vector2 parentSize)
        {
            return FitInside(
                CapToDefaultFoodCanvasSize(canvasSize, grid, defaultCellCanvasSize),
                parentSize);
        }

        public static Vector2 RectSize(RectTransform rect)
        {
            if (rect == null)
            {
                return Vector2.zero;
            }

            Vector2 size = rect.rect.size;
            if (size.x > 0.01f && size.y > 0.01f)
            {
                return size;
            }

            RectTransform current = rect;
            while (current != null)
            {
                Vector2 delta = current.sizeDelta;
                if (delta.x > 0.01f && delta.y > 0.01f)
                {
                    return new Vector2(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
                }

                current = current.parent as RectTransform;
            }

            return Vector2.zero;
        }

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
        public static BoardPlacement Compute(
            float halfW,
            float halfH,
            GpTable board,
            float bottomMargin,
            IEnumerable<GridPos> additionalVisibleCells = null)
        {
            float boardLeft = -halfW + SideMargin;
            float boardRight = halfW - SideMargin;
            float boardTop = halfH - TopMargin;
            float boardBottom = -halfH + bottomMargin;
            return ComputeInRect(
                boardLeft,
                boardRight,
                boardBottom,
                boardTop,
                board,
                MinCellSize,
                additionalVisibleCells);
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
            float minCellSize,
            IEnumerable<GridPos> additionalVisibleCells = null)
        {
            TableFragmentBuilder.PlacementBounds bounds = VisibleBounds(
                board,
                additionalVisibleCells);

            return ComputeInRectForBounds(
                boardLeft,
                boardRight,
                boardBottom,
                boardTop,
                board.Width,
                board.Height,
                bounds,
                minCellSize);
        }

        /// <summary>
        /// 求餐桌的视觉包围盒。除玩法上的存在格外，还包含 Boss 演出后仍保留在桌面上的移除格。
        /// </summary>
        public static TableFragmentBuilder.PlacementBounds VisibleBounds(
            GpTable board,
            IEnumerable<GridPos> additionalVisibleCells = null)
        {
            bool hasBounds = board.TryGetExistingBounds(
                out int minX,
                out int minY,
                out int maxX,
                out int maxY);

            if (additionalVisibleCells != null)
            {
                foreach (GridPos cell in additionalVisibleCells)
                {
                    if (!board.InBounds(cell))
                    {
                        continue;
                    }

                    if (!hasBounds)
                    {
                        minX = maxX = cell.X;
                        minY = maxY = cell.Y;
                        hasBounds = true;
                        continue;
                    }

                    minX = Mathf.Min(minX, cell.X);
                    minY = Mathf.Min(minY, cell.Y);
                    maxX = Mathf.Max(maxX, cell.X);
                    maxY = Mathf.Max(maxY, cell.Y);
                }
            }

            if (!hasBounds)
            {
                minX = minY = 0;
                maxX = board.Width - 1;
                maxY = board.Height - 1;
            }

            return new TableFragmentBuilder.PlacementBounds(minX, minY, maxX, maxY);
        }

        public static BoardPlacement ComputeInRectForBounds(
            float boardLeft,
            float boardRight,
            float boardBottom,
            float boardTop,
            int boardWidth,
            int boardHeight,
            TableFragmentBuilder.PlacementBounds bounds,
            float minCellSize,
            float maxCellSize = MaxCellSize)
        {
            float availW = Mathf.Max(1f, boardRight - boardLeft);
            float availH = Mathf.Max(1f, boardTop - boardBottom);
            int minX = bounds.MinX;
            int minY = bounds.MinY;
            int maxX = bounds.MaxX;
            int maxY = bounds.MaxY;
            int boxW = Mathf.Max(1, maxX - minX + 1);
            int boxH = Mathf.Max(1, maxY - minY + 1);
            float upperBound = Mathf.Max(minCellSize, maxCellSize);
            float lowerBound = Mathf.Min(minCellSize, upperBound);
            float cellSize = Mathf.Clamp(Mathf.Min(availW / boxW, availH / boxH), lowerBound, upperBound);

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
