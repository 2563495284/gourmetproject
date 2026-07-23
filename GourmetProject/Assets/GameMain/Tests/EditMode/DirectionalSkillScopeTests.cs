using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DirectionalSkillScopeTests
    {
        [Test]
        public void DirectionalScopeCellsProjectEachOccupiedRowOrColumnToBoardEdge()
        {
            DiningTable table = CreateReferenceTable();
            DishInstance self = CreateSelf();
            table.Place(self);

            AssertCells(table, self, SkillScope.Left,
                Row(2, 2, 7)
                    .Concat(Row(3, 2, 5))
                    .Concat(Row(4, 0, 4)));
            AssertCells(table, self, SkillScope.Up,
                Column(5, 0, 3)
                    .Concat(Column(6, 0, 2))
                    .Concat(Column(7, 0, 3))
                    .Concat(Column(8, 0, 1)));
            AssertCells(table, self, SkillScope.Right,
                Row(2, 9, 10)
                    .Concat(Row(3, 9, 10))
                    .Concat(Row(4, 9, 10)));
            AssertCells(table, self, SkillScope.Down,
                Column(5, 5, 8)
                    .Concat(Column(6, 5, 8))
                    .Concat(Column(7, 5, 6))
                    .Concat(Column(8, 5, 6)));

            CollectionAssert.Contains(
                SkillConditionEvaluator.ScopeCells(table, self, SkillScope.Left).ToList(),
                new GridPos(5, 2));
            CollectionAssert.Contains(
                SkillConditionEvaluator.ScopeCells(table, self, SkillScope.Up).ToList(),
                new GridPos(5, 2));
        }

        [Test]
        public void DirectionalScopeDishesUseProjectedCellsAndExcludeUnrelatedDishes()
        {
            DiningTable table = CreateReferenceTable();
            DishInstance self = CreateSelf();
            DishShape cell = DishShape.FromRows(new[] { "X" });
            DishDef otherDef = Dish("other", cell);
            DishInstance overlap = Instance(2, otherDef, cell, 5, 2);
            DishInstance leftOnly = Instance(3, otherDef, cell, 3, 4);
            DishInstance upOnly = Instance(4, otherDef, cell, 7, 1);
            DishInstance right = Instance(5, otherDef, cell, 10, 3);
            DishInstance down = Instance(6, otherDef, cell, 8, 6);
            DishInstance unrelated = Instance(7, otherDef, cell, 2, 0);

            table.Place(self);
            table.Place(overlap);
            table.Place(leftOnly);
            table.Place(upOnly);
            table.Place(right);
            table.Place(down);
            table.Place(unrelated);

            AssertDishIds(table, self, SkillScope.Left, 2, 3);
            AssertDishIds(table, self, SkillScope.Up, 2, 4);
            AssertDishIds(table, self, SkillScope.Right, 5);
            AssertDishIds(table, self, SkillScope.Down, 6);
        }

        [Test]
        public void RuntimeAndGeneratedDirectionalScopeValuesStayInSync()
        {
            Assert.That((int)SkillScope.Left, Is.EqualTo((int)cfg.SkillScope.Left));
            Assert.That((int)SkillScope.Up, Is.EqualTo((int)cfg.SkillScope.Up));
            Assert.That((int)SkillScope.Right, Is.EqualTo((int)cfg.SkillScope.Right));
            Assert.That((int)SkillScope.Down, Is.EqualTo((int)cfg.SkillScope.Down));
        }

        private static DiningTable CreateReferenceTable()
        {
            var existing = new List<GridPos>();
            existing.AddRange(Rect(2, 0, 10, 3));
            existing.AddRange(Rect(0, 4, 10, 6));
            existing.AddRange(Rect(5, 7, 6, 8));
            return new DiningTable(11, 9, existing, null);
        }

        private static DishInstance CreateSelf()
        {
            DishShape shape = DishShape.FromRows(new[]
            {
                "...X",
                ".X.X",
                "XXXX",
            });
            DishDef def = Dish("self", shape);
            return Instance(1, def, shape, 5, 2);
        }

        private static DishDef Dish(string id, DishShape shape)
        {
            return new DishDef(
                id,
                id,
                10,
                shape,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                false);
        }

        private static DishInstance Instance(int id, DishDef def, DishShape shape, int x, int y)
        {
            return new DishInstance(
                id,
                def,
                new Placement(shape, 0, new GridPos(x, y)),
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        private static IEnumerable<GridPos> Rect(int minX, int minY, int maxX, int maxY)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    yield return new GridPos(x, y);
                }
            }
        }

        private static IEnumerable<GridPos> Row(int y, int minX, int maxX)
        {
            for (int x = minX; x <= maxX; x++)
            {
                yield return new GridPos(x, y);
            }
        }

        private static IEnumerable<GridPos> Column(int x, int minY, int maxY)
        {
            for (int y = minY; y <= maxY; y++)
            {
                yield return new GridPos(x, y);
            }
        }

        private static void AssertCells(
            DiningTable table,
            DishInstance self,
            SkillScope scope,
            IEnumerable<GridPos> expected)
        {
            CollectionAssert.AreEquivalent(
                expected.ToList(),
                SkillConditionEvaluator.ScopeCells(table, self, scope).ToList());
        }

        private static void AssertDishIds(
            DiningTable table,
            DishInstance self,
            SkillScope scope,
            params int[] expected)
        {
            CollectionAssert.AreEquivalent(
                expected,
                SkillConditionEvaluator.ScopeDishes(table, self, scope).Select(d => d.Id).ToArray());
        }
    }
}
