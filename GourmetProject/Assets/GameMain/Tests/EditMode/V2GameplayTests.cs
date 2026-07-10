using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Orchestration;
using GourmetProject.Game.Run;

namespace GourmetProject.Tests
{
    public class V2GameplayTests
    {
        [Test]
        public void GameRun_UsesGameBaseInitialGold()
        {
            GameRun run = NewRun(week: 1, characterId: "glutton_dog");

            Assert.AreEqual(9999, run.Tables.TbGameBase.InitialGold);
            Assert.AreEqual(9999, run.Gold);
        }

        [Test]
        public void ActionScheduleChoices_UseGeneratedGroupSequence()
        {
            GameRun run = NewRun(week: 1);
            run.BeginTimeline("tl_normal", 7);
            var rng = new RandomService();
            rng.Init(123UL);

            List<ActionChoice> choices = ActionScheduleService.GenerateChoices(run, rng.Stream("choices"));
            List<ActionChoice> tooManyChoices = ActionScheduleService.GenerateChoices(run, rng.Stream("choices_more"), 5);

            Assert.Greater(choices.Count, 0);
            Assert.LessOrEqual(choices.Count, ActionRandomService.MaxChoiceCount);
            Assert.Greater(tooManyChoices.Count, 0);
            Assert.LessOrEqual(tooManyChoices.Count, ActionRandomService.MaxChoiceCount);
            Assert.AreEqual(7f, run.TimelineLengthDays, 1e-4f);
            Assert.AreEqual(1, run.ActionGroupSequence.Count);
            Assert.AreEqual("lg_debug", run.ActionGroupSequence[0]);
            Assert.AreEqual(1, choices.Count);
            Assert.AreEqual("act_shop", choices[0].Action.Id);

            var ids = new HashSet<string>();
            foreach (ActionChoice choice in choices)
            {
                Assert.IsTrue(ids.Add(choice.Action.Id), $"Duplicate action '{choice.Action.Id}' in scheduled choices.");
                Assert.AreEqual(run.ActionGroupSequence[0], choice.ActionGroupId);
                Assert.Greater(choice.CostDays, 0f);
            }
        }

        [Test]
        public void ActionScheduleRules_ForceRewardWindowsAndAvoidImmediateRepeat()
        {
            GameRun run = NewRun(week: 1);
            var rng = new MaxWeightRandomStream();

            for (int i = 0; i < 12; i++)
            {
                ActionScheduleService.EnsureCurrentGroup(run, rng);
                run.AdvanceActionStep();
            }

            Assert.AreEqual("lg_debug", run.ActionGroupSequence[0], "The debug rule should fill the first run action.");
            Assert.AreEqual("lg_event", run.ActionGroupSequence[1], "The opening event rule should fill the second run action.");
            Assert.AreEqual("lg_reward", run.ActionGroupSequence[2], "The early reward rule should fill the third run action.");
            Assert.AreEqual("lg_reward", run.ActionGroupSequence[9], "The mid reward rule should fill the tenth run action.");
            for (int i = 1; i < run.ActionGroupSequence.Count; i++)
            {
                Assert.AreNotEqual(run.ActionGroupSequence[i - 1], run.ActionGroupSequence[i], $"Repeated action group at index {i}.");
            }
        }

        [Test]
        public void ActionChoice_RestoresCostGroupAndRunStep()
        {
            GameRun run = NewRun(week: 1);
            run.BeginTimeline("tl_normal", 7);
            cfg.GameAction action = run.Tables.TbAction.Get("act_food_hard_gold");
            var context = new ActionExecutionContext(action, stepIndex: 2, runStepIndex: 7, actionGroupId: "grp_food_hard", costDays: 3);
            run.AppendActionGroup("grp_food_normal");
            run.AppendActionGroup("grp_food_hard");
            run.RestoreRunActionStepIndex(8);
            run.RestoreActionStepIndex(3);
            run.SetLastActionContext(context);

            GameRun restored = GameRun.FromSaveData(run.Tables, run.Database, run.ToSaveData());

            Assert.AreEqual(8, restored.RunActionStepIndex);
            Assert.AreEqual(2, restored.ActionGroupSequence.Count);
            Assert.AreEqual("grp_food_hard", restored.LastActionContext.ActionGroupId);
            Assert.AreEqual(7, restored.LastActionContext.RunStepIndex);
            Assert.AreEqual(3f, restored.LastActionContext.CostDays, 1e-4f);
        }

