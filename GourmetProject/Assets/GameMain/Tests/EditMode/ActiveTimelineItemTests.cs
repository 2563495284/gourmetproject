using System.Linq;
using GourmetProject.Config;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Hud;
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
        public void TimelineSelectionItems_RequireTargetsEvenWhenConfiguredAsGlobal()
        {
            string[] itemIds =
            {
                "item_active_add_reward_node",
                "item_active_add_interest_node",
                "item_active_add_shop_node",
                "item_active_add_lottery_node",
                "item_active_delete_timeline_node",
                "item_active_execute_future_node",
                "item_active_execute_past_node",
            };

            foreach (string itemId in itemIds)
            {
                ItemDefinition item = ItemDefinition.Get(_tables, itemId, cfg.ItemKind.Active);
                Assert.That(item, Is.Not.Null, itemId);
                Assert.That(item.TargetKind, Is.EqualTo(cfg.ItemTargetKind.Global), itemId);
                Assert.That(ItemActiveUsage.RequiresTarget(item), Is.True, itemId);
            }
        }

        [Test]
        public void TimelineFanLayout_UsesOneModelAndDensifiesWithoutCountLimit()
        {
            TimelineNodeFanPose single = TimelineNodeFanLayout.Calculate(0, 1);
            Assert.That(single.Position.x, Is.EqualTo(0f).Within(0.001f));

            TimelineNodeFanPose four0 = TimelineNodeFanLayout.Calculate(0, 4);
            TimelineNodeFanPose four1 = TimelineNodeFanLayout.Calculate(1, 4);
            TimelineNodeFanPose ten0 = TimelineNodeFanLayout.Calculate(0, 10);
            TimelineNodeFanPose ten1 = TimelineNodeFanLayout.Calculate(1, 10);

            float fourSpacing = four1.Position.x - four0.Position.x;
            float tenSpacing = ten1.Position.x - ten0.Position.x;
            Assert.That(fourSpacing, Is.GreaterThan(0f));
            Assert.That(tenSpacing, Is.GreaterThan(0f).And.LessThan(fourSpacing));

            TimelineNodeFanPose edgeFirst = TimelineNodeFanLayout.Calculate(0, 2);
            TimelineNodeFanPose edgeLast = TimelineNodeFanLayout.Calculate(1, 2);
            Assert.That(edgeFirst.Position.x, Is.LessThan(edgeLast.Position.x));
            Assert.That(
                edgeLast.Position.x - edgeFirst.Position.x,
                Is.EqualTo(29f).Within(0.001f),
                "边缘日期也要保持正常扇形间距，不能把同日节点 Clamp 到同一位置。");
            Assert.That(
                edgeFirst.Position.x + edgeLast.Position.x,
                Is.EqualTo(0f).Within(0.001f),
                "最后一天的气泡仍以日期点为中心，不向行动轴内部偏移。");

            TimelineNodeFanPose edgeSingle = TimelineNodeFanLayout.Calculate(0, 1);
            Assert.That(edgeSingle.Position.x, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void TimelineAddItem_AppliesOnlyToRuntimeState()
        {
            GameRun run = CreateRun();
            run.BeginTimeline("test", 7f);
            ItemDefinition item = ItemDefinition.Get(
                _tables,
                "item_active_add_reward_node",
                cfg.ItemKind.Active);
            var context = new ActionSelectUseContext(run, null);

            ActiveItemUseResult result = ActiveItemEffectRegistry.Apply(
                context,
                item,
                new[] { new ActiveTarget("7", 7, targetKind: cfg.ItemTargetKind.Global) });

            Assert.That(result.Success, Is.True);
            Assert.That(
                TimelineService.GetNodes(run).Any(node => node.Day == 7),
                Is.True);
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
        public void TimelineNodeCommit_DoesNotAdvanceOrConsumeHalfDayBuff()
        {
            GameRun run = CreateRun();
            cfg.GameAction action = _tables.TbAction.DataList.First();
            run.BeginTimeline("test", 7f);
            run.AddNextDailyActionHalfCostStack();
            var context = new ActionExecutionContext(action, 0, 0, string.Empty, 0f)
            {
                SourceKey = "timeline_node",
                HalfDayBuffApplied = true,
            };

            float previousDay = ActionExecutor.Commit(run, context);

            Assert.That(context.IsDailyAction, Is.False);
            Assert.That(previousDay, Is.Zero);
            Assert.That(run.CurrentDay, Is.Zero);
            Assert.That(run.ActionStepIndex, Is.Zero);
            Assert.That(run.NextDailyActionHalfCostStacks, Is.EqualTo(1));
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
