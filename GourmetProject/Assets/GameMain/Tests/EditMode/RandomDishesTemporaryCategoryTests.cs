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
    public sealed class RandomDishesTemporaryCategoryTests
    {
        [Test]
        public void EnoughNonCategoryDishes_SelectsOnlyFromPreferredPool()
        {
            CategoryFixture fixture = CreateFixture("cake", string.Empty, string.Empty, string.Empty);
            var calls = new List<string>();
            ScoreContext context = fixture.CreateContext((minimum, maximum) =>
            {
                calls.Add($"{minimum}:{maximum}");
                return maximum;
            });

            ApplyRandomCakeCategory(context);

            Assert.That(context.IsCategory(fixture.Dishes[1], "cake"), Is.True);
            Assert.That(context.IsCategory(fixture.Dishes[2], "cake"), Is.False);
            Assert.That(context.IsCategory(fixture.Dishes[3], "cake"), Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "0:2", "1:2" }));
        }

        [Test]
        public void OnePreferredDish_SelectsItThenConsumesFallbackSelection()
        {
            CategoryFixture fixture = CreateFixture("cake", "cake", string.Empty);
            var calls = new List<string>();
            ScoreContext context = fixture.CreateContext((minimum, maximum) =>
            {
                calls.Add($"{minimum}:{maximum}");
                return maximum;
            });

            ApplyRandomCakeCategory(context);

            Assert.That(context.IsCategory(fixture.Dishes[2], "cake"), Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "0:0", "0:1" }));
        }

        [Test]
        public void AllDishesAlreadyInCategory_UsesFallbackWithoutDuplicatingRuntimeEffects()
        {
            CategoryFixture fixture = CreateFixture("cake", "cake", "cake");
            var calls = new List<string>();
            ScoreContext context = fixture.CreateContext((minimum, maximum) =>
            {
                calls.Add($"{minimum}:{maximum}");
                return minimum;
            });

            ApplyRandomCakeCategory(context);

            Assert.That(fixture.Dishes.All(dish => context.IsCategory(dish, "cake")), Is.True);
            Assert.That(fixture.Dishes.All(dish => dish.TemporaryCategoryEffects.Count == 0), Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "0:2", "1:2" }));
        }

        [Test]
        public void FewerDishesThanRequested_SelectsOnlyExistingDishes()
        {
            CategoryFixture fixture = CreateFixture(string.Empty);
            var calls = new List<string>();
            ScoreContext context = fixture.CreateContext((minimum, maximum) =>
            {
                calls.Add($"{minimum}:{maximum}");
                return minimum;
            });

            ApplyRandomCakeCategory(context);

            Assert.That(context.IsCategory(fixture.Dishes[0], "cake"), Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "0:0" }));
        }

        [Test]
        public void FixedSelector_IsDeterministicAndConsumesOneCallPerTarget()
        {
            (bool[] firstSelection, int firstCalls) = ApplyWithFixedSelector();
            (bool[] secondSelection, int secondCalls) = ApplyWithFixedSelector();

            Assert.That(firstSelection, Is.EqualTo(secondSelection));
            Assert.That(firstSelection, Is.EqualTo(new[] { true, false, true }));
            Assert.That(firstCalls, Is.EqualTo(2));
            Assert.That(secondCalls, Is.EqualTo(2));
        }

        [Test]
        public void RepeatedEffects_TreatEarlierLiveCategoriesAsFallback()
        {
            CategoryFixture fixture = CreateFixture(string.Empty, string.Empty, string.Empty, string.Empty);
            int calls = 0;
            ScoreContext context = fixture.CreateContext((minimum, _) =>
            {
                calls++;
                return minimum;
            });

            ApplyRandomCakeCategory(context);
            ApplyRandomCakeCategory(context);

            Assert.That(fixture.Dishes.All(dish => context.IsCategory(dish, "cake")), Is.True);
            Assert.That(calls, Is.EqualTo(4));
        }

        private static (bool[] Selection, int Calls) ApplyWithFixedSelector()
        {
            CategoryFixture fixture = CreateFixture(string.Empty, string.Empty, string.Empty);
            int calls = 0;
            ScoreContext context = fixture.CreateContext((_, maximum) =>
            {
                calls++;
                return maximum;
            });

            ApplyRandomCakeCategory(context);
            bool[] selection = fixture.Dishes
                .Select(dish => context.IsCategory(dish, "cake"))
                .ToArray();
            return (selection, calls);
        }

        private static void ApplyRandomCakeCategory(ScoreContext context)
        {
            var spec = new ItemScoreSpec(
                ItemScoreEffectType.RandomDishesTemporaryCategory,
                2f,
                "category:cake",
                "item_random_two_as_cake",
                "随机蛋糕签");
            new ItemScoreEffect(spec).Apply(context);
        }

        private static CategoryFixture CreateFixture(params string[] categories)
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            var definitions = new List<DishDef>();
            var dishes = new List<DishInstance>();
            var board = new DiningTable(Math.Max(1, categories.Length), 1);
            for (int i = 0; i < categories.Length; i++)
            {
                var definition = new DishDef(
                    $"dish_{i}",
                    $"食物{i}",
                    deliciousness: 1,
                    shape,
                    hiddenMin: 0,
                    hiddenMax: 0,
                    baseWeight: 1f,
                    Array.Empty<string>(),
                    flavorId: string.Empty,
                    category: categories[i]);
                var dish = new DishInstance(
                    i + 1,
                    definition,
                    new Placement(shape, rotationIndex: 0, new GridPos(i, 0)),
                    Array.Empty<string>(),
                    Array.Empty<string>());
                definitions.Add(definition);
                dishes.Add(dish);
                board.Place(dish);
            }

            var database = new GameplayDatabase(
                definitions,
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            return new CategoryFixture(board, database, dishes);
        }

        private sealed class CategoryFixture
        {
            public CategoryFixture(
                DiningTable board,
                GameplayDatabase database,
                IReadOnlyList<DishInstance> dishes)
            {
                Board = board;
                Database = database;
                Dishes = dishes;
            }

            public DiningTable Board { get; }

            public GameplayDatabase Database { get; }

            public IReadOnlyList<DishInstance> Dishes { get; }

            public ScoreContext CreateContext(Func<int, int, int> selector)
                => new ScoreContext(new ScoreSnapshot(
                    Board,
                    Database,
                    randomIntegerSelector: selector,
                    captureDiagnostics: false));
        }
    }
}
