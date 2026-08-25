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
        private const float RowGap = 0.08f;
        private const float ColumnGap = 0.16f;
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
        public void TopRightTarget_StacksAllLabelsDownwardWithoutOverlap()
        {
            Camera camera = CreateCamera();
            try
            {
                Vector3 anchor = camera.ViewportToWorldPoint(new Vector3(0.88f, 0.88f, 10f));
                Vector3[] positions = Resolve(camera, anchor, 4);

                Assert.That(
                    positions[0],
                    Is.EqualTo(anchor).Using(Vector3ComparerWithEqualsOperator.Instance));
                for (int i = 1; i < positions.Length; i++)
                {
                    Vector3 previous = camera.WorldToViewportPoint(positions[i - 1]);
                    Vector3 current = camera.WorldToViewportPoint(positions[i]);
                    Assert.That(current.x, Is.EqualTo(previous.x).Within(0.0001f));
                    Assert.That(current.y, Is.LessThan(previous.y - 0.04f));
                }

                AssertFootprintsInsideSafeViewport(camera, positions);
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [TestCase(0.10f, 1f)]
        [TestCase(0.90f, -1f)]
        public void BottomEdge_WrapsIntoColumnsTowardScreenCenter(
            float anchorViewportX,
            float expectedDirection)
        {
            Camera camera = CreateCamera();
            try
            {
                Vector3 anchor = camera.ViewportToWorldPoint(
                    new Vector3(anchorViewportX, 0.08f, 10f));
                Vector3[] positions = Resolve(camera, anchor, 4);
                Vector3 first = camera.WorldToViewportPoint(positions[0]);

                for (int i = 1; i < positions.Length; i++)
                {
                    Vector3 current = camera.WorldToViewportPoint(positions[i]);
                    Assert.That(
                        Mathf.Sign(current.x - first.x),
                        Is.EqualTo(expectedDirection));
                    Assert.That(current.y, Is.EqualTo(first.y).Within(0.0001f));
                }

                AssertFootprintsInsideSafeViewport(camera, positions);
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test]
        public void MissingCamera_PreservesDeterministicScaleAwareStacking()
        {
            Vector3 anchor = new(2f, 3f, 0f);
            var second = new ResultLabelLayoutSlot(1, 2);

            Vector3 normal = SettlementResultLabelLayout.Resolve(
                null,
                anchor,
                second,
                Footprint,
                RowGap,
                ColumnGap);
            Vector3 doubled = SettlementResultLabelLayout.Resolve(
                null,
                anchor,
                second,
                Footprint * 2f,
                RowGap * 2f,
                ColumnGap * 2f);

            Assert.That(normal, Is.EqualTo(anchor + Vector3.down * 0.56f)
                .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(doubled, Is.EqualTo(anchor + Vector3.down * 1.12f)
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
                    RowGap,
                    ColumnGap,
                    ViewportPadding);
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
