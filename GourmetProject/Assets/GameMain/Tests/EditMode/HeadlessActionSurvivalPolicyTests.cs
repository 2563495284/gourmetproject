using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Balance;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using Luban.SimpleJSON;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class HeadlessActionSurvivalPolicyTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;
        private string _characterId;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            string configDirectory = Path.GetFullPath(
                Path.Combine("Assets", "StreamingAssets", "Config"));
            _tables = new cfg.Tables(name =>
                JSON.Parse(File.ReadAllText(Path.Combine(configDirectory, name + ".json"))));
            _database = GameplayContentBuilder.BuildDatabase(_tables);
            _characterId = _tables.TbCharacter.DataList.First().Id;
        }

        [Test]
        public void Expert_OneHeartAndRecentFailures_PicksEventOverHardReward_WithoutRuleRngConsumption()
        {
            GameRun run = CreateRun("survival-expert-low-heart");
            Assert.That(run.TryLoseHearts(2, out _, out int hearts), Is.True);
            Assert.That(hearts, Is.EqualTo(1));
            HeadlessWeekLoopView view = CreateView(run, AutoPlayerLevel.Expert, 1201);
            var stage = new AutoRunStageTrace { Week = 1, MealBattles = 3, MealPasses = 0 };
            var choices = new[]
            {
                Choice("act_food_hard_active_strengthen", 0.8f),
                Choice("act_event", 0.5f),
            };
            RandomSnapshot before = run.Random.Capture();

            ActionChoice selected = view.PickAction(
                choices,
                MetaRoute.Normal,
                stage,
                out string reason);

            Assert.That(selected.Action.Id, Is.EqualTo("act_event"));
            Assert.That(reason, Does.Contain("生存优先"));
            Assert.That(reason, Does.Contain("心=1/3"));
            Assert.That(reason, Does.Contain("本周通过=0/3"));
            Assert.That(reason, Does.Contain("act_food_hard_active_strengthen="));
            AssertRandomSnapshotEqual(before, run.Random.Capture());
        }

        [Test]
        public void Expert_StrongBuildAndRecentPasses_CanAcceptMealAtOneHeart()
        {
            GameRun run = CreateRun("survival-expert-strong-build");
            run.TryLoseHearts(2, out _, out _);
            foreach (RecipeBookSlot slot in run.RecipeEntries)
            {
                slot.AddScoreFlat(5000);
            }

            HeadlessWeekLoopView view = CreateView(run, AutoPlayerLevel.Expert, 1202);
            var stage = new AutoRunStageTrace { Week = 1, MealBattles = 3, MealPasses = 3 };
            var choices = new[]
            {
                Choice("act_food_active_strengthen", 0.7f),
                Choice("act_event", 0.5f),
            };

            ActionChoice selected = view.PickAction(
                choices,
                MetaRoute.Normal,
                stage,
                out string reason);

            Assert.That(selected.Action.Id, Is.EqualTo("act_food_active_strengthen"));
            Assert.That(reason, Does.Contain("构筑比=2.00"));
            Assert.That(reason, Does.Contain("本周通过=3/3"));
            Assert.That(reason, Does.Contain("类型=日常营业"));
        }

        [Test]
        public void Expert_RecentFailureEvidence_ChangesOtherwiseStrongOpeningChoiceToSafety()
        {
            GameRun successfulRun = CreateRun("survival-history-success");
            successfulRun.TryLoseHearts(1, out _, out _);
            HeadlessWeekLoopView successfulView = CreateView(successfulRun, AutoPlayerLevel.Expert, 1203);
            var choices = new[]
            {
                Choice("act_food_active_strengthen", 0.7f),
                Choice("act_event", 0.5f),
            };

            ActionChoice beforeFailures = successfulView.PickAction(
                choices,
                MetaRoute.Normal,
                new AutoRunStageTrace { Week = 1 },
                out _);

            GameRun failingRun = CreateRun("survival-history-fail");
            failingRun.TryLoseHearts(1, out _, out _);
            HeadlessWeekLoopView failingView = CreateView(failingRun, AutoPlayerLevel.Expert, 1203);
            ActionChoice afterFailures = failingView.PickAction(
                choices,
                MetaRoute.Normal,
                new AutoRunStageTrace { Week = 1, MealBattles = 4, MealPasses = 0 },
                out string failureReason);

            Assert.That(beforeFailures.Action.Id, Is.EqualTo("act_food_active_strengthen"));
            Assert.That(afterFailures.Action.Id, Is.EqualTo("act_event"));
            Assert.That(failureReason, Does.Contain("本周通过=0/4"));
        }

        [Test]
        public void Normal_SoftmaxChoice_IsDeterministicAndUsesOnlyPolicyRng()
        {
            GameRun firstRun = CreateRun("survival-normal-rule-a");
            GameRun secondRun = CreateRun("survival-normal-rule-a");
            firstRun.TryLoseHearts(2, out _, out _);
            secondRun.TryLoseHearts(2, out _, out _);
            HeadlessWeekLoopView first = CreateView(firstRun, AutoPlayerLevel.Normal, 1301);
            HeadlessWeekLoopView second = CreateView(secondRun, AutoPlayerLevel.Normal, 1301);
            var stage = new AutoRunStageTrace { Week = 1, MealBattles = 3, MealPasses = 1 };
            var choices = new[]
            {
                Choice("act_food_active_strengthen", 0.7f),
                Choice("act_food_hard_fragment", 0.8f),
                Choice("act_event", 0.5f),
            };
            RandomSnapshot firstBefore = firstRun.Random.Capture();
            RandomSnapshot secondBefore = secondRun.Random.Capture();

            ActionChoice firstSelected = first.PickAction(choices, MetaRoute.Normal, stage, out string firstReason);
            ActionChoice secondSelected = second.PickAction(choices, MetaRoute.Normal, stage, out string secondReason);

            Assert.That(secondSelected.Action.Id, Is.EqualTo(firstSelected.Action.Id));
            Assert.That(secondReason, Is.EqualTo(firstReason));
            Assert.That(firstReason, Does.Contain("policy-softmax"));
            Assert.That(firstReason, Does.Contain("候选=["));
            AssertRandomSnapshotEqual(firstBefore, firstRun.Random.Capture());
            AssertRandomSnapshotEqual(secondBefore, secondRun.Random.Capture());
        }

        [Test]
        public void ActionDecision_RefreshesRouteAfterMidweekItemAcquire_WithoutRuleRngConsumption()
        {
            GameRun run = CreateRun("survival-route-refresh");
            ItemAcquireResult acquired = run.AcquireItem(
                "item_gold_random",
                fallbackGold: 0,
                fireOnAcquire: false);
            Assert.That(acquired.Outcome, Is.EqualTo(ItemAcquireOutcome.Added));

            MetaAffinityCatalog affinity = UnityEngine.ScriptableObject.CreateInstance<MetaAffinityCatalog>();
            try
            {
                affinity.Entries = new List<MetaAffinityEntry>
                {
                    new MetaAffinityEntry
                    {
                        ItemId = "item_gold_random",
                        Normal = 0.25f,
                        Event = 100f,
                    },
                };
                AutoRunRequest request = Request(AutoPlayerLevel.Expert, 1302);
                request.MetaAffinity = affinity;
                var trace = new AutoRunTrace
                {
                    Seed = 1302,
                    CharacterId = _characterId,
                    PlayerLevel = AutoPlayerLevel.Expert,
                };
                var view = new HeadlessWeekLoopView(run, request, _database, trace);
                var stage = new AutoRunStageTrace
                {
                    Week = 1,
                    MetaRoute = MetaRoute.Normal,
                };
                RandomSnapshot before = run.Random.Capture();

                ActionChoice selected = view.PickAction(
                    new[] { Choice("act_event", 0.5f) },
                    MetaRoute.Normal,
                    stage,
                    out _);

                Assert.That(selected.Action.Id, Is.EqualTo("act_event"));
                Assert.That(stage.MetaRoute, Is.EqualTo(MetaRoute.Event));
                Assert.That(stage.Actions, Does.Contain("路线:Normal->Event"));
                AssertRandomSnapshotEqual(before, run.Random.Capture());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(affinity);
            }
        }

        [Test]
        public void ExpertRewardPolicy_ValuesGoldAmountAndPrioritizesLifeSavingItemWhenWounded()
        {
            GameRun run = CreateRun("survival-reward-value");
            run.TryLoseHearts(2, out _, out _);
            HeadlessWeekLoopView view = CreateView(run, AutoPlayerLevel.Expert, 1351);
            ArchetypeClassification archetype = BuildArchetypeClassifier.Classify(run);
            var goldGroup = new RewardChoiceGroup("金币", new[]
            {
                RewardChoice.Gold(10),
                RewardChoice.Gold(100),
            });
            var survivalGroup = new RewardChoiceGroup("装饰品", new[]
            {
                new RewardChoice(
                    cfg.RewardKind.PassiveItemChoice,
                    "item_gold_random",
                    "金币摆件",
                    string.Empty),
                new RewardChoice(
                    cfg.RewardKind.PassiveItemChoice,
                    "item_famous_knife",
                    "名刀展示架",
                    string.Empty),
            });

            int gold = view.PickReward(goldGroup, new[] { 0, 1 }, archetype, MetaRoute.Normal);
            int survival = view.PickReward(survivalGroup, new[] { 0, 1 }, archetype, MetaRoute.Normal);

            Assert.That(gold, Is.EqualTo(1));
            Assert.That(survival, Is.EqualTo(1));
            Assert.That(
                view.RewardValue(survivalGroup.Choices[1], archetype, MetaRoute.Normal),
                Is.GreaterThan(view.RewardValue(survivalGroup.Choices[0], archetype, MetaRoute.Normal)));
        }

        [Test]
        public void ExpertShopPolicy_RejectsPoorActiveValueButAcceptsLifeSavingValue()
        {
            GameRun run = CreateRun("survival-shop-value");
            run.TryLoseHearts(2, out _, out _);
            HeadlessWeekLoopView view = CreateView(run, AutoPlayerLevel.Expert, 1352);
            var lowValueActive = new ShopEntry(
                ShopEntryKind.ActiveItem,
                "item_active_half_next_action_cost",
                "加速行动单",
                string.Empty,
                basePrice: 500);
            var knife = new ShopEntry(
                ShopEntryKind.PassiveItem,
                "item_famous_knife",
                "名刀展示架",
                string.Empty,
                basePrice: 150);

            float activePriority = view.ShopPurchasePriority(
                lowValueActive,
                MetaRoute.Normal,
                out bool buyActive);
            float knifePriority = view.ShopPurchasePriority(
                knife,
                MetaRoute.Normal,
                out bool buyKnife);

            Assert.That(buyActive, Is.False, $"unexpected active priority {activePriority}");
            Assert.That(buyKnife, Is.True, $"unexpected knife priority {knifePriority}");
            Assert.That(knifePriority, Is.GreaterThan(activePriority));
        }

        [Test]
        public void ExpertShopDeletePolicy_UsesFormalServiceForAtMostOneWeakAcquiredDish()
        {
            GameRun run = CreateRun("survival-shop-delete-filler");
            run.Gold = 200;
            HeadlessWeekLoopView view = CreateView(run, AutoPlayerLevel.Expert, 1358);
            int initialRecipeCount = run.RecipeEntries.Count;
            Assert.That(run.AddBonusDish("ice_cream"), Is.True);
            RecipeBookSlot firstFiller = run.RecipeEntries.Last();
            Assert.That(run.AddBonusDish("ice_cream"), Is.True);
            RecipeBookSlot secondFiller = run.RecipeEntries.Last();
            int deleteCost = ShopService.DeleteCost(run);
            int goldBefore = run.Gold;
            var stage = new AutoRunStageTrace { Week = 1 };

            bool deleted = view.TryDeleteObviousAcquiredFiller(stage);

            Assert.That(deleted, Is.True);
            Assert.That(run.RecipeEntries, Has.Count.EqualTo(initialRecipeCount + 1));
            Assert.That(run.RecipeEntries.Contains(firstFiller), Is.False);
            Assert.That(run.RecipeEntries.Contains(secondFiller), Is.True);
            Assert.That(run.Gold, Is.EqualTo(goldBefore - deleteCost));
            Assert.That(run.DeleteDishCount, Is.EqualTo(1));
            Assert.That(run.CurrentShopDeleteDishCount, Is.EqualTo(1));
            Assert.That(stage.GoldSpent, Is.EqualTo(deleteCost));
            Assert.That(stage.Purchases, Is.EqualTo(new[] { "delete:ice_cream" }));

            Assert.That(view.TryDeleteObviousAcquiredFiller(stage), Is.False,
                "the formal per-shop deletion limit must prevent a second delete");
            Assert.That(run.RecipeEntries.Contains(secondFiller), Is.True);
            Assert.That(stage.Purchases, Has.Count.EqualTo(1));
        }

        [Test]
        public void ExpertShopDeletePolicy_PreservesInitialAndInvestedAcquiredDishes()
        {
            GameRun run = CreateRun("survival-shop-delete-protected");
            run.Gold = 500;
            HeadlessWeekLoopView view = CreateView(run, AutoPlayerLevel.Expert, 1359);
            List<RecipeBookSlot> initialEntries = run.RecipeEntries.ToList();

            Assert.That(run.AddBonusDish("ice_cream"), Is.True);
            RecipeBookSlot flat = run.RecipeEntries.Last();
            flat.AddScoreFlat(1);
            Assert.That(run.AddBonusDish("ice_cream"), Is.True);
            RecipeBookSlot multiplied = run.RecipeEntries.Last();
            multiplied.MultiplyScore(2);
            Assert.That(run.AddBonusDish("ice_cream"), Is.True);
            RecipeBookSlot flavored = run.RecipeEntries.Last();
            Assert.That(run.AddRecipeFlavor(run.RecipeEntries.Count - 1, "t_sweet"), Is.True);
            Assert.That(run.AddBonusDish("ice_cream"), Is.True);
            RecipeBookSlot skilled = run.RecipeEntries.Last();
            skilled.AddExtraSkill("sk_arrow_cookie_1");
            var stage = new AutoRunStageTrace { Week = 1 };
            int goldBefore = run.Gold;

            bool deleted = view.TryDeleteObviousAcquiredFiller(stage);

            Assert.That(deleted, Is.False);
            Assert.That(run.RecipeEntries, Has.Count.EqualTo(initialEntries.Count + 4));
            Assert.That(initialEntries.All(entry => run.RecipeEntries.Contains(entry)), Is.True,
                "initial recipe entries must never become delete candidates");
            Assert.That(run.RecipeEntries.Contains(flat), Is.True);
            Assert.That(run.RecipeEntries.Contains(multiplied), Is.True);
            Assert.That(run.RecipeEntries.Contains(flavored), Is.True);
            Assert.That(run.RecipeEntries.Contains(skilled), Is.True);
            Assert.That(run.Gold, Is.EqualTo(goldBefore));
            Assert.That(run.DeleteDishCount, Is.Zero);
            Assert.That(stage.Purchases, Is.Empty);
        }

        [Test]
        public void ShopDeletePolicy_RespectsNormalGoldAndNoRemoveGuards()
        {
            GameRun normal = CreateRun("survival-shop-delete-normal");
            normal.Gold = 500;
            HeadlessWeekLoopView normalView = CreateView(normal, AutoPlayerLevel.Normal, 1360);
            Assert.That(normal.AddBonusDish("ice_cream"), Is.True);
            Assert.That(
                normalView.TryDeleteObviousAcquiredFiller(new AutoRunStageTrace { Week = 1 }),
                Is.False);

            GameRun poor = CreateRun("survival-shop-delete-poor");
            poor.Gold = Math.Max(0, ShopService.DeleteCost(poor) - 1);
            HeadlessWeekLoopView poorView = CreateView(poor, AutoPlayerLevel.Expert, 1361);
            Assert.That(poor.AddBonusDish("ice_cream"), Is.True);
            Assert.That(ShopService.CanDeleteDish(poor), Is.False);
            Assert.That(
                poorView.TryDeleteObviousAcquiredFiller(new AutoRunStageTrace { Week = 1 }),
                Is.False);

            GameRun blocked = CreateRun("survival-shop-delete-blocked");
            blocked.Gold = 500;
            ItemAcquireResult acquired = blocked.AcquireItem(
                "item_no_remove",
                fallbackGold: 0,
                fireOnAcquire: false);
            Assert.That(acquired.Outcome, Is.EqualTo(ItemAcquireOutcome.Added));
            HeadlessWeekLoopView blockedView = CreateView(blocked, AutoPlayerLevel.Expert, 1362);
            Assert.That(blocked.AddBonusDish("ice_cream"), Is.True);
            Assert.That(ShopService.CanDeleteDish(blocked), Is.False);
            Assert.That(
                blockedView.TryDeleteObviousAcquiredFiller(new AutoRunStageTrace { Week = 1 }),
                Is.False);
        }

        [Test]
        public void ExposureIncreasingActionItems_AreHeldAtLowHeartsOrLowRecentPassRate()
        {
            GameRun run = CreateRun("survival-action-item-hold");
            HeadlessWeekLoopView view = CreateView(run, AutoPlayerLevel.Expert, 1353);
            ItemDefinition halfCost = ItemDefinition.Get(
                _tables,
                "item_active_half_next_action_cost",
                cfg.ItemKind.Active);
            ItemDefinition executeFuture = ItemDefinition.Get(
                _tables,
                "item_active_execute_future_node",
                cfg.ItemKind.Active);

            Assert.That(
                view.ShouldUseActionSelectActiveItem(halfCost, new AutoRunStageTrace { Week = 1 }),
                Is.True);
            Assert.That(
                view.ShouldUseActionSelectActiveItem(
                    executeFuture,
                    new AutoRunStageTrace { Week = 1, MealBattles = 4, MealPasses = 1 }),
                Is.False);

            run.TryLoseHearts(1, out _, out _);
            Assert.That(
                view.ShouldUseActionSelectActiveItem(halfCost, new AutoRunStageTrace { Week = 1 }),
                Is.False);
        }

        [Test]
        public void BattleActivePolicy_AtTwoHeartsUsesMultipleScoreHelpersAndReservesOneForBoss()
        {
            GameRun run = CreateRun("survival-active-reserve");
            Assert.That(run.TryLoseHearts(1, out _, out int hearts), Is.True);
            Assert.That(hearts, Is.EqualTo(2));
            AcquireActive(run, "item_active_lay_marble");
            AcquireActive(run, "item_active_lay_obsidian");
            AcquireActive(run, "item_active_lay_cherry");
            BattleSession session = BattleSessionFactory.Build(
                run,
                int.MaxValue,
                string.Empty,
                "survival-active-reserve-battle",
                string.Empty);
            HeadlessWeekLoopView view = CreateView(run, AutoPlayerLevel.Expert, 1354);
            var stage = new AutoRunStageTrace { Week = 1 };
            RandomSnapshot before = run.Random.Capture();

            view.UseBattleActiveItems(
                session,
                stage,
                beforePlacement: true,
                requiredScore: int.MaxValue,
                isBoss: false);

            Assert.That(stage.ActiveItemsUsed, Has.Count.EqualTo(2));
            Assert.That(stage.ActiveItemsUsed, Does.Contain("item_active_lay_marble"));
            Assert.That(stage.ActiveItemsUsed, Does.Contain("item_active_lay_obsidian"));
            Assert.That(run.ActiveItemCount, Is.EqualTo(1));
            Assert.That(run.HasItem("item_active_lay_cherry"), Is.True);
            Assert.That(
                session.DiningTable.ExistingCells()
                    .Count(cell => session.DiningTable.MaterialsAt(cell).Count > 0),
                Is.EqualTo(2),
                "successive material items should improve distinct cells instead of overwriting one cell");
            AssertRandomSnapshotEqual(before, run.Random.Capture());
        }

        [Test]
        public void BattleActivePolicy_FailedHighestRankedItemDoesNotCountOrBlockNextCandidate()
        {
            GameRun run = CreateRun("survival-active-failed-candidate");
            Assert.That(run.TryLoseHearts(2, out _, out int hearts), Is.True);
            Assert.That(hearts, Is.EqualTo(1));
            AcquireActive(run, "item_active_lay_marble");
            AcquireActive(run, "item_active_lay_obsidian");
            BattleSession session = BattleSessionFactory.Build(
                run,
                int.MaxValue,
                string.Empty,
                "survival-active-failed-candidate-battle",
                string.Empty);
            foreach (GridPos cell in session.DiningTable.ExistingCells())
            {
                Assert.That(session.DiningTable.SetMaterialAt(cell, "m_marble"), Is.True);
            }

            HeadlessWeekLoopView view = CreateView(run, AutoPlayerLevel.Expert, 1355);
            var stage = new AutoRunStageTrace { Week = 1 };

            view.UseBattleActiveItems(
                session,
                stage,
                beforePlacement: true,
                requiredScore: int.MaxValue,
                isBoss: false);

            Assert.That(stage.ActiveItemsUsed, Is.EqualTo(new[] { "item_active_lay_obsidian" }));
            Assert.That(run.HasItem("item_active_lay_marble"), Is.True);
            Assert.That(run.HasItem("item_active_lay_obsidian"), Is.False);
        }

        [Test]
        public void BattleActivePolicy_RanksCurrentScoreHelpAboveEconomyOrServeOrder()
        {
            GameRun run = CreateRun("survival-active-score-order");
            HeadlessWeekLoopView view = CreateView(run, AutoPlayerLevel.Expert, 1356);
            ItemDefinition marble = Active("item_active_lay_marble");
            ItemDefinition gold = Active("item_active_lay_gold");
            ItemDefinition salty = Active("item_active_season_salty");
            ItemDefinition fresh = Active("item_active_season_umami");

            Assert.That(view.BattleActiveScoreHelp(marble), Is.GreaterThan(0d));
            Assert.That(view.BattleActiveScoreHelp(gold), Is.Zero);
            Assert.That(view.BattleActiveScoreHelp(salty), Is.GreaterThan(0d));
            Assert.That(view.BattleActiveScoreHelp(fresh), Is.Zero);
        }

        [Test]
        public void ExpertEventPolicy_PicksHeartRecoveryWhenWoundedAndPermanentScoreAtFullHealth()
        {
            GameRun wounded = CreateRun("survival-event-heal");
            wounded.TryLoseHearts(1, out _, out _);
            List<cfg.EventOption> chuka = EventService.GetRootOptions(
                wounded,
                "ev_chuka_ichiban_trial");

            HeadlessEventOptionDecision heal = HeadlessEventOptionPolicy.Pick(
                wounded,
                "最后一灶也会发光",
                chuka.Select(option => EventService.FormatRuntimeText(wounded, option.Text)).ToList(),
                chuka.Select(_ => true).ToList(),
                AutoPlayerLevel.Expert,
                new Xoshiro256SS(1601));

            Assert.That(heal.OptionId, Is.EqualTo("opt_chuka_ichiban_dumpling"));
            Assert.That(heal.Reason, Does.Contain("最高事件效用"));
            Assert.That(heal.Reason, Does.Contain("心=2/3"));

            GameRun full = CreateRun("survival-event-permanent-score");
            List<cfg.EventOption> twinPeaks = EventService.GetRootOptions(
                full,
                "ev_twin_peaks_diner");
            HeadlessEventOptionDecision permanent = HeadlessEventOptionPolicy.Pick(
                full,
                "双R餐厅的夜晚",
                twinPeaks.Select(option => EventService.FormatRuntimeText(full, option.Text)).ToList(),
                twinPeaks.Select(_ => true).ToList(),
                AutoPlayerLevel.Expert,
                new Xoshiro256SS(1602));

            Assert.That(permanent.OptionId, Is.EqualTo("opt_twin_peaks_log"));
        }

        [Test]
        public void NormalEventPolicy_IsDeterministicAndDoesNotConsumeRuleRandom()
        {
            GameRun first = CreateRun("survival-event-normal");
            GameRun second = CreateRun("survival-event-normal");
            List<cfg.EventOption> options = EventService.GetRootOptions(first, "ev_midnight_tasting");
            List<string> text = options
                .Select(option => EventService.FormatRuntimeText(first, option.Text))
                .ToList();
            List<bool> enabled = options.Select(_ => true).ToList();
            RandomSnapshot firstBefore = first.Random.Capture();
            RandomSnapshot secondBefore = second.Random.Capture();

            HeadlessEventOptionDecision firstDecision = HeadlessEventOptionPolicy.Pick(
                first,
                "午夜食堂",
                text,
                enabled,
                AutoPlayerLevel.Normal,
                new Xoshiro256SS(1603));
            HeadlessEventOptionDecision secondDecision = HeadlessEventOptionPolicy.Pick(
                second,
                "午夜食堂",
                text,
                enabled,
                AutoPlayerLevel.Normal,
                new Xoshiro256SS(1603));

            Assert.That(secondDecision.OptionId, Is.EqualTo(firstDecision.OptionId));
            Assert.That(secondDecision.Reason, Is.EqualTo(firstDecision.Reason));
            AssertRandomSnapshotEqual(firstBefore, first.Random.Capture());
            AssertRandomSnapshotEqual(secondBefore, second.Random.Capture());
        }

        [Test]
        public void PendingFragment_UsesLocalBoundsFormalValidationAndCommitsCenteredTableAttachment()
        {
            GameRun run = CreateRun("survival-centered-fragment");
            cfg.Character character = _tables.TbCharacter.Get(run.CharacterId);
            DiningTable board = run.BuildTablePreviewFromFragments();
            HashSet<GridPos> existing = TableFragmentBuilder.ToExistingSet(board);
            TableFragmentDef fragment = _database.GetFragment("frag_4_1");
            Assert.That(fragment, Is.Not.Null);
            Assert.That(existing.Min(cell => cell.X), Is.GreaterThan(character.MaxDiningTableWidth));

            int legacyLegal = 0;
            for (int y = -character.MaxDiningTableHeight; y <= character.MaxDiningTableHeight; y++)
            {
                for (int x = -character.MaxDiningTableWidth; x <= character.MaxDiningTableWidth; x++)
                {
                    if (TableFragmentBuilder.CanPlaceFragmentAt(
                        existing,
                        fragment,
                        0,
                        new GridPos(x, y),
                        character.MaxDiningTableWidth,
                        character.MaxDiningTableHeight))
                    {
                        legacyLegal++;
                    }
                }
            }

            Assert.That(legacyLegal, Is.Zero, "the old absolute 0..max validator cannot reach the centered table");
            run.SetPendingFragmentPack(new[] { fragment.Id }, new[] { 0 });
            HeadlessWeekLoopView view = CreateView(run, AutoPlayerLevel.Expert, 1357);

            bool resolved = view.ResolvePendingFragment();

            Assert.That(resolved, Is.True);
            Assert.That(run.PendingFragmentPack, Is.Empty);
            Assert.That(run.FragmentPlacements, Has.Count.EqualTo(1));
            TableFragmentPlacement placement = run.FragmentPlacements.Single();
            Assert.That(placement.FragmentId, Is.EqualTo(fragment.Id));
            DiningTable rebuilt = run.BuildTablePreviewFromFragments();
            Assert.That(
                rebuilt.CellCapacity,
                Is.EqualTo(board.CellCapacity + TableFragmentBuilder.FilledCells(fragment).Count),
                "the persisted placement must add real cells when the table is rebuilt");
            Assert.That(
                TableFragmentBuilder.GetFragmentPlacementStatusWithinMaxBounds(
                    existing,
                    fragment,
                    placement.Origin,
                    character.MaxDiningTableWidth,
                    character.MaxDiningTableHeight),
                Is.EqualTo(TableFragmentBuilder.FragmentPlacementStatus.Valid));

            int sharedEdges = 0;
            foreach (GridPos local in TableFragmentBuilder.FilledCells(fragment))
            {
                GridPos cell = local.Offset(placement.Origin.X, placement.Origin.Y);
                if (existing.Contains(cell.Offset(-1, 0))) sharedEdges++;
                if (existing.Contains(cell.Offset(1, 0))) sharedEdges++;
                if (existing.Contains(cell.Offset(0, -1))) sharedEdges++;
                if (existing.Contains(cell.Offset(0, 1))) sharedEdges++;
            }

            Assert.That(sharedEdges, Is.EqualTo(2), "Expert should attach the 2x2 fragment flush to a table side");
        }

        [Test]
        public void OpenWeekMap_StoresDecisionReasonSeparatelyAndLeavesNodeTracePrefixUntouched()
        {
            GameRun run = CreateRun("survival-trace");
            var trace = new AutoRunTrace
            {
                Seed = 1401,
                CharacterId = _characterId,
                PlayerLevel = AutoPlayerLevel.Expert,
            };
            var view = new HeadlessWeekLoopView(
                run,
                Request(AutoPlayerLevel.Expert, 1401),
                _database,
                trace);

            view.OpenWeekMap();
            view.ShowTimelineNodeCard(null, null, () => { });

            List<string> actions = trace.Stages.Single().Actions;
            Assert.That(actions.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(actions[0], Does.Not.StartWith("决策:"));
            Assert.That(actions[0], Does.Not.StartWith("节点:"));
            Assert.That(actions[1], Does.StartWith("决策:Expert|"));
            Assert.That(actions.Last(), Is.EqualTo("节点:"));

            AutoRunActionDecisionTrace decision = trace.Stages.Single().ActionDecisions.Single();
            Assert.That(decision.OfferKey, Is.Not.Empty);
            Assert.That(decision.Week, Is.EqualTo(1));
            Assert.That(decision.CandidateActionIds, Is.Not.Empty);
            Assert.That(decision.CandidateCosts.Count, Is.EqualTo(decision.CandidateActionIds.Count));
            Assert.That(decision.CandidatePolicyWeights.Count, Is.EqualTo(decision.CandidateActionIds.Count));
            Assert.That(decision.SelectedIndex, Is.InRange(0, decision.CandidateActionIds.Count - 1));
            Assert.That(decision.SelectedActionId, Is.EqualTo(decision.CandidateActionIds[decision.SelectedIndex]));
            Assert.That(decision.SelectionReason, Does.Contain("候选=["));
            Assert.That(decision.CandidatePolicyWeights.Sum(), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void BattleTrace_ReadsHeartsAfterFormalBossSettlement()
        {
            GameRun run = CreatePreparedTimelineRun(
                "survival-terminal-boss",
                currentDay: 4f,
                scoreFlat: -1000000,
                heartsToLoseBeforeRun: 2);
            AutoRunTrace trace = RunUntil(
                run,
                AutoPlayerLevel.Expert,
                1501,
                value => value.Stages.SelectMany(stage => stage.Battles).Any(battle => battle.TerminalDeath));

            AutoRunBattleTrace battle = trace.Stages
                .SelectMany(stage => stage.Battles)
                .Single(value => value.IsBoss && Math.Abs(value.Day - 4f) < 0.001f);
            Assert.That(battle.TargetHit, Is.False);
            Assert.That(battle.HeartsBefore, Is.EqualTo(1));
            Assert.That(battle.HeartsAfter, Is.Zero);
            Assert.That(battle.HeartsLost, Is.EqualTo(1));
            Assert.That(battle.Survived, Is.False);
            Assert.That(battle.TerminalDeath, Is.True);
            Assert.That(battle.BattleKey, Is.Not.Empty);
            Assert.That(battle.EncounterKey, Does.Contain("cfg_tl_1_d4_1"));
            Assert.That(battle.SourceKey, Is.EqualTo("cfg_tl_1_d4_1"));
            Assert.That(battle.PlacementDecisionCount, Is.GreaterThan(0));
        }

        [Test]
        public void FixedDay4AndDay7Bosses_HaveStableDistinctEncounterKeysAcrossSeeds()
        {
            List<AutoRunBattleTrace> first = RunStrongWeekOneBosses("survival-boss-encounters-a", 1601);
            List<AutoRunBattleTrace> second = RunStrongWeekOneBosses("survival-boss-encounters-b", 1602);

            Assert.That(first.Select(value => value.Day), Is.EqualTo(new[] { 4f, 7f }));
            Assert.That(second.Select(value => value.Day), Is.EqualTo(new[] { 4f, 7f }));
            Assert.That(first[0].EncounterKey, Is.Not.EqualTo(first[1].EncounterKey));
            Assert.That(second.Select(value => value.EncounterKey),
                Is.EqualTo(first.Select(value => value.EncounterKey)));
            Assert.That(first.All(value => !value.EncounterKey.Contains(value.BossDebuffId ?? string.Empty)
                                           || string.IsNullOrEmpty(value.BossDebuffId)),
                Is.True,
                "Encounter identity must not be split by rolled debuff");
            Assert.That(first.Select(value => value.BattleKey).Distinct().Count(), Is.EqualTo(2));
        }

        private List<AutoRunBattleTrace> RunStrongWeekOneBosses(string seed, int policySeed)
        {
            GameRun run = CreatePreparedTimelineRun(
                seed,
                currentDay: 7f,
                scoreFlat: 1000000,
                heartsToLoseBeforeRun: 0);
            AutoRunTrace trace = RunUntil(
                run,
                AutoPlayerLevel.Expert,
                policySeed,
                value => value.Stages
                    .SelectMany(stage => stage.Battles)
                    .Count(battle => battle.Week == 1 && battle.IsBoss) >= 2);
            return trace.Stages
                .SelectMany(stage => stage.Battles)
                .Where(battle => battle.Week == 1
                    && battle.IsBoss
                    && (Math.Abs(battle.Day - 4f) < 0.001f || Math.Abs(battle.Day - 7f) < 0.001f))
                .OrderBy(battle => battle.Day)
                .ToList();
        }

        private GameRun CreatePreparedTimelineRun(
            string seed,
            float currentDay,
            int scoreFlat,
            int heartsToLoseBeforeRun)
        {
            GameRun run = CreateRun(seed);
            foreach (RecipeBookSlot slot in run.RecipeEntries)
            {
                slot.AddScoreFlat(scoreFlat);
            }

            if (heartsToLoseBeforeRun > 0)
            {
                run.TryLoseHearts(heartsToLoseBeforeRun, out _, out _);
            }

            TimelineService.RollWeekTimeline(
                run,
                run.Random.DomainStream(SeedDomains.Map, "survival-policy-prepared-w1"));
            run.CurrentDay = currentDay;
            return run;
        }

        private AutoRunTrace RunUntil(
            GameRun run,
            AutoPlayerLevel level,
            int policySeed,
            Func<AutoRunTrace, bool> stop)
        {
            var trace = new AutoRunTrace
            {
                Seed = policySeed,
                CharacterId = _characterId,
                PlayerLevel = level,
            };
            var view = new HeadlessWeekLoopView(
                run,
                Request(level, policySeed),
                _database,
                trace);
            view.Begin();
            int guard = 0;
            while (!view.IsCompleted && !stop(trace) && ++guard <= 2000)
            {
                view.Step(8);
            }

            Assert.That(guard, Is.LessThanOrEqualTo(2000), "headless prepared run did not reach expected trace point");
            Assert.That(stop(trace), Is.True, "headless prepared run ended before expected trace point");
            if (!view.IsCompleted)
            {
                view.Cancel();
            }

            return trace;
        }

        private HeadlessWeekLoopView CreateView(GameRun run, AutoPlayerLevel level, int seed)
        {
            return new HeadlessWeekLoopView(
                run,
                Request(level, seed),
                _database,
                new AutoRunTrace
                {
                    Seed = seed,
                    CharacterId = _characterId,
                    PlayerLevel = level,
                });
        }

        private AutoRunRequest Request(AutoPlayerLevel level, int seed)
        {
            return new AutoRunRequest
            {
                CharacterId = _characterId,
                PlayerLevel = level,
                Seed = seed,
                Policy = new AutoPlayerPolicy(),
            };
        }

        private ActionChoice Choice(string actionId, float costDays)
        {
            return new ActionChoice(
                _tables.TbAction.Get(actionId),
                "survival-policy-test",
                0,
                0,
                costDays);
        }

        private ItemDefinition Active(string itemId)
        {
            ItemDefinition item = ItemDefinition.Get(_tables, itemId, cfg.ItemKind.Active);
            Assert.That(item, Is.Not.Null, itemId);
            return item;
        }

        private static void AcquireActive(GameRun run, string itemId)
        {
            ItemAcquireResult result = run.AcquireItem(itemId, fallbackGold: 0, fireOnAcquire: false);
            Assert.That(
                result.Outcome,
                Is.EqualTo(ItemAcquireOutcome.Stacked),
                $"failed to acquire {itemId}: {result.Outcome}");
        }

        private GameRun CreateRun(string seed)
        {
            return new GameRun(
                _tables,
                _database,
                _characterId,
                seed,
                execution: RunExecutionEnvironment.CreateIsolated(_tables, seed));
        }

        private static void AssertRandomSnapshotEqual(RandomSnapshot expected, RandomSnapshot actual)
        {
            Assert.That(actual.SeedText, Is.EqualTo(expected.SeedText));
            Assert.That(actual.MasterSeed, Is.EqualTo(expected.MasterSeed));
            Assert.That(actual.Streams.Keys, Is.EquivalentTo(expected.Streams.Keys));
            foreach (KeyValuePair<string, RngState> pair in expected.Streams)
            {
                Assert.That(actual.Streams[pair.Key], Is.EqualTo(pair.Value), pair.Key);
            }
        }
    }
}
