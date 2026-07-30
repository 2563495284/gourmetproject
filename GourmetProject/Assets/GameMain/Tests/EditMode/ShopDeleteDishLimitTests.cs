using System.Linq;
using GourmetProject.Config;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ShopDeleteDishLimitTests
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
        public void DeleteDishAt_StopsAtConfiguredPerShopLimitAndResetsForNextVisit()
        {
            GameRun run = CreateRun();
            run.Gold = 1_000_000;
            run.BeginShopVisit();

            int limit = ShopService.DeleteDishLimit(run);
            Assume.That(limit, Is.GreaterThan(0));
            EnsureRecipeCount(run, limit + 1);

            for (int i = 0; i < limit; i++)
            {
                Assert.That(ShopService.DeleteDishAt(run, 0), Is.True);
            }

            int recipeCountAtLimit = run.RecipeEntries.Count;
            int goldAtLimit = run.Gold;
            Assert.That(run.CurrentShopDeleteDishCount, Is.EqualTo(limit));
            Assert.That(ShopService.DeleteDishRemaining(run), Is.Zero);
            Assert.That(ShopService.CanDeleteDish(run), Is.False);
            Assert.That(ShopService.DeleteDishAt(run, 0), Is.False);
            Assert.That(run.RecipeEntries.Count, Is.EqualTo(recipeCountAtLimit));
            Assert.That(run.Gold, Is.EqualTo(goldAtLimit));

            run.BeginShopVisit();

            Assert.That(run.CurrentShopDeleteDishCount, Is.Zero);
            Assert.That(ShopService.DeleteDishRemaining(run), Is.EqualTo(limit));
            Assert.That(ShopService.DeleteDishAt(run, 0), Is.True);
            Assert.That(run.DeleteDishCount, Is.EqualTo(limit + 1));
        }

        [Test]
        public void CurrentShopDeleteCount_RoundTripsWithoutResettingRecoveredShop()
        {
            GameRun run = CreateRun();
            run.Gold = 1_000_000;
            run.BeginShopVisit();

            int limit = ShopService.DeleteDishLimit(run);
            Assume.That(limit, Is.GreaterThan(0));
            EnsureRecipeCount(run, limit + 1);
            Assert.That(ShopService.DeleteDishAt(run, 0), Is.True);

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());

            Assert.That(restored.CurrentShopDeleteDishCount, Is.EqualTo(1));
            Assert.That(
                ShopService.DeleteDishRemaining(restored),
                Is.EqualTo(System.Math.Max(0, limit - 1)));
        }

        [Test]
        public void FailedDelete_DoesNotConsumeCurrentShopLimit()
        {
            GameRun run = CreateRun();
            run.Gold = 0;
            run.BeginShopVisit();

            Assert.That(ShopService.DeleteDishAt(run, 0), Is.False);
            Assert.That(run.CurrentShopDeleteDishCount, Is.Zero);
            Assert.That(run.DeleteDishCount, Is.Zero);
        }

        private GameRun CreateRun()
        {
            string characterId = _tables.TbCharacter.DataList.First().Id;
            return new GameRun(_tables, _database, characterId, "shop-delete-limit-tests");
        }

        private void EnsureRecipeCount(GameRun run, int count)
        {
            string dishId = _database.AllDishes.First().Id;
            while (run.RecipeEntries.Count < count)
            {
                Assert.That(run.AddBonusDish(dishId), Is.True);
            }
        }
    }
}
