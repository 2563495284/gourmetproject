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
        public static readonly Color Valid = new Color(0.30f, 1f, 0.42f, 0.95f);
        public static readonly Color Missing = new Color(1f, 0.76f, 0.12f, 0.95f);
        public static readonly Color Blocked = new Color(1f, 0.25f, 0.22f, 0.95f);

        public static Color ColorFor(GridPlacementFeedbackState state)
        {
            return state switch
            {
                GridPlacementFeedbackState.Blocked => Blocked,
                GridPlacementFeedbackState.Missing => Missing,
                _ => Valid,
            };
        }
    }
}
