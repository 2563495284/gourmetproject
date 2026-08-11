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
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class EventAutoEndPresentationTests
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

        [Test]
        public void AddRandomRecipeFlavor_ReturnsTwoDistinctDishMutationsForPresentation()
        {
            var run = new GameRun(_tables, _database, "glutton_dog", "event-flavor-presentation-test", 1);
            cfg.EventOption option = _tables.TbEventOption.Get("opt_new_event_choice_2");

            EventResolveResult result = EventService.ResolveOption(
                run,
                option,
                new Xoshiro256SS(0xF1A70FUL));

            Assert.That(run.RecipeEntries.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(result.RecipeMutation, Is.Not.Null);
            Assert.That(result.RecipeMutation.Entries, Has.Count.EqualTo(2));
            Assert.That(
                result.RecipeMutation.Entries.Select(entry => entry.DishIndex).Distinct().Count(),
                Is.EqualTo(2));
            foreach (RecipeMutationEntry entry in result.RecipeMutation.Entries)
            {
                Assert.That(entry.Before, Is.Not.Null);
                Assert.That(entry.After, Is.Not.Null);
                Assert.That(entry.After.FlavorIds, Is.Not.EqualTo(entry.Before.FlavorIds));
            }
        }

        [Test]
        public void AutoEnd_WaitsForMutationAndTransientFeedbackBeforeCompletingEvent()
        {
            var run = new GameRun(_tables, _database, "glutton_dog", "event-auto-end-order-test", 1);
            var view = new RecordingWeekLoopView();
            var controller = new WeekLoopController(run, view);
            cfg.GameEvent ev = _tables.TbEvent.Get("ev_new_event");
            cfg.EventOption option = _tables.TbEventOption.Get("opt_new_event_choice_2");
            var mutation = new RecipeMutationResult { Title = "添加风味" };
            mutation.Entries.Add(new RecipeMutationEntry
            {
                BookIndex = 0,
                DishIndex = 0,
                Before = new RecipeDishSnapshot { DishId = "before" },
                After = new RecipeDishSnapshot { DishId = "after" },
            });
            EventResolveResult result = EventResolveResult.Immediate("已添加风味。", mutation);
            bool completed = false;

            MethodInfo continueMethod = typeof(WeekLoopController).GetMethod(
                "ContinueResolvedEventOption",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(continueMethod, Is.Not.Null);
            continueMethod.Invoke(controller, new object[]
            {
                ev,
                ev.Desc,
                option,
                result,
                new Xoshiro256SS(0xA070E0DUL),
                new Action(() => completed = true),
                false,
            });

            Assert.That(view.RecipeMutationCompletion, Is.Not.Null);
            Assert.That(view.EffectFeedbackCompletion, Is.Null);
            Assert.That(completed, Is.False);

            view.RecipeMutationCompletion.Invoke();

            Assert.That(view.EffectFeedbackCompletion, Is.Not.Null);
            Assert.That(completed, Is.False);

            view.EffectFeedbackCompletion.Invoke();

            Assert.That(completed, Is.True);
            Assert.That(run.HasUsedEvent(ev.Id), Is.True);
        }

        private sealed class RecordingWeekLoopView : IWeekLoopView
        {
            public Action RecipeMutationCompletion { get; private set; }

            public Action EffectFeedbackCompletion { get; private set; }

            public BigDouble LastBattleTotal => 0;

            public void HideBattleWorld() { }

            public void ResetBossBattlePresentation() { }

            public void SavePendingRewardBattleView() { }

            public void RestorePendingRewardBattleView() { }

            public void HideResultPanel() { }

            public void OpenWeekMap() { }

            public void OpenShop() { }

            public void ShowTimelineNodeCard(cfg.TimelineNode node, int? interestMaxGain, Action onPick) { }

            public void ShowTimelineNodeSkipped(
                cfg.TimelineNode node,
                TimelineMutationResult result,
                Action onDone) { }

            public void PlayTimelineAdvance(
                float fromDay,
                float toDay,
                string arrivingNodeId,
                Action onDone) { }

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

            public void ShowNotice(string title, string message, Action onContinue) { }

            public void ShowHeartBreak(HeartBreakFormOpenArgs args, Action onComplete) { }

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

            public void ShowEventEffectFeedback(
                string title,
                string message,
                string bgSprite,
                Action onComplete)
            {
                EffectFeedbackCompletion = onComplete;
            }

            public void ShowEventRecipeMutation(
                RecipeMutationResult result,
                Action onComplete)
            {
                RecipeMutationCompletion = onComplete;
            }

            public void ShowDirectPassiveItemAcquire(
                ItemDefinition item,
                ItemAcquireResult acquireResult,
                Action onComplete) { }

            public void ShowRunResult(bool win, BigDouble total) { }
        }
    }
}
