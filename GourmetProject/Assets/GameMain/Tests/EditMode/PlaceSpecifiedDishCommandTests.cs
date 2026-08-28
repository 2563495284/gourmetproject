#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Game.DevConsole;
using GourmetProject.Game.DevConsole.Commands;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class PlaceSpecifiedDishCommandTests
    {
        [Test]
        public void FillWithoutDishId_KeepsNormalServeFlow()
        {
            DishDef served = CreateDish("served", new[] { "X" });
            BattleSession session = CreateSession(
                new DiningTable(2, 1),
                new[] { served },
                new[] { "served", "served" });

            RandomPlaceResult result = PlaceCommand.Fill(
                session,
                new Xoshiro256SS(42UL));

            Assert.That(result.PlacedCount, Is.EqualTo(2));
            Assert.That(session.ServesUsed, Is.EqualTo(2));
            Assert.That(session.DiningTable.Dishes.Select(dish => dish.Def.Id),
                Is.All.EqualTo("served"));
        }

        [Test]
        public void RepeatedSolver_FindsMaximumWhenRowMajorGreedyWouldLose()
        {
            var existing = new List<GridPos>();
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    if (x != 0 || y != 1)
                    {
                        existing.Add(new GridPos(x, y));
                    }
                }
            }

            var table = new DiningTable(4, 3, existing);
            DishDef lShape = CreateDish("l_shape", new[] { "X.", "XX" });

            IReadOnlyList<Placement> result =
                RepeatedDishPlacementSolver.Solve(table, lShape);

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].Origin, Is.EqualTo(new GridPos(2, 0)));
            Assert.That(result[1].Origin, Is.EqualTo(new GridPos(1, 1)));
        }

        [Test]
        public void RepeatedSolver_PathologicalSearchStopsAtHardNodeLimit()
        {
            DishDef sparseShape = CreateDish(
                "pathological_sparse_shape",
                new[] { "X..", "...", "..X" });

            RepeatedDishPlacementResult result =
                RepeatedDishPlacementSolver.SolveWithDiagnostics(
                    new DiningTable(12, 12),
                    sparseShape);

            Assert.That(result.Truncated, Is.True);
            Assert.That(result.SearchNodes,
                Is.EqualTo(RepeatedDishPlacementSolver.SearchNodeLimit));
            Assert.That(result.Placements, Has.Count.GreaterThan(0));
            AssertPlacementsDoNotOverlap(result.Placements);
        }

        [Test]
        public void RepeatedSolver_FindsExactMaximumForConfiguredLShapeOnMaximumTable()
        {
            DishDef lShape = CreateDish(
                "configured_l_shape",
                new[] { "XX", ".X", ".X" });

            RepeatedDishPlacementResult result =
                RepeatedDishPlacementSolver.SolveWithDiagnostics(
                    new DiningTable(12, 12),
                    lShape);

            Assert.That(result.Truncated, Is.False);
            Assert.That(result.Placements, Has.Count.EqualTo(28));
            AssertPlacementsDoNotOverlap(result.Placements);
        }

        [Test]
        public void FillSpecified_DirectlyGeneratesConfiguredDishWithoutServing()
        {
            DishDef target = CreateDish(
                "targett_sweet",
                new[] { "X" },
                new[] { "skill_target" },
                "t_sweet");
            DishDef outletDish = CreateDish("outlet", new[] { "XX" });
            BattleSession session = CreateSession(
                new DiningTable(3, 2),
                new[] { target, outletDish },
                new[] { "outlet" });
            ServePrepareResult prepared = session.PrepareServeAutomatically(0);
            Assert.That(prepared.Success, Is.True);
            PreparedServeDish preparedBeforeGm = session.PreparedServe;
            int remainingRecipeEntries = session.Slots[0].Entries.Count;

            SpecifiedPlaceResult result =
                PlaceCommand.FillSpecified(session, target.Id);

            Assert.That(result.StopReason, Is.EqualTo(SpecifiedPlaceStopReason.Completed));
            Assert.That(result.PlacedCount, Is.EqualTo(6));
            Assert.That(session.ServesUsed, Is.Zero);
            Assert.That(session.PreparedServe, Is.SameAs(preparedBeforeGm));
            Assert.That(session.Slots[0].Entries, Has.Count.EqualTo(remainingRecipeEntries));
            Assert.That(session.DiningTable.Dishes, Has.Count.EqualTo(6));
            foreach (DishInstance dish in session.DiningTable.Dishes)
            {
                Assert.That(dish.Def.Id, Is.EqualTo(target.Id));
                CollectionAssert.AreEqual(target.SkillIds, dish.SkillIds);
                CollectionAssert.AreEqual(new[] { "t_sweet" }, dish.FlavorIds);
            }
        }

        [Test]
        public void FillSpecified_ReportsUnknownNoSpaceAndSettledStates()
        {
            DishDef wide = CreateDish("wide", new[] { "XX" });
            BattleSession session = CreateSession(
                new DiningTable(1, 1),
                new[] { wide },
                Array.Empty<string>());

            SpecifiedPlaceResult unknown =
                PlaceCommand.FillSpecified(session, "missing");
            SpecifiedPlaceResult noSpace =
                PlaceCommand.FillSpecified(session, wide.Id);
            session.Settle();
            SpecifiedPlaceResult settled =
                PlaceCommand.FillSpecified(session, wide.Id);

            Assert.That(unknown.StopReason,
                Is.EqualTo(SpecifiedPlaceStopReason.DishNotFound));
            Assert.That(noSpace.StopReason,
                Is.EqualTo(SpecifiedPlaceStopReason.NoLegalPlacement));
            Assert.That(settled.StopReason,
                Is.EqualTo(SpecifiedPlaceStopReason.Settled));
        }

        [Test]
        public void Execute_WithTooManyArguments_ReturnsUsageBeforeBattleValidation()
        {
            CmdResult result = new PlaceCommand().Execute(new[] { "dish", "2" });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("用法：place [dish-id]"));
        }

        [Test]
        public void DishCompletion_IncludesRuntimeFlavorVariantsAndFiltersThem()
        {
            GameplayDatabase database = CreateDatabase(new[]
            {
                CreateDish("jelly", new[] { "XX" }),
                CreateDish("jellyt_sweet", new[] { "XX" }, flavorId: "t_sweet"),
                CreateDish("cake", new[] { "XX" }),
            });

            IReadOnlyList<string> all = PlaceCommand.AllDishIds(database);
            IReadOnlyList<string> filtered =
                PlaceCommand.CompleteDishIds(database, "jellyt_");

            CollectionAssert.AreEqual(
                new[] { "cake", "jelly", "jellyt_sweet" },
                all);
            CollectionAssert.AreEqual(new[] { "jellyt_sweet" }, filtered);
        }

        [TestCase(1, 1, 144)]
        [TestCase(2, 1, 72)]
        [TestCase(1, 2, 72)]
        [TestCase(3, 1, 48)]
        [TestCase(1, 3, 48)]
        [TestCase(2, 2, 36)]
        [TestCase(3, 2, 24)]
        [TestCase(2, 3, 24)]
        [TestCase(3, 3, 16)]
        public void RepeatedSolver_HandlesConfiguredShapesOnMaximumTable(
            int dishWidth,
            int dishHeight,
            int expectedCount)
        {
            string[] rows = Enumerable.Repeat(
                new string('X', dishWidth),
                dishHeight).ToArray();
            DishDef dish = CreateDish($"rect_{dishWidth}_{dishHeight}", rows);

            IReadOnlyList<Placement> result =
                RepeatedDishPlacementSolver.Solve(new DiningTable(12, 12), dish);

            Assert.That(result, Has.Count.EqualTo(expectedCount));
        }

        private static BattleSession CreateSession(
            DiningTable table,
            IReadOnlyList<DishDef> dishes,
            IReadOnlyList<string> recipeDishIds)
        {
            GameplayDatabase database = CreateDatabase(dishes);
            var slot = new RecipeSlot("recipe", recipeDishIds);
            return new BattleSession(
                table,
                database,
                new Xoshiro256SS(12345UL),
                new[] { slot },
                requiredScore: 0);
        }

        private static GameplayDatabase CreateDatabase(
            IEnumerable<DishDef> dishes)
            => new GameplayDatabase(
                dishes,
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());

        private static DishDef CreateDish(
            string id,
            IReadOnlyList<string> rows,
            IReadOnlyList<string> skillIds = null,
            string flavorId = "")
            => new DishDef(
                id,
                id,
                deliciousness: 1,
                DishShape.FromRows(rows),
                hiddenMin: 0,
                hiddenMax: 0,
                baseWeight: 1f,
                skillIds ?? Array.Empty<string>(),
                flavorId);

        private static void AssertPlacementsDoNotOverlap(
            IEnumerable<Placement> placements)
        {
            var occupied = new HashSet<GridPos>();
            foreach (Placement placement in placements)
            {
                foreach (GridPos local in placement.Orientation.Cells)
                {
                    GridPos cell = local.Offset(
                        placement.Origin.X,
                        placement.Origin.Y);
                    Assert.That(occupied.Add(cell), Is.True, $"重复占用了格子 {cell}");
                }
            }
        }
    }
}
#endif
