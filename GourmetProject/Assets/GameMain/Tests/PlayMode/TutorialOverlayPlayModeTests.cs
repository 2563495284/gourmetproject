using System.Collections;
using GourmetProject.Game.Tutorial;
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
            var anchorObject = new GameObject("TutorialTestAnchor", typeof(RectTransform));
            RectTransform anchor = anchorObject.GetComponent<RectTransform>();
            anchor.position = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
            anchor.sizeDelta = new Vector2(240f, 100f);
            TutorialAnchorRegistry.Register("tutorial.test.anchor", anchor);

            TutorialOverlayView overlay = TutorialOverlayView.Create();
            var info = new TutorialStepDefinition(
                "信息说明",
                TutorialMascotPose.Remind,
                TutorialAdvanceMode.Continue,
                signal: null,
                enterCommand: null,
                exitCommand: null,
                allowTargetInteraction: false,
                "tutorial.test.anchor");
            overlay.Show(info, 0, 1, () => { });
            yield return null;

            Transform blocker = overlay.transform.Find("HoleInputBlocker");
            Image mascot = overlay.transform.Find("DangDangDialogue/DangDang")?.GetComponent<Image>();
            Assert.That(blocker, Is.Not.Null);
            Assert.That(blocker.gameObject.activeSelf, Is.True);
            Assert.That(mascot, Is.Not.Null);
            Assert.That(mascot.sprite, Is.Not.Null);

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

            Assert.That(blocker.gameObject.activeSelf, Is.False);

            TutorialAnchorRegistry.Unregister("tutorial.test.anchor", anchor);
            overlay.Dispose();
            Object.Destroy(anchorObject);
            yield return null;
        }
    }
}
