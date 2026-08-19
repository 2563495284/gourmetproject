using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BreakInfinity;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Orchestration;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    /// <summary>
    /// Balance Lab 依赖的规则同源契约。这里直接驱动正式 WeekLoopController、
    /// BattleSettlementApplier 与 ShopSession，避免再为 GM 复制一套规则断言。
    /// </summary>
    public sealed class BalanceRuleParityTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            string configDirectory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name =>
                JSON.Parse(File.ReadAllText(Path.Combine(configDirectory, name + ".json"))));
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [TestCase(false, 1)]
        [TestCase(true, 1)]
        public void FormalWeekLoop_FailedBattleLosesConfiguredHeartCount(bool isBoss, int expectedLoss)
        {
            GameRun run = CreateRun($"heart-loss-{isBoss}");
            var view = new RecordingWeekLoopView();
            WeekLoopController loop = CreateBattleLoop(run, view, isBoss);
            int before = run.HeartsRemaining;

            loop.OnBattleSettled(EmptyScore(), isWin: false, finalHappyCakeLayers: 0);

            Assert.That(run.HeartsRemaining, Is.EqualTo(before - expectedLoss));
            Assert.That(view.HeartBreak, Is.Not.Null);
            Assert.That(view.HeartBreak.BeforeHeartCount, Is.EqualTo(before));
            Assert.That(view.HeartBreak.AfterHeartCount, Is.EqualTo(before - expectedLoss));
            Assert.That(view.NoticeCount, Is.Zero);
        }

        [Test]
        public void FormalWeekLoop_UndyingConsumesKnifeAndKeepsHeartsAndRewardFlow()
        {
            GameRun run = CreateRun("undying-boss");
            Assert.That(run.TryLoseHearts(2, out _, out _), Is.True);
            Assert.That(run.HeartsRemaining, Is.EqualTo(1));
            Assert.That(
                run.AcquireItem("item_famous_knife", fallbackGold: 0, fireOnAcquire: false).Outcome,
                Is.EqualTo(ItemAcquireOutcome.Added));
            var view = new RecordingWeekLoopView();
            WeekLoopController loop = CreateBattleLoop(run, view, isBoss: true);

            loop.OnBattleSettled(EmptyScore(), isWin: false, finalHappyCakeLayers: 0);

            Assert.That(run.HeartsRemaining, Is.EqualTo(1));
            Assert.That(run.HasItem("item_famous_knife"), Is.False);
            Assert.That(view.HeartBreak, Is.Null);
            Assert.That(view.NoticeCount, Is.EqualTo(1));
            Assert.That(run.HasPendingRewardOffer, Is.True,
                "不死只替换失败结果，正式营业奖励仍应按存活失败路径生成。");
        }

        [Test]
        public void BattleSettlementApplier_WritesGrowthRemovalGoldAndCountsExactlyOnce()
        {
            GameRun run = CreateRun("shared-settlement");
            Assert.That(run.RecipeEntries.Count, Is.GreaterThanOrEqualTo(2));
            int originalRecipeCount = run.RecipeEntries.Count;
            int goldBefore = run.Gold;
            BattleSession session = CreateSettlementSession(run);

            ScoreResult score = session.Settle();

            Assert.That(score, Is.Not.Null);
            Assert.That(session.LastRecipeScoreFlatDeltas, Has.Count.EqualTo(2));
            Assert.That(session.LastRecipeScoreMultiplierDeltas, Has.Count.EqualTo(2));
            Assert.That(session.LastRecipeRemovalOutcomes, Has.Count.EqualTo(1));
            Assert.That(session.LastRecipeRemovalOutcomes[0].Removed, Is.True);

            Assert.That(BattleSettlementApplier.ApplyRecipeGrowth(run, session), Is.True);
            Assert.That(run.RecipeEntries[0].ScoreFlatBonus.ToDouble(), Is.EqualTo(7d).Within(0.0001d));
            Assert.That(run.RecipeEntries[1].ScoreFlatBonus.ToDouble(), Is.EqualTo(7d).Within(0.0001d));
            Assert.That(run.RecipeEntries[0].ScoreMultiplier.ToDouble(), Is.EqualTo(1.5d).Within(0.0001d));
            Assert.That(run.RecipeEntries[1].ScoreMultiplier.ToDouble(), Is.EqualTo(1.5d).Within(0.0001d));

            BattleRunSettlement applied = BattleSettlementApplier.ApplyFinal(run, session);
            Assert.That(applied.Applied, Is.True);
            Assert.That(applied.GoldDelta, Is.EqualTo(4));
            Assert.That(applied.RemovedRecipeIndices, Is.EqualTo(new[] { 1 }));
            Assert.That(run.Gold, Is.EqualTo(goldBefore + 4));
            Assert.That(run.RecipeEntries.Count, Is.EqualTo(originalRecipeCount - 1));
            Assert.That(run.RunSettledCounts["balance_settlement_base_0"], Is.EqualTo(1));
            Assert.That(run.RunSettledCounts["balance_settlement_base_1"], Is.EqualTo(1));

            Assert.That(BattleSettlementApplier.ApplyRecipeGrowth(run, session), Is.False);
            BattleRunSettlement duplicate = BattleSettlementApplier.ApplyFinal(run, session);
            Assert.That(duplicate.Applied, Is.False);
            Assert.That(run.Gold, Is.EqualTo(goldBefore + 4));
            Assert.That(run.RecipeEntries.Count, Is.EqualTo(originalRecipeCount - 1));
            Assert.That(run.RunSettledCounts["balance_settlement_base_0"], Is.EqualTo(1));
            Assert.That(run.RunSettledCounts["balance_settlement_base_1"], Is.EqualTo(1));
        }

        [Test]
        public void ShopSession_PreservesPendingStockAndUsesFormalFragmentAndOnAcquirePaths()
        {
            GameRun run = CreateRun("shop-shared-session");
            run.Gold = 1000;
            string key = GameRun.BuildShopKey(run.WeekIndex, run.CurrentDay);
            run.SetPendingShopStock(key, new[]
            {
                new ShopEntry(
                    ShopEntryKind.PassiveItem,
                    "item_block_active",
                    "封条备品柜",
                    string.Empty,
                    basePrice: 80,
                    slotIndex: 0),
                new ShopEntry(
                    ShopEntryKind.Fragment,
                    "fragment_pack",
                    "碎片包",
                    string.Empty,
                    basePrice: 10,
                    slotIndex: 0),
            });
            var session = new ShopSession(run);
            Assert.That(session.Stock, Has.Count.EqualTo(2));

            ShopPurchaseResult passive = session.Purchase(
                session.Stock.Single(entry => entry.Kind == ShopEntryKind.PassiveItem));

            Assert.That(passive.Success, Is.True, passive.Error);
            Assert.That(run.HasItem("item_block_active"), Is.True);
            Assert.That(run.Gold, Is.EqualTo(passive.GoldBefore - passive.Price + 400),
                "购买必须经 AcquireItem 触发正式 OnAcquire，而不是仅把 id 塞入背包。");
            Assert.That(session.Stock.Single(entry => entry.Kind == ShopEntryKind.PassiveItem).IsStocked, Is.False);

            ShopPurchaseResult fragment = session.Purchase(
                session.Stock.Single(entry => entry.Kind == ShopEntryKind.Fragment));

            Assert.That(fragment.Success, Is.True, fragment.Error);
            Assert.That(run.PendingFragmentPack, Is.Not.Empty);
            Assert.That(run.CurrentShopFragmentPackPurchaseCount, Is.EqualTo(1));
            Assert.That(run.Gold, Is.EqualTo(fragment.GoldBefore - fragment.Price));

            var restoredSession = new ShopSession(run);
            Assert.That(restoredSession.Stock, Has.Count.EqualTo(2));
            Assert.That(restoredSession.Stock.All(entry => !entry.IsStocked), Is.True,
                "同一次商店访问必须恢复 pending 库存，不能重新刷新已购买槽位。");
        }

        [Test]
        public void FormalEventPage_KeepsUnsatisfiedOptionsVisibleButDisabled()
        {
            GameRun run = CreateRun("event-disabled-option");
            run.Gold = 0;
            var view = new RecordingWeekLoopView();
            var loop = new WeekLoopController(run, view);

            Assert.That(loop.ExecuteEventImmediately("ev_kitchen_god_statue"), Is.True);

            List<cfg.EventOption> roots = EventService.GetRootOptions(run, "ev_kitchen_god_statue");
            int goldIndex = roots.FindIndex(option => option.Id == "opt_statue_gold");
            int dishIndex = roots.FindIndex(option => option.Id == "opt_statue_dish");
            Assert.That(goldIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(dishIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(view.EventOptions, Has.Count.EqualTo(roots.Count));
            Assert.That(view.EventOptionEnabled[goldIndex], Is.False);
            Assert.That(view.EventOptionEnabled[dishIndex], Is.True);
        }

        [Test]
        public void ActiveItemEligibility_IsSharedAndHonorsBlockActivePassive()
        {
            GameRun run = CreateRun("active-eligibility");
            ItemAcquireOutcome activeOutcome = run.AcquireItem(
                "item_active_season_sweet",
                fallbackGold: 0,
                fireOnAcquire: false).Outcome;
            Assert.That(
                activeOutcome == ItemAcquireOutcome.Added || activeOutcome == ItemAcquireOutcome.Stacked,
                Is.True);
            ItemDefinition active = ItemDefinition.Get(
                _tables,
                "item_active_season_sweet",
                cfg.ItemKind.Active);

            Assert.That(
                ItemActiveUsage.CanUse(run, active, ActiveUseContextKind.Battle, out string initialReason),
                Is.True,
                initialReason);
            ItemAcquireOutcome blockerOutcome = run.AcquireItem(
                "item_block_active",
                fallbackGold: 0,
                fireOnAcquire: false).Outcome;
            Assert.That(
                blockerOutcome == ItemAcquireOutcome.Added || blockerOutcome == ItemAcquireOutcome.Stacked,
                Is.True);

            Assert.That(
                ItemActiveUsage.CanUse(run, active, ActiveUseContextKind.Battle, out string blockedReason),
                Is.False);
            Assert.That(blockedReason, Does.Contain("禁止使用"));
        }

        private WeekLoopController CreateBattleLoop(
            GameRun run,
            RecordingWeekLoopView view,
            bool isBoss)
        {
            cfg.GameAction action = _tables.TbAction.Get(isBoss ? "act_boss" : "act_food_gold");
            run.SetLastActionContext(new ActionExecutionContext(action));
            var loop = new WeekLoopController(run, view);
            typeof(WeekLoopController)
                .GetField("_currentBattleIsBoss", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(loop, isBoss);
            return loop;
        }

        private BattleSession CreateSettlementSession(GameRun run)
        {
            const string firstId = "balance_settlement_dish_0";
            const string secondId = "balance_settlement_dish_1";
            SkillDef grantGold = Skill(
                "balance_grant_gold",
                new SkillRuleDef(
                    "balance_grant_gold_rule",
                    "balance_grant_gold",
                    0,
                    SkillTrigger.OnSettle,
                    SkillConditionType.None,
                    SkillScope.Self,
                    CountUnit.Instances,
                    CountMode.Gate,
                    string.Empty,
                    SkillActionType.GrantGold,
                    SkillScope.Self,
                    0,
                    new[] { 4f },
                    Array.Empty<string>()));
            SkillDef removeSelf = Skill(
                "balance_remove_self",
                new SkillRuleDef(
                    "balance_remove_self_rule",
                    "balance_remove_self",
                    0,
                    SkillTrigger.OnSettle,
                    SkillConditionType.None,
                    SkillScope.Self,
                    CountUnit.Instances,
                    CountMode.Gate,
                    string.Empty,
                    SkillActionType.RequestRecipeRemoval,
                    SkillScope.Self,
                    0,
                    new[] { 1f },
                    Array.Empty<string>()));
            var first = new DishDef(
                firstId,
                firstId,
                10,
                DishShape.FromRows(new[] { "X" }),
                0,
                0,
                1f,
                new[] { grantGold.Id },
                string.Empty,
                baseId: "balance_settlement_base_0");
            var second = new DishDef(
                secondId,
                secondId,
                10,
                DishShape.FromRows(new[] { "X" }),
                0,
                0,
                1f,
                new[] { removeSelf.Id },
                string.Empty,
                baseId: "balance_settlement_base_1");
            var database = new GameplayDatabase(
                new[] { first, second },
                new[] { grantGold, removeSelf },
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var scoreSource = new ItemScoreEffectSource(new[]
            {
                new ItemScoreSpec(ItemScoreEffectType.PermanentAddFlatAll, 7f, string.Empty, "flat", "flat"),
                new ItemScoreSpec(ItemScoreEffectType.PermanentAddMultAll, 0.5f, string.Empty, "mult", "mult"),
            });
            var session = new BattleSession(
                new DiningTable(2, 1),
                database,
                new Xoshiro256SS(7711UL),
                new[]
                {
                    new RecipeSlot("first", new[]
                    {
                        new RecipeSlotEntry(firstId, null, null, 1f, 0f, 0, 0),
                    }),
                    new RecipeSlot("second", new[]
                    {
                        new RecipeSlotEntry(secondId, null, null, 1f, 0f, 0, 1),
                    }),
                },
                requiredScore: 0,
                calculator: new ScoreCalculator(effectSources: new[] { scoreSource }));

            PlacePrepared(session, 0);
            PlacePrepared(session, 1);
            return session;
        }

        private static void PlacePrepared(BattleSession session, int slotIndex)
        {
            ServePrepareResult prepared = session.PrepareServeAutomatically(slotIndex);
            Assert.That(prepared.Success, Is.True);
            Assert.That(prepared.PreparedDish.Placements, Is.Not.Empty);
            ServeResult placed = session.CommitPreparedServe(prepared.PreparedDish.Placements[0]);
            Assert.That(placed.Success, Is.True);
        }

        private static SkillDef Skill(string id, SkillRuleDef rule)
            => new SkillDef(
                id,
                id,
                string.Empty,
                Array.Empty<string>(),
                new[] { rule });

        private GameRun CreateRun(string seed)
            => new GameRun(
                _tables,
                _database,
                "glutton_dog",
                seed,
                execution: RunExecutionEnvironment.CreateIsolated(_tables, seed));

        private static ScoreResult EmptyScore()
            => new ScoreResult(Array.Empty<DishScore>(), 0f, 0f, 1f);

        private sealed class RecordingWeekLoopView : IWeekLoopView
        {
            public BigDouble LastBattleTotal => BigDouble.Zero;

            public HeartBreakFormOpenArgs HeartBreak { get; private set; }

            public int NoticeCount { get; private set; }

            public IReadOnlyList<string> EventOptions { get; private set; } = Array.Empty<string>();

            public IReadOnlyList<bool> EventOptionEnabled { get; private set; } = Array.Empty<bool>();

            public void HideBattleWorld() { }

            public void ResetBossBattlePresentation() { }

            public void SavePendingRewardBattleView() { }

            public void RestorePendingRewardBattleView() { }

            public void HideResultPanel() { }

            public void OpenWeekMap() { }

            public void OpenShop() { }

            public void OpenRewardForm(RewardFormOpenArgs args) { }

            public void ShowTimelineNodeCard(cfg.TimelineNode node, int? interestMaxGain, Action onPick) { }

            public void DismissTimelineNodeCard(Action onDone) => onDone?.Invoke();

            public void ShowTimelineNodeSkipped(
                cfg.TimelineNode node,
                TimelineMutationResult result,
                Action onDone) { }

            public void PlayTimelineAdvance(
                float fromDay,
                float toDay,
                string arrivingNodeId,
                Action onDone) { }

            public void BeginTimelineAdvanceSequence(float fromDay) { }

            public void EndTimelineAdvanceSequence() { }

            public void PlayTimelineNodeCue(
                string nodeId,
                TimelinePresentationCueKind kind,
                Action onDone) { }

            public void StartBattle(
                int requiredScore,
                string modifier,
                string key,
                string bossDebuffId,
                ActionExecutionContext actionContext) { }

            public void ShowNotice(string title, string message, Action onContinue)
            {
                NoticeCount++;
            }

            public void ShowHeartBreak(HeartBreakFormOpenArgs args, Action onComplete)
            {
                HeartBreak = args;
            }

            public void OpenEventRecipeDishDelete(
                GameRun run,
                string title,
                Action onCancel,
                Action<ActiveTarget> onTargetConfirmed,
                Action onChanged) { }

            public void ShowEventPage(
                string title,
                string desc,
                string resultButtonText,
                string bgSprite,
                IReadOnlyList<string> options,
                IReadOnlyList<string> optionRequirements,
                IReadOnlyList<bool> optionEnabled,
                Action<int> onPick,
                Action onEnd)
            {
                EventOptions = options;
                EventOptionEnabled = optionEnabled;
            }

            public void ShowEventEffectFeedback(
                string title,
                string message,
                string bgSprite,
                Action onComplete) { }

            public void ExitEventPage(Action onExited) { }

            public void ShowEventRecipeMutation(RecipeMutationResult result, Action onComplete) { }

            public void ShowDirectPassiveItemAcquire(
                ItemDefinition item,
                ItemAcquireResult acquireResult,
                Action onComplete) { }

            public void ShowRunResult(bool win, BigDouble total) { }
        }
    }
}
