using System;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class RecipeFlavorEffectSourceTests
    {
        [Test]
        public void UnservedSour_MultipliesEveryScoringDishAtSettlementStart()
        {
            FlavorDef sour = Flavor("t_sour", FlavorEffectType.SourRecipeMult, 1.5f);
            TestContext context = CreateContext("pending_sour", sour);

            ScoreResult result = Calculate(context);

            Assert.That(ScoreOf(result, context.First).Multiplier, Is.EqualTo(1.5f));
            Assert.That(ScoreOf(result, context.Second).Multiplier, Is.EqualTo(1.5f));
            Assert.That(result.Total, Is.EqualTo(30));
            AssertSettlementStartLines(result, sour.Id, 2);
        }

        [Test]
        public void UnservedSalty_AddsTwentyBaseScoreToEveryScoringDishWithoutGrantingGold()
        {
            FlavorDef salty = Flavor("t_salty", FlavorEffectType.SaltyRecipeFlat, 20f);
            TestContext context = CreateContext("pending_salty", salty);

            ScoreResult result = Calculate(context);

            Assert.That(ScoreOf(result, context.First).FlatBonus, Is.EqualTo(20f));
            Assert.That(ScoreOf(result, context.Second).FlatBonus, Is.EqualTo(20f));
            Assert.That(result.GoldDelta, Is.Zero);
            Assert.That(result.Total, Is.EqualTo(60));
            AssertSettlementStartLines(result, salty.Id, 2);
        }

        private static void AssertSettlementStartLines(ScoreResult result, string flavorId, int count)
        {
            ScoreLine[] lines = result.ScoreLines.Where(line => line.Source.Id == flavorId).ToArray();
            Assert.That(lines, Has.Length.EqualTo(count));
            Assert.That(lines.All(line => line.Phase == ScorePhase.BeforeAll), Is.True);

            int firstDishBase = Array.FindIndex(
                result.ScoreLines.ToArray(),
                line => line.Kind == ScoreLineKind.DishBase);
            int lastFlavor = Array.FindLastIndex(
                result.ScoreLines.ToArray(),
                line => line.Source.Id == flavorId);
            Assert.That(lastFlavor, Is.LessThan(firstDishBase));
        }

        private static ScoreResult Calculate(TestContext context)
        {
            return new ScoreCalculator().Calculate(
                context.Table,
                context.Database,
                unservedRecipeDishes: new[]
                {
                    new UnservedRecipeDish(0, context.Pending.Id),
                });
        }

        private static TestContext CreateContext(string pendingId, FlavorDef flavor)
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            DishDef firstDef = Dish("served_first", shape);
            DishDef secondDef = Dish("served_second", shape);
            DishDef pending = Dish(pendingId, shape, flavor.Id);
            DishInstance first = Instance(1, firstDef, shape, 0);
            DishInstance second = Instance(2, secondDef, shape, 1);
            first.SetSourceSlotIndex(0);
            second.SetSourceSlotIndex(1);

            var table = new DiningTable(2, 1);
            table.Place(first);
            table.Place(second);
            var database = new GameplayDatabase(
                new[] { firstDef, secondDef, pending },
                Array.Empty<SkillDef>(),
                new[] { flavor },
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());

            return new TestContext(table, database, first, second, pending);
        }

        private static DishScore ScoreOf(ScoreResult result, DishInstance dish)
            => result.DishScores.Single(score => score.DishInstanceId == dish.Id);

        private static FlavorDef Flavor(string id, FlavorEffectType type, float value)
        {
            return new FlavorDef(
                id,
                id,
                string.Empty,
                type,
                new[] { value },
                Array.Empty<string>(),
                string.Empty);
        }

        private static DishDef Dish(string id, DishShape shape, string flavorId = null)
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
                flavorId ?? string.Empty,
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

        private readonly struct TestContext
        {
            public TestContext(
                DiningTable table,
                GameplayDatabase database,
                DishInstance first,
                DishInstance second,
                DishDef pending)
            {
                Table = table;
                Database = database;
                First = first;
                Second = second;
                Pending = pending;
            }

            public DiningTable Table { get; }

            public GameplayDatabase Database { get; }

            public DishInstance First { get; }

            public DishInstance Second { get; }

            public DishDef Pending { get; }
        }
    }
}
