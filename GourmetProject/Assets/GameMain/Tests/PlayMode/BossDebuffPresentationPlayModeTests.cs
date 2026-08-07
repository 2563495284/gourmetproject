using System.Collections;
using System.Threading;
using GourmetProject.Game.UI.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class BossDebuffPresentationPlayModeTests
    {
        private const string PresentationPrefabPath = "Prefabs/UI/BossDebuffPresentationOverlay";

        [Test]
        public void Presentation_UsesDistinctPointAndGrabHandSprites()
        {
            Sprite pointHand = Resources.Load<Sprite>("Sprites/UI/serve_point_hand");
            Sprite grabHand = Resources.Load<Sprite>("Sprites/UI/serve_hand");

            Assert.That(pointHand, Is.Not.Null);
            Assert.That(grabHand, Is.Not.Null);
            Assert.That(pointHand, Is.Not.SameAs(grabHand));
        }

        [Test]
        public void Presentation_EnsureBuilt_DoesNotCreateRuntimeHierarchy()
        {
            BossDebuffPresentationView view = CreateView();
            int childCount = view.transform.childCount;

            view.EnsureBuilt();

            Assert.That(view.transform.childCount, Is.EqualTo(childCount));
            Object.DestroyImmediate(view.gameObject);
        }

        [UnityTest]
        public IEnumerator PresentationLock_ReleasesAfterSequenceCompletes()
        {
            BossDebuffPresentationView view = CreateView();
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
            Object.Destroy(view.gameObject);

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
            BossDebuffPresentationView view = CreateView();
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
            Object.Destroy(view.gameObject);
            yield return null;
        }

        private static BossDebuffPresentationView CreateView()
        {
            BossDebuffPresentationView prefab =
                Resources.Load<BossDebuffPresentationView>(PresentationPrefabPath);
            Assert.That(prefab, Is.Not.Null, $"Missing presentation prefab at Resources/{PresentationPrefabPath}.");
            return Object.Instantiate(prefab);
        }
    }
}
