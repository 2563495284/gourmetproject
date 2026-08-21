using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>结算前餐桌从休息框放大到结算框时的目标位姿。缩放为结算格尺寸除以构建格尺寸。</summary>
    public readonly struct FoodSettlementBoardTween
    {
        public FoodSettlementBoardTween(Vector3 position, float scale, float cellSize)
        {
            Position = position;
            Scale = scale;
            CellSize = cellSize;
        }

        public Vector3 Position { get; }

        public float Scale { get; }

        public float CellSize { get; }
    }

    /// <summary>
    /// 美食战斗结算布局：按休息框构建的格尺寸，计算铺满结算框所需的根位移与缩放。
    /// 缩放后的世界格尺寸不超过默认格大小 <see cref="DiningTableLayout.MaxCellSize"/>，并据此反推缩放上限。
    /// </summary>
    public static class FoodSettlementLayout
    {
        public static FoodSettlementBoardTween ComputeBoardTween(
            float restLeft,
            float restRight,
            float restBottom,
            float restTop,
            float settlementLeft,
            float settlementRight,
            float settlementBottom,
            float settlementTop,
            GpTable board,
            float builtCellSize,
            IEnumerable<GridPos> additionalVisibleCells = null)
        {
            if (board == null)
            {
                return Identity(builtCellSize);
            }

            return ComputeBoardTween(
                restLeft,
                restRight,
                restBottom,
                restTop,
                settlementLeft,
                settlementRight,
                settlementBottom,
                settlementTop,
                board.Width,
                board.Height,
                DiningTableLayout.VisibleBounds(board, additionalVisibleCells),
                builtCellSize);
        }

        public static FoodSettlementBoardTween ComputeBoardTween(
            float restLeft,
            float restRight,
            float restBottom,
            float restTop,
            float settlementLeft,
            float settlementRight,
            float settlementBottom,
            float settlementTop,
            int boardWidth,
            int boardHeight,
            TableFragmentBuilder.PlacementBounds bounds,
            float builtCellSize)
        {
            _ = restLeft;
            _ = restRight;
            _ = restBottom;
            _ = restTop;

            float safeBuiltCell = Mathf.Max(0.0001f, builtCellSize);
            BoardPlacement settlement = DiningTableLayout.ComputeInRectForBounds(
                settlementLeft,
                settlementRight,
                settlementBottom,
                settlementTop,
                boardWidth,
                boardHeight,
                bounds,
                0.01f,
                DiningTableLayout.MaxCellSize);

            float targetCellSize = Mathf.Min(settlement.CellSize, DiningTableLayout.MaxCellSize);
            float maxScale = DiningTableLayout.MaxCellSize / safeBuiltCell;
            float scale = Mathf.Min(targetCellSize / safeBuiltCell, maxScale);
            return new FoodSettlementBoardTween(settlement.Position, scale, targetCellSize);
        }

        public static FoodSettlementBoardTween Identity(float builtCellSize)
        {
            return new FoodSettlementBoardTween(Vector3.zero, 1f, builtCellSize);
        }
    }
}
