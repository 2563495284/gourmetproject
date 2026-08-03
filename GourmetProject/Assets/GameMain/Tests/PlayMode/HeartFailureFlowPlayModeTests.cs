using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Orchestration;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Scoring;
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
        }

        [Test]
        public void NonTerminalFailure_SavesHeartBreakAndKeepsRewardPending()
        {
            GameRun run = CreateRun();
            string rewardKey = GameRun.BuildRewardKey(run.WeekIndex, run.CurrentDay, null);
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
        public void LastHeartWithFamousKnife_DoesNotBreakHeartOrGrantReward()
        {
            GameRun run = CreateRunAtHearts(1);
            run.AcquireItem("item_famous_knife", fallbackGold: 0);
            var view = new RecordingLoopView(run);
            var controller = new WeekLoopController(run, view);

            using (RunPersistence.SuppressSave())
            {
                controller.OnBattleSettled(Result(5), isWin: false, finalHappyCakeLayers: 0);
            }

            Assert.That(run.HeartsRemaining, Is.EqualTo(1));
            Assert.That(run.Items.Any(item => item.ItemId == "item_famous_knife"), Is.False);
            Assert.That(view.NoticeShown, Is.True);
            Assert.That(view.HeartBreakArgs, Is.Null);
            Assert.That(run.HasPendingRewardOffer, Is.False);
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

            public void ShowTimelineNodeCard(cfg.TimelineNode node, int? interestMaxGain, Action onPick) { }
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
                onContinue?.Invoke();
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
