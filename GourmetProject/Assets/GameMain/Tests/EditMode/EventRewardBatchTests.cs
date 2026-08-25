#if UNITY_EDITOR
using System.IO;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class EventRewardBatchTests
    {
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
        public void LastSupperShare_QueuesOneOfferWithTwoDishChoiceGroups()
        {
            GameRun run = CreateRun();
            cfg.EventOption option = _tables.TbEventOption.GetOrDefault("opt_last_supper_share_reward");

            EventResolveResult result = EventService.ResolveOption(run, option, new Xoshiro256SS(101UL));

            Assert.That(result.FollowUpKind, Is.EqualTo(EventFollowUpKind.None));
            Assert.That(run.TryPeekPendingGenericReward(out string key, out _, out RewardOffer offer), Is.True);
            Assert.That(offer.FixedGroups.Count, Is.EqualTo(2));
            AssertDishChoiceGroup(offer.FixedGroups[0], "第一道回礼");
            AssertDishChoiceGroup(offer.FixedGroups[1], "第二道回礼");

            run.ClearPendingGenericRewardOffer(key);
            Assert.That(run.HasPendingGenericRewards, Is.False, "合并奖励清除后不应再出现第二个奖励框。");
        }

        [Test]
        public void LastSupperShare_RequiresBothGroupsAndRestoresPartialProgress()
        {
            GameRun run = CreateRun();
            cfg.EventOption option = _tables.TbEventOption.GetOrDefault("opt_last_supper_share_reward");
            EventService.ResolveOption(run, option, new Xoshiro256SS(202UL));
            Assert.That(run.TryPeekPendingGenericReward(out string key, out _, out RewardOffer offer), Is.True);

            offer.FixedGroups[0].MarkClaimed(0);
            Assert.That(offer.IsFullyClaimed, Is.False);
            run.SetPendingGenericRewardOffer(key, offer);

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            Assert.That(
                restored.TryPeekPendingGenericReward(out string restoredKey, out _, out RewardOffer restoredOffer),
                Is.True);
            Assert.That(restoredKey, Is.EqualTo(key));
            Assert.That(restoredOffer.FixedGroups.Count, Is.EqualTo(2));
            Assert.That(restoredOffer.FixedGroups[0].ClaimedIndices, Is.EqualTo(new[] { 0 }));
            Assert.That(restoredOffer.FixedGroups[1].ClaimedIndices, Is.Empty);
            Assert.That(restoredOffer.IsFullyClaimed, Is.False);

            restoredOffer.FixedGroups[1].MarkClaimed(0);
            Assert.That(restoredOffer.IsFullyClaimed, Is.True);
        }

        [Test]
        public void SingleRewardOption_KeepsImmediateEffectsAndOneRewardGroup()
        {
            GameRun run = CreateRun();
            int goldBefore = run.Gold;
            cfg.EventOption option = BuildOption(
                "test_single_reward_with_gold",
                "单份奖励",
                "[1,12]",
                "[-17,3]",
                "[\"-\",\"dish_choice_3|单份回礼\"]");

            EventService.ResolveOption(run, option, new Xoshiro256SS(303UL));

            Assert.That(run.Gold, Is.EqualTo(System.Math.Max(0, goldBefore - 17)));
            Assert.That(run.TryPeekPendingGenericReward(out string key, out _, out RewardOffer offer), Is.True);
            Assert.That(offer.FixedGroups.Count, Is.EqualTo(1));
            AssertDishChoiceGroup(offer.FixedGroups[0], "单份回礼");
            run.ClearPendingGenericRewardOffer(key);
            Assert.That(run.HasPendingGenericRewards, Is.False);
        }

        [Test]
        public void InvalidRewardInBatch_StillFallsBackToGoldWithoutAddingASecondOffer()
        {
            GameRun run = CreateRun();
            int goldBefore = run.Gold;
            cfg.EventOption option = BuildOption(
                "test_reward_batch_fallback",
                "混合奖励",
                "[12,12]",
                "[3,3]",
                "[\"missing_reward_group|失效奖励\",\"dish_choice_3|有效奖励\"]");

            EventService.ResolveOption(run, option, new Xoshiro256SS(404UL));

            Assert.That(run.Gold, Is.EqualTo(goldBefore + 30));
            Assert.That(run.TryPeekPendingGenericReward(out string key, out _, out RewardOffer offer), Is.True);
            Assert.That(offer.FixedGroups.Count, Is.EqualTo(1));
            AssertDishChoiceGroup(offer.FixedGroups[0], "有效奖励");
            run.ClearPendingGenericRewardOffer(key);
            Assert.That(run.HasPendingGenericRewards, Is.False);
        }

        private GameRun CreateRun() =>
            new(
                _tables,
                _database,
                "glutton_dog",
                "event-reward-batch-tests",
                weekIndex: 1,
                isTutorialRun: false);

        private static cfg.EventOption BuildOption(
            string id,
            string text,
            string effectTypes,
            string effectValues,
            string effectParams)
        {
            string json =
                "{"
                + $"\"id\":\"{id}\","
                + "\"eventId\":\"test_event\","
                + "\"parentId\":\"\","
                + $"\"text\":\"{text}\","
                + "\"resultText\":\"\","
                + "\"condition\":\"\","
                + "\"conditionText\":\"\","
                + $"\"effectTypes\":{effectTypes},"
                + $"\"effectValues\":{effectValues},"
                + $"\"effectParams\":{effectParams},"
                + "\"autoEnd\":true,"
                + "\"branchWeight\":0,"
                + "\"branchPageText\":\"\""
                + "}";
            return new cfg.EventOption(JSON.Parse(json));
        }

        private static void AssertDishChoiceGroup(RewardChoiceGroup group, string title)
        {
            Assert.That(group, Is.Not.Null);
            Assert.That(group.Title, Is.EqualTo(title));
            Assert.That(group.SourceSlotId, Is.EqualTo("dish_choice_3"));
            Assert.That(group.Choices.Count, Is.EqualTo(3));
            Assert.That(group.RequiredChoiceCount, Is.EqualTo(1));
            Assert.That(group.Choices, Has.All.Matches<RewardChoice>(choice =>
                choice != null && choice.Kind == cfg.RewardKind.DishChoice));
        }
    }
}
#endif
