using System.Collections.Generic;
using System.Reflection;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SettlementSequencerLayoutTests
    {
        [TestCase(new[] { ".XX", "XXX" }, 0, 1, 2)]
        [TestCase(new[] { "XXX", "X.X" }, 0, 0, 2)]
        [TestCase(new[] { "X.X", "XXX" }, 0, 0, 0)]
        public void TopContinuousRunUsesTopmostLongestSegment(
            string[] rows,
            int expectedRow,
            int expectedStartX,
            int expectedEndX)
        {
            DishShape shape = DishShape.FromRows(rows);
            object run = InvokeFindTopContinuousRun(shape.Cells);

            Assert.That(ReadInt(run, "Row"), Is.EqualTo(expectedRow));
            Assert.That(ReadInt(run, "StartX"), Is.EqualTo(expectedStartX));
            Assert.That(ReadInt(run, "EndX"), Is.EqualTo(expectedEndX));
        }

        private static object InvokeFindTopContinuousRun(IReadOnlyList<GridPos> cells)
        {
            MethodInfo method = typeof(SettlementSequencer).GetMethod(
                "FindTopContinuousRun",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, new object[] { cells });
        }

        private static int ReadInt(object target, string propertyName)
        {
            PropertyInfo property = target?.GetType().GetProperty(propertyName);
            Assert.That(property, Is.Not.Null);
            return (int)property.GetValue(target);
        }
    }
}
