using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Board;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class FoodSettlementLayoutTests
    {
        [Test]
        public void ComputeBoardTween_EnlargedArea_MovesDownWithoutExceedingDefaultCell()
        {
            const float restLeft = -5.65f;
            const float restRight = 5.65f;
            const float restBottom = -2.2f;
            const float restTop = 5f;
            const float settlementLeft = -5.65f;
            const float settlementRight = 5.65f;
            const float settlementBottom = -4.6f;
            const float settlementTop = 5f;
            var bounds = new TableFragmentBuilder.PlacementBounds(0, 0, 1, 1);
            const float builtCellSize = DiningTableLayout.MaxCellSize;

            FoodSettlementBoardTween tween = FoodSettlementLayout.ComputeBoardTween(
                restLeft,
                restRight,
                restBottom,
                restTop,
                settlementLeft,
                settlementRight,
                settlementBottom,
                settlementTop,
                8,
                8,
                bounds,
                builtCellSize);

            BoardPlacement restPlacement = DiningTableLayout.ComputeInRectForBounds(
                restLeft,
                restRight,
                restBottom,
                restTop,
                8,
                8,
                bounds,
                DiningTableLayout.MinCellSize);

            Assert.That(tween.CellSize, Is.EqualTo(DiningTableLayout.MaxCellSize).Within(0.0001f));
            Assert.That(tween.Scale, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(tween.Position.y, Is.LessThan(restPlacement.Position.y));
        }

        [Test]
        public void ComputeBoardTween_SmallerBuiltCell_ScalesUpUntilDefaultCell()
        {
            const float restLeft = -5.65f;
            const float restRight = 5.65f;
            const float restBottom = -2.2f;
            const float restTop = 5f;
            const float settlementLeft = -5.65f;
            const float settlementRight = 5.65f;
            const float settlementBottom = -4.6f;
            const float settlementTop = 5f;
            var bounds = new TableFragmentBuilder.PlacementBounds(0, 0, 7, 7);

            BoardPlacement restPlacement = DiningTableLayout.ComputeInRectForBounds(
                restLeft,
                restRight,
                restBottom,
                restTop,
                8,
                8,
                bounds,
                DiningTableLayout.MinCellSize);

            FoodSettlementBoardTween tween = FoodSettlementLayout.ComputeBoardTween(
                restLeft,
                restRight,
                restBottom,
                restTop,
                settlementLeft,
                settlementRight,
                settlementBottom,
                settlementTop,
                8,
                8,
                bounds,
                restPlacement.CellSize);

            Assert.That(restPlacement.CellSize, Is.LessThan(DiningTableLayout.MaxCellSize));
            Assert.That(tween.Scale, Is.GreaterThan(1f));
            Assert.That(tween.CellSize, Is.LessThanOrEqualTo(DiningTableLayout.MaxCellSize));
            Assert.That(
                restPlacement.CellSize * tween.Scale,
                Is.LessThanOrEqualTo(DiningTableLayout.MaxCellSize + 0.0001f));
        }

        [Test]
        public void ComputeBoardTween_SameArea_KeepsBuiltScale()
        {
            const float left = -4f;
            const float right = 4f;
            const float bottom = -2f;
            const float top = 2f;
            var bounds = new TableFragmentBuilder.PlacementBounds(0, 0, 3, 3);
            BoardPlacement restPlacement = DiningTableLayout.ComputeInRectForBounds(
                left,
                right,
                bottom,
                top,
                8,
                8,
                bounds,
                DiningTableLayout.MinCellSize);

            FoodSettlementBoardTween tween = FoodSettlementLayout.ComputeBoardTween(
                left,
                right,
                bottom,
                top,
                left,
                right,
                bottom,
                top,
                8,
                8,
                bounds,
                restPlacement.CellSize);

            Assert.That(tween.Scale, Is.EqualTo(1f).Within(0.001f));
            Assert.That(tween.Position.y, Is.EqualTo(restPlacement.Position.y).Within(0.001f));
        }
    }
}
