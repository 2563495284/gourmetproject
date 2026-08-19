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
        public void TriggerSweetTransfer_IsAnnouncedBeforeHandoffs()
        {
            var groups = new List<SettlementEffectGroup>
            {
                KindGroup(ScoreLineKind.TriggerSweetTransfer, 7, executionGroupId: 1),
                KindGroup(ScoreLineKind.TriggeredSweetTransferSource, 1, executionGroupId: 2),
                TransferGroup(sourceId: 1, executorId: 11, executionGroupId: 3),
            };

            Assert.That(SettlementSequencer.IsSweetTransferAnnounceLine(groups[0].Lines[0]), Is.True);
            Assert.That(SettlementSequencer.IsSweetTransferAnnounceLine(groups[1].Lines[0]), Is.False);
            Assert.That(SettlementSequencer.IsSweetTransferAnnounceLine(groups[2].Lines[0]), Is.False);
            Assert.That(
                SettlementSequencer.CountSweetTransferAnnounceLines(groups, 0, 3),
                Is.EqualTo(1));
            Assert.That(
                SettlementStageView.ResultHeader(groups[0].Lines[0]),
                Is.EqualTo("甜蜜传递"));
            Assert.That(
                SettlementStageView.ResultText(groups[0].Lines[0], BigDouble.Zero, BigDouble.One),
                Is.EqualTo("触发甜蜜传递"));
            Assert.That(
                SettlementSequencer.CollectWaveNativeLaunchSourceIds(groups, 0, 3),
                Is.Empty);
        }

        [Test]
        public void NativeTransfer_AnnouncesLaunchOnTheSourceDish()
        {
            var groups = new List<SettlementEffectGroup>
            {
                TransferGroup(sourceId: 1, executorId: 11, executionGroupId: 1),
                TransferGroup(sourceId: 1, executorId: 12, executionGroupId: 2),
            };

            List<int> launchIds = SettlementSequencer.CollectWaveNativeLaunchSourceIds(groups, 0, 2);
            Assert.That(launchIds, Is.EqualTo(new[] { 1 }));
            Assert.That(SettlementSequencer.CountSweetTransferAnnounceLines(groups, 0, 2), Is.EqualTo(0));
        }

        [Test]
        public void ExtraTargetBuff_PlaysAfterLaunchWithBuffOwnerHeader()
        {
            const int triggerSourceId = 1;
            var groups = new List<SettlementEffectGroup>
            {
                ExtraTargetGroup(
                    ownerId: 8,
                    executionGroupId: 1,
                    triggerSourceId: triggerSourceId),
                TransferGroup(sourceId: triggerSourceId, executorId: 11, executionGroupId: 2),
                TransferGroup(sourceId: triggerSourceId, executorId: 12, executionGroupId: 3),
            };

            Assert.That(SettlementSequencer.IsExtraTargetBuffLine(groups[0].Lines[0]), Is.True);
            Assert.That(SettlementSequencer.IsSweetTransferAnnounceLine(groups[0].Lines[0]), Is.True);
            Assert.That(SettlementSequencer.IsSweetTransferAnnounceLine(groups[1].Lines[0]), Is.False);
            Assert.That(
                SettlementSequencer.CountSweetTransferAnnounceLines(groups, 0, 3),
                Is.EqualTo(1));
            Assert.That(
                SettlementSequencer.CollectWaveNativeLaunchSourceIds(groups, 0, 3),
                Is.EqualTo(new[] { 1 }));
            Assert.That(
                SettlementStageView.ResultHeader(groups[0].Lines[0]),
                Is.EqualTo("黑松露Buff"));
            Assert.That(
                SettlementStageView.ResultText(groups[0].Lines[0], BigDouble.Zero, BigDouble.One),
                Is.EqualTo("额外目标 +1"));
            Assert.That(SettlementStageView.IsLaunchResultLabel(groups[0].Lines[0]), Is.False);
            Assert.That(
                SettlementSequencer.ResultVisualDishInstanceId(groups[0].Lines[0]),
                Is.EqualTo(triggerSourceId),
                "额外目标提示必须显示在触发甜蜜传递的食物上，而不是 Buff 所有者上");
        }

        [Test]
        public void MarshmallowExtraTarget_UsesBuffOwnerHeader()
        {
            var groups = new List<SettlementEffectGroup>
            {
                ExtraTargetGroup(
                    ownerId: 8,
                    executionGroupId: 1,
                    extraTargets: 3d,
                    dishName: "棉花糖",
                    skillId: "sk_marshmallow"),
                TransferGroup(sourceId: 1, executorId: 11, executionGroupId: 2),
            };

            Assert.That(SettlementSequencer.IsExtraTargetBuffLine(groups[0].Lines[0]), Is.True);
            Assert.That(SettlementSequencer.IsSweetTransferResponseLine(groups[0].Lines[0]), Is.False);
            Assert.That(
                SettlementSequencer.CountSweetTransferAnnounceLines(groups, 0, 2),
                Is.EqualTo(1));
            Assert.That(
                SettlementStageView.ResultHeader(groups[0].Lines[0]),
                Is.EqualTo("棉花糖Buff"));
            Assert.That(
                SettlementStageView.ResultText(groups[0].Lines[0], BigDouble.Zero, BigDouble.One),
                Is.EqualTo("额外目标 +3"));
        }

        [Test]
        public void MultipleExtraTargetBuffs_RemainSeparateAndKeepBoardOrder()
        {
            var groups = new List<SettlementEffectGroup>
            {
                ExtraTargetGroup(
                    ownerId: 8,
                    executionGroupId: 1,
                    extraTargets: 1d,
                    dishName: "松露巧克力",
                    skillId: "sk_chocolate_truffle"),
                ExtraTargetGroup(
                    ownerId: 9,
                    executionGroupId: 2,
                    extraTargets: 3d,
                    dishName: "棉花糖",
                    skillId: "sk_marshmallow"),
                TransferGroup(sourceId: 1, executorId: 11, executionGroupId: 3),
            };

            var announce = new List<ScoreLine>();
            var settle = new List<ScoreLine>();
            var response = new List<ScoreLine>();
            SettlementSequencer.ClassifySweetTransferWaveLines(
                groups,
                0,
                3,
                announce,
                settle,
                response);

            Assert.That(announce.Count, Is.EqualTo(2), "多个额外目标 Buff 不得合并");
            Assert.That(
                announce.ConvertAll(SettlementStageView.ResultHeader),
                Is.EqualTo(new[] { "松露巧克力Buff", "棉花糖Buff" }));
            Assert.That(
                announce.ConvertAll(line =>
                    SettlementStageView.ResultText(line, BigDouble.Zero, BigDouble.One)),
                Is.EqualTo(new[] { "额外目标 +1", "额外目标 +3" }));
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
        public void ReceiveTransferFlatBuff_StaysInWaveButPlaysAfterSettle()
        {
            var groups = new List<SettlementEffectGroup>
            {
                ReceiveFlatGroup(ownerId: 8, value: 120d, executionGroupId: 1),
                TransferGroup(sourceId: 1, executorId: 11, executionGroupId: 2),
                TransferGroup(sourceId: 1, executorId: 12, executionGroupId: 3),
                TransferGroup(sourceId: 1, executorId: 13, executionGroupId: 4),
            };

            Assert.That(SettlementSequencer.IsSweetTransferRelatedGroup(groups[0]), Is.True);
            Assert.That(SettlementSequencer.CountSweetTransferWaveLength(groups, 0), Is.EqualTo(4));
            Assert.That(SettlementSequencer.IsSweetTransferResponseLine(groups[0].Lines[0]), Is.True);
            Assert.That(SettlementSequencer.IsSweetTransferAnnounceLine(groups[0].Lines[0]), Is.False);
            Assert.That(SettlementSequencer.IsExtraTargetBuffLine(groups[0].Lines[0]), Is.False);
            Assert.That(
                SettlementSequencer.CountSweetTransferResponseLines(groups, 0, 4),
                Is.EqualTo(1));
            Assert.That(
                SettlementSequencer.CountSweetTransferAnnounceLines(groups, 0, 4),
                Is.EqualTo(0));

            var announce = new List<ScoreLine>();
            var settle = new List<ScoreLine>();
            var response = new List<ScoreLine>();
            SettlementSequencer.ClassifySweetTransferWaveLines(groups, 0, 4, announce, settle, response);
            Assert.That(announce, Is.Empty);
            Assert.That(settle.Count, Is.EqualTo(3));
            Assert.That(settle[0].DishInstanceId, Is.EqualTo(11));
            Assert.That(settle[1].DishInstanceId, Is.EqualTo(12));
            Assert.That(settle[2].DishInstanceId, Is.EqualTo(13));
            Assert.That(response.Count, Is.EqualTo(3), "跳跳糖同组的分数明细仍要写账本，只是跟在传递结算之后");
            Assert.That(response[0].Kind, Is.EqualTo(ScoreLineKind.SweetTransferBuffTriggered));
            Assert.That(response[0].Value.ToDouble(), Is.EqualTo(120d));
            Assert.That(response[1].Kind, Is.EqualTo(ScoreLineKind.DishFlat));
            Assert.That(response[2].Kind, Is.EqualTo(ScoreLineKind.DishFlat));
            Assert.That(
                SettlementStageView.ResultHeader(response[0]),
                Is.EqualTo("分数"));
            Assert.That(
                SettlementStageView.ResultText(response[0], BigDouble.Zero, BigDouble.One),
                Is.EqualTo("[score]+120[/score]"));
        }

        [Test]
        public void ReceiverSkillInTheSameGroup_StillSettlesBeforePoppingCandy()
        {
            const int poppingId = 8;
            const int receiverId = 11;
            var mixed = new SettlementEffectGroup(Line(
                ScoreLineKind.SweetTransferBuffTriggered,
                poppingId,
                1,
                ReceiveFlatTrace(poppingId),
                40d));
            mixed.Append(Line(
                ScoreLineKind.DishFlat,
                poppingId,
                1,
                ReceiveFlatTrace(poppingId),
                40d));
            mixed.Append(Line(
                ScoreLineKind.DishFlat,
                receiverId,
                1,
                TransferTrace(sourceId: 1, executorId: receiverId, skillId: "sk_transfer"),
                25d));

            var groups = new List<SettlementEffectGroup> { mixed };
            Assert.That(SettlementSequencer.CountSweetTransferWaveLength(groups, 0), Is.EqualTo(1));
            Assert.That(
                SettlementSequencer.IsSweetTransferExecutorResultLine(mixed.Lines[2]),
                Is.True);

            var announce = new List<ScoreLine>();
            var settle = new List<ScoreLine>();
            var response = new List<ScoreLine>();
            SettlementSequencer.ClassifySweetTransferWaveLines(groups, 0, 1, announce, settle, response);
            Assert.That(announce, Is.Empty);
            Assert.That(settle.Count, Is.EqualTo(1), "B 接受并执行传递技能必须单独先播");
            Assert.That(settle[0].DishInstanceId, Is.EqualTo(receiverId));
            Assert.That(settle[0].Value.ToDouble(), Is.EqualTo(25d));
            Assert.That(response.Count, Is.EqualTo(2));
            Assert.That(response[0].Kind, Is.EqualTo(ScoreLineKind.SweetTransferBuffTriggered));
            Assert.That(response[1].DishInstanceId, Is.EqualTo(poppingId));
            SettlementScopeSignal settleScope = SettlementSequencer.ScopeForScoreLines(settle);
            SettlementScopeSignal responseScope = SettlementSequencer.ScopeForScoreLines(response);
            Assert.That(settleScope.Traces.Count, Is.EqualTo(1));
            Assert.That(settleScope.Traces[0].Kind, Is.EqualTo(SkillExecutionKind.SweetTransfer));
            Assert.That(responseScope.Traces.Count, Is.EqualTo(1));
            Assert.That(responseScope.Traces[0].Kind, Is.EqualTo(SkillExecutionKind.NativeSkill));
            Assert.That(responseScope.Traces[0].OwnerDishInstanceId, Is.EqualTo(poppingId));
        }

        [Test]
        public void GummyTransferMultBuff_WaitsUntilAfterTheWaveSettle()
        {
            var groups = new List<SettlementEffectGroup>
            {
                TransferMultGroup(ownerId: 8, value: 0.8d, executionGroupId: 1),
                TransferGroup(sourceId: 1, executorId: 11, executionGroupId: 2),
                TransferGroup(sourceId: 1, executorId: 12, executionGroupId: 3),
            };

            Assert.That(SettlementSequencer.IsSweetTransferResponseLine(groups[0].Lines[0]), Is.True);
            Assert.That(SettlementSequencer.IsSweetTransferAnnounceLine(groups[0].Lines[0]), Is.False);
            Assert.That(SettlementSequencer.CountSweetTransferWaveLength(groups, 0), Is.EqualTo(3));
            Assert.That(
                SettlementSequencer.CountSweetTransferResponseLines(groups, 0, 3),
                Is.EqualTo(1));

            var announce = new List<ScoreLine>();
            var settle = new List<ScoreLine>();
            var response = new List<ScoreLine>();
            SettlementSequencer.ClassifySweetTransferWaveLines(groups, 0, 3, announce, settle, response);
            Assert.That(announce, Is.Empty);
            Assert.That(settle.Count, Is.EqualTo(2));
            Assert.That(response[0].Kind, Is.EqualTo(ScoreLineKind.SweetTransferBuffTriggered));
            Assert.That(
                SettlementStageView.ResultHeader(response[0]),
                Is.EqualTo("倍率"));
            Assert.That(
                SettlementStageView.ResultText(response[0], BigDouble.Zero, BigDouble.One),
                Is.EqualTo("[multadd]+0.8[/multadd]"));
        }

        [Test]
        public void ExtraTargetAndReceiveFlat_StayOnOppositeBeats()
        {
            var groups = new List<SettlementEffectGroup>
            {
                ExtraTargetGroup(ownerId: 8, executionGroupId: 1),
                ReceiveFlatGroup(ownerId: 9, value: 120d, executionGroupId: 2),
                TransferGroup(sourceId: 1, executorId: 11, executionGroupId: 3),
            };

            Assert.That(SettlementSequencer.CountSweetTransferWaveLength(groups, 0), Is.EqualTo(3));
            var announce = new List<ScoreLine>();
            var settle = new List<ScoreLine>();
            var response = new List<ScoreLine>();
            SettlementSequencer.ClassifySweetTransferWaveLines(groups, 0, 3, announce, settle, response);
            Assert.That(announce.Count, Is.EqualTo(1));
            Assert.That(SettlementSequencer.IsExtraTargetBuffLine(announce[0]), Is.True);
            Assert.That(settle.Count, Is.EqualTo(1));
            Assert.That(response[0].Kind, Is.EqualTo(ScoreLineKind.SweetTransferBuffTriggered));
            Assert.That(SettlementSequencer.IsSweetTransferResponseLine(response[0]), Is.True);
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

        private static SettlementEffectGroup ExtraTargetGroup(
            int ownerId,
            int executionGroupId,
            double extraTargets = 1d,
            string dishName = "黑松露",
            string skillId = "sk_chocolate_truffle",
            int triggerSourceId = 0)
        {
            int runtimeSelfId = triggerSourceId > 0 ? triggerSourceId : ownerId;
            return new SettlementEffectGroup(Line(
                ScoreLineKind.SweetTransferBuffTriggered,
                ownerId,
                executionGroupId,
                ExtraTargetTrace(ownerId, runtimeSelfId, dishName, skillId),
                extraTargets));
        }

        private static SettlementEffectGroup ReceiveFlatGroup(
            int ownerId,
            double value,
            int executionGroupId)
        {
            var group = new SettlementEffectGroup(Line(
                ScoreLineKind.SweetTransferBuffTriggered,
                ownerId,
                executionGroupId,
                ReceiveFlatTrace(ownerId),
                value));
            group.Append(Line(
                ScoreLineKind.DishFlat,
                ownerId,
                executionGroupId,
                value: value));
            group.Append(Line(
                ScoreLineKind.DishFlat,
                ownerId + 1,
                executionGroupId,
                value: value));
            return group;
        }

        private static SettlementEffectGroup TransferMultGroup(
            int ownerId,
            double value,
            int executionGroupId)
        {
            var group = new SettlementEffectGroup(Line(
                ScoreLineKind.SweetTransferBuffTriggered,
                ownerId,
                executionGroupId,
                TransferMultTrace(ownerId),
                value));
            group.Append(Line(
                ScoreLineKind.DishMultiplierAdd,
                ownerId,
                executionGroupId,
                value: value));
            group.Append(Line(
                ScoreLineKind.DishMultiplierAdd,
                ownerId + 1,
                executionGroupId,
                value: value));
            return group;
        }

        private static ScoreLine Line(
            ScoreLineKind kind,
            int dishInstanceId,
            int executionGroupId,
            SkillExecutionTrace trace = null,
            double value = 1d)
        {
            return new ScoreLine(
                ScorePhase.DishSkills,
                kind,
                ScoreSource.FinalModifier("test_transfer", "甜蜜传递"),
                dishInstanceId,
                $"dish_{dishInstanceId}",
                null,
                new BigDouble(value),
                BigDouble.Zero,
                new BigDouble(value),
                "test",
                trace,
                executionGroupId);
        }

        private static SkillExecutionTrace ExtraTargetTrace(
            int ownerId,
            int runtimeSelfId,
            string dishName = "黑松露",
            string skillId = "sk_chocolate_truffle")
        {
            return new SkillExecutionTrace(
                SkillExecutionKind.NativeSkill,
                ownerId,
                $"owner_{ownerId}",
                dishName,
                runtimeSelfId,
                $"source_{runtimeSelfId}",
                "触发来源",
                skillId,
                dishName,
                $"{skillId}_1",
                0,
                SkillTrigger.OnSettle,
                SkillActionType.TriggerSweetTransfer,
                SkillConditionType.None,
                SkillScope.Self,
                SkillScope.All,
                dishName);
        }

        private static SkillExecutionTrace ReceiveFlatTrace(int ownerId)
        {
            return new SkillExecutionTrace(
                SkillExecutionKind.NativeSkill,
                ownerId,
                $"owner_{ownerId}",
                "跳跳糖",
                ownerId,
                $"owner_{ownerId}",
                "跳跳糖",
                "sk_popping_candy",
                "跳跳糖",
                "sk_popping_candy_1",
                0,
                SkillTrigger.OnSettle,
                SkillActionType.AddFlat,
                SkillConditionType.None,
                SkillScope.ColumnAndSelf,
                SkillScope.ColumnAndSelf,
                "跳跳糖");
        }

        private static SkillExecutionTrace TransferMultTrace(int ownerId)
        {
            return new SkillExecutionTrace(
                SkillExecutionKind.NativeSkill,
                ownerId,
                $"owner_{ownerId}",
                "软糖",
                ownerId,
                $"owner_{ownerId}",
                "软糖",
                "sk_gummy",
                "软糖",
                "sk_gummy_1",
                0,
                SkillTrigger.OnSettle,
                SkillActionType.AddMultFlat,
                SkillConditionType.None,
                SkillScope.ColumnAndSelf,
                SkillScope.ColumnAndSelf,
                "软糖");
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
