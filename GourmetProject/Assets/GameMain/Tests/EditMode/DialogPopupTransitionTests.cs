#if UNITY_EDITOR
using GourmetProject.Game.UI.Common;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DialogPopupTransitionTests
    {
        [TestCase(false, 0f)]
        [TestCase(true, 1f)]
        public void PreparePopupLayers_SeparatesBackdropFromContent(
            bool isHandoffArrival,
            float expectedBackdropAlpha)
        {
            var backdropObject = new GameObject("Backdrop", typeof(CanvasGroup));
            var contentObject = new GameObject("Content", typeof(CanvasGroup));
            CanvasGroup backdrop = backdropObject.GetComponent<CanvasGroup>();
            CanvasGroup content = contentObject.GetComponent<CanvasGroup>();

            try
            {
                UITransition.PreparePopupLayers(backdrop, content, isHandoffArrival);

                Assert.That(backdrop.alpha, Is.EqualTo(expectedBackdropAlpha));
                Assert.That(backdrop.interactable, Is.True);
                Assert.That(backdrop.blocksRaycasts, Is.True);
                Assert.That(content.alpha, Is.Zero);
                Assert.That(content.interactable, Is.False);
                Assert.That(content.blocksRaycasts, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(contentObject);
                Object.DestroyImmediate(backdropObject);
            }
        }
    }
}
#endif
