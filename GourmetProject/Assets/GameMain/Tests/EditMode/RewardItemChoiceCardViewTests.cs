using GourmetProject.Game.UI.Meta;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class RewardItemChoiceCardViewTests
    {
        [Test]
        public void SetResolved_CreatesCanvasGroupAndUpdatesInteractionState()
        {
            var gameObject = new GameObject("RewardItemChoiceCardViewTest");
            try
            {
                RewardItemChoiceCardView card = gameObject.AddComponent<RewardItemChoiceCardView>();

                Assert.DoesNotThrow(() => card.SetResolved(false));
                CanvasGroup group = gameObject.GetComponent<CanvasGroup>();
                Assert.That(group, Is.Not.Null);
                Assert.That(group.alpha, Is.EqualTo(1f));
                Assert.That(group.interactable, Is.True);
                Assert.That(group.blocksRaycasts, Is.True);

                card.SetResolved(true);
                Assert.That(group.alpha, Is.EqualTo(0.45f));
                Assert.That(group.interactable, Is.False);
                Assert.That(group.blocksRaycasts, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
