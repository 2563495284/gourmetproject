using System.Linq;
using GourmetProject.Config;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ActiveTimelineItemTests
    {
        private static cfg.Tables _tables;
        private static GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void HalfDayCost_IsQuantizedToOneDecimal()
        {
            Assert.That(TimelineMath.Quantize(0.25f), Is.EqualTo(0.3f));
            Assert.That(TimelineMath.Quantize(0.75f), Is.EqualTo(0.8f));
        }

        [Test]
        public void ActionChoice_CarriesHalfDayFlagIntoExecutionContext()
        {
            var choice = new ActionChoice(
                action: null,
                actionGroupId: "group",
                weekStepIndex: 2,
                runStepIndex: 5,
                costDays: 0.75f,
                halfDayBuffApplied: true);

            ActionExecutionContext context = choice.ToExecutionContext();

            Assert.That(choice.CostDays, Is.EqualTo(0.8f));
            Assert.That(context.HalfDayBuffApplied, Is.True);
            Assert.That(context.IsExtraTimelineExecution, Is.False);
        }

        [Test]
        public void NewSaveFields_DefaultToBackwardCompatibleValues()
        {
            var save = new RunSaveData();

            Assert.That(save.NextDailyActionHalfCostStacks, Is.Zero);
            Assert.That(save.BossDebuffRerollNodeId, Is.Null);
            Assert.That(save.PendingExtraTimelineNodeIds, Is.Empty);
            Assert.That(save.RuntimeTimelineNodeSerial, Is.Zero);
            Assert.That(save.LastActionHalfDayBuffApplied, Is.False);
            Assert.That(save.LastActionIsExtraTimelineExecution, Is.False);
        }

        [Test]
        public void NewActiveEffectTypes_AreRegistered()
        {
            Assert.That(ItemEffectTypes.IsValidActiveEffectType(ItemEffectTypes.HalfNextActionCost), Is.True);
            Assert.That(ItemEffectTypes.IsValidActiveEffectType(ItemEffectTypes.ResetBossDebuff), Is.True);
            Assert.That(ItemEffectTypes.IsValidActiveEffectType(ItemEffectTypes.TimelineExecuteFuture), Is.True);
            Assert.That(ItemEffectTypes.IsValidActiveEffectType(ItemEffectTypes.TimelineExecutePast), Is.True);
            Assert.That(ItemEffectTypes.IsValidActiveEffectType(ItemEffectTypes.TimelineAddRewardNode), Is.True);
            Assert.That(ItemEffectTypes.IsValidActiveEffectType(ItemEffectTypes.TimelineAddInterestNode), Is.True);
            Assert.That(ItemEffectTypes.IsValidActiveEffectType(ItemEffectTypes.TimelineAddShopNode), Is.True);
            Assert.That(ItemEffectTypes.IsValidActiveEffectType(ItemEffectTypes.TimelineAddLotteryNode), Is.True);
            Assert.That(ItemEffectTypes.IsValidActiveEffectType(ItemEffectTypes.TimelineDeleteNode), Is.True);
        }

        [Test]
        public void OnlyLotteryTimelineEffect_RemainsTodo()
        {
            Assert.That(ItemActiveUsage.IsTodoTimelineEffect(ItemEffectTypes.TimelineAddRewardNode), Is.False);
            Assert.That(ItemActiveUsage.IsTodoTimelineEffect(ItemEffectTypes.TimelineAddInterestNode), Is.False);
            Assert.That(ItemActiveUsage.IsTodoTimelineEffect(ItemEffectTypes.TimelineAddShopNode), Is.False);
            Assert.That(ItemActiveUsage.IsTodoTimelineEffect(ItemEffectTypes.TimelineAddLotteryNode), Is.True);
            Assert.That(ItemActiveUsage.IsTodoTimelineEffect(ItemEffectTypes.TimelineDeleteNode), Is.False);
            Assert.That(ItemActiveUsage.IsTodoTimelineEffect(ItemEffectTypes.TimelineExecuteFuture), Is.False);
        }

        [Test]
        public void RuntimeTimelineNode_AddAtSameDay_UsesMonotonicIdsAcrossDeleteAndSave()
        {
            GameRun run = CreateRun();
            cfg.GameAction action = _tables.TbAction.DataList.First();
            run.BeginTimeline("test", 7f);

            string first = run.AddRuntimeTimelineNodeAtDay(action.Id, 4);
            string second = run.AddRuntimeTimelineNodeAtDay(action.Id, 4);
            Assert.That(first, Is.Not.Empty);
            Assert.That(second, Is.Not.Empty.And.Not.EqualTo(first));
            Assert.That(TimelineService.GetNodes(run).Count(node => node.Day == 4), Is.EqualTo(2));

            Assert.That(run.RemoveRuntimeTimelineNode(first), Is.True);
            string third = run.AddRuntimeTimelineNodeAtDay(action.Id, 4);
            Assert.That(third, Is.Not.EqualTo(first).And.Not.EqualTo(second));

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            string fourth = restored.AddRuntimeTimelineNodeAtDay(action.Id, 5);
            Assert.That(fourth, Is.Not.EqualTo(first).And.Not.EqualTo(second).And.Not.EqualTo(third));
        }

        [Test]
        public void RuntimeTimelineNode_AddRejectsCurrentPastAndBeyondWeek()
        {
            GameRun run = CreateRun();
            cfg.GameAction action = _tables.TbAction.DataList.First();
            run.BeginTimeline("test", 7f);
            run.CurrentDay = 3.4f;

            Assert.That(run.AddRuntimeTimelineNodeAtDay(action.Id, 3), Is.Empty);
            Assert.That(run.AddRuntimeTimelineNodeAtDay(action.Id, 8), Is.Empty);
            Assert.That(run.AddRuntimeTimelineNodeAtDay(action.Id, 4), Is.Not.Empty);
        }

        [Test]
        public void RuntimeTimelineNode_SameDaySortsDynamicNodesByNumericCreationOrder()
        {
            GameRun run = CreateRun();
            cfg.GameAction action = _tables.TbAction.DataList.First();
            run.BeginTimeline("test", 7f);
            for (int i = 0; i < 12; i++)
            {
                run.AddRuntimeTimelineNodeAtDay(action.Id, 4);
            }

            Assert.That(
                TimelineService.GetNodes(run).Select(node => node.Id),
                Is.EqualTo(Enumerable.Range(1, 12).Select(i => $"dyn_w{run.WeekIndex}_{i}")));
        }

        [Test]
        public void DeleteNode_CleansExtraQueueAndDoesNotShrinkWeek()
        {
            GameRun run = CreateRun();
            cfg.GameAction action = _tables.TbAction.DataList.First();
            run.BeginTimeline(
                "test",
                8f,
                new[] { new RuntimeTimelineNode("late", "test", 8, action.Id) });
            run.EnqueueExtraTimelineNode("late");

            Assert.That(run.RemoveRuntimeTimelineNode("late"), Is.True);
            Assert.That(run.RuntimeTimelineNodes.Any(node => node.Id == "late"), Is.False);
            Assert.That(run.PendingExtraTimelineNodeIds, Is.Empty);
            Assert.That(run.TimelineLengthDays, Is.EqualTo(8f));
        }

        [Test]
        public void DeleteNode_RejectsTriggeredAndCurrentlyExecutingNodes()
        {
            GameRun run = CreateRun();
            cfg.GameAction action = _tables.TbAction.DataList.First();
            run.BeginTimeline(
                "test",
                7f,
                new[]
                {
                    new RuntimeTimelineNode("triggered", "test", 2, action.Id),
                    new RuntimeTimelineNode("executing", "test", 3, action.Id),
                });
            run.MarkNodeTriggered("triggered");
            var context = new ActionExecutionContext(action) { SourceKey = "executing" };
            run.SetPendingActionExecution(context, ActionOutcome.Shop());

            Assert.That(run.RemoveRuntimeTimelineNode("triggered"), Is.False);
            Assert.That(run.RemoveRuntimeTimelineNode("executing"), Is.False);
            Assert.That(
                TimelineService.GetDeletableUnsettledNodes(run).Select(node => node.Id),
                Is.Empty);
        }

        [Test]
        public void HalfDayBuff_StacksConsumesAndRoundTripsSave()
        {
            GameRun run = CreateRun();
            cfg.GameAction action = _tables.TbAction.DataList.First();
            run.BeginTimeline("test", 7f);
            run.AddNextDailyActionHalfCostStack();
            run.AddNextDailyActionHalfCostStack();

            float cost = run.PreviewDailyActionCost(1.5f);
            var context = new ActionExecutionContext(action, 0, 0, "test", cost)
            {
                HalfDayBuffApplied = true,
            };
            ActionExecutor.Commit(run, context);

            Assert.That(cost, Is.EqualTo(0.8f));
            Assert.That(run.CurrentDay, Is.EqualTo(0.8f));
            Assert.That(run.NextDailyActionHalfCostStacks, Is.EqualTo(1));

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            Assert.That(restored.NextDailyActionHalfCostStacks, Is.EqualTo(1));
        }

        [Test]
        public void ExtraNodeExecution_DoesNotAdvanceDayOrSteps()
        {
            GameRun run = CreateRun();
            cfg.GameAction action = _tables.TbAction.DataList.First();
            run.BeginTimeline("test", 7f);
            var context = new ActionExecutionContext(action)
            {
                SourceKey = "node_future",
                IsExtraTimelineExecution = true,
            };

            ActionExecutor.Commit(run, context);

            Assert.That(run.CurrentDay, Is.Zero);
            Assert.That(run.ActionStepIndex, Is.Zero);
            Assert.That(run.RunActionStepIndex, Is.Zero);
            Assert.That(run.IsNodeTriggered("node_future"), Is.False);
        }

        [Test]
        public void ExtraNodeQueue_RoundTripsInFifoOrder()
        {
            GameRun run = CreateRun();
            run.EnqueueExtraTimelineNode("node_a");
            run.EnqueueExtraTimelineNode("node_b");

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());

            Assert.That(restored.TryDequeueExtraTimelineNode(out string first), Is.True);
            Assert.That(restored.TryDequeueExtraTimelineNode(out string second), Is.True);
            Assert.That(first, Is.EqualTo("node_a"));
            Assert.That(second, Is.EqualTo("node_b"));
        }

        [Test]
        public void TimelineCandidates_FilterFutureAndPastAndTargetLastBoss()
        {
            GameRun run = CreateRun();
            cfg.GameAction regular = _tables.TbAction.DataList.First(action => !FoodService.IsBossAction(_tables, action));
            cfg.GameAction boss = _tables.TbAction.DataList.First(action => FoodService.IsBossAction(_tables, action));
            run.BeginTimeline(
                "test",
                7f,
                new[]
                {
                    new RuntimeTimelineNode("past", "test", 2, regular.Id),
                    new RuntimeTimelineNode("future", "test", 5, regular.Id),
                    new RuntimeTimelineNode("boss", "test", 6, boss.Id),
                });
            run.CurrentDay = 4f;
            run.MarkNodeTriggered("past");

            Assert.That(TimelineService.GetPastTriggeredNodes(run).Select(node => node.Id), Is.EqualTo(new[] { "past" }));
            Assert.That(
                TimelineService.GetFutureUntriggeredNodes(run).Select(node => node.Id),
                Is.EqualTo(new[] { "future", "boss" }));
            Assert.That(TimelineService.GetLastUntriggeredBossNode(run)?.Id, Is.EqualTo("boss"));
        }

        [Test]
        public void LoadRepair_NeverShrinksBelowBaseOrLastNode()
        {
            GameRun run = CreateRun();
            cfg.GameAction action = _tables.TbAction.DataList.First();
            run.BeginTimeline(
                "test",
                7f,
                new[] { new RuntimeTimelineNode("late", "test", 8, action.Id) });

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());

            Assert.That(restored.TimelineLengthDays, Is.EqualTo(8f));
        }

        private static GameRun CreateRun()
        {
            string characterId = _tables.TbCharacter.DataList.First().Id;
            return new GameRun(_tables, _database, characterId, "active-item-tests");
        }
    }
}