        [Test]
        public void ActionExecutor_DefersTimelineProgressUntilCommit()
        {
            GameRun run = NewRun(week: 1);
            run.BeginTimeline("tl_normal", 7);
            cfg.GameAction action = run.Tables.TbAction.Get("act_shop");
            float costDays = TimelineMath.Quantize(action.MinCostDays);
            var context = new ActionExecutionContext(action, run.ActionStepIndex, run.RunActionStepIndex, "lg_debug", costDays);

            ActionExecutor.Execute(run, context, new MaxWeightRandomStream());

            Assert.AreSame(context, run.LastActionContext);
            Assert.AreEqual(0f, run.CurrentDay, 1e-4f, "进入行动时行动轴进度不应提前增长。");
            Assert.AreEqual(0, run.ActionStepIndex);
            Assert.AreEqual(0, run.RunActionStepIndex);

            float prevDay = ActionExecutor.Commit(run, context);

            Assert.AreEqual(0f, prevDay, 1e-4f);
            Assert.AreEqual(costDays, run.CurrentDay, 1e-4f, "行动结算提交后才推进天数。");
            Assert.AreEqual(1, run.ActionStepIndex);
            Assert.AreEqual(1, run.RunActionStepIndex);
        }

        [Test]
        public void HiddenScore_UsesRunStepSegments()
        {
            GameRun early = NewRun(week: 1);
            GameRun late = NewRun(week: 5);
            late.RestoreRunActionStepIndex(14);

            int earlyScore = HiddenScoreService.TargetScore(early, new ActionExecutionContext(early.Tables.TbAction.Get("act_food_dish"), 0, 0, "grp_food_normal", 1));
            int lateScore = HiddenScoreService.TargetScore(late, new ActionExecutionContext(late.Tables.TbAction.Get("act_food_dish"), 0, 14, "grp_food_normal", 1));

            Assert.Greater(lateScore, earlyScore);
        }

        [Test]
        public void ActiveItemPool_IsEmpty_AfterAllPassiveRework()
        {
            // 设计已转全被动：配置表不再有主动道具，主动池抽取恒为空。
            GameRun run = NewRun(week: 1);
            var rng = new MaxWeightRandomStream();

            List<string> activeItems = ItemPoolService.Roll(run.Tables, run, cfg.ItemKind.Active, rng, 2, hidden: 0, distanceFloor: 5);

            Assert.AreEqual(0, activeItems.Count, "无主动道具，主动池应为空。");
        }

        [Test]
        public void HiddenScore_HardActionProducesHigherTargetAndRewardHidden()
        {
            GameRun run = NewRun(week: 2);
            run.CurrentDay = 3;
            run.RestoreActionStepIndex(4);

            cfg.GameAction normal = run.Tables.TbAction.Get("act_food_dish");
            cfg.GameAction hard = run.Tables.TbAction.Get("act_food_hard_passive");

            var normalContext = new ActionExecutionContext(normal);
            var hardContext = new ActionExecutionContext(hard);

            Assert.Greater(HiddenScoreService.TargetScore(run, hardContext), HiddenScoreService.TargetScore(run, normalContext));
            Assert.Greater(HiddenScoreService.PassiveItemHiddenScore(run, hardContext), HiddenScoreService.DishHiddenScore(run, normalContext));
        }

        [Test]
        public void GoldRewardRange_UsesActionDifficultyAndCurve()
        {
            GameRun run = NewRun(week: 1);
            cfg.GameAction normal = run.Tables.TbAction.Get("act_food_dish");
            cfg.GameAction hard = run.Tables.TbAction.Get("act_food_hard_passive");
            cfg.Food normalFood = FoodService.Resolve(run.Tables, normal);
            cfg.Food hardFood = FoodService.Resolve(run.Tables, hard);
            GoldRange normalRange = HiddenScoreService.GoldRewardRange(run, new ActionExecutionContext(normal), run.Tables.TbRewardPackage.Get(normalFood.RewardPackageId));
            GoldRange hardRange = HiddenScoreService.GoldRewardRange(run, new ActionExecutionContext(hard), run.Tables.TbRewardPackage.Get(hardFood.RewardPackageId));

            Assert.Greater(hardRange.Min, normalRange.Min);
            Assert.Greater(hardRange.Max, hardRange.Min);
        }

        [Test]
        public void HiddenScoreWeight_PrefersCloserHiddenMean()
        {
            float close = RewardPoolService.HiddenScoreWeight(10f, hiddenMean: 20f, requiredHidden: 20, distanceFloor: 5);
            float far = RewardPoolService.HiddenScoreWeight(10f, hiddenMean: 40f, requiredHidden: 20, distanceFloor: 5);

            Assert.Greater(close, far);
        }

        [Test]
        public void TimelineService_UsesCharacterTimelinePool()
        {
            var rng = new RandomService();
            rng.Init(123UL);

            GameRun dog = NewRun(week: 1, characterId: "glutton_dog");
            GameRun cat = NewRun(week: 1, characterId: "wok_cat");

            Assert.AreEqual("tl_normal", TimelineService.RollWeekTimeline(dog, rng.Stream("dog_timeline")));
            Assert.AreEqual("tl_busy", TimelineService.RollWeekTimeline(cat, rng.Stream("cat_timeline")));
        }

