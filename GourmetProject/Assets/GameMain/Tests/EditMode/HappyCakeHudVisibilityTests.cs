using System.Collections.Generic;
using GourmetProject.Config;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Gameplay.Data;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class HappyCakeHudVisibilityTests
    {
        private const string CakeDishId = "cake_slice";
        private const string NonCakeDishId = "eclair";

        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);

            Assert.That(_database.GetDish(CakeDishId)?.IsCategory("cake"), Is.True);
            Assert.That(_database.GetDish(NonCakeDishId)?.IsCategory("cake"), Is.False);
        }

        [Test]
        public void RecipeContainsCake_ShowsOnAnyPage()
        {
            GameRun run = CreateEmptyRecipeRun();
            Assert.That(run.AddBonusDish(CakeDishId), Is.True);

            bool visible = HappyCakeHudVisibility.ShouldShow(
                run,
                GameplayView.ActionSelect,
                EmptyChoices(),
                EmptyStock());

            Assert.That(visible, Is.True);
        }

        [Test]
        public void RewardChoicesContainCake_ShowsOnlyOnDishChoicePage()
        {
            GameRun run = CreateEmptyRecipeRun();
            IReadOnlyList<RewardChoice> choices = new[]
            {
                DishChoice(NonCakeDishId),
                DishChoice(CakeDishId),
                DishChoice(NonCakeDishId),
            };

            Assert.That(
                HappyCakeHudVisibility.ShouldShow(
                    run,
                    GameplayView.RewardDishPack,
                    choices,
                    EmptyStock()),
                Is.True);
            Assert.That(
                HappyCakeHudVisibility.ShouldShow(
                    run,
                    GameplayView.ActionSelect,
                    choices,
                    EmptyStock()),
                Is.False,
                "离开三选一页面后，残留候选不应继续触发显示");
        }

        [Test]
        public void ShopContainsStockedCake_ShowsOnlyOnShopPage()
        {
            GameRun run = CreateEmptyRecipeRun();
            var purchasedCake = DishStock(CakeDishId);
            purchasedCake.ClearStock();
            IReadOnlyList<ShopEntry> stock = new[]
            {
                DishStock(NonCakeDishId),
                purchasedCake,
                DishStock(CakeDishId),
                new ShopEntry(
                    ShopEntryKind.PassiveItem,
                    CakeDishId,
                    CakeDishId,
                    string.Empty,
                    1),
            };

            Assert.That(
                HappyCakeHudVisibility.ShouldShow(
                    run,
                    GameplayView.Shop,
                    EmptyChoices(),
                    stock),
                Is.True);
            Assert.That(
                HappyCakeHudVisibility.ShouldShow(
                    run,
                    GameplayView.ActionSelect,
                    EmptyChoices(),
                    stock),
                Is.False,
                "离开商店后，残留库存不应继续触发显示");
        }

        [Test]
        public void NoCakeInRecipeOrCurrentPage_Hides()
        {
            GameRun run = CreateEmptyRecipeRun();
            Assert.That(run.AddBonusDish(NonCakeDishId), Is.True);
            var purchasedCake = DishStock(CakeDishId);
            purchasedCake.ClearStock();

            bool visible = HappyCakeHudVisibility.ShouldShow(
                run,
                GameplayView.Shop,
                new[]
                {
                    DishChoice(NonCakeDishId),
                    new RewardChoice(
                        cfg.RewardKind.PassiveItemChoice,
                        CakeDishId,
                        CakeDishId,
                        string.Empty),
                },
                new[]
                {
                    DishStock(NonCakeDishId),
                    purchasedCake,
                    new ShopEntry(
                        ShopEntryKind.PassiveItem,
                        CakeDishId,
                        CakeDishId,
                        string.Empty,
                        1),
                });

            Assert.That(visible, Is.False);
        }

        private GameRun CreateEmptyRecipeRun()
        {
            string characterId = _tables.TbCharacter.DataList[0].Id;
            var run = new GameRun(
                _tables,
                _database,
                characterId,
                "happy-cake-hud-visibility-tests");
            while (run.RecipeEntries.Count > 0)
            {
                Assert.That(run.RemoveBonusDishAt(run.RecipeEntries.Count - 1), Is.True);
            }

            return run;
        }

        private static RewardChoice DishChoice(string id)
        {
            return new RewardChoice(
                cfg.RewardKind.DishChoice,
                id,
                id,
                string.Empty);
        }

        private static ShopEntry DishStock(string id)
        {
            return new ShopEntry(
                ShopEntryKind.Dish,
                id,
                id,
                string.Empty,
                1);
        }

        private static IReadOnlyList<RewardChoice> EmptyChoices()
        {
            return System.Array.Empty<RewardChoice>();
        }

        private static IReadOnlyList<ShopEntry> EmptyStock()
        {
            return System.Array.Empty<ShopEntry>();
        }
    }
}
