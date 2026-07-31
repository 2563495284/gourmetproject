using System.Collections.Generic;
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
    }
}
