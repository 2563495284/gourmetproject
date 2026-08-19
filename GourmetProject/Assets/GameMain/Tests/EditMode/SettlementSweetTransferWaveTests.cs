using System.Collections.Generic;
using BreakInfinity;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SettlementSweetTransferWaveTests
    {
        [Test]
        public void OneSourceToManyTargets_MergesIntoASingleWave()
        {
            var groups = new List<SettlementEffectGroup>
            {
                TransferGroup(sourceId: 1, executorId: 11, executionGroupId: 1),
                TransferGroup(sourceId: 1, executorId: 12, executionGroupId: 2),
                TransferGroup(sourceId: 1, executorId: 13, executionGroupId: 3),
            };

            Assert.That(SettlementSequencer.CountSweetTransferWaveLength(groups, 0), Is.EqualTo(3));
            List<SettlementSweetTransferPresentationContext> handoffs =
                SettlementSequencer.CollectWaveHandoffs(groups, 0, 3);
            Assert.That(handoffs.Count, Is.EqualTo(3));
            Assert.That(handoffs[0].ExecutorDishInstanceId, Is.EqualTo(11));
            Assert.That(handoffs[1].ExecutorDishInstanceId, Is.EqualTo(12));
            Assert.That(handoffs[2].ExecutorDishInstanceId, Is.EqualTo(13));

            IReadOnlyList<SkillExecutionTrace> scopes =
                SettlementSequencer.CollectWaveScopeTraces(groups, 0, 3);
            Assert.That(scopes.Count, Is.EqualTo(3), "同时传递多个接收者时，每个接收者都要有自己的作用范围框");
            Assert.That(scopes[0].RuntimeSelfDishInstanceId, Is.EqualTo(11));
            Assert.That(scopes[1].RuntimeSelfDishInstanceId, Is.EqualTo(12));
            Assert.That(scopes[2].RuntimeSelfDishInstanceId, Is.EqualTo(13));
            SettlementScopeSignal waveScope = SettlementSequencer.ScopeForWave(groups, 0, 3);
            Assert.That(waveScope.Traces.Count, Is.EqualTo(3));
        }

        [Test]
        public void NativeSkillInBetween_SplitsWaves()
        {
            var groups = new List<SettlementEffectGroup>
            {
                TransferGroup(sourceId: 1, executorId: 11, executionGroupId: 1),
                NativeGroup(21, executionGroupId: 2),
                TransferGroup(sourceId: 3, executorId: 31, executionGroupId: 3),
            };

            Assert.That(SettlementSequencer.CountSweetTransferWaveLength(groups, 0), Is.EqualTo(1));
            Assert.That(SettlementSequencer.CountSweetTransferWaveLength(groups, 1), Is.EqualTo(0));
            Assert.That(SettlementSequencer.CountSweetTransferWaveLength(groups, 2), Is.EqualTo(1));
        }

        [Test]
        public void TriggerSweetTransferSources_StayInOneWave()
        {
            var groups = new List<SettlementEffectGroup>
            {
                KindGroup(ScoreLineKind.TriggerSweetTransfer, 7, executionGroupId: 1),
                KindGroup(ScoreLineKind.TriggeredSweetTransferSource, 1, executionGroupId: 2),
                TransferGroup(sourceId: 1, executorId: 11, executionGroupId: 3),
                KindGroup(ScoreLineKind.TriggeredSweetTransferSource, 2, executionGroupId: 4),
                TransferGroup(sourceId: 2, executorId: 11, executionGroupId: 5),
            };

            Assert.That(SettlementSequencer.CountSweetTransferWaveLength(groups, 0), Is.EqualTo(5));
            List<SettlementSweetTransferPresentationContext> handoffs =
                SettlementSequencer.CollectWaveHandoffs(groups, 0, 5);
            Assert.That(handoffs.Count, Is.EqualTo(2));
            Assert.That(handoffs[0].SourceDishInstanceId, Is.EqualTo(1));
            Assert.That(handoffs[1].SourceDishInstanceId, Is.EqualTo(2));
            Assert.That(handoffs[0].ExecutorDishInstanceId, Is.EqualTo(11));
            Assert.That(handoffs[1].ExecutorDishInstanceId, Is.EqualTo(11));
        }

        [Test]
        public void BuffApplied_DoesNotJoinTheFollowingTransferWave()
        {
            var groups = new List<SettlementEffectGroup>
            {
                KindGroup(ScoreLineKind.SweetTransferBuffApplied, 8, executionGroupId: 1),
                TransferGroup(sourceId: 1, executorId: 11, executionGroupId: 2),
            };

            Assert.That(SettlementSequencer.IsSweetTransferRelatedGroup(groups[0]), Is.False);
            Assert.That(SettlementSequencer.CountSweetTransferWaveLength(groups, 0), Is.EqualTo(0));
            Assert.That(SettlementSequencer.CountSweetTransferWaveLength(groups, 1), Is.EqualTo(1));
        }

        [Test]
        public void DuplicateHandoffKeys_AreDeduped()
        {
            var groups = new List<SettlementEffectGroup>
            {
                TransferGroup(sourceId: 1, executorId: 11, executionGroupId: 1, skillId: "sk_a"),
                TransferGroup(sourceId: 1, executorId: 11, executionGroupId: 2, skillId: "sk_a"),
            };

            List<SettlementSweetTransferPresentationContext> handoffs =
                SettlementSequencer.CollectWaveHandoffs(groups, 0, 2);
            Assert.That(handoffs.Count, Is.EqualTo(1));
        }

        [Test]
        public void ResultLabelScatterOffset_UsesSignedXAndUpwardY()
        {
            Assert.That(
                SettlementStageView.ResultLabelScatterOffset(1f, 1f, 1f),
                Is.EqualTo(new Vector3(0.12f, 0.18f, 0f)));
            Assert.That(
                SettlementStageView.ResultLabelScatterOffset(2f, -1f, 0f),
                Is.EqualTo(new Vector3(-0.24f, 0f, 0f)));
        }

        [Test]
        public void WorldLabelSorting_LaterLabelsReceiveHigherOrder()
        {
            int first = WorldLabelSorting.NextOrder();
            int second = WorldLabelSorting.NextOrder();
            Assert.That(second, Is.GreaterThan(first));
        }

        private static SettlementEffectGroup TransferGroup(
            int sourceId,
            int executorId,
            int executionGroupId,
            string skillId = "sk_transfer")
        {
            return new SettlementEffectGroup(Line(
                ScoreLineKind.DishFlat,
                executorId,
                executionGroupId,
                TransferTrace(sourceId, executorId, skillId)));
        }

        private static SettlementEffectGroup NativeGroup(int dishInstanceId, int executionGroupId)
        {
            return new SettlementEffectGroup(Line(
                ScoreLineKind.DishFlat,
                dishInstanceId,
                executionGroupId));
        }

        private static SettlementEffectGroup KindGroup(
            ScoreLineKind kind,
            int dishInstanceId,
            int executionGroupId)
        {
            return new SettlementEffectGroup(Line(kind, dishInstanceId, executionGroupId));
        }

        private static ScoreLine Line(
            ScoreLineKind kind,
            int dishInstanceId,
            int executionGroupId,
            SkillExecutionTrace trace = null)
        {
            return new ScoreLine(
                ScorePhase.DishSkills,
                kind,
                ScoreSource.FinalModifier("test_transfer", "甜蜜传递"),
                dishInstanceId,
                $"dish_{dishInstanceId}",
                null,
                BigDouble.One,
                BigDouble.Zero,
                BigDouble.One,
                "test",
                trace,
                executionGroupId);
        }

        private static SkillExecutionTrace TransferTrace(int sourceId, int executorId, string skillId)
        {
            return new SkillExecutionTrace(
                SkillExecutionKind.SweetTransfer,
                sourceId,
                $"src_{sourceId}",
                "来源",
                executorId,
                $"dst_{executorId}",
                "接收者",
                skillId,
                "传递技能",
                "rule",
                0,
                SkillTrigger.OnSettle,
                SkillActionType.AddFlat,
                SkillConditionType.None,
                SkillScope.Self,
                SkillScope.Self,
                "来源<甜蜜传递>");
        }
    }
}