        [Test]
        public void BossService_ResolvesSingleBossFood()
        {
            GameRun dog = NewRun(week: 1, characterId: "glutton_dog");
            GameRun cat = NewRun(week: dog.TotalWeeks, characterId: "wok_cat");

            Assert.AreEqual("food_boss", BossService.ResolveBossFood(dog)?.Id);
            Assert.AreEqual("food_boss", BossService.ResolveBossFood(cat)?.Id);
        }

        [Test]
        public void EventService_UsesWeightWhenRolling()
        {
            cfg.Tables tables = LoadTables(new Dictionary<string, string>
            {
                ["tbevent"] =
                    "[" +
                    EventJson("ev_low", cfg.ActionBehavior.Event, weight: 1) + "," +
                    EventJson("ev_high", cfg.ActionBehavior.Event, weight: 100) +
                    "]",
                ["tbeventoption"] = "[]",
            });
            GameRun run = NewRun(week: 1, tables: tables);
            var rng = new MaxWeightRandomStream();

            Assert.AreEqual("ev_high", EventService.RollEvent(run, rng, cfg.ActionBehavior.Event)?.Id);
        }

        [Test]
        public void EventService_ResolvesBattleAndRunEndingFollowUps()
        {
            cfg.Tables tables = LoadTables(new Dictionary<string, string>
            {
                ["tbevent"] =
                    "[" +
                    EventJson("ev_battle", cfg.ActionBehavior.Event, weight: 1) + "," +
                    EventJson("ev_shop", cfg.ActionBehavior.Event, weight: 1) + "," +
                    EventJson("ev_gameover", cfg.ActionBehavior.Event, weight: 1) +
                    "]",
                ["tbeventoption"] =
                    "[" +
                    OptionJson("o_battle", "ev_battle", cfg.EffectType.FoodBattle, 123, "进入挑战") + "," +
                    OptionJson("o_shop", "ev_shop", cfg.EffectType.Shop, 0, "进入商店") + "," +
                    OptionJson("o_gameover", "ev_gameover", cfg.EffectType.GameOver, 0, "坏结局") +
                    "]",
            });
            GameRun run = NewRun(week: 1, tables: tables);
            var rng = new MaxWeightRandomStream();

            EventResolveResult battle = EventService.ResolveImmediate(run, tables.TbEvent.Get("ev_battle"), rng);
            EventResolveResult shop = EventService.ResolveImmediate(run, tables.TbEvent.Get("ev_shop"), rng);
            EventResolveResult gameOver = EventService.ResolveImmediate(run, tables.TbEvent.Get("ev_gameover"), rng);

            Assert.AreEqual(EventFollowUpKind.Battle, battle.FollowUpKind);
            Assert.AreEqual(123, battle.RequiredScore);
            Assert.AreEqual(EventFollowUpKind.Shop, shop.FollowUpKind);
            Assert.AreEqual(EventFollowUpKind.GameOver, gameOver.FollowUpKind);
        }

        [Test]
        public void ShopService_RollsActiveItemsAndSupportsShopManagement()
        {
            GameRun run = NewRun(week: 1);
            run.Gold = 200;
            var rng = new MaxWeightRandomStream();

            List<ShopEntry> stock = ShopService.RollStock(run.Tables, run, rng, new MaxWeightRandomStream());

            // 设计已转全被动：商店不再有主动道具，改用被动道具验证购买/出售流程。
            Assert.IsNull(stock.Find(entry => entry.Kind == ShopEntryKind.ActiveItem), "全被动后商店不应出现主动道具。");
            ShopEntry passive = stock.Find(entry => entry.Kind == ShopEntryKind.PassiveItem);
            Assert.NotNull(passive, "Shop stock should include passive items.");

            Assert.IsTrue(ShopService.Purchase(run, passive));
            Assert.IsTrue(run.HasItem(passive.Id));
            int afterPurchaseGold = run.Gold;

            Assert.IsTrue(ShopService.SellItem(run, passive.Id));
            Assert.IsFalse(run.HasItem(passive.Id));
            Assert.AreEqual(afterPurchaseGold + ShopService.ItemSellPrice, run.Gold);

            Assert.IsTrue(run.AddBonusDish("cookie"));
            int beforeDeleteGold = run.Gold;

            Assert.IsTrue(ShopService.DeleteDish(run, "cookie"));
            Assert.IsFalse(run.BonusDishIds.Contains("cookie"));
            Assert.AreEqual(beforeDeleteGold - ShopService.DeleteDishCost, run.Gold);
        }

