#if UNITY_EDITOR
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class EventPanelWideLayoutTests
    {
        private const string EventPanelPath =
            "Assets/GameMain/Content/Prefabs/UI/Battle/EventPanel.prefab";

        private static readonly string[] EventSpriteNames =
        {
            "midnight_tasting",
            "ancestral_recipe_page",
            "kitchen_god_statue",
            "crossroad_sign",
            "interest",
            "slot_machine",
            "new_event",
            "last_supper",
            "twin_peaks_diner",
            "chuka_ichiban_trial",
        };

        [TestCaseSource(nameof(EventSpriteNames))]
        public void WideEventSprite_LoadsAtExpectedSize(string eventName)
        {
            string resourcePath = $"Sprites/UI/Events/event_{eventName}_wide";
            Sprite sprite = Resources.Load<Sprite>(resourcePath)
                ?? Resources.LoadAll<Sprite>(resourcePath).FirstOrDefault();

            Assert.That(sprite, Is.Not.Null, $"Missing wide event sprite: {eventName}");
            Assert.That(sprite.texture.width, Is.EqualTo(1920));
            Assert.That(sprite.texture.height, Is.EqualTo(1632));
        }

        [Test]
        public void EventPanel_UsesTwentyBySeventeenLayout()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EventPanelPath);
            Assert.That(prefab, Is.Not.Null);

            AspectRatioFitter fitter = prefab.GetComponent<AspectRatioFitter>();
            Assert.That(fitter, Is.Not.Null);
            Assert.That(fitter.aspectMode, Is.EqualTo(AspectRatioFitter.AspectMode.FitInParent));
            Assert.That(fitter.aspectRatio, Is.EqualTo(20f / 17f).Within(0.000001f));

            RectTransform content = prefab.transform.Find("Content") as RectTransform;
            Assert.That(content, Is.Not.Null);
            Assert.That(content.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(content.anchorMax.x, Is.EqualTo(1f).Within(0.000001f));
            Assert.That(content.anchorMax.y, Is.EqualTo(608f / 1632f).Within(0.000001f));
            Assert.That(content.sizeDelta, Is.EqualTo(Vector2.zero));

            Image illustration = prefab.GetComponentsInChildren<Image>(true)
                .Single(image => image.gameObject.name == "Illustration");
            Assert.That(illustration.preserveAspect, Is.True);
        }

        [TestCase(1920f, 1080f)]
        [TestCase(1727f, 1465f)]
        public void EventPanel_FitsReferenceViewportWithoutChangingRatio(float width, float height)
        {
            GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform));
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EventPanelPath);
            GameObject instance = Object.Instantiate(prefab, viewportObject.transform);

            try
            {
                RectTransform viewport = (RectTransform)viewportObject.transform;
                viewport.sizeDelta = new Vector2(width, height);

                AspectRatioFitter fitter = instance.GetComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.None;
                fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(viewport);

                RectTransform page = (RectTransform)instance.transform;
                Assert.That(page.rect.width / page.rect.height, Is.EqualTo(20f / 17f).Within(0.001f));
                Assert.That(page.rect.width, Is.LessThanOrEqualTo(width + 0.01f));
                Assert.That(page.rect.height, Is.LessThanOrEqualTo(height + 0.01f));
            }
            finally
            {
                Object.DestroyImmediate(viewportObject);
            }
        }

        [Test]
        public void GeneratedEventConfig_UsesWideSpritePaths()
        {
            string configPath = Path.Combine(Application.streamingAssetsPath, "Config/tbevent.json");
            string json = File.ReadAllText(configPath);

            foreach (string eventName in EventSpriteNames)
            {
                Assert.That(
                    json,
                    Does.Contain($"Sprites/UI/Events/event_{eventName}_wide"),
                    $"Config is not using the wide sprite for {eventName}");
            }
        }
    }
}
#endif
