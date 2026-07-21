using System.IO;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ShopPriceTests
    {
        private cfg.Tables _tables;
        private GameRun _run;

        [SetUp]
        public void SetUp()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name => JSON.Parse(File.ReadAllText(Path.Combine(dir, name + ".json"))));
            GameplayDatabase db = GameplayContentBuilder.BuildDatabase(_tables);
            string characterId = _tables.TbCharacter.DataList[0].Id;
            _run = new GameRun(_tables, db, characterId, "shop-price-test-seed", weekIndex: 1);
        }

        [Test]
        public void TwentyPercentPassiveDiscount_RoundsDown()
        {
            _run.AcquireItem("item_discount_fragment", fallbackGold: 0);

            int price = new ItemRuntime(_run).ModifyShopPrice(ShopEntryKind.Fragment, 43);

            Assert.That(price, Is.EqualTo(34));
        }

        [Test]
        public void RefreshStockPrices_StoresSamePriceUsedByPurchase()
        {
            _run.AcquireItem("item_discount_active", fallbackGold: 0);
            var entry = new ShopEntry(ShopEntryKind.ActiveItem, "active_test", "测试主动道具", string.Empty, 43);

            ShopService.RefreshStockPrices(_run, new[] { entry });

            Assert.That(entry.Price, Is.EqualTo(36));
            Assert.That(entry.Price, Is.EqualTo(ShopService.CurrentPrice(_run, entry)));
        }
    }
}
