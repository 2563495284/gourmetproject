using System.Collections;
using System.Threading;
using GourmetProject.Game.UI.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class BossDebuffPresentationPlayModeTests
    {
        [Test]
        public void Presentation_UsesDistinctPointAndGrabHandSprites()
        {
            Sprite pointHand = Resources.Load<Sprite>("Sprites/UI/serve_point_hand");
            Sprite grabHand = Resources.Load<Sprite>("Sprites/UI/serve_hand");

            Assert.That(pointHand, Is.Not.Null);
            Assert.That(grabHand, Is.Not.Null);
            Assert.That(pointHand, Is.Not.SameAs(grabHand));
        }

        [UnityTest]
        public IEnumerator PresentationLock_ReleasesAfterSequenceCompletes()
        {
            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var host = new GameObject("Host", typeof(RectTransform));
            host.transform.SetParent(canvasObject.transform, false);
            var view = host.AddComponent<BossDebuffPresentationView>();
            view.EnsureBuilt();

            bool completed = false;
            _ = RunAsync();
            Assert.That(view.IsPlaying, Is.True);

            for (int i = 0; i < 8 && !completed; i++)
            {
                yield return null;
            }

            Assert.That(completed, Is.True);
            Assert.That(view.IsPlaying, Is.False);
            Object.Destroy(canvasObject);

            async Awaitable RunAsync()
            {
                await view.PlayLockedAsync(
                    async token =>
                    {
                        await Awaitable.NextFrameAsync(token);
                        await Awaitable.NextFrameAsync(token);
                    },
                    CancellationToken.None);
                completed = true;
            }
        }

        [UnityTest]
        public IEnumerator PresentationLock_CancelReleasesImmediately()
        {
            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var host = new GameObject("Host", typeof(RectTransform));
            host.transform.SetParent(canvasObject.transform, false);
            var view = host.AddComponent<BossDebuffPresentationView>();
            view.EnsureBuilt();

            _ = view.PlayLockedAsync(
                async token =>
                {
                    while (true)
                    {
                        await Awaitable.NextFrameAsync(token);
                    }
                },
                CancellationToken.None);
            Assert.That(view.IsPlaying, Is.True);

            view.CancelCurrent();
            Assert.That(view.IsPlaying, Is.False);
            Object.Destroy(canvasObject);
            yield return null;
        }
    }
}
