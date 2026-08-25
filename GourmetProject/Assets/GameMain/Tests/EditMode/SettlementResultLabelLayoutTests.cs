#if UNITY_EDITOR
using System.Collections.Generic;
using GourmetProject.Game.Presentation.Battle;
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
                    Request(
                        10,
                        firstTarget,
                        ViewportWorld(camera, 0.80f, 0.20f)),
                    Request(
                        20,
                        secondTarget,
                        ViewportWorld(camera, 0.20f, 0.80f)));

                Assert.That(
                    plan[0].Position,
                    Is.EqualTo(firstTarget)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(
                    plan[1].Position,
                    Is.EqualTo(secondTarget)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(plan[0].VerticalDirection, Is.Zero);
                Assert.That(plan[1].VerticalDirection, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test]
        public void OffsetStartsAtOriginalAnchor_AndDirectionStaysAwayFromSource()
        {
            Camera camera = CreateCamera();
            try
            {
                Vector3 anchor = ViewportWorld(camera, 0.5f, 0.30f);
                Vector3 source = ViewportWorld(camera, 0.5f, 0.40f);
                Vector3 foodCenter = ViewportWorld(camera, 0.5f, 0.50f);
                ResultLabelLayoutPlan plan = Resolve(
                    camera,
                    Request(10, anchor, foodCenter, source),
                    Request(10, anchor, foodCenter, source));

                // 来源位于原锚点和目标中心之间：位置从锚点起算，方向仍远离来源。
                Assert.That(source.y, Is.GreaterThan(anchor.y));
                Assert.That(source.y, Is.LessThan(foodCenter.y));
                Assert.That(
                    plan[0].Position,
                    Is.EqualTo(anchor)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(plan[0].VerticalDirection, Is.Zero);
                Assert.That(plan[1].Position.y, Is.GreaterThan(anchor.y));
                Assert.That(plan[1].VerticalDirection, Is.EqualTo(1f));
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [TestCase(0.70f, 0.70f, -1f, -1f)]
        [TestCase(0.30f, 0.70f, 1f, -1f)]
        [TestCase(0.70f, 0.30f, -1f, 1f)]
        [TestCase(0.30f, 0.30f, 1f, 1f)]
        public void SourceQuadrants_OffsetAwayWithVerticalScreenPriority(
            float sourceViewportX,
            float sourceViewportY,
            float expectedHorizontalDirection,
            float expectedVerticalDirection)
        {
            Camera camera = CreateCamera();
            try
            {
                Vector3 target = ViewportWorld(camera, 0.5f, 0.5f);
                Vector3 source = ViewportWorld(
                    camera,
                    sourceViewportX,
                    sourceViewportY);
                ResultLabelLayoutPlan plan = Resolve(
                    camera,
                    Request(10, target, source),
                    Request(10, target, source));
                Vector3 offsetViewport = camera.WorldToViewportPoint(plan[1].Position)
                    - camera.WorldToViewportPoint(target);

                Assert.That(
                    Mathf.Sign(offsetViewport.x),
                    Is.EqualTo(expectedHorizontalDirection));
                Assert.That(
                    Mathf.Sign(offsetViewport.y),
                    Is.EqualTo(expectedVerticalDirection));
                Assert.That(
                    Mathf.Abs(offsetViewport.y),
                    Is.GreaterThan(Mathf.Abs(offsetViewport.x)));
                Assert.That(
                    plan[1].VerticalDirection,
                    Is.EqualTo(expectedVerticalDirection));
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [TestCase(0.70f, -1f)]
        [TestCase(0.30f, 1f)]
        public void VerticallyAlignedSource_OffsetsAwayOnY(
            float sourceViewportY,
            float expectedVerticalDirection)
        {
            Camera camera = CreateCamera();
            try
            {
                Vector3 target = ViewportWorld(camera, 0.5f, 0.5f);
                Vector3 source = ViewportWorld(camera, 0.5f, sourceViewportY);
                ResultLabelLayoutPlan plan = Resolve(
                    camera,
                    Request(10, target, source),
                    Request(10, target, source));

                Assert.That(
                    Mathf.Sign(plan[1].Position.y - target.y),
                    Is.EqualTo(expectedVerticalDirection));
                Assert.That(
                    plan[1].VerticalDirection,
                    Is.EqualTo(expectedVerticalDirection));
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [TestCase(0.50f, 0.80f, 0.20f, -1f)]
        [TestCase(0.50f, 0.20f, 0.20f, 1f)]
        [TestCase(0.50f, 0.50f, 0.20f, 1f)]
        [TestCase(0.50f, 0.50f, 0.80f, -1f)]
        public void NearlyHorizontalSource_UsesScreenSpaceFallback(
            float targetViewportX,
            float targetViewportY,
            float sourceViewportX,
            float expectedVerticalDirection)
        {
            Camera camera = CreateCamera();
            try
            {
                Vector3 target = ViewportWorld(
                    camera,
                    targetViewportX,
                    targetViewportY);
                Vector3 source = ViewportWorld(
                    camera,
                    sourceViewportX,
                    targetViewportY);
                ResultLabelLayoutPlan plan = Resolve(
                    camera,
                    Request(10, target, source),
                    Request(10, target, source));

                Assert.That(
                    plan[1].VerticalDirection,
                    Is.EqualTo(expectedVerticalDirection));
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test]
        public void FourLabelsOnSameSide_ContinueOutwardWithTwentyPercentOverlap()
        {
            Camera camera = CreateCamera();
            try
            {
                Vector3 target = ViewportWorld(camera, 0.5f, 0.5f);
                Vector3 source = ViewportWorld(camera, 0.35f, 0.72f);
                var requests = new ResultLabelLayoutRequest[4];
                for (int i = 0; i < requests.Length; i++)
                {
                    requests[i] = Request(10, target, source);
                }

                ResultLabelLayoutPlan plan = Resolve(camera, requests);
                float expectedVerticalPitch = Footprint.y
                    * SettlementResultLabelLayout.VerticalPitchRatio;
                Assert.That(
                    plan[0].Position,
                    Is.EqualTo(target)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(plan[0].VerticalDirection, Is.Zero);
                for (int i = 1; i < plan.Count; i++)
                {
                    Assert.That(plan[i].VerticalDirection, Is.EqualTo(-1f));
                    if (i == 1)
                    {
                        continue;
                    }

                    Assert.That(
                        plan[i - 1].Position.y - plan[i].Position.y,
                        Is.EqualTo(expectedVerticalPitch).Within(0.0001f));
                    Assert.That(
                        plan[i].Position.x,
                        Is.GreaterThanOrEqualTo(plan[i - 1].Position.x));
                }

                float verticalOverlap = Footprint.y - expectedVerticalPitch;
                Assert.That(
                    verticalOverlap,
                    Is.EqualTo(Footprint.y * 0.20f).Within(0.0001f));
                Assert.That(
                    plan[3].Position.x - target.x,
                    Is.EqualTo(
                        Footprint.x
                        * (SettlementResultLabelLayout.FirstHorizontalOffsetRatio
                            + SettlementResultLabelLayout.HorizontalStaggerRatio * 2f))
                    .Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test]
        public void OppositeSidesOfOneTarget_KeepIndependentLaneIndices()
        {
            Camera camera = CreateCamera();
            try
            {
                Vector3 target = ViewportWorld(camera, 0.5f, 0.5f);
                Vector3 sourceAbove = ViewportWorld(camera, 0.35f, 0.72f);
                Vector3 sourceBelow = ViewportWorld(camera, 0.35f, 0.28f);
                ResultLabelLayoutPlan plan = Resolve(
                    camera,
                    Request(10, target, sourceAbove),
                    Request(10, target, sourceBelow),
                    Request(10, target, sourceAbove),
                    Request(10, target, sourceBelow));
                float firstVerticalOffset = Footprint.y
                    * SettlementResultLabelLayout.FirstVerticalOffsetRatio;
                float secondVerticalOffset = firstVerticalOffset
                    + Footprint.y * SettlementResultLabelLayout.VerticalPitchRatio;

                Assert.That(
                    plan[0].Position,
                    Is.EqualTo(target)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(
                    plan[1].Position.y - target.y,
                    Is.EqualTo(firstVerticalOffset).Within(0.0001f));
                Assert.That(
                    target.y - plan[2].Position.y,
                    Is.EqualTo(firstVerticalOffset).Within(0.0001f));
                Assert.That(
                    plan[3].Position.y - target.y,
                    Is.EqualTo(secondVerticalOffset).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test]
        public void DifferentTargets_KeepTheirOwnFirstLabelAtTheOriginalAnchor()
        {
            Camera camera = CreateCamera();
            try
            {
                Vector3 targetA = ViewportWorld(camera, 0.35f, 0.5f);
                Vector3 targetB = ViewportWorld(camera, 0.65f, 0.5f);
                Vector3 sourceA = ViewportWorld(camera, 0.25f, 0.72f);
                Vector3 sourceB = ViewportWorld(camera, 0.55f, 0.72f);
                ResultLabelLayoutPlan plan = Resolve(
                    camera,
                    Request(10, targetA, sourceA),
                    Request(20, targetB, sourceB),
                    Request(10, targetA, sourceA),
                    Request(20, targetB, sourceB));
                float firstVerticalOffset = Footprint.y
                    * SettlementResultLabelLayout.FirstVerticalOffsetRatio;

                Assert.That(
                    plan[0].Position,
                    Is.EqualTo(targetA)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(
                    plan[1].Position,
                    Is.EqualTo(targetB)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(
                    targetA.y - plan[2].Position.y,
                    Is.EqualTo(firstVerticalOffset).Within(0.0001f));
                Assert.That(
                    targetB.y - plan[3].Position.y,
                    Is.EqualTo(firstVerticalOffset).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test]
        public void MissingAndCoincidentSources_UseDeterministicDetailOrderFallback()
        {
            Camera camera = CreateCamera();
            try
            {
                Vector3 target = ViewportWorld(camera, 0.5f, 0.5f);
                ResultLabelLayoutRequest[] requests =
                {
                    RequestWithoutSource(10, target),
                    Request(10, target, target),
                    RequestWithoutSource(10, target),
                    Request(10, target, target),
                };
                ResultLabelLayoutPlan first = Resolve(camera, requests);
                ResultLabelLayoutPlan second = Resolve(camera, requests);

                for (int i = 0; i < requests.Length; i++)
                {
                    Assert.That(
                        second[i].Position,
                        Is.EqualTo(first[i].Position)
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
                    Assert.That(
                        second[i].VerticalDirection,
                        Is.EqualTo(first[i].VerticalDirection));
                }

                Assert.That(first[0].VerticalDirection, Is.Zero);
                Assert.That(first[1].VerticalDirection, Is.EqualTo(-1f));
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test]
        public void ViewportEdges_NeverPullOffsetsBackTowardOriginalAnchor()
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
                for (int targetIndex = 0;
                     targetIndex < targetViewports.Length;
                     targetIndex++)
                {
                    Vector2 viewport = targetViewports[targetIndex];
                    Vector3 target = ViewportWorld(camera, viewport.x, viewport.y);
                    Vector3 source = ViewportWorld(
                        camera,
                        1f - viewport.x,
                        1f - viewport.y);
                    var requests = new ResultLabelLayoutRequest[4];
                    for (int i = 0; i < requests.Length; i++)
                    {
                        requests[i] = Request(targetIndex + 1, target, source);
                    }

                    ResultLabelLayoutPlan raw = Resolve(null, requests);
                    ResultLabelLayoutPlan fitted = Resolve(camera, requests);
                    Assert.That(
                        fitted[0].Position,
                        Is.EqualTo(target)
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
                    for (int i = 2; i < fitted.Count; i++)
                    {
                        Assert.That(
                            fitted[i].Position - fitted[1].Position,
                            Is.EqualTo(raw[i].Position - raw[1].Position)
                            .Using(Vector3ComparerWithEqualsOperator.Instance));
                    }

                    Vector3 targetViewport = camera.WorldToViewportPoint(target);
                    for (int i = 1; i < fitted.Count; i++)
                    {
                        Vector3 rawOffset = camera.WorldToViewportPoint(raw[i].Position)
                            - targetViewport;
                        Vector3 fittedOffset = camera.WorldToViewportPoint(fitted[i].Position)
                            - targetViewport;
                        Assert.That(
                            Mathf.Sign(fittedOffset.x),
                            Is.EqualTo(Mathf.Sign(rawOffset.x)));
                        Assert.That(
                            Mathf.Sign(fittedOffset.y),
                            Is.EqualTo(Mathf.Sign(rawOffset.y)));
                        Assert.That(
                            Mathf.Abs(fittedOffset.x),
                            Is.GreaterThanOrEqualTo(Mathf.Abs(rawOffset.x) - 0.0001f));
                        Assert.That(
                            Mathf.Abs(fittedOffset.y),
                            Is.GreaterThanOrEqualTo(Mathf.Abs(rawOffset.y) - 0.0001f));
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test]
        public void ViewportFit_StillAppliesWhenItMovesFurtherAwayFromOriginalAnchor()
        {
            Camera camera = CreateCamera();
            try
            {
                Vector3 target = ViewportWorld(camera, 0.06f, 0.50f);
                Vector3 source = ViewportWorld(camera, 0.01f, 0.50f);
                ResultLabelLayoutRequest[] requests =
                {
                    Request(10, target, source),
                    Request(10, target, source),
                };

                ResultLabelLayoutPlan raw = Resolve(null, requests);
                ResultLabelLayoutPlan fitted = Resolve(camera, requests);

                Assert.That(fitted[1].Position.x, Is.GreaterThan(raw[1].Position.x));
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
            Vector3 source = new(1f, 4f, 0f);
            ResultLabelLayoutRequest request = Request(10, target, source);
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

        private static ResultLabelLayoutRequest Request(
            int targetKey,
            Vector3 anchor,
            Vector3 source)
        {
            return Request(targetKey, anchor, anchor, source);
        }

        private static ResultLabelLayoutRequest Request(
            int targetKey,
            Vector3 anchor,
            Vector3 targetPosition,
            Vector3 source)
        {
            return new ResultLabelLayoutRequest(
                targetKey,
                anchor,
                targetPosition,
                source,
                true);
        }

        private static ResultLabelLayoutRequest RequestWithoutSource(
            int targetKey,
            Vector3 anchor)
        {
            return new ResultLabelLayoutRequest(
                targetKey,
                anchor,
                anchor,
                default,
                false);
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
