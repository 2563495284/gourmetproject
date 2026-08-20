using System.IO;
using GourmetProject.Game.UI.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ToastTests
    {
        private const string SpriteRoot = "Assets/GameMain/Content/Resources/Sprites/UI/ToastFresh/";
        private const string PrefabPath = "Assets/GameMain/Content/Prefabs/UI/Common/ToastForm.prefab";

        [Test]
        public void Queue_IsFifo_Deduplicates_AndDropsOldestWaitingItem()
        {
            var queue = new ToastQueue(2);

            Assert.That(queue.TryEnqueue("A", ToastKind.Info), Is.True);
            Assert.That(queue.TryEnqueue("A", ToastKind.Info), Is.False);
            Assert.That(queue.TryDequeue(out ToastRequest active), Is.True);
            Assert.That(active.Message, Is.EqualTo("A"));
            Assert.That(queue.TryEnqueue("A", ToastKind.Info), Is.False);

            Assert.That(queue.TryEnqueue("B", ToastKind.Success), Is.True);
            Assert.That(queue.TryEnqueue("C", ToastKind.Warning), Is.True);
            Assert.That(queue.TryEnqueue("D", ToastKind.Info), Is.True);
            Assert.That(queue.PendingCount, Is.EqualTo(2));

            queue.CompleteActive();
            Assert.That(queue.TryDequeue(out ToastRequest second), Is.True);
            Assert.That(second.Message, Is.EqualTo("C"));
            queue.CompleteActive();
            Assert.That(queue.TryDequeue(out ToastRequest third), Is.True);
            Assert.That(third.Message, Is.EqualTo("D"));
        }

        [Test]
        public void Queue_RejectsBlankMessages_AndKeepsKindsDistinct()
        {
            var queue = new ToastQueue();

            Assert.That(queue.TryEnqueue(null, ToastKind.Info), Is.False);
            Assert.That(queue.TryEnqueue("   ", ToastKind.Info), Is.False);
            Assert.That(queue.TryEnqueue("同一文案", ToastKind.Info), Is.True);
            Assert.That(queue.TryEnqueue("同一文案", ToastKind.Warning), Is.True);
            Assert.That(queue.PendingCount, Is.EqualTo(2));
        }

        [Test]
        public void Theme_MapsThreeKindsToDistinctIcons()
        {
            ToastTheme theme = AssetDatabase.LoadAssetAtPath<ToastTheme>(SpriteRoot + "ToastTheme.asset");
            Assert.That(theme, Is.Not.Null);
            Assert.That(theme.Bubble, Is.Not.Null);
            Assert.That(theme.Tail, Is.Not.Null);
            Assert.That(theme.ResolveIcon(ToastKind.Info), Is.Not.Null);
            Assert.That(theme.ResolveIcon(ToastKind.Success), Is.Not.Null);
            Assert.That(theme.ResolveIcon(ToastKind.Warning), Is.Not.Null);
            Assert.That(theme.ResolveIcon(ToastKind.Info), Is.Not.SameAs(theme.ResolveIcon(ToastKind.Success)));
            Assert.That(theme.ResolveIcon(ToastKind.Info), Is.Not.SameAs(theme.ResolveIcon(ToastKind.Warning)));
            Assert.That(theme.ResolveIcon(ToastKind.Success), Is.Not.SameAs(theme.ResolveIcon(ToastKind.Warning)));
        }

        [Test]
        public void Prefab_HasSerializedReferences_NoInputBlocking_AndNoWhiteSprite()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            ToastForm form = prefab.GetComponent<ToastForm>();
            ToastPresenter presenter = prefab.GetComponentInChildren<ToastPresenter>(true);
            Assert.That(form, Is.Not.Null);
            Assert.That(presenter, Is.Not.Null);

            var formObject = new SerializedObject(form);
            Assert.That(formObject.FindProperty("_presenter").objectReferenceValue, Is.Not.Null);
            var presenterObject = new SerializedObject(presenter);
            foreach (string propertyName in new[]
                     {
                         "_theme", "_rect", "_group", "_background", "_tail", "_icon", "_message",
                     })
            {
                Assert.That(
                    presenterObject.FindProperty(propertyName).objectReferenceValue,
                    Is.Not.Null,
                    propertyName);
            }

            string whitePath = AssetDatabase.GUIDToAssetPath("7295a5360413845b8b3c9d468aa1e4e8");
            Sprite white = AssetDatabase.LoadAssetAtPath<Sprite>(whitePath);
            foreach (Graphic graphic in prefab.GetComponentsInChildren<Graphic>(true))
            {
                Assert.That(graphic.raycastTarget, Is.False, graphic.name);
                if (graphic is Image image)
                {
                    Assert.That(image.sprite, Is.Not.Null, image.name);
                    Assert.That(image.sprite, Is.Not.SameAs(white), image.name);
                }
            }

            Image bubble = prefab.transform.Find("ToastAnchor/Bubble").GetComponent<Image>();
            Image tail = prefab.transform.Find("ToastAnchor/Tail").GetComponent<Image>();
            Image icon = prefab.transform.Find("ToastAnchor/Icon").GetComponent<Image>();
            Assert.That(bubble.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(bubble.pixelsPerUnitMultiplier, Is.EqualTo(4f).Within(0.001f));
            Assert.That(tail.preserveAspect, Is.True);
            Assert.That(icon.preserveAspect, Is.True);

            CanvasGroup group = prefab.transform.Find("ToastAnchor").GetComponent<CanvasGroup>();
            Assert.That(group.blocksRaycasts, Is.False);
            Assert.That(group.interactable, Is.False);
        }

        [Test]
        public void Sprites_UseRequiredImportPolicy_AndRealCenteredAlpha()
        {
            string[] names =
            {
                "toast_bubble_fresh.png",
                "toast_tail_fresh.png",
                "toast_icon_info_fresh.png",
                "toast_icon_success_fresh.png",
                "toast_icon_warning_fresh.png",
            };

            foreach (string name in names)
            {
                string assetPath = SpriteRoot + name;
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                Assert.That(importer, Is.Not.Null, name);
                Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite), name);
                Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single), name);
                Assert.That(importer.alphaIsTransparency, Is.True, name);
                Assert.That(importer.mipmapEnabled, Is.False, name);
                Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), name);
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear), name);

                AssertRealCenteredAlpha(assetPath, name);
            }

            var bubbleImporter = (TextureImporter)AssetImporter.GetAtPath(
                SpriteRoot + "toast_bubble_fresh.png");
            Assert.That(bubbleImporter.spriteBorder.x, Is.GreaterThan(0f));
            Assert.That(bubbleImporter.spriteBorder.y, Is.GreaterThan(0f));
        }

        private static void AssertRealCenteredAlpha(string assetPath, string label)
        {
            string absolutePath = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                assetPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(ImageConversion.LoadImage(texture, File.ReadAllBytes(absolutePath), false), Is.True);
                Color32[] pixels = texture.GetPixels32();
                int minX = texture.width;
                int minY = texture.height;
                int maxX = -1;
                int maxY = -1;
                bool hasTransparent = false;
                bool hasFractional = false;

                for (int y = 0; y < texture.height; y++)
                {
                    for (int x = 0; x < texture.width; x++)
                    {
                        byte alpha = pixels[y * texture.width + x].a;
                        hasTransparent |= alpha == 0;
                        hasFractional |= alpha > 0 && alpha < byte.MaxValue;
                        if (alpha == 0)
                        {
                            continue;
                        }

                        minX = Mathf.Min(minX, x);
                        minY = Mathf.Min(minY, y);
                        maxX = Mathf.Max(maxX, x);
                        maxY = Mathf.Max(maxY, y);
                    }
                }

                Assert.That(hasTransparent, Is.True, label + " must contain real transparent pixels.");
                Assert.That(hasFractional, Is.True, label + " must retain antialiased alpha.");
                Assert.That(maxX, Is.GreaterThan(minX), label);
                int left = minX;
                int right = texture.width - 1 - maxX;
                int bottom = minY;
                int top = texture.height - 1 - maxY;
                Assert.That(left, Is.EqualTo(right).Within(2), label + " horizontal alpha bounds");
                Assert.That(bottom, Is.EqualTo(top).Within(2), label + " vertical alpha bounds");
                Assert.That(Mathf.Min(left, right, bottom, top), Is.GreaterThanOrEqualTo(3), label);
                Assert.That(Mathf.Max(left, right, bottom, top), Is.LessThanOrEqualTo(7), label);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
