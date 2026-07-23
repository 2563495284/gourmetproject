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
    public sealed class BattleRecipeAvailabilityTests
    {
        [Test]
        public void CanFitRecipeEntry_ReflectsCurrentDiningTableOccupancy()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            DishShape bar = DishShape.FromRows(new[] { "XX" });
            DishDef small = Dish("small", "小菜", cell);
            DishDef large = Dish("large", "大菜", bar);
            GameplayDatabase database = Database(small, large);
            var table = new DiningTable(2, 1);
            var slot = new RecipeSlot("recipe", new[] { small.Id, large.Id });
            var session = new BattleSession(table, database, new FirstRandomStream(), new[] { slot }, 0);

            Assert.That(session.CanFitRecipeEntry(0, 0), Is.True);
            Assert.That(session.CanFitRecipeEntry(0, 1), Is.True);

            var placed = new DishInstance(
                99,
                small,
                new Placement(cell, 0, new GridPos(0, 0)),
                Array.Empty<string>(),
                Array.Empty<string>());
            table.Place(placed);

            Assert.That(session.CanFitRecipeEntry(0, 0), Is.True);
            Assert.That(session.CanFitRecipeEntry(0, 1), Is.False);
        }

        [Test]
        public void PrepareServe_StagesDishWithoutPlacingOrTriggeringServe()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            DishDef dish = Dish("dish", "菜", cell);
            var table = new DiningTable(2, 1);
            var slot = new RecipeSlot("recipe", new[] { dish.Id });
            var session = new BattleSession(table, Database(dish), new FirstRandomStream(), new[] { slot }, 0);
            int servedEvents = 0;
            session.Served += (_, _) => servedEvents++;

            ServePrepareResult result = session.PrepareServe(0);

            Assert.That(result.Success, Is.True);
            Assert.That(session.PreparedServe, Is.SameAs(result.PreparedDish));
            Assert.That(slot.Count, Is.Zero);
            Assert.That(table.DishCount, Is.Zero);
            Assert.That(session.ServesUsed, Is.Zero);
            Assert.That(servedEvents, Is.Zero);
        }

        [Test]
        public void CommitPreparedServe_UsesPlayerPlacementAndThenTriggersServe()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            DishDef dish = Dish("dish", "菜", cell);
            var table = new DiningTable(2, 1);
            var session = new BattleSession(
                table,
                Database(dish),
                new FirstRandomStream(),
                new[] { new RecipeSlot("recipe", new[] { dish.Id }) },
                0);
            int servedEvents = 0;
            session.Served += (_, _) => servedEvents++;
            PreparedServeDish prepared = session.PrepareServe(0).PreparedDish;
            Placement rightCell = prepared.Placements.First(
                p => p.Origin.X == 1 && p.Origin.Y == 0);

            ServeResult result = session.CommitPreparedServe(rightCell);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Dish.Placement.Origin.X, Is.EqualTo(1));
            Assert.That(table.DishAt(new GridPos(1, 0)), Is.SameAs(result.Dish));
            Assert.That(session.PreparedServe, Is.Null);
            Assert.That(session.ServesUsed, Is.EqualTo(1));
            Assert.That(servedEvents, Is.EqualTo(1));
        }

        [Test]
        public void InvalidCommit_KeepsDishWaitingAtOutlet()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            DishDef dish = Dish("dish", "菜", cell);
            var table = new DiningTable(1, 1);
            var session = new BattleSession(
                table,
                Database(dish),
                new FirstRandomStream(),
                new[] { new RecipeSlot("recipe", new[] { dish.Id }) },
                0);
            PreparedServeDish prepared = session.PrepareServe(0).PreparedDish;
            var outside = new Placement(cell, 0, new GridPos(2, 0));

            ServeResult result = session.CommitPreparedServe(outside);

            Assert.That(result.Outcome, Is.EqualTo(ServeOutcome.InvalidPlacement));
            Assert.That(session.PreparedServe, Is.SameAs(prepared));
            Assert.That(table.DishCount, Is.Zero);
            Assert.That(session.ServesUsed, Is.Zero);
        }

        private static DishDef Dish(string id, string name, DishShape shape)
        {
            return new DishDef(
                id,
                name,
                10,
                shape,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                false);
        }

        private static GameplayDatabase Database(params DishDef[] dishes)
        {
            return new GameplayDatabase(
                dishes,
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
        }

        private sealed class FirstRandomStream : IRandomStream
        {
            public RngState State { get; set; }

            public uint NextUInt() => 0;
            public ulong NextULong() => 0;
            public int Range(int minInclusive, int maxExclusive) => minInclusive;
            public float Range(float minInclusive, float maxExclusive) => minInclusive;
            public float NextFloat() => 0f;
            public double NextDouble() => 0d;
            public bool NextBool(double probability = 0.5) => probability > 0d;
            public void Shuffle<T>(IList<T> list) { }
            public T Pick<T>(IReadOnlyList<T> list) => list[0];
            public int WeightedPickIndex(IReadOnlyList<float> weights) => 0;
        }
    }
}
