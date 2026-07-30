using System.Collections.Generic;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Gameplay.Data;
using GourmetProject.Runtime;
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
            Assert.That(save.LastActionTimelineStopChance, Is.Zero);
            Assert.That(save.LastActionNodeRepeatIndex, Is.EqualTo(1));
            Assert.That(save.LastActionNodeRepeatTotal, Is.EqualTo(1));
        }

        [Test]
        public void CommentedPassiveItems_AreGeneratedWithDescriptionsUnchanged()
        {
            var expected = new Dictionary<string, string>
            {
                ["item_extra_interest"] = "将一个收取利息\n添加至每周末尾",
                ["item_loan"] = "获得200金币\n将失去400金币的节点行动\n添加至本周末尾",
                ["item_timeline_random"] = "打乱时间轴的节点行动",
                ["item_extra_day"] = "每周长度变为8天",
                ["item_block_active"] = "无法再使用主动道具\n立即获得600金币",
                ["item_flavor_contagion"] = "使1个食物\n拥有另1个食物的风味",
                ["item_shop_restock"] = "商店会自动补货食物",
                ["item_shop_restock_active"] = "商店会自动补货主动道具",
                ["item_shop_restock_passive"] = "商店会自动补货被动道具",
                ["item_gold_meal_penalty"] = "营业获得的金币-20%",
                ["item_skip_node"] = "跳过下一个收取利息节点",
                ["item_skip_reward_node"] = "跳过下一个幸运事件节点",
                ["item_gold_week_clear"] = "将失去所有金币的节点行动\n添加至本周末尾",
                ["item_double_daily_cost_repeat_node"] = "日常行动消耗天数加倍\n节点行动可以执行2次",
                ["item_timeline_stop_chance"] = "遇到节点行动时\n时间轴有30%概率会停止",
            };

            foreach (KeyValuePair<string, string> pair in expected)
            {
                ItemDefinition item = ItemDefinition.Get(_tables, pair.Key, cfg.ItemKind.Passive);
                Assert.That(item, Is.Not.Null, pair.Key);
                Assert.That(item.Desc, Is.EqualTo(pair.Value), pair.Key);
            }

            Assert.That(
                ItemDefinition.Get(_tables, "item_extra_day", cfg.ItemKind.Passive).EffectValue,
                Is.EqualTo(8f));
            Assert.That(_tables.TbAction.GetOrDefault("act_loan_repay").Behavior, Is.EqualTo(cfg.ActionBehavior.Effect));
            Assert.That(_tables.TbAction.GetOrDefault("act_gold_clear").Behavior, Is.EqualTo(cfg.ActionBehavior.Effect));
            Assert.That(
                ActionDisplay.KindOf(_tables, _tables.TbAction.GetOrDefault("act_loan_repay")),
                Is.EqualTo(ActionDisplayKind.Negative));
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
        public void ActiveItems_AreClassifiedForRewardPools()
        {
            foreach (cfg.ActiveItem active in _tables.TbActiveItem.DataList)
            {
                bool strengthen = active.Id.StartsWith("item_active_season_") ||
                                  active.Id.StartsWith("item_active_lay_");
                Assert.That(
                    active.Category,
                    Is.EqualTo(strengthen ? cfg.ActiveItemCategory.Strengthen : cfg.ActiveItemCategory.Adjust),
                    active.Id);
            }
        }

        [Test]
        public void FoodRewards_ReferenceSeparatedActiveItemPackages()
        {
            AssertActiveReward(
                "food_active_strengthen",
                cfg.RewardKind.ActiveItemStrengthen,
                "reward_food_active_strengthen");
            AssertActiveReward(
                "food_active_ajust",
                cfg.RewardKind.ActiveItemAdjust,
                "reward_food_active_adjust");
            AssertActiveReward(
                "food_hard_active_strengthen",
                cfg.RewardKind.ActiveItemStrengthen,
                "reward_food_hard_active_strengthen");
            AssertActiveReward(
                "food_hard_active_ajust",
                cfg.RewardKind.ActiveItemAdjust,
                "reward_food_hard_active_adjust");
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

        private static void AssertActiveReward(
            string foodId,
            cfg.RewardKind expectedKind,
            string expectedPackageId)
        {
            cfg.Food food = _tables.TbFood.Get(foodId);
            Assert.That(food.RewardKind, Is.EqualTo(expectedKind), foodId);
            Assert.That(food.RewardPackageId, Is.EqualTo(expectedPackageId), foodId);
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
            Assert.That(result.CreatedTimelineNodeId, Is.Not.Empty);
            Assert.That(
                TimelineService.GetNodes(run).Any(node => node.Day == 7),
                Is.True);
        }

        [Test]
        public void RushItem_ClonesSelectedActionToCurrentOrNextIntegerDay()
        {
            GameRun run = CreateRun();
            cfg.GameAction regular = _tables.TbAction.DataList.First(action => !FoodService.IsBossAction(_tables, action));
            run.BeginTimeline(
                "test",
                7f,
                new[]
                {
                    new RuntimeTimelineNode("past", "test", 2, regular.Id),
                    new RuntimeTimelineNode("future", "test", 6, regular.Id),
                });
            run.CurrentDay = 3.4f;
            run.MarkNodeTriggered("past");
            var context = new ActionSelectUseContext(run, null);
            ItemDefinition futureItem = ItemDefinition.Get(
                _tables,
                "item_active_execute_future_node",
                cfg.ItemKind.Active);

            ActiveItemUseResult result = ActiveItemEffectRegistry.Apply(
                context,
                futureItem,
                new[] { new ActiveTarget("future", 6, targetKind: cfg.ItemTargetKind.Global) });

            Assert.That(result.Success, Is.True);
            Assert.That(result.CreatedTimelineNodeId, Is.Not.Empty);
            cfg.TimelineNode clone = TimelineService.GetNode(run, result.CreatedTimelineNodeId);
            Assert.That(clone.Day, Is.EqualTo(4));
            Assert.That(clone.ActionId, Is.EqualTo(regular.Id));
            Assert.That(
                run.RuntimeTimelineNodes.Single(node => node.Id == result.CreatedTimelineNodeId).SourceItemId,
                Is.EqualTo("item_active_execute_future_node"));
            Assert.That(run.IsNodeTriggered("future"), Is.False);
            Assert.That(TimelineService.GetNode(run, "future").Day, Is.EqualTo(6));
        }

        [Test]
        public void RushItem_AtWeekEndCanCloneToCurrentIntegerDay()
        {
            GameRun run = CreateRun();
            cfg.GameAction action = _tables.TbAction.DataList.First();
            run.BeginTimeline(
                "test",
                7f,
                new[] { new RuntimeTimelineNode("past", "test", 2, action.Id) });
            run.CurrentDay = 7f;
            run.MarkNodeTriggered("past");
            var context = new ActionSelectUseContext(run, null);
            ItemDefinition item = ItemDefinition.Get(
                _tables,
                "item_active_execute_past_node",
                cfg.ItemKind.Active);

            Assert.That(context.EnumerateTargets(item).Select(target => target.Id), Does.Contain("past"));
            string cloneId = run.CloneRuntimeTimelineNodeToCurrentOrNextIntegerDay("past");
            Assert.That(cloneId, Is.Not.Empty);
            Assert.That(TimelineService.GetNode(run, cloneId).Day, Is.EqualTo(7));
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
        public void RuntimeTimelineNode_AddAllowsCurrentIntegerButRejectsPastAndBeyondWeek()
        {
            GameRun run = CreateRun();
            cfg.GameAction action = _tables.TbAction.DataList.First();
            run.BeginTimeline("test", 7f);

            run.CurrentDay = 3f;
            Assert.That(run.AddRuntimeTimelineNodeAtDay(action.Id, 2), Is.Empty);
            Assert.That(run.AddRuntimeTimelineNodeAtDay(action.Id, 3), Is.Not.Empty);
            Assert.That(run.AddRuntimeTimelineNodeAtDay(action.Id, 8), Is.Empty);

            run.CurrentDay = 3.4f;
            Assert.That(run.AddRuntimeTimelineNodeAtDay(action.Id, 3), Is.Empty);
            Assert.That(run.AddRuntimeTimelineNodeAtDay(action.Id, 4), Is.Not.Empty);
        }

        [Test]
        public void CurrentOrNextIntegerDay_AndAddCandidatesKeepExactCurrentDay()
        {
            Assert.That(TimelineMath.CurrentOrNextIntegerDay(2f), Is.EqualTo(2));
            Assert.That(TimelineMath.CurrentOrNextIntegerDay(2.1f), Is.EqualTo(3));

            GameRun run = CreateRun();
            run.BeginTimeline("test", 7f);
            ItemDefinition item = ItemDefinition.Get(
                _tables,
                "item_active_add_reward_node",
                cfg.ItemKind.Active);
            var context = new ActionSelectUseContext(run, null);

            run.CurrentDay = 2f;
            Assert.That(
                context.EnumerateTargets(item).Select(target => target.X),
                Is.EqualTo(new[] { 2, 3, 4, 5, 6, 7 }));

            run.CurrentDay = 2.1f;
            Assert.That(
                context.EnumerateTargets(item).Select(target => target.X),
                Is.EqualTo(new[] { 3, 4, 5, 6, 7 }));
        }

        [Test]
        public void DueNodes_IncludeCurrentDaySkipTriggeredAndKeepStableOrder()
        {
            GameRun run = CreateRun();
            cfg.GameAction action = _tables.TbAction.DataList.First();
            run.BeginTimeline(
                "test",
                7f,
                new[]
                {
                    new RuntimeTimelineNode("day_2_done", "test", 2, action.Id),
                    new RuntimeTimelineNode("day_2_static", "test", 2, action.Id),
                    new RuntimeTimelineNode("day_3_static", "test", 3, action.Id),
                });
            run.CurrentDay = 2f;
            run.MarkNodeTriggered("day_2_done");
            string dynamicId = run.AddRuntimeTimelineNodeAtDay(action.Id, 2);

            Assert.That(dynamicId, Is.Not.Empty);
            Assert.That(
                TimelineService.GetDueUntriggeredNodes(run).Select(node => node.Id),
                Is.EqualTo(new[] { "day_2_static", dynamicId }));

            run.CurrentDay = 3f;
            Assert.That(
                TimelineService.CollectPassedNodes(run, 2f, 3f).Select(node => node.Id),
                Is.EqualTo(new[] { "day_2_static", dynamicId, "day_3_static" }));
        }

        [Test]
        public void WeekEndAnchoredNode_AllowsCurrentDayMovesWithWeekAndStaysDeleted()
        {
            GameRun run = CreateRun();
            cfg.GameAction boss = _tables.TbAction.DataList.First(action => FoodService.IsBossAction(_tables, action));
            run.BeginTimeline(
                "test",
                7f,
                new[] { new RuntimeTimelineNode("boss", "test", 7, boss.Id) });
            run.CurrentDay = 7f;

            string nodeId = run.AddWeekEndAnchoredTimelineNode("act_interest", "item_extra_interest");
            Assert.That(nodeId, Is.Not.Empty);
            RuntimeTimelineNode anchored = run.RuntimeTimelineNodes.Single(node => node.Id == nodeId);
            Assert.That(anchored.Day, Is.EqualTo(7));
            Assert.That(anchored.SourceItemId, Is.EqualTo("item_extra_interest"));
            Assert.That(anchored.WeekEndAnchored, Is.True);

            Assert.That(run.EnsureTimelineLengthAtLeast(8), Is.True);
            Assert.That(run.RuntimeTimelineNodes.Single(node => node.Id == nodeId).Day, Is.EqualTo(8));
            Assert.That(run.RuntimeTimelineNodes.Single(node => node.Id == "boss").Day, Is.EqualTo(7));
            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            RuntimeTimelineNode restoredAnchor =
                restored.RuntimeTimelineNodes.Single(node => node.Id == nodeId);
            Assert.That(restoredAnchor.SourceItemId, Is.EqualTo("item_extra_interest"));
            Assert.That(restoredAnchor.WeekEndAnchored, Is.True);
            Assert.That(run.RemoveRuntimeTimelineNode(nodeId), Is.True);
            Assert.That(run.EnsureTimelineLengthAtLeast(9), Is.True);
            Assert.That(run.RuntimeTimelineNodes.Select(node => node.Id), Is.EqualTo(new[] { "boss" }));
        }

        [Test]
        public void LoanAndGoldClear_CreateWeekEndEffectNodes()
        {
            GameRun run = CreateRun();
            run.BeginTimeline("test", 7f);

            int beforeGold = run.Gold;
            run.AcquireItem("item_loan", 0);
            run.AcquireItem("item_gold_week_clear", 0);

            Assert.That(run.Gold, Is.EqualTo(beforeGold + 200));
            Assert.That(
                run.RuntimeTimelineNodes.Select(node => node.ActionId),
                Is.EquivalentTo(new[] { "act_loan_repay", "act_gold_clear" }));
            Assert.That(run.RuntimeTimelineNodes.All(node => node.Day == 7 && node.WeekEndAnchored), Is.True);
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
        public void DoubleDailyCost_AppliesBeforeHalfDayAndRepeatsNonBossTwice()
        {
            GameRun run = CreateRun();
            run.AcquireItem("item_double_daily_cost_repeat_node", 0);
            run.AddNextDailyActionHalfCostStack();

            float doubled = run.SnapshotDailyActionCost(1.5f);
            float halved = run.PreviewDailyActionCost(doubled);
            var runtime = new ItemRuntime(run);

            Assert.That(doubled, Is.EqualTo(3f));
            Assert.That(halved, Is.EqualTo(1.5f));
            Assert.That(runtime.TimelineNodeRepeatCount(), Is.EqualTo(2));
        }

        [Test]
        public void CategoryRestockAndMealPenalty_AreIndependent()
        {
            GameRun run = CreateRun();
            run.AcquireItem("item_shop_restock", 0);
            run.AcquireItem("item_shop_restock_active", 0);
            run.AcquireItem("item_shop_restock_passive", 0);
            run.AcquireItem("item_gold_meal_penalty", 0);
            var runtime = new ItemRuntime(run);

            Assert.That(runtime.AutoRestock(ShopEntryKind.Dish), Is.True);
            Assert.That(runtime.AutoRestock(ShopEntryKind.ActiveItem), Is.True);
            Assert.That(runtime.AutoRestock(ShopEntryKind.PassiveItem), Is.True);
            Assert.That(runtime.AutoRestock(ShopEntryKind.Fragment), Is.False);
            Assert.That(runtime.ModifyMealRewardGold(100), Is.EqualTo(80));
        }

        [Test]
        public void BlockActive_GrantsGoldWithoutDeletingHeldActiveItems()
        {
            GameRun run = CreateRun();
            run.AcquireItem("item_active_reroll_action", 0);
            int activeBefore = run.GetItemCount("item_active_reroll_action");
            int goldBefore = run.Gold;

            run.AcquireItem("item_block_active", 0);

            Assert.That(run.Gold, Is.EqualTo(goldBefore + 600));
            Assert.That(run.GetItemCount("item_active_reroll_action"), Is.EqualTo(activeBefore));
            Assert.That(new ItemRuntime(run).BlocksActiveItems(), Is.True);
        }

        [Test]
        public void SkipItems_OnlyConsumeTheirMatchingNodeBehavior()
        {
            GameRun run = CreateRun();
            run.AcquireItem("item_skip_node", 0);
            run.AcquireItem("item_skip_reward_node", 0);
            var runtime = new ItemRuntime(run);

            Assert.That(runtime.TryConsumeTimelineSkip(cfg.ActionBehavior.Shop), Is.False);
            Assert.That(run.HasItem("item_skip_node"), Is.True);
            Assert.That(run.HasItem("item_skip_reward_node"), Is.True);

            Assert.That(runtime.TryConsumeTimelineSkip(cfg.ActionBehavior.Interest), Is.True);
            Assert.That(run.HasItem("item_skip_node"), Is.False);
            Assert.That(run.HasItem("item_skip_reward_node"), Is.True);

            Assert.That(runtime.TryConsumeTimelineSkip(cfg.ActionBehavior.Reward), Is.True);
            Assert.That(run.HasItem("item_skip_reward_node"), Is.False);
        }

        [Test]
        public void TimelineStopAtFirstNodeDay_TruncatesDailyAdvanceOnce()
        {
            var random = new RandomService();
            random.Init(12345UL);
            typeof(GameApp)
                .GetProperty(nameof(GameApp.Random))
                ?.GetSetMethod(nonPublic: true)
                ?.Invoke(null, new object[] { random });

            GameRun run = CreateRun();
            cfg.GameAction action = _tables.TbAction.DataList.First();
            run.BeginTimeline(
                "test",
                7f,
                new[]
                {
                    new RuntimeTimelineNode("same_day_a", "test", 2, action.Id),
                    new RuntimeTimelineNode("same_day_b", "test", 2, action.Id),
                    new RuntimeTimelineNode("later", "test", 5, action.Id),
                });
            var context = new ActionExecutionContext(action, 0, 0, "daily", 6f)
            {
                TimelineStopChance = 1f,
            };

            ActionExecutor.Commit(run, context);

            Assert.That(context.TimelineStopTriggered, Is.True);
            Assert.That(context.TimelineStopDay, Is.EqualTo(2));
            Assert.That(run.CurrentDay, Is.EqualTo(2f));
            Assert.That(run.ActionStepIndex, Is.EqualTo(1));
        }

        [Test]
        public void TimelineStop_IncludesUntriggeredNodeOnCurrentIntegerDay()
        {
            var random = new RandomService();
            random.Init(67890UL);
            typeof(GameApp)
                .GetProperty(nameof(GameApp.Random))
                ?.GetSetMethod(nonPublic: true)
                ?.Invoke(null, new object[] { random });

            GameRun run = CreateRun();
            cfg.GameAction action = _tables.TbAction.DataList.First();
            run.BeginTimeline(
                "test",
                7f,
                new[] { new RuntimeTimelineNode("current_day", "test", 2, action.Id) });
            run.CurrentDay = 2f;
            var context = new ActionExecutionContext(action, 0, 0, "daily", 3f)
            {
                TimelineStopChance = 1f,
            };

            ActionExecutor.Commit(run, context);

            Assert.That(context.TimelineStopTriggered, Is.True);
            Assert.That(context.TimelineStopDay, Is.EqualTo(2));
            Assert.That(run.CurrentDay, Is.EqualTo(2f));
            Assert.That(run.ActionStepIndex, Is.EqualTo(1));
        }

        [Test]
        public void PendingRepeatAndTimelineStopSnapshot_RoundTripSave()
        {
            GameRun run = CreateRun();
            cfg.GameAction action = _tables.TbAction.DataList.First();
            var context = new ActionExecutionContext(action)
            {
                SourceKey = "node",
                TimelineStopChance = 0.3f,
                NodeRepeatIndex = 2,
                NodeRepeatTotal = 2,
            };
            run.SetPendingActionExecution(context, ActionOutcome.Immediate("ok"));

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            PendingActionExecutionSaveData pending = restored.GetPendingActionExecution();

            Assert.That(pending.TimelineStopChance, Is.EqualTo(0.3f));
            Assert.That(pending.NodeRepeatIndex, Is.EqualTo(2));
            Assert.That(pending.NodeRepeatTotal, Is.EqualTo(2));
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