        [Test]
        public void ShopService_SupportsRecipeBooksMoveAndTrash()
        {
            GameRun run = NewRun(week: 1);
            run.Gold = 100;

            Assert.AreEqual(GameRun.DefaultRecipeBookCount, run.RecipeBookCount);
            Assert.IsTrue(ShopService.PurchaseRecipeBook(run));
            Assert.AreEqual(3, run.RecipeBookCount);
            Assert.AreEqual(100 - ShopService.EmptyRecipeBookPrice, run.Gold);

            Assert.IsTrue(run.AddBonusDish("cookie"));
            Assert.AreEqual(1, run.GetRecipeBookDishes(0).Count);
            Assert.IsTrue(ShopService.MoveDish(run, fromBookIndex: 0, dishIndex: 0, toBookIndex: 2));
            Assert.AreEqual(0, run.GetRecipeBookDishes(0).Count);
            Assert.AreEqual("cookie", run.GetRecipeBookDishes(2)[0]);

            int beforeDeleteGold = run.Gold;
            Assert.IsTrue(ShopService.DeleteDishAt(run, bookIndex: 2, dishIndex: 0));
            Assert.AreEqual(0, run.GetRecipeBookDishes(2).Count);
            Assert.AreEqual(beforeDeleteGold - ShopService.DeleteDishCost, run.Gold);
        }

        [Test]
        public void ShopService_PurchasesDishIntoSelectedRecipeBook()
        {
            GameRun run = NewRun(week: 1);
            run.Gold = 100;
            var dish = new ShopEntry(ShopEntryKind.Dish, "cookie", "曲奇", "加入菜谱池的菜品", 30);

            Assert.IsTrue(ShopService.PurchaseDishToBook(run, dish, bookIndex: 1));

            Assert.AreEqual(70, run.Gold);
            Assert.AreEqual(0, run.GetRecipeBookDishes(0).Count);
            Assert.AreEqual("cookie", run.GetRecipeBookDishes(1)[0]);
            Assert.IsTrue(run.BonusDishIds.Contains("cookie"));
        }

        [Test]
        public void ShopService_PurchaseDishToBookRejectsInsufficientGoldAndFullBook()
        {
            GameRun run = NewRun(week: 1);
            var dish = new ShopEntry(ShopEntryKind.Dish, "cookie", "曲奇", "加入菜谱池的菜品", 30);

            run.Gold = 29;
            Assert.IsFalse(ShopService.PurchaseDishToBook(run, dish, bookIndex: 0));
            Assert.AreEqual(29, run.Gold);
            Assert.AreEqual(0, run.GetRecipeBookDishes(0).Count);

            run.Gold = 100;
            for (int i = 0; i < GameRun.RecipeBookCapacity; i++)
            {
                Assert.IsTrue(run.AddBonusDishToBook("cookie", 0));
            }

            Assert.IsFalse(ShopService.PurchaseDishToBook(run, dish, bookIndex: 0));
            Assert.AreEqual(100, run.Gold);
            Assert.AreEqual(GameRun.RecipeBookCapacity, run.GetRecipeBookDishes(0).Count);
        }

        [Test]
        public void RunSaveData_RestoresRecipeBooks()
        {
            GameRun run = NewRun(week: 1);
            Assert.IsTrue(run.AddBonusDish("cookie"));
            Assert.IsTrue(run.AddRecipeBook());
            Assert.IsTrue(run.MoveBonusDish(0, 0, 2));

            GameRun restored = GameRun.FromSaveData(run.Tables, run.Database, run.ToSaveData());

            Assert.AreEqual(3, restored.RecipeBookCount);
            Assert.AreEqual(0, restored.GetRecipeBookDishes(0).Count);
            Assert.AreEqual("cookie", restored.GetRecipeBookDishes(2)[0]);
            Assert.IsTrue(restored.BonusDishIds.Contains("cookie"));
        }

        [Test]
        public void RunSaveData_RestoresGameBaseRuntimeValues()
        {
            GameRun run = NewRun(week: 1);
            RunSaveData data = run.ToSaveData();
            data.InterestThreshold = 9;
            data.InterestGoldPer = 2;
            data.InterestCap = 7;
            data.FoodAdjustCount = 6;

            GameRun restored = GameRun.FromSaveData(run.Tables, run.Database, data);

            Assert.AreEqual(9, restored.InterestThreshold);
            Assert.AreEqual(2, restored.InterestGoldPer);
            Assert.AreEqual(7, restored.InterestCap);
            Assert.AreEqual(6, restored.FoodAdjustBaseCount);
        }

        [Test]
        public void SettlementService_ShowsRunMetricsAndUnlockDiscoveries()
        {
            GameRun run = NewRun(week: 4);
            run.Gold = 123;
            run.MarkBossCompleted("boss_glutton");
            run.MarkEventUsed("ev_recruit");
            Assert.IsTrue(run.AddBonusDish("cookie"));

            var update = new MetaProgressUpdate
            {
                Statistics = RunStatisticsService.Build(run, won: true, lastTotal: 360, lastTarget: 320),
                NewUnlocks = new List<UnlockEntry>
                {
                    new UnlockEntry("item_chef_knife", "主厨刀", "被动道具", string.Empty),
                },
            };

            SettlementSummary summary = SettlementService.Build(run, won: true, lastTotal: 360, lastTarget: 320, update);

            StringAssert.Contains("通关", summary.Title);
            StringAssert.Contains("金币：123", summary.Body);
            StringAssert.Contains("击败 Boss：1 个", summary.Body);
            StringAssert.Contains("新解锁", summary.Body);
            StringAssert.Contains("被动道具：主厨刀", summary.Body);
        }

