using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class PendingRewardBattleLifecycleTests
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
        }

        [Test]
        public void PendingRewardBattleView_DefaultsAreBackwardCompatible()
        {
            var snapshot = new PendingRewardBattleViewSaveData();

            Assert.That(snapshot.FinalHappyCakeLayers, Is.EqualTo(-1));
            Assert.That(snapshot.HasDetailedScore, Is.False);
            Assert.That(snapshot.FinalMultiplier, Is.EqualTo(1f));
            Assert.That(snapshot.Dishes, Is.Empty);
            Assert.That(snapshot.Cakes, Is.Empty);
        }

        [Test]
        public void PendingRewardBattleView_RoundTripsDetailedScoreAndCakeVisuals()
        {
            GameRun run = CreateRun();
            string dishId = _database.AllDishes.First().Id;
            run.SetPendingRewardBattleView(new PendingRewardBattleViewSaveData
            {
                RequiredScore = 120,
                RawRequiredScore = 100,
                Modifier = "test",
                BossDebuffId = "debuff_carb_meal",
                BattleKey = "battle",
                LastTotal = 88,
                FinalHappyCakeLayers = 7,
                HasDetailedScore = true,
                RawSum = 80f,
                FinalFlat = 8f,
                FinalMultiplier = 1f,
                Dishes = new List<PendingRewardBattleDishSaveData>
                {
                    new PendingRewardBattleDishSaveData
                    {
                        Id = 42,
                        DishId = dishId,
                        HasDishScore = true,
                        ScoreBaseValue = 31f,
                        ScoreFlatBonus = 5f,
                        ScoreMultiplier = 1.5f,
                    },
                },
                Cakes = new List<PendingRewardCakeVisualSaveData>
                {
                    new PendingRewardCakeVisualSaveData
                    {
                        ViewportX = 0.25f,
                        ViewportY = 0.75f,
                        RotationZ = 12f,
                        ScaleX = 0.9f,
                        ScaleY = 1.1f,
                        ScaleZ = 1f,
                    },
                },
            });

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            PendingRewardBattleViewSaveData actual = restored.GetPendingRewardBattleView();

            Assert.That(actual, Is.Not.Null);
            Assert.That(actual.BossDebuffId, Is.EqualTo("debuff_carb_meal"));
            Assert.That(actual.FinalHappyCakeLayers, Is.EqualTo(7));
            Assert.That(actual.HasDetailedScore, Is.True);
            Assert.That(actual.RawSum, Is.EqualTo(80f));
            Assert.That(actual.Dishes, Has.Count.EqualTo(1));
            Assert.That(actual.Dishes[0].HasDishScore, Is.True);
            Assert.That(actual.Dishes[0].ScoreBaseValue, Is.EqualTo(31f));
            Assert.That(actual.Dishes[0].ScoreFlatBonus, Is.EqualTo(5f));
            Assert.That(actual.Dishes[0].ScoreMultiplier, Is.EqualTo(1.5f));
            Assert.That(actual.Cakes, Has.Count.EqualTo(1));
            Assert.That(actual.Cakes[0].ViewportX, Is.EqualTo(0.25f));
            Assert.That(actual.Cakes[0].ViewportY, Is.EqualTo(0.75f));
            Assert.That(actual.Cakes[0].RotationZ, Is.EqualTo(12f));
        }

        [Test]
        public void ClearingOffer_DoesNotClearBattleViewUntilLifecycleEnds()
        {
            GameRun run = CreateRun();
            run.SetPendingRewardOffer(
                "reward",
                new RewardOffer(
                    0,
                    Array.Empty<RewardChoice>(),
                    Array.Empty<RewardChoice>(),
                    baseGoldClaimed: true));
            run.SetPendingRewardBattleView(new PendingRewardBattleViewSaveData
            {
                RequiredScore = 10,
                LastTotal = 20,
            });

            run.ClearPendingRewardOffer();

            Assert.That(run.HasPendingRewardOffer, Is.False);
            Assert.That(run.HasPendingRewardBattleView, Is.True);

            run.ClearPendingBattleReward();

            Assert.That(run.HasPendingRewardOffer, Is.False);
            Assert.That(run.HasPendingRewardBattleView, Is.False);
        }

        [Test]
        public void RewardChoiceGroup_FiveChooseTwoTracksDistinctSourceIndices()
        {
            var choices = new List<RewardChoice>
            {
                RewardChoice.Gold(1),
                RewardChoice.Gold(2),
                RewardChoice.Gold(3),
                RewardChoice.Gold(4),
                RewardChoice.Gold(5),
            };
            var group = new RewardChoiceGroup("五选二", choices, requiredChoiceCount: 2);

            group.MarkClaimed(3);
            group.MarkClaimed(3);

            Assert.That(group.ClaimedIndices, Is.EqualTo(new[] { 3 }));
            Assert.That(group.IsResolved, Is.False);

            group.MarkClaimed(1);

            Assert.That(group.ClaimedIndices, Is.EqualTo(new[] { 3, 1 }));
            Assert.That(group.IsResolved, Is.True);
        }

        private GameRun CreateRun()
        {
            string characterId = _tables.TbCharacter.DataList.First().Id;
            return new GameRun(_tables, _database, characterId, "pending-reward-battle-tests");
        }
    }
}
