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
    public sealed class ChukaIchibanEventRewardTests
    {
        private static readonly string[] SeasoningBoxIds =
        {
            "item_active_season_sweet",
            "item_active_season_bitter",
            "item_active_season_umami",
            "item_active_season_numb",
            "item_active_season_sour",
            "item_active_season_salty",
        };

        private static readonly string ExpectedEffectParam =
            $"Ids:{string.Join(",", SeasoningBoxIds)}|Replace";

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
        public void ChukaIchibanRewards_LoadWithFinalLabelsAndImplementedEffects()
        {
            cfg.EventOption friedRice = _tables.TbEventOption.Get("opt_chuka_ichiban_fried_rice");
            cfg.EventOption friedRiceReward = _tables.TbEventOption.Get("opt_chuka_ichiban_fried_rice_reward");
            cfg.EventOption mapoTofu = _tables.TbEventOption.Get("opt_chuka_ichiban_mapo_tofu");
            cfg.EventOption mapoTofuReward = _tables.TbEventOption.Get("opt_chuka_ichiban_mapo_tofu_reward");
            cfg.EventOption dumpling = _tables.TbEventOption.Get("opt_chuka_ichiban_dumpling");
            cfg.EventOption dumplingReward = _tables.TbEventOption.Get("opt_chuka_ichiban_dumpling_reward");

            Assert.That(friedRice.Text, Is.EqualTo("黄金炒饭"));
            Assert.That(mapoTofu.Text, Is.EqualTo("麻婆豆腐"));
            Assert.That(dumpling.Text, Is.EqualTo("大宇宙烧卖"));
            AssertRootOption(friedRice);
            AssertRootOption(mapoTofu);
            AssertRootOption(dumpling);

            AssertEffect(friedRiceReward, cfg.EffectType.GainGold, 80f, "-");
            AssertEffect(mapoTofuReward, cfg.EffectType.GrantRandomActiveItems, 2f, ExpectedEffectParam);
            AssertEffect(dumplingReward, cfg.EffectType.RestoreHearts, 1f, "-");
            AssertRewardOption(friedRiceReward, friedRice.Id);
            AssertRewardOption(mapoTofuReward, mapoTofu.Id);
            AssertRewardOption(dumplingReward, dumpling.Id);
            Assert.That(mapoTofuReward.Text, Is.EqualTo("获得2个风味强化箱"));
            Assert.That(
                mapoTofuReward.ResultText,
                Is.EqualTo("你从“酥”的灵感中举一反三，从六种风味强化箱中随机获得2个，可能重复。"));
        }

        [Test]
        public void MapoTofuReward_QueuesPersistsAndClaimsTwoSeasoningBoxesWithReplacement()
        {
            cfg.EventOption option = _tables.TbEventOption.Get("opt_chuka_ichiban_mapo_tofu_reward");
            GameRun run = CreateRun();
            run.ReplaceItems(Array.Empty<string>());

            string feedback = EffectResolver.Apply(
                run,
                option.EffectTypes.Single(),
                option.EffectValues.Single(),
                option.EffectParams.Single(),
                new FirstWeightedRandomStream(20260813UL));

            StringAssert.Contains("2 个消耗品", feedback);
            Assert.That(run.HasPendingGenericRewards, Is.True);

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            Assert.That(
                restored.TryPeekPendingGenericReward(out _, out _, out RewardOffer offer),
                Is.True);

            RewardChoiceGroup group = offer.FixedGroups.Single();
            var allowedIds = new HashSet<string>(SeasoningBoxIds, StringComparer.Ordinal);
            Assert.That(group.Choices, Has.Count.EqualTo(2));
            Assert.That(group.RequiredChoiceCount, Is.EqualTo(2));
            Assert.That(group.Choices.All(choice => allowedIds.Contains(choice.Id)), Is.True);
            Assert.That(
                group.Choices.All(choice => choice.Kind == cfg.RewardKind.ActiveItemStrengthen),
                Is.True);
            Assert.That(
                group.Choices.Select(choice => choice.Id).Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(1),
                "放回抽取应允许两次获得相同风味箱。");

            for (int index = 0; index < group.Choices.Count; index++)
            {
                Assert.That(
                    RewardGranter.TryClaimChoice(restored, group.Choices[index], out _),
                    Is.True);
                group.MarkClaimed(index);
            }

            Assert.That(group.IsResolved, Is.True);
            Assert.That(restored.GetItemCount(group.Choices[0].Id), Is.EqualTo(2));
            Assert.That(restored.ActiveItemCount, Is.EqualTo(2));
        }

        private static void AssertEffect(
            cfg.EventOption option,
            cfg.EffectType expectedType,
            float expectedValue,
            string expectedParam)
        {
            Assert.That(option.EffectTypes, Is.EqualTo(new[] { expectedType }));
            Assert.That(option.EffectValues, Is.EqualTo(new[] { expectedValue }));
            Assert.That(option.EffectParams, Is.EqualTo(new[] { expectedParam }));
        }

        private static void AssertRootOption(cfg.EventOption option)
        {
            Assert.That(option.ParentId, Is.Empty);
            Assert.That(option.AutoEnd, Is.False);
            AssertEffect(option, cfg.EffectType.None, 0f, "-");
        }

        private static void AssertRewardOption(cfg.EventOption option, string expectedParentId)
        {
            Assert.That(option.ParentId, Is.EqualTo(expectedParentId));
            Assert.That(option.AutoEnd, Is.True);
        }

        private GameRun CreateRun()
        {
            return new GameRun(
                _tables,
                _database,
                "glutton_dog",
                "chuka-ichiban-event-test",
                weekIndex: 1);
        }

        private sealed class FirstWeightedRandomStream : IRandomStream
        {
            private readonly Xoshiro256SS _inner;

            public FirstWeightedRandomStream(ulong seed)
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

            public int Range(int minInclusive, int maxExclusive) =>
                _inner.Range(minInclusive, maxExclusive);

            public float Range(float minInclusive, float maxExclusive) =>
                _inner.Range(minInclusive, maxExclusive);

            public float NextFloat() => _inner.NextFloat();

            public double NextDouble() => _inner.NextDouble();

            public bool NextBool(double probability = 0.5) => _inner.NextBool(probability);

            public void Shuffle<T>(IList<T> list) => _inner.Shuffle(list);

            public T Pick<T>(IReadOnlyList<T> list) => _inner.Pick(list);

            public int WeightedPickIndex(IReadOnlyList<float> weights) => 0;
        }
    }
}
