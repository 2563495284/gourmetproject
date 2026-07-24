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
        public void PrepareServeFromBell_AppliesOutputEffectsBeforePlacement()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            var source = new DishDef(
                "source",
                "原菜",
                10,
                cell,
                0,
                0,
                1f,
                new[] { "source_skill" },
                "source_flavor",
                false);
            var session = new BattleSession(
                new DiningTable(1, 1),
                Database(source),
                new FirstRandomStream(),
                new[] { new RecipeSlot("recipe", new[] { source.Id }) },
                0)
            {
                GoldCostPerBellServe = 5,
                BellServeMantouChance = 1f,
            };

            ServePrepareResult prepared = session.PrepareServeFromBell(0);

            Assert.That(prepared.Success, Is.True);
            Assert.That(prepared.PreparedDish.Definition.Name, Is.EqualTo("馒头"));
            Assert.That(prepared.PreparedDish.Definition.BaseId, Is.EqualTo("dumpling"));
            Assert.That(prepared.PreparedDish.Dish.SkillIds, Is.Empty);
            Assert.That(prepared.PreparedDish.Dish.FlavorIds, Is.Empty);
            Assert.That(session.PendingGold, Is.EqualTo(-5f));
            Assert.That(session.ServesUsed, Is.Zero);

            ServeResult served = session.CommitPreparedServe(prepared.PreparedDish.Placements[0]);

            Assert.That(served.Success, Is.True);
            Assert.That(session.PendingGold, Is.EqualTo(-5f), "上菜不能重复触发霸王餐扣费");
            Assert.That(session.ServesUsed, Is.EqualTo(1));
        }

        [Test]
        public void PrepareServe_FromSystemEffectDoesNotTriggerBellEffects()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            DishDef source = Dish("source", "原菜", cell);
            var session = new BattleSession(
                new DiningTable(1, 1),
                Database(source),
                new FirstRandomStream(),
                new[] { new RecipeSlot("recipe", new[] { source.Id }) },
                0)
            {
                GoldCostPerBellServe = 5,
                BellServeMantouChance = 1f,
            };

            ServePrepareResult prepared = session.PrepareServe(0);

            Assert.That(prepared.Success, Is.True);
            Assert.That(prepared.PreparedDish.Definition, Is.SameAs(source));
            Assert.That(session.PendingGold, Is.Zero);
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

        [Test]
        public void DiscardPreparedServe_ConsumesBattleOnlyEntryAndConfiguredUse()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            DishDef dish = Dish("dish", "菜", cell);
            var table = new DiningTable(1, 1);
            var slot = new RecipeSlot("recipe", new[] { dish.Id, dish.Id });
            var session = new BattleSession(
                table,
                Database(dish),
                new FirstRandomStream(),
                new[] { slot },
                0);
            session.ConfigureFoodDiscardLimit(1);
            session.PrepareServe(0);

            bool discarded = session.TryDiscardPreparedServe();

            Assert.That(discarded, Is.True);
            Assert.That(session.PreparedServe, Is.Null);
            Assert.That(session.FoodDiscardsUsed, Is.EqualTo(1));
            Assert.That(session.FoodDiscardsRemaining, Is.Zero);
            Assert.That(session.ServesUsed, Is.Zero);
            Assert.That(table.DishCount, Is.Zero);
            Assert.That(slot.Count, Is.EqualTo(1));
        }

        [Test]
        public void DiscardPreparedServe_RejectsWhenConfiguredUsesAreExhausted()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            DishDef dish = Dish("dish", "菜", cell);
            var slot = new RecipeSlot("recipe", new[] { dish.Id });
            var session = new BattleSession(
                new DiningTable(1, 1),
                Database(dish),
                new FirstRandomStream(),
                new[] { slot },
                0);
            session.ConfigureFoodDiscardLimit(0);
            PreparedServeDish prepared = session.PrepareServe(0).PreparedDish;

            bool discarded = session.TryDiscardPreparedServe();

            Assert.That(discarded, Is.False);
            Assert.That(session.PreparedServe, Is.SameAs(prepared));
            Assert.That(session.FoodDiscardsUsed, Is.Zero);
        }

        [Test]
        public void Appetizer_RemovesOnlyFirstTwoDishesAfterTheyAreServed()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            DishDef dish = Dish("dish", "菜", cell);
            var session = new BattleSession(
                new DiningTable(1, 1),
                Database(dish),
                new FirstRandomStream(),
                new[] { new RecipeSlot("recipe", new[] { dish.Id, dish.Id, dish.Id }) },
                0)
            {
                RemoveFirstServedDishes = true,
                FirstServedDishesToRemove = 2,
            };

            ServeResult first = ServeNext(session);
            ServeResult second = ServeNext(session);
            ServeResult third = ServeNext(session);

            Assert.That(first.RemovedAfterServe, Is.True);
            Assert.That(second.RemovedAfterServe, Is.True);
            Assert.That(third.RemovedAfterServe, Is.False);
            Assert.That(session.DiningTable.DishCount, Is.EqualTo(1));
            Assert.That(session.ServesUsed, Is.EqualTo(3));
        }

        private static ServeResult ServeNext(BattleSession session)
        {
            PreparedServeDish prepared = session.PrepareServe(0).PreparedDish;
            return session.CommitPreparedServe(prepared.Placements[0]);
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
