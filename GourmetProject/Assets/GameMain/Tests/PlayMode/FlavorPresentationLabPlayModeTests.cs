using System.Collections;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Development;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class FlavorPresentationLabPlayModeTests
    {
        [UnityTest]
        public IEnumerator StandaloneOpenAndClose_CreatesAndReleasesLivePreviewRig()
        {
            int baseline = FlavorPrototypePreviewRig.ActiveRigCount;
            var root = new GameObject(
                "FlavorLabTest",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            FlavorPresentationLabForm form = root.AddComponent<FlavorPresentationLabForm>();

            try
            {
                form.OpenPreviewForTests();
                yield return null;

                Assert.That(form.PreviewRigActive, Is.True);
                Assert.That(form.PreviewTexturesCreated, Is.True);
                Assert.That(FlavorPrototypePreviewRig.ActiveRigCount, Is.EqualTo(baseline + 1));

                float movingStart = form.MotionTimeForTests;
                yield return new WaitForSecondsRealtime(0.05f);
                Assert.That(form.MotionTimeForTests, Is.GreaterThan(movingStart));

                form.ToggleAnimationForTests();
                float pausedAt = form.MotionTimeForTests;
                yield return new WaitForSecondsRealtime(0.05f);
                Assert.That(form.MotionTimeForTests, Is.EqualTo(pausedAt).Within(0.0001f));
                form.ToggleAnimationForTests();

                int[] masks = { 1, 3, 51, 63, FlavorVisualCatalog.AllMask };
                foreach (int mask in masks)
                {
                    form.SetFlavorMaskForTests(mask);
                    yield return null;
                    Assert.That(form.CurrentFlavorMask, Is.EqualTo(mask));
                }

                Assert.That(form.CurrentFlavorMask, Is.EqualTo(FlavorVisualCatalog.AllMask));
                Assert.That(form.ActiveParticleSystemCount, Is.EqualTo(7));
                Assert.That(form.PlayingParticleSystemCount, Is.EqualTo(7));

                Assert.That(form.CurrentDishIndex, Is.Zero);
                for (int expected = 1; expected <= 3; expected++)
                {
                    form.CycleDishForTests();
                    yield return null;
                    Assert.That(form.CurrentDishIndex, Is.EqualTo(expected % 3));
                    Assert.That(form.PreviewTexturesCreated, Is.True);
                }

                var textures = form.PreviewTexturesForTests;

                form.ClosePreviewForTests();
                yield return null;

                Assert.That(form.PreviewRigActive, Is.False);
                Assert.That(FlavorPrototypePreviewRig.ActiveRigCount, Is.EqualTo(baseline));
                foreach (RenderTexture texture in textures)
                {
                    Assert.That(texture == null || !texture.IsCreated(), Is.True);
                }
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator FormalRenderTexture_ReusesTargetAndUpdatesAnimatedFlavor()
        {
            var sourceTexture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var pixels = new Color32[16 * 16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(242, 224, 190, 255);
            }
            sourceTexture.SetPixels32(pixels);
            sourceTexture.Apply();
            Sprite sprite = Sprite.Create(
                sourceTexture,
                new Rect(0f, 0f, 16f, 16f),
                new Vector2(0.5f, 0.5f),
                16f);
            var cellObject = new GameObject("CellPrefab");
            SpriteRenderer cell = cellObject.AddComponent<SpriteRenderer>();
            cell.sprite = sprite;
            var badgeObject = new GameObject("BadgePrefab");
            DishValueBadgeView badge = badgeObject.AddComponent<DishValueBadgeView>();
            var dish = new DishDef(
                "rt_flavor_test",
                "RT Flavor Test",
                10,
                DishShape.FromRows(new[] { "X" }),
                0,
                0,
                1f,
                System.Array.Empty<string>(),
                "t_sour",
                allowRotate: false);
            string[] flavors = { "t_sour", "t_spicy" };
            RenderTexture renderTexture = null;
            Texture2D readback = null;

            try
            {
                renderTexture = DishIconPreviewRenderer.Render(
                    dish,
                    sprite,
                    10,
                    flavors,
                    cell,
                    badge,
                    64,
                    DishIconPreviewMode.Card);
                Assert.That(renderTexture, Is.Not.Null);
                Assert.That(renderTexture.IsCreated(), Is.True);
                readback = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                long firstChecksum = ReadbackChecksum(renderTexture, readback);

                DishIconPreviewRenderer rig = Resources.FindObjectsOfTypeAll<DishIconPreviewRenderer>()[0];
                int pooledObjectCount = rig.GetComponentsInChildren<Transform>(true).Length;
                yield return new WaitForSecondsRealtime(0.18f);

                bool rendered = DishIconPreviewRenderer.RenderInto(
                    renderTexture,
                    dish,
                    sprite,
                    10,
                    flavors,
                    cell,
                    badge,
                    64,
                    DishIconPreviewMode.Card);
                long secondChecksum = ReadbackChecksum(renderTexture, readback);

                Assert.That(rendered, Is.True);
                Assert.That(secondChecksum, Is.Not.EqualTo(firstChecksum),
                    "同一个 RT 在时间推进后应包含不同的动态风味帧。");
                Assert.That(
                    rig.GetComponentsInChildren<Transform>(true).Length,
                    Is.EqualTo(pooledObjectCount),
                    "实时重绘不能逐帧创建棋盘格、标签或相机。");
            }
            finally
            {
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.Destroy(renderTexture);
                }
                if (readback != null) UnityEngine.Object.Destroy(readback);
                UnityEngine.Object.Destroy(sprite);
                UnityEngine.Object.Destroy(sourceTexture);
                UnityEngine.Object.Destroy(cellObject);
                UnityEngine.Object.Destroy(badgeObject);
                foreach (DishIconPreviewRenderer rig in Resources.FindObjectsOfTypeAll<DishIconPreviewRenderer>())
                {
                    if (rig != null) UnityEngine.Object.Destroy(rig.gameObject);
                }
            }

            yield return null;
        }

        private static long ReadbackChecksum(RenderTexture source, Texture2D target)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = source;
            target.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
            target.Apply(false, false);
            RenderTexture.active = previous;

            Color32[] colors = target.GetPixels32();
            long checksum = 17;
            for (int i = 0; i < colors.Length; i += 7)
            {
                Color32 color = colors[i];
                checksum = unchecked(checksum * 31 + color.r * 3 + color.g * 5 + color.b * 7 + color.a * 11);
            }
            return checksum;
        }
    }
}
