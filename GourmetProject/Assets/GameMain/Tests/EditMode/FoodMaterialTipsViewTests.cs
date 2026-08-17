using System.Collections.Generic;
using GourmetProject.Game.UI.Tooltips;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class FoodMaterialTipsViewTests
    {
        private const string MaterialTipsPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Tooltips/FoodMaterialTipsView.prefab";
        private const string FoodTipsPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Tooltips/FoodTipsView.prefab";
        private const string OtherCardPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Tooltips/FoodOtherTipCardView.prefab";
        private const string ScrollbarThumbPath =
            "Assets/GameMain/Content/Resources/Sprites/UI/FengKuangCanTing/Controls/scrollbar_thumb.png";

        [Test]
        public void MaterialPrefabs_UseFoodOtherTipCardWithoutOuterBackground()
        {
            FoodTipCardView expectedCard = AssetDatabase
                .LoadAssetAtPath<GameObject>(OtherCardPrefabPath)
                .GetComponent<FoodTipCardView>();
            Sprite expectedThumb = AssetDatabase.LoadAssetAtPath<Sprite>(ScrollbarThumbPath);

            AssertPrefabConfiguration(MaterialTipsPrefabPath, expectedCard, expectedThumb);
            AssertPrefabConfiguration(FoodTipsPrefabPath, expectedCard, expectedThumb);
        }

        [Test]
        public void Bind_UsesMaterialNameAndDescriptionOnFoodOtherCard()
        {
            FoodMaterialTipsView view = CreateView(out GameObject instance);
            try
            {
                view.Bind(new[]
                {
                    Entry("m_cherry", "樱桃木", "分数+80"),
                });

                RectTransform content = Content(view);
                Assert.That(content.childCount, Is.EqualTo(1));
                FoodTipCardView card = content.GetChild(0).GetComponent<FoodTipCardView>();
                Assert.That(card, Is.Not.Null);
                Assert.That(card.transform.Find("Title").GetComponent<TMP_Text>().text, Is.EqualTo("樱桃木"));
                Assert.That(card.transform.Find("Panel/Desc").GetComponent<TMP_Text>().text, Is.EqualTo("分数+80"));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void FoodTipsPrefab_MaterialBindingCreatesFoodOtherCardWithoutErrors()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodTipsPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = Object.Instantiate(prefab);

            try
            {
                FoodMaterialTipsView view = instance.GetComponentInChildren<FoodMaterialTipsView>(true);
                Assert.That(view, Is.Not.Null);

                view.Bind(new[]
                {
                    Entry("m_silver", "银", "每占1格银材质\n独立1/5概率获得消耗品"),
                });

                Assert.That(Content(view).childCount, Is.EqualTo(1));
                Assert.That(
                    Content(view).GetChild(0).GetComponent<FoodTipCardView>(),
                    Is.Not.Null);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(6)]
        public void Bind_AlwaysExpandsAndHidesScrollbar(int count)
        {
            FoodMaterialTipsView view = CreateView(out GameObject instance);
            try
            {
                view.Bind(CreateEntries(count));

                ScrollRect scrollRect = view.GetComponentInChildren<ScrollRect>(true);
                Assert.That(scrollRect.content.childCount, Is.EqualTo(count));
                Assert.That(scrollRect.vertical, Is.False);
                Assert.That(scrollRect.verticalScrollbar.gameObject.activeSelf, Is.False);

                float expectedHeight = Mathf.Max(
                    80f,
                    Mathf.Max(
                        scrollRect.content.rect.height,
                        LayoutUtility.GetPreferredHeight(scrollRect.content)));
                RectTransform scrollTransform = (RectTransform)scrollRect.transform;
                Assert.That(scrollTransform.rect.height, Is.EqualTo(expectedHeight).Within(0.1f));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void Bind_HidesEmptyListAndRebuildsDeduplicatedCards()
        {
            FoodMaterialTipsView view = CreateView(out GameObject instance);
            try
            {
                view.Bind(System.Array.Empty<FoodMaterialTipsEntry>());
                Assert.That(view.gameObject.activeSelf, Is.False);

                view.Bind(new[]
                {
                    Entry("m_cherry", "樱桃木", "分数+80"),
                    Entry("m_cherry", "重复樱桃木", "不会显示"),
                    Entry("m_walnut", "胡桃木", "分数永久+20"),
                });
                Assert.That(view.gameObject.activeSelf, Is.True);
                Assert.That(Content(view).childCount, Is.EqualTo(2));

                view.Bind(new[] { Entry("m_marble", "大理石", "倍率+2") });
                Assert.That(Content(view).childCount, Is.EqualTo(1));
                Assert.That(Content(view).GetChild(0).name, Is.EqualTo("Material_0"));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void PlaceAroundRectTransform_SeparatesVisibleModulesAroundFoodIcon()
        {
            var canvasObject = new GameObject(
                "FoodTipsLayoutTest",
                typeof(RectTransform),
                typeof(Canvas));
            try
            {
                RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
                canvasRect.sizeDelta = new Vector2(640f, 720f);
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                var targetObject = new GameObject("FoodIcon", typeof(RectTransform), typeof(Image));
                RectTransform target = targetObject.GetComponent<RectTransform>();
                target.SetParent(canvasRect, false);
                target.sizeDelta = new Vector2(48f, 48f);
                target.anchoredPosition = new Vector2(-190f, 130f);

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodTipsPrefabPath);
                FoodTipsView tips = Object.Instantiate(prefab, canvasRect, false)
                    .GetComponent<FoodTipsView>();
                tips.Bind(new FoodTipsData(
                    new FoodSummaryTipsData(
                        "芒果西米露",
                        System.Array.Empty<FoodInfoEntry>(),
                        System.Array.Empty<string>(),
                        countAs: 6),
                    new FoodScoreTipsData(6f, 1f),
                    new[] { Entry("material", "材质", "描述") },
                    System.Array.Empty<FoodInfoEntry>(),
                    System.Array.Empty<FoodInfoEntry>(),
                    System.Array.Empty<FoodInfoEntry>()));
                tips.Show();
                tips.PlaceAroundRectTransform(target, canvas);
                Canvas.ForceUpdateCanvases();

                Rect targetRect = LocalRect(target, canvasRect);
                var visibleModules = new List<RectTransform>();
                for (int i = 0; i < tips.transform.childCount; i++)
                {
                    if (tips.transform.GetChild(i) is RectTransform child
                        && child.gameObject.activeSelf)
                    {
                        visibleModules.Add(child);
                    }
                }

                Assert.That(visibleModules, Has.Count.EqualTo(4));
                for (int i = 0; i < visibleModules.Count; i++)
                {
                    Rect moduleRect = LocalRect(visibleModules[i], canvasRect);
                    Assert.That(
                        moduleRect.Overlaps(targetRect),
                        Is.False,
                        $"{visibleModules[i].name} overlaps the food icon.");
                    for (int j = i + 1; j < visibleModules.Count; j++)
                    {
                        Rect otherRect = LocalRect(visibleModules[j], canvasRect);
                        Assert.That(
                            moduleRect.Overlaps(otherRect),
                            Is.False,
                            $"{visibleModules[i].name} overlaps {visibleModules[j].name}.");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(canvasObject);
            }
        }

        private static void AssertPrefabConfiguration(
            string path,
            FoodTipCardView expectedCard,
            Sprite expectedThumb)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);

            FoodMaterialTipsView view = prefab.GetComponentInChildren<FoodMaterialTipsView>(true);
            Assert.That(view, Is.Not.Null, path);
            var serialized = new SerializedObject(view);
            Assert.That(
                serialized.FindProperty("_itemPrefab").objectReferenceValue,
                Is.SameAs(expectedCard),
                path);
            Assert.That(view.GetComponent<Image>().enabled, Is.False, path);

            ScrollRect scrollRect = view.GetComponentInChildren<ScrollRect>(true);
            Assert.That(
                scrollRect.verticalScrollbar.handleRect.GetComponent<Image>().sprite,
                Is.SameAs(expectedThumb),
                path);
        }

        private static FoodMaterialTipsView CreateView(out GameObject instance)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MaterialTipsPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            instance = Object.Instantiate(prefab);
            return instance.GetComponent<FoodMaterialTipsView>();
        }

        private static RectTransform Content(FoodMaterialTipsView view)
        {
            return view.GetComponentInChildren<ScrollRect>(true).content;
        }

        private static Rect LocalRect(RectTransform rect, RectTransform parent)
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

        private static IReadOnlyList<FoodMaterialTipsEntry> CreateEntries(int count)
        {
            var entries = new List<FoodMaterialTipsEntry>(count);
            for (int i = 0; i < count; i++)
            {
                entries.Add(Entry($"material_{i}", $"材质{i}", $"描述{i}"));
            }

            return entries;
        }

        private static FoodMaterialTipsEntry Entry(string id, string name, string desc)
        {
            return new FoodMaterialTipsEntry(id, name, desc, 1);
        }
    }
}
