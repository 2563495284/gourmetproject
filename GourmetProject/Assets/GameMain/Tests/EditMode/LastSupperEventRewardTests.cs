using System;
using System.IO;
using System.Linq;
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
    public sealed class LastSupperEventRewardTests
    {
        private const string RewardOptionId = "opt_last_supper_share_reward";
        private const string FirstRewardParam = "dish_choice_3|第一道回礼";
        private const string SecondRewardParam = "dish_choice_3|第二道回礼";

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
        public void ShareReward_LoadsAsTwoIndependentThreeChoiceDishOffers()
        {
            cfg.EventOption option = _tables.TbEventOption.Get(RewardOptionId);

            Assert.That(option.Text, Is.EqualTo("获得两个食物3选1"));
            Assert.That(
                option.ResultText,
                Is.EqualTo("十二位厨师依次端上拿手菜。你从两组三道回礼中各选一道，加入食谱。"));
            Assert.That(
                option.EffectTypes,
                Is.EqualTo(new[]
                {
                    cfg.EffectType.EnqueueDishChoice,
                    cfg.EffectType.EnqueueDishChoice,
                }));
            Assert.That(option.EffectValues, Is.EqualTo(new[] { 3f, 3f }));
            Assert.That(option.EffectParams, Is.EqualTo(new[] { FirstRewardParam, SecondRewardParam }));
            Assert.That(option.ParentId, Is.EqualTo("opt_last_supper_share"));
            Assert.That(option.AutoEnd, Is.True);
        }

        [Test]
        public void ShareReward_QueuesPersistsAndClaimsBothThreeChoiceOffers()
        {
            cfg.EventOption option = _tables.TbEventOption.Get(RewardOptionId);
            GameRun run = CreateRun();

            EventResolveResult result = EventService.ResolveOption(
                run,
                option,
                new Xoshiro256SS(20260817UL));

            StringAssert.Contains("第一道回礼", result.Feedback);
            StringAssert.Contains("第二道回礼", result.Feedback);

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            int recipeCountBeforeClaim = restored.RecipeEntries.Count;

            Assert.That(
                restored.TryPeekPendingGenericReward(out string firstKey, out string firstTitle, out RewardOffer firstOffer),
                Is.True);
            Assert.That(firstTitle, Is.EqualTo("第一道回礼"));
            AssertThreeChoiceDishOffer(firstOffer);
            ClaimFirstChoice(restored, firstOffer);
            restored.ClearPendingGenericRewardOffer(firstKey);

            Assert.That(
                restored.TryPeekPendingGenericReward(out string secondKey, out string secondTitle, out RewardOffer secondOffer),
                Is.True);
            Assert.That(secondTitle, Is.EqualTo("第二道回礼"));
            Assert.That(secondKey, Is.Not.EqualTo(firstKey));
            AssertThreeChoiceDishOffer(secondOffer);
            ClaimFirstChoice(restored, secondOffer);
            restored.ClearPendingGenericRewardOffer(secondKey);

            Assert.That(restored.RecipeEntries, Has.Count.EqualTo(recipeCountBeforeClaim + 2));
            Assert.That(restored.HasPendingGenericRewards, Is.False);
        }

        private static void AssertThreeChoiceDishOffer(RewardOffer offer)
        {
            Assert.That(offer, Is.Not.Null);
            Assert.That(offer.FixedGroups, Has.Count.EqualTo(1));

            RewardChoiceGroup group = offer.FixedGroups.Single();
            Assert.That(group.SourceSlotId, Is.EqualTo("dish_choice_3"));
            Assert.That(group.Choices, Has.Count.EqualTo(3));
            Assert.That(group.RequiredChoiceCount, Is.EqualTo(1));
            Assert.That(group.Choices.All(choice => choice.Kind == cfg.RewardKind.DishChoice), Is.True);
        }

        private static void ClaimFirstChoice(GameRun run, RewardOffer offer)
        {
            RewardChoiceGroup group = offer.FixedGroups.Single();
            Assert.That(RewardGranter.TryClaimChoice(run, group.Choices[0], out _), Is.True);
            group.MarkClaimed(0);
            Assert.That(group.IsResolved, Is.True);
        }

        private GameRun CreateRun()
        {
            return new GameRun(
                _tables,
                _database,
                "glutton_dog",
                "last-supper-event-test",
                weekIndex: 1);
        }
    }
}
