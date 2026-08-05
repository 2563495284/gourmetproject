using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Orchestration;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Scoring;
using GourmetProject.Runtime;
using NUnit.Framework;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class HeartFailureFlowPlayModeTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);

            var random = new RandomService();
            random.Init("heart-failure-flow-tests");
            typeof(GameApp)
                .GetProperty(nameof(GameApp.Random))
                ?.GetSetMethod(nonPublic: true)
                ?.Invoke(null, new object[] { random });
        }

        [TestCase(cfg.FoodActionKind.Normal)]
        [TestCase(cfg.FoodActionKind.Super)]
        [TestCase(cfg.FoodActionKind.Feast)]
        public void NonTerminalFoodFailure_SavesHeartBreakAndKeepsRewardPending(
            cfg.FoodActionKind actionKind)
        {
            GameRun run = CreateRun();
            run.AcquireItem("item_extra_food_choice", fallbackGold: 0);
            cfg.GameAction action = ActionOfKind(actionKind);
            var context = new ActionExecutionContext(
                action,
                0,
                0,
                "heart-failure-flow",
                action.MinCostDays);
            run.SetLastActionContext(context);
            string rewardKey = GameRun.BuildRewardKey(run.WeekIndex, run.CurrentDay, context);
            run.SetPendingRewardOffer(
                rewardKey,
                new RewardOffer(
                    0,
                    Array.Empty<RewardChoice>(),
                    Array.Empty<RewardChoice>(),
                    baseGoldClaimed: true));
            var view = new RecordingLoopView(run);
            var controller = new WeekLoopController(run, view);

            using (RunPersistence.SuppressSave())
            {
                controller.OnBattleSettled(Result(42), isWin: false, finalHappyCakeLayers: 0);
            }

            PendingHeartBreakSaveData pending = run.GetPendingHeartBreak();
            Assert.That(run.HeartsRemaining, Is.EqualTo(2));
            Assert.That(pending, Is.Not.Null);
            Assert.That(pending.IsTerminal, Is.False);
            Assert.That(pending.BattleTotal, Is.EqualTo(42));
            Assert.That(run.HasPendingRewardOffer, Is.True);
            Assert.That(view.HeartBreakArgs, Is.Not.Null);
            Assert.That(view.RunResultShown, Is.False);
            Assert.That(
                run.PassiveModels.Single(model => model.ItemId == "item_extra_food_choice").InfoText,
                Is.EqualTo(actionKind == cfg.FoodActionKind.Normal ? "1" : "0"),
                "加餐只统计已经结算的日常营业，非致命失败也必须计数");
        }

        [Test]
        public void LastHeartFailure_ShowsHeartBreakBeforeDefeat()
        {
            GameRun run = CreateRunAtHearts(1);
            var view = new RecordingLoopView(run) { CompleteHeartBreakImmediately = true };
            var controller = new WeekLoopController(run, view);

            using (RunPersistence.SuppressSave())
            {
                controller.OnBattleSettled(Result(17), isWin: false, finalHappyCakeLayers: 0);
            }

            Assert.That(run.HeartsRemaining, Is.Zero);
            Assert.That(view.HeartBreakArgs, Is.Not.Null);
            Assert.That(view.HeartBreakArgs.IsTerminal, Is.True);
            Assert.That(view.RunResultShown, Is.True);
            Assert.That(view.RunResultWin, Is.False);
            Assert.That(view.RunResultTotal, Is.EqualTo(17));
        }

        [TestCase(1)]
        [TestCase(0)]
        public void FamousKnifeFailure_RestoresOneHeartAndCreatesRewardAfterConsumingItem(
            int startingHearts)
        {
            GameRun run = CreateRunAtHearts(startingHearts);
            Assert.That(
                run.AcquireItem("item_famous_knife", fallbackGold: 0).Outcome,
                Is.EqualTo(ItemAcquireOutcome.Added));
            cfg.GameAction action = ActionOfKind(cfg.FoodActionKind.Normal);
            var context = new ActionExecutionContext(
                action,
                0,
                0,
                "famous-knife-failure",
                action.MinCostDays);
            run.SetLastActionContext(context);
            string rewardKey = GameRun.BuildRewardKey(run.WeekIndex, run.CurrentDay, context);
            Assert.That(run.GetPendingRewardOffer(rewardKey), Is.Null, "测试必须由本次结算现场生成奖励");
            var view = new RecordingLoopView(run) { CompleteNoticeImmediately = false };
            var controller = new WeekLoopController(run, view);

            using (RunPersistence.SuppressSave())
            {
                controller.OnBattleSettled(Result(17), isWin: false, finalHappyCakeLayers: 0);
            }

            Assert.That(run.HeartsRemaining, Is.EqualTo(1));
            Assert.That(run.GetItemState("item_famous_knife"), Is.Null);
            Assert.That(run.GetPendingRewardOffer(rewardKey), Is.Not.Null,
                "名刀保住最后一颗心后仍需现场生成本场奖励");
            Assert.That(view.NoticeShown, Is.True);
            Assert.That(view.HeartBreakArgs, Is.Null);
            Assert.That(view.RunResultShown, Is.False);

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            Assert.That(restored.HeartsRemaining, Is.EqualTo(1));
            Assert.That(restored.GetItemState("item_famous_knife"), Is.Null);
            Assert.That(restored.GetPendingRewardOffer(rewardKey), Is.Not.Null,
                "名刀结算后的待领奖状态必须可随存档恢复");
        }

        [Test]
        public void SixthNormalFailureWhileAlive_QueuesBaseAndExtraFoodRewards()
        {
            GameRun run = CreateRun();
            run.AcquireItem("item_extra_food_choice", fallbackGold: 0);
            run.PassiveModels
                .Single(model => model.ItemId == "item_extra_food_choice")
                .RestoreState("count:5");
            cfg.GameAction action = ActionOfKind(cfg.FoodActionKind.Normal);
            var context = new ActionExecutionContext(
                action,
                0,
                0,
                "sixth-normal-failure",
                action.MinCostDays);
            run.SetLastActionContext(context);
            string rewardKey = GameRun.BuildRewardKey(run.WeekIndex, run.CurrentDay, context);
            run.SetPendingRewardOffer(
                rewardKey,
                new RewardOffer(
                    0,
                    Array.Empty<RewardChoice>(),
                    Array.Empty<RewardChoice>(),
                    baseGoldClaimed: true));
            var view = new RecordingLoopView(run);
            var controller = new WeekLoopController(run, view);

            using (RunPersistence.SuppressSave())
            {
                controller.OnBattleSettled(Result(21), isWin: false, finalHappyCakeLayers: 0);
            }

            Assert.That(run.HeartsRemaining, Is.EqualTo(2));
            Assert.That(run.HasPendingRewardOffer, Is.True, "失去红心后仍存活时保留本场基础领奖");
            Assert.That(run.HasPendingGenericRewards, Is.True, "第 6 次日常营业失败也应产生加餐领奖");
            Assert.That(
                run.PassiveModels.Single(model => model.ItemId == "item_extra_food_choice").InfoText,
                Is.EqualTo("0"));
        }

        [Test]
        public void AlreadyZeroHeartState_StillShowsTerminalHeartBreakBeforeDefeat()
        {
            GameRun run = CreateRunAtHearts(0);
            var view = new RecordingLoopView(run) { CompleteHeartBreakImmediately = true };
            var controller = new WeekLoopController(run, view);

            using (RunPersistence.SuppressSave())
            {
                controller.OnBattleSettled(Result(9), isWin: false, finalHappyCakeLayers: 0);
            }

            Assert.That(view.HeartBreakArgs, Is.Not.Null);
            Assert.That(view.HeartBreakArgs.BeforeHeartCount, Is.Zero);
            Assert.That(view.HeartBreakArgs.AfterHeartCount, Is.Zero);
            Assert.That(view.HeartBreakArgs.IsTerminal, Is.True);
            Assert.That(view.RunResultShown, Is.True);
        }

        [Test]
        public void FinishedFinalWeek_WithRemainingHeart_WinsWithoutBossCompletion()
        {
            GameRun run = CreateFinishedWeek(_tables.TbGameBase.TotalWeeks);
            var view = new RecordingLoopView(run);
            var controller = new WeekLoopController(run, view);

            using (RunPersistence.SuppressSave())
            {
                controller.PromptNextAction();
            }

            Assert.That(view.RunResultShown, Is.True);
            Assert.That(view.RunResultWin, Is.True);
            Assert.That(run.WeekIndex, Is.EqualTo(run.TotalWeeks));
        }

        [Test]
        public void FinishedFinalWeek_ExecutesDueWeekEndNodeBeforeVictory()
        {
            GameRun run = CreateRun();
            run.SetWeekIndex(run.TotalWeeks);
            run.Gold = 250;
            run.BeginTimeline(
                "final-week-node-test",
                1f,
                new[]
                {
                    new RuntimeTimelineNode(
                        "final-week-repayment",
                        "final-week-node-test",
                        1,
                        "act_loan_repay"),
                });
            run.CurrentDay = 1f;
            var view = new RecordingLoopView(run);
            var controller = new WeekLoopController(run, view);

            using (RunPersistence.SuppressSave())
            {
                controller.PromptNextAction();
            }

            Assert.That(run.IsNodeTriggered("final-week-repayment"), Is.True);
            Assert.That(run.Gold, Is.EqualTo(50));
            Assert.That(view.RunResultShown, Is.True);
            Assert.That(view.RunResultWin, Is.True);
        }

        [Test]
        public void FinishedFinalWeek_AppliesLegacyDebtBeforeVictory()
        {
            GameRun run = CreateFinishedWeek(_tables.TbGameBase.TotalWeeks);
            run.Gold = 80;
            run.RegisterLoanDebt(30);
            var view = new RecordingLoopView(run);
            var controller = new WeekLoopController(run, view);

            using (RunPersistence.SuppressSave())
            {
                controller.PromptNextAction();
            }

            Assert.That(run.Gold, Is.EqualTo(50));
            Assert.That(run.LoanDebt, Is.Zero);
            Assert.That(view.RunResultWin, Is.True);
        }

        [Test]
        public void FinishedFinalWeek_WithNoHeart_LosesWithoutAdvancing()
        {
            GameRun run = CreateFinishedWeek(_tables.TbGameBase.TotalWeeks);
            while (run.HeartsRemaining > 0)
            {
                run.TryLoseHeart(out _, out _);
            }

            var view = new RecordingLoopView(run);
            var controller = new WeekLoopController(run, view);

            using (RunPersistence.SuppressSave())
            {
                controller.PromptNextAction();
            }

            Assert.That(view.RunResultShown, Is.True);
            Assert.That(view.RunResultWin, Is.False);
            Assert.That(run.WeekIndex, Is.EqualTo(run.TotalWeeks));
        }

        [Test]
        public void FinishedPreviousWeek_AdvancesWithoutVictory()
        {
            GameRun run = CreateFinishedWeek(_tables.TbGameBase.TotalWeeks - 1);
            var view = new RecordingLoopView(run);
            var controller = new WeekLoopController(run, view);

            using (RunPersistence.SuppressSave())
            {
                controller.PromptNextAction();
            }

            Assert.That(run.WeekIndex, Is.EqualTo(run.TotalWeeks));
            Assert.That(view.RunResultShown, Is.False);
        }

        private GameRun CreateRunAtHearts(int hearts)
        {
            GameRun run = CreateRun();
            while (run.HeartsRemaining > hearts)
            {
                run.TryLoseHeart(out _, out _);
            }

            return run;
        }

        private GameRun CreateRun()
        {
            string characterId = _tables.TbCharacter.DataList.First().Id;
            return new GameRun(_tables, _database, characterId, "heart-failure-flow-tests");
        }

        private GameRun CreateFinishedWeek(int weekIndex)
        {
            GameRun run = CreateRun();
            run.SetWeekIndex(weekIndex);
            run.BeginTimeline("finished-week-test", 1f);
            run.CurrentDay = 1f;
            return run;
        }

        private cfg.GameAction ActionOfKind(cfg.FoodActionKind kind)
        {
            return _tables.TbAction.DataList.First(action =>
                FoodService.Resolve(_tables, action)?.ActionKind == kind);
        }

        private static ScoreResult Result(float total)
        {
            return new ScoreResult(Array.Empty<DishScore>(), total, 0f, 1f);
        }

        private sealed class RecordingLoopView : IWeekLoopView
        {
            private readonly GameRun _run;

            public RecordingLoopView(GameRun run)
            {
                _run = run;
            }

            public int LastBattleTotal => 0;
            public bool CompleteHeartBreakImmediately { get; set; }
            public bool CompleteNoticeImmediately { get; set; } = true;
            public HeartBreakFormOpenArgs HeartBreakArgs { get; private set; }
            public bool RunResultShown { get; private set; }
            public bool RunResultWin { get; private set; }
            public int RunResultTotal { get; private set; }
            public bool NoticeShown { get; private set; }

            public void HideBattleWorld() { }
            public void ResetBossBattlePresentation() { }

            public void SavePendingRewardBattleView()
            {
                _run.SetPendingRewardBattleView(new PendingRewardBattleViewSaveData());
            }

            public void RestorePendingRewardBattleView() { }
            public void HideResultPanel() { }
            public void OpenWeekMap() { }
            public void OpenShop() { }

            public void ShowTimelineNodeCard(cfg.TimelineNode node, int? interestMaxGain, Action onPick)
            {
                onPick?.Invoke();
            }
            public void ShowTimelineNodeSkipped(cfg.TimelineNode node, Action onDone) { }

            public void StartBattle(
                int requiredScore,
                string modifier,
                string key,
                string bossDebuffId,
                ActionExecutionContext actionContext) { }

            public void ShowNotice(string title, string message, Action onContinue)
            {
                NoticeShown = true;
                if (CompleteNoticeImmediately)
                {
                    onContinue?.Invoke();
                }
            }

            public void ShowHeartBreak(HeartBreakFormOpenArgs args, Action onComplete)
            {
                HeartBreakArgs = args;
                if (CompleteHeartBreakImmediately)
                {
                    onComplete?.Invoke();
                }
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
                Action onEnd) { }

            public void ShowRunResult(bool win, int total)
            {
                RunResultShown = true;
                RunResultWin = win;
                RunResultTotal = total;
            }
        }
    }
}
