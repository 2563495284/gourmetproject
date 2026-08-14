using GameStartStudio.UI;
using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Meta;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class TmpTextAnimationPresetTests
    {
        [TestCase(ActionDisplayKind.Food, TmpTextAnimationPreset.Food)]
        [TestCase(ActionDisplayKind.Boss, TmpTextAnimationPreset.Boss)]
        [TestCase(ActionDisplayKind.Event, TmpTextAnimationPreset.Event)]
        [TestCase(ActionDisplayKind.Reward, TmpTextAnimationPreset.Reward)]
        [TestCase(ActionDisplayKind.Negative, TmpTextAnimationPreset.Negative)]
        [TestCase(ActionDisplayKind.Shop, TmpTextAnimationPreset.Shop)]
        [TestCase(ActionDisplayKind.Interest, TmpTextAnimationPreset.Interest)]
        [TestCase(ActionDisplayKind.Slot, TmpTextAnimationPreset.Slot)]
        public void DisplayKind_MapsToExpectedPreset(
            ActionDisplayKind displayKind,
            TmpTextAnimationPreset expectedPreset)
        {
            WeekEventTitleAnimation animation = WeekEventCardView.ResolveTitleAnimation(displayKind);

            Assert.That(animation.Preset, Is.EqualTo(expectedPreset));
        }

        [Test]
        public void NormalFood_UsesRestrainedAnimation()
        {
            WeekEventTitleAnimation animation = WeekEventCardView.ResolveTitleAnimation(
                ActionDisplayKind.Food,
                cfg.FoodActionKind.Normal);

            Assert.That(animation.Preset, Is.EqualTo(TmpTextAnimationPreset.Food));
            Assert.That(animation.Intensity, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(animation.Speed, Is.EqualTo(0.83f).Within(0.001f));
        }

        [Test]
        public void SuperFood_UsesHotterAnimation()
        {
            WeekEventTitleAnimation animation = WeekEventCardView.ResolveTitleAnimation(
                ActionDisplayKind.Food,
                cfg.FoodActionKind.Super);

            Assert.That(animation.Preset, Is.EqualTo(TmpTextAnimationPreset.Food));
            Assert.That(animation.Intensity, Is.EqualTo(1.15f).Within(0.001f));
            Assert.That(animation.Speed, Is.EqualTo(1.33f).Within(0.001f));
        }

        [Test]
        public void FeastFood_UsesBossAnimation()
        {
            WeekEventTitleAnimation animation = WeekEventCardView.ResolveTitleAnimation(
                ActionDisplayKind.Food,
                cfg.FoodActionKind.Feast);

            Assert.That(animation.Preset, Is.EqualTo(TmpTextAnimationPreset.Boss));
            Assert.That(animation.Intensity, Is.EqualTo(1.2f).Within(0.001f));
            Assert.That(animation.Speed, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void Rebuild_AfterEnable_AcceptsEmptyRichTextAndMultilineContent()
        {
            var gameObject = new GameObject(
                "TmpAnimationTest",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI),
                typeof(TmpTextVertexAnimator));

            try
            {
                var text = gameObject.GetComponent<TextMeshProUGUI>();
                text.font = TMP_Settings.defaultFontAsset;
                var animator = gameObject.GetComponent<TmpTextVertexAnimator>();

                Assert.DoesNotThrow(() =>
                {
                    text.text = string.Empty;
                    animator.SetPreset(TmpTextAnimationPreset.Event);
                    animator.Rebuild();

                    text.text = "<color=#FFD86A>Daily</color>\nBusiness";
                    animator.SetPreset(TmpTextAnimationPreset.Boss, 1.2f, 1f);
                    animator.Rebuild();

                    animator.enabled = false;
                });
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Rebuild_WhenTitleShrinks_DropsOldCharacterVertices(bool remainsActive)
        {
            var canvasObject = new GameObject(
                "TmpAnimationShrinkCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            var gameObject = new GameObject(
                "TmpAnimationShrinkTest",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI),
                typeof(TmpTextVertexAnimator));

            try
            {
                gameObject.transform.SetParent(canvasObject.transform, false);
                var rect = gameObject.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(400f, 80f);

                var text = gameObject.GetComponent<TextMeshProUGUI>();
                text.font = TMP_Settings.defaultFontAsset;
                var animator = gameObject.GetComponent<TmpTextVertexAnimator>();

                text.text = "ABCDE";
                animator.SetPreset(TmpTextAnimationPreset.TipTitle);
                animator.Rebuild();
                Assert.That(text.textInfo.characterCount, Is.EqualTo(5));

                gameObject.SetActive(remainsActive);
                text.text = "XYZ";
                animator.Rebuild();

                Assert.That(text.textInfo.characterCount, Is.EqualTo(3));
                TMP_MeshInfo meshInfo = text.textInfo.meshInfo[0];
                for (int i = meshInfo.vertexCount; i < meshInfo.vertices.Length; i++)
                {
                    Assert.That(meshInfo.vertices[i], Is.EqualTo(Vector3.zero),
                        $"Unused vertex {i} still contains geometry from the longer title.");
                }
            }
            finally
            {
                Object.DestroyImmediate(canvasObject);
            }
        }
    }
}
