using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Library;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class RecipeRollerTests
    {
        [Test]
        public void Roll_SelectsWeightedPlanAndRollsConfiguredCountPerGroup()
        {
            RecipeDef recipe = Recipe(
                new[]
                {
                    Group("a", Entry("a1", 10f, 1), Entry("a2", 5f, 0)),
                    Group("b", Entry("b1", 3f, 0)),
                    Group("c", Entry("c1", 7f, 0)),
                },
                new RecipeRollPlanDef("first", 25f, new[] { 1, 2, 0 }),
                new RecipeRollPlanDef("second", 75f, new[] { 2, 1, 1 }));
            var stream = new RecordingRandomStream(1, 0, 0, 0, 0);

            List<string> result = RecipeRoller.Roll(recipe, EmptyDatabase(), stream);

            Assert.That(result, Is.EqualTo(new[] { "fixed", "a1", "a2", "b1", "c1" }));
            Assert.That(stream.SeenWeights[0], Is.EqualTo(new[] { 25f, 75f }));
            Assert.That(stream.SeenWeights[1], Is.EqualTo(new[] { 10f, 5f }));
            Assert.That(stream.SeenWeights[2], Is.EqualTo(new[] { 5f }));
        }

        [Test]
        public void Roll_ThrowsWhenMaxCountsCannotSatisfyPlan()
        {
            RecipeDef recipe = Recipe(
                new[] { Group("limited", Entry("only", 1f, 1)) },
                new RecipeRollPlanDef("too_many", 1f, new[] { 2 }));

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => RecipeRoller.Roll(recipe, EmptyDatabase(), new RecordingRandomStream(0, 0)));

            Assert.That(error.Message, Does.Contain("maxCount"));
            Assert.That(error.Message, Does.Contain("limited"));
        }

        [Test]
        public void Roll_ThrowsWhenPlanCountLengthDoesNotMatchGroups()
        {
            RecipeDef recipe = Recipe(
                new[] { Group("a", Entry("a1", 1f, 0)), Group("b", Entry("b1", 1f, 0)) },
                new RecipeRollPlanDef("invalid", 1f, new[] { 1 }));

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => RecipeRoller.Roll(recipe, EmptyDatabase(), new RecordingRandomStream(0)));

            Assert.That(error.Message, Does.Contain("2 个小组"));
        }

        private static RecipeDef Recipe(
            IReadOnlyList<RecipeGroupDef> groups,
            params RecipeRollPlanDef[] plans)
        {
            return new RecipeDef("recipe", new[] { "fixed" }, groups, plans);
        }

        private static RecipeGroupDef Group(string id, params RecipeEntryDef[] entries)
        {
            return new RecipeGroupDef(id, entries);
        }

        private static RecipeEntryDef Entry(string id, float weight, int maxCount)
        {
            return new RecipeEntryDef(id, weight, maxCount);
        }

        private static GameplayDatabase EmptyDatabase()
        {
            return new GameplayDatabase(
                Array.Empty<DishDef>(),
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
        }

        private sealed class RecordingRandomStream : IRandomStream
        {
            private readonly Queue<int> _picks;

            public RecordingRandomStream(params int[] picks)
            {
                _picks = new Queue<int>(picks);
            }

            public List<float[]> SeenWeights { get; } = new List<float[]>();

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

            public int WeightedPickIndex(IReadOnlyList<float> weights)
            {
                var copy = new float[weights.Count];
                for (int i = 0; i < weights.Count; i++)
                {
                    copy[i] = weights[i];
                }

                SeenWeights.Add(copy);
                return _picks.Dequeue();
            }
        }
    }
}
