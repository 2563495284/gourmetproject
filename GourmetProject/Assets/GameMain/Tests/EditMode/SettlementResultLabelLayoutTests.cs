#if UNITY_EDITOR
using BreakInfinity;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Utils;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SettlementResultLabelLayoutTests
    {
        private static readonly Vector2 Footprint = new(1.8f, 0.48f);
        private const float ViewportPadding = 0.035f;

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
            AssertColor(1, new Color32(0x73, 0xCF, 0xFF, 0xFF));
            AssertColor(50, new Color32(0x58, 0xE1, 0xDE, 0xFF));
            AssertColor(200, new Color32(0xFF, 0xD4, 0x5B, 0xFF));
            AssertColor(1000, new Color32(0xFF, 0x87, 0x2F, 0xFF));
            AssertColor(5000, new Color32(0xFF, 0xF3, 0xD0, 0xFF));

            AssertColor(-1, new Color32(0xF2, 0xA1, 0xAE, 0xFF));
            AssertColor(-50, new Color32(0xFF, 0x74, 0x85, 0xFF));
            AssertColor(-200, new Color32(0xFF, 0x48, 0x5E, 0xFF));
            AssertColor(-1000, new Color32(0xF1, 0x26, 0x46, 0xFF));
            AssertColor(-5000, new Color32(0xFF, 0x12, 0x3D, 0xFF));
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
            Assert.That(huge.PeakScale, Is.EqualTo(1.30f).Within(0.0001f));
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
        public void ScoreDeltaFeedback_UsesNetDeltaAndSeparatesPositiveFromNegativeMotion()
        {
            SettlementScoreFeedbackProfile positive =
                SettlementScoreFeedbackResolver.Resolve(500d);
            SettlementScoreFeedbackProfile negative =
                SettlementScoreFeedbackResolver.Resolve(-500d);
            SettlementScoreFeedbackProfile cancelled =
                SettlementScoreFeedbackResolver.Resolve(500d - 500d);

            Assert.That(positive.Intensity, Is.EqualTo(negative.Intensity).Within(0.0001f));
            Assert.That(positive.TravelDistance, Is.GreaterThan(0f));
            Assert.That(positive.ShakeStrength, Is.Zero);
            Assert.That(negative.TravelDistance, Is.LessThan(0f));
            Assert.That(negative.ShakeStrength, Is.GreaterThan(0f));
            Assert.That(negative.FireStrength, Is.Zero);
            Assert.That(cancelled.Visible, Is.False);
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
    }
}
#endif
