using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class EventCategoryTests
    {
        [Test]
        public void MultiCategory_MatchesEveryConfiguredPool_AndKeepsFirstAsPrimary()
        {
            cfg.GameEvent ev = LoadEvent();
            SetCategories(ev, cfg.ActionBehavior.Event, cfg.ActionBehavior.Reward);

            Assert.That(ev.PrimaryEventType, Is.EqualTo(cfg.ActionBehavior.Event));
            Assert.That(ev.HasEventType(cfg.ActionBehavior.Event), Is.True);
            Assert.That(ev.HasEventType(cfg.ActionBehavior.Reward), Is.True);
            Assert.That(ev.HasEventType(cfg.ActionBehavior.Negative), Is.False);
            Assert.That(ev.IsActionEventPoolMember, Is.True);
        }

        [Test]
        public void CategoryOrder_ChangesPrimaryButNotPoolMembership()
        {
            cfg.GameEvent ev = LoadEvent();
            SetCategories(ev, cfg.ActionBehavior.Reward, cfg.ActionBehavior.Event);

            Assert.That(ev.PrimaryEventType, Is.EqualTo(cfg.ActionBehavior.Reward));
            Assert.That(ev.HasEventType(cfg.ActionBehavior.Event), Is.True);
            Assert.That(ev.HasEventType(cfg.ActionBehavior.Reward), Is.True);
        }

        [Test]
        public void DuplicateCategory_DoesNotChangeMembershipSemantics()
        {
            cfg.GameEvent ev = LoadEvent();
            SetCategories(ev, cfg.ActionBehavior.Event, cfg.ActionBehavior.Event);

            Assert.That(ev.PrimaryEventType, Is.EqualTo(cfg.ActionBehavior.Event));
            Assert.That(ev.HasEventType(cfg.ActionBehavior.Event), Is.True);
            Assert.That(ev.EventTypes.Count, Is.EqualTo(2));
        }

        [Test]
        public void MultiCategory_CanBeRolledFromEveryConfiguredPool()
        {
            var config = new ConfigService();
            config.LoadAll();
            cfg.Tables tables = config.Tables;
            foreach (cfg.GameEvent configuredEvent in tables.TbEvent.DataList)
            {
                configuredEvent.EventTypes.Clear();
            }

            cfg.GameEvent target = tables.TbEvent.Get("ev_midnight_tasting");
            SetCategories(target, cfg.ActionBehavior.Event, cfg.ActionBehavior.Reward);

            var database = GameplayContentBuilder.BuildDatabase(tables);
            var run = new GameRun(
                tables,
                database,
                tables.TbCharacter.DataList[0].Id,
                "multi-category-test");
            var rng = new Xoshiro256SS(123UL);

            Assert.That(ActionRandomService.IsAvailable(run, tables.TbAction.Get("act_event")), Is.True);
            Assert.That(ActionRandomService.IsAvailable(run, tables.TbAction.Get("act_reward")), Is.True);
            Assert.That(EventService.RollEvent(run, rng, cfg.ActionBehavior.Event), Is.SameAs(target));
            Assert.That(EventService.RollEvent(run, rng, cfg.ActionBehavior.Reward), Is.SameAs(target));
            Assert.That(EventService.RollActionEvent(run, rng), Is.SameAs(target));
        }

        [Test]
        public void ConfiguredEvents_HaveAtLeastOneUniqueCategory()
        {
            var config = new ConfigService();
            config.LoadAll();

            foreach (cfg.GameEvent ev in config.Tables.TbEvent.DataList)
            {
                Assert.That(ev.EventTypes, Is.Not.Empty, $"{ev.Id} 必须至少配置一个事件分类");
                Assert.That(
                    new HashSet<cfg.ActionBehavior>(ev.EventTypes).Count,
                    Is.EqualTo(ev.EventTypes.Count),
                    $"{ev.Id} 的 eventTypes 不能包含重复分类");
            }
        }

        [Test]
        public void NonRepeatableMultiCategory_IsRemovedFromEveryPoolAfterUse()
        {
            var config = new ConfigService();
            config.LoadAll();
            cfg.Tables tables = config.Tables;
            foreach (cfg.GameEvent configuredEvent in tables.TbEvent.DataList)
            {
                configuredEvent.EventTypes.Clear();
            }

            cfg.GameEvent target = tables.TbEvent.Get("ev_wishing_casserole_i");
            Assert.That(target.Repeatable, Is.False);
            SetCategories(target, cfg.ActionBehavior.Event, cfg.ActionBehavior.Reward);

            var database = GameplayContentBuilder.BuildDatabase(tables);
            var run = new GameRun(
                tables,
                database,
                tables.TbCharacter.DataList[0].Id,
                "multi-category-repeatable-test");
            run.MarkEventUsed(target.Id);
            var rng = new Xoshiro256SS(456UL);

            Assert.That(EventService.RollEvent(run, rng, cfg.ActionBehavior.Event), Is.Null);
            Assert.That(EventService.RollEvent(run, rng, cfg.ActionBehavior.Reward), Is.Null);
        }

        [Test]
        public void LuckyChance_ActEventBoostsEveryRewardMemberOnce_AndFlashesWhenPicked()
        {
            var config = new ConfigService();
            config.LoadAll();
            cfg.Tables tables = config.Tables;
            cfg.GameEvent[] candidates = tables.TbEvent.DataList
                .Where(ev => ev.Weight > 0f && !ev.Preconditions.Any())
                .Take(4)
                .ToArray();
            Assert.That(candidates, Has.Length.EqualTo(4));

            foreach (cfg.GameEvent configuredEvent in tables.TbEvent.DataList)
            {
                configuredEvent.EventTypes.Clear();
            }
            SetCategories(candidates[0], cfg.ActionBehavior.Event);
            SetCategories(
                candidates[1],
                cfg.ActionBehavior.Event,
                cfg.ActionBehavior.Reward,
                cfg.ActionBehavior.Reward);
            SetCategories(candidates[2], cfg.ActionBehavior.Reward, cfg.ActionBehavior.Event);
            SetCategories(candidates[3], cfg.ActionBehavior.Negative);

            var database = GameplayContentBuilder.BuildDatabase(tables);
            var run = new GameRun(
                tables,
                database,
                tables.TbCharacter.DataList[0].Id,
                "event-decoration-weight-test");
            Assert.That(
                run.AcquireItem("item_lucky_chance", fallbackGold: 0).Outcome,
                Is.EqualTo(ItemAcquireOutcome.Added));
            Assert.That(
                run.AcquireItem("item_more_events", fallbackGold: 0).Outcome,
                Is.EqualTo(ItemAcquireOutcome.Added));
            var luckyModel = run.PassiveModels.Single(model => model.ItemId == "item_lucky_chance");
            var moreEventsModel = run.PassiveModels.Single(model => model.ItemId == "item_more_events");
            int luckyFlashes = 0;
            int moreEventsFlashes = 0;
            luckyModel.Flashed += _ => luckyFlashes++;
            moreEventsModel.Flashed += _ => moreEventsFlashes++;

            var rng = new CapturingRandomStream(selectedIndex: 1);
            cfg.GameEvent picked = EventService.RollActionEvent(run, rng);

            float multiplier = 1f + tables.TbPassiveItem.Get("item_lucky_chance").EffectValue;
            Assert.That(rng.LastWeights, Has.Count.EqualTo(4));
            Assert.That(rng.LastWeights[0], Is.EqualTo(candidates[0].Weight).Within(0.0001f));
            Assert.That(rng.LastWeights[1], Is.EqualTo(candidates[1].Weight * multiplier).Within(0.0001f));
            Assert.That(rng.LastWeights[2], Is.EqualTo(candidates[2].Weight * multiplier).Within(0.0001f));
            Assert.That(rng.LastWeights[3], Is.EqualTo(candidates[3].Weight).Within(0.0001f));
            Assert.That(candidates[1].PrimaryEventType, Is.EqualTo(cfg.ActionBehavior.Event));
            Assert.That(picked, Is.SameAs(candidates[1]));
            Assert.That(luckyFlashes, Is.EqualTo(1));
            Assert.That(moreEventsFlashes, Is.Zero);
        }

        [Test]
        public void LuckyChance_DirectRewardPoolKeepsBaseWeightsAndDoesNotFlash()
        {
            var config = new ConfigService();
            config.LoadAll();
            cfg.Tables tables = config.Tables;
            cfg.GameEvent[] candidates = tables.TbEvent.DataList
                .Where(ev => ev.Weight > 0f && !ev.Preconditions.Any())
                .Take(2)
                .ToArray();
            Assert.That(candidates, Has.Length.EqualTo(2));

            foreach (cfg.GameEvent configuredEvent in tables.TbEvent.DataList)
            {
                configuredEvent.EventTypes.Clear();
            }
            SetCategories(candidates[0], cfg.ActionBehavior.Reward);
            SetCategories(candidates[1], cfg.ActionBehavior.Event, cfg.ActionBehavior.Reward);

            var database = GameplayContentBuilder.BuildDatabase(tables);
            var run = new GameRun(
                tables,
                database,
                tables.TbCharacter.DataList[0].Id,
                "direct-reward-weight-test");
            Assert.That(
                run.AcquireItem("item_lucky_chance", fallbackGold: 0).Outcome,
                Is.EqualTo(ItemAcquireOutcome.Added));
            var luckyModel = run.PassiveModels.Single(model => model.ItemId == "item_lucky_chance");
            int luckyFlashes = 0;
            luckyModel.Flashed += _ => luckyFlashes++;

            var rng = new CapturingRandomStream(selectedIndex: 1);
            cfg.GameEvent picked = EventService.RollEvent(run, rng, cfg.ActionBehavior.Reward);

            Assert.That(rng.LastWeights, Has.Count.EqualTo(2));
            Assert.That(rng.LastWeights[0], Is.EqualTo(candidates[0].Weight).Within(0.0001f));
            Assert.That(rng.LastWeights[1], Is.EqualTo(candidates[1].Weight).Within(0.0001f));
            Assert.That(picked, Is.SameAs(candidates[1]));
            Assert.That(luckyFlashes, Is.Zero);
        }

        private static cfg.GameEvent LoadEvent()
        {
            var config = new ConfigService();
            config.LoadAll();
            return config.Tables.TbEvent.Get("ev_midnight_tasting");
        }

        private static void SetCategories(cfg.GameEvent ev, params cfg.ActionBehavior[] eventTypes)
        {
            ev.EventTypes.Clear();
            for (int i = 0; i < eventTypes.Length; i++)
            {
                ev.EventTypes.Add(eventTypes[i]);
            }
        }

        private sealed class CapturingRandomStream : IRandomStream
        {
            private readonly int _selectedIndex;

            public CapturingRandomStream(int selectedIndex = 0)
            {
                _selectedIndex = selectedIndex;
            }

            public IReadOnlyList<float> LastWeights { get; private set; } = Array.Empty<float>();

            public RngState State { get; set; }

            public uint NextUInt() => 0;
            public ulong NextULong() => 0;
            public int Range(int minInclusive, int maxExclusive) => minInclusive;
            public float Range(float minInclusive, float maxExclusive) => minInclusive;
            public float NextFloat() => 0f;
            public double NextDouble() => 0d;
            public bool NextBool(double probability = 0.5d) => probability > 0d;
            public void Shuffle<T>(IList<T> list) { }
            public T Pick<T>(IReadOnlyList<T> list) => list[0];

            public int WeightedPickIndex(IReadOnlyList<float> weights)
            {
                LastWeights = weights.ToArray();
                return Math.Max(0, Math.Min(_selectedIndex, weights.Count - 1));
            }
        }
    }
}
