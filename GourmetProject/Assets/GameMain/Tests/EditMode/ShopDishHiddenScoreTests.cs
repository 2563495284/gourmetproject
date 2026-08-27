#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ShopDishHiddenScoreTests
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
        public void ResolveShopDishHiddenScore_AddsConfiguredOffsetWithoutChangingRewardScore()
        {
            GameRun run = CreateRun();

            int rewardHidden = HiddenScoreService.DishHiddenScore(run, run.LastActionContext);
            int shopHidden = ShopService.ResolveShopDishHiddenScore(_tables, run);

            Assert.That(rewardHidden, Is.EqualTo(20));
            Assert.That(_tables.TbGameBase.ShopDishHiddenScoreOffset, Is.EqualTo(5));
            Assert.That(shopHidden, Is.EqualTo(25));
            Assert.That(
                HiddenScoreService.DishHiddenScore(run, run.LastActionContext),
                Is.EqualTo(20));
        }

        [Test]
        public void RollStock_CanSelectDishTierUnlockedByShopOffset()
        {
            GameRun run = CreateRun();

            List<ShopEntry> stock = ShopService.RollStock(
                _tables,
                run,
                new LastCandidateRandomStream(),
                new LastCandidateRandomStream());
            List<cfg.DishVariant> dishVariants = stock
                .Where(entry => entry.Kind == ShopEntryKind.Dish)
                .Select(entry => _tables.TbDishVariant.GetOrDefault(entry.Id))
                .ToList();

            Assert.That(dishVariants, Has.Count.EqualTo(_tables.TbGameBase.ShopFoodSaleSlotCount));
            Assert.That(dishVariants, Has.All.Not.Null);
            Assert.That(dishVariants.Select(variant => variant.HiddenRange.Min), Has.All.EqualTo(23));
        }

        [Test]
        public void RollRestockEntry_CanSelectDishTierUnlockedByShopOffset()
        {
            GameRun run = CreateRun();

            ShopEntry entry = ShopService.RollRestockEntry(
                _tables,
                run,
                ShopEntryKind.Dish,
                new LastCandidateRandomStream(),
                new LastCandidateRandomStream());
            cfg.DishVariant variant = _tables.TbDishVariant.GetOrDefault(entry?.Id);

            Assert.That(entry, Is.Not.Null);
            Assert.That(variant, Is.Not.Null);
            Assert.That(variant.HiddenRange.Min, Is.EqualTo(23));
        }

        private GameRun CreateRun()
        {
            var run = new GameRun(
                _tables,
                _database,
                "glutton_dog",
                "shop-dish-hidden-score-test",
                weekIndex: 1);
            run.CurrentDay = 0f;
            return run;
        }

        private sealed class LastCandidateRandomStream : IRandomStream
        {
            public RngState State { get; set; }

            public uint NextUInt() => 0u;

            public ulong NextULong() => 0UL;

            public int Range(int minInclusive, int maxExclusive)
                => maxExclusive > minInclusive ? maxExclusive - 1 : minInclusive;

            public float Range(float minInclusive, float maxExclusive) => minInclusive;

            public float NextFloat() => 0f;

            public double NextDouble() => 0d;

            public bool NextBool(double probability = 0.5d) => probability > 0d;

            public void Shuffle<T>(IList<T> list)
            {
            }

            public T Pick<T>(IReadOnlyList<T> list) => list[list.Count - 1];

            public int WeightedPickIndex(IReadOnlyList<float> weights) => weights.Count - 1;
        }
    }
}
#endif