        [Test]
        public void RunSaveData_RestoresScoreOverrideAndLastActionContext()
        {
            GameRun run = NewRun(week: 2);
            run.BeginTimeline("tl_normal", 7);
            run.CurrentDay = 3;
            run.RequiredScoreOverride = 99;

            cfg.GameAction hard = run.Tables.TbAction.Get("act_food_hard_passive");
            run.SetLastActionContext(new ActionExecutionContext(hard, 4));
            run.RestoreActionStepIndex(5);

            GameRun restored = GameRun.FromSaveData(run.Tables, run.Database, run.ToSaveData());

            Assert.AreEqual(99, restored.RequiredScoreOverride);
            Assert.AreEqual("act_food_hard_passive", restored.LastActionContext?.Action?.Id);
            Assert.AreEqual(4, restored.LastActionContext?.StepIndex);
            Assert.AreEqual(5, restored.ActionStepIndex);
        }

        [Test]
        public void RunSaveData_RestoresPendingActionChoices()
        {
            GameRun run = NewRun(week: 1);
            run.BeginTimeline("tl_normal", 7);
            var rng = new RandomService();
            rng.Init("pending-action");

            string key = GameRun.BuildActionChoiceKey(run.RunActionStepIndex, run.WeekIndex, run.CurrentDay, run.ActionStepIndex);
            List<ActionChoice> choices = ActionScheduleService.GenerateChoices(run, rng.Stream("choices"));
            run.SetPendingActionChoices(key, choices);

            GameRun restored = GameRun.FromSaveData(run.Tables, run.Database, run.ToSaveData());
            List<ActionChoice> restoredChoices = restored.GetPendingActionChoices(key);

            Assert.IsTrue(restored.HasPendingActionChoices(key));
            Assert.AreEqual(choices.Count, restoredChoices.Count);
            for (int i = 0; i < choices.Count; i++)
            {
                Assert.AreEqual(choices[i].Action.Id, restoredChoices[i].Action.Id);
                Assert.AreEqual(choices[i].ActionGroupId, restoredChoices[i].ActionGroupId);
                Assert.AreEqual(choices[i].CostDays, restoredChoices[i].CostDays);
            }
        }

        [Test]
        public void RunSaveData_RestoresEmptyPendingShopStock()
        {
            GameRun run = NewRun(week: 1);
            string key = GameRun.BuildShopKey(run.WeekIndex, run.CurrentDay);
            run.SetPendingShopStock(key, new List<ShopEntry>());

            GameRun restored = GameRun.FromSaveData(run.Tables, run.Database, run.ToSaveData());

            Assert.IsTrue(restored.HasPendingShopStock(key));
            Assert.AreEqual(0, restored.GetPendingShopStock(key).Count);
        }

        [Test]
        public void RunSaveData_RestoresPendingRewardOffer()
        {
            GameRun run = NewRun(week: 1);
            string key = GameRun.BuildRewardKey(run.WeekIndex, run.CurrentDay, null);
            var offer = new RewardOffer(
                25,
                new[] { new RewardChoice(cfg.RewardKind.DishChoice, "cookie", "曲奇", "加入菜谱池") },
                new[] { RewardChoice.Gold(8, "额外金币") });
            offer.MarkBaseGoldClaimed();
            offer.MarkMainChoiceClaimed(0);

            run.SetPendingRewardOffer(key, offer);

            GameRun restored = GameRun.FromSaveData(run.Tables, run.Database, run.ToSaveData());
            RewardOffer restoredOffer = restored.GetPendingRewardOffer(key);

            Assert.NotNull(restoredOffer);
            Assert.AreEqual(25, restoredOffer.BaseGold);
            Assert.IsTrue(restoredOffer.BaseGoldClaimed);
            Assert.AreEqual(0, restoredOffer.MainChoiceIndex);
            Assert.AreEqual("cookie", restoredOffer.MainChoices[0].Id);
            Assert.AreEqual(8, restoredOffer.ExtraChoices[0].GoldAmount);
        }

