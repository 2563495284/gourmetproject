using System.IO;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;

namespace GourmetProject.Tests
{
    /// <summary>
    /// 道具局外效果测试（Game 层）：加载真实 StreamingAssets 配置，构造 GameRun 后验证
    /// ItemRuntime 对商店价格、目标分、金币等 hook 的计算。
    /// </summary>
    public class ItemMetaTests
    {
        private static cfg.Tables LoadTables()
        {
            string root = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "StreamingAssets", "Config");
            return new cfg.Tables(name => JSON.Parse(File.ReadAllText(Path.Combine(root, name + ".json"))));
        }

        private static GameRun NewRun()
        {
            cfg.Tables tables = LoadTables();
            GameplayDatabase database = GameplayContentBuilder.BuildDatabase(tables);
            return new GameRun(tables, database, "glutton_dog", "item-meta-test", 1);
        }

        [Test]
        public void PassiveItem_CannotUpgradeOrReenterPool()
        {
            GameRun run = NewRun();
            cfg.Item item = run.Tables.TbItem.Get("item_discount_food");

            ItemAcquireResult first = run.AcquireItem(item.Id, 50);
            Assert.AreEqual(ItemAcquireOutcome.Added, first.Outcome);
            Assert.AreEqual(1, run.GetItemCount(item.Id));
            Assert.IsFalse(ItemPoolService.CanEnterPool(run, item));

            ItemAcquireResult second = run.AcquireItem(item.Id, 50);
            Assert.AreEqual(ItemAcquireOutcome.ConvertedToGold, second.Outcome);
            Assert.AreEqual(1, run.GetItemCount(item.Id));
            Assert.AreEqual(50, second.Gold);
        }

        [Test]
        public void ShopDiscount_FoodPrice_ReducedBy20Percent()
        {
            GameRun run = NewRun();
            run.AcquireItem("item_discount_food", 0);
            var rt = new ItemRuntime(run);
            Assert.AreEqual(80, rt.ModifyShopPrice(ShopEntryKind.Dish, 100));
            // 未匹配类别不受影响。
            Assert.AreEqual(100, rt.ModifyShopPrice(ShopEntryKind.PassiveItem, 100));
        }

        [Test]
        public void RemovePriceFixed_OverridesDeleteCost()
        {
            GameRun run = NewRun();
            run.AcquireItem("item_remove_fixed_30", 0);
            Assert.AreEqual(30, new ItemRuntime(run).ModifyDeletePrice(15));
        }

        [Test]
        public void NoRemoveDish_BlocksDeletion()
        {
            GameRun run = NewRun();
            run.AcquireItem("item_no_remove", 0);
            Assert.IsTrue(new ItemRuntime(run).BlockRemoveDish());
            Assert.IsFalse(ShopService.DeleteDish(run, "any_dish"));
        }

        [Test]
        public void ShopPriceUp_IncreasesPrice()
        {
            GameRun run = NewRun();
            run.AcquireItem("item_shop_price_up", 0);
            // +25% → 100 → 125。
            Assert.AreEqual(125, new ItemRuntime(run).ModifyShopPrice(ShopEntryKind.Dish, 100));
        }

        [Test]
        public void RequiredScore_NormalPctReducesOnlyNormalTier()
        {
            GameRun run = NewRun();
            run.AcquireItem("item_req_normal_down", 0);
            var rt = new ItemRuntime(run);
            Assert.AreEqual(900, rt.ModifyRequiredScore(1000, MealTier.Normal));
            // 盛宴档不受普通档道具影响。
            Assert.AreEqual(1000, rt.ModifyRequiredScore(1000, MealTier.Feast));
        }

        [Test]
        public void RequiredScore_FeastPctIncreaseIsNegativeItem()
        {
            GameRun run = NewRun();
            run.AcquireItem("item_req_feast_up", 0);
            Assert.AreEqual(1100, new ItemRuntime(run).ModifyRequiredScore(1000, MealTier.Feast));
        }

        [Test]
        public void Undying_ConsumesOnceThenGone()
        {
            GameRun run = NewRun();
            run.AcquireItem("item_famous_knife", 0);
            Assert.IsTrue(new ItemRuntime(run).HasUndying());
            Assert.IsTrue(run.TryConsumeUndying());
            Assert.IsFalse(new ItemRuntime(run).HasUndying());
            Assert.IsFalse(run.TryConsumeUndying());
        }

        [Test]
        public void CakeHooks_ReadValues()
        {
            GameRun run = NewRun();
            run.AcquireItem("item_cake_init_bonus", 0);
            run.AcquireItem("item_cake_req_minus", 0);
            var rt = new ItemRuntime(run);
            Assert.AreEqual(10, rt.CakeInitialLayers());
            Assert.AreEqual(10, rt.CakeThresholdReduction());
        }

        [Test]
        public void MealRewardGold_PercentAndPenalty()
        {
            GameRun run = NewRun();
            run.AcquireItem("item_gold_percent", 0);
            Assert.AreEqual(120, new ItemRuntime(run).ModifyMealRewardGold(100));

            GameRun run2 = NewRun();
            run2.AcquireItem("item_gold_meal_penalty", 0);
            Assert.AreEqual(75, new ItemRuntime(run2).ModifyMealRewardGold(100));
        }

        [Test]
        public void ChoiceHooks_CountAndTimes()
        {
            GameRun run = NewRun();
            run.AcquireItem("item_choice_count_plus1", 0); // +1
            run.AcquireItem("item_choice_minus1", 0);      // -1（负面）
            run.AcquireItem("item_choice_times_plus1", 0); // 次数 +1
            var rt = new ItemRuntime(run);
            Assert.AreEqual(0, rt.ChoiceCountDelta()); // +1 与 -1 抵消
            Assert.AreEqual(1, rt.ChoiceTimesBonus());
        }

        [Test]
        public void EventHooks_ChanceAndGuarantee()
        {
            GameRun run = NewRun();
            run.AcquireItem("item_lucky_chance", 0);
            run.AcquireItem("item_lucky_guarantee", 0);
            run.AcquireItem("item_gold_on_event", 0);
            var rt = new ItemRuntime(run);
            Assert.AreEqual(0.2f, rt.LuckyEventChanceBonus(), 0.001f);
            Assert.AreEqual(4, rt.LuckyEventGuaranteeEvery());
            Assert.AreEqual(10, rt.EventCompleteGold());
        }

        [Test]
        public void ActiveSlotHooks()
        {
            GameRun run = NewRun();
            run.AcquireItem("item_extra_active_slots", 0);
            var rt = new ItemRuntime(run);
            Assert.AreEqual(2, rt.ExtraActiveSlots());

            GameRun run2 = NewRun();
            run2.AcquireItem("item_block_active", 0);
            Assert.IsTrue(new ItemRuntime(run2).BlocksActiveItems());
        }

        [Test]
        public void StarGazeHooks_ParseParams()
        {
            GameRun run = NewRun();
            run.AcquireItem("item_stargaze_every5", 0);
            run.AcquireItem("item_stargaze_first3", 0);
            var rt = new ItemRuntime(run);
            Assert.AreEqual(5, rt.StarGazeEvery());
            Assert.AreEqual(3, rt.StarGazeFirst());
        }
    }
}
