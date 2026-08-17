using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class CountThresholdMultItemTests
    {
        [Test]
        public void CountLe_AddsMultiplierWhenPortionCountIsAtMostFifteen()
        {
            DishInstance first = Dish(1, "first", 0, countAs: 7);
            DishInstance second = Dish(2, "second", 1, countAs: 8);
            ScoreResult result = Settle(new[] { first, second }, "lte:15", 0.5f);

            Assert.That(result.DishScores.Select(score => score.Multiplier.ToDouble()),
                Is.All.EqualTo(1.5d).Within(0.0001d));
            Assert.That(result.Total.ToDouble(), Is.EqualTo(30d).Within(0.0001d));
            Assert.That(
                result.ScoreLines.Count(line => line.Kind == ScoreLineKind.DishMultiplierAdd),
                Is.EqualTo(2));
            Assert.That(
                result.ScoreLines.Count(line => line.Kind == ScoreLineKind.DishMultiplier),
                Is.EqualTo(0));
        }

        [Test]
        public void CountLe_DoesNotAddMultiplierWhenPortionCountExceedsFifteen()
        {
            DishInstance dish = Dish(1, "heavy", 0, countAs: 16);
            ScoreResult result = Settle(new[] { dish }, "lte:15", 0.5f);

            Assert.That(result.DishScores.Single().Multiplier.ToDouble(), Is.EqualTo(1d).Within(0.0001d));
            Assert.That(result.Total.ToDouble(), Is.EqualTo(10d).Within(0.0001d));
            Assert.That(
                result.ScoreLines.Any(line =>
                    line.Kind == ScoreLineKind.DishMultiplierAdd
                    || line.Kind == ScoreLineKind.DishMultiplier),
                Is.False);
        }

        [Test]
        public void CountGe_AddsMultiplierWhenPortionCountMeetsTwentyFive()
        {
            DishInstance dish = Dish(1, "feast", 0, countAs: 25);
            ScoreResult result = Settle(new[] { dish }, "gte:25", 0.5f);

            Assert.That(result.DishScores.Single().Multiplier.ToDouble(), Is.EqualTo(1.5d).Within(0.0001d));
            Assert.That(result.Total.ToDouble(), Is.EqualTo(15d).Within(0.0001d));
        }

        [Test]
        public void CountGe_DoesNotAddMultiplierWhenPortionCountIsBelowTwentyFive()
        {
            DishInstance dish = Dish(1, "almost", 0, countAs: 24);
            ScoreResult result = Settle(new[] { dish }, "gte:25", 0.5f);

            Assert.That(result.DishScores.Single().Multiplier.ToDouble(), Is.EqualTo(1d).Within(0.0001d));
            Assert.That(result.Total.ToDouble(), Is.EqualTo(10d).Within(0.0001d));
        }

        private static ScoreResult Settle(
            IReadOnlyList<DishInstance> dishes,
            string param,
            float value)
        {
            var table = new DiningTable(Math.Max(1, dishes.Count), 1);
            foreach (DishInstance dish in dishes)
            {
                table.Place(dish);
            }

            var source = new ItemScoreEffectSource(new[]
            {
                new ItemScoreSpec(
                    ItemScoreEffectType.CountThresholdAllDishMult,
                    value,
                    param,
                    "item_count_le_mult",
                    "素色桌旗"),
            });
            return new ScoreCalculator(effectSources: new[] { source }).Calculate(
                table,
                new GameplayDatabase(
                    dishes.Select(dish => dish.Def).ToArray(),
                    Array.Empty<SkillDef>(),
                    Array.Empty<FlavorDef>(),
                    Array.Empty<MaterialDef>(),
                    Array.Empty<RecipeDef>()));
        }

        private static DishInstance Dish(int instanceId, string id, int x, int countAs)
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            var def = new DishDef(
                id, id, 10, shape, 0, 0, 1f,
                Array.Empty<string>(), string.Empty, countAs: countAs);
            return new DishInstance(
                instanceId,
                def,
                new Placement(shape, 0, new GridPos(x, 0)),
                Array.Empty<string>(),
                Array.Empty<string>());
        }
    }
}
