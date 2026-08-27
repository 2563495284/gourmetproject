using System.Collections;
using GourmetProject.Game.UI.Battle.View;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class TimelineAxisWeekTransitionPlayModeTests
    {
        [UnityTest]
        public IEnumerator SwapWeekContent_ShowsNewWeekBetweenHiddenOldAndNewAxes_AndRestoresEverything()
        {
            TestRig rig = CreateRig();
            try
            {
                int entered = 0;
                int replaced = 0;
                int contentEntered = 0;
                int exited = 0;
                float displayedDay = 7f;
                float alphaAtReplacement = 1f;
                float dayAtReplacement = -1f;
                Vector3 scaleAtReplacement = Vector3.zero;
                GameObject oldWeekNode = new GameObject("OldWeekNode");
                oldWeekNode.transform.SetParent(rig.Axis, false);
                GameObject newWeekNode = null;

                rig.Presenter.Enter(() => entered++);
                Assert.That(rig.Group.interactable, Is.False);
                Assert.That(rig.Group.blocksRaycasts, Is.False);
                yield return new WaitForSecondsRealtime(0.70f);

                Assert.That(entered, Is.EqualTo(1));
                Assert.That(
                    rig.Axis.anchoredPosition.y,
                    Is.EqualTo(TimelineAxisFocusPresenter.CenteredAnchoredY(rig.Axis)).Within(1f));

                rig.Presenter.SwapWeekContent(
                    2,
                    () =>
                    {
                        replaced++;
                        alphaAtReplacement = rig.Group.alpha;
                        dayAtReplacement = displayedDay;
                        scaleAtReplacement = rig.Axis.localScale;
                        oldWeekNode.SetActive(false);
                        newWeekNode = new GameObject("NewWeekNode");
                        newWeekNode.transform.SetParent(rig.Axis, false);
                        displayedDay = 0f;
                    },
                    () => contentEntered++);

                yield return new WaitForSecondsRealtime(0.18f);
                Assert.That(replaced, Is.Zero);
                Assert.That(displayedDay, Is.EqualTo(7f));
                Assert.That(oldWeekNode.activeSelf, Is.True);
                Assert.That(newWeekNode, Is.Null);

                yield return new WaitForSecondsRealtime(0.28f);
                Assert.That(replaced, Is.EqualTo(1));
                Assert.That(alphaAtReplacement, Is.LessThanOrEqualTo(0.01f));
                Assert.That(dayAtReplacement, Is.EqualTo(7f));
                Assert.That(scaleAtReplacement.x, Is.EqualTo(rig.RestScale.x * 0.82f).Within(0.01f));
                Assert.That(scaleAtReplacement.y, Is.EqualTo(rig.RestScale.y * 0.82f).Within(0.01f));
                Assert.That(displayedDay, Is.Zero);
                Assert.That(oldWeekNode.activeSelf, Is.False);
                Assert.That(newWeekNode, Is.Not.Null);
                Assert.That(rig.Group.alpha, Is.Zero.Within(0.001f));
                Assert.That(rig.Axis.localScale.x, Is.EqualTo(rig.RestScale.x * 1.08f).Within(0.01f));
                Assert.That(rig.Axis.localScale.y, Is.EqualTo(rig.RestScale.y * 1.08f).Within(0.01f));

                Transform weekTitle = rig.Root.transform.Find("TimelineAxisWeekLabel");
                Assert.That(weekTitle, Is.Not.Null);
                Assert.That(weekTitle.gameObject.activeSelf, Is.True);
                TMP_Text weekTitleText = weekTitle.GetComponent<TMP_Text>();
                Assert.That(weekTitleText.text, Is.EqualTo("第2周"));
                Assert.That(
                    weekTitleText.color,
                    Is.EqualTo(Color.white).Using(ColorEqualityComparer.Instance));
                Assert.That(weekTitleText.outlineColor, Is.EqualTo(new Color32(0, 0, 0, 220)));
                Assert.That(weekTitleText.outlineWidth, Is.EqualTo(0.18f).Within(0.001f));

                yield return new WaitForSecondsRealtime(0.22f);
                CanvasGroup weekTitleGroup = weekTitle.GetComponent<CanvasGroup>();
                Assert.That(weekTitleGroup.alpha, Is.EqualTo(1f).Within(0.02f));
                Assert.That(rig.Group.alpha, Is.Zero.Within(0.001f));

                yield return new WaitForSecondsRealtime(0.46f);
                Assert.That(weekTitle.gameObject.activeSelf, Is.True);
                Assert.That(weekTitleGroup.alpha, Is.EqualTo(1f).Within(0.02f));
                Assert.That(rig.Group.alpha, Is.Zero.Within(0.001f));

                yield return new WaitForSecondsRealtime(0.10f);
                Assert.That(weekTitle.gameObject.activeSelf, Is.True);
                Assert.That(weekTitleGroup.alpha, Is.GreaterThan(0f));
                Assert.That(weekTitleGroup.alpha, Is.LessThan(1f));
                Assert.That(rig.Group.alpha, Is.Zero.Within(0.001f));

                yield return new WaitForSecondsRealtime(0.30f);
                Assert.That(weekTitle.gameObject.activeSelf, Is.False);
                Assert.That(rig.Group.alpha, Is.GreaterThan(0f));
                Assert.That(rig.Group.alpha, Is.LessThan(rig.RestAlpha));
                Assert.That(rig.Axis.localScale.x, Is.GreaterThan(rig.RestScale.x));
                Assert.That(rig.Axis.localScale.y, Is.GreaterThan(rig.RestScale.y));

                yield return new WaitForSecondsRealtime(0.60f);
                Assert.That(contentEntered, Is.EqualTo(1));
                Assert.That(rig.Group.alpha, Is.EqualTo(rig.RestAlpha).Within(0.01f));
                Assert.That(rig.Axis.localScale, Is.EqualTo(rig.RestScale).Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(rig.Group.interactable, Is.False);
                Assert.That(rig.Group.blocksRaycasts, Is.False);

                rig.Presenter.Exit(() => exited++);
                yield return new WaitForSecondsRealtime(1.34f);

                Assert.That(exited, Is.EqualTo(1));
                Assert.That(rig.Axis.anchoredPosition, Is.EqualTo(rig.RestPosition).Using(Vector2ComparerWithEqualsOperator.Instance));
                Assert.That(rig.Axis.localScale, Is.EqualTo(rig.RestScale).Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(rig.Group.alpha, Is.EqualTo(rig.RestAlpha).Within(0.001f));
                Assert.That(rig.Group.interactable, Is.True);
                Assert.That(rig.Group.blocksRaycasts, Is.True);
                Assert.That(rig.Axis.GetSiblingIndex(), Is.EqualTo(rig.RestSiblingIndex));

                yield return new WaitForSecondsRealtime(0.1f);
                Assert.That(entered, Is.EqualTo(1));
                Assert.That(replaced, Is.EqualTo(1));
                Assert.That(contentEntered, Is.EqualTo(1));
                Assert.That(exited, Is.EqualTo(1));
            }
            finally
            {
                rig.Presenter.Cancel();
                Object.DestroyImmediate(rig.Root);
            }
        }

        [UnityTest]
        public IEnumerator CancelDuringOldContentExit_RestoresTransformLayerAlphaAndInput()
        {
            TestRig rig = CreateRig();
            try
            {
                int replacements = 0;
                int completions = 0;

                rig.Presenter.Enter(null);
                yield return new WaitForSecondsRealtime(0.70f);
                rig.Presenter.SwapWeekContent(2, () => replacements++, () => completions++);
                yield return new WaitForSecondsRealtime(0.16f);

                rig.Presenter.Cancel();

                Assert.That(replacements, Is.Zero);
                Assert.That(completions, Is.Zero);
                Assert.That(rig.Axis.anchoredPosition, Is.EqualTo(rig.RestPosition).Using(Vector2ComparerWithEqualsOperator.Instance));
                Assert.That(rig.Axis.localScale, Is.EqualTo(rig.RestScale).Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(rig.Group.alpha, Is.EqualTo(rig.RestAlpha).Within(0.001f));
                Assert.That(rig.Group.interactable, Is.True);
                Assert.That(rig.Group.blocksRaycasts, Is.True);
                Assert.That(rig.Axis.GetSiblingIndex(), Is.EqualTo(rig.RestSiblingIndex));

                Transform backdrop = rig.Root.transform.Find("TimelineAxisFocusBackdrop");
                Assert.That(backdrop, Is.Not.Null);
                Assert.That(backdrop.gameObject.activeSelf, Is.False);
                Transform weekTitle = rig.Root.transform.Find("TimelineAxisWeekLabel");
                Assert.That(weekTitle, Is.Not.Null);
                Assert.That(weekTitle.gameObject.activeSelf, Is.False);

                yield return new WaitForSecondsRealtime(0.50f);
                Assert.That(replacements, Is.Zero);
                Assert.That(completions, Is.Zero);
            }
            finally
            {
                rig.Presenter.Cancel();
                Object.DestroyImmediate(rig.Root);
            }
        }

        [UnityTest]
        public IEnumerator CancelDuringWeekTitleHold_HidesTitleAndRestoresAxisBackdropAndInput()
        {
            TestRig rig = CreateRig();
            try
            {
                int replacements = 0;
                int completions = 0;

                rig.Presenter.Enter(null);
                yield return new WaitForSecondsRealtime(0.70f);
                rig.Presenter.SwapWeekContent(3, () => replacements++, () => completions++);
                yield return new WaitForSecondsRealtime(0.75f);

                Transform weekTitle = rig.Root.transform.Find("TimelineAxisWeekLabel");
                Assert.That(replacements, Is.EqualTo(1));
                Assert.That(completions, Is.Zero);
                Assert.That(weekTitle, Is.Not.Null);
                Assert.That(weekTitle.gameObject.activeSelf, Is.True);
                Assert.That(weekTitle.GetComponent<TMP_Text>().text, Is.EqualTo("第3周"));
                Assert.That(rig.Group.alpha, Is.Zero.Within(0.001f));

                rig.Presenter.Cancel();

                Assert.That(completions, Is.Zero);
                Assert.That(weekTitle.gameObject.activeSelf, Is.False);
                Assert.That(rig.Axis.anchoredPosition, Is.EqualTo(rig.RestPosition).Using(Vector2ComparerWithEqualsOperator.Instance));
                Assert.That(rig.Axis.localScale, Is.EqualTo(rig.RestScale).Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(rig.Group.alpha, Is.EqualTo(rig.RestAlpha).Within(0.001f));
                Assert.That(rig.Group.interactable, Is.True);
                Assert.That(rig.Group.blocksRaycasts, Is.True);
                Assert.That(rig.Axis.GetSiblingIndex(), Is.EqualTo(rig.RestSiblingIndex));

                Transform backdrop = rig.Root.transform.Find("TimelineAxisFocusBackdrop");
                Assert.That(backdrop, Is.Not.Null);
                Assert.That(backdrop.gameObject.activeSelf, Is.False);

                yield return new WaitForSecondsRealtime(0.60f);
                Assert.That(completions, Is.Zero);
            }
            finally
            {
                rig.Presenter.Cancel();
                Object.DestroyImmediate(rig.Root);
            }
        }

        private static TestRig CreateRig()
        {
            var root = new GameObject("Root", typeof(RectTransform));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(1200f, 800f);

            var before = new GameObject("Before", typeof(RectTransform));
            before.transform.SetParent(rootRect, false);
            var axisObject = new GameObject("Axis", typeof(RectTransform), typeof(CanvasGroup));
            RectTransform axis = axisObject.GetComponent<RectTransform>();
            axis.SetParent(rootRect, false);
            axis.anchorMin = new Vector2(0.5f, 1f);
            axis.anchorMax = new Vector2(0.5f, 1f);
            axis.pivot = new Vector2(0.5f, 0.5f);
            axis.sizeDelta = new Vector2(900f, 120f);
            axis.anchoredPosition = new Vector2(17f, -76f);
            axis.localScale = new Vector3(1.15f, 0.90f, 1f);
            CanvasGroup group = axisObject.GetComponent<CanvasGroup>();
            group.alpha = 0.86f;
            group.interactable = true;
            group.blocksRaycasts = true;
            var styleSourceObject = new GameObject(
                "CurrentDayText",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            styleSourceObject.transform.SetParent(axis, false);
            TMP_Text styleSource = styleSourceObject.GetComponent<TMP_Text>();
            styleSource.fontSize = 20f;
            styleSource.color = Color.white;
            styleSource.raycastTarget = false;
            var after = new GameObject("After", typeof(RectTransform));
            after.transform.SetParent(rootRect, false);

            return new TestRig
            {
                Root = root,
                Axis = axis,
                Group = group,
                Presenter = new TimelineAxisFocusPresenter(axis, group, styleSource),
                RestPosition = axis.anchoredPosition,
                RestScale = axis.localScale,
                RestAlpha = group.alpha,
                RestSiblingIndex = axis.GetSiblingIndex(),
            };
        }

        private sealed class TestRig
        {
            public GameObject Root;
            public RectTransform Axis;
            public CanvasGroup Group;
            public TimelineAxisFocusPresenter Presenter;
            public Vector2 RestPosition;
            public Vector3 RestScale;
            public float RestAlpha;
            public int RestSiblingIndex;
        }
    }
}
