using System;
using System.Collections.Generic;
using System.IO;
using BreakInfinity;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Orchestration;
using GourmetProject.Game.Run;
using GourmetProject.Game.Save;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    /// <summary>
    /// 删除当前节点行动卡后续跑序列；删其它节点不得白刷当前页；
    /// 评鉴调整单带出新旧 Tip 文案。
    /// </summary>
    public sealed class TimelineDeleteAndBossRerollTests
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
        public void RemovePresentedNode_DismissesCardAndShowsNextDueNode()
        {
            GameRun run = CreateRun("delete-current-node");
            run.BeginTimeline(
                "test",
                7f,
                new[]
                {
                    new RuntimeTimelineNode("n1", "test", 1, "act_interest"),
                    new RuntimeTimelineNode("n2", "test", 1, "act_shop"),
                });
            run.CurrentDay = 1f;
            var view = new HoldingWeekLoopView();
            var loop = new WeekLoopController(run, view);

            loop.PromptNextAction();

            Assert.That(view.CurrentCardId, Is.EqualTo("n1"));
            Assert.That(loop.RemoveTimelineNode("n1"), Is.True);
            Assert.That(view.DismissCount, Is.EqualTo(1));
            Assert.That(view.CurrentCardId, Is.EqualTo("n2"));
            Assert.That(view.OpenWeekMapCount, Is.Zero);
            Assert.That(TimelineService.GetNode(run, "n1"), Is.Null);
            Assert.That(TimelineService.GetNode(run, "n2"), Is.Not.Null);
        }

        [Test]
        public void RemoveOtherNode_DoesNotDismissCurrentCard()
        {
            GameRun run = CreateRun("delete-other-node");
            run.BeginTimeline(
                "test",
                7f,
                new[]
                {
                    new RuntimeTimelineNode("n1", "test", 1, "act_interest"),
                    new RuntimeTimelineNode("n2", "test", 1, "act_shop"),
                });
            run.CurrentDay = 1f;
            var view = new HoldingWeekLoopView();
            var loop = new WeekLoopController(run, view);

            loop.PromptNextAction();

            Assert.That(view.CurrentCardId, Is.EqualTo("n1"));
            Assert.That(loop.RemoveTimelineNode("n2"), Is.True);
            Assert.That(view.DismissCount, Is.Zero);
            Assert.That(view.CurrentCardId, Is.EqualTo("n1"));
            Assert.That(view.ShowCount, Is.EqualTo(1));
            Assert.That(view.OpenWeekMapCount, Is.Zero);
            Assert.That(TimelineService.GetNode(run, "n1"), Is.Not.Null);
            Assert.That(TimelineService.GetNode(run, "n2"), Is.Null);
        }

        [Test]
        public void RemoveLastDueNode_ReturnsToDailyActionSelect()
        {
            GameRun run = CreateRun("delete-last-due-node");
            run.BeginTimeline(
                "test",
                7f,
                new[]
                {
                    new RuntimeTimelineNode("n1", "test", 1, "act_interest"),
                });
            run.CurrentDay = 1f;
            var view = new HoldingWeekLoopView();
            var loop = new WeekLoopController(run, view);

            loop.PromptNextAction();
            Assert.That(view.CurrentCardId, Is.EqualTo("n1"));
            Assert.That(loop.RemoveTimelineNode("n1"), Is.True);
            Assert.That(view.DismissCount, Is.EqualTo(1));
            Assert.That(view.CurrentCardId, Is.Empty);
            Assert.That(view.OpenWeekMapCount, Is.EqualTo(1));
        }

        [Test]
        public void ResetBossDebuff_CapturesOldAndNewTipTexts()
        {
            GameRun run = CreateRun("reroll-boss-tip");
            run.BeginTimeline(
                "test",
                7f,
                new[]
                {
                    new RuntimeTimelineNode("boss1", "test", 7, "act_boss"),
                });
            cfg.TimelineNode node = TimelineService.GetNearestUntriggeredBossNode(run);
            Assert.That(node, Is.Not.Null);
            cfg.BossDebuff before = BossService.PreviewBossDebuff(run, node);
            Assert.That(before, Is.Not.Null);

            ItemDefinition item = ItemDefinition.Get(
                _tables,
                "item_active_reroll_last_boss_debuff",
                cfg.ItemKind.Active);
            Assert.That(item, Is.Not.Null);
            var ctx = new ActionSelectUseContext(run, weekLoop: null);
            ActiveItemUseResult result = ActiveItemEffectRegistry.Apply(ctx, item, Array.Empty<ActiveTarget>());

            Assert.That(result.Success, Is.True);
            Assert.That(result.PresentationNodeId, Is.EqualTo(node.Id));
            Assert.That(result.OldTipTitle, Is.EqualTo(before.Name));
            Assert.That(result.OldTipDesc, Is.EqualTo(before.Desc));
            Assert.That(run.BossDebuffRerollIndex, Is.EqualTo(1));
            Assert.That(run.BossDebuffRerollNodeId, Is.EqualTo(node.Id));

            cfg.BossDebuff after = BossService.PreviewBossDebuff(run, node);
            Assert.That(after, Is.Not.Null);
            Assert.That(result.NewTipTitle, Is.EqualTo(after.Name));
            Assert.That(result.NewTipDesc, Is.EqualTo(after.Desc));
        }

        private GameRun CreateRun(string seed)
            => new GameRun(
                _tables,
                _database,
                "glutton_dog",
                seed,
                execution: RunExecutionEnvironment.CreateIsolated(_tables, seed));

        private sealed class HoldingWeekLoopView : IWeekLoopView
        {
            public BigDouble LastBattleTotal => BigDouble.Zero;

            public string CurrentCardId { get; private set; } = string.Empty;

            public int ShowCount { get; private set; }

            public int DismissCount { get; private set; }

            public int OpenWeekMapCount { get; private set; }

            public void HideBattleWorld() { }

            public void ResetBossBattlePresentation() { }

            public void SavePendingRewardBattleView() { }

            public void RestorePendingRewardBattleView() { }

            public void HideResultPanel() { }

            public void OpenWeekMap()
            {
                OpenWeekMapCount++;
                CurrentCardId = string.Empty;
            }

            public void OpenShop() { }

            public void OpenRewardForm(RewardFormOpenArgs args) { }

            public void ShowTimelineNodeCard(cfg.TimelineNode node, int? interestMaxGain, Action onPick)
            {
                ShowCount++;
                CurrentCardId = node?.Id ?? string.Empty;
            }

            public void DismissTimelineNodeCard(Action onDone)
            {
                DismissCount++;
                CurrentCardId = string.Empty;
                onDone?.Invoke();
            }

            public void ShowTimelineNodeSkipped(
                cfg.TimelineNode node,
                TimelineMutationResult result,
                Action onDone) => onDone?.Invoke();

            public void PlayTimelineAdvance(
                float fromDay,
                float toDay,
                string arrivingNodeId,
                Action onDone) => onDone?.Invoke();

            public void BeginTimelineAdvanceSequence(float fromDay) { }

            public void EndTimelineAdvanceSequence() { }

            public void PlayTimelineNodeCue(
                string nodeId,
                TimelinePresentationCueKind kind,
                Action onDone) => onDone?.Invoke();

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
                Action onComplete) { }

            public void ExitEventPage(Action onExited) => onExited?.Invoke();

            public void ShowEventRecipeMutation(RecipeMutationResult result, Action onComplete)
                => onComplete?.Invoke();

            public void ShowDirectPassiveItemAcquire(
                ItemDefinition item,
                ItemAcquireResult acquireResult,
                Action onComplete) => onComplete?.Invoke();

            public void ShowRunResult(bool win, BigDouble total) { }
        }
    }
}
