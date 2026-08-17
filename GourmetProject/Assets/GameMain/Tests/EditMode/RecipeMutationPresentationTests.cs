using System.Collections.Generic;
using System.IO;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class RecipeMutationPresentationTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            string configDirectory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name =>
                JSON.Parse(File.ReadAllText(Path.Combine(configDirectory, name + ".json"))));
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void RecipeMutationEntry_EmptyAfter_IsRemove()
        {
            var entry = new RecipeMutationEntry
            {
                Before = new RecipeDishSnapshot { DishId = "arrow_cookie_up" },
                After = new RecipeDishSnapshot(),
            };

            Assert.That(entry.IsRemove, Is.True);
            Assert.That(entry.IsAdd, Is.False);
            Assert.That(entry.HasBeforeDish, Is.True);
            Assert.That(entry.HasAfterDish, Is.False);
        }

        [Test]
        public void CopyRandomFood_UsesCopyFlyPresentation()
        {
            GameRun run = CreateRun("copy-fly");
            Assert.That(run.RecipeEntries.Count, Is.GreaterThan(0));

            RecipeMutationResult result = PassiveRecipeMutationService.CopyRandomFood(
                run,
                "复制",
                1,
                new FirstRandomStream(1));

            Assert.That(result.HasChanges, Is.True);
            Assert.That(result.Presentation, Is.EqualTo(RecipeMutationPresentation.CopyFly));
            Assert.That(result.Entries[0].IsAdd, Is.True);
        }

        [Test]
        public void AddRandomFlavors_UsesFlavorStagePresentation()
        {
            GameRun run = CreateRun("flavor-stage");
            RecipeMutationResult result = PassiveRecipeMutationService.AddRandomFlavors(
                run,
                "加风味",
                1,
                new FirstRandomStream(2));

            Assert.That(result.Presentation, Is.EqualTo(RecipeMutationPresentation.FlavorStage));
        }

        [Test]
        public void RemoveArrowCookies_ProducesRemoveEntriesOnBookPresentation()
        {
            GameRun run = CreateRun("remove-cookies");
            int beforeCount = run.RecipeEntries.Count;
            RecipeMutationResult result = PassiveRecipeMutationService.RemoveArrowCookies(
                run,
                "饼干回收盒",
                2);

            Assert.That(result.Presentation, Is.EqualTo(RecipeMutationPresentation.Book));
            if (!result.HasChanges)
            {
                Assert.That(run.RecipeEntries.Count, Is.EqualTo(beforeCount));
                return;
            }

            Assert.That(result.Entries, Has.All.Matches<RecipeMutationEntry>(entry => entry.IsRemove));
            Assert.That(run.RecipeEntries.Count, Is.EqualTo(beforeCount - result.Entries.Count));
        }

        [Test]
        public void RemoveRandomRecipeDish_ProducesRemoveMutation()
        {
            GameRun run = CreateRun("event-remove");
            int beforeCount = run.RecipeEntries.Count;
            Assert.That(beforeCount, Is.GreaterThan(0));

            string feedback = EffectResolver.Apply(
                run,
                cfg.EffectType.RemoveRandomRecipeDish,
                1f,
                string.Empty,
                new FirstRandomStream(3),
                out RecipeMutationResult mutation);

            Assert.That(feedback, Does.Contain("随机献上了"));
            Assert.That(mutation, Is.Not.Null);
            Assert.That(mutation.HasChanges, Is.True);
            Assert.That(mutation.Presentation, Is.EqualTo(RecipeMutationPresentation.Book));
            Assert.That(mutation.Entries, Has.Count.EqualTo(1));
            Assert.That(mutation.Entries[0].IsRemove, Is.True);
            Assert.That(mutation.BeforeRecipe, Has.Count.EqualTo(beforeCount));
            Assert.That(mutation.AfterRecipe, Has.Count.EqualTo(beforeCount - 1));
            Assert.That(run.RecipeEntries.Count, Is.EqualTo(beforeCount - 1));
        }

        private GameRun CreateRun(string seed)
        {
            return new GameRun(_tables, _database, "glutton_dog", seed, weekIndex: 1);
        }

        private sealed class FirstRandomStream : IRandomStream
        {
            private readonly Xoshiro256SS _inner;

            public FirstRandomStream(ulong seed)
            {
                _inner = new Xoshiro256SS(seed);
            }

            public RngState State
            {
                get => _inner.State;
                set => _inner.State = value;
            }

            public uint NextUInt() => _inner.NextUInt();

            public ulong NextULong() => _inner.NextULong();

            public int Range(int minInclusive, int maxExclusive) => minInclusive;

            public float Range(float minInclusive, float maxExclusive) => minInclusive;

            public float NextFloat() => 0f;

            public double NextDouble() => 0d;

            public bool NextBool(double probability = 0.5) => probability > 0d;

            public void Shuffle<T>(IList<T> list)
            {
            }

            public T Pick<T>(IReadOnlyList<T> list) => list[0];

            public int WeightedPickIndex(IReadOnlyList<float> weights) => 0;
        }
    }
}
