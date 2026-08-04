using System.Linq;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using GourmetProject.Runtime;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ShopFragmentPackPurchaseLimitTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;
        private RandomService _previousRandom;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);

            _previousRandom = GameApp.Random;
            var random = new RandomService();
            random.Init(0xF12A6UL);
            SetGameRandom(random);
        }

        [OneTimeTearDown]
        public void RestoreRandom()
        {
            SetGameRandom(_previousRandom);
        }

        [Test]
        public void FragmentPackPurchase_StopsAtConfiguredLimitAndResetsForNextVisit()
        {
            GameRun run = CreateRun();
            run.Gold = 1_000_000;
            run.BeginShopVisit();
            ShopEntry entry = CreateFragmentEntry(run);

            int limit = ShopService.FragmentPackPurchaseLimit(run);
            Assert.That(limit, Is.EqualTo(1));
            Assert.That(ShopService.Purchase(run, entry), Is.True);
            Assert.That(run.CurrentShopFragmentPackPurchaseCount, Is.EqualTo(1));
            Assert.That(ShopService.FragmentPackPurchaseRemaining(run), Is.Zero);

            run.ClearPendingFragmentPack();
            int goldAtLimit = run.Gold;
            int totalPurchasesAtLimit = run.FragmentPackPurchaseCount;
            Assert.That(ShopService.Purchase(run, entry), Is.False);
            Assert.That(run.Gold, Is.EqualTo(goldAtLimit));
            Assert.That(run.FragmentPackPurchaseCount, Is.EqualTo(totalPurchasesAtLimit));
            Assert.That(run.PendingFragmentPack, Is.Empty);

            string shopKey = GameRun.BuildShopKey(run.WeekIndex, run.CurrentDay);
            run.SetPendingShopStock(
                shopKey,
                new[] { ShopEntry.CreateEmpty(ShopEntryKind.Fragment, 0) });
            run.BeginShopVisit();

            Assert.That(run.CurrentShopFragmentPackPurchaseCount, Is.Zero);
            Assert.That(run.HasPendingShopStock(shopKey), Is.False);
            Assert.That(ShopService.FragmentPackPurchaseRemaining(run), Is.EqualTo(limit));
            Assert.That(ShopService.Purchase(run, entry), Is.True);
            Assert.That(run.FragmentPackPurchaseCount, Is.EqualTo(totalPurchasesAtLimit + 1));
        }

        [Test]
        public void CurrentShopFragmentPackPurchaseCount_RoundTripsWithoutResettingRecoveredShop()
        {
            GameRun run = CreateRun();
            run.Gold = 1_000_000;
            run.BeginShopVisit();
            ShopEntry entry = CreateFragmentEntry(run);
            Assert.That(ShopService.Purchase(run, entry), Is.True);
            run.ClearPendingFragmentPack();

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());

            Assert.That(restored.CurrentShopFragmentPackPurchaseCount, Is.EqualTo(1));
            Assert.That(ShopService.FragmentPackPurchaseRemaining(restored), Is.Zero);
            Assert.That(ShopService.CanPurchaseFragmentPack(restored), Is.False);
        }

        [Test]
        public void FailedFragmentPackPurchase_DoesNotConsumeCurrentShopLimit()
        {
            GameRun run = CreateRun();
            run.Gold = 0;
            run.BeginShopVisit();
            ShopEntry entry = CreateFragmentEntry(run);

            Assert.That(ShopService.Purchase(run, entry), Is.False);
            Assert.That(run.CurrentShopFragmentPackPurchaseCount, Is.Zero);
            Assert.That(run.FragmentPackPurchaseCount, Is.Zero);
            Assert.That(run.PendingFragmentPack, Is.Empty);

            run.Gold = 1_000_000;
            Assert.That(ShopService.Purchase(run, entry), Is.True);
            Assert.That(run.CurrentShopFragmentPackPurchaseCount, Is.EqualTo(1));
        }

        private GameRun CreateRun()
        {
            string characterId = _tables.TbCharacter.DataList.First().Id;
            return new GameRun(_tables, _database, characterId, "shop-fragment-limit-tests");
        }

        private ShopEntry CreateFragmentEntry(GameRun run)
        {
            ShopEntry entry = ShopService.RollStock(
                    _tables,
                    run,
                    new Xoshiro256SS(0xF12A601UL),
                    new Xoshiro256SS(0xF12A602UL))
                .SingleOrDefault(candidate => candidate.Kind == ShopEntryKind.Fragment);
            Assert.That(entry, Is.Not.Null, "测试配置必须至少能生成一个可拼入当前餐桌的碎片包。");
            return entry;
        }

        private static void SetGameRandom(RandomService random)
        {
            typeof(GameApp)
                .GetProperty(nameof(GameApp.Random))
                ?.GetSetMethod(nonPublic: true)
                ?.Invoke(null, new object[] { random });
        }
    }
}
