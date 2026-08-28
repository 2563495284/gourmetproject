#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using BreakInfinity;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools.Utils;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SettlementResultLabelLayoutTests
    {
        private static readonly Vector2 Footprint = new(1.8f, 0.48f);
        private const float ViewportPadding = 0.035f;

        [Test]
        public void SettlementScorePresentation_UpdatesOnlyCurrentScoreText()
        {
            var root = new GameObject("SettlementScorePresentationTest");
            var scoreObject = new GameObject(
                "ScoreCurrent",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            var tableCountObject = new GameObject(
                "TableCount",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            var discardCountObject = new GameObject(
                "DiscardCount",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            try
            {
                scoreObject.transform.SetParent(root.transform, false);
                tableCountObject.transform.SetParent(root.transform, false);
                discardCountObject.transform.SetParent(root.transform, false);

                var infoColumn = root.AddComponent<BattleInfoColumn>();
                TMP_Text scoreText = scoreObject.GetComponent<TMP_Text>();
                TMP_Text tableCountText = tableCountObject.GetComponent<TMP_Text>();
                TMP_Text discardCountText = discardCountObject.GetComponent<TMP_Text>();
                SetPrivateField(infoColumn, "_scoreCurrentText", scoreText);
                SetPrivateField(infoColumn, "_viewTableCountText", tableCountText);
                SetPrivateField(infoColumn, "_discardCountText", discardCountText);

                scoreText.text = "旧分数";
                tableCountText.text = "餐桌哨兵";
                discardCountText.text = "弃置哨兵";

                BigDouble score = 123456;
                infoColumn.SetSettlementScorePresentation(score);

                Assert.That(scoreText.text, Is.EqualTo(ScoreNumberFormatter.Format(score)));
                Assert.That(tableCountText.text, Is.EqualTo("餐桌哨兵"));
                Assert.That(discardCountText.text, Is.EqualTo("弃置哨兵"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ScoreDeltaBeat_RequiresANonZeroTotalChange()
        {
            var zeroDelta = new SettlementBeatSignal(
                SettlementBeatKind.ResultApplied,
                "金币",
                0,
                1f,
                0.5f,
                ScoreLineKind.Gold,
                10,
                10,
                SettlementImpactTier.Normal,
                1,
                reachedTarget: false);
            var positiveDelta = new SettlementBeatSignal(
                SettlementBeatKind.ResultApplied,
                "技能",
                1,
                1f,
                0.5f,
                ScoreLineKind.DishFlat,
                10,
                15,
                SettlementImpactTier.Normal,
                1,
                reachedTarget: false);

            Assert.That(BattleInfoColumn.ShouldQueueSettlementScoreBeat(zeroDelta), Is.False);
            Assert.That(BattleInfoColumn.ShouldQueueSettlementScoreBeat(positiveDelta), Is.True);
            Assert.That(BattleInfoColumn.HasVisibleSettlementScoreDelta(5 - 5), Is.False);
        }

        [TestCase(1, (int)SettlementScoreFeedbackStep.Subtle)]
        [TestCase(49, (int)SettlementScoreFeedbackStep.Subtle)]
        [TestCase(50, (int)SettlementScoreFeedbackStep.Clear)]
        [TestCase(199, (int)SettlementScoreFeedbackStep.Clear)]
        [TestCase(200, (int)SettlementScoreFeedbackStep.Strong)]
        [TestCase(999, (int)SettlementScoreFeedbackStep.Strong)]
        [TestCase(1000, (int)SettlementScoreFeedbackStep.Burst)]
        [TestCase(4999, (int)SettlementScoreFeedbackStep.Burst)]
        [TestCase(5000, (int)SettlementScoreFeedbackStep.Peak)]
        public void ScoreDeltaFeedback_UsesFixedScoreSteps(
            int delta,
            int expectedStep)
        {
            SettlementScoreFeedbackProfile profile =
                SettlementScoreFeedbackResolver.Resolve(delta);

            Assert.That((int)profile.Step, Is.EqualTo(expectedStep));
            Assert.That(profile.Visible, Is.True);
        }

        [Test]
        public void ScoreDeltaFeedback_UsesPositiveAndNegativeColorAnchors()
        {
            AssertColor(1, new Color32(0xE8, 0xB8, 0x57, 0xFF));
            AssertColor(50, new Color32(0xFF, 0xD4, 0x5B, 0xFF));
            AssertColor(200, new Color32(0xFF, 0xAA, 0x3D, 0xFF));
            AssertColor(1000, new Color32(0xFF, 0x6A, 0x2A, 0xFF));
            AssertColor(5000, new Color32(0xFF, 0xF3, 0xD0, 0xFF));

            AssertColor(-1, new Color32(0xF2, 0xA1, 0xAE, 0xFF));
            AssertColor(-50, new Color32(0xFF, 0x74, 0x85, 0xFF));
            AssertColor(-200, new Color32(0xFF, 0x48, 0x5E, 0xFF));
            AssertColor(-1000, new Color32(0xF1, 0x26, 0x46, 0xFF));
            AssertColor(-5000, new Color32(0xFF, 0x12, 0x3D, 0xFF));
        }

        [TestCase(1)]
        [TestCase(49)]
        [TestCase(50)]
        [TestCase(90)]
        [TestCase(199)]
        [TestCase(200)]
        [TestCase(999)]
        [TestCase(1000)]
        [TestCase(4999)]
        [TestCase(5000)]
        public void ScoreDeltaFeedback_PositiveScoresStayInWarmHue(int delta)
        {
            SettlementScoreFeedbackProfile profile =
                SettlementScoreFeedbackResolver.Resolve(delta);

            Color.RGBToHSV(profile.TextColor, out float hue, out _, out _);

            Assert.That(
                hue,
                Is.LessThanOrEqualTo(0.18f),
                $"+{delta} 不应落入蓝色或绿色色相");
        }

        [TestCase(49.999d, 50d)]
        [TestCase(199.999d, 200d)]
        [TestCase(999.999d, 1000d)]
        [TestCase(4999.999d, 5000d)]
        public void ScoreDeltaFeedback_IsContinuousAtEveryStepBoundary(
            double justBelow,
            double boundaryScore)
        {
            SettlementScoreFeedbackProfile below =
                SettlementScoreFeedbackResolver.Resolve(justBelow);
            SettlementScoreFeedbackProfile boundary =
                SettlementScoreFeedbackResolver.Resolve(boundaryScore);

            Assert.That(
                Vector4.Distance(below.TextColor, boundary.TextColor),
                Is.LessThan(0.001f));
            Assert.That(below.Intensity, Is.LessThanOrEqualTo(boundary.Intensity));
        }

        [Test]
        public void ScoreDeltaFeedback_CapsHugeValues()
        {
            SettlementScoreFeedbackProfile huge =
                SettlementScoreFeedbackResolver.Resolve(BigDouble.Normalize(1d, 100));

            Assert.That(huge.Step, Is.EqualTo(SettlementScoreFeedbackStep.Peak));
            Assert.That(huge.Intensity, Is.EqualTo(1f));
            Assert.That(huge.ImpactScale, Is.EqualTo(1.75f).Within(0.0001f));
            Assert.That(huge.SettleScale, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(huge.SettleDuration, Is.EqualTo(0.22f).Within(0.0001f));
        }

        [Test]
        public void ScoreBeatAccumulator_MergesCrossStepAndNetZeroBatches()
        {
            var accumulator = new SettlementScoreBeatAccumulator();
            accumulator.Add(CreateScoreBeat(0, 49));
            accumulator.Add(CreateScoreBeat(49, 50));

            SettlementScoreBeatAggregate crossStep = accumulator.Consume();

            Assert.That(crossStep.BeforeScore, Is.EqualTo((BigDouble)0));
            Assert.That(crossStep.AfterScore, Is.EqualTo((BigDouble)50));
            Assert.That(crossStep.Delta, Is.EqualTo((BigDouble)50));
            Assert.That(accumulator.HasPending, Is.False);

            accumulator.Add(CreateScoreBeat(0, 200));
            accumulator.Add(CreateScoreBeat(200, 0));
            SettlementScoreBeatAggregate cancelled = accumulator.Consume();

            Assert.That(cancelled.Delta, Is.EqualTo(BigDouble.Zero));
            Assert.That(BattleInfoColumn.HasVisibleSettlementScoreDelta(cancelled.Delta), Is.False);
        }

        [Test]
        public void ScoreDeltaFeedback_UsesNetDeltaAndStationaryImpactMotion()
        {
            SettlementScoreFeedbackProfile positive =
                SettlementScoreFeedbackResolver.Resolve(500d);
            SettlementScoreFeedbackProfile negative =
                SettlementScoreFeedbackResolver.Resolve(-500d);
            SettlementScoreFeedbackProfile cancelled =
                SettlementScoreFeedbackResolver.Resolve(500d - 500d);

            Assert.That(positive.Intensity, Is.EqualTo(negative.Intensity).Within(0.0001f));
            Assert.That(positive.ImpactScale, Is.GreaterThan(positive.SettleScale));
            Assert.That(positive.ShakeStrength, Is.Zero);
            Assert.That(negative.ImpactScale, Is.EqualTo(positive.ImpactScale).Within(0.0001f));
            Assert.That(negative.SettleScale, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(negative.ShakeStrength, Is.GreaterThan(0f));
            Assert.That(negative.FireStrength, Is.Zero);
            Assert.That(cancelled.Visible, Is.False);
        }

        [Test]
        public void ScoreDeltaFeedback_ImpactScaleGrowsAndCapsAcrossFixedSteps()
        {
            SettlementScoreFeedbackProfile subtle =
                SettlementScoreFeedbackResolver.Resolve(1d);
            SettlementScoreFeedbackProfile clear =
                SettlementScoreFeedbackResolver.Resolve(90d);
            SettlementScoreFeedbackProfile strong =
                SettlementScoreFeedbackResolver.Resolve(500d);
            SettlementScoreFeedbackProfile burst =
                SettlementScoreFeedbackResolver.Resolve(2000d);
            SettlementScoreFeedbackProfile peak =
                SettlementScoreFeedbackResolver.Resolve(5000d);

            Assert.That(subtle.ImpactScale, Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(clear.ImpactScale, Is.GreaterThan(subtle.ImpactScale));
            Assert.That(strong.ImpactScale, Is.GreaterThan(clear.ImpactScale));
            Assert.That(burst.ImpactScale, Is.GreaterThan(strong.ImpactScale));
            Assert.That(peak.ImpactScale, Is.EqualTo(1.75f).Within(0.0001f));
            Assert.That(peak.SettleScale, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void ScoreDeltaFeedback_UsesReadableOutlineWidthsAtEveryIntensity()
        {
            SettlementScoreFeedbackProfile subtle =
                SettlementScoreFeedbackResolver.Resolve(1d);
            SettlementScoreFeedbackProfile peak =
                SettlementScoreFeedbackResolver.Resolve(5000d);

            Assert.That(subtle.OutlineWidth, Is.EqualTo(0.16f).Within(0.0001f));
            Assert.That(peak.OutlineWidth, Is.EqualTo(0.24f).Within(0.0001f));
            Assert.That(peak.OutlineWidth, Is.GreaterThan(subtle.OutlineWidth));
        }

        [Test]
        public void ScoreDeltaOutline_EnablesTmpOutlineShaderKeyword()
        {
            var textObject = new GameObject(
                "SettlementScoreDeltaOutlineTest",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            Material runtimeMaterial = null;
            try
            {
                var text = textObject.GetComponent<TextMeshProUGUI>();
                TMP_FontAsset font = Resources.Load<TMP_FontAsset>(
                    "Fonts/AlimamaShuHeiTi-Bold SDF");
                Assert.That(font, Is.Not.Null);
                runtimeMaterial = new Material(font.material);
                text.font = font;
                text.fontSharedMaterial = runtimeMaterial;
                var outlineColor = new Color32(0x6E, 0x45, 0x15, 0xFF);

                BattleInfoColumn.ApplySettlementScoreDeltaOutline(
                    text,
                    outlineColor,
                    0.20f);

                Material configuredMaterial = text.fontSharedMaterial;
                Assert.That(
                    configuredMaterial.IsKeywordEnabled(ShaderUtilities.Keyword_Outline),
                    Is.True);
                Assert.That(
                    configuredMaterial.GetFloat(ShaderUtilities.ID_OutlineWidth),
                    Is.EqualTo(0.20f).Within(0.0001f));
                Assert.That(
                    configuredMaterial.GetColor(ShaderUtilities.ID_OutlineColor),
                    Is.EqualTo((Color)outlineColor)
                        .Using(ColorEqualityComparer.Instance));
            }
            finally
            {
                Object.DestroyImmediate(textObject);
                if (runtimeMaterial != null)
                {
                    Object.DestroyImmediate(runtimeMaterial);
                }
            }
        }

        [Test]
        public void ScoreDeltaLayer_IsReparentedAboveBossStatWithoutMoving()
        {
            var root = new GameObject("BattleInfoColumn", typeof(RectTransform));
            var scoreMeter = new GameObject("ScoreMeter", typeof(RectTransform));
            var delta = new GameObject("SettlementScoreDeltaText", typeof(RectTransform));
            var bossStat = new GameObject("BossStat", typeof(RectTransform));
            try
            {
                scoreMeter.transform.SetParent(root.transform, false);
                delta.transform.SetParent(scoreMeter.transform, false);
                bossStat.transform.SetParent(root.transform, false);
                delta.transform.position = new Vector3(23f, 47f, 0f);
                Vector3 worldPosition = delta.transform.position;

                BattleInfoColumn.PlaceSettlementScoreDeltaAboveBossStat(
                    delta.GetComponent<RectTransform>(),
                    bossStat.transform,
                    root.transform);

                Assert.That(delta.transform.parent, Is.SameAs(root.transform));
                Assert.That(
                    delta.transform.GetSiblingIndex(),
                    Is.GreaterThan(bossStat.transform.GetSiblingIndex()));
                Assert.That(
                    delta.transform.position,
                    Is.EqualTo(worldPosition)
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ScoreDeltaFireFeedback_FollowsFixedPositiveThresholdsAndPace()
        {
            AssertFire(49, SettlementPacePhase.DoubleTarget, SettlementScoreFireFeedback.None);
            AssertFire(50, SettlementPacePhase.BelowTarget, SettlementScoreFireFeedback.None);
            AssertFire(50, SettlementPacePhase.TargetReached, SettlementScoreFireFeedback.IgnitedBurst);
            AssertFire(199, SettlementPacePhase.BelowTarget, SettlementScoreFireFeedback.None);
            AssertFire(200, SettlementPacePhase.BelowTarget, SettlementScoreFireFeedback.TransientSpark);
            AssertFire(200, SettlementPacePhase.TargetReached, SettlementScoreFireFeedback.IgnitedBurst);
            AssertFire(5000, SettlementPacePhase.DoubleTarget, SettlementScoreFireFeedback.IgnitedBurst);
            AssertFire(-5000, SettlementPacePhase.DoubleTarget, SettlementScoreFireFeedback.None);
            AssertFire(0, SettlementPacePhase.DoubleTarget, SettlementScoreFireFeedback.None);
        }

        [Test]
        public void ScoreFire_TransientBurstEmitsSparksWithoutIgnitingContinuousFire()
        {
            var fireObject = new GameObject(
                "SettlementScoreFireTransientTest",
                typeof(RectTransform));
            try
            {
                SettlementScoreFireView fire =
                    fireObject.AddComponent<SettlementScoreFireView>();
                fire.Show();
                fire.SetPhase(SettlementPacePhase.BelowTarget, 1f);

                fire.BurstTransient(1f);

                Assert.That(fire.Phase, Is.EqualTo(SettlementPacePhase.BelowTarget));
                Assert.That(fire.TransientSparkActive, Is.True);
                Assert.That(fire.ActiveMoteCount, Is.GreaterThan(0));
                Assert.That(fire.CoreEmissionRate, Is.Zero);
                Assert.That(fire.TongueEmissionRate, Is.Zero);
                Assert.That(fire.EmberEmissionRate, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(fireObject);
            }
        }

        [Test]
        public void ScoreFire_CoalescesRepeatedBurstRequestsBeforeScreenFeverEmission()
        {
            var rootObject = new GameObject("SettlementBurstRoot", typeof(RectTransform));
            var meterObject = new GameObject("ScoreMeter", typeof(RectTransform));
            var scoreObject = new GameObject("Score", typeof(RectTransform));
            var fireObject = new GameObject("SettlementScoreFire", typeof(RectTransform));
            try
            {
                meterObject.transform.SetParent(rootObject.transform, false);
                scoreObject.transform.SetParent(meterObject.transform, false);
                fireObject.transform.SetParent(rootObject.transform, false);
                var fire = fireObject.AddComponent<SettlementScoreFireView>();
                fire.BindToScore(
                    scoreObject.GetComponent<RectTransform>(),
                    rootObject.GetComponent<RectTransform>());
                fire.Show();
                fire.SetPhase(SettlementPacePhase.TargetReached, 1f);
                int feverMotesBeforeBurst = fire.ScreenFever.ActiveMoteCount;

                fire.Burst(0.35f);
                fire.Burst(0.90f);

                Assert.That(fire.QueuedBurstStrength, Is.EqualTo(0.90f).Within(0.0001f));
                Assert.That(
                    fire.ScreenFever.ActiveMoteCount,
                    Is.EqualTo(feverMotesBeforeBurst),
                    "Fever 粒子应在合并后的爆燃真正执行时只发射一次");
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        public void BatchVisibility_SkipsNonVisualSourceLines()
        {
            ScoreLine hiddenSourceLine = CreateLine(ScoreLineKind.TriggeredSweetTransferSource);
            ScoreLine visibleLine = CreateLine(ScoreLineKind.DishFlat);

            Assert.That(
                SettlementSequencer.ShouldShowResultLabelInBatch(hiddenSourceLine, false),
                Is.False);
            Assert.That(
                SettlementSequencer.ShouldShowResultLabelInBatch(visibleLine, false),
                Is.True);
        }

        [Test]
        public void CakeLayerBurstProfiles_ThreeStepsEscalateAndMapSemantics()
        {
            CakeLayerBurstStepProfile flat =
                SettlementSequencer.ResolveCakeLayerBurstStepProfile(
                    0,
                    3,
                    ScoreLineKind.DishFlat);
            CakeLayerBurstStepProfile addMultiplier =
                SettlementSequencer.ResolveCakeLayerBurstStepProfile(
                    1,
                    3,
                    ScoreLineKind.DishMultiplierAdd);
            CakeLayerBurstStepProfile multiply =
                SettlementSequencer.ResolveCakeLayerBurstStepProfile(
                    2,
                    3,
                    ScoreLineKind.DishMultiplier);

            Assert.That(flat.ImpactTier, Is.EqualTo(SettlementImpactTier.Strong));
            Assert.That(addMultiplier.ImpactTier, Is.EqualTo(SettlementImpactTier.Chain));
            Assert.That(multiply.ImpactTier, Is.EqualTo(SettlementImpactTier.Finale));
            Assert.That(flat.AudioPitch, Is.LessThan(addMultiplier.AudioPitch));
            Assert.That(addMultiplier.AudioPitch, Is.LessThan(multiply.AudioPitch));
            Assert.That(flat.CakePulseStrength, Is.LessThan(addMultiplier.CakePulseStrength));
            Assert.That(addMultiplier.CakePulseStrength, Is.LessThan(multiply.CakePulseStrength));
            Assert.That(flat.CameraImpactStrength, Is.LessThan(addMultiplier.CameraImpactStrength));
            Assert.That(addMultiplier.CameraImpactStrength, Is.LessThan(multiply.CameraImpactStrength));
            Assert.That(
                SettlementSequencer.CameraImpactStrengthFor(
                    SettlementPacePhase.BelowTarget,
                    SettlementImpactTier.Finale),
                Is.Zero,
                "普通技能的未达标镜头规则不应被蛋糕专属脉冲改写");
            Assert.That(flat.CameraImpactStrength, Is.GreaterThan(0f));
            Assert.That(flat.AnticipationDuration, Is.EqualTo(0.08f).Within(0.0001f));
            Assert.That(addMultiplier.AnticipationDuration, Is.EqualTo(0.12f).Within(0.0001f));
            Assert.That(multiply.AnticipationDuration, Is.EqualTo(0.16f).Within(0.0001f));
            Assert.That(flat.DishFeedbackKind, Is.EqualTo(SettlementDishFeedbackKind.CakeLayerFlatBurst));
            Assert.That(addMultiplier.DishFeedbackKind, Is.EqualTo(SettlementDishFeedbackKind.CakeLayerMultiplierAddBurst));
            Assert.That(multiply.DishFeedbackKind, Is.EqualTo(SettlementDishFeedbackKind.CakeLayerMultiplierBurst));
            Assert.That(
                SettlementSequencer.CakeLayerChargeDuration
                + flat.AnticipationDuration
                + addMultiplier.AnticipationDuration
                + multiply.AnticipationDuration,
                Is.LessThanOrEqualTo(0.8f));
        }

        [Test]
        public void CakeLayerBurstProfiles_SingleAndDoubleStepUseStrongToFinaleFallbacks()
        {
            CakeLayerBurstStepProfile single =
                SettlementSequencer.ResolveCakeLayerBurstStepProfile(
                    0,
                    1,
                    ScoreLineKind.DishFlat);
            CakeLayerBurstStepProfile doubleFirst =
                SettlementSequencer.ResolveCakeLayerBurstStepProfile(
                    0,
                    2,
                    ScoreLineKind.DishFlat);
            CakeLayerBurstStepProfile doubleFinal =
                SettlementSequencer.ResolveCakeLayerBurstStepProfile(
                    1,
                    2,
                    ScoreLineKind.DishMultiplier);

            Assert.That(single.ImpactTier, Is.EqualTo(SettlementImpactTier.Strong));
            Assert.That(single.StepNumber, Is.EqualTo(1));
            Assert.That(single.StepCount, Is.EqualTo(1));
            Assert.That(doubleFirst.ImpactTier, Is.EqualTo(SettlementImpactTier.Strong));
            Assert.That(doubleFinal.ImpactTier, Is.EqualTo(SettlementImpactTier.Finale));
            Assert.That(doubleFirst.AudioPitch, Is.LessThan(doubleFinal.AudioPitch));
        }

        [Test]
        public void CakeLayerBurstLabels_ExposeStepAndOperation()
        {
            CakeLayerBurstStepProfile flat =
                SettlementSequencer.ResolveCakeLayerBurstStepProfile(
                    0,
                    3,
                    ScoreLineKind.DishFlat);
            CakeLayerBurstStepProfile addMultiplier =
                SettlementSequencer.ResolveCakeLayerBurstStepProfile(
                    1,
                    3,
                    ScoreLineKind.DishMultiplierAdd);
            CakeLayerBurstStepProfile multiply =
                SettlementSequencer.ResolveCakeLayerBurstStepProfile(
                    2,
                    3,
                    ScoreLineKind.DishMultiplier);

            Assert.That(
                SettlementStageView.CakeLayerBurstResultHeader(
                    CreateLine(ScoreLineKind.DishFlat),
                    flat),
                Is.EqualTo("1/3 加分"));
            Assert.That(
                SettlementStageView.CakeLayerBurstResultHeader(
                    CreateLine(ScoreLineKind.DishMultiplierAdd),
                    addMultiplier),
                Is.EqualTo("2/3 倍率 +"));
            Assert.That(
                SettlementStageView.CakeLayerBurstResultHeader(
                    CreateLine(ScoreLineKind.DishMultiplier),
                    multiply),
                Is.EqualTo("3/3 倍率 ×"));
        }

        [Test]
        public void CakeLayerBurstBatching_PreservesEveryLineAndRealKindOrder()
        {
            ScoreSource source = ScoreSource.TableTag(
                "cake_layer_buff",
                "欢乐蛋糕层数");
            var group = new SettlementEffectGroup(
                CreateCakeLayerLine(source, ScoreLineKind.DishFlat, 1));
            group.Append(CreateCakeLayerLine(source, ScoreLineKind.DishFlat, 2));
            group.Append(CreateCakeLayerLine(source, ScoreLineKind.DishMultiplierAdd, 1));
            group.Append(CreateCakeLayerLine(source, ScoreLineKind.DishMultiplierAdd, 2));
            group.Append(CreateCakeLayerLine(source, ScoreLineKind.DishMultiplier, 1));
            group.Append(CreateCakeLayerLine(source, ScoreLineKind.DishMultiplier, 2));

            List<List<int>> batches = SettlementSequencer.BuildResultLineBatches(group);

            Assert.That(batches.Count, Is.EqualTo(3));
            Assert.That(batches[0], Is.EqualTo(new[] { 0, 1 }));
            Assert.That(batches[1], Is.EqualTo(new[] { 2, 3 }));
            Assert.That(batches[2], Is.EqualTo(new[] { 4, 5 }));
            Assert.That(
                batches[0].Count + batches[1].Count + batches[2].Count,
                Is.EqualTo(group.Lines.Count));
            Assert.That(
                SettlementSequencer.ShouldRepeatImpactPerResultBatch(group, batches.Count),
                Is.True);
        }

        [Test]
        public void CakeLayerBurstBatching_DoesNotChangeOtherTableTagGroups()
        {
            ScoreSource source = ScoreSource.TableTag("other_buff", "其他 Buff");
            var group = new SettlementEffectGroup(
                CreateCakeLayerLine(source, ScoreLineKind.DishFlat, 1));
            group.Append(CreateCakeLayerLine(
                source,
                ScoreLineKind.DishMultiplierAdd,
                1));
            group.Append(CreateCakeLayerLine(
                source,
                ScoreLineKind.DishMultiplier,
                1));

            List<List<int>> batches = SettlementSequencer.BuildResultLineBatches(group);

            Assert.That(batches.Count, Is.EqualTo(1));
            Assert.That(batches[0], Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(
                SettlementSequencer.ShouldRepeatImpactPerResultBatch(group, 3),
                Is.False);
        }

        [Test]
        public void CakeLayerWorldFx_ResetAndDisableRestoreCapturedScaleAndColor()
        {
            var root = new GameObject("CakeLayerBurstResetTest");
            var cakeObject = new GameObject(
                "Cake",
                typeof(SpriteRenderer));
            try
            {
                var fx = root.AddComponent<CakeLayerWorldFx>();
                var renderer = cakeObject.GetComponent<SpriteRenderer>();
                cakeObject.transform.SetParent(root.transform, false);
                cakeObject.transform.localScale = new Vector3(1.17f, 0.93f, 1f);
                renderer.color = new Color(0.82f, 0.74f, 0.66f, 0.9f);
                Vector3 originalScale = cakeObject.transform.localScale;
                Color originalColor = renderer.color;
                FieldInfo cakesField = typeof(CakeLayerWorldFx).GetField(
                    "_cakes",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(cakesField, Is.Not.Null);
                var cakes = (List<SpriteRenderer>)cakesField.GetValue(fx);
                cakes.Add(renderer);

                fx.BeginBuffCharge(0.28f);
                cakeObject.transform.localScale = Vector3.one * 3f;
                renderer.color = Color.magenta;
                MethodInfo onDisable = typeof(CakeLayerWorldFx).GetMethod(
                    "OnDisable",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(onDisable, Is.Not.Null);
                onDisable.Invoke(fx, null);

                Assert.That(
                    cakeObject.transform.localScale,
                    Is.EqualTo(originalScale)
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(
                    renderer.color,
                    Is.EqualTo(originalColor)
                        .Using(ColorEqualityComparer.Instance));

                fx.BeginBuffCharge(0.28f);
                cakeObject.transform.localScale = Vector3.one * 3f;
                renderer.color = Color.magenta;
                fx.ResetBuffBurstVisuals();

                Assert.That(
                    cakeObject.transform.localScale,
                    Is.EqualTo(originalScale)
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(
                    renderer.color,
                    Is.EqualTo(originalColor)
                        .Using(ColorEqualityComparer.Instance));
                Assert.DoesNotThrow(() => fx.PlayBuffPulse(
                    1f,
                    SettlementColorPalette.MultiplyMultiplier,
                    0.34f));
                fx.ResetBuffBurstVisuals();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OneLabelPerTarget_KeepsOriginalAnchorWithoutDrift()
        {
            Camera camera = CreateCamera();
            try
            {
                Vector3 firstTarget = ViewportWorld(camera, 0.15f, 0.80f);
                Vector3 secondTarget = ViewportWorld(camera, 0.85f, 0.20f);
                ResultLabelLayoutPlan plan = Resolve(
                    camera,
                    Request(10, firstTarget),
                    Request(20, secondTarget));

                AssertPlacement(plan[0], firstTarget, 0f, 0);
                AssertPlacement(plan[1], secondTarget, 0f, 0);
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test]
        public void MultipleLabels_KeepFirstAtAnchorAndExpandDownRight()
        {
            Camera camera = CreateCamera();
            try
            {
                Vector3 anchor = ViewportWorld(camera, 0.5f, 0.5f);
                ResultLabelLayoutPlan plan = Resolve(
                    camera,
                    Request(10, anchor),
                    Request(10, anchor),
                    Request(10, anchor),
                    Request(10, anchor));

                AssertPlacement(plan[0], anchor, 0f, 0);
                for (int i = 1; i < plan.Count; i++)
                {
                    Assert.That(plan[i].Position.x, Is.GreaterThan(anchor.x));
                    Assert.That(plan[i].Position.y, Is.LessThan(anchor.y));
                    Assert.That(plan[i].VerticalDirection, Is.EqualTo(-1f));
                    Assert.That(plan[i].StackIndex, Is.EqualTo(i));
                }
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test]
        public void FourLabels_UseCompactTwentyPercentVerticalOverlap()
        {
            Vector3 anchor = new(2f, 3f, 0f);
            ResultLabelLayoutPlan plan = Resolve(
                null,
                Request(10, anchor),
                Request(10, anchor),
                Request(10, anchor),
                Request(10, anchor));
            float expectedVerticalPitch = Footprint.y
                * SettlementResultLabelLayout.VerticalPitchRatio;

            Assert.That(
                plan[1].Position.x - anchor.x,
                Is.EqualTo(
                    Footprint.x
                    * SettlementResultLabelLayout.FirstHorizontalOffsetRatio)
                .Within(0.0001f));
            Assert.That(
                anchor.y - plan[1].Position.y,
                Is.EqualTo(
                    Footprint.y
                    * SettlementResultLabelLayout.FirstVerticalOffsetRatio)
                .Within(0.0001f));
            for (int i = 2; i < plan.Count; i++)
            {
                Assert.That(
                    plan[i - 1].Position.y - plan[i].Position.y,
                    Is.EqualTo(expectedVerticalPitch).Within(0.0001f));
                Assert.That(plan[i].Position.x, Is.GreaterThan(plan[i - 1].Position.x));
            }

            Assert.That(
                Footprint.y - expectedVerticalPitch,
                Is.EqualTo(Footprint.y * 0.20f).Within(0.0001f));
        }

        [Test]
        public void ManyLabels_CapHorizontalStaggerWhileContinuingDownward()
        {
            Vector3 anchor = new(2f, 3f, 0f);
            var requests = new ResultLabelLayoutRequest[8];
            for (int i = 0; i < requests.Length; i++)
            {
                requests[i] = Request(10, anchor);
            }

            ResultLabelLayoutPlan plan = Resolve(null, requests);
            float maximumHorizontalOffset = Footprint.x
                * SettlementResultLabelLayout.MaximumHorizontalOffsetRatio;
            Assert.That(
                plan[^1].Position.x - anchor.x,
                Is.EqualTo(maximumHorizontalOffset).Within(0.0001f));
            for (int i = 2; i < plan.Count; i++)
            {
                Assert.That(plan[i].Position.y, Is.LessThan(plan[i - 1].Position.y));
                Assert.That(plan[i].StackIndex, Is.GreaterThan(plan[i - 1].StackIndex));
            }
        }

        [Test]
        public void DifferentTargets_KeepIndependentOriginalAndStackIndices()
        {
            Camera camera = CreateCamera();
            try
            {
                Vector3 targetA = ViewportWorld(camera, 0.35f, 0.5f);
                Vector3 targetB = ViewportWorld(camera, 0.65f, 0.5f);
                ResultLabelLayoutPlan plan = Resolve(
                    camera,
                    Request(10, targetA),
                    Request(20, targetB),
                    Request(10, targetA),
                    Request(20, targetB));

                AssertPlacement(plan[0], targetA, 0f, 0);
                AssertPlacement(plan[1], targetB, 0f, 0);
                Assert.That(plan[2].Position.x, Is.GreaterThan(targetA.x));
                Assert.That(plan[2].Position.y, Is.LessThan(targetA.y));
                Assert.That(plan[2].StackIndex, Is.EqualTo(1));
                Assert.That(plan[3].Position.x, Is.GreaterThan(targetB.x));
                Assert.That(plan[3].Position.y, Is.LessThan(targetB.y));
                Assert.That(plan[3].StackIndex, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test]
        public void ViewportEdges_NeverPullOffsetsLeftOrUpTowardOriginalAnchor()
        {
            Camera camera = CreateCamera();
            try
            {
                Vector2[] targetViewports =
                {
                    new(0.06f, 0.06f),
                    new(0.94f, 0.06f),
                    new(0.06f, 0.94f),
                    new(0.94f, 0.94f),
                };
                for (int targetIndex = 0; targetIndex < targetViewports.Length; targetIndex++)
                {
                    Vector2 viewport = targetViewports[targetIndex];
                    Vector3 target = ViewportWorld(camera, viewport.x, viewport.y);
                    var requests = new ResultLabelLayoutRequest[4];
                    for (int i = 0; i < requests.Length; i++)
                    {
                        requests[i] = Request(targetIndex + 1, target);
                    }

                    ResultLabelLayoutPlan raw = Resolve(null, requests);
                    ResultLabelLayoutPlan fitted = Resolve(camera, requests);
                    AssertPlacement(fitted[0], target, 0f, 0);
                    for (int i = 1; i < fitted.Count; i++)
                    {
                        Assert.That(fitted[i].Position.x, Is.GreaterThan(target.x));
                        Assert.That(fitted[i].Position.y, Is.LessThan(target.y));
                        Assert.That(
                            fitted[i].Position.x - target.x,
                            Is.GreaterThanOrEqualTo(raw[i].Position.x - target.x - 0.0001f));
                        Assert.That(
                            target.y - fitted[i].Position.y,
                            Is.GreaterThanOrEqualTo(target.y - raw[i].Position.y - 0.0001f));
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test]
        public void ViewportFit_AppliesWhenTranslationAlsoMovesDownRight()
        {
            Camera camera = CreateCamera();
            try
            {
                Vector3 target = ViewportWorld(camera, 0.06f, 0.98f);
                ResultLabelLayoutRequest[] requests =
                {
                    Request(10, target),
                    Request(10, target),
                    Request(10, target),
                };

                ResultLabelLayoutPlan raw = Resolve(null, requests);
                ResultLabelLayoutPlan fitted = Resolve(camera, requests);

                Assert.That(fitted[1].Position.x, Is.GreaterThan(raw[1].Position.x));
                Assert.That(fitted[1].Position.y, Is.LessThan(raw[1].Position.y));
                AssertFootprintsInsideSafeViewport(camera, fitted, 1);
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test]
        public void MissingCamera_PreservesDeterministicScaleAwareOffsets()
        {
            Vector3 target = new(2f, 3f, 0f);
            ResultLabelLayoutRequest request = Request(10, target);
            ResultLabelLayoutRequest[] requests = { request, request };

            ResultLabelLayoutPlan normal = SettlementResultLabelLayout.ResolveBatch(
                null,
                requests,
                Footprint);
            ResultLabelLayoutPlan doubled = SettlementResultLabelLayout.ResolveBatch(
                null,
                requests,
                Footprint * 2f);

            Assert.That(
                doubled[1].Position - target,
                Is.EqualTo((normal[1].Position - target) * 2f)
                .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(doubled[1].StackIndex, Is.EqualTo(normal[1].StackIndex));
        }

        private static Camera CreateCamera()
        {
            var cameraObject = new GameObject("SettlementResultLabelLayoutTestCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.aspect = 16f / 9f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            return camera;
        }

        private static void SetPrivateField(object owner, string fieldName, object value)
        {
            FieldInfo field = owner.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field: {fieldName}");
            field.SetValue(owner, value);
        }

        private static Vector3 ViewportWorld(Camera camera, float x, float y)
        {
            return camera.ViewportToWorldPoint(new Vector3(x, y, 10f));
        }

        private static ResultLabelLayoutRequest Request(int targetKey, Vector3 anchor)
        {
            return new ResultLabelLayoutRequest(targetKey, anchor);
        }

        private static ResultLabelLayoutPlan Resolve(
            Camera camera,
            params ResultLabelLayoutRequest[] requests)
        {
            return SettlementResultLabelLayout.ResolveBatch(
                camera,
                requests,
                Footprint,
                ViewportPadding);
        }

        private static void AssertPlacement(
            ResultLabelLayoutPlacement placement,
            Vector3 expectedPosition,
            float expectedDrift,
            int expectedStackIndex)
        {
            Assert.That(
                placement.Position,
                Is.EqualTo(expectedPosition)
                .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(placement.VerticalDirection, Is.EqualTo(expectedDrift));
            Assert.That(placement.StackIndex, Is.EqualTo(expectedStackIndex));
        }

        private static void AssertFootprintsInsideSafeViewport(
            Camera camera,
            ResultLabelLayoutPlan plan,
            int startIndex = 0)
        {
            float halfWidth = Footprint.x
                / (camera.orthographicSize * 2f * camera.aspect)
                * 0.5f;
            float halfHeight = Footprint.y
                / (camera.orthographicSize * 2f)
                * 0.5f;
            for (int i = Mathf.Max(0, startIndex); i < plan.Count; i++)
            {
                Vector3 viewport = camera.WorldToViewportPoint(plan[i].Position);
                Assert.That(
                    viewport.x - halfWidth,
                    Is.GreaterThanOrEqualTo(ViewportPadding - 0.0001f));
                Assert.That(
                    viewport.x + halfWidth,
                    Is.LessThanOrEqualTo(1f - ViewportPadding + 0.0001f));
                Assert.That(
                    viewport.y - halfHeight,
                    Is.GreaterThanOrEqualTo(ViewportPadding - 0.0001f));
                Assert.That(
                    viewport.y + halfHeight,
                    Is.LessThanOrEqualTo(1f - ViewportPadding + 0.0001f));
            }
        }

        private static void AssertColor(BigDouble delta, Color32 expected)
        {
            SettlementScoreFeedbackProfile profile =
                SettlementScoreFeedbackResolver.Resolve(delta);
            Assert.That(
                profile.TextColor,
                Is.EqualTo((Color)expected).Using(ColorEqualityComparer.Instance));
        }

        private static void AssertFire(
            BigDouble delta,
            SettlementPacePhase phase,
            SettlementScoreFireFeedback expected)
        {
            SettlementScoreFeedbackProfile profile =
                SettlementScoreFeedbackResolver.Resolve(delta);
            Assert.That(
                SettlementScoreFeedbackResolver.ResolveFireFeedback(profile, phase),
                Is.EqualTo(expected));
        }

        private static SettlementBeatSignal CreateScoreBeat(
            BigDouble before,
            BigDouble after,
            bool reachedTarget = false,
            float speed = 1f)
        {
            return new SettlementBeatSignal(
                SettlementBeatKind.ResultApplied,
                "测试",
                1,
                speed,
                0.5f,
                ScoreLineKind.DishFlat,
                before,
                after,
                SettlementImpactTier.Normal,
                1,
                reachedTarget);
        }

        private static ScoreLine CreateLine(ScoreLineKind kind)
        {
            return new ScoreLine(
                default,
                kind,
                default,
                10,
                "dish_test",
                null,
                1f,
                0f,
                1f,
                string.Empty);
        }

        private static ScoreLine CreateCakeLayerLine(
            ScoreSource source,
            ScoreLineKind kind,
            int dishInstanceId)
        {
            return new ScoreLine(
                ScorePhase.AfterAllDishes,
                kind,
                source,
                dishInstanceId,
                $"dish_{dishInstanceId}",
                null,
                1f,
                0f,
                1f,
                string.Empty,
                executionGroupId: 77);
        }
    }
}
#endif