        [Test]
        public void RunSaveData_RestoresSkippedRewardOfferWithoutResolving()
        {
            GameRun run = NewRun(week: 1);
            string key = GameRun.BuildRewardKey(run.WeekIndex, run.CurrentDay, null);
            var offer = new RewardOffer(
                25,
                new[] { new RewardChoice(cfg.RewardKind.DishChoice, "cookie", "曲奇", "加入菜谱池") },
                null);
            offer.MarkBaseGoldClaimed();
            offer.MarkMainChoiceSkipped();

            run.SetPendingRewardOffer(key, offer);

            GameRun restored = GameRun.FromSaveData(run.Tables, run.Database, run.ToSaveData());
            RewardOffer restoredOffer = restored.GetPendingRewardOffer(key);

            Assert.NotNull(restoredOffer);
            Assert.IsTrue(restoredOffer.MainChoiceSkipped);
            Assert.IsFalse(restoredOffer.MainChoiceResolved);
            Assert.IsFalse(restoredOffer.IsFullyClaimed);
            Assert.AreEqual(-1, restoredOffer.MainChoiceIndex);
        }

        [Test]
        public void RewardGranterApply_AddsBaseGoldAndSelectedGoldChoice()
        {
            GameRun run = NewRun(week: 1);
            int beforeGold = run.Gold;
            RewardChoice goldChoice = RewardChoice.Gold(8, "额外金币");
            var offer = new RewardOffer(25, new[] { goldChoice }, null);

            string resultText = RewardGranter.Apply(run, offer, goldChoice, null);

            Assert.AreEqual(beforeGold + 33, run.Gold);
            StringAssert.Contains("金币 +25", resultText);
            StringAssert.Contains("额外金币 +8", resultText);
        }

        [Test]
        public void RewardGranterApplyBaseGold_IsIdempotentPerOffer()
        {
            GameRun run = NewRun(week: 1);
            int beforeGold = run.Gold;
            var offer = new RewardOffer(25, null, null);

            RewardGranter.ApplyBaseGold(run, offer);
            RewardGranter.ApplyBaseGold(run, offer);

            Assert.AreEqual(beforeGold + 25, run.Gold);
            Assert.IsTrue(offer.BaseGoldClaimed);
        }

        [Test]
        public void RewardGranterApplyDishChoiceToBook_UsesSelectedRecipeBook()
        {
            GameRun run = NewRun(week: 1);
            RewardChoice dishChoice = new RewardChoice(cfg.RewardKind.DishChoice, "cookie", "曲奇", "加入菜谱池");

            Assert.IsTrue(RewardGranter.ApplyDishChoiceToBook(run, dishChoice, 1));

            Assert.AreEqual(0, run.GetRecipeBookDishes(0).Count);
            Assert.AreEqual("cookie", run.GetRecipeBookDishes(1)[0]);
        }

        [Test]
        public void RewardGranterApplyDishChoiceToBook_RejectsFullRecipeBook()
        {
            GameRun run = NewRun(week: 1);
            RewardChoice dishChoice = new RewardChoice(cfg.RewardKind.DishChoice, "cookie", "曲奇", "加入菜谱池");
            for (int i = 0; i < GameRun.RecipeBookCapacity; i++)
            {
                Assert.IsTrue(run.AddBonusDishToBook("cookie", 0));
            }

            Assert.IsFalse(RewardGranter.ApplyDishChoiceToBook(run, dishChoice, 0));

            Assert.AreEqual(GameRun.RecipeBookCapacity, run.GetRecipeBookDishes(0).Count);
        }

        [Test]
        public void RewardGranterApplyFragmentPack_SetsPendingFragmentPack()
        {
            GameRun run = NewRun(week: 1);
            List<RewardChoice> choices = run.Database.AllFragments
                .Take(3)
                .Select(f => new RewardChoice(cfg.RewardKind.FragmentChoice, f.Id, f.Id, string.Empty))
                .ToList();

            string resultText = RewardGranter.ApplyFragmentPack(run, choices);

            Assert.IsTrue(run.HasPendingFragmentPack);
            Assert.AreEqual(choices.Count, run.PendingFragmentPack.Count);
            StringAssert.Contains("胃部碎片包", resultText);
        }

        [Test]
        public void FoodBehavior_NormalFoodProducesNoDebuffModifier()
        {
            GameRun run = NewRun(week: 1);
            cfg.GameAction action = run.Tables.TbAction.Get("act_food_dish");

            ActionOutcome outcome = new FoodBehaviorHandler().Execute(
                run,
                new ActionExecutionContext(action),
                new MaxWeightRandomStream());

            Assert.AreEqual(ActionOutcomeKind.Battle, outcome.Kind);
            Assert.IsFalse(outcome.IsBoss);
            Assert.AreEqual(string.Empty, outcome.Modifier);
            Assert.AreEqual(string.Empty, outcome.BossDebuffId);
        }

