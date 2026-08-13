using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Model;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    public enum GridPlacementFeedbackState
    {
        Valid,
        Missing,
        Blocked,
    }

    public readonly struct GridPlacementFeedbackCell
    {
        public GridPlacementFeedbackCell(GridPos position, GridPlacementFeedbackState state)
        {
            Position = position;
            State = state;
        }

        public GridPos Position { get; }

        public GridPlacementFeedbackState State { get; }
    }

    public sealed class GridPlacementFeedback
    {
        public GridPlacementFeedback(
            GridPos centerCell,
            GridPlacementFeedbackState overallState,
            IReadOnlyList<GridPlacementFeedbackCell> cells)
        {
            CenterCell = centerCell;
            OverallState = overallState;
            Cells = cells ?? Array.Empty<GridPlacementFeedbackCell>();
        }

        public GridPos CenterCell { get; }

        public GridPlacementFeedbackState OverallState { get; }

        public IReadOnlyList<GridPlacementFeedbackCell> Cells { get; }
    }

    public static class GridPlacementFeedbackPalette
    {
        // 餐盘反馈统一使用偏浅、低饱和色，避免大面积染色时过于刺眼。
        public static readonly Color Valid = new Color(0.52f, 0.86f, 0.58f, 0.95f);
        public static readonly Color Missing = new Color(0.96f, 0.78f, 0.38f, 0.95f);
        public static readonly Color Blocked = new Color(0.94f, 0.48f, 0.46f, 0.95f);

        public static Color ColorFor(GridPlacementFeedbackState state)
        {
            return state switch
            {
                GridPlacementFeedbackState.Blocked => Blocked,
                GridPlacementFeedbackState.Missing => Missing,
                _ => Valid,
            };
        }

        /// <summary>
        /// 食物拖拽反馈：
        /// 整块可放时所有占用格为绿色；否则仅可用餐桌格为黄色，
        /// 已占用/禁用格与餐桌外网格都为红色。
        /// </summary>
        public static Color DishColorFor(
            GridPlacementFeedbackState overallState,
            GridPlacementFeedbackState cellState)
        {
            if (overallState == GridPlacementFeedbackState.Valid)
            {
                return Valid;
            }

            return cellState == GridPlacementFeedbackState.Valid
                ? Missing
                : Blocked;
        }
    }
}
