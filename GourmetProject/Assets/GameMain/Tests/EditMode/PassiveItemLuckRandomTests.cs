using System;
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
    public sealed class PassiveItemLuckRandomTests
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
        public void ItemLuck_GrowsByWeekAndDayAndKeepsExistingOffsets()
        {
            GameRun opening = CreateRun("luck-opening", weekIndex: 1);
            opening.CurrentDay = 1f;
            Assert.That(ItemLuckService.GetLuck(opening), Is.EqualTo(0f).Within(0.0001f));

            ItemAcquireResult acquired = opening.AcquireItem(
                "item_passive_hidden_bonus",
                fallbackGold: 0,
                fireOnAcquire: false);
            Assert.That(acquired.Outcome, Is.EqualTo(ItemAcquireOutcome.Added));
            Assert.That(ItemLuckService.GetLuck(opening), Is.EqualTo(10f).Within(0.0001f));

            opening.AddEventHiddenScoreOffset(HiddenScorePurpose.ItemLuck, -3f);
            Assert.That(ItemLuckService.GetLuck(opening), Is.EqualTo(7f).Within(0.0001f));

            GameRun ending = CreateRun("luck-ending", weekIndex: 3);
            ending.CurrentDay = 7f;
            Assert.That(ItemLuckService.GetLuck(ending), Is.EqualTo(26f).Within(0.0001f));
        }

        [Test]
        public void QualityWeights_MatchConfiguredOpeningAndWeekThreeEndProbabilities()
        {
            AssertProbabilities(
                luck: 0f,
                new Dictionary<cfg.ItemQuality, double>
                {
                    [cfg.ItemQuality.Common] = 0.58d,
                    [cfg.ItemQuality.Uncommon] = 0.28d,
                    [cfg.ItemQuality.Rare] = 0.11d,
                    [cfg.ItemQuality.Epic] = 0.03d,
                });
            AssertProbabilities(
                luck: 26f,
                new Dictionary<cfg.ItemQuality, double>
                {
                    [cfg.ItemQuality.Common] = 0.28d,
                    [cfg.ItemQuality.Uncommon] = 0.34d,
                    [cfg.ItemQuality.Rare] = 0.27d,
                    [cfg.ItemQuality.Epic] = 0.11d,
                });
            Assert.That(
                PassiveItemRandomService.QualityWeight(_tables, cfg.ItemQuality.Epic, 1000f),
                Is.EqualTo(PassiveItemRandomService.QualityWeight(_tables, cfg.ItemQuality.Epic, 100f))
                    .Within(0.0001d));
        }

        [Test]
        public void TwoStageRoll_ExcludesEmptyQualitiesBeforeNormalizing()
        {
            ItemDefinition common = ItemDefinition.All(_tables, cfg.ItemKind.Passive)
                .First(item => item.Quality == cfg.ItemQuality.Common && !item.IsNegative);
            ItemDefinition epic = ItemDefinition.All(_tables, cfg.ItemKind.Passive)
                .First(item => item.Quality == cfg.ItemQuality.Epic && !item.IsNegative);
            var rng = new IndexedWeightedRandomStream(1);

            List<ItemDefinition> picked = PassiveItemRandomService.Roll(
                _tables,
                new[] { common, epic },
                rng,
                count: 1,
                luck: 0f);

            Assert.That(picked.Single().Quality, Is.EqualTo(cfg.ItemQuality.Epic));
            Assert.That(rng.WeightedCalls.Single().Count, Is.EqualTo(2));
        }

        [Test]
        public void TwoStageRoll_UsesBaseWeightOnlyWithinSelectedQuality()
        {
            ItemDefinition[] common = ItemDefinition.All(_tables, cfg.ItemKind.Passive)
                .Where(item => item.Quality == cfg.ItemQuality.Common && !item.IsNegative)
                .GroupBy(item => item.BaseWeight)
                .Select(group => group.First())
                .Take(2)
                .ToArray();
            Assert.That(common, Has.Length.EqualTo(2));
            var rng = new IndexedWeightedRandomStream(1);

            List<ItemDefinition> picked = PassiveItemRandomService.Roll(
                _tables,
                common,
                rng,
                count: 1,
                luck: 26f);

            Assert.That(picked.Single().Id, Is.EqualTo(common[1].Id));
            Assert.That(
                rng.WeightedCalls.Single(),
                Is.EqualTo(common.Select(item => item.BaseWeight).ToArray()));
        }

        [Test]
        public void ItemPool_NormalIsUniqueAndNegativeRequiresExplicitFilter()
        {
            GameRun run = CreateRun("normal-and-negative", weekIndex: 2);
            run.CurrentDay = 4f;
            List<string> normalIds = ItemPoolService.Roll(
                _tables,
                run,
                cfg.ItemKind.Passive,
                new Xoshiro256SS(101UL),
                count: 500);
            List<string> negativeIds = ItemPoolService.Roll(
                _tables,
                run,
                cfg.ItemKind.Passive,
                new Xoshiro256SS(102UL),
                count: 500,
                itemLuck: ItemLuckService.GetLuck(run),
                requiredTag: cfg.ItemSpecialTag.Negative);

            Assert.That(normalIds, Is.Not.Empty);
            Assert.That(normalIds.Distinct().Count(), Is.EqualTo(normalIds.Count));
            Assert.That(normalIds.Select(Passive).All(item =>
                !item.IsNegative && PassiveItemRandomService.IsNormalQuality(_tables, item.Quality)), Is.True);
            Assert.That(negativeIds, Is.Not.Empty);
            Assert.That(negativeIds.Distinct().Count(), Is.EqualTo(negativeIds.Count));
            Assert.That(negativeIds.Select(Passive).All(item => item.IsNegative), Is.True);
        }

        [Test]
        public void RewardLegendaryPool_UsesCurrentHighestQualityEpic()
        {
            GameRun run = CreateRun("highest-quality-pool", weekIndex: 3);
            run.CurrentDay = 7f;
            cfg.RewardSlot slot = _tables.TbRewardSlot.Get("legendary_choice_1");
            var context = new RewardContext(
                _tables,
                run,
                run.CurrentWeek,
                package: null,
                rng: new Xoshiro256SS(103UL));

            List<RewardChoice> choices = RewardPoolService.RollChoices(context, slot);

            Assert.That(choices, Is.Not.Empty);
            Assert.That(choices, Has.All.Matches<RewardChoice>(choice =>
                choice.Kind == cfg.RewardKind.PassiveItemChoice
                && Passive(choice.Id).Quality == cfg.ItemQuality.Epic));
        }

        [Test]
        public void ShopAndEventNormalPools_UseTheSameNormalQualityRules()
        {
            GameRun run = CreateRun("shop-and-event-pools", weekIndex: 2);
            run.CurrentDay = 4f;

            List<ShopEntry> stock = ShopService.RollStock(
                _tables,
                run,
                new Xoshiro256SS(104UL),
                new Xoshiro256SS(105UL));
            ItemDefinition[] shopPassives = stock
                .Where(entry => entry.Kind == ShopEntryKind.PassiveItem)
                .Select(entry => Passive(entry.Id))
                .ToArray();
            List<ItemDefinition> eventPassives = ItemPoolService.RollFiltered(
                _tables,
                run,
                cfg.ItemKind.Passive,
                new Xoshiro256SS(106UL),
                count: 500,
                allowedIds: null,
                activeCategory: null,
                requireNegative: false,
                withReplacement: false);

            Assert.That(shopPassives, Is.Not.Empty);
            Assert.That(eventPassives, Is.Not.Empty);
            Assert.That(shopPassives.Concat(eventPassives).All(item =>
                !item.IsNegative && PassiveItemRandomService.IsNormalQuality(_tables, item.Quality)), Is.True);
            Assert.That(eventPassives.Select(item => item.Id).Distinct().Count(), Is.EqualTo(eventPassives.Count));
        }

        private void AssertProbabilities(
            float luck,
            IReadOnlyDictionary<cfg.ItemQuality, double> expected)
        {
            var weights = expected.Keys.ToDictionary(
                quality => quality,
                quality => PassiveItemRandomService.QualityWeight(_tables, quality, luck));
            double total = weights.Values.Sum();
            foreach (KeyValuePair<cfg.ItemQuality, double> pair in expected)
            {
                Assert.That(
                    weights[pair.Key] / total,
                    Is.EqualTo(pair.Value).Within(0.0001d),
                    pair.Key.ToString());
            }
        }

        private ItemDefinition Passive(string id)
            => ItemDefinition.Get(_tables, id, cfg.ItemKind.Passive);

        private GameRun CreateRun(string seed, int weekIndex)
            => new GameRun(
                _tables,
                _database,
                "glutton_dog",
                seed,
                weekIndex,
                execution: RunExecutionEnvironment.CreateIsolated(_tables, seed));

        private sealed class IndexedWeightedRandomStream : IRandomStream
        {
            private readonly Xoshiro256SS _inner = new Xoshiro256SS(991UL);
            private readonly Queue<int> _indices;

            public IndexedWeightedRandomStream(params int[] indices)
            {
                _indices = new Queue<int>(indices);
            }

            public List<IReadOnlyList<float>> WeightedCalls { get; } =
                new List<IReadOnlyList<float>>();

            public RngState State
            {
                get => _inner.State;
                set => _inner.State = value;
            }

            public uint NextUInt() => _inner.NextUInt();

            public ulong NextULong() => _inner.NextULong();

            public int Range(int minInclusive, int maxExclusive) =>
                _inner.Range(minInclusive, maxExclusive);

            public float Range(float minInclusive, float maxExclusive) =>
                _inner.Range(minInclusive, maxExclusive);

            public float NextFloat() => _inner.NextFloat();

            public double NextDouble() => _inner.NextDouble();

            public bool NextBool(double probability = 0.5) => _inner.NextBool(probability);

            public void Shuffle<T>(IList<T> list) => _inner.Shuffle(list);

            public T Pick<T>(IReadOnlyList<T> list) => _inner.Pick(list);

            public int WeightedPickIndex(IReadOnlyList<float> weights)
            {
                WeightedCalls.Add(weights.ToArray());
                return _indices.Count > 0 ? _indices.Dequeue() : 0;
            }
        }
    }
}
