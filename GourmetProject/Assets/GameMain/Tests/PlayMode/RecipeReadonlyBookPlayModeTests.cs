using System;
using System.Collections;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GourmetProject.Tests.PlayMode
{
    public sealed class RecipeReadonlyBookPlayModeTests
    {
        [UnityTest]
        public IEnumerator ReadonlyPool_RendersResponsiveChrome_AndReusesWarehouse()
        {
#if UNITY_EDITOR
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/GameMain/Content/Prefabs/UI/Meta/Recipes/RecipeReadonlyBookView.prefab");
#else
            GameObject prefab = null;
#endif
            Assert.That(prefab, Is.Not.Null);

            var canvasObject = new GameObject(
                "RecipeBookTestCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject instance = UnityEngine.Object.Instantiate(
                prefab,
                canvasObject.transform,
                false);
            try
            {
                var rect = (RectTransform)instance.transform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(1350f, 750f);
                rect.anchoredPosition = Vector2.zero;
                var database = new GameplayDatabase(
                    Array.Empty<DishDef>(),
                    Array.Empty<SkillDef>(),
                    Array.Empty<FlavorDef>(),
                    Array.Empty<RecipeDef>());
                var view = instance.GetComponent<RecipeReadonlyBookView>();

                view.OpenForReadonlyDishPool(
                    database,
                    Array.Empty<string>(),
                    "可能获得的食物");
                yield return null;
                Canvas.ForceUpdateCanvases();

                var chrome = (RectTransform)instance.transform.Find("BookChrome");
                Assert.That(
                    chrome.rect.width / chrome.rect.height,
                    Is.EqualTo(1.8f).Within(0.001f));
                Assert.That(
                    chrome.rect.height,
                    Is.EqualTo(714.6667f).Within(0.01f));

                rect.sizeDelta = new Vector2(1000f, 500f);
                yield return null;
                Canvas.ForceUpdateCanvases();
                float expectedCompactScale = 500f / 714.6667f;
                Assert.That(
                    chrome.localScale.x,
                    Is.EqualTo(expectedCompactScale).Within(0.001f));
                Assert.That(
                    chrome.localScale.y,
                    Is.EqualTo(expectedCompactScale).Within(0.001f));

                rect.sizeDelta = new Vector2(1350f, 750f);
                yield return null;
                Canvas.ForceUpdateCanvases();
                TMP_Text title = chrome.Find("TitleTab/Title")
                    .GetComponent<TMP_Text>();
                Assert.That(title.text, Is.EqualTo("可能获得的食物"));
                Assert.That(instance.transform.Find("BackButton").gameObject.activeSelf, Is.False);

                RecipeWarehouseView first = instance
                    .GetComponentInChildren<RecipeWarehouseView>(true);
                view.OpenForReadonlyDishPool(
                    database,
                    Array.Empty<string>(),
                    "再次预览");
                yield return null;
                RecipeWarehouseView[] warehouses = instance
                    .GetComponentsInChildren<RecipeWarehouseView>(true);
                Assert.That(warehouses, Has.Length.EqualTo(1));
                Assert.That(warehouses[0], Is.SameAs(first));
                Assert.That(title.text, Is.EqualTo("再次预览"));
            }
            finally
            {
                UnityEngine.Object.Destroy(instance);
                UnityEngine.Object.Destroy(canvasObject);
            }
        }
    }
}
