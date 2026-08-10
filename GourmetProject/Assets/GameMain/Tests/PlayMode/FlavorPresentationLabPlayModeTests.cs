using System.Collections;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Development;
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
    }
}
