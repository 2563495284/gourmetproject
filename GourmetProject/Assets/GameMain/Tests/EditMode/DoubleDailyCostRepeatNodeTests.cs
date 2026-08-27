#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Orchestration;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DoubleDailyCostRepeatNodeTests
    {
        private const string ItemId = "item_double_daily_cost_repeat_node";
        private const string ExpectedDesc =
            "普通行动消耗天数+0.5\n"
            + "除[context]星级评鉴[/context]外\n"
            + "所有节点行动额外执行1次";

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
        public void Configuration_UsesFixedHalfDayBonusAndRepeatTwo()
        {
            cfg.PassiveItem item = _tables.TbPassiveItem.Get(ItemId);

            Assert.That(item.EffectValue, Is.EqualTo(0.5f));
            Assert.That(item.EffectParam, Is.EqualTo("repeat:2"));
            Assert.That(item.Desc, Is.EqualTo(ExpectedDesc));
        }

        [TestCase(0.4f, 0.9f)]
        [TestCase(0.6f, 1.1f)]
        [TestCase(0.7f, 1.2f)]
        [TestCase(0.9f, 1.4f)]
        public void SnapshotDailyActionCost_AddsFixedHalfDay(float baseDays, float expectedDays)
        {
            GameRun run = CreateRun("fixed-cost");
            run.AcquireItem(ItemId, fallbackGold: 0, fireOnAcquire: false);

            Assert.That(run.SnapshotDailyActionCost(baseDays), Is.EqualTo(expectedDays).Within(0.0001f));
        }

        [TestCase(0.4f)]
        [TestCase(0.6f)]
        [TestCase(0.7f)]
        [TestCase(0.9f)]
        public void SnapshotDailyActionCost_WithoutItemKeepsBaseCost(float baseDays)
        {
            GameRun run = CreateRun("base-cost");

            Assert.That(run.SnapshotDailyActionCost(baseDays), Is.EqualTo(baseDays).Within(0.0001f));
        }

        [Test]
        public void HalfDayEffect_AppliesAfterFixedBonusAndQuantizesToTenth()
        {
            GameRun run = CreateRun("half-after-bonus");
            run.AcquireItem(ItemId, fallbackGold: 0, fireOnAcquire: false);
            float snapshot = run.SnapshotDailyActionCost(0.6f);

            run.AddNextDailyActionHalfCostStack();

            Assert.That(snapshot, Is.EqualTo(1.1f).Within(0.0001f));
            Assert.That(run.PreviewDailyActionCost(snapshot), Is.EqualTo(0.6f).Within(0.0001f));
        }

        [Test]
        public void FixedCostBonuses_AggregateByAddition()
        {
            GameRun run = CreateRun("additive-bonuses");
            AddTestModel(run, new FixedCostBonusModel(0.2f), "test_bonus_02");
            AddTestModel(run, new FixedCostBonusModel(0.3f), "test_bonus_03");

            Assert.That(new ItemRuntime(run).DailyActionCostBonusDays(), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(run.SnapshotDailyActionCost(0.4f), Is.EqualTo(0.9f).Within(0.0001f));
        }

        [Test]
        public void InvalidConfiguredBonus_FallsBackToHalfDay()
        {
            GameRun run = CreateRun("invalid-bonus");
            var model = new DoubleDailyCostRepeatNodeModel();
            var state = new RunItemState(ItemId, 1);
            var definition = ItemDefinition.From(new cfg.PassiveItem(JSON.Parse(
                "{\"id\":\"item_double_daily_cost_repeat_node\",\"name\":\"双栏排班板\","
                + "\"desc\":\"\",\"quality\":2,\"specialTags\":0,\"effectValue\":0,"
                + "\"effectParam\":\"repeat:2\",\"baseWeight\":0,"
                + "\"targetScoreHiddenOffset\":0,\"dishHiddenOffset\":0,"
                + "\"itemLuckOffset\":0,\"fragmentHiddenOffset\":0,"
                + "\"termId\":\"\",\"price\":0,\"archetypeTags\":[]}")));
            model.Bind(run, definition, state);

            Assert.That(model.DailyActionCostBonusDays(), Is.EqualTo(0.5f));
        }

        [Test]
        public void ExistingActionChoice_KeepsItsCostSnapshotAfterItemRemoval()
        {
            GameRun run = CreateRun("snapshot-stability");
            run.AcquireItem(ItemId, fallbackGold: 0, fireOnAcquire: false);
            var choice = new ActionChoice(
                _tables.TbAction.Get("act_shop"),
                "test",
                weekStepIndex: 0,
                runStepIndex: 0,
                costDays: run.SnapshotDailyActionCost(0.6f));

            run.RemoveItem(ItemId);

            Assert.That(choice.CostDays, Is.EqualTo(1.1f).Within(0.0001f));
            Assert.That(run.SnapshotDailyActionCost(0.6f), Is.EqualTo(0.6f).Within(0.0001f));
        }

        [TestCase("act_shop")]
        [TestCase("act_reward")]
        [TestCase("act_interest")]
        [TestCase("act_slot")]
        [TestCase("act_restore_heart")]
        [TestCase("act_loan_repay")]
        public void NaturalNonBossNodes_ExecuteTwice(string actionId)
        {
            GameRun run = CreateRun("natural-repeat-" + actionId);
            run.AcquireItem(ItemId, fallbackGold: 0, fireOnAcquire: false);

            int repeat = WeekLoopController.ResolveNaturalTimelineNodeRepeatCount(
                run,
                _tables.TbAction.Get(actionId));

            Assert.That(repeat, Is.EqualTo(2));
        }

        [Test]
        public void RuntimeAddedAndCopiedNodes_UseTheSameRepeatDecision()
        {
            GameRun run = CreateRun("runtime-node-repeat");
            run.AcquireItem(ItemId, fallbackGold: 0, fireOnAcquire: false);
            run.BeginTimeline(
                "runtime_test",
                4f,
                new[]
                {
                    new RuntimeTimelineNode("dynamic_node", "runtime_test", 1, "act_restore_heart"),
                    new RuntimeTimelineNode("copied_node", "runtime_test", 2, "act_interest"),
                });

            foreach (cfg.TimelineNode node in TimelineService.GetNodes(run))
            {
                cfg.GameAction action = TimelineService.NodeAction(run, node);
                Assert.That(
                    WeekLoopController.ResolveNaturalTimelineNodeRepeatCount(run, action),
                    Is.EqualTo(2),
                    node.Id);
            }
        }

        [Test]
        public void BossAndMissingAction_AlwaysExecuteOnce()
        {
            GameRun run = CreateRun("boss-repeat");
            run.AcquireItem(ItemId, fallbackGold: 0, fireOnAcquire: false);

            Assert.That(
                WeekLoopController.ResolveNaturalTimelineNodeRepeatCount(
                    run,
                    _tables.TbAction.Get("act_boss")),
                Is.EqualTo(1));
            Assert.That(
                WeekLoopController.ResolveNaturalTimelineNodeRepeatCount(run, null),
                Is.EqualTo(1));
        }

        [Test]
        public void RepresentativeFlow_UsesDailyBonusThenTwoInterestTwoShopAndOneBossPass()
        {
            GameRun run = CreateRun("representative-flow");
            run.AcquireItem(ItemId, fallbackGold: 0, fireOnAcquire: false);

            float dailyCost = run.SnapshotDailyActionCost(0.4f);
            int[] nodePasses =
            {
                WeekLoopController.ResolveNaturalTimelineNodeRepeatCount(
                    run,
                    _tables.TbAction.Get("act_interest")),
                WeekLoopController.ResolveNaturalTimelineNodeRepeatCount(
                    run,
                    _tables.TbAction.Get("act_shop")),
                WeekLoopController.ResolveNaturalTimelineNodeRepeatCount(
                    run,
                    _tables.TbAction.Get("act_boss")),
            };

            Assert.That(dailyCost, Is.EqualTo(0.9f).Within(0.0001f));
            Assert.That(nodePasses, Is.EqualTo(new[] { 2, 2, 1 }));
        }

        [Test]
        public void RepeatState_RoundTripsAsExactlyTwoPasses()
        {
            GameRun run = CreateRun("repeat-save");
            run.AcquireItem(ItemId, fallbackGold: 0, fireOnAcquire: false);
            run.BeginTimeline(
                "repeat_save_test",
                3f,
                new[] { new RuntimeTimelineNode("interest_node", "repeat_save_test", 1, "act_interest") });
            run.MarkNodeTriggered("interest_node");
            var context = new ActionExecutionContext(_tables.TbAction.Get("act_interest"))
            {
                SourceKey = "interest_node",
                NodeRepeatIndex = 1,
                NodeRepeatTotal = 2,
            };
            run.SetPendingActionExecution(context, ActionOutcome.Immediate(string.Empty));

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            cfg.TimelineNode restoredNode = TimelineService.GetNode(restored, "interest_node");

            Assert.That(restoredNode, Is.Not.Null);
            Assert.That(restored.IsNodeTriggered("interest_node"), Is.True);
            Assert.That(restored.LastActionContext.NodeRepeatIndex, Is.EqualTo(1));
            Assert.That(restored.LastActionContext.NodeRepeatTotal, Is.EqualTo(2));
            Assert.That(restored.GetPendingActionExecution().NodeRepeatTotal, Is.EqualTo(2));
        }

        [Test]
        public void ExtraTimelineExecution_SaveStateRemainsSinglePass()
        {
            GameRun run = CreateRun("extra-node-single-pass");
            var context = new ActionExecutionContext(_tables.TbAction.Get("act_interest"))
            {
                SourceKey = "extra_interest",
                IsExtraTimelineExecution = true,
            };
            run.SetPendingActionExecution(context, ActionOutcome.Immediate(string.Empty));

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());

            Assert.That(restored.LastActionContext.IsExtraTimelineExecution, Is.True);
            Assert.That(restored.LastActionContext.NodeRepeatIndex, Is.EqualTo(1));
            Assert.That(restored.LastActionContext.NodeRepeatTotal, Is.EqualTo(1));
            Assert.That(restored.GetPendingActionExecution().NodeRepeatTotal, Is.EqualTo(1));
        }

        private GameRun CreateRun(string seed)
        {
            return new GameRun(_tables, _database, "glutton_dog", seed);
        }

        private static void AddTestModel(GameRun run, PassiveItemModel model, string itemId)
        {
            var state = new RunItemState(itemId, 1) { Model = model };
            ((List<RunItemState>)run.Items).Add(state);
        }

        private sealed class FixedCostBonusModel : PassiveItemModel
        {
            private readonly float _bonusDays;

            public FixedCostBonusModel(float bonusDays)
            {
                _bonusDays = bonusDays;
            }

            public override float DailyActionCostBonusDays() => _bonusDays;
        }
    }
}
#endif
