using System.Collections.Generic;
using GourmetProject.Game.Presentation.Battle;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class TemporaryAreaStackLayoutTests
    {
        private const float PaddingRatio = 0.84f;
        private const float VisibleRatio = 0.5f;
        private const float Epsilon = 0.0001f;

        [Test]
        public void SingleDish_IsCenteredInContentRect()
        {
            Rect rect = Rect.MinMaxRect(-3f, -1f, 3f, 1f);
            TemporaryAreaStackSlot[] slots = TemporaryAreaStackLayout.Calculate(
                rect,
                new[] { new Vector2(2f, 1f) },
                PaddingRatio,
                VisibleRatio);

            Assert.That(slots, Has.Length.EqualTo(1));
            Assert.That(slots[0].Center.x, Is.EqualTo(rect.center.x).Within(Epsilon));
            Assert.That(slots[0].Center.y, Is.EqualTo(rect.center.y).Within(Epsilon));
        }

        [Test]
        public void MoreDishes_CompressHorizontalSpacingWithoutChangingScale()
        {
            Rect rect = Rect.MinMaxRect(-2f, -1f, 2f, 1f);
            var twoFootprints = Repeat(new Vector2(2f, 1f), 2);
            var fourFootprints = Repeat(new Vector2(2f, 1f), 4);

            TemporaryAreaStackSlot[] two = TemporaryAreaStackLayout.Calculate(
                rect,
                twoFootprints,
                PaddingRatio,
                VisibleRatio);
            TemporaryAreaStackSlot[] four = TemporaryAreaStackLayout.Calculate(
                rect,
                fourFootprints,
                PaddingRatio,
                VisibleRatio);

            float twoStep = two[1].Center.x - two[0].Center.x;
            float fourStep = four[1].Center.x - four[0].Center.x;
            Assert.That(fourStep, Is.LessThan(twoStep));
            Assert.That(four[0].Scale, Is.EqualTo(two[0].Scale).Within(Epsilon));
            AssertStrictlyIncreasing(four);
            AssertInside(rect, four, fourFootprints);
        }

        [Test]
        public void MixedFootprints_UseOneScaleAndKeepEveryDishInside()
        {
            Rect rect = Rect.MinMaxRect(-2.4f, -1.1f, 2.4f, 1.1f);
            var footprints = new[]
            {
                new Vector2(1f, 1f),
                new Vector2(3f, 1f),
                new Vector2(1f, 2f),
                new Vector2(2f, 1f),
                new Vector2(3f, 2f),
            };

            TemporaryAreaStackSlot[] slots = TemporaryAreaStackLayout.Calculate(
                rect,
                footprints,
                PaddingRatio,
                VisibleRatio);

            Assert.That(slots, Has.Length.EqualTo(footprints.Length));
            for (int i = 1; i < slots.Length; i++)
            {
                Assert.That(slots[i].Scale, Is.EqualTo(slots[0].Scale).Within(Epsilon));
                Assert.That(slots[i].Center.y, Is.EqualTo(rect.center.y).Within(Epsilon));
            }

            AssertStrictlyIncreasing(slots);
            AssertInside(rect, slots, footprints);
        }

        private static Vector2[] Repeat(Vector2 footprint, int count)
        {
            var result = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = footprint;
            }

            return result;
        }

        private static void AssertStrictlyIncreasing(IReadOnlyList<TemporaryAreaStackSlot> slots)
        {
            for (int i = 1; i < slots.Count; i++)
            {
                Assert.That(slots[i].Center.x, Is.GreaterThan(slots[i - 1].Center.x));
            }
        }

        private static void AssertInside(
            Rect rect,
            IReadOnlyList<TemporaryAreaStackSlot> slots,
            IReadOnlyList<Vector2> footprints)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                float halfWidth = footprints[i].x * slots[i].Scale * 0.5f;
                float halfHeight = footprints[i].y * slots[i].Scale * 0.5f;
                Assert.That(slots[i].Center.x - halfWidth, Is.GreaterThanOrEqualTo(rect.xMin - Epsilon));
                Assert.That(slots[i].Center.x + halfWidth, Is.LessThanOrEqualTo(rect.xMax + Epsilon));
                Assert.That(slots[i].Center.y - halfHeight, Is.GreaterThanOrEqualTo(rect.yMin - Epsilon));
                Assert.That(slots[i].Center.y + halfHeight, Is.LessThanOrEqualTo(rect.yMax + Epsilon));
            }
        }
    }
}
