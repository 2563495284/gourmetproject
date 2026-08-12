using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GourmetProject.Game.Tutorial;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Game.UI.Meta;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class TutorialOverlayPlayModeTests
    {
        [UnityTest]
        public IEnumerator InformationalHighlight_BlocksTarget_WhileSignalStepAllowsInteraction()
        {
            RectTransform host = CreateCanvasHost(out GameObject canvasObject);
            var anchorObject = new GameObject("TutorialTestAnchor", typeof(RectTransform));
            RectTransform anchor = anchorObject.GetComponent<RectTransform>();
            anchor.SetParent(host, false);
            anchor.anchorMin = new Vector2(0.4f, 0.4f);
            anchor.anchorMax = new Vector2(0.6f, 0.6f);
            anchor.offsetMin = Vector2.zero;
            anchor.offsetMax = Vector2.zero;
            TutorialAnchorRegistry.Register("tutorial.test.anchor", anchor);

            TutorialOverlayView overlay = TutorialOverlayView.CreateForTests(host);
            Assert.That(overlay, Is.Not.Null);
            int continueCount = 0;
            var info = new TutorialStepDefinition(
                "信息说明",
                TutorialMascotPose.Remind,
                TutorialAdvanceMode.Continue,
                signal: null,
                enterCommand: null,
                exitCommand: null,
                allowTargetInteraction: false,
                "tutorial.test.anchor");
            overlay.Show(info, 0, 1, () => continueCount++);
            yield return null;

            Transform blocker = overlay.transform.Find("HoleInputBlocker");
            Image mascot = overlay.transform.Find("DangDangDialogue/DangDang")?.GetComponent<Image>();
            TutorialOverlayClickSurface maskSurface = overlay.transform.Find("Mask0")?.GetComponent<TutorialOverlayClickSurface>();
            Assert.That(blocker, Is.Not.Null);
            Assert.That(blocker.gameObject.activeSelf, Is.True);
            Assert.That(mascot, Is.Not.Null);
            Assert.That(mascot.sprite, Is.Not.Null);
            Assert.That(maskSurface, Is.Not.Null);

            maskSurface.OnPointerClick(null);
            Assert.That(overlay.IsPresentationReady, Is.True);
            Assert.That(continueCount, Is.Zero, "The first click must only finish the reveal.");
            maskSurface.OnPointerClick(null);
            Assert.That(continueCount, Is.EqualTo(1));

            var signal = new TutorialStepDefinition(
                "操作说明",
                TutorialMascotPose.PointRight,
                TutorialAdvanceMode.Signal,
                "tutorial.test.signal",
                enterCommand: null,
                exitCommand: null,
                allowTargetInteraction: true,
                "tutorial.test.anchor");
            overlay.Show(signal, 0, 1, advance: null);
            yield return null;

            Assert.That(blocker.gameObject.activeSelf, Is.True, "Signal targets stay blocked during the reveal.");
            maskSurface.OnPointerClick(null);
            Assert.That(blocker.gameObject.activeSelf, Is.False);
            Assert.That(continueCount, Is.EqualTo(1), "Signal steps must ignore overlay clicks.");

            TutorialAnchorRegistry.Unregister("tutorial.test.anchor", anchor);
            overlay.Dispose();
            UnityEngine.Object.Destroy(canvasObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DialoguePlacement_AvoidsTheCompositeHole()
        {
            RectTransform host = CreateCanvasHost(out GameObject canvasObject);
            var anchorObject = new GameObject("TutorialLargeAnchor", typeof(RectTransform));
            RectTransform anchor = anchorObject.GetComponent<RectTransform>();
            anchor.SetParent(host, false);
            anchor.anchorMin = new Vector2(0.1f, 0f);
            anchor.anchorMax = new Vector2(0.9f, 0.82f);
            anchor.offsetMin = Vector2.zero;
            anchor.offsetMax = Vector2.zero;
            TutorialAnchorRegistry.Register("tutorial.test.large_anchor", anchor);

            TutorialOverlayView overlay = TutorialOverlayView.CreateForTests(host);
            overlay.Show(
                new TutorialStepDefinition("避让测试文本", TutorialAdvanceMode.Continue, null, "tutorial.test.large_anchor"),
                0,
                1,
                advance: null);
            yield return null;

            Assert.That(overlay.transform.parent, Is.EqualTo(host));
            Assert.That(overlay.transform.GetComponent<Canvas>(), Is.Null);
            AssertWorldCornersMatch(host, (RectTransform)overlay.transform);
            Assert.That(overlay.CurrentDialogueBounds.Overlaps(overlay.CurrentHole), Is.False);

            TutorialAnchorRegistry.Unregister("tutorial.test.large_anchor", anchor);
            overlay.Dispose();
            UnityEngine.Object.Destroy(canvasObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ActionCardDeck_CompletesOnlyAfterAllShowTweens()
        {
            var root = new GameObject("ActionCardDeckTest", typeof(RectTransform));
            var containerObject = new GameObject("Cards", typeof(RectTransform));
            RectTransform container = containerObject.GetComponent<RectTransform>();
            container.SetParent(root.transform, false);
            container.sizeDelta = new Vector2(900f, 600f);

            WeekEventCardView cardPrefab = CreateCardPrefab(root.transform);
            ActionCardDeck deck = root.AddComponent<ActionCardDeck>();
            SetPrivateField(deck, "_cardsContainer", container);
            SetPrivateField(deck, "_cardPrefab", cardPrefab);
            deck.ShowEventOptions(new List<string> { "选项" }, _ => { }, () => { });

            bool completed = false;
            deck.PlayShowWhenReady(() => true, () => completed = true);
            Assert.That(completed, Is.False);
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(completed, Is.False);
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(completed, Is.True);

            deck.Clear();
            UnityEngine.Object.Destroy(root);
            yield return null;
        }

        private static RectTransform CreateCanvasHost(out GameObject canvasObject)
        {
            canvasObject = new GameObject(
                "TutorialTestCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var hostObject = new GameObject("TutorialHost", typeof(RectTransform));
            RectTransform host = hostObject.GetComponent<RectTransform>();
            host.SetParent(canvasObject.transform, false);
            host.anchorMin = Vector2.zero;
            host.anchorMax = Vector2.one;
            host.offsetMin = Vector2.zero;
            host.offsetMax = Vector2.zero;
            Canvas.ForceUpdateCanvases();
            return host;
        }

        private static WeekEventCardView CreateCardPrefab(Transform parent)
        {
            var cardObject = new GameObject("CardPrefab", typeof(RectTransform));
            cardObject.transform.SetParent(parent, false);
            cardObject.SetActive(false);
            WeekEventCardView card = cardObject.AddComponent<WeekEventCardView>();

            Image glow = CreateImage("GlowBorder", cardObject.transform);
            RectTransform particles = new GameObject("Particles", typeof(RectTransform)).GetComponent<RectTransform>();
            particles.SetParent(cardObject.transform, false);
            Image particle = CreateImage("ParticleTemplate", particles);
            SetPrivateField(card, "_glowBorder", glow);
            SetPrivateField(card, "_particleContainer", particles);
            SetPrivateField(card, "_particleTemplate", particle);
            cardObject.SetActive(true);
            return card;
        }

        private static Image CreateImage(string name, Transform parent)
        {
            var imageObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            return imageObject.GetComponent<Image>();
        }

        private static void AssertWorldCornersMatch(RectTransform expected, RectTransform actual)
        {
            var expectedCorners = new Vector3[4];
            var actualCorners = new Vector3[4];
            expected.GetWorldCorners(expectedCorners);
            actual.GetWorldCorners(actualCorners);
            for (int i = 0; i < expectedCorners.Length; i++)
            {
                Assert.That(actualCorners[i].x, Is.EqualTo(expectedCorners[i].x).Within(0.01f), $"corner {i} x");
                Assert.That(actualCorners[i].y, Is.EqualTo(expectedCorners[i].y).Within(0.01f), $"corner {i} y");
            }
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {name}");
            field.SetValue(target, value);
        }
    }
}
