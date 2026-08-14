using System.Collections.Generic;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime.Settings;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BoardEditInteractionTests
    {
        [Test]
        public void ServeDrop_DefaultsToImmediateConfirmation()
        {
            Assert.That(SettingsService.DefaultRequireServeConfirmation, Is.False);
            Assert.That(
                BattleWorldController.ShouldAutoConfirmPendingDish(
                    PendingDishActionKind.Serve,
                    SettingsService.DefaultRequireServeConfirmation),
                Is.True);
            Assert.That(
                BattleWorldController.ShouldShowPendingDishActionButton(
                    PendingDishActionKind.Serve,
                    SettingsService.DefaultRequireServeConfirmation),
                Is.False);
        }

        [Test]
        public void TemporaryDishPlacement_StillShowsConfirmationButton()
        {
            Assert.That(
                BattleWorldController.ShouldAutoConfirmPendingDish(
                    PendingDishActionKind.Confirm,
                    SettingsService.DefaultRequireServeConfirmation),
                Is.False);
            Assert.That(
                BattleWorldController.ShouldShowPendingDishActionButton(
                    PendingDishActionKind.Confirm,
                    SettingsService.DefaultRequireServeConfirmation),
                Is.True);
        }

        [Test]
        public void TrayHitTester_ExactCellBeatsEarlierCandidatePadding()
        {
            var regions = new List<BoardEditTrayHitRegion>
            {
                Region(0, 0f, 0f, 1f, 1f),
                Region(1, 0.8f, 0f, 0.4f, 0.4f),
            };

            bool picked = BoardEditTrayHitTester.TryPick(
                new Vector2(0.8f, 0f),
                regions,
                0.4f,
                out int candidateIndex);

            Assert.That(picked, Is.True);
            Assert.That(candidateIndex, Is.EqualTo(1));
        }

        [Test]
        public void TrayHitTester_OverlappingPaddingChoosesNearestCellCenter()
        {
            var regions = new List<BoardEditTrayHitRegion>
            {
                Region(0, 0f, 0f, 0.4f, 0.4f),
                Region(1, 1f, 0f, 0.4f, 0.4f),
            };

            bool picked = BoardEditTrayHitTester.TryPick(
                new Vector2(0.62f, 0f),
                regions,
                0.5f,
                out int candidateIndex);

            Assert.That(picked, Is.True);
            Assert.That(candidateIndex, Is.EqualTo(1));
        }

        [Test]
        public void TrayHitTester_SparseCandidateHoleDoesNotStealMiddleCell()
        {
            var regions = new List<BoardEditTrayHitRegion>
            {
                Region(0, 0f, 0f, 0.5f, 0.5f),
                Region(0, 2f, 0f, 0.5f, 0.5f),
                Region(1, 1f, 0f, 0.5f, 0.5f),
            };

            bool picked = BoardEditTrayHitTester.TryPick(
                new Vector2(1f, 0f),
                regions,
                0.15f,
                out int candidateIndex);

            Assert.That(picked, Is.True);
            Assert.That(candidateIndex, Is.EqualTo(1));
        }

        [Test]
        public void TrayHitTester_PointOutsideEveryCellAndPaddingReturnsNoCandidate()
        {
            var regions = new List<BoardEditTrayHitRegion>
            {
                Region(0, 0f, 0f, 0.5f, 0.5f),
                Region(1, 1f, 0f, 0.5f, 0.5f),
            };

            bool picked = BoardEditTrayHitTester.TryPick(
                new Vector2(3f, 2f),
                regions,
                0.15f,
                out int candidateIndex);

            Assert.That(picked, Is.False);
            Assert.That(candidateIndex, Is.EqualTo(-1));
        }

        [Test]
        public void FragmentFeedback_AllCellsValidUsesGreenPlateGhosts()
        {
            DiningTable table = Table(new GridPos(3, 3));
            TableFragmentPlacementEvaluation evaluation = Evaluate(
                table,
                Fragment("XX"),
                new GridPos(4, 3),
                maxWidth: 4,
                maxHeight: 4);

            Assert.That(evaluation.CanCommit, Is.True);
            Assert.That(evaluation.Feedback.OverallState, Is.EqualTo(GridPlacementFeedbackState.Valid));
            Assert.That(evaluation.Feedback.Cells, Has.All.Matches<GridPlacementFeedbackCell>(
                cell => cell.State == GridPlacementFeedbackState.Valid));
            Assert.That(BoardEditGhostPalette.Alpha, Is.EqualTo(0.5f));
            Assert.That(BoardEditGhostPalette.BaseColor.a, Is.EqualTo(0.5f));
            Assert.That(
                BoardEditGhostPalette.PlateColor(
                    evaluation.Feedback.OverallState,
                    evaluation.Feedback.Cells[0].State),
                Is.EqualTo(new Color(
                    GridPlacementFeedbackPalette.Valid.r,
                    GridPlacementFeedbackPalette.Valid.g,
                    GridPlacementFeedbackPalette.Valid.b,
                    1f)));
        }

        [Test]
        public void FragmentFeedback_OverlapMarksOnlyConflictingCellBlocked()
        {
            DiningTable table = Table(new GridPos(3, 3));
            TableFragmentPlacementEvaluation evaluation = Evaluate(
                table,
                Fragment("XX"),
                new GridPos(3, 3),
                maxWidth: 4,
                maxHeight: 4);

            Assert.That(evaluation.Feedback.OverallState, Is.EqualTo(GridPlacementFeedbackState.Blocked));
            Assert.That(StateAt(evaluation, new GridPos(3, 3)), Is.EqualTo(GridPlacementFeedbackState.Blocked));
            Assert.That(StateAt(evaluation, new GridPos(4, 3)), Is.EqualTo(GridPlacementFeedbackState.Valid));
            Assert.That(
                GridPlacementFeedbackPalette.DishColorFor(
                    evaluation.Feedback.OverallState,
                    StateAt(evaluation, new GridPos(3, 3))),
                Is.EqualTo(GridPlacementFeedbackPalette.Blocked));
            Assert.That(
                GridPlacementFeedbackPalette.DishColorFor(
                    evaluation.Feedback.OverallState,
                    StateAt(evaluation, new GridPos(4, 3))),
                Is.EqualTo(GridPlacementFeedbackPalette.Missing));
        }

        [Test]
        public void FragmentFeedback_MaxBoundsMarksOnlyOverflowCellBlocked()
        {
            DiningTable table = Table(new GridPos(2, 2), new GridPos(3, 2));
            TableFragmentPlacementEvaluation evaluation = Evaluate(
                table,
                Fragment("XX"),
                new GridPos(4, 2),
                maxWidth: 3,
                maxHeight: 3);

            Assert.That(evaluation.Feedback.OverallState, Is.EqualTo(GridPlacementFeedbackState.Blocked));
            Assert.That(StateAt(evaluation, new GridPos(4, 2)), Is.EqualTo(GridPlacementFeedbackState.Valid));
            Assert.That(StateAt(evaluation, new GridPos(5, 2)), Is.EqualTo(GridPlacementFeedbackState.Blocked));
        }

        [Test]
        public void FragmentFeedback_DetachedCellsRemainValidSoGhostsAreYellow()
        {
            DiningTable table = Table(new GridPos(3, 3));
            TableFragmentPlacementEvaluation evaluation = Evaluate(
                table,
                Fragment("XX"),
                new GridPos(0, 0),
                maxWidth: 4,
                maxHeight: 4);

            Assert.That(evaluation.Feedback.OverallState, Is.EqualTo(GridPlacementFeedbackState.Missing));
            Assert.That(evaluation.Feedback.Cells, Has.All.Matches<GridPlacementFeedbackCell>(
                cell => cell.State == GridPlacementFeedbackState.Valid));
            Assert.That(
                GridPlacementFeedbackPalette.DishColorFor(
                    evaluation.Feedback.OverallState,
                    evaluation.Feedback.Cells[0].State),
                Is.EqualTo(GridPlacementFeedbackPalette.Missing));
        }

        [Test]
        public void PlacementAnimationOrder_RotatedLShapeStartsAtAttachmentAndTravelsOutward()
        {
            TableFragmentDef rotated = Fragment("XX", "X.").Rotated(1);
            List<GridPos> cells = TableFragmentBuilder.FilledCells(rotated);
            var existing = new HashSet<GridPos> { new GridPos(4, 5) };

            List<GridPos> ordered = TableFragmentPlacementAnimationOrder.Build(
                existing,
                cells,
                new GridPos(5, 5));

            Assert.That(ordered, Is.EqualTo(new[]
            {
                new GridPos(0, 0),
                new GridPos(1, 0),
                new GridPos(1, 1),
            }));
        }

        [Test]
        public void PlacementAnimationOrder_SparseDisconnectedCellRunsAfterConnectedCells()
        {
            List<GridPos> cells = TableFragmentBuilder.FilledCells(Fragment("XX.X"));
            var existing = new HashSet<GridPos> { new GridPos(4, 5) };

            List<GridPos> ordered = TableFragmentPlacementAnimationOrder.Build(
                existing,
                cells,
                new GridPos(5, 5));

            Assert.That(ordered, Is.EqualTo(new[]
            {
                new GridPos(0, 0),
                new GridPos(1, 0),
                new GridPos(3, 0),
            }));
        }

        private static DiningTable Table(params GridPos[] existing)
        {
            return new DiningTable(8, 8, existing, null);
        }

        private static TableFragmentDef Fragment(params string[] rows)
        {
            return new TableFragmentDef(
                "test_fragment",
                rows,
                0,
                0,
                1f,
                null,
                null);
        }

        private static TableFragmentPlacementEvaluation Evaluate(
            DiningTable table,
            TableFragmentDef fragment,
            GridPos origin,
            int maxWidth,
            int maxHeight)
        {
            return TableFragmentPlacementEvaluator.EvaluateAtOrigin(
                table,
                fragment,
                origin,
                origin,
                maxWidth,
                maxHeight);
        }

        private static GridPlacementFeedbackState StateAt(
            TableFragmentPlacementEvaluation evaluation,
            GridPos position)
        {
            foreach (GridPlacementFeedbackCell cell in evaluation.Feedback.Cells)
            {
                if (cell.Position.Equals(position))
                {
                    return cell.State;
                }
            }

            Assert.Fail($"Missing feedback cell at {position}.");
            return default;
        }

        private static BoardEditTrayHitRegion Region(
            int candidateIndex,
            float centerX,
            float centerY,
            float width,
            float height)
        {
            return new BoardEditTrayHitRegion(
                candidateIndex,
                new Bounds(
                    new Vector3(centerX, centerY, 0f),
                    new Vector3(width, height, 0f)));
        }
    }
}
