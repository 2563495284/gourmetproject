using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using GourmetProject.Runtime;
using Luban.SimpleJSON;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BusinessPassiveSettlementTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);

            var random = new RandomService();
            random.Init("business-passive-settlement-tests");
            typeof(GameApp)
                .GetProperty(nameof(GameApp.Random))
                ?.GetSetMethod(nonPublic: true)
                ?.Invoke(null, new object[] { random });
        }

        [Test]
        public void RequiredScore_ModifiersApplyToNormalAndSuper()
        {
            AssertRequiredScoreModifier(
                cfg.FoodActionKind.Normal,
                "item_req_normal_up");
            AssertRequiredScoreModifier(
                cfg.FoodActionKind.Super,
                "item_req_super_down");
        }

        [Test]
        public void RequiredScore_NormalDownAndUp_AddBeforeSingleRounding()
        {
            GameRun run = CreateRun();
            run.AcquireItem("item_req_normal_down", 0);
            run.AcquireItem("item_req_normal_up", 0);

            int result = new ItemRuntime(run).ModifyRequiredScore(
                123,
                cfg.FoodActionKind.Normal);

            Assert.That(result, Is.EqualTo(123), "-20% 与 +20% 应在同一百分比加区相互抵消");
        }

        [Test]
        public void ScoreToOne_ConsumesOnBusinessEntryButNotFeast()
        {
            GameRun run = CreateRun();
            run.AcquireItem("item_score_to_one", 0);
            while (run.ScoreToOneRemaining > 0)
            {
                run.ConsumeScoreToOneMeal();
            }

            run.AddScoreToOneMeals(2);
            ActionOutcome normal = ExecuteFood(run, ActionOfKind(cfg.FoodActionKind.Normal), 0);
            Assert.That(normal.RequiredScore, Is.EqualTo(1));
            Assert.That(run.ScoreToOneRemaining, Is.EqualTo(1));

            ExecuteFood(run, ActionOfKind(cfg.FoodActionKind.Feast), 1);
            Assert.That(run.ScoreToOneRemaining, Is.EqualTo(1), "星级评鉴不属于营业，不消耗免检次数");

            ActionOutcome super = ExecuteFood(run, ActionOfKind(cfg.FoodActionKind.Super), 2);
            Assert.That(super.RequiredScore, Is.EqualTo(1));
            Assert.That(run.ScoreToOneRemaining, Is.Zero);
        }

        [TestCase(cfg.FoodActionKind.Normal, 80)]
        [TestCase(cfg.FoodActionKind.Super, 80)]
        [TestCase(cfg.FoodActionKind.Feast, 100)]
        public void MealGoldPercent_OnlyChangesBusinessBaseGold(
            cfg.FoodActionKind kind,
            int expectedGold)
        {
            GameRun run = CreateRun();
            run.AcquireItem("item_gold_meal_penalty", 0);
            cfg.GameAction action = ActionOfKind(kind);
            run.SetLastActionContext(Context(action));
            int before = run.Gold;
            var offer = new RewardOffer(
                100,
                Array.Empty<RewardChoice>(),
                Array.Empty<RewardChoice>());

            RewardGranter.ApplyBaseGold(run, offer);

            Assert.That(run.Gold - before, Is.EqualTo(expectedGold));
        }

        [Test]
        public void ExtraItemChoice_CountsOnlySuperAndRoundTripsBeforeAliveFailureReward()
        {
            GameRun run = CreateRun();
            run.AcquireItem("item_extra_item_choice", 0);
            ActionExecutionContext normal = Context(ActionOfKind(cfg.FoodActionKind.Normal));
            ActionExecutionContext super = Context(ActionOfKind(cfg.FoodActionKind.Super));
            IRandomStream rng = RewardStream("extra-item-before-save");
            var runtime = new ItemRuntime(run);

            for (int i = 0; i < 6; i++)
            {
                Assert.That(runtime.OnFoodBattleSettled(normal, survived: true, rng), Is.Empty);
            }

            for (int i = 0; i < 3; i++)
            {
                Assert.That(runtime.OnFoodBattleSettled(super, survived: true, rng), Is.Empty);
            }

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            IReadOnlyList<FoodSettlementReward> rewards = new ItemRuntime(restored)
                .OnFoodBattleSettled(super, survived: true, RewardStream("extra-item-after-save"));

            Assert.That(rewards, Has.Count.EqualTo(1), "失去红心但仍存活的第 4 次火热营业也应发奖");
            Assert.That(rewards[0].SourceItemId, Is.EqualTo("item_extra_item_choice"));
            Assert.That(rewards[0].Offer.FixedGroups, Is.Not.Empty);
        }

        [Test]
        public void ExtraItemChoice_TerminalFailureCountsButDoesNotGrantReward()
        {
            GameRun run = CreateRun();
            run.AcquireItem("item_extra_item_choice", 0);
            ActionExecutionContext super = Context(ActionOfKind(cfg.FoodActionKind.Super));
            IRandomStream rng = RewardStream("extra-item-terminal");
            var runtime = new ItemRuntime(run);
            for (int i = 0; i < 3; i++)
            {
                runtime.OnFoodBattleSettled(super, survived: true, rng);
            }

            IReadOnlyList<FoodSettlementReward> rewards =
                runtime.OnFoodBattleSettled(super, survived: false, rng);

            Assert.That(rewards, Is.Empty);
            Assert.That(
                run.PassiveModels.Single(model => model.ItemId == "item_extra_item_choice").InfoText,
                Is.EqualTo("0"),
                "终局失败仍完成第 4 次计数，只是不发续局奖励");
        }

        [Test]
        public void ExtraFoodChoice_UsesEffectParamAndCountsTerminalNormalFailure()
        {
            GameRun run = CreateRun();
            PassiveItemModel model = AttachPassiveModel(
                run,
                "item_extra_food_choice",
                "every:2");
            ActionExecutionContext normal = Context(ActionOfKind(cfg.FoodActionKind.Normal));
            IRandomStream rng = RewardStream("extra-food-config-period");

            RewardOffer first = model.OnFoodBattleSettled(normal, survived: false, rng);
            Assert.That(first, Is.Null, "终局失败也应累计日常营业次数，但不能发续局奖励");
            Assert.That(model.InfoText, Is.EqualTo("1"));

            RewardOffer second = model.OnFoodBattleSettled(normal, survived: true, rng);
            Assert.That(second, Is.Not.Null, "every:2 应在第二次日常营业结算触发，而不是写死为 6");
            Assert.That(second.FixedGroups, Is.Not.Empty);
            Assert.That(model.InfoText, Is.EqualTo("0"));
        }

        [Test]
        public void RemovingPassiveByDirectOrReplacementPathClearsOwnedRunState()
        {
            GameRun run = CreateRun();
            run.AcquireItem("item_gold_meal_bonus", 0);
            run.AcquireItem("item_score_to_one", 0);
            run.AcquireItem("item_cake_retain", 0);
            run.SetRetainedHappyCakeLayers(12);

            Assert.That(run.RemoveItem("item_score_to_one"), Is.True);
            Assert.That(run.ScoreToOneRemaining, Is.Zero);

            run.ReplaceItems(Array.Empty<string>());
            Assert.That(run.MealBonusRemaining, Is.Zero);
            Assert.That(run.ConsumeRetainedHappyCakeLayers(), Is.Zero);
        }

        [Test]
        public void LoadingRunWithoutSourcePassiveDropsStaleOwnedRunState()
        {
            RunSaveData save = CreateRun().ToSaveData();
            save.MealBonusRemaining = 7;
            save.ScoreToOneRemaining = 3;
            save.RetainedHappyCakeLayers = 9;

            GameRun restored = GameRun.FromSaveData(_tables, _database, save);

            Assert.That(restored.MealBonusRemaining, Is.Zero);
            Assert.That(restored.ScoreToOneRemaining, Is.Zero);
            Assert.That(restored.ConsumeRetainedHappyCakeLayers(), Is.Zero);
        }

        [Test]
        public void SettledFoodBattleKey_RoundTripsAndRejectsDuplicate()
        {
            GameRun run = CreateRun();
            Assert.That(run.TryMarkFoodBattleSettled("food-battle-a"), Is.True);
            Assert.That(run.TryMarkFoodBattleSettled("food-battle-a"), Is.False);

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());

            Assert.That(restored.TryMarkFoodBattleSettled("food-battle-a"), Is.False);
            Assert.That(restored.TryMarkFoodBattleSettled("food-battle-b"), Is.True);
        }

        private void AssertRequiredScoreModifier(cfg.FoodActionKind kind, string itemId)
        {
            GameRun run = CreateRun();
            run.AcquireItem(itemId, 0);
            cfg.GameAction action = ActionOfKind(kind);
            ActionExecutionContext context = Context(action);
            int baseRequired = HiddenScoreService.TargetScore(run, context);

            ActionOutcome outcome = new FoodBehaviorHandler().Execute(run, context, rng: null);
            int expected = new ItemRuntime(run).ModifyRequiredScore(baseRequired, kind);

            Assert.That(outcome.RequiredScore, Is.EqualTo(expected));
            Assert.That(outcome.RequiredScore, Is.Not.EqualTo(baseRequired));
        }

        private ActionOutcome ExecuteFood(GameRun run, cfg.GameAction action, int step)
        {
            var context = new ActionExecutionContext(
                action,
                step,
                step,
                "business-tests",
                action.MinCostDays);
            return new FoodBehaviorHandler().Execute(run, context, rng: null);
        }

        private cfg.GameAction ActionOfKind(cfg.FoodActionKind kind)
        {
            return _tables.TbAction.DataList.First(action =>
                FoodService.Resolve(_tables, action)?.ActionKind == kind);
        }

        private static ActionExecutionContext Context(cfg.GameAction action)
        {
            return new ActionExecutionContext(
                action,
                0,
                0,
                "business-tests",
                action.MinCostDays);
        }

        private static IRandomStream RewardStream(string key)
        {
            var random = new RandomService();
            random.Init("business-passive-reward-stream");
            return random.DomainStream(SeedDomains.Reward, key);
        }

        private static PassiveItemModel AttachPassiveModel(
            GameRun run,
            string itemId,
            string effectParam)
        {
            string json = $@"{{
                ""id"":""{itemId}"",
                ""name"":""测试装饰品和消耗品"",
                ""desc"":"""",
                ""quality"":0,
                ""specialTags"":0,
                ""effectValue"":0,
                ""effectParam"":""{effectParam}"",
                ""baseWeight"":1,
                ""hiddenRange"":{{""min"":0,""max"":0}},
                ""targetScoreHiddenOffset"":0,
                ""dishHiddenOffset"":0,
                ""passiveItemHiddenOffset"":0,
                ""fragmentHiddenOffset"":0,
                ""termId"":"""",
                ""price"":1
            }}";
            cfg.PassiveItem configured = cfg.PassiveItem.DeserializePassiveItem(JSON.Parse(json));
            var state = new RunItemState(itemId, 1);
            PassiveItemModel model = PassiveItemModelRegistry.Create(itemId);
            model.Bind(run, ItemDefinition.From(configured), state);
            state.Model = model;
            ((List<RunItemState>)run.Items).Add(state);
            return model;
        }

        private GameRun CreateRun()
        {
            string characterId = _tables.TbCharacter.DataList.First().Id;
            return new GameRun(_tables, _database, characterId, "business-passive-tests");
        }
    }
}
