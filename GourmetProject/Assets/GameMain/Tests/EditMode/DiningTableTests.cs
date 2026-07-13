using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Tests
{
    /// <summary>棋盘测试：占用统计、越界/重叠拒绝、相邻菜品计数、合法摆放枚举。</summary>
    public class DiningTableTests
    {
        [Test]
        public void Place_OccupiesExpectedCellsAndCounts()
        {
            var board = new GpTable(4, 4);
            DishDef square = GameplayTestFactory.Dish("square", new[] { "XX", "XX" }, allowRotate: false);
            DishInstance inst = GameplayTestFactory.Instance(1, square, 0, 0);

            board.Place(inst);

            Assert.AreEqual(1, board.DishCount);
            Assert.AreEqual(4, board.OccupiedCellCount);
            Assert.AreEqual(12, board.EmptyCellCount);
            Assert.IsFalse(board.IsEmpty(new GridPos(0, 0)));
            Assert.IsTrue(board.IsEmpty(new GridPos(3, 3)));
        }

        [Test]
        public void CanPlace_RejectsOverlapAndOutOfBounds()
        {
            var board = new GpTable(4, 4);
            DishDef square = GameplayTestFactory.Dish("square", new[] { "XX", "XX" }, allowRotate: false);
            board.Place(GameplayTestFactory.Instance(1, square, 0, 0));

            DishShape shape = square.Shape;
            Assert.IsFalse(board.CanPlace(shape, new GridPos(0, 0)), "overlap should be rejected");
            Assert.IsFalse(board.CanPlace(shape, new GridPos(3, 3)), "out-of-bounds should be rejected");
            Assert.IsTrue(board.CanPlace(shape, new GridPos(2, 2)), "empty area should accept");
        }

        [Test]
        public void GetAdjacentDishCount_CountsEdgeSharingNeighbors()
        {
            var board = new GpTable(4, 4);
            DishDef square = GameplayTestFactory.Dish("square", new[] { "XX", "XX" }, allowRotate: false);
            DishDef single = GameplayTestFactory.Dish("single", new[] { "X" }, allowRotate: false);

            DishInstance sq = GameplayTestFactory.Instance(1, square, 0, 0); // (0,0)(1,0)(0,1)(1,1)
            DishInstance touching = GameplayTestFactory.Instance(2, single, 2, 0); // (2,0) shares edge with (1,0)
            DishInstance diagonal = GameplayTestFactory.Instance(3, single, 2, 2); // only diagonal -> not adjacent

            board.Place(sq);
            board.Place(touching);
            board.Place(diagonal);

            Assert.AreEqual(1, board.GetAdjacentDishCount(sq));
            Assert.AreEqual(1, board.GetAdjacentDishCount(touching));
            Assert.AreEqual(0, board.GetAdjacentDishCount(diagonal));
        }

        [Test]
        public void FindValidPlacements_FullBoardLeavesNoSpaceForLargeDish()
        {
            var board = new GpTable(2, 2);
            DishDef square = GameplayTestFactory.Dish("square", new[] { "XX", "XX" }, allowRotate: false);
            board.Place(GameplayTestFactory.Instance(1, square, 0, 0));

            DishDef single = GameplayTestFactory.Dish("single", new[] { "X" }, allowRotate: false);
            Assert.IsFalse(board.CanFit(single));
            Assert.AreEqual(0, board.FindValidPlacements(single).Count);
        }

        [Test]
        public void Clear_ResetsBoard()
        {
            var board = new GpTable(4, 4);
            DishDef square = GameplayTestFactory.Dish("square", new[] { "XX", "XX" }, allowRotate: false);
            board.Place(GameplayTestFactory.Instance(1, square, 0, 0));

            board.Clear();

            Assert.AreEqual(0, board.DishCount);
            Assert.AreEqual(0, board.OccupiedCellCount);
        }
    }
}
