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
        public void Bind_NameStaysCenteredWithSymmetricContentSlots(bool isTemporaryCopy, int countAs)
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
                RectTransform countAsSlot = baseInfo.Find("CountAsSlot") as RectTransform;
                Assert.That(countAsSlot, Is.Not.Null);
                RectTransform countAsRect = countAsSlot.Find("CountAsView") as RectTransform;
                RectTransform name = baseInfo.Find("Name") as RectTransform;
                RectTransform duplicateSlot = baseInfo.Find("DuplicateSlot") as RectTransform;
                Assert.That(duplicateSlot, Is.Not.Null);
                RectTransform duplicate = duplicateSlot.Find("DuplicateView") as RectTransform;

                Assert.That(baseInfo.TryGetComponent(out HorizontalLayoutGroup _), Is.True);
                AssertContentSizedBadge(countAsRect, countAs > 1);
                AssertContentSized(name);
                AssertContentSizedBadge(duplicate, isTemporaryCopy);

                float expectedSlotWidth = Mathf.Max(
                    countAs > 1 ? LayoutUtility.GetPreferredWidth(countAsRect) : 0f,
                    isTemporaryCopy ? LayoutUtility.GetPreferredWidth(duplicate) : 0f);
                Assert.That(countAsSlot.rect.width, Is.EqualTo(expectedSlotWidth).Within(0.1f));
                Assert.That(duplicateSlot.rect.width, Is.EqualTo(expectedSlotWidth).Within(0.1f));

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
        public void Bind_SummaryWidthIncludesBaseInfoPreferredWidth()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodTipsPath);
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Assert.That(instance.TryGetComponent(out FoodTipsView tips), Is.True);
                FoodSummaryTipsView summary = tips.SummaryView;
                summary.Bind(new FoodSummaryTipsData(
                    "这是一个非常非常非常非常非常非常非常非常长的食物名字",
                    Array.Empty<FoodInfoEntry>(),
                    Array.Empty<string>(),
                    isTemporaryCopy: true,
                    countAs: 9));
                Canvas.ForceUpdateCanvases();

                RectTransform summaryRect = summary.transform as RectTransform;
                Assert.That(summaryRect, Is.Not.Null);
                RectTransform baseInfo = summaryRect.Find("BaseInfoView") as RectTransform;
                Assert.That(baseInfo, Is.Not.Null);

                float baseInfoWidth = LayoutUtility.GetPreferredWidth(baseInfo);
                Assert.That(summaryRect.rect.width, Is.EqualTo(baseInfoWidth + 24f).Within(0.1f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void AssertContentSized(RectTransform rect)
        {
            Assert.That(rect, Is.Not.Null);
            Assert.That(rect.TryGetComponent(out ContentSizeFitter fitter), Is.True);
            Assert.That(fitter.horizontalFit, Is.EqualTo(ContentSizeFitter.FitMode.PreferredSize));
            Assert.That(rect.rect.width, Is.EqualTo(LayoutUtility.GetPreferredWidth(rect)).Within(0.1f));
        }

        private static void AssertContentSizedBadge(RectTransform rect, bool visible)
        {
            Assert.That(rect, Is.Not.Null);
            Assert.That(rect.gameObject.activeSelf, Is.EqualTo(visible));
            Assert.That(rect.TryGetComponent(out ContentSizeFitter fitter), Is.True);
            Assert.That(fitter.horizontalFit, Is.EqualTo(ContentSizeFitter.FitMode.PreferredSize));
            Assert.That(fitter.verticalFit, Is.EqualTo(ContentSizeFitter.FitMode.PreferredSize));
            if (visible)
            {
                Assert.That(rect.rect.width, Is.EqualTo(LayoutUtility.GetPreferredWidth(rect)).Within(0.1f));
            }
        }
    }
}
