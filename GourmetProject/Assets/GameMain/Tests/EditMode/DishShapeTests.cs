using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests
{
    /// <summary>
    /// 形状解析与旋转测试：行解析正确、归一化对齐、旋转去重、单格旋转不变。
    /// </summary>
    public class DishShapeTests
    {
        [Test]
        public void FromRows_ParsesFilledCells()
        {
            DishShape shape = DishShape.FromRows(new[] { "XX", "X." });

            Assert.AreEqual(3, shape.CellCount);
            Assert.AreEqual(2, shape.Width);
            Assert.AreEqual(2, shape.Height);
            CollectionAssert.Contains(shape.Cells, new GridPos(0, 0));
            CollectionAssert.Contains(shape.Cells, new GridPos(1, 0));
            CollectionAssert.Contains(shape.Cells, new GridPos(0, 1));
        }

        [Test]
        public void Rotate90_SwapsDimensionsForLine()
        {
            DishShape line = DishShape.FromRows(new[] { "XXX" });
            Assert.AreEqual(3, line.Width);
            Assert.AreEqual(1, line.Height);

            DishShape rotated = line.Rotate90();
            Assert.AreEqual(1, rotated.Width);
            Assert.AreEqual(3, rotated.Height);
            Assert.AreEqual(3, rotated.CellCount);
        }

        [Test]
        public void GetOrientations_SingleCell_HasOneUniqueOrientation()
        {
            DishShape single = DishShape.FromRows(new[] { "X" });
            Assert.AreEqual(1, single.GetOrientations(allowRotate: true).Count);
        }

        [Test]
        public void GetOrientations_Square_HasOneUniqueOrientation()
        {
            DishShape square = DishShape.FromRows(new[] { "XX", "XX" });
            Assert.AreEqual(1, square.GetOrientations(allowRotate: true).Count);
        }

        [Test]
        public void GetOrientations_LShape_HasFourUniqueOrientations()
        {
            DishShape lShape = DishShape.FromRows(new[] { "X.", "X.", "XX" });
            Assert.AreEqual(4, lShape.GetOrientations(allowRotate: true).Count);
        }

        [Test]
        public void GetOrientations_NoRotate_ReturnsSelfOnly()
        {
            DishShape lShape = DishShape.FromRows(new[] { "X.", "XX" });
            Assert.AreEqual(1, lShape.GetOrientations(allowRotate: false).Count);
        }
    }
}
