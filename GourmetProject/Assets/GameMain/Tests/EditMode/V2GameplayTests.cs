using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Tests
{
    public class V2GameplayTests
    {
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
            Assert.AreEqual(7, run.TimelineLengthDays);
            Assert.AreEqual(1, run.ActionGroupSequence.Count);
            Assert.IsFalse(string.IsNullOrEmpty(run.ActionGroupSequence[0]));

            var ids = new HashSet<string>();
            foreach (ActionChoice choice in choices)
            {
                Assert.IsTrue(ids.Add(choice.Action.Id), $"Duplicate action '{choice.Action.Id}' in scheduled choices.");
                Assert.AreEqual(run.ActionGroupSequence[0], choice.ActionGroupId);
                Assert.Greater(choice.CostDays, 0);
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

            Assert.AreEqual("grp_event_food", run.ActionGroupSequence[1], "The opening event rule should fill the second run action.");
            Assert.AreEqual("grp_reward", run.ActionGroupSequence[2], "The early reward rule should fill the third run action.");
            Assert.AreEqual("grp_reward", run.ActionGroupSequence[9], "The mid reward rule should fill the tenth run action.");
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
            Assert.AreEqual(3, restored.LastActionContext.CostDays);
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
        public void ActiveItemRolls_WithReplacement()
        {
            GameRun run = NewRun(week: 1);
            var rng = new MaxWeightRandomStream();

            List<string> activeItems = ItemPoolService.Roll(run.Tables, run, cfg.ItemKind.Active, rng, 2, hidden: 0, distanceFloor: 5);

            Assert.AreEqual(2, activeItems.Count);
            Assert.AreEqual(activeItems[0], activeItems[1], "Active item rolls should be with replacement.");
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
            GoldRange normalRange = HiddenScoreService.GoldRewardRange(run, new ActionExecutionContext(normal), run.Tables.TbRewardPackage.Get(normal.RewardPackageId));
            GoldRange hardRange = HiddenScoreService.GoldRewardRange(run, new ActionExecutionContext(hard), run.Tables.TbRewardPackage.Get(hard.RewardPackageId));

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
        public void BossService_UsesCharacterBossPoolAndNodePool()
        {
            var rng = new RandomService();
            rng.Init(456UL);

            GameRun dog = NewRun(week: 4, characterId: "glutton_dog");
            GameRun cat = NewRun(week: 4, characterId: "wok_cat");

            Assert.AreEqual("boss_glutton", BossService.RollBoss(dog, rng.Stream("dog_boss"))?.Id);
            Assert.AreEqual("boss_iron", BossService.RollBoss(cat, rng.Stream("cat_boss"))?.Id);
            Assert.IsNull(BossService.RollBoss(cat, rng.Stream("cat_blocked_boss"), "boss_glutton"));

            GameRun finalWeek = NewRun(week: dog.TotalWeeks, characterId: "glutton_dog");
            Assert.AreEqual("boss_final", BossService.RollBoss(finalWeek, rng.Stream("final_boss"))?.Id);
            Assert.IsNull(BossService.RollBoss(finalWeek, rng.Stream("blocked_normal_boss"), "boss_glutton"));
        }

        [Test]
        public void EventService_UsesWeightAndFiltersUsedNonRepeatable()
        {
            cfg.Tables tables = LoadTables(new Dictionary<string, string>
            {
                ["tbevent"] =
                    "[" +
                    "{\"id\":\"ev_low\",\"name\":\"低权重\",\"desc\":\"\",\"timeCost\":1,\"effectType\":\"GainGold\",\"effectValue\":1,\"category\":\"test\",\"weight\":1,\"repeatable\":true,\"preconditions\":\"\"}," +
                    "{\"id\":\"ev_high\",\"name\":\"高权重\",\"desc\":\"\",\"timeCost\":1,\"effectType\":\"GainGold\",\"effectValue\":1,\"category\":\"test\",\"weight\":100,\"repeatable\":false,\"preconditions\":\"\"}" +
                    "]",
            });
            GameRun run = NewRun(week: 1, tables: tables);
            var rng = new MaxWeightRandomStream();

            Assert.AreEqual("ev_high", EventService.RollEvent(run, rng)?.Id);

            run.MarkEventUsed("ev_high");

            Assert.AreEqual("ev_low", EventService.RollEvent(run, rng)?.Id);
        }

        [Test]
        public void EventService_ResolvesBattleAndRunEndingFollowUps()
        {
            cfg.Tables tables = LoadTables(new Dictionary<string, string>
            {
                ["tbevent"] =
                    "[" +
                    "{\"id\":\"ev_battle\",\"name\":\"挑战\",\"desc\":\"进入挑战\",\"timeCost\":1,\"effectType\":\"FoodBattle\",\"effectValue\":123,\"category\":\"test\",\"weight\":1,\"repeatable\":true,\"preconditions\":\"\"}," +
                    "{\"id\":\"ev_gameover\",\"name\":\"坏结局\",\"desc\":\"\",\"timeCost\":1,\"effectType\":\"GameOver\",\"effectValue\":0,\"category\":\"test\",\"weight\":1,\"repeatable\":true,\"preconditions\":\"\"}" +
                    "]",
            });
            GameRun run = NewRun(week: 1, tables: tables);
            var rng = new MaxWeightRandomStream();

            EventResolveResult battle = EventService.ResolveImmediate(run, tables.TbEvent.Get("ev_battle"), rng);
            EventResolveResult gameOver = EventService.ResolveImmediate(run, tables.TbEvent.Get("ev_gameover"), rng);

            Assert.AreEqual(EventFollowUpKind.Battle, battle.FollowUpKind);
            Assert.AreEqual(123, battle.RequiredScore);
            Assert.AreEqual(EventFollowUpKind.GameOver, gameOver.FollowUpKind);
        }

        [Test]
        public void ShopService_RollsActiveItemsAndSupportsShopManagement()
        {
            GameRun run = NewRun(week: 1);
            run.Gold = 100;
            var rng = new MaxWeightRandomStream();

            List<ShopEntry> stock = ShopService.RollStock(run.Tables, run, rng, new MaxWeightRandomStream());

            ShopEntry active = stock.Find(entry => entry.Kind == ShopEntryKind.ActiveItem);
            Assert.NotNull(active, "Shop stock should include active items from the action-design shop pool.");

            Assert.IsTrue(ShopService.Purchase(run, active));
            Assert.IsTrue(run.HasItem(active.Id));
            int afterPurchaseGold = run.Gold;

            Assert.IsTrue(ShopService.SellItem(run, active.Id));
            Assert.IsFalse(run.HasItem(active.Id));
            Assert.AreEqual(afterPurchaseGold + ShopService.ItemSellPrice, run.Gold);

            Assert.IsTrue(run.AddBonusDish("rice"));
            int beforeDeleteGold = run.Gold;

            Assert.IsTrue(ShopService.DeleteDish(run, "rice"));
            Assert.IsFalse(run.BonusDishIds.Contains("rice"));
            Assert.AreEqual(beforeDeleteGold - ShopService.DeleteDishCost, run.Gold);
        }

        [Test]
        public void SettlementService_ShowsRunMetricsAndUnlockDiscoveries()
        {
            GameRun run = NewRun(week: 4);
            run.Gold = 123;
            run.MarkBossCompleted("boss_glutton");
            run.MarkEventUsed("ev_recruit");
            Assert.IsTrue(run.AddBonusDish("rice"));

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
                new[] { new RewardChoice(cfg.RewardKind.DishChoice, "rice", "米饭", "加入菜谱池") },
                new[] { RewardChoice.Gold(8, "额外金币") });

            run.SetPendingRewardOffer(key, offer);

            GameRun restored = GameRun.FromSaveData(run.Tables, run.Database, run.ToSaveData());
            RewardOffer restoredOffer = restored.GetPendingRewardOffer(key);

            Assert.NotNull(restoredOffer);
            Assert.AreEqual(25, restoredOffer.BaseGold);
            Assert.AreEqual("rice", restoredOffer.MainChoices[0].Id);
            Assert.AreEqual(8, restoredOffer.ExtraChoices[0].GoldAmount);
        }

        [Test]
        public void BossService_SkipsCompletedGenericBosses()
        {
            var rng = new MaxWeightRandomStream();
            GameRun run = NewRun(week: 8, characterId: "glutton_dog");

            run.MarkBossCompleted("boss_glutton");

            Assert.IsNull(BossService.RollBoss(run, rng, "boss_glutton"));
            Assert.AreEqual("boss_final", BossService.RollBoss(run, rng, "boss_final")?.Id);
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
                new ShopEntry(ShopEntryKind.Dish, "rice", "米饭", "测试菜品", 10),
            });

            // 推进行动步只清行动候选，不应影响商店库存：离开商店后同一步内再进仍沿用同一份库存。
            run.AdvanceActionStep();
            Assert.IsTrue(run.HasPendingShopStock(key), "Advancing the action step must not refresh shop stock.");

            // 进入下一刷新点（新行动轴）才清空商店库存。
            run.BeginTimeline("tl_normal", 7);
            Assert.IsFalse(run.HasPendingShopStock(key), "BeginTimeline is the shop refresh point and should clear pending stock.");
        }

        [Test]
        public void BossRoll_PerNodeKeyIsOrderIndependent()
        {
            GameRun runForward = NewRun(week: 4, characterId: "glutton_dog");
            GameRun runReverse = NewRun(week: 4, characterId: "glutton_dog");

            var forward = new RandomService();
            forward.Init("boss-order");
            string alphaFirst = BossService.RollBoss(runForward, forward.DomainStream(SeedDomains.Boss, "w4_alpha"))?.Id;
            string betaSecond = BossService.RollBoss(runForward, forward.DomainStream(SeedDomains.Boss, "w4_beta"))?.Id;

            var reverse = new RandomService();
            reverse.Init("boss-order");
            string betaFirst = BossService.RollBoss(runReverse, reverse.DomainStream(SeedDomains.Boss, "w4_beta"))?.Id;
            string alphaSecond = BossService.RollBoss(runReverse, reverse.DomainStream(SeedDomains.Boss, "w4_alpha"))?.Id;

            // 每个节点的 boss 只由自己的 key 决定，与同周其它 Boss 节点的抽取顺序无关。
            Assert.AreEqual(alphaFirst, alphaSecond, "Boss for node 'alpha' must not depend on whether node 'beta' rolled first.");
            Assert.AreEqual(betaFirst, betaSecond, "Boss for node 'beta' must not depend on whether node 'alpha' rolled first.");
        }

        private static GameRun NewRun(int week, string characterId = "glutton_dog", cfg.Tables tables = null)
        {
            tables ??= LoadTables();
            GameplayDatabase database = GameplayContentBuilder.BuildDatabase(tables);
            return new GameRun(tables, database, characterId, "v2-test", week);
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
