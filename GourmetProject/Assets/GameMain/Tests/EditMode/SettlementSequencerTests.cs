using System;
using System.Collections.Generic;
using BreakInfinity;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SettlementSequencerTests
    {
        [Test]
        public void PresentationPlan_PartitionsGlobalAndDishStages_AndKeepsEmptyDishChapter()
        {
            var scores = new List<DishScore>
            {
                DishScoreFor(1, "dish_1"),
                DishScoreFor(2, "dish_2"),
                DishScoreFor(3, "dish_3"),
            };
            var lines = new List<ScoreLine>
            {
                Line(ScorePhase.BeforeAll, ScoreLineKind.DishFlat, 1, 1),
                Line(ScorePhase.BeforeDish, ScoreLineKind.DishFlat, 1, 2),
                BaseLine(1, "dish_1"),
                // 目标是食物 2，但执行时仍处于食物 1 的章节。
                Line(ScorePhase.DishSkills, ScoreLineKind.DishFlat, 2, 3),
                // 额外结算不会再次写入基础分标记，BeforeDish 仍应留在食物 1。
                Line(ScorePhase.BeforeDish, ScoreLineKind.DishFlat, 1, 5),
                BaseLine(2, "dish_2"),
                BaseLine(3, "dish_3"),
                Line(ScorePhase.AfterAllDishes, ScoreLineKind.FinalFlat, 0, 4),
            };

            SettlementPresentationPlan plan = SettlementPresentationPlan.Build(Result(scores, lines));

            Assert.AreEqual(1, plan.PreludeGroups.Count);
            Assert.AreEqual(3, plan.DishChapters.Count);
            Assert.AreEqual(3, plan.DishChapters[0].Groups.Count);
            Assert.AreEqual(2, plan.DishChapters[0].Groups[1].Lines[0].DishInstanceId);
            Assert.AreEqual(0, plan.DishChapters[1].Groups.Count);
            Assert.AreEqual(0, plan.DishChapters[2].Groups.Count);
            Assert.AreEqual(1, plan.EpilogueGroups.Count);
            Assert.AreEqual(6, plan.ResultBeatCount, "基础分不应重复占用结果节拍。");
        }

        [Test]
        public void PresentationPlan_UsesDishScoreOrder_ForReversedSettlement()
        {
            var scores = new List<DishScore>
            {
                DishScoreFor(3, "dish_3"),
                DishScoreFor(1, "dish_1"),
                DishScoreFor(2, "dish_2"),
            };
            var lines = new List<ScoreLine>
            {
                BaseLine(3, "dish_3"),
                BaseLine(1, "dish_1"),
                BaseLine(2, "dish_2"),
            };

            SettlementPresentationPlan plan = SettlementPresentationPlan.Build(Result(scores, lines));

            CollectionAssert.AreEqual(
                new[] { 3, 1, 2 },
                new[]
                {
                    plan.DishChapters[0].DishInstanceId,
                    plan.DishChapters[1].DishInstanceId,
                    plan.DishChapters[2].DishInstanceId,
                });
        }

        [Test]
        public void PresentationPlan_BeforeDishWithoutSemanticOwner_UsesNextBaseMarker()
        {
            var scores = new List<DishScore>
            {
                DishScoreFor(1, "dish_1"),
                DishScoreFor(2, "dish_2"),
            };
            var lines = new List<ScoreLine>
            {
                BaseLine(1, "dish_1"),
                Line(ScorePhase.BeforeDish, ScoreLineKind.CopySkill, 0, 20),
                BaseLine(2, "dish_2"),
            };

            SettlementPresentationPlan plan = SettlementPresentationPlan.Build(Result(scores, lines));

            Assert.AreEqual(0, plan.DishChapters[0].Groups.Count);
            Assert.AreEqual(1, plan.DishChapters[1].Groups.Count);
        }

        [Test]
        public void PresentationPlan_KeepsSweetTransferWaveInsideTriggeringDishChapter()
        {
            var scores = new List<DishScore>
            {
                DishScoreFor(1, "dish_1"),
                DishScoreFor(2, "dish_2"),
            };
            var lines = new List<ScoreLine>
            {
                BaseLine(1, "dish_1"),
                Line(ScorePhase.DishSkills, ScoreLineKind.TriggerSweetTransfer, 1, 10),
                Line(ScorePhase.DishSkills, ScoreLineKind.SweetTransferFailed, 1, 11),
                BaseLine(2, "dish_2"),
            };

            SettlementPresentationPlan plan = SettlementPresentationPlan.Build(Result(scores, lines));

            Assert.AreEqual(2, plan.DishChapters[0].Groups.Count);
            Assert.AreEqual(
                2,
                SettlementSequencer.CountSweetTransferWaveLength(
                    plan.DishChapters[0].Groups,
                    0));
            Assert.AreEqual(0, plan.DishChapters[1].Groups.Count);
        }

        [Test]
        public void SettlementBeatKind_AppendsDishBoundariesWithoutRenumberingExistingKinds()
        {
            Assert.AreEqual(4, (int)SettlementBeatKind.FinaleConfirmed);
            Assert.AreEqual(5, (int)SettlementBeatKind.DishStarted);
            Assert.AreEqual(6, (int)SettlementBeatKind.DishCompleted);
            Assert.AreEqual(19, (int)SettlementDishFeedbackKind.TemporaryCategoryApplied);
            Assert.AreEqual(20, (int)SettlementDishFeedbackKind.DishChapterStarted);
            Assert.AreEqual(21, (int)SettlementDishFeedbackKind.DishChapterCompleted);
        }

        [Test]
        public void CameraTweenDuration_KeepsMotionReadableAtHighPlaybackSpeed()
        {
            Assert.AreEqual(0.16f, SettlementSequencer.CameraFocusTweenDuration(0.04f), 0.0001f);
            Assert.AreEqual(0.28f, SettlementSequencer.CameraFocusTweenDuration(1f), 0.0001f);
            Assert.AreEqual(0.16f, SettlementSequencer.CameraHomeTweenDuration(0.04f), 0.0001f);
            Assert.AreEqual(0.24f, SettlementSequencer.CameraHomeTweenDuration(1f), 0.0001f);
        }

        [Test]
        public void ResultVisualDishInstanceIds_SweetTransferResponse_ReturnsEveryBuffTarget()
        {
            var trace = new SkillExecutionTrace(
                SkillExecutionKind.NativeSkill,
                ownerDishInstanceId: 10,
                ownerDishId: "popping_candy",
                ownerDishName: "跳跳糖",
                runtimeSelfDishInstanceId: 20,
                runtimeSelfDishId: "transfer_source",
                runtimeSelfDishName: "传递来源",
                skillId: "sk_popping_candy",
                skillName: "跳跳糖",
                ruleId: "sk_popping_candy_1",
                ruleOrder: 0,
                trigger: SkillTrigger.OnSettle,
                actionType: SkillActionType.AddFlat,
                conditionType: SkillConditionType.None,
                conditionScope: SkillScope.Self,
                actionScope: SkillScope.ColumnAndSelf,
                sourceLabel: "跳跳糖",
                visualTargetDishInstanceIds: new[] { 10, 30, 40, 30 });
            var line = new ScoreLine(
                ScorePhase.DishSkills,
                ScoreLineKind.SweetTransferBuffTriggered,
                source: null,
                dishInstanceId: 10,
                dishId: "popping_candy",
                cell: null,
                value: 50,
                before: BigDouble.Zero,
                after: 50,
                message: string.Empty,
                trace);

            CollectionAssert.AreEqual(
                new[] { 10, 30, 40 },
                SettlementSequencer.ResultVisualDishInstanceIds(line));
        }

        [Test]
        public void SweetTransferParticle_Begin_CreatesLayeredAfterimagesWithoutRibbonTrail()
        {
            const string PrefabPath =
                "Assets/GameMain/Content/Prefabs/Battle/Effects/SweetTransferParticle.prefab";
            SweetTransferParticleView prefab =
                AssetDatabase.LoadAssetAtPath<SweetTransferParticleView>(PrefabPath);
            var parent = new GameObject("SweetTransferParticleTestRoot");
            SweetTransferParticleView view = null;

            try
            {
                Assert.IsNotNull(prefab, $"找不到甜蜜传递粒子 prefab：{PrefabPath}");
                view = SweetTransferParticleView.Begin(
                    prefab,
                    parent.transform,
                    Vector3.zero,
                    Vector3.one,
                    duration: 0.32f);

                Assert.IsNotNull(view);
                Assert.IsNull(view.GetComponent<TrailRenderer>(), "不应再生成连续色带式 TrailRenderer。");

                Transform afterimageRoot = parent.transform.Find("SweetTransferAfterimages");
                Assert.IsNotNull(afterimageRoot);
                Assert.GreaterOrEqual(afterimageRoot.childCount, 24);
                Assert.IsTrue(afterimageRoot.GetChild(0).name.StartsWith(
                    "Afterimage_",
                    StringComparison.Ordinal));
            }
            finally
            {
                if (view != null)
                {
                    UnityEngine.Object.DestroyImmediate(view.gameObject);
                }

                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void SettlementStagePrefab_UsesDistinctDishBrightnessHierarchy()
        {
            const string PrefabPath =
                "Assets/GameMain/Content/Prefabs/Battle/Settlement/SettlementStage.prefab";
            SettlementStageView prefab = AssetDatabase.LoadAssetAtPath<SettlementStageView>(PrefabPath);

            Assert.IsNotNull(prefab, $"找不到结算舞台 prefab：{PrefabPath}");
            var serialized = new SerializedObject(prefab);
            float completed = serialized.FindProperty("_completedDishBrightness").floatValue;
            float pending = serialized.FindProperty("_pendingDishBrightness").floatValue;
            float scope = serialized.FindProperty("_scopeDishBrightness").floatValue;
            float sweetTransferSource = serialized
                .FindProperty("_sweetTransferSourceBrightness")
                .floatValue;

            Assert.Less(pending, completed, "未结算食物应比已结算食物更暗。");
            Assert.Less(pending, scope, "技能范围目标应显著亮于结算背景。");
            Assert.Less(scope, sweetTransferSource, "甜蜜传递辅助来源应比范围目标更突出。");
            Assert.Less(sweetTransferSource, 1f, "当前食物、执行者与命中目标保留全亮层级。");
        }

        [Test]
        public void CakeLayerBuff_ResultBatchesRepeatImpactFeedback()
        {
            ScoreSource cakeBuff = ScoreSource.TableTag("cake_layer_buff", "欢乐蛋糕层数");
            var group = new SettlementEffectGroup(
                LineWithSource(ScoreLineKind.DishFlat, cakeBuff));
            group.Append(LineWithSource(ScoreLineKind.DishMultiplierAdd, cakeBuff));
            group.Append(LineWithSource(ScoreLineKind.DishMultiplier, cakeBuff));

            List<List<int>> batches = SettlementSequencer.BuildResultLineBatches(group);

            Assert.AreEqual(3, batches.Count, "三种蛋糕 Buff 结果应保持三个连续批次。");
            Assert.IsTrue(
                SettlementSequencer.ShouldRepeatImpactPerResultBatch(group, batches.Count),
                "每个蛋糕 Buff 结果批次都应单独触发镜头反馈。");

            ScoreSource ordinarySource = ScoreSource.FinalModifier("ordinary", "普通结算");
            var ordinaryGroup = new SettlementEffectGroup(
                LineWithSource(ScoreLineKind.DishFlat, ordinarySource));
            Assert.IsFalse(
                SettlementSequencer.ShouldRepeatImpactPerResultBatch(ordinaryGroup, 3),
                "普通结算组仍应只触发一次镜头反馈。");
        }

        private static DishScore DishScoreFor(int dishInstanceId, string dishId)
        {
            return new DishScore(
                dishInstanceId,
                dishId,
                baseValue: 10,
                flatBonus: 0,
                multiplier: 1);
        }

        private static ScoreLine BaseLine(int dishInstanceId, string dishId)
        {
            return new ScoreLine(
                ScorePhase.DishBase,
                ScoreLineKind.DishBase,
                ScoreSource.FinalModifier("test", "测试"),
                dishInstanceId,
                dishId,
                cell: null,
                value: 10,
                before: 0,
                after: 10,
                message: string.Empty);
        }

        private static ScoreLine Line(
            ScorePhase phase,
            ScoreLineKind kind,
            int dishInstanceId,
            int executionGroupId)
        {
            return new ScoreLine(
                phase,
                kind,
                ScoreSource.FinalModifier($"source_{executionGroupId}", $"来源 {executionGroupId}"),
                dishInstanceId,
                dishInstanceId > 0 ? $"dish_{dishInstanceId}" : string.Empty,
                cell: null,
                value: 1,
                before: 0,
                after: 1,
                message: string.Empty,
                executionGroupId: executionGroupId);
        }

        private static ScoreLine LineWithSource(ScoreLineKind kind, ScoreSource source)
        {
            return new ScoreLine(
                ScorePhase.AfterAllDishes,
                kind,
                source,
                dishInstanceId: 1,
                dishId: "cake",
                cell: null,
                value: 1,
                before: 0,
                after: 1,
                message: string.Empty,
                executionGroupId: 42);
        }

        private static ScoreResult Result(
            IReadOnlyList<DishScore> scores,
            IReadOnlyList<ScoreLine> lines)
        {
            return new ScoreResult(
                scores,
                rawSum: 0,
                finalFlat: 0,
                finalMultiplier: 1,
                scoreLines: lines);
        }
    }
}
