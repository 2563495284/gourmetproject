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
    public sealed class BusinessFailureSettlementPlayModeTests
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
        public void TerminalNormalFailure_GrantsAndConsumesMealBonusExactlyOnceAcrossSave()
        {
            GameRun run = CreateRunAtOneHeart();
            run.AcquireItem("item_gold_meal_bonus", 0);
            int remainingBefore = run.MealBonusRemaining;
            int bonus = new ItemRuntime(run).MealBonusGoldPerMeal();
            int goldBefore = run.Gold;
            cfg.GameAction action = _tables.TbAction.Get("act_food_gold");
            var context = new ActionExecutionContext(
                action,
                0,
                0,
                "business-failure-test",
                action.MinCostDays);
            const string battleKey = "normal-terminal-failure";
            run.SetPendingActionExecution(
                context,
                ActionOutcome.Battle(100, string.Empty, battleKey));
            var view = new RecordingLoopView(run);
            var controller = new WeekLoopController(run, view);

            using (RunPersistence.SuppressSave())
            {
                controller.BeginWeek();
                controller.OnBattleSettled(Result(0), isWin: false, finalHappyCakeLayers: 0);
                controller.OnBattleSettled(Result(0), isWin: false, finalHappyCakeLayers: 0);
            }

            Assert.That(run.Gold, Is.EqualTo(goldBefore + bonus));
            Assert.That(run.MealBonusRemaining, Is.EqualTo(remainingBefore - 1));
            Assert.That(run.HeartsRemaining, Is.Zero);
            Assert.That(view.HeartBreakArgs, Is.Not.Null);
            Assert.That(view.HeartBreakArgs.IsTerminal, Is.True);

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            Assert.That(restored.Gold, Is.EqualTo(goldBefore + bonus));
            Assert.That(restored.MealBonusRemaining, Is.EqualTo(remainingBefore - 1));
            Assert.That(restored.TryMarkFoodBattleSettled(battleKey), Is.False);
        }

        private GameRun CreateRunAtOneHeart()
        {
            string characterId = _tables.TbCharacter.DataList.First().Id;
            var run = new GameRun(_tables, _database, characterId, "business-failure-playmode");
            while (run.HeartsRemaining > 1)
            {
                run.TryLoseHeart(out _, out _);
            }

            return run;
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

            public HeartBreakFormOpenArgs HeartBreakArgs { get; private set; }

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
                onContinue?.Invoke();
            }

            public void ShowHeartBreak(HeartBreakFormOpenArgs args, Action onComplete)
            {
                HeartBreakArgs = args;
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

            public void ShowRunResult(bool win, int total) { }
        }
    }
}
