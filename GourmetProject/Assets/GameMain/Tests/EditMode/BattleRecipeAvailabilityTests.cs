using System;
using System.Collections.Generic;
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
