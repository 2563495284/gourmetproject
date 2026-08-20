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
}
