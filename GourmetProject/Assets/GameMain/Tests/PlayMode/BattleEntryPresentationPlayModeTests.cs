using System.Collections;
using DG.Tweening;
using GourmetProject.Game.UI.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class BattleEntryPresentationPlayModeTests
    {
        [UnityTest]
        public IEnumerator Presentation_ActivatesOnPlayback_AndSkipResetReleasesInput()
        {
            BattleEntryPresentationView prefab = Resources.Load<BattleEntryPresentationView>(
                "Prefabs/UI/Battle/BattleEntryPresentationOverlay");
            Assert.That(prefab, Is.Not.Null);
            BattleEntryPresentationView view = Object.Instantiate(prefab);
            Tween presentation = null;
            try
            {
                view.EnsureBuilt();
                view.Prepare(7654321, null);
                int skips = 0;
                presentation = view.BuildPresentationTween();
                view.BindSkipHandler(() => skips++);

                Assert.That(view.gameObject.activeSelf, Is.False);
                Assert.That(view.BlocksInput, Is.False);
                yield return null;

                Assert.That(view.gameObject.activeSelf, Is.True);
                Assert.That(view.BlocksInput, Is.True);
                view.OnPointerClick(null);
                view.OnPointerClick(null);
                Assert.That(skips, Is.EqualTo(1));

                view.BindSkipHandler(null);
                Assert.That(view.gameObject.activeSelf, Is.False);
                Assert.That(view.BlocksInput, Is.False);
            }
            finally
            {
                presentation?.Kill(complete: false);
                Object.Destroy(view.gameObject);
            }

            yield return null;
        }
    }
}
