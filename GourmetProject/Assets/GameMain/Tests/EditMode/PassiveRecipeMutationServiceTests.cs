using System;
using System.Collections.Generic;
using BreakInfinity;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class PassiveRecipeMutationServiceTests
    {
        private cfg.Tables _tables;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
        }

        [Test]
        public void RandomizeAllRecipeDishes_UsesSameSizeBaseFoodAndPreservesSlotState()
        {
            DishDef sourceBase = Dish("source", "source", new[] { "XXX" }, 10f);
            DishDef sourceSweet = Dish("source_sweet", "source", new[] { "XXX" }, 5f, "t_sweet");
            DishDef eligible = Dish("eligible", "eligible", new[] { "XX", "X." }, 37f);
            DishDef wrongSize = Dish("wrong_size", "wrong_size", new[] { "XX" }, 1000f);
            DishDef flavoredCandidate = Dish(
                "other_bitter",
                "other",
                new[] { "XXX" },
                1000f,
                "t_bitter");
            DishDef zeroWeight = Dish("zero_weight", "zero_weight", new[] { "XXX" }, 0f);
            GameplayDatabase database = Database(
                sourceBase,
                sourceSweet,
                eligible,
                wrongSize,
                flavoredCandidate,
                zeroWeight);
            GameRun run = Run(database, sourceSweet.Id);
            run.AcquireItem("item_flavor_double_slot", fallbackGold: 0);
            Assert.That(run.FoodFlavorLimit, Is.EqualTo(2));
            Assert.That(run.AddRecipeFlavor(0, "t_sour"), Is.True);
            Assert.That(run.AddRecipeExtraSkill(0, "extra_skill"), Is.True);
            Assert.That(run.AddRecipeScoreFlat(0, new BigDouble(15)), Is.True);
            Assert.That(run.MultiplyRecipeScore(0, new BigDouble(2)), Is.True);
            var rng = new SelectWeightRandomStream(37f);

            RecipeMutationResult result = PassiveRecipeMutationService.RandomizeAllRecipeDishes(
                run,
                "万花筒菜单板",
                rng);

            Assert.That(result.Entries, Has.Count.EqualTo(1));
            Assert.That(result.Entries[0].Before.DishId, Is.EqualTo(sourceSweet.Id));
            Assert.That(result.Entries[0].After.DishId, Is.EqualTo(eligible.Id));
            Assert.That(run.RecipeEntries[0].DishId, Is.EqualTo(eligible.Id));
            Assert.That(run.GetRecipeFlavorIds(0), Is.EqualTo(new[] { "t_sweet", "t_sour" }));
            Assert.That(run.RecipeEntries[0].ExtraFlavorIds, Is.EqualTo(new[] { "t_sweet", "t_sour" }));
            Assert.That(run.RecipeEntries[0].ExtraSkillIds, Is.EqualTo(new[] { "extra_skill" }));
            Assert.That(run.RecipeEntries[0].ScoreFlatBonus.ToDouble(), Is.EqualTo(15d));
            Assert.That(run.RecipeEntries[0].ScoreMultiplier.ToDouble(), Is.EqualTo(2d));
            Assert.That(rng.LastWeights, Is.EqualTo(new[] { 37f }));
        }

        [Test]
        public void RandomizeAllRecipeDishes_NoEligibleCandidateLeavesDishUntouched()
        {
            DishDef source = Dish("source", "source", new[] { "XXX" }, 10f);
            DishDef sourceVariant = Dish("source_sweet", "source", new[] { "XXX" }, 1000f, "t_sweet");
            DishDef wrongSize = Dish("wrong_size", "wrong_size", new[] { "XX" }, 1000f);
            DishDef otherVariant = Dish("other_sour", "other", new[] { "XXX" }, 1000f, "t_sour");
            DishDef zeroWeight = Dish("zero_weight", "zero_weight", new[] { "XXX" }, 0f);
            GameplayDatabase database = Database(source, sourceVariant, wrongSize, otherVariant, zeroWeight);
            GameRun run = Run(database, source.Id);
            Assert.That(run.AddRecipeFlavor(0, "t_numb"), Is.True);
            var rng = new SelectWeightRandomStream(1f);

            RecipeMutationResult result = PassiveRecipeMutationService.RandomizeAllRecipeDishes(
                run,
                "万花筒菜单板",
                rng);

            Assert.That(result.HasChanges, Is.False);
            Assert.That(run.RecipeEntries[0].DishId, Is.EqualTo(source.Id));
            Assert.That(run.GetRecipeFlavorIds(0), Is.EqualTo(new[] { "t_numb" }));
            Assert.That(rng.CallCount, Is.Zero);
        }

        [Test]
        public void RandomizeRecipeDishes_ConfigDescribesSizeAndFlavorRules()
        {
            string description = _tables.TbPassiveItem.Get("item_randomize_recipe_dishes").Desc;

            StringAssert.Contains("占格数相同", description);
            StringAssert.Contains("保留原有风味", description);
        }

        private GameRun Run(GameplayDatabase database, string dishId)
        {
            var run = new GameRun(_tables, database, string.Empty, "randomize-recipe-dishes-test");
            Assert.That(run.AddBonusDish(dishId), Is.True);
            return run;
        }

        private static GameplayDatabase Database(params DishDef[] dishes)
        {
            return new GameplayDatabase(
                dishes,
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
        }

        private static DishDef Dish(
            string id,
            string baseId,
            IReadOnlyList<string> shapeRows,
            float baseWeight,
            string flavorId = "")
        {
            return new DishDef(
                id,
                id,
                10,
                DishShape.FromRows(shapeRows),
                0,
                100,
                baseWeight,
                Array.Empty<string>(),
                flavorId,
                baseId);
        }

        private sealed class SelectWeightRandomStream : IRandomStream
        {
            private readonly float _selectedWeight;

            public SelectWeightRandomStream(float selectedWeight)
            {
                _selectedWeight = selectedWeight;
            }

            public RngState State { get; set; }

            public IReadOnlyList<float> LastWeights { get; private set; } = Array.Empty<float>();

            public int CallCount { get; private set; }

            public int WeightedPickIndex(IReadOnlyList<float> weights)
            {
                CallCount++;
                LastWeights = new List<float>(weights);
                for (int i = 0; i < weights.Count; i++)
                {
                    if (Math.Abs(weights[i] - _selectedWeight) < 0.0001f)
                    {
                        return i;
                    }
                }

                Assert.Fail($"Expected candidate weight {_selectedWeight} was not present.");
                return 0;
            }

            public uint NextUInt() => throw new NotSupportedException();

            public ulong NextULong() => throw new NotSupportedException();

            public int Range(int minInclusive, int maxExclusive) => throw new NotSupportedException();

            public float Range(float minInclusive, float maxExclusive) => throw new NotSupportedException();

            public float NextFloat() => throw new NotSupportedException();

            public double NextDouble() => throw new NotSupportedException();

            public bool NextBool(double probability = 0.5) => throw new NotSupportedException();

            public void Shuffle<T>(IList<T> list) => throw new NotSupportedException();

            public T Pick<T>(IReadOnlyList<T> list) => throw new NotSupportedException();
        }
    }
}
