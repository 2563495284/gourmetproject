using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class NumbRotationOccupiedCellsTests
    {
        [Test]
        public void OriginAfterCenteredRotation_VerticalBarCcw_OccupiesCenteredRow()
        {
            DishShape vertical = DishShape.FromRows(new[] { "X", "X", "X" });
            DishShape horizontal = vertical.RotatedBy(3);
            GridPos origin = DishShape.OriginAfterCenteredRotation(
                vertical,
                new GridPos(16, 18),
                horizontal);

            Assert.That(origin, Is.EqualTo(new GridPos(15, 19)));
            Assert.That(
                Occupied(horizontal, origin),
                Is.EquivalentTo(new[]
                {
                    new GridPos(15, 19),
                    new GridPos(16, 19),
                    new GridPos(17, 19),
                }));
        }

        [Test]
        public void OriginAfterCenteredRotation_KeepsSquareInPlace()
        {
            DishShape square = DishShape.FromRows(new[] { "XX", "XX" });
            GridPos origin = DishShape.OriginAfterCenteredRotation(
                square,
                new GridPos(5, 5),
                square.RotatedBy(1));

            Assert.That(origin, Is.EqualTo(new GridPos(5, 5)));
        }

        [Test]
        public void OriginAfterCenteredRotation_SingleCell_KeepsOrigin()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            GridPos origin = DishShape.OriginAfterCenteredRotation(
                cell,
                new GridPos(3, 4),
                cell.RotatedBy(1));

            Assert.That(origin, Is.EqualTo(new GridPos(3, 4)));
        }

        [Test]
        public void MoveDishToTemporaryAreaAfterRotationDelta_PoppingCandy_KeepsOccupiedCentroid()
        {
            DishShape vertical = DishShape.FromRows(new[] { "X", "X", "X" });
            DishDef popping = new DishDef(
                "popping_candy",
                "跳跳糖",
                30,
                vertical,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty);
            var table = new DiningTable(36, 36);
            var dish = new DishInstance(
                7,
                popping,
                new Placement(vertical, 0, new GridPos(16, 18)),
                Array.Empty<string>(),
                Array.Empty<string>());
            table.Place(dish);
            BattleSession session = Session(table, popping);

            Assert.That(session.MoveDishToTemporaryAreaAfterRotationDelta(7, 1), Is.True);
            Assert.That(session.DiningTable.DishCount, Is.EqualTo(0));
            Assert.That(session.TemporaryAreaDishes, Has.Count.EqualTo(1));

            DishInstance rotated = session.TemporaryAreaDishes[0];
            Assert.That(rotated.Placement.RotationIndex, Is.EqualTo(3));
            Assert.That(rotated.Placement.Origin, Is.EqualTo(new GridPos(15, 19)));
            Assert.That(
                rotated.OccupiedCells,
                Is.EquivalentTo(new[]
                {
                    new GridPos(15, 19),
                    new GridPos(16, 19),
                    new GridPos(17, 19),
                }));
        }

        [Test]
        public void MoveDishToTemporaryAreaAfterRotationDelta_AlreadyHorizontalBar_RotatesAroundCenter()
        {
            DishShape vertical = DishShape.FromRows(new[] { "X", "X", "X" });
            DishShape horizontal = vertical.RotatedBy(3);
            DishDef popping = new DishDef(
                "popping_candy",
                "跳跳糖",
                30,
                vertical,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty);
            var table = new DiningTable(36, 36);
            var dish = new DishInstance(
                7,
                popping,
                new Placement(horizontal, 3, new GridPos(16, 18)),
                Array.Empty<string>(),
                Array.Empty<string>());
            table.Place(dish);
            BattleSession session = Session(table, popping);

            Assert.That(session.MoveDishToTemporaryAreaAfterRotationDelta(7, 1), Is.True);

            DishInstance rotated = session.TemporaryAreaDishes[0];
            Assert.That(rotated.Placement.RotationIndex, Is.EqualTo(2));
            Assert.That(
                rotated.OccupiedCells,
                Is.EquivalentTo(new[]
                {
                    new GridPos(17, 17),
                    new GridPos(17, 18),
                    new GridPos(17, 19),
                }));
        }

        [Test]
        public void PrepareServe_NumbDoesNotFitRotated_DoesNotPrepare()
        {
            FlavorDef numb = NumbFlavor();
            DishDef fruitCake = Dish("fruit_cake", DishShape.FromRows(new[] { "XX", "XX", "XX" }));
            var slot = new RecipeSlot("slot", new[]
            {
                new RecipeSlotEntry(fruitCake.Id, new[] { "t_numb" }),
            });
            BattleSession session = Session(
                new DiningTable(2, 3),
                new[] { fruitCake },
                new[] { numb },
                slot);

            ServePrepareResult result = session.PrepareServe(0);

            Assert.That(result.Outcome, Is.EqualTo(ServePrepareOutcome.NoFittingDish));
            Assert.That(session.PreparedServe, Is.Null);
            Assert.That(session.CanFitRecipeEntry(0, 0), Is.False);
        }

        [Test]
        public void PrepareServe_NumbDoesNotFitRotated_SkipsToOtherDish()
        {
            FlavorDef numb = NumbFlavor();
            DishDef fruitCake = Dish("fruit_cake", DishShape.FromRows(new[] { "XX", "XX", "XX" }));
            DishDef cookie = Dish("cookie", DishShape.FromRows(new[] { "X" }));
            var slot = new RecipeSlot("slot", new[]
            {
                new RecipeSlotEntry(fruitCake.Id, new[] { "t_numb" }),
                new RecipeSlotEntry(cookie.Id),
            });
            BattleSession session = Session(
                new DiningTable(2, 3),
                new[] { fruitCake, cookie },
                new[] { numb },
                slot);

            ServePrepareResult result = session.PrepareServe(0);

            Assert.That(result.Outcome, Is.EqualTo(ServePrepareOutcome.Prepared));
            Assert.That(result.PreparedDish.Definition.Id, Is.EqualTo(cookie.Id));
            Assert.That(result.PreparedDish.Dish.Placement.RotationIndex, Is.EqualTo(0));
        }

        [Test]
        public void PrepareServe_NumbFitsRotated_UsesRotatedPlacement()
        {
            FlavorDef numb = NumbFlavor();
            DishDef fruitCake = Dish("fruit_cake", DishShape.FromRows(new[] { "XX", "XX", "XX" }));
            var slot = new RecipeSlot("slot", new[]
            {
                new RecipeSlotEntry(fruitCake.Id, new[] { "t_numb" }),
            });
            BattleSession session = Session(
                new DiningTable(3, 2),
                new[] { fruitCake },
                new[] { numb },
                slot);

            ServePrepareResult result = session.PrepareServe(0);

            Assert.That(result.Outcome, Is.EqualTo(ServePrepareOutcome.Prepared));
            Assert.That(result.PreparedDish.Dish.Placement.RotationIndex, Is.EqualTo(3));
            Assert.That(result.PreparedDish.Dish.Placement.Orientation.Width, Is.EqualTo(3));
            Assert.That(result.PreparedDish.Dish.Placement.Orientation.Height, Is.EqualTo(2));
        }

        private static IReadOnlyList<GridPos> Occupied(DishShape orientation, GridPos origin)
            => orientation.Cells.Select(cell => cell.Offset(origin.X, origin.Y)).ToList();

        private static BattleSession Session(DiningTable table, DishDef dish)
            => Session(table, new[] { dish }, Array.Empty<FlavorDef>());

        private static BattleSession Session(
            DiningTable table,
            IReadOnlyList<DishDef> dishes,
            IReadOnlyList<FlavorDef> flavors,
            params RecipeSlot[] slots)
            => new BattleSession(
                table,
                new GameplayDatabase(
                    dishes,
                    Array.Empty<SkillDef>(),
                    flavors,
                    Array.Empty<RecipeDef>()),
                new Xoshiro256SS(1UL),
                slots,
                requiredScore: 0);

        private static DishDef Dish(string id, DishShape shape)
            => new DishDef(id, id, 10, shape, 0, 0, 1f, Array.Empty<string>(), string.Empty);

        private static FlavorDef NumbFlavor()
            => new FlavorDef(
                "t_numb",
                "麻",
                string.Empty,
                FlavorEffectType.Rotate,
                new[] { 1f },
                Array.Empty<string>(),
                string.Empty);
    }
}
