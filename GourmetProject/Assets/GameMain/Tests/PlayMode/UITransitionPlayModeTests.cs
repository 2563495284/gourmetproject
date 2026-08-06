using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Battle.Pages;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Meta;
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

                for (int i = 0; i < rects.Count; i++)
                {
                    CanvasGroup group = rects[i].GetComponent<CanvasGroup>();
                    Assert.That(group.interactable, Is.True,
                        "淡入期间不能禁用 CanvasGroup，否则子按钮会切换到 DisabledColor");
                    Assert.That(group.blocksRaycasts, Is.False,
                        "淡入期间仍应屏蔽指针点击");
                }

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

        [UnityTest]
        public IEnumerator RewardForm_WhenRowsAreRebuilt_DoesNotFadeListToBlack()
        {
            var formObject = new GameObject("RewardForm", typeof(RectTransform), typeof(RewardForm));
            try
            {
                var contentObject = new GameObject("RewardListContent", typeof(RectTransform), typeof(CanvasGroup));
                contentObject.transform.SetParent(formObject.transform, false);
                CanvasGroup contentGroup = contentObject.GetComponent<CanvasGroup>();

                var rowObject = new GameObject("RewardRow", typeof(RectTransform), typeof(RewardChoiceRowView));
                rowObject.transform.SetParent(contentObject.transform, false);
                RewardChoiceRowView row = rowObject.GetComponent<RewardChoiceRowView>();
                RewardForm form = formObject.GetComponent<RewardForm>();

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(RewardForm).GetField("_rewardListContent", flags)?.SetValue(
                    form,
                    contentObject.GetComponent<RectTransform>());
                typeof(RewardForm).GetField("_hasBuiltRewardRows", flags)?.SetValue(form, true);

                var rows = typeof(RewardForm).GetField("_spawnedRows", flags)?.GetValue(form)
                    as List<RewardChoiceRowView>;
                Assert.That(rows, Is.Not.Null);
                rows.Add(row);

                typeof(RewardForm).GetMethod("RebuildRewardRows", flags)?.Invoke(form, null);
                yield return null;

                Assert.That(contentGroup.alpha, Is.EqualTo(1f).Within(0.001f),
                    "领取奖励后的列表重建不能把整组列表淡出到黑色背景");
            }
            finally
            {
                UnityEngine.Object.Destroy(formObject);
            }
        }

        [UnityTest]
        public IEnumerator RewardSubflowLayer_EveryHandoffFrameHasVisibleInputOwner()
        {
            var root = new GameObject("BattleRoot", typeof(RectTransform));
            var center = new GameObject("ShopSource", typeof(RectTransform));
            var layerObject = new GameObject(
                "RewardSubflowLayer",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup),
                typeof(RewardSubflowLayer));
            var child = new GameObject("RewardChild", typeof(RectTransform), typeof(CanvasGroup));

            try
            {
                center.transform.SetParent(root.transform, false);
                layerObject.transform.SetParent(root.transform, false);
                child.transform.SetParent(layerObject.transform, false);
                int sourceInstanceId = center.GetInstanceID();
                var layer = layerObject.GetComponent<RewardSubflowLayer>();
                CanvasGroup childGroup = child.GetComponent<CanvasGroup>();

                layer.Initialize();
                Assert.That(center.activeInHierarchy, Is.True);

                layer.Prepare();
                yield return null;
                Assert.That(layer.IsVisible, Is.True);
                Assert.That(center.activeInHierarchy, Is.True);

                layer.Show(childGroup);
                yield return null;
                Assert.That(child.activeInHierarchy, Is.True);
                Assert.That(layer.IsVisible, Is.True);

                // 返回交接帧先恢复来源（它一直保活），覆盖层继续拦截；下一帧才撤覆盖层也不会空窗。
                layer.Hide(childGroup);
                yield return null;
                Assert.That(layer.IsVisible, Is.True);
                Assert.That(center.activeInHierarchy, Is.True);
                Assert.That(center.GetInstanceID(), Is.EqualTo(sourceInstanceId));

                layer.HideImmediate();
                yield return null;
                Assert.That(layer.IsVisible, Is.False);
                Assert.That(center.activeInHierarchy, Is.True);
                Assert.That(center.GetInstanceID(), Is.EqualTo(sourceInstanceId));
            }
            finally
            {
                UnityEngine.Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator RewardForm_SuspendIsIdempotentAndKeepsSameInstanceRegistered()
        {
            var formObject = new GameObject(
                "RewardForm",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(RewardForm));
            var continueObject = new GameObject(
                "ContinueButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            try
            {
                continueObject.transform.SetParent(formObject.transform, false);
                RewardForm form = formObject.GetComponent<RewardForm>();
                CanvasGroup group = formObject.GetComponent<CanvasGroup>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(RewardForm).GetField("_transitionGroup", flags)?.SetValue(form, group);
                typeof(RewardForm).GetField("_continueButton", flags)?.SetValue(
                    form,
                    continueObject.GetComponent<Button>());
                int instanceId = formObject.GetInstanceID();

                MethodInfo suspend = typeof(RewardForm).GetMethod(
                    "SuspendForRewardSubflow",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(suspend, Is.Not.Null);
                suspend.Invoke(form, null);
                suspend.Invoke(form, null);
                yield return null;

                Assert.That(formObject.activeInHierarchy, Is.True);
                Assert.That(formObject.GetInstanceID(), Is.EqualTo(instanceId));
                Assert.That(group.alpha, Is.EqualTo(0f).Within(0.001f));
                Assert.That(group.interactable, Is.False);
                Assert.That(group.blocksRaycasts, Is.False);

                MethodInfo prepareOpen = typeof(RewardForm).GetMethod(
                    "PrepareOpenTransition",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(prepareOpen, Is.Not.Null);
                prepareOpen.Invoke(form, null);
                yield return null;

                Assert.That(formObject.GetInstanceID(), Is.EqualTo(instanceId));
                Assert.That(group.interactable, Is.True,
                    "UIForm 复用同一实例重新打开时必须清除领奖子流程留下的禁用状态。");
                Assert.That(group.blocksRaycasts, Is.False,
                    "打开动画完成前仍由 blocksRaycasts 控制输入时机。");
            }
            finally
            {
                UnityEngine.Object.Destroy(formObject);
            }
        }

        [UnityTest]
        public IEnumerator RewardForm_HidesReturnButtonWhileInspectingResultPages()
        {
            var formObject = new GameObject("RewardForm", typeof(RectTransform), typeof(RewardForm));
            var returnObject = new GameObject(
                "ReturnRewardButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            try
            {
                returnObject.transform.SetParent(formObject.transform, false);
                RewardForm form = formObject.GetComponent<RewardForm>();
                Button returnButton = returnObject.GetComponent<Button>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(RewardForm).GetField("_peekReturnButton", flags)?.SetValue(form, returnButton);
                typeof(RewardForm).GetField("_allowResultPeek", flags)?.SetValue(form, true);
                typeof(RewardForm).GetField("_peekHidden", flags)?.SetValue(form, true);
                typeof(RewardForm).GetMethod("ConfigurePeekButtons", flags)?.Invoke(form, null);
                MethodInfo setInspection = typeof(RewardForm).GetMethod(
                    "SetResultPeekInspectionActive",
                    flags);
                Assert.That(setInspection, Is.Not.Null);

                Assert.That(returnObject.activeSelf, Is.True);
                setInspection.Invoke(form, new object[] { true });
                yield return null;
                Assert.That(returnObject.activeSelf, Is.False,
                    "查看餐桌/菜谱期间不应把‘返回奖励’覆盖在查看页上。");

                setInspection.Invoke(form, new object[] { false });
                yield return null;
                Assert.That(returnObject.activeSelf, Is.True);
            }
            finally
            {
                UnityEngine.Object.Destroy(formObject);
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
