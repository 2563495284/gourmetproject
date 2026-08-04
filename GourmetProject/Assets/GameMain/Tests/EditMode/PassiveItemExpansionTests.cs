using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Config;
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

namespace GourmetProject.Tests.EditMode
{
    public sealed class PassiveItemExpansionTests
    {
        private static readonly string[] FinalizedHashItemIds =
        {
            "item_discount_active",
            "item_discount_adjust",
            "item_discount_active_festival",
            "item_discount_adjust_festival",
            "item_famous_knife",
            "item_heart_flat_all",
            "item_empty_heart_mult_all",
            "item_timeline_random",
            "item_lucky_chance",
            "item_more_events",
            "item_copy_food",
        };

        private static readonly string[] NewItemIds =
        {
            "item_discount_active",
            "item_discount_adjust",
            "item_discount_active_festival",
            "item_discount_adjust_festival",
            "item_famous_knife",
            "item_heart_flat_all",
            "item_empty_heart_mult_all",
            "item_timeline_random",
            "item_lucky_chance",
            "item_more_events",
            "item_copy_food",
            "item_perma_flat_all_plus",
            "item_extra_active_slots_max",
            "item_more_super_actions",
            "item_slot_win_chance",
            "item_dish_hidden_bonus",
            "item_fragment_hidden_bonus",
            "item_passive_hidden_bonus",
        };

        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [SetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void EveryConfiguredPassiveItem_HasAnExplicitModel()
        {
            foreach (cfg.PassiveItem item in _tables.TbPassiveItem.DataList)
            {
                Assert.That(
                    PassiveItemModelRegistry.HasModel(item.Id),
                    Is.True,
                    $"{item.Id} 未注册 PassiveItemModel，会意外落入 NoopPassiveModel。");
            }

            foreach (string itemId in NewItemIds)
            {
                Assert.That(
                    _tables.TbPassiveItem.GetOrDefault(itemId),
                    Is.Not.Null,
                    $"新装饰品 {itemId} 未进入运行时配置。");
                Assert.That(PassiveItemModelRegistry.HasModel(itemId), Is.True, itemId);
            }
        }

        [Test]
        public void FinalizedHashItems_HaveShortNamesAndGeneratedRuntimeDescriptions()
        {
            Assert.That(_tables.TbPassiveItem.DataList, Has.Count.EqualTo(91));
            foreach (string itemId in FinalizedHashItemIds)
            {
                cfg.PassiveItem item = _tables.TbPassiveItem.GetOrDefault(itemId);
                Assert.That(item, Is.Not.Null, itemId);
                Assert.That(
                    new System.Globalization.StringInfo(item.Name).LengthInTextElements,
                    Is.LessThanOrEqualTo(5),
                    $"{itemId} 的名称超过5个字：{item.Name}");
            }

            Assert.That(
                _tables.TbPassiveItem.Get("item_lucky_chance").Desc,
                Is.EqualTo("事件行动中\n奖励事件随机权重+20%"));
            Assert.That(
                _tables.TbPassiveItem.Get("item_more_events").Desc,
                Is.EqualTo("事件行动大组\n随机权重+20%"));
        }

        [TestCase("item_perma_flat_all_plus", typeof(PermanentAddFlatAllModel))]
        [TestCase("item_extra_active_slots_max", typeof(ExtraActiveSlotModel))]
        [TestCase("item_more_super_actions", typeof(MoreSuperActionsModel))]
        [TestCase("item_slot_win_chance", typeof(SlotWinChanceModel))]
        [TestCase("item_dish_hidden_bonus", typeof(HiddenScoreBonusModel))]
        [TestCase("item_fragment_hidden_bonus", typeof(HiddenScoreBonusModel))]
        [TestCase("item_passive_hidden_bonus", typeof(HiddenScoreBonusModel))]
        [TestCase("item_discount_food_festival", typeof(DiscountFoodModel))]
        [TestCase("item_discount_fragment_festival", typeof(DiscountFragmentModel))]
        [TestCase("item_discount_passive_festival", typeof(DiscountPassiveModel))]
        [TestCase("item_discount_remove_festival", typeof(DiscountRemoveModel))]
        public void NewAndRenamedIds_UseTheExpectedExistingModel(string itemId, Type expectedType)
        {
            Assert.That(PassiveItemModelRegistry.Create(itemId), Is.TypeOf(expectedType));
        }

        [Test]
        public void ActiveSlotCapacity_StartsAtFourAndCapsAtNine()
        {
            GameRun run = CreateRun();

            Assert.That(_tables.TbGameBase.BaseActiveSlots, Is.EqualTo(4));
            Assert.That(run.ActiveSlotCapacity, Is.EqualTo(4));

            AttachPassiveModel(run, "item_extra_active_slots_max", effectValue: 20f);

            Assert.That(run.ActiveSlotCapacity, Is.EqualTo(9));
        }

        [Test]
        public void HiddenScoreBonus_UsesConfiguredRewardChannelsAndHasNoGoldChannel()
        {
            GameRun run = CreateRun();
            PassiveItemModel model = AttachPassiveModel(
                run,
                "item_dish_hidden_bonus",
                targetOffset: 7,
                dishOffset: 11,
                passiveOffset: 13,
                fragmentOffset: 17);
            var runtime = new ItemRuntime(run);

            Assert.That(model.HiddenScoreOffset(HiddenScorePurpose.TargetScore), Is.EqualTo(7f));
            Assert.That(runtime.HiddenScoreOffset(HiddenScorePurpose.Dish), Is.EqualTo(11f));
            Assert.That(runtime.HiddenScoreOffset(HiddenScorePurpose.PassiveItem), Is.EqualTo(13f));
            Assert.That(runtime.HiddenScoreOffset(HiddenScorePurpose.Fragment), Is.EqualTo(17f));
            Assert.That(runtime.HiddenScoreOffset(HiddenScorePurpose.Gold), Is.Zero);
        }

        [Test]
        public void ActiveItemRewardKinds_IgnoreBaseAndSlotHiddenScores()
        {
            GameRun run = CreateRun();
            AttachPassiveModel(
                run,
                "item_passive_hidden_bonus",
                passiveOffset: 500);
            var context = new RewardContext(_tables, run, null, null, null);

            foreach (cfg.RewardKind kind in new[]
            {
                cfg.RewardKind.ActiveItemGrant,
                cfg.RewardKind.ActiveItemStrengthen,
                cfg.RewardKind.ActiveItemAdjust,
            })
            {
                cfg.RewardSlot slot = CreateRewardSlot(kind, passiveHiddenOffset: 900);
                Assert.That(RewardPoolService.ResolveHiddenScoreForSlot(context, slot), Is.Zero, kind.ToString());
            }

            cfg.RewardSlot passiveSlot = CreateRewardSlot(
                cfg.RewardKind.PassiveItemChoice,
                passiveHiddenOffset: 900);
            Assert.That(RewardPoolService.ResolveHiddenScoreForSlot(context, passiveSlot), Is.GreaterThan(900));
        }

        [Test]
        public void ActiveItemPool_UsesOnlyConfiguredBaseWeights()
        {
            GameRun run = CreateRun();
            var progress = new MetaProgressSaveData();
            foreach (cfg.ActiveItem item in _tables.TbActiveItem.DataList)
            {
                progress.AddUnlockedItem(item.Id);
            }

            var rng = new CapturingRandomStream();
            ItemPoolService.Roll(
                _tables,
                run,
                cfg.ItemKind.Active,
                rng,
                count: 1,
                hidden: 99999,
                distanceFloor: 1,
                progress: progress);

            float defaultWeight = Math.Max(float.Epsilon, _tables.TbGameBase.DefaultRandomWeight);
            float[] expected = _tables.TbActiveItem.DataList
                .Select(item => item.BaseWeight > 0f ? item.BaseWeight : defaultWeight)
                .ToArray();
            Assert.That(rng.LastWeights, Is.EqualTo(expected));
        }

        [Test]
        public void FoodDiscardCapacity_UsesHeldDecorationsAndSurvivesRunRestore()
        {
            GameRun run = CreateRun();
            int baseCapacity = new ItemRuntime(run).FoodDiscardCapacity();
            cfg.PassiveItem item = _tables.TbPassiveItem.Get("item_trash_evolve");

            ItemAcquireResult acquired = run.AcquireItem(item.Id, fallbackGold: 0);

            Assert.That(acquired.Outcome, Is.EqualTo(ItemAcquireOutcome.Added));
            Assert.That(
                new ItemRuntime(run).FoodDiscardCapacity(),
                Is.EqualTo(baseCapacity + (int)item.EffectValue));

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            Assert.That(
                new ItemRuntime(restored).FoodDiscardCapacity(),
                Is.EqualTo(baseCapacity + (int)item.EffectValue));
        }

        [Test]
        public void HeartScorePassives_ReadHeartsWhenBattleSessionSettles()
        {
            GameRun run = CreateRun();
            run.AdjustHeartCapacity(2);
            AttachPassiveModel(run, "item_heart_flat_all", effectValue: 5f);
            AttachPassiveModel(run, "item_empty_heart_mult_all", effectValue: 0.1f);

            Assert.That(run.HeartCapacity, Is.EqualTo(5));
            Assert.That(run.HeartsRemaining, Is.EqualTo(3));

            BattleSession session = BuildHeartPassiveSession(run);

            // 会话构建完成后再变更心数，验证结算不会使用建局时的旧快照。
            Assert.That(run.TryLoseHeart(out _, out _), Is.True);
            ScoreResult result = session.Settle();

            Assert.That(run.HeartsRemaining, Is.EqualTo(2));
            Assert.That(result.DishScores, Has.Count.EqualTo(2));
            Assert.That(result.DishScores.All(score =>
                Math.Abs(score.FlatBonus - 10f) < 0.0001f), Is.True);
            Assert.That(result.DishScores.All(score =>
                Math.Abs(score.Multiplier - 1.3f) < 0.0001f), Is.True);
            Assert.That(result.PermanentFlatDeltas, Is.Empty);
            Assert.That(result.PermanentMultDeltas, Is.Empty);
            Assert.That(
                result.ScoreLines.Count(line =>
                    line.Source?.Type == ScoreSourceType.Relic),
                Is.EqualTo(4));
            Assert.That(
                result.ScoreLines
                    .Where(line => line.Source?.Type == ScoreSourceType.Relic)
                    .All(line => line.Phase == ScorePhase.BeforeAll),
                Is.True);
        }

        [Test]
        public void CopyFood_ClonesCompleteRecipeEntryAndRoundTripsSave()
        {
            GameRun run = CreateRun();
            while (run.RecipeEntries.Count > 1)
            {
                Assert.That(run.RemoveBonusDishAt(run.RecipeEntries.Count - 1), Is.True);
            }

            Assert.That(run.RecipeEntries, Has.Count.EqualTo(1));
            Assert.That(run.AddRecipeFlavor(0, "t_sweet"), Is.True);
            Assert.That(run.AddRecipeExtraSkill(0, _database.AllSkills.First().Id), Is.True);
            Assert.That(run.AddRecipeScoreFlat(0, 7f), Is.True);
            Assert.That(run.MultiplyRecipeScore(0, 1.5f), Is.True);
            RecipeBookSlot source = run.RecipeEntries[0];

            RecipeMutationResult result = PassiveRecipeMutationService.CopyRandomFood(
                run,
                "复制食物",
                count: 1,
                rng: new CapturingRandomStream());

            Assert.That(result.HasChanges, Is.True);
            Assert.That(run.RecipeEntries, Has.Count.EqualTo(2));
            RecipeBookSlot clone = run.RecipeEntries[1];
            Assert.That(clone, Is.Not.SameAs(source));
            Assert.That(clone.DishId, Is.EqualTo(source.DishId));
            Assert.That(clone.ExtraFlavorIds, Is.EqualTo(source.ExtraFlavorIds));
            Assert.That(clone.ExtraSkillIds, Is.EqualTo(source.ExtraSkillIds));
            Assert.That(clone.ScoreFlatBonus, Is.EqualTo(source.ScoreFlatBonus));
            Assert.That(clone.ScoreMultiplier, Is.EqualTo(source.ScoreMultiplier));
            Assert.That(result.Entries.Single().DishIndex, Is.EqualTo(1));
            Assert.That(result.Entries.Single().Before.DishId, Is.Empty);
            Assert.That(result.Entries.Single().After.DishId, Is.EqualTo(source.DishId));

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            Assert.That(restored.RecipeEntries, Has.Count.EqualTo(2));
            RecipeBookSlot restoredClone = restored.RecipeEntries[1];
            Assert.That(restoredClone.DishId, Is.EqualTo(source.DishId));
            Assert.That(restoredClone.ExtraFlavorIds, Is.EqualTo(source.ExtraFlavorIds));
            Assert.That(restoredClone.ExtraSkillIds, Is.EqualTo(source.ExtraSkillIds));
            Assert.That(restoredClone.ScoreFlatBonus, Is.EqualTo(7f));
            Assert.That(restoredClone.ScoreMultiplier, Is.EqualTo(1.5f));
        }

        [Test]
        public void CopyFood_EmptyRecipeIsSafeNoOp()
        {
            GameRun run = CreateRun();
            while (run.RecipeEntries.Count > 0)
            {
                Assert.That(run.RemoveBonusDishAt(run.RecipeEntries.Count - 1), Is.True);
            }

            RecipeMutationResult result = PassiveRecipeMutationService.CopyRandomFood(
                run,
                "复制食物",
                count: 1,
                rng: new CapturingRandomStream());

            Assert.That(result.HasChanges, Is.False);
            Assert.That(run.RecipeEntries, Is.Empty);
        }

        private GameRun CreateRun()
        {
            string characterId = _tables.TbCharacter.DataList.First().Id;
            return new GameRun(_tables, _database, characterId, "passive-expansion-tests");
        }

        private static BattleSession BuildHeartPassiveSession(GameRun run)
        {
            var definition = new DishDef(
                "heart_passive_test_dish",
                "测试食物",
                10,
                DishShape.FromRows(new[] { "X" }),
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                false);
            var database = new GameplayDatabase(
                new[] { definition },
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var table = new DiningTable(2, 1);
            for (int index = 0; index < 2; index++)
            {
                var placement = new Placement(
                    definition.Shape,
                    0,
                    new GridPos(index, 0));
                table.Place(new DishInstance(
                    index + 1,
                    definition,
                    placement,
                    Array.Empty<string>(),
                    Array.Empty<string>()));
            }

            var calculator = new ScoreCalculator(
                effectSources: ItemScoreEffectAdapter.BuildScoreSources(run));
            return new BattleSession(
                table,
                database,
                new Xoshiro256SS(123UL),
                Array.Empty<RecipeSlot>(),
                requiredScore: 1,
                calculator: calculator);
        }

        private static PassiveItemModel AttachPassiveModel(
            GameRun run,
            string itemId,
            float effectValue = 0f,
            int targetOffset = 0,
            int dishOffset = 0,
            int passiveOffset = 0,
            int fragmentOffset = 0)
        {
            cfg.PassiveItem configured = CreatePassiveItem(
                itemId,
                effectValue,
                targetOffset,
                dishOffset,
                passiveOffset,
                fragmentOffset);
            var state = new RunItemState(itemId, 1);
            PassiveItemModel model = PassiveItemModelRegistry.Create(itemId);
            model.Bind(run, ItemDefinition.From(configured), state);
            state.Model = model;
            ((List<RunItemState>)run.Items).Add(state);
            return model;
        }

        private static cfg.PassiveItem CreatePassiveItem(
            string itemId,
            float effectValue,
            int targetOffset,
            int dishOffset,
            int passiveOffset,
            int fragmentOffset)
        {
            string value = effectValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string json = $@"{{
                ""id"":""{itemId}"",
                ""name"":""测试装饰品和消耗品"",
                ""desc"":"""",
                ""quality"":0,
                ""specialTags"":0,
                ""effectValue"":{value},
                ""effectParam"":"""",
                ""baseWeight"":1,
                ""hiddenRange"":{{""min"":0,""max"":0}},
                ""targetScoreHiddenOffset"":{targetOffset},
                ""dishHiddenOffset"":{dishOffset},
                ""passiveItemHiddenOffset"":{passiveOffset},
                ""fragmentHiddenOffset"":{fragmentOffset},
                ""termId"":"""",
                ""price"":1
            }}";
            return cfg.PassiveItem.DeserializePassiveItem(JSON.Parse(json));
        }

        private static cfg.RewardSlot CreateRewardSlot(
            cfg.RewardKind kind,
            int passiveHiddenOffset)
        {
            string json = $@"{{
                ""id"":""test_{kind}"",
                ""groupId"":""test"",
                ""kind"":{(int)kind},
                ""choiceCount"":1,
                ""requiredPickCount"":1,
                ""weight"":1,
                ""poolId"":""test"",
                ""dishHiddenOffset"":[0,0],
                ""passiveItemHiddenOffset"":[{passiveHiddenOffset},{passiveHiddenOffset}],
                ""fragmentHiddenOffset"":[0,0],
                ""goldHiddenOffset"":[0,0],
                ""name"":""测试"",
                ""desc"":"""",
                ""ruleTemplate"":""""
            }}";
            return cfg.RewardSlot.DeserializeRewardSlot(JSON.Parse(json));
        }

        private sealed class CapturingRandomStream : IRandomStream
        {
            public IReadOnlyList<float> LastWeights { get; private set; } = Array.Empty<float>();

            public RngState State { get; set; }

            public uint NextUInt() => 0;

            public ulong NextULong() => 0;

            public int Range(int minInclusive, int maxExclusive) => minInclusive;

            public float Range(float minInclusive, float maxExclusive) => minInclusive;

            public float NextFloat() => 0f;

            public double NextDouble() => 0d;

            public bool NextBool(double probability = 0.5d) => probability > 0d;

            public void Shuffle<T>(IList<T> list)
            {
            }

            public T Pick<T>(IReadOnlyList<T> list) => list[0];

            public int WeightedPickIndex(IReadOnlyList<float> weights)
            {
                LastWeights = weights.ToArray();
                return 0;
            }
        }
    }
}
