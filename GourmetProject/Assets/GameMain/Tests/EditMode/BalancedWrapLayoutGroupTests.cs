using GourmetProject.Game.UI.Meta;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BalancedWrapLayoutGroupTests
    {
        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(3, 3)]
        [TestCase(4, 2)]
        [TestCase(5, 3)]
        [TestCase(6, 3)]
        [TestCase(7, 3)]
        public void ResolveColumnCount_BalancesWithinMaxThree(int itemCount, int expectedColumns)
        {
            Assert.That(
                BalancedWrapLayoutGroup.ResolveColumnCount(itemCount),
                Is.EqualTo(expectedColumns));
        }

        [Test]
        public void Layout_ThreeItems_StayOnOneRow()
        {
            Vector2[] positions = LayoutPositions(3);
            Assert.That(positions[0].y, Is.EqualTo(positions[1].y).Within(0.01f));
            Assert.That(positions[1].y, Is.EqualTo(positions[2].y).Within(0.01f));
            Assert.That(positions[0].x, Is.LessThan(positions[1].x));
            Assert.That(positions[1].x, Is.LessThan(positions[2].x));
        }

        [Test]
        public void Layout_FourItems_FormsTwoByTwoGrid()
        {
            Vector2[] positions = LayoutPositions(4);
            Assert.That(positions[0].y, Is.EqualTo(positions[1].y).Within(0.01f));
            Assert.That(positions[2].y, Is.EqualTo(positions[3].y).Within(0.01f));
            Assert.That(positions[0].y, Is.GreaterThan(positions[2].y));
            Assert.That(positions[0].x, Is.EqualTo(positions[2].x).Within(0.01f));
            Assert.That(positions[1].x, Is.EqualTo(positions[3].x).Within(0.01f));
        }

        [Test]
        public void Layout_FiveItems_UsesThreeByTwoGrid()
        {
            Vector2[] positions = LayoutPositions(5);
            Assert.That(positions[0].y, Is.EqualTo(positions[2].y).Within(0.01f));
            Assert.That(positions[3].y, Is.EqualTo(positions[4].y).Within(0.01f));
            Assert.That(positions[0].y, Is.GreaterThan(positions[3].y));
            Assert.That(positions[0].x, Is.EqualTo(positions[3].x).Within(0.01f));
            Assert.That(positions[1].x, Is.EqualTo(positions[4].x).Within(0.01f));
            Assert.That(positions[2].x, Is.GreaterThan(positions[4].x));
        }

        [Test]
        public void Layout_DoesNotResizeNestedIconFrame()
        {
            var root = new GameObject(
                "BalancedWrapRoot",
                typeof(RectTransform),
                typeof(BalancedWrapLayoutGroup));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(800f, 400f);
            BalancedWrapLayoutGroup layout = root.GetComponent<BalancedWrapLayoutGroup>();
            layout.maxColumns = 3;
            layout.spacing = 10f;

            var row = new GameObject("Row", typeof(RectTransform));
            RectTransform rowRect = row.GetComponent<RectTransform>();
            rowRect.SetParent(rect, false);
            rowRect.sizeDelta = new Vector2(400f, 400f);

            var iconFrame = new GameObject("IconFrame", typeof(RectTransform));
            RectTransform iconRect = iconFrame.GetComponent<RectTransform>();
            iconRect.SetParent(rowRect, false);
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = new Vector2(48f, 0f);
            iconRect.sizeDelta = new Vector2(64f, 72f);

            try
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                Assert.That(iconRect.sizeDelta.x, Is.EqualTo(64f).Within(0.01f));
                Assert.That(iconRect.sizeDelta.y, Is.EqualTo(72f).Within(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Vector2[] LayoutPositions(int count)
        {
            var root = new GameObject(
                "BalancedWrapRoot",
                typeof(RectTransform),
                typeof(BalancedWrapLayoutGroup));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1200f, 800f);

            BalancedWrapLayoutGroup layout = root.GetComponent<BalancedWrapLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.maxColumns = 3;
            layout.spacing = 24f;

            var children = new RectTransform[count];
            for (int i = 0; i < count; i++)
            {
                var child = new GameObject($"Child_{i}", typeof(RectTransform));
                children[i] = child.GetComponent<RectTransform>();
                children[i].SetParent(rect, false);
                children[i].sizeDelta = new Vector2(400f, 400f);
            }

            try
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                var positions = new Vector2[count];
                for (int i = 0; i < count; i++)
                {
                    positions[i] = children[i].anchoredPosition;
                }

                return positions;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
