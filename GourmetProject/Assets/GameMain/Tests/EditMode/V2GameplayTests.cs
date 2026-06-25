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

            List<ShopEntry> stock = ShopService.RollStock(run.Tables, run, rng);

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

            SettlementSummary summary = SettlementService.Build(run, won: true, lastTotal: 360, lastTarget: 320);

            StringAssert.Contains("通关", summary.Title);
            StringAssert.Contains("金币：123", summary.Body);
            StringAssert.Contains("Boss 图鉴", summary.Body);
            StringAssert.Contains("新解锁 / 新发现", summary.Body);
            StringAssert.Contains("菜谱扩展记录", summary.Body);
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
        public void BossService_SkipsCompletedGenericBosses()
        {
            var rng = new MaxWeightRandomStream();
            GameRun run = NewRun(week: 8, characterId: "glutton_dog");

            run.MarkBossCompleted("boss_glutton");

            Assert.IsNull(BossService.RollBoss(run, rng, "boss_glutton"));
            Assert.AreEqual("boss_final", BossService.RollBoss(run, rng, "boss_final")?.Id);
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
