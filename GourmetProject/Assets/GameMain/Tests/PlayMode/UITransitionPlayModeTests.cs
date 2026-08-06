using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class UITransitionPlayModeTests
    {
        [UnityTest]
        public IEnumerator CoverSwap_AtZeroTimeScale_LeavesPersistentColumnsUntouched()
        {
            float previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            var canvasObject = new GameObject(
                "BattleForm",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(BattleForm));
            var left = new GameObject("Left", typeof(RectTransform), typeof(CanvasGroup));
            var center = new GameObject("Center", typeof(RectTransform), typeof(CanvasGroup));
            var cover = new GameObject(
                "Cover",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            var right = new GameObject("Right", typeof(RectTransform), typeof(CanvasGroup));

            try
            {
                left.transform.SetParent(canvasObject.transform, false);
                center.transform.SetParent(canvasObject.transform, false);
                cover.transform.SetParent(canvasObject.transform, false);
                right.transform.SetParent(canvasObject.transform, false);

                Vector3 leftPosition = left.transform.localPosition;
                Vector3 rightPosition = right.transform.localPosition;
                Vector3 leftScale = left.transform.localScale;
                Vector3 rightScale = right.transform.localScale;
                float leftAlpha = left.GetComponent<CanvasGroup>().alpha;
                float rightAlpha = right.GetComponent<CanvasGroup>().alpha;
                int swapCount = 0;
                int doneCount = 0;
                CanvasGroup coverGroup = cover.GetComponent<CanvasGroup>();
                float alphaAtSwap = -1f;

                typeof(UITransition).GetMethod("CoverSwap")?.Invoke(
                    null,
                    new object[]
                    {
                        coverGroup,
                        (Action)(() =>
                        {
                            swapCount++;
                            alphaAtSwap = coverGroup.alpha;
                        }),
                        0.08f,
                        0.01f,
                        0.08f,
                        (Action)(() => doneCount++),
                    });

                Assert.That(coverGroup.alpha, Is.EqualTo(0f).Within(0.001f),
                    "构建揭开 Tween 时不应提前把遮罩写成全黑");
                Assert.That(swapCount, Is.EqualTo(0));
                yield return null;
                Assert.That(coverGroup.alpha, Is.LessThan(1f),
                    "覆盖段应逐帧淡入，不能首帧直接全黑");
                Assert.That(swapCount, Is.EqualTo(0));

                float timeout = Time.realtimeSinceStartup + 1f;
                while (doneCount == 0 && Time.realtimeSinceStartup < timeout)
                {
                    yield return null;
                }

                Assert.That(swapCount, Is.EqualTo(1));
                Assert.That(doneCount, Is.EqualTo(1));
                Assert.That(alphaAtSwap, Is.EqualTo(1f).Within(0.001f));
                Assert.That(coverGroup.alpha, Is.EqualTo(0f).Within(0.001f));
                Assert.That(coverGroup.blocksRaycasts, Is.False);
                Assert.That(left.transform.localPosition, Is.EqualTo(leftPosition));
                Assert.That(right.transform.localPosition, Is.EqualTo(rightPosition));
                Assert.That(left.transform.localScale, Is.EqualTo(leftScale));
                Assert.That(right.transform.localScale, Is.EqualTo(rightScale));
                Assert.That(left.GetComponent<CanvasGroup>().alpha, Is.EqualTo(leftAlpha));
                Assert.That(right.GetComponent<CanvasGroup>().alpha, Is.EqualTo(rightAlpha));
            }
            finally
            {
                Time.timeScale = previousTimeScale;
                UnityEngine.Object.Destroy(canvasObject);
            }
        }

        [UnityTest]
        public IEnumerator CoverSwap_WhenCancelled_RestoresTransparentNonBlockingCover()
        {
            var cover = new GameObject(
                "Cover",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            try
            {
                CanvasGroup group = cover.GetComponent<CanvasGroup>();
                int swapCount = 0;
                int doneCount = 0;
                object tween = typeof(UITransition).GetMethod("CoverSwap")?.Invoke(
                    null,
                    new object[]
                    {
                        group,
                        (Action)(() => swapCount++),
                        0.5f,
                        0.03f,
                        0.5f,
                        (Action)(() => doneCount++),
                    });

                yield return null;
                Assert.That(group.blocksRaycasts, Is.True);
                KillTween(tween);
                yield return null;

                Assert.That(swapCount, Is.EqualTo(0));
                Assert.That(doneCount, Is.EqualTo(0));
                Assert.That(group.alpha, Is.EqualTo(0f).Within(0.001f));
                Assert.That(group.interactable, Is.False);
                Assert.That(group.blocksRaycasts, Is.False);
            }
            finally
            {
                UnityEngine.Object.Destroy(cover);
            }
        }

        [UnityTest]
        public IEnumerator FadeSwapStable_RestoresCenterAndInvokesCallbacksOnce()
        {
            var center = new GameObject("Center", typeof(RectTransform), typeof(CanvasGroup));
            try
            {
                RectTransform rect = center.GetComponent<RectTransform>();
                CanvasGroup group = center.GetComponent<CanvasGroup>();
                rect.anchoredPosition = new Vector2(12f, 34f);
                Vector2 restingPosition = rect.anchoredPosition;
                int swapCount = 0;
                int doneCount = 0;

                typeof(UITransition).GetMethod("FadeSwapStable")?.Invoke(
                    null,
                    new object[]
                    {
                        group,
                        (Action)(() => swapCount++),
                        0.02f,
                        0.02f,
                        (Action)(() => doneCount++),
                    });

                float timeout = Time.realtimeSinceStartup + 1f;
                while (doneCount == 0 && Time.realtimeSinceStartup < timeout)
                {
                    yield return null;
                }

                Assert.That(swapCount, Is.EqualTo(1));
                Assert.That(doneCount, Is.EqualTo(1));
                Assert.That(group.alpha, Is.EqualTo(1f).Within(0.001f));
                Assert.That(group.interactable, Is.True);
                Assert.That(group.blocksRaycasts, Is.True);
                Assert.That(rect.anchoredPosition, Is.EqualTo(restingPosition));
            }
            finally
            {
                UnityEngine.Object.Destroy(center);
            }
        }

        [UnityTest]
        public IEnumerator FadeSwapStable_WhenCancelled_RestoresCenterWithoutCompletingCallbacks()
        {
            var center = new GameObject("Center", typeof(RectTransform), typeof(CanvasGroup));
            try
            {
                RectTransform rect = center.GetComponent<RectTransform>();
                CanvasGroup group = center.GetComponent<CanvasGroup>();
                rect.anchoredPosition = new Vector2(18f, 27f);
                Vector2 restingPosition = rect.anchoredPosition;
                int swapCount = 0;
                int doneCount = 0;

                object tween = typeof(UITransition).GetMethod("FadeSwapStable")?.Invoke(
                    null,
                    new object[]
                    {
                        group,
                        (Action)(() => swapCount++),
                        0.5f,
                        0.5f,
                        (Action)(() => doneCount++),
                    });

                yield return null;
                KillTween(tween);
                yield return null;

                Assert.That(swapCount, Is.EqualTo(0));
                Assert.That(doneCount, Is.EqualTo(0));
                Assert.That(group.alpha, Is.EqualTo(1f).Within(0.001f));
                Assert.That(group.interactable, Is.True);
                Assert.That(group.blocksRaycasts, Is.True);
                Assert.That(rect.anchoredPosition, Is.EqualTo(restingPosition));
            }
            finally
            {
                UnityEngine.Object.Destroy(center);
            }
        }

        [UnityTest]
        public IEnumerator StaggerIn_AtZeroTimeScale_RestoresEveryCardTerminalState()
        {
            float previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            var root = new GameObject("Cards", typeof(RectTransform));
            var rects = new List<RectTransform>();
            var positions = new List<Vector2>();
            var scales = new List<Vector3>();

            try
            {
                for (int i = 0; i < 4; i++)
                {
                    var card = new GameObject($"Card{i}", typeof(RectTransform), typeof(CanvasGroup));
                    card.transform.SetParent(root.transform, false);
                    RectTransform rect = card.GetComponent<RectTransform>();
                    rect.anchoredPosition = new Vector2(i * 20f, i * 13f);
                    rect.localScale = Vector3.one * (1f + i * 0.05f);
                    rects.Add(rect);
                    positions.Add(rect.anchoredPosition);
                    scales.Add(rect.localScale);
                }

                typeof(UITransition).GetMethod("StaggerIn")?.Invoke(
                    null,
                    new object[] { rects, 0.02f, 0.01f, 0.03f, 0f });

                float timeout = Time.realtimeSinceStartup + 1f;
                bool completed = false;
                while (!completed && Time.realtimeSinceStartup < timeout)
                {
                    completed = true;
                    for (int i = 0; i < rects.Count; i++)
                    {
                        Assert.That(rects[i].anchoredPosition, Is.EqualTo(positions[i]));
                        Assert.That(rects[i].localScale, Is.EqualTo(scales[i]));
                        if (rects[i].GetComponent<CanvasGroup>().alpha < 0.999f)
                        {
                            completed = false;
                            break;
                        }
                    }

                    yield return null;
                }

                for (int i = 0; i < rects.Count; i++)
                {
                    CanvasGroup group = rects[i].GetComponent<CanvasGroup>();
                    Assert.That(group.alpha, Is.EqualTo(1f).Within(0.001f));
                    Assert.That(group.interactable, Is.True);
                    Assert.That(group.blocksRaycasts, Is.True);
                    Assert.That(rects[i].anchoredPosition, Is.EqualTo(positions[i]));
                    Assert.That(rects[i].localScale, Is.EqualTo(scales[i]));
                }
            }
            finally
            {
                Time.timeScale = previousTimeScale;
                UnityEngine.Object.Destroy(root);
            }
        }

        private static void KillTween(object tween)
        {
            Assert.That(tween, Is.Not.Null);
            Type extensions = tween.GetType().Assembly.GetType("DG.Tweening.TweenExtensions");
            Assert.That(extensions, Is.Not.Null);
            MethodInfo kill = null;
            foreach (MethodInfo candidate in extensions.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                ParameterInfo[] parameters = candidate.GetParameters();
                if (candidate.Name == "Kill"
                    && parameters.Length == 2
                    && parameters[1].ParameterType == typeof(bool))
                {
                    kill = candidate;
                    break;
                }
            }

            Assert.That(kill, Is.Not.Null);
            kill.Invoke(null, new[] { tween, (object)false });
        }
    }
}
