using System;
using GourmetProject.Game.UI.Tooltips;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class FoodSummaryTipsViewTests
    {
        private const string FoodTipsPath =
            "Assets/GameMain/Content/Prefabs/UI/Tooltips/FoodTipsView.prefab";

        [TestCase(false, 1)]
        [TestCase(true, 1)]
        [TestCase(false, 9)]
        [TestCase(true, 9)]
        public void Bind_NameStaysCenteredWithFixedBadgeSlots(bool isTemporaryCopy, int countAs)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodTipsPath);
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Assert.That(instance.TryGetComponent(out FoodTipsView tips), Is.True);
                FoodSummaryTipsView summary = tips.SummaryView;
                summary.Bind(new FoodSummaryTipsData(
                    "巧克力威化",
                    new[]
                    {
                        new FoodInfoEntry(
                            string.Empty,
                            "这是一个非常非常非常非常非常非常长的技能描述"),
                    },
                    Array.Empty<string>(),
                    isTemporaryCopy: isTemporaryCopy,
                    countAs: countAs));
                Canvas.ForceUpdateCanvases();

                RectTransform summaryRect = summary.transform as RectTransform;
                Assert.That(summaryRect, Is.Not.Null);
                RectTransform baseInfo = summaryRect.Find("BaseInfoView") as RectTransform;
                Assert.That(baseInfo, Is.Not.Null);
                RectTransform countAsRect = baseInfo.Find("CountAsView") as RectTransform;
                RectTransform name = baseInfo.Find("Name") as RectTransform;
                RectTransform duplicate = baseInfo.Find("DuplicateView") as RectTransform;

                Assert.That(baseInfo.TryGetComponent(out HorizontalLayoutGroup _), Is.True);
                AssertFixedBadge(countAsRect, countAs > 1);
                AssertContentSized(name);
                AssertFixedBadge(duplicate, isTemporaryCopy);
                Assert.That(countAsRect.rect.width, Is.EqualTo(duplicate.rect.width).Within(0.1f));

                Vector3 nameCenter = baseInfo.InverseTransformPoint(
                    name.TransformPoint(name.rect.center));
                Assert.That(nameCenter.x, Is.EqualTo(baseInfo.rect.center.x).Within(0.1f));

                float baseInfoWidth = LayoutUtility.GetPreferredWidth(baseInfo);
                Assert.That(summaryRect.rect.width, Is.GreaterThan(baseInfoWidth + 24f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void PlaceAroundRectTransform_WhenSummaryIsLeft_ExternalSkillsStayOnItsLeft()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodTipsPath);
            Assert.That(prefab, Is.Not.Null);

            var parentObject = new GameObject("FoodTipsPlacementParent", typeof(RectTransform));
            RectTransform parent = parentObject.GetComponent<RectTransform>();
            parent.sizeDelta = new Vector2(1600f, 1000f);
            GameObject instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            try
            {
                Assert.That(instance.TryGetComponent(out FoodTipsView tips), Is.True);
                tips.Bind(new FoodTipsData(
                    new FoodSummaryTipsData(
                        "蛋卷",
                        new[] { new FoodInfoEntry("本行", "每有1份食物，分数+10") },
                        Array.Empty<string>()),
                    FoodScoreTipsData.Empty,
                    Array.Empty<FoodInfoEntry>(),
                    new[] { new FoodInfoEntry("糖豆<甜蜜传递>", "右侧及自身每有1个技能，分数+6") },
                    Array.Empty<FoodInfoEntry>()));

                var targetObject = new GameObject("Target", typeof(RectTransform));
                RectTransform target = targetObject.GetComponent<RectTransform>();
                target.SetParent(parent, false);
                target.anchorMin = new Vector2(0.5f, 0.5f);
                target.anchorMax = new Vector2(0.5f, 0.5f);
                target.pivot = new Vector2(0.5f, 0.5f);
                target.sizeDelta = new Vector2(100f, 100f);
                target.anchoredPosition = new Vector2(700f, 0f);

                Canvas.ForceUpdateCanvases();
                tips.PlaceAroundRectTransform(target, null);

                RectTransform summary = tips.SummaryView.transform as RectTransform;
                RectTransform externalSkills = tips.transform.Find("4_TransferredSubSkills") as RectTransform;
                Assert.That(summary, Is.Not.Null);
                Assert.That(externalSkills, Is.Not.Null);
                Rect targetBounds = RectInParent(target, parent);
                Rect summaryBounds = RectInParent(summary, parent);
                Rect externalBounds = RectInParent(externalSkills, parent);
                Assert.That(summaryBounds.center.x, Is.LessThan(targetBounds.center.x));
                Assert.That(externalBounds.xMax, Is.LessThan(summaryBounds.xMin));
                Assert.That(summaryBounds.xMin - externalBounds.xMax, Is.EqualTo(10f).Within(0.1f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parentObject);
            }
        }

        private static void AssertContentSized(RectTransform rect)
        {
            Assert.That(rect, Is.Not.Null);
            Assert.That(rect.TryGetComponent(out ContentSizeFitter fitter), Is.True);
            Assert.That(fitter.horizontalFit, Is.EqualTo(ContentSizeFitter.FitMode.PreferredSize));
            Assert.That(rect.rect.width, Is.EqualTo(LayoutUtility.GetPreferredWidth(rect)).Within(0.1f));
        }

        private static void AssertFixedBadge(RectTransform rect, bool visible)
        {
            Assert.That(rect, Is.Not.Null);
            Assert.That(rect.gameObject.activeSelf, Is.True);
            Assert.That(rect.rect.width, Is.EqualTo(80f).Within(0.1f));
            Assert.That(rect.rect.height, Is.EqualTo(38f).Within(0.1f));
            Assert.That(rect.TryGetComponent(out ContentSizeFitter fitter), Is.True);
            Assert.That(fitter.horizontalFit, Is.EqualTo(ContentSizeFitter.FitMode.Unconstrained));
            Assert.That(fitter.verticalFit, Is.EqualTo(ContentSizeFitter.FitMode.Unconstrained));
            Assert.That(rect.TryGetComponent(out CanvasGroup canvasGroup), Is.True);
            Assert.That(canvasGroup.alpha, Is.EqualTo(visible ? 1f : 0f));
        }

        private static Rect RectInParent(RectTransform rect, RectTransform parent)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 min = parent.InverseTransformPoint(corners[0]);
            Vector2 max = min;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 point = parent.InverseTransformPoint(corners[i]);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
