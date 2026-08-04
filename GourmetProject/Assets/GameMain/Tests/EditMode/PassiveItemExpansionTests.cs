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
using Luban.SimpleJSON;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class PassiveItemExpansionTests
    {
        private static readonly string[] NewItemIds =
        {
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
                    $"新被动道具 {itemId} 未进入运行时配置。");
                Assert.That(PassiveItemModelRegistry.HasModel(itemId), Is.True, itemId);
            }
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

        private GameRun CreateRun()
        {
            string characterId = _tables.TbCharacter.DataList.First().Id;
            return new GameRun(_tables, _database, characterId, "passive-expansion-tests");
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
                ""name"":""测试道具"",
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
