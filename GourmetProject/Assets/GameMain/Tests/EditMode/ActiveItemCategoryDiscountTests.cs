using System.Collections.Generic;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ActiveItemCategoryDiscountTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;
        private cfg.ActiveItem _strengthen;
        private cfg.ActiveItem _adjust;

        [SetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);
            _strengthen = _tables.TbActiveItem.DataList.First(
                item => item.Category == cfg.ActiveItemCategory.Strengthen);
            _adjust = _tables.TbActiveItem.DataList.First(
                item => item.Category == cfg.ActiveItemCategory.Adjust);
        }

        [Test]
        public void CategoryDiscounts_OnlyAffectTheirMatchingActiveItems()
        {
            GameRun run = CreateRun();
            Attach(run, "item_discount_active", 0.10f);
            Attach(run, "item_discount_adjust", 0.20f);

            var strengthenEntry = new ShopEntry(
                ShopEntryKind.ActiveItem,
                _strengthen.Id,
                _strengthen.Name,
                _strengthen.Desc,
                basePrice: 100);
            var adjustEntry = new ShopEntry(
                ShopEntryKind.ActiveItem,
                _adjust.Id,
                _adjust.Name,
                _adjust.Desc,
                basePrice: 100);

            Assert.That(ShopService.CurrentPrice(run, strengthenEntry), Is.EqualTo(90));
            Assert.That(ShopService.CurrentPrice(run, adjustEntry), Is.EqualTo(80));
        }

        [Test]
        public void SameCategoryDiscounts_StackMultiplicativelyBeforeFlooring()
        {
            GameRun run = CreateRun();
            Attach(run, "item_discount_active", 0.15f);
            Attach(run, "item_discount_active_festival", 0.25f);
            var runtime = new ItemRuntime(run);

            Assert.That(
                runtime.ModifyShopPrice(ShopEntryKind.ActiveItem, _strengthen.Id, 100),
                Is.EqualTo(63),
                "100 × 0.85 × 0.75 = 63.75，所有装饰品和消耗品处理后统一向下取整。");
            Assert.That(
                runtime.ModifyShopPrice(ShopEntryKind.ActiveItem, _adjust.Id, 100),
                Is.EqualTo(100));
        }

        [Test]
        public void LegacyKindOnlyOverload_RemainsBackwardCompatible()
        {
            GameRun run = CreateRun();
            Attach(run, "item_discount_active", 0.15f);

            Assert.That(
                new ItemRuntime(run).ModifyShopPrice(ShopEntryKind.ActiveItem, 100),
                Is.EqualTo(85));
        }

        [TestCase("item_discount_active", typeof(DiscountActiveModel))]
        [TestCase("item_discount_active_festival", typeof(DiscountActiveModel))]
        [TestCase("item_discount_adjust", typeof(DiscountAdjustModel))]
        [TestCase("item_discount_adjust_festival", typeof(DiscountAdjustModel))]
        public void DiscountIds_ResolveToCategoryModels(string itemId, System.Type expectedType)
        {
            Assert.That(PassiveItemModelRegistry.Create(itemId), Is.TypeOf(expectedType));
        }

        private GameRun CreateRun()
        {
            string characterId = _tables.TbCharacter.DataList.First().Id;
            return new GameRun(_tables, _database, characterId, "active-item-category-discount-tests");
        }

        private static void Attach(GameRun run, string itemId, float effectValue)
        {
            string value = effectValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string json = $@"{{
                ""id"":""{itemId}"",
                ""name"":""测试折扣"",
                ""desc"":"""",
                ""quality"":0,
                ""specialTags"":0,
                ""effectValue"":{value},
                ""effectParam"":"""",
                ""baseWeight"":1,
                ""hiddenRange"":{{""min"":0,""max"":0}},
                ""targetScoreHiddenOffset"":0,
                ""dishHiddenOffset"":0,
                ""passiveItemHiddenOffset"":0,
                ""fragmentHiddenOffset"":0,
                ""termId"":"""",
                ""price"":1
            }}";
            cfg.PassiveItem configured = cfg.PassiveItem.DeserializePassiveItem(JSON.Parse(json));
            var state = new RunItemState(itemId, 1);
            PassiveItemModel model = PassiveItemModelRegistry.Create(itemId);
            model.Bind(run, ItemDefinition.From(configured), state);
            state.Model = model;
            ((List<RunItemState>)run.Items).Add(state);
        }
    }
}
