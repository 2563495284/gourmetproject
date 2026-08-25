#if UNITY_EDITOR
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
        private const float HorizontalOverlap = 0.35f;
        private const float ViewportPadding = 0.035f;

        [Test]
        public void SlotAllocator_KeepsIndependentStableSequencesPerTarget()
        {
            var allocator = new ResultLabelSlotAllocator();
            allocator.Add(10);
            allocator.Add(20);
            allocator.Add(10);
            allocator.Add(10);

            AssertSlot(allocator.Take(10), 0, 3);
            AssertSlot(allocator.Take(20), 0, 1);
            AssertSlot(allocator.Take(10), 1, 3);
            AssertSlot(allocator.Take(10), 2, 3);
        }

        [Test]
        public void BatchVisibility_SkipsNonVisualSourceLinesBeforeTheyConsumeSlots()
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
        public void FourLabels_KeepAnchorYAndSpreadEvenlyOnX()
        {
            Camera camera = CreateCamera();
            try
            {
                Vector3 anchor = camera.ViewportToWorldPoint(new Vector3(0.5f, 0.82f, 10f));
                Vector3[] positions = Resolve(camera, anchor, 4);
                Vector3[] viewport = ToViewport(camera, positions);
                float expectedPitch = Footprint.x * (1f - HorizontalOverlap);

                for (int i = 0; i < positions.Length; i++)
                {
                    Assert.That(positions[i].y, Is.EqualTo(anchor.y).Within(0.0001f));
                    Assert.That(positions[i].z, Is.EqualTo(anchor.z).Within(0.0001f));
                    if (i > 0)
                    {
                        Assert.That(
                            positions[i].x - positions[i - 1].x,
                            Is.EqualTo(expectedPitch).Within(0.0001f));
                    }
                }

                Assert.That(expectedPitch, Is.LessThan(Footprint.x));
                Assert.That(viewport[0].y, Is.EqualTo(viewport[3].y).Within(0.0001f));
                AssertFootprintsInsideSafeViewport(camera, positions);
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [TestCase(0.10f, 1f)]
        [TestCase(0.90f, -1f)]
        public void SideEdge_ShiftsWholeRowOnXWithoutChangingY(
            float anchorViewportX,
            float expectedDirection)
        {
            Camera camera = CreateCamera();
            try
            {
                Vector3 anchor = camera.ViewportToWorldPoint(
                    new Vector3(anchorViewportX, 0.82f, 10f));
                Vector3[] positions = Resolve(camera, anchor, 4);
                Vector3[] viewport = ToViewport(camera, positions);
                float centroidX = 0f;
                for (int i = 0; i < viewport.Length; i++)
                {
                    centroidX += viewport[i].x;
                    Assert.That(positions[i].y, Is.EqualTo(anchor.y).Within(0.0001f));
                }

                centroidX /= viewport.Length;

                Assert.That(
                    Mathf.Sign(centroidX - anchorViewportX),
                    Is.EqualTo(expectedDirection));

                AssertFootprintsInsideSafeViewport(camera, positions);
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test]
        public void EightLabels_StayInOneHorizontalRow()
        {
            Vector3 anchor = new(2f, 3f, 0f);
            Vector3[] positions = Resolve(null, anchor, 8);
            AssertHorizontalPitch(positions);
            for (int i = 0; i < positions.Length; i++)
            {
                Assert.That(positions[i].y, Is.EqualTo(anchor.y).Within(0.0001f));
                if (i > 0)
                {
                    Assert.That(positions[i].x, Is.GreaterThan(positions[i - 1].x));
                }
            }
        }

        [Test]
        public void TwoThroughEightLabels_UseConfiguredOverlapWithinTheSafeViewport()
        {
            Camera camera = CreateCamera();
            try
            {
                Vector3 anchor = camera.ViewportToWorldPoint(
                    new Vector3(0.5f, 0.82f, 10f));
                for (int count = 2; count <= 8; count++)
                {
                    Vector3[] positions = Resolve(camera, anchor, count);
                    for (int i = 0; i < positions.Length; i++)
                    {
                        Assert.That(positions[i].y, Is.EqualTo(anchor.y).Within(0.0001f));
                    }

                    AssertHorizontalPitch(positions);
                    AssertFootprintsInsideSafeViewport(camera, positions);
                }
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test]
        public void ViewportEdges_KeepTheWholeRowInsideTheSafeArea()
        {
            Camera camera = CreateCamera();
            try
            {
                Vector2[] anchors =
                {
                    new(0.5f, 0.08f),
                    new(0.5f, 0.92f),
                    new(0.08f, 0.5f),
                    new(0.92f, 0.5f),
                    new(0.08f, 0.08f),
                    new(0.92f, 0.92f),
                };
                for (int i = 0; i < anchors.Length; i++)
                {
                    Vector3 anchor = camera.ViewportToWorldPoint(
                        new Vector3(anchors[i].x, anchors[i].y, 10f));
                    Vector3[] positions = Resolve(camera, anchor, 8);
                    for (int positionIndex = 0;
                         positionIndex < positions.Length;
                         positionIndex++)
                    {
                        Assert.That(
                            positions[positionIndex].y,
                            Is.EqualTo(anchor.y).Within(0.0001f));
                    }

                    AssertHorizontalPitch(positions);
                    AssertFootprintsInsideSafeViewport(camera, positions);
                }
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test]
        public void MissingCamera_PreservesDeterministicScaleAwareHorizontalRow()
        {
            Vector3 anchor = new(2f, 3f, 0f);
            var first = new ResultLabelLayoutSlot(0, 2);

            Vector3 normal = SettlementResultLabelLayout.Resolve(
                null,
                anchor,
                first,
                Footprint,
                HorizontalOverlap);
            Vector3 doubled = SettlementResultLabelLayout.Resolve(
                null,
                anchor,
                first,
                Footprint * 2f,
                HorizontalOverlap);

            Assert.That(
                doubled - anchor,
                Is.EqualTo((normal - anchor) * 2f)
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

        private static Vector3[] Resolve(Camera camera, Vector3 anchor, int count)
        {
            var result = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = SettlementResultLabelLayout.Resolve(
                    camera,
                    anchor,
                    new ResultLabelLayoutSlot(i, count),
                    Footprint,
                    HorizontalOverlap,
                    ViewportPadding);
            }

            return result;
        }

        private static Vector3[] ToViewport(Camera camera, Vector3[] positions)
        {
            var result = new Vector3[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                result[i] = camera.WorldToViewportPoint(positions[i]);
            }

            return result;
        }

        private static void AssertFootprintsInsideSafeViewport(
            Camera camera,
            Vector3[] positions)
        {
            float halfWidth = Footprint.x / (camera.orthographicSize * 2f * camera.aspect) * 0.5f;
            float halfHeight = Footprint.y / (camera.orthographicSize * 2f) * 0.5f;
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 viewport = camera.WorldToViewportPoint(positions[i]);
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

        private static void AssertHorizontalPitch(Vector3[] positions)
        {
            float expectedPitch = Footprint.x * (1f - HorizontalOverlap);
            for (int i = 1; i < positions.Length; i++)
            {
                Assert.That(
                    positions[i].x - positions[i - 1].x,
                    Is.EqualTo(expectedPitch).Within(0.0001f));
            }
        }

        private static void AssertSlot(ResultLabelLayoutSlot slot, int index, int count)
        {
            Assert.That(slot.Index, Is.EqualTo(index));
            Assert.That(slot.Count, Is.EqualTo(count));
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
