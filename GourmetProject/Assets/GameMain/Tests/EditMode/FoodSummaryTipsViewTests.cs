using System;
using GourmetProject.Game.UI.Tooltips;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class FoodSummaryTipsViewTests
    {
        private const float MinimumVisibleTextGap = 18f;
        private const string FoodTipsPath =
            "Assets/GameMain/Content/Prefabs/UI/Tooltips/FoodTipsView.prefab";

        [TestCase(false, 1)]
        [TestCase(true, 1)]
        [TestCase(false, 2)]
        [TestCase(true, 9)]
        public void Bind_BaseInfoChildrenDoNotOverlap(bool isTemporaryCopy, int countAs)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodTipsPath);
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Assert.That(instance.TryGetComponent(out FoodTipsView tips), Is.True);
                FoodSummaryTipsView summary = tips.SummaryView;

                // Bind a wide value first to catch stale layout data when this row
                // is subsequently reused for a much shorter food name.
                summary.Bind(new FoodSummaryTipsData(
                    "这是一个非常非常非常非常长的食物名字",
                    Array.Empty<FoodInfoEntry>(),
                    Array.Empty<string>(),
                    isTemporaryCopy: true,
                    countAs: 99));
                summary.Bind(new FoodSummaryTipsData(
                    "麻薯",
                    Array.Empty<FoodInfoEntry>(),
                    Array.Empty<string>(),
                    isTemporaryCopy: isTemporaryCopy,
                    countAs: countAs));
                Canvas.ForceUpdateCanvases();

                RectTransform summaryRect = summary.transform as RectTransform;
                Assert.That(summaryRect, Is.Not.Null);
                RectTransform baseInfo = summaryRect.Find("BaseInfoView") as RectTransform;
                Assert.That(baseInfo, Is.Not.Null);
                RectTransform countAsView =
                    baseInfo.Find("CountAsSlot/CountAsView") as RectTransform;
                RectTransform name = baseInfo.Find("Name") as RectTransform;
                RectTransform duplicateView =
                    baseInfo.Find("DuplicateSlot/DuplicateView") as RectTransform;

                Assert.That(countAsView, Is.Not.Null);
                Assert.That(name, Is.Not.Null);
                Assert.That(duplicateView, Is.Not.Null);
                AssertNameFitterDoesNotDriveLayout(name);

                Rect nameBounds = BoundsIn(baseInfo, name);
                if (countAsView.gameObject.activeSelf)
                {
                    Assert.That(BoundsIn(baseInfo, countAsView).Overlaps(nameBounds), Is.False);
                }

                if (duplicateView.gameObject.activeSelf)
                {
                    Assert.That(BoundsIn(baseInfo, duplicateView).Overlaps(nameBounds), Is.False);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void Bind_LegacyNameFitterIsDisabledBeforeLayout()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodTipsPath);
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Assert.That(instance.TryGetComponent(out FoodTipsView tips), Is.True);
                RectTransform name = tips.SummaryView.transform
                    .Find("BaseInfoView/Name") as RectTransform;
                Assert.That(name, Is.Not.Null);
                ContentSizeFitter fitter = name.gameObject.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

                tips.SummaryView.Bind(new FoodSummaryTipsData(
                    "蛋黄酥",
                    Array.Empty<FoodInfoEntry>(),
                    Array.Empty<string>(),
                    countAs: 3));

                AssertNameFitterDoesNotDriveLayout(name);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void Bind_CountBadgeAndNameKeepVisibleGlyphGap()
        {
            var canvasObject = new GameObject(
                "FoodSummaryTipsViewTests_Canvas",
                typeof(RectTransform),
                typeof(Canvas));
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodTipsPath);
            Assert.That(prefab, Is.Not.Null);
            try
            {
                RectTransform canvasRect = canvasObject.transform as RectTransform;
                Assert.That(canvasRect, Is.Not.Null);
                canvasRect.sizeDelta = new Vector2(1200f, 800f);
                canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

                GameObject instance = UnityEngine.Object.Instantiate(prefab, canvasRect, false);
                Assert.That(instance.TryGetComponent(out FoodTipsView tips), Is.True);
                tips.SummaryView.Bind(new FoodSummaryTipsData(
                    "蛋黄酥",
                    Array.Empty<FoodInfoEntry>(),
                    Array.Empty<string>(),
                    countAs: 3));
                tips.Show();
                Canvas.ForceUpdateCanvases();

                RectTransform baseInfo = tips.SummaryView.transform
                    .Find("BaseInfoView") as RectTransform;
                TMP_Text countText = baseInfo
                    .Find("CountAsSlot/CountAsView/Count")
                    .GetComponent<TMP_Text>();
                TMP_Text nameText = baseInfo.Find("Name").GetComponent<TMP_Text>();
                countText.ForceMeshUpdate(ignoreActiveState: false, forceTextReparsing: true);
                nameText.ForceMeshUpdate(ignoreActiveState: false, forceTextReparsing: true);

                Rect countGlyphs = VisibleTextBoundsIn(baseInfo, countText);
                Rect nameGlyphs = VisibleTextBoundsIn(baseInfo, nameText);
                Assert.That(
                    nameGlyphs.xMin - countGlyphs.xMax,
                    Is.GreaterThanOrEqualTo(MinimumVisibleTextGap));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        [Test]
        public void Show_RebuildsBaseInfoAfterInactiveTipSwitches()
        {
            var canvasObject = new GameObject(
                "FoodSummaryTipsViewTests_SwitchCanvas",
                typeof(RectTransform),
                typeof(Canvas));
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodTipsPath);
            Assert.That(prefab, Is.Not.Null);
            try
            {
                RectTransform canvasRect = canvasObject.transform as RectTransform;
                Assert.That(canvasRect, Is.Not.Null);
                canvasRect.sizeDelta = new Vector2(1200f, 800f);
                canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

                GameObject instance = UnityEngine.Object.Instantiate(prefab, canvasRect, false);
                Assert.That(instance.TryGetComponent(out FoodTipsView tips), Is.True);
                var cases = new[]
                {
                    new FoodSummaryTipsData(
                        "这是一个非常非常长的食物名字",
                        Array.Empty<FoodInfoEntry>(),
                        Array.Empty<string>(),
                        countAs: 99),
                    new FoodSummaryTipsData(
                        "杨枝甘露",
                        Array.Empty<FoodInfoEntry>(),
                        Array.Empty<string>(),
                        countAs: 5),
                    new FoodSummaryTipsData(
                        "麻薯",
                        Array.Empty<FoodInfoEntry>(),
                        Array.Empty<string>(),
                        countAs: 2),
                };

                foreach (FoodSummaryTipsData data in cases)
                {
                    tips.Hide();
                    tips.Bind(new FoodTipsData(data, null, null, null, null));

                    // Simulate the stale values that can survive when Bind runs while
                    // the tooltip hierarchy is inactive during rapid hover switching.
                    RectTransform baseInfo = tips.SummaryView.transform
                        .Find("BaseInfoView") as RectTransform;
                    baseInfo.GetComponent<HorizontalLayoutGroup>().spacing = 0f;
                    baseInfo.Find("CountAsSlot")
                        .GetComponent<LayoutElement>().preferredWidth = 0f;
                    baseInfo.Find("DuplicateSlot")
                        .GetComponent<LayoutElement>().preferredWidth = 0f;

                    tips.Show();
                    Canvas.ForceUpdateCanvases();

                    TMP_Text countText = baseInfo
                        .Find("CountAsSlot/CountAsView/Count")
                        .GetComponent<TMP_Text>();
                    TMP_Text nameText = baseInfo.Find("Name").GetComponent<TMP_Text>();
                    countText.ForceMeshUpdate(ignoreActiveState: false, forceTextReparsing: true);
                    nameText.ForceMeshUpdate(ignoreActiveState: false, forceTextReparsing: true);

                    Rect countGlyphs = VisibleTextBoundsIn(baseInfo, countText);
                    Rect nameGlyphs = VisibleTextBoundsIn(baseInfo, nameText);
                    Assert.That(
                        nameGlyphs.xMin - countGlyphs.xMax,
                        Is.GreaterThanOrEqualTo(MinimumVisibleTextGap),
                        $"Visible text overlapped after switching to '{nameText.text}'.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        private static void AssertNameFitterDoesNotDriveLayout(RectTransform name)
        {
            ContentSizeFitter fitter = name.GetComponent<ContentSizeFitter>();
            if (fitter == null)
            {
                return;
            }

            Assert.That(fitter.horizontalFit, Is.EqualTo(ContentSizeFitter.FitMode.Unconstrained));
            Assert.That(fitter.verticalFit, Is.EqualTo(ContentSizeFitter.FitMode.Unconstrained));
        }

        private static Rect BoundsIn(RectTransform parent, RectTransform child)
        {
            var corners = new Vector3[4];
            child.GetWorldCorners(corners);
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

        private static Rect VisibleTextBoundsIn(RectTransform parent, TMP_Text text)
        {
            bool found = false;
            Vector2 min = default;
            Vector2 max = default;
            for (int i = 0; i < text.textInfo.characterCount; i++)
            {
                TMP_CharacterInfo character = text.textInfo.characterInfo[i];
                if (!character.isVisible)
                {
                    continue;
                }

                Vector2 bottomLeft = parent.InverseTransformPoint(
                    text.rectTransform.TransformPoint(character.bottomLeft));
                Vector2 topRight = parent.InverseTransformPoint(
                    text.rectTransform.TransformPoint(character.topRight));
                if (!found)
                {
                    min = Vector2.Min(bottomLeft, topRight);
                    max = Vector2.Max(bottomLeft, topRight);
                    found = true;
                }
                else
                {
                    min = Vector2.Min(min, Vector2.Min(bottomLeft, topRight));
                    max = Vector2.Max(max, Vector2.Max(bottomLeft, topRight));
                }
            }

            Assert.That(found, Is.True, $"'{text.text}' did not generate any visible glyphs.");
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
