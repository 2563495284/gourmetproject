using System.Linq;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BossMechanicTests
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
        public void CarbMeal_InsertsExactlyOneConfiguredMantouPerFiveBellOutputs()
        {
            var entries = Enumerable.Range(0, 10)
                .Select(_ => new RecipeSlotEntry("mantou"))
                .ToList();
            var slot = new RecipeSlot("test", entries);
            var session = new BattleSession(
                new DiningTable(10, 1),
                _database,
                new Xoshiro256SS(20260730UL),
                new[] { slot },
                requiredScore: 1);
            session.ConfigureInsertedDishSequence("mantou", windowSize: 5, countPerWindow: 1);

            int[] insertedPerWindow = { 0, 0 };
            for (int outputIndex = 0; outputIndex < 10; outputIndex++)
            {
                int countBefore = slot.Count;
                ServePrepareResult prepared = session.PrepareServeFromBell(0);

                Assert.That(prepared.Success, Is.True, $"output {outputIndex + 1}");
                Assert.That(prepared.PreparedDish.Definition, Is.SameAs(_database.GetDish("mantou")));
                if (slot.Count == countBefore)
                {
                    insertedPerWindow[outputIndex / 5]++;
                }

                Assert.That(
                    session.CommitPreparedServe(prepared.PreparedDish.Placements[0]).Success,
                    Is.True,
                    $"output {outputIndex + 1}");
            }

            Assert.That(insertedPerWindow, Is.EqualTo(new[] { 1, 1 }));
            Assert.That(slot.Count, Is.EqualTo(2), "两个馒头是额外插入，不能消耗原菜谱条目。");
        }

        [Test]
        public void BossMechanicNumbers_AreLoadedFromBossTable()
        {
            cfg.BossDebuff carb = _tables.TbBossDebuff.Get("debuff_carb_meal");
            Assert.That(carb.InsertDishId, Is.EqualTo("mantou"));
            Assert.That(carb.InsertDishWindowSize, Is.EqualTo(5));
            Assert.That(carb.InsertDishCountPerWindow, Is.EqualTo(1));

            cfg.BossDebuff dark = _tables.TbBossDebuff.Get("debuff_dark_cuisine");
            Assert.That(dark.RandomServeMultiplierMin, Is.EqualTo(0.5f));
            Assert.That(dark.RandomServeMultiplierMax, Is.EqualTo(1.5f));
            Assert.That(dark.RandomServeMultiplierStep, Is.EqualTo(0.1f));
        }

        [Test]
        public void FeastRequiredScoreItems_AreAdditiveAndUseAwayFromZeroRounding()
        {
            GameRun run = CreateRun();
            run.AcquireItem("item_req_feast_down", fallbackGold: 0);
            Assert.That(
                new ItemRuntime(run).ModifyRequiredScore(101, cfg.FoodActionKind.Feast),
                Is.EqualTo(86));

            run.AcquireItem("item_req_feast_up", fallbackGold: 0);
            Assert.That(
                new ItemRuntime(run).ModifyRequiredScore(101, cfg.FoodActionKind.Feast),
                Is.EqualTo(101));
        }

        [Test]
        public void BossBounty_IsClaimedOnceAfterAcquisitionAndPersists()
        {
            GameRun run = CreateRun();
            run.AcquireItem("item_gold_boss", fallbackGold: 0);

            Assert.That(new ItemRuntime(run).ClaimBossCompleteGold(), Is.EqualTo(100));
            Assert.That(new ItemRuntime(run).ClaimBossCompleteGold(), Is.Zero);

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            Assert.That(new ItemRuntime(restored).ClaimBossCompleteGold(), Is.Zero);
        }

        private GameRun CreateRun()
        {
            return new GameRun(
                _tables,
                _database,
                _tables.TbCharacter.DataList[0].Id,
                "boss-mechanic-tests");
        }
    }
}
