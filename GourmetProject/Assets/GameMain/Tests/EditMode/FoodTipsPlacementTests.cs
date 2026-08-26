using System;
using GourmetProject.Game.UI.Tooltips;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class FoodTipsPlacementTests
    {
        private const string FoodTipsPath =
            "Assets/GameMain/Content/Prefabs/UI/Tooltips/FoodTipsView.prefab";

        [Test]
        public void PlaceAroundRectTransform_WideExternalSkillsKeepSummaryNextToFood()
        {
            var canvasObject = new GameObject(
                "FoodTipsPlacementTests_Canvas",
                typeof(RectTransform),
                typeof(Canvas));
            var targetObject = new GameObject("Food", typeof(RectTransform));
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodTipsPath);

            try
            {
                Assert.That(prefab, Is.Not.Null);
                RectTransform canvasRect = canvasObject.transform as RectTransform;
                Assert.That(canvasRect, Is.Not.Null);
                canvasRect.sizeDelta = new Vector2(1200f, 800f);
                Assert.That(canvasObject.TryGetComponent(out Canvas canvas), Is.True);
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                Canvas.ForceUpdateCanvases();

                RectTransform target = targetObject.transform as RectTransform;
                Assert.That(target, Is.Not.Null);
                target.SetParent(canvasRect, false);
                target.anchorMin = new Vector2(0.5f, 0.5f);
                target.anchorMax = new Vector2(0.5f, 0.5f);
                target.pivot = new Vector2(0.5f, 0.5f);
                target.sizeDelta = new Vector2(160f, 160f);
                target.anchoredPosition = new Vector2(canvasRect.rect.xMax - 100f, 0f);

                GameObject instance = UnityEngine.Object.Instantiate(prefab, canvasRect, false);
                Assert.That(instance.TryGetComponent(out FoodTipsView tips), Is.True);
                tips.Bind(new FoodTipsData(
                    new FoodSummaryTipsData(
                        "跳跳糖",
                        Array.Empty<FoodInfoEntry>(),
                        Array.Empty<string>()),
                    new FoodScoreTipsData(114f, 23.29f),
                    Array.Empty<FoodInfoEntry>(),
                    new[]
                    {
                        new FoodInfoEntry("咸", "有 20% 概率额外结算 1 次"),
                    },
                    Array.Empty<FoodInfoEntry>()));
                tips.Show();
                Canvas.ForceUpdateCanvases();

                tips.PlaceAroundRectTransform(target, canvas);
                Canvas.ForceUpdateCanvases();

                Rect summaryBounds = BoundsIn(canvasRect, tips.SummaryView.transform as RectTransform);
                Rect targetBounds = BoundsIn(canvasRect, target);
                float gap = targetBounds.xMin - summaryBounds.xMax;
                Assert.That(summaryBounds.xMax, Is.LessThan(targetBounds.xMin));
                Assert.That(gap, Is.InRange(17.5f, 40f));
            }
            finally
            {
                if (targetObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(targetObject);
                }

                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        [Test]
        public void Bind_LongSpecialTagAfterShortTag_KeepsConfiguredLineBreaks()
        {
            var canvasObject = new GameObject(
                "FoodTipsPlacementTests_Canvas",
                typeof(RectTransform),
                typeof(Canvas));
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodTipsPath);

            try
            {
                Assert.That(prefab, Is.Not.Null);
                RectTransform canvasRect = canvasObject.transform as RectTransform;
                Assert.That(canvasRect, Is.Not.Null);
                canvasRect.sizeDelta = new Vector2(1200f, 800f);

                GameObject instance = UnityEngine.Object.Instantiate(prefab, canvasRect, false);
                Assert.That(instance.TryGetComponent(out FoodTipsView tips), Is.True);

                tips.Bind(DataWithSpecialTag("短", "短"));
                tips.Show();
                Canvas.ForceUpdateCanvases();
                tips.Hide();
                Assert.That(tips.gameObject.activeSelf, Is.False);
                tips.Bind(DataWithSpecialTag(
                    "甜蜜传递",
                    "使随机食物获得此技能\n并结算其获得的\n所有甜蜜传递技能"));
                tips.Show();
                Canvas.ForceUpdateCanvases();

                Transform specialTagsRoot = instance.transform.Find("4_SpecialTags");
                Assert.That(specialTagsRoot, Is.Not.Null);
                TMP_Text[] texts = specialTagsRoot.GetComponentsInChildren<TMP_Text>(true);
                TMP_Text desc = Array.Find(texts, text => text.gameObject.name == "Desc");
                Assert.That(desc, Is.Not.Null);
                desc.ForceMeshUpdate();

                Assert.That(desc.textInfo.lineCount, Is.EqualTo(3));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        private static FoodTipsData DataWithSpecialTag(string title, string desc)
        {
            return new FoodTipsData(
                FoodSummaryTipsData.Empty,
                FoodScoreTipsData.Empty,
                Array.Empty<FoodInfoEntry>(),
                Array.Empty<FoodInfoEntry>(),
                new[] { new FoodInfoEntry(title, desc) });
        }

        private static Rect BoundsIn(RectTransform parent, RectTransform child)
        {
            Assert.That(child, Is.Not.Null);
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
    }
}
