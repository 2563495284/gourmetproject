using System.Collections.Generic;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DiningTableSettlementOrderHintTests
    {
        [Test]
        public void BuildSettlementOrderHintCells_FiltersVoidAndDisabledCells_InRowMajorOrder()
        {
            var table = new DiningTable(
                4,
                3,
                new[]
                {
                    new GridPos(0, 0),
                    new GridPos(2, 0),
                    new GridPos(1, 1),
                    new GridPos(3, 1),
                    new GridPos(0, 2),
                });
            table.SetDisabled(new GridPos(1, 1), true);

            List<GridPos> actual = DiningTableView.BuildSettlementOrderHintCells(
                table,
                reverseOrder: false);

            CollectionAssert.AreEqual(
                new[]
                {
                    new GridPos(0, 0),
                    new GridPos(2, 0),
                    new GridPos(3, 1),
                    new GridPos(0, 2),
                },
                actual);
        }

        [Test]
        public void BuildSettlementOrderHintCells_ReverseOrder_IsExactInverseOfForwardOrder()
        {
            var table = new DiningTable(
                3,
                3,
                new[]
                {
                    new GridPos(0, 0),
                    new GridPos(2, 0),
                    new GridPos(1, 1),
                    new GridPos(0, 2),
                });
            table.SetDisabled(new GridPos(1, 1), true);

            List<GridPos> actual = DiningTableView.BuildSettlementOrderHintCells(
                table,
                reverseOrder: true);

            CollectionAssert.AreEqual(
                new[]
                {
                    new GridPos(0, 2),
                    new GridPos(2, 0),
                    new GridPos(0, 0),
                },
                actual);
        }
    }

    public sealed class DiningTableLayoutTests
    {
        [Test]
        public void ComputeInRect_IncludesPersistentlyVisibleRemovedCells()
        {
            var table = new DiningTable(
                4,
                4,
                new[]
                {
                    new GridPos(0, 0), new GridPos(1, 0), new GridPos(2, 0), new GridPos(3, 0),
                    new GridPos(0, 1), new GridPos(1, 1), new GridPos(2, 1), new GridPos(3, 1),
                    new GridPos(0, 2), new GridPos(1, 2), new GridPos(2, 2), new GridPos(3, 2),
                });
            var removedBottomRow = new[]
            {
                new GridPos(0, 3),
                new GridPos(1, 3),
                new GridPos(2, 3),
                new GridPos(3, 3),
            };

            BoardPlacement placement = DiningTableLayout.ComputeInRect(
                0f,
                4f,
                0f,
                3f,
                table,
                0f,
                removedBottomRow);

            Assert.That(placement.CellSize, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(placement.Position.x, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(placement.Position.y, Is.EqualTo(1.5f).Within(0.0001f));
        }

        [Test]
        public void FoodSettlementLayout_IncludesPersistentlyVisibleRemovedCells()
        {
            var table = new DiningTable(
                4,
                4,
                new[]
                {
                    new GridPos(0, 0), new GridPos(1, 0), new GridPos(2, 0), new GridPos(3, 0),
                    new GridPos(0, 1), new GridPos(1, 1), new GridPos(2, 1), new GridPos(3, 1),
                    new GridPos(0, 2), new GridPos(1, 2), new GridPos(2, 2), new GridPos(3, 2),
                });
            var removedBottomRow = new[]
            {
                new GridPos(0, 3),
                new GridPos(1, 3),
                new GridPos(2, 3),
                new GridPos(3, 3),
            };

            FoodSettlementBoardTween tween = FoodSettlementLayout.ComputeBoardTween(
                0f,
                4f,
                0f,
                3f,
                0f,
                4f,
                0f,
                3f,
                table,
                1f,
                removedBottomRow);

            Assert.That(tween.CellSize, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(tween.Scale, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(tween.Position.x, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(tween.Position.y, Is.EqualTo(1.5f).Within(0.0001f));
        }
    }
}