        [Test]
        public void BossDebuffRoll_UsesHistoryAndResetsWhenExhausted()
        {
            var rng = new MaxWeightRandomStream();
            GameRun run = NewRun(week: 1, characterId: "glutton_dog");

            cfg.BossDebuff first = BossService.RollBossDebuff(run, rng);
            string firstId = run.Tables.TbBossDebuff.DataList[0].Id;
            Assert.AreEqual(firstId, first?.Id);
            run.MarkBossDebuffRolled(first.Id);

            cfg.BossDebuff second = BossService.RollBossDebuff(run, rng);
            Assert.AreEqual(run.Tables.TbBossDebuff.DataList[1].Id, second?.Id);
            run.MarkBossDebuffRolled(second.Id);

            for (int i = 2; i < run.Tables.TbBossDebuff.DataList.Count; i++)
            {
                run.MarkBossDebuffRolled(run.Tables.TbBossDebuff.DataList[i].Id);
            }

            Assert.AreEqual(run.Tables.TbBossDebuff.DataList.Count, run.RolledBossDebuffIds.Count);
            Assert.AreEqual(firstId, BossService.RollBossDebuff(run, rng)?.Id);
            Assert.AreEqual(0, run.RolledBossDebuffIds.Count);
        }

        [Test]
        public void BossDebuffRoll_PreviewDoesNotResetHistory()
        {
            var rng = new MaxWeightRandomStream();
            GameRun run = NewRun(week: 1, characterId: "glutton_dog");
            foreach (cfg.BossDebuff debuff in run.Tables.TbBossDebuff.DataList)
            {
                run.MarkBossDebuffRolled(debuff.Id);
            }

            cfg.BossDebuff preview = BossService.RollBossDebuff(run, rng, mutateHistoryOnExhaustion: false);

            Assert.AreEqual(run.Tables.TbBossDebuff.DataList[0].Id, preview?.Id);
            Assert.AreEqual(run.Tables.TbBossDebuff.DataList.Count, run.RolledBossDebuffIds.Count);
        }

        [Test]
        public void RunSaveData_RestoresBossDebuffRollHistory()
        {
            GameRun run = NewRun(week: 1);
            run.MarkBossDebuffRolled("debuff_indulgent");

            GameRun restored = GameRun.FromSaveData(run.Tables, run.Database, run.ToSaveData());

            Assert.IsTrue(restored.IsBossDebuffRolled("debuff_indulgent"));
        }

