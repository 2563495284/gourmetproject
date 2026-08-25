#if UNITY_EDITOR
using GourmetProject.Game.UI.Battle.View;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class TimelineAxisWeekTransitionTests
    {
        [Test]
        public void MissingCanvasGroup_DisablesPresentationAndUsesImmediateSwapFallback()
        {
            var root = new GameObject("Root", typeof(RectTransform));
            var axisObject = new GameObject("Axis", typeof(RectTransform));
            RectTransform axis = axisObject.GetComponent<RectTransform>();
            axis.SetParent(root.transform, false);
            var presenter = new TimelineAxisFocusPresenter(axis, null);

            try
            {
                int replacements = 0;
                int completions = 0;

                Assert.That(presenter.CanPresent, Is.False);
                presenter.SwapContent(() => replacements++, () => completions++);

                Assert.That(replacements, Is.EqualTo(1));
                Assert.That(completions, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CenteredAnchoredY_AccountsForParentRectAndAxisAnchors()
        {
            var root = new GameObject("Root", typeof(RectTransform));
            RectTransform parent = root.GetComponent<RectTransform>();
            parent.sizeDelta = new Vector2(1200f, 800f);
            var axisObject = new GameObject("Axis", typeof(RectTransform));
            RectTransform axis = axisObject.GetComponent<RectTransform>();
            axis.SetParent(parent, false);
            axis.anchorMin = new Vector2(0.5f, 1f);
            axis.anchorMax = new Vector2(0.5f, 1f);

            try
            {
                Assert.That(
                    TimelineAxisFocusPresenter.CenteredAnchoredY(axis),
                    Is.EqualTo(-400f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
#endif
