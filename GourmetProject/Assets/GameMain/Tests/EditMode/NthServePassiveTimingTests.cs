using System;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class NthServePassiveTimingTests
    {
        [TestCase("item_first_+2", "index:1", 1)]
        [TestCase("item_last_+2", "index:-1", 2)]
        public void FlatNthServePassiveExecutesBeforeDishBaseAndSkills(
            string itemId,
            string param,
            int expectedTargetId)
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            DishDef firstDef = Dish("first", shape);
            DishDef lastDef = Dish("last", shape);
            DishInstance first = Instance(1, firstDef, shape, 0);
            DishInstance last = Instance(2, lastDef, shape, 1);
            var table = new DiningTable(2, 1);
            table.Place(first);
            table.Place(last);

            var source = new ItemScoreEffectSource(new[]
            {
                new ItemScoreSpec(ItemScoreEffectType.NthServeMultFlat, 2f, param, itemId, itemId),
            });
            var calculator = new ScoreCalculator(effectSources: new[] { source });

            ScoreResult result = calculator.Calculate(table, Database(firstDef, lastDef));

            ScoreLine passiveLine = result.ScoreLines[0];
            Assert.That(passiveLine.Phase, Is.EqualTo(ScorePhase.BeforeAll));
            Assert.That(passiveLine.Source.Type, Is.EqualTo(ScoreSourceType.Relic));
            Assert.That(passiveLine.Source.Id, Is.EqualTo(itemId));
            Assert.That(passiveLine.DishInstanceId, Is.EqualTo(expectedTargetId));
            Assert.That(passiveLine.Before, Is.EqualTo(1f));
            Assert.That(passiveLine.After, Is.EqualTo(3f));
            Assert.That(result.ScoreLines.Skip(1).Any(line => line.Kind == ScoreLineKind.DishBase), Is.True);
            Assert.That(
                result.DishScores.Single(score => score.DishInstanceId == expectedTargetId).Multiplier,
                Is.EqualTo(3f));
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

        private static DishInstance Instance(int id, DishDef def, DishShape shape, int x)
        {
            return new DishInstance(
                id,
                def,
                new Placement(shape, 0, new GridPos(x, 0)),
                Array.Empty<string>(),
                Array.Empty<string>());
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
    }
}