        [Test]
        public void WeekLoop_FinalBossVictory_UsesLastWeekInsteadOfFoodWeek()
        {
            GameRun finalWeek = NewRun(week: 8, characterId: "glutton_dog");
            GameRun earlyWeek = NewRun(week: 1, characterId: "glutton_dog");
            cfg.Food boss = finalWeek.Tables.TbFood.Get("food_boss");
            MethodInfo method = typeof(WeekLoopController).GetMethod(
                "IsFinalBossVictory",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            bool finalResult = (bool)method.Invoke(new WeekLoopController(finalWeek, null), new object[] { boss });
            bool earlyResult = (bool)method.Invoke(new WeekLoopController(earlyWeek, null), new object[] { boss });

            Assert.IsTrue(finalResult);
            Assert.IsFalse(earlyResult);
        }

        [Test]
        public void RunSaveData_RestoresPendingShopStockContent()
        {
            GameRun run = NewRun(week: 1);
            var rng = new RandomService();
            rng.Init("pending-shop");

            string key = GameRun.BuildShopKey(run.WeekIndex, run.CurrentDay);
            List<ShopEntry> rolled = ShopService.RollStock(
                run.Tables,
                run,
                rng.DomainStream(SeedDomains.Shop, key),
                rng.DomainStream(SeedDomains.Loot, $"shop_{key}"));
            Assert.Greater(rolled.Count, 0, "Precondition: shop should roll a non-empty stock.");
            run.SetPendingShopStock(key, rolled);

            GameRun restored = GameRun.FromSaveData(run.Tables, run.Database, run.ToSaveData());
            List<ShopEntry> restoredStock = restored.GetPendingShopStock(key);

            Assert.IsTrue(restored.HasPendingShopStock(key));
            Assert.AreEqual(rolled.Count, restoredStock.Count);
            for (int i = 0; i < rolled.Count; i++)
            {
                Assert.AreEqual(rolled[i].Kind, restoredStock[i].Kind);
                Assert.AreEqual(rolled[i].Id, restoredStock[i].Id);
                Assert.AreEqual(rolled[i].Name, restoredStock[i].Name);
                Assert.AreEqual(rolled[i].Price, restoredStock[i].Price);
            }
        }

        [Test]
        public void PendingShopStock_ClearedByBeginTimelineNotByActionStep()
        {
            GameRun run = NewRun(week: 1);
            run.BeginTimeline("tl_normal", 7);
            string key = GameRun.BuildShopKey(run.WeekIndex, run.CurrentDay);
            run.SetPendingShopStock(key, new List<ShopEntry>
            {
                new ShopEntry(ShopEntryKind.Dish, "cookie", "曲奇", "测试菜品", 10),
            });

            // 推进行动步只清行动候选，不应影响商店库存：离开商店后同一步内再进仍沿用同一份库存。
            run.AdvanceActionStep();
            Assert.IsTrue(run.HasPendingShopStock(key), "Advancing the action step must not refresh shop stock.");

            // 进入下一刷新点（新行动轴）才清空商店库存。
            run.BeginTimeline("tl_normal", 7);
            Assert.IsFalse(run.HasPendingShopStock(key), "BeginTimeline is the shop refresh point and should clear pending stock.");
        }

        [Test]
        public void BossDebuffRoll_PerNodeKeyIsOrderIndependent()
        {
            GameRun runForward = NewRun(week: 4, characterId: "glutton_dog");
            GameRun runReverse = NewRun(week: 4, characterId: "glutton_dog");

            var forward = new RandomService();
            forward.Init("boss-order");
            string alphaFirst = BossService.RollBossDebuff(runForward, forward.DomainStream(SeedDomains.Boss, "w4_alpha_debuff"))?.Id;
            string betaSecond = BossService.RollBossDebuff(runForward, forward.DomainStream(SeedDomains.Boss, "w4_beta_debuff"))?.Id;

            var reverse = new RandomService();
            reverse.Init("boss-order");
            string betaFirst = BossService.RollBossDebuff(runReverse, reverse.DomainStream(SeedDomains.Boss, "w4_beta_debuff"))?.Id;
            string alphaSecond = BossService.RollBossDebuff(runReverse, reverse.DomainStream(SeedDomains.Boss, "w4_alpha_debuff"))?.Id;

            Assert.AreEqual(alphaFirst, alphaSecond, "Debuff for node 'alpha' must not depend on whether node 'beta' rolled first.");
            Assert.AreEqual(betaFirst, betaSecond, "Debuff for node 'beta' must not depend on whether node 'alpha' rolled first.");
        }

        private static GameRun NewRun(int week, string characterId = "glutton_dog", cfg.Tables tables = null)
        {
            tables ??= LoadTables();
            GameplayDatabase database = GameplayContentBuilder.BuildDatabase(tables);
            return new GameRun(tables, database, characterId, "v2-test", week);
        }

        /// <summary>构造一行 TbEvent JSON（tbevent 覆盖用），字段与生成的 GameEvent 对齐。</summary>
        private static string EventJson(string id, cfg.ActionBehavior eventType, float weight)
        {
            return "{" +
                $"\"id\":\"{id}\",\"name\":\"{id}\",\"desc\":\"\"," +
                $"\"eventType\":{(int)eventType}," +
                $"\"preconditions\":\"\",\"weight\":{weight.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"repeatable\":true" +
                "}";
        }

        /// <summary>构造一行 TbEventOption JSON（tbeventoption 覆盖用），字段与生成的 EventOption 对齐。</summary>
        private static string OptionJson(string id, string eventId, cfg.EffectType effectType, float effectValue, string text = "")
        {
            return "{" +
                $"\"id\":\"{id}\",\"eventId\":\"{eventId}\",\"text\":\"{text}\"," +
                $"\"effectType\":{(int)effectType},\"effectValue\":{effectValue.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"effectParam\":\"\"" +
                "}";
        }

        private static cfg.Tables LoadTables(IReadOnlyDictionary<string, string> overrides = null)
        {
            string root = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "StreamingAssets", "Config");
            return new cfg.Tables(name =>
            {
                if (overrides != null && overrides.TryGetValue(name, out string json))
                {
                    return JSON.Parse(json);
                }

                string path = Path.Combine(root, name + ".json");
                return JSON.Parse(File.ReadAllText(path));
            });
        }

        private sealed class MaxWeightRandomStream : IRandomStream
        {
            public RngState State { get; set; }

            public uint NextUInt() => throw new NotSupportedException();

            public ulong NextULong() => throw new NotSupportedException();

            public int Range(int minInclusive, int maxExclusive) => minInclusive;

            public float Range(float minInclusive, float maxExclusive) => minInclusive;

            public float NextFloat() => throw new NotSupportedException();

            public double NextDouble() => throw new NotSupportedException();

            public bool NextBool(double probability = 0.5) => throw new NotSupportedException();

            public void Shuffle<T>(IList<T> list) => throw new NotSupportedException();

            public T Pick<T>(IReadOnlyList<T> list) => throw new NotSupportedException("EventService must use weighted selection.");

            public int WeightedPickIndex(IReadOnlyList<float> weights)
            {
                int bestIndex = 0;
                float bestWeight = float.MinValue;
                for (int i = 0; i < weights.Count; i++)
                {
                    if (weights[i] > bestWeight)
                    {
                        bestWeight = weights[i];
                        bestIndex = i;
                    }
                }

                return bestIndex;
            }
        }
    }
}
