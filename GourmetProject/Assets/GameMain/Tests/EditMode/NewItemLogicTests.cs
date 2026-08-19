using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class NewItemLogicTests
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
        public void NodeCopyTicket_EnumeratesPastTriggeredAndFutureUntriggeredNodes()
        {
            GameRun run = CreateRun("node-copy-any");
            run.BeginTimeline(
                "test",
                7f,
                new[]
                {
                    new RuntimeTimelineNode("past", "test", 1, "act_shop"),
                    new RuntimeTimelineNode("current", "test", 3, "act_interest"),
                    new RuntimeTimelineNode("future", "test", 5, "act_shop"),
                });
            run.CurrentDay = 3f;
            run.MarkNodeTriggered("past");

            ItemDefinition item = ItemDefinition.Get(
                _tables,
                "item_active_execute_future_node",
                cfg.ItemKind.Active);
            var context = new ActionSelectUseContext(run, null);
            IReadOnlyList<ActiveTarget> targets = context.EnumerateTargets(item);
            IReadOnlyList<ActiveTarget> shopTargets = new ShopUseContext(run).EnumerateTargets(item);

            Assert.That(item.Name, Is.EqualTo("节点复制单"));
            Assert.That(
                TimelineService.GetCloneableNodes(run).Select(node => node.Id),
                Is.EquivalentTo(new[] { "past", "current", "future" }));
            Assert.That(
                TimelineService.GetFutureUntriggeredNodes(run).Select(node => node.Id),
                Is.EquivalentTo(new[] { "future" }));
            Assert.That(targets.Select(target => target.Id), Is.EquivalentTo(new[] { "past", "current", "future" }));
            Assert.That(shopTargets.Select(target => target.Id), Is.EquivalentTo(new[] { "past", "current", "future" }));

            ActiveItemUseResult result = ActiveItemEffectRegistry.Apply(
                context,
                item,
                new[] { new ActiveTarget("past", 1, targetKind: cfg.ItemTargetKind.Global) });
            Assert.That(result.Success, Is.True);
            cfg.TimelineNode cloned = TimelineService.GetNode(run, result.CreatedTimelineNodeId);
            Assert.That(cloned, Is.Not.Null);
            Assert.That(cloned.ActionId, Is.EqualTo("act_shop"));
            Assert.That(cloned.Day, Is.EqualTo(3));
        }

        [Test]
        public void RestoreHeartTicket_AddsRestoreHeartNodeAndDisplaysAsEvent()
        {
            GameRun run = CreateRun("restore-heart");
            run.BeginTimeline(
                "test",
                7f,
                new[] { new RuntimeTimelineNode("n1", "test", 1, "act_shop") });
            run.CurrentDay = 1f;

            cfg.ActiveItem config = _tables.TbActiveItem.Get("item_active_add_restore_heart_node");
            cfg.GameAction action = _tables.TbAction.Get("act_restore_heart");
            ItemDefinition item = ItemDefinition.Get(
                _tables,
                "item_active_add_restore_heart_node",
                cfg.ItemKind.Active);
            var context = new ActionSelectUseContext(run, null);

            Assert.That(config.Name, Is.EqualTo("回心单"));
            Assert.That(config.EffectType, Is.EqualTo(ItemEffectTypes.TimelineAddRestoreHeartNode));
            Assert.That(config.EffectParam, Is.EqualTo("act_restore_heart"));
            Assert.That(ItemActiveUsage.IsTimelineAddEffect(item.EffectType), Is.True);
            Assert.That(action.EffectType, Is.EqualTo(cfg.EffectType.RestoreHearts));
            Assert.That(action.EffectValue, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(ActionDisplay.KindOf(_tables, action), Is.EqualTo(ActionDisplayKind.Event));

            ActiveItemUseResult result = ActiveItemEffectRegistry.Apply(
                context,
                item,
                new[] { new ActiveTarget("2", 2, targetKind: cfg.ItemTargetKind.Global) });
            Assert.That(result.Success, Is.True);
            cfg.TimelineNode node = TimelineService.GetNode(run, result.CreatedTimelineNodeId);
            Assert.That(node, Is.Not.Null);
            Assert.That(node.ActionId, Is.EqualTo("act_restore_heart"));
            Assert.That(node.Day, Is.EqualTo(2));

            Assert.That(run.TryLoseHearts(1, out _, out int afterLoss), Is.True);
            string feedback = EffectResolver.Apply(
                run,
                action.EffectType,
                action.EffectValue,
                action.EffectParam,
                new Xoshiro256SS(19UL));
            StringAssert.Contains("恢复", feedback);
            Assert.That(run.HeartsRemaining, Is.EqualTo(afterLoss + 1));
        }

        [Test]
        public void NewPassiveItems_BindConfiguredModelsAndValues()
        {
            GameRun run = CreateRun("new-passives-bind");
            AssertAcquire(run, "item_count_as_plus1");
            AssertAcquire(run, "item_self_count_flat");
            AssertAcquire(run, "item_self_count_mult");
            AssertAcquire(run, "item_edge_count_as");
            AssertAcquire(run, "item_transfer_target_flat");
            AssertAcquire(run, "item_transfer_source_flat");
            AssertAcquire(run, "item_cake_req_minus_30");
            AssertAcquire(run, "item_random_two_as_cake");
            AssertAcquire(run, "item_cake_on_settle");

            var runtime = new ItemRuntime(run);
            Assert.That(ItemScoreEffectAdapter.ExtraCountAsPerDish(run), Is.EqualTo(1));
            Assert.That(runtime.SweetTransferTargetFlat(), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(runtime.SweetTransferSourceFlat(), Is.EqualTo(2f).Within(0.0001f));
            Assert.That(runtime.CakeThresholdReduction(), Is.EqualTo(30));
            Assert.That(
                ItemScoreEffectAdapter.BuildSpecs(run).Select(spec => spec.Type),
                Is.EquivalentTo(new[]
                {
                    ItemScoreEffectType.PerDishFlatTimesOwnCountAs,
                    ItemScoreEffectType.PerDishMultFlatTimesOwnCountAs,
                    ItemScoreEffectType.TagCountAsBonus,
                    ItemScoreEffectType.RandomDishesTemporaryCategory,
                    ItemScoreEffectType.CakeLayersPerCakeDish,
                }));
        }

        [Test]
        public void CountAsPlus1_StacksWithNineGridPlatter()
        {
            GameRun run = CreateRun("count-as-stack");
            AssertAcquire(run, "item_count_as_all");
            AssertAcquire(run, "item_count_as_plus1");
            Assert.That(ItemScoreEffectAdapter.ExtraCountAsPerDish(run), Is.EqualTo(3));
        }

        [Test]
        public void SelfCountFlat_AddsFiveTimesEffectiveCountAsAfterAll()
        {
            DishInstance dish = Dish(1, "counted", 10, 0, 0, countAs: 2);
            ScoreResult result = Settle(
                new[] { dish },
                new DiningTable(1, 1),
                extraCountAsPerDish: 1,
                new ItemScoreSpec(
                    ItemScoreEffectType.PerDishFlatTimesOwnCountAs,
                    5f,
                    string.Empty,
                    "item_self_count_flat",
                    "份数记分牌"));

            Assert.That(result.DishScores.Single().EffectiveCountAs, Is.EqualTo(3));
            Assert.That(result.DishScores.Single().FlatBonus.ToDouble(), Is.EqualTo(15d).Within(0.0001d));
            Assert.That(result.Total.ToDouble(), Is.EqualTo(25d).Within(0.0001d));
        }

        [Test]
        public void SelfCountMult_AddsPointTwoTimesEffectiveCountAsAfterAll()
        {
            DishInstance dish = Dish(1, "counted", 10, 0, 0, countAs: 2);
            ScoreResult result = Settle(
                new[] { dish },
                new DiningTable(1, 1),
                extraCountAsPerDish: 1,
                new ItemScoreSpec(
                    ItemScoreEffectType.PerDishMultFlatTimesOwnCountAs,
                    0.2f,
                    string.Empty,
                    "item_self_count_mult",
                    "份数倍率架"));

            Assert.That(result.DishScores.Single().Multiplier.ToDouble(), Is.EqualTo(1.6d).Within(0.0001d));
            Assert.That(result.Total.ToDouble(), Is.EqualTo(16d).Within(0.0001d));
        }

        [Test]
        public void EdgeCountAs_AddsOnePortionOnlyOnEdgeDishes()
        {
            DishInstance edge = Dish(1, "edge", 10, 0, 0);
            DishInstance center = Dish(2, "center", 10, 1, 1);
            var table = new DiningTable(3, 3);
            table.Place(edge);
            table.Place(center);
            ScoreResult result = Settle(
                new[] { edge, center },
                table,
                extraCountAsPerDish: 0,
                new ItemScoreSpec(
                    ItemScoreEffectType.TagCountAsBonus,
                    1f,
                    "position:edge",
                    "item_edge_count_as",
                    "沿边餐碟"));

            Assert.That(ScoreOf(result, edge).EffectiveCountAs, Is.EqualTo(2));
            Assert.That(ScoreOf(result, center).EffectiveCountAs, Is.EqualTo(1));
        }

        [Test]
        public void RandomTwoAsCakeThenCakeKnife_AddsThreeLayersPerTemporaryCake()
        {
            DishInstance first = Dish(1, "a", 10, 0, 0);
            DishInstance second = Dish(2, "b", 10, 1, 0);
            DishInstance third = Dish(3, "c", 10, 2, 0);
            var table = new DiningTable(3, 1);
            table.Place(first);
            table.Place(second);
            table.Place(third);
            ScoreResult result = Settle(
                new[] { first, second, third },
                table,
                extraCountAsPerDish: 0,
                new ItemScoreSpec(
                    ItemScoreEffectType.RandomDishesTemporaryCategory,
                    2f,
                    "category:cake",
                    "item_random_two_as_cake",
                    "随机蛋糕签"),
                new ItemScoreSpec(
                    ItemScoreEffectType.CakeLayersPerCakeDish,
                    3f,
                    string.Empty,
                    "item_cake_on_settle",
                    "切块蛋糕刀"));

            Assert.That(result.HappyCakeLayerDelta, Is.EqualTo(6));
        }

        [Test]
        public void CakeOnSettle_AddsThreeLayersPerExistingCakeDish()
        {
            DishInstance cake = Dish(1, "cake", 10, 0, 0, category: "cake");
            DishInstance plain = Dish(2, "plain", 10, 1, 0);
            var table = new DiningTable(2, 1);
            table.Place(cake);
            table.Place(plain);
            ScoreResult result = Settle(
                new[] { cake, plain },
                table,
                extraCountAsPerDish: 0,
                new ItemScoreSpec(
                    ItemScoreEffectType.CakeLayersPerCakeDish,
                    3f,
                    string.Empty,
                    "item_cake_on_settle",
                    "切块蛋糕刀"));

            Assert.That(result.HappyCakeLayerDelta, Is.EqualTo(3));
        }

        [Test]
        public void SweetTransferFlats_WriteBackSourceAndTargetRecipeDeltasOnSettle()
        {
            BattleSession session = CreateTransferSession(SkillTrigger.OnSettle);
            session.SweetTransferTargetFlat = 1f;
            session.SweetTransferSourceFlat = 2f;

            ScoreResult score = session.Settle();

            Assert.That(score.SkillTransfers, Is.Not.Empty);
            Assert.That(
                session.LastRecipeScoreFlatDeltas.Select(delta => (delta.DishIndex, delta.Delta.ToDouble())),
                Is.EquivalentTo(new[] { (0, 1d), (1, 2d) }));
            Assert.That(session.DiningTable.Dishes.Single(d => d.SourceDishIndex == 0).PermanentFlatBonus.ToDouble(),
                Is.EqualTo(1d).Within(0.0001d));
            Assert.That(session.DiningTable.Dishes.Single(d => d.SourceDishIndex == 1).PermanentFlatBonus.ToDouble(),
                Is.EqualTo(2d).Within(0.0001d));
        }

        [Test]
        public void SweetTransferFlats_KeepServePhaseRecipeDeltasAfterSettle()
        {
            BattleSession session = CreateTransferSession(SkillTrigger.OnServe);
            session.SweetTransferTargetFlat = 1f;
            session.SweetTransferSourceFlat = 2f;
            Assert.That(
                session.LastRecipeScoreFlatDeltas.Select(delta => (delta.DishIndex, delta.Delta.ToDouble())),
                Is.EquivalentTo(new[] { (0, 1d), (1, 2d) }));

            session.Settle();

            Assert.That(
                session.LastRecipeScoreFlatDeltas.Select(delta => (delta.DishIndex, delta.Delta.ToDouble())),
                Is.EquivalentTo(new[] { (0, 1d), (1, 2d) }));
        }

        private BattleSession CreateTransferSession(SkillTrigger trigger)
        {
            SkillDef transfer = TransferSkill("transfer_payload", trigger);
            var target = new DishDef(
                "target_dish",
                "target_dish",
                10,
                DishShape.FromRows(new[] { "X" }),
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty);
            var source = new DishDef(
                "source_dish",
                "source_dish",
                10,
                DishShape.FromRows(new[] { "X" }),
                0,
                0,
                1f,
                new[] { transfer.Id },
                string.Empty);
            var session = new BattleSession(
                new DiningTable(2, 1),
                new GameplayDatabase(
                    new[] { target, source },
                    new[] { transfer },
                    Array.Empty<FlavorDef>(),
                    Array.Empty<RecipeDef>()),
                new Xoshiro256SS(42UL),
                new[]
                {
                    new RecipeSlot("target", new[]
                    {
                        new RecipeSlotEntry(target.Id, null, null, 1f, 0f, 0, 0),
                    }),
                    new RecipeSlot("source", new[]
                    {
                        new RecipeSlotEntry(source.Id, null, null, 1f, 0f, 0, 1),
                    }),
                },
                requiredScore: 0);

            PlacePrepared(session, 0);
            PlacePrepared(session, 1);
            return session;
        }

        private static void PlacePrepared(BattleSession session, int slotIndex)
        {
            ServePrepareResult prepared = session.PrepareServeAutomatically(slotIndex);
            Assert.That(prepared.Success, Is.True);
            Assert.That(prepared.PreparedDish.Placements, Is.Not.Empty);
            ServeResult placed = session.CommitPreparedServe(prepared.PreparedDish.Placements[0]);
            Assert.That(placed.Success, Is.True);
        }

        private static SkillDef TransferSkill(string id, SkillTrigger trigger)
        {
            var payload = new SkillRuleDef(
                id + "_payload",
                id,
                0,
                trigger,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.AddFlat,
                SkillScope.Self,
                0,
                new[] { 0f },
                Array.Empty<string>());
            var transfer = new SkillRuleDef(
                id + "_transfer",
                id,
                1,
                trigger,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.TransferSkills,
                SkillScope.All,
                1,
                new[] { 0f },
                Array.Empty<string>());
            return new SkillDef(id, id, string.Empty, Array.Empty<string>(), new[] { payload, transfer });
        }

        private static ScoreResult Settle(
            IReadOnlyList<DishInstance> dishes,
            DiningTable table,
            int extraCountAsPerDish,
            params ItemScoreSpec[] specs)
        {
            var source = new ItemScoreEffectSource(specs);
            return new ScoreCalculator(effectSources: new[] { source }).Calculate(
                table,
                new GameplayDatabase(
                    dishes.Select(dish => dish.Def).ToArray(),
                    Array.Empty<SkillDef>(),
                    Array.Empty<FlavorDef>(),
                    Array.Empty<RecipeDef>()),
                extraCountAsPerDish: extraCountAsPerDish);
        }

        private static DishInstance Dish(
            int instanceId,
            string id,
            int deliciousness,
            int x,
            int y,
            int countAs = 1,
            string category = "")
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            var def = new DishDef(
                id,
                id,
                deliciousness,
                shape,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                countAs: countAs,
                category: category);
            return new DishInstance(
                instanceId,
                def,
                new Placement(shape, 0, new GridPos(x, y)),
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        private static DishScore ScoreOf(ScoreResult result, DishInstance dish)
            => result.DishScores.Single(score => score.DishInstanceId == dish.Id);

        private static void AssertAcquire(GameRun run, string itemId)
        {
            ItemAcquireResult result = run.AcquireItem(itemId, fallbackGold: 0, fireOnAcquire: false);
            Assert.That(result.Outcome, Is.EqualTo(ItemAcquireOutcome.Added), itemId);
            Assert.That(PassiveItemModelRegistry.HasModel(itemId), Is.True, itemId);
        }

        private GameRun CreateRun(string seed)
            => new GameRun(
                _tables,
                _database,
                "glutton_dog",
                seed,
                execution: RunExecutionEnvironment.CreateIsolated(_tables, seed));
    }
}
