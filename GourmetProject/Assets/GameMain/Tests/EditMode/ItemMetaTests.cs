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
            ItemDefinition item = ItemDefinition.Get(run.Tables, "item_discount_food", cfg.ItemKind.Passive);

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

        // —— OnAcquire（获得时结算一次，由道具模型 OnAcquired 驱动）及计数类被动 ——

        [Test]
        public void OnAcquire_GoldNow_GrantsGoldInRange()
        {
            GameRun run = NewRun();
            int before = run.Gold;
            run.AcquireItem("item_gold_random", 0); // GoldNow, range:1,100
            int gained = run.Gold - before;
            // 无随机流时取区间中值 50；有随机流时落在 [1,100]。两种情况都应在区间内。
            Assert.GreaterOrEqual(gained, 1);
            Assert.LessOrEqual(gained, 100);
        }

        [Test]
        public void OnAcquire_Loan_GrantsGoldAndRegistersDebt()
        {
            GameRun run = NewRun();
            int before = run.Gold;
            run.AcquireItem("item_loan", 0); // +300 立即，登记 600 债务
            Assert.AreEqual(before + 300, run.Gold);
            Assert.AreEqual(600, run.LoanDebt);
        }

        [Test]
        public void OnAcquire_FiresOnce_AndNotAgainOnSaveLoad()
        {
            GameRun run = NewRun();
            run.AcquireItem("item_loan", 0);
            int goldAfterAcquire = run.Gold;
            Assert.AreEqual(600, run.LoanDebt);

            RunSaveData save = run.ToSaveData();
            GameRun restored = GameRun.FromSaveData(run.Tables, run.Database, save);

            Assert.AreEqual(goldAfterAcquire, restored.Gold, "读档不应重复触发高利贷发钱");
            Assert.AreEqual(600, restored.LoanDebt, "债务随档保存且不翻倍");
            Assert.AreEqual(1, restored.GetItemCount("item_loan"));
        }

        [Test]
        public void OnAcquire_GoldMealBonus_InitializesCounter()
        {
            GameRun run = NewRun();
            run.AcquireItem("item_gold_meal_bonus", 0); // meals:10, +15/局
            Assert.AreEqual(10, run.MealBonusRemaining);
            Assert.AreEqual(15, new ItemRuntime(run).MealBonusGoldPerMeal());

            run.ConsumeMealBonusMeal();
            Assert.AreEqual(9, run.MealBonusRemaining);
        }

        [Test]
        public void OnAcquire_RequiredScoreToOne_ForcesOneWhileCounting()
        {
            GameRun reference = NewRun();
            int normalRequired = reference.ComputeFoodRequiredScore(1f);

            GameRun run = NewRun();
            run.AcquireItem("item_score_to_one", 0); // meals:3
            Assert.AreEqual(3, run.ScoreToOneRemaining);
            Assert.AreEqual(1, run.ComputeFoodRequiredScore(1f), "生效期内非盛宴要求分固定为 1");

            run.ConsumeScoreToOneMeal();
            run.ConsumeScoreToOneMeal();
            run.ConsumeScoreToOneMeal();
            Assert.AreEqual(0, run.ScoreToOneRemaining);
            Assert.AreEqual(normalRequired, run.ComputeFoodRequiredScore(1f), "计数用尽后恢复正常要求分");
        }

        [Test]
        public void OnAcquire_DiscardNegativeForGold_RemovesAllNegativesAndPays()
        {
            GameRun run = NewRun();
            run.AcquireItem("item_shop_price_up", 0); // Negative
            run.AcquireItem("item_no_remove", 0);     // Negative
            int negBefore = CountNegatives(run);
            Assert.GreaterOrEqual(negBefore, 2);

            int goldBefore = run.Gold;
            run.AcquireItem("item_discard_negative_gold", 0); // 移除全部负面，每个 +100
            Assert.AreEqual(0, CountNegatives(run), "负面道具应被清空");
            Assert.AreEqual(goldBefore + 100 * negBefore, run.Gold);
        }

        [Test]
        public void OnAcquire_DiscardNegative_RemovesUpToCount()
        {
            GameRun run = NewRun();
            run.AcquireItem("item_shop_price_up", 0);
            run.AcquireItem("item_no_remove", 0);
            run.AcquireItem("item_skip_node", 0);
            int negBefore = CountNegatives(run);

            run.AcquireItem("item_discard_negative", 0); // effectValue 2 → 最多丢 2 个
            Assert.AreEqual(System.Math.Max(0, negBefore - 2), CountNegatives(run));
        }

        private static int CountNegatives(GameRun run)
        {
            int count = 0;
            foreach (RunItemState state in run.Items)
            {
                ItemDefinition def = ItemDefinition.Get(run.Tables, state.ItemId, cfg.ItemKind.Passive);
                if (def != null && !string.IsNullOrEmpty(def.SpecialTags) && def.SpecialTags.Contains("Negative"))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
