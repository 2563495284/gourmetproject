#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using BreakInfinity;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class PassiveRecipeMutationTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            string directory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name =>
                JSON.Parse(File.ReadAllText(Path.Combine(directory, name + ".json"))));
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void RemoveFlavorDoubleScore_DoublesWithFlatScoreAndLeavesMultiplierUnchanged()
        {
            var run = new GameRun(_tables, _database, "glutton_dog", "passive-recipe-mutation-test");
            int targetIndex = run.RecipeEntries.Count;
            Assert.That(run.AddBonusDish("jelly", "t_sweet"), Is.True);
            Assert.That(run.AddRecipeScoreFlat(targetIndex, 7f), Is.True);
            Assert.That(run.MultiplyRecipeScore(targetIndex, 1.5f), Is.True);

            RecipeBookSlot slot = run.RecipeEntries[targetIndex];
            DishDef beforeDish = _database.GetDish(slot.DishId);
            BigDouble beforeFlat = slot.ScoreFlatBonus;
            BigDouble beforeMultiplier = slot.ScoreMultiplier;
            BigDouble beforeScore = (beforeDish.Deliciousness + beforeFlat) * beforeMultiplier;

            RecipeMutationResult result = PassiveRecipeMutationService.RemoveFlavorDoubleScore(
                run,
                "小石磨",
                2f,
                new LastRangeRandomStream());

            DishDef afterDish = _database.GetDish(slot.DishId);
            BigDouble afterScore = (afterDish.Deliciousness + slot.ScoreFlatBonus) * slot.ScoreMultiplier;
            Assert.That(result.HasChanges, Is.True);
            Assert.That(result.Entries, Has.Count.EqualTo(1));
            Assert.That(result.Entries[0].DishIndex, Is.EqualTo(targetIndex));
            Assert.That(run.GetRecipeFlavorIds(targetIndex), Is.Empty);
            Assert.That(slot.ScoreMultiplier, Is.EqualTo(beforeMultiplier));
            Assert.That(slot.ScoreFlatBonus, Is.EqualTo(beforeFlat + beforeDish.Deliciousness + beforeFlat));
            Assert.That(afterScore.ToDouble(), Is.EqualTo((beforeScore * 2f).ToDouble()).Within(1e-9));
        }

        private sealed class LastRangeRandomStream : IRandomStream
        {
            public RngState State { get; set; }

            public uint NextUInt() => 0u;

            public ulong NextULong() => 0UL;

            public int Range(int minInclusive, int maxExclusive)
                => maxExclusive > minInclusive ? maxExclusive - 1 : minInclusive;

            public float Range(float minInclusive, float maxExclusive) => minInclusive;

            public float NextFloat() => 0f;

            public double NextDouble() => 0d;

            public bool NextBool(double probability = 0.5) => probability > 0d;

            public void Shuffle<T>(IList<T> list)
            {
            }

            public T Pick<T>(IReadOnlyList<T> list) => list[list.Count - 1];

            public int WeightedPickIndex(IReadOnlyList<float> weights) => weights.Count - 1;
        }
    }
}
#endif
