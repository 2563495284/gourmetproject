using System;
using System.Collections;
using System.Reflection;
using DG.Tweening;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Runtime.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGameFramework.Runtime;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class DialogFormHandoffPlayModeTests
    {
        [UnityTest]
        public IEnumerator NextFormOpen_ClosesHeldFormAndMarksArrival()
        {
            TestFormRig outgoing = CreateForm("Outgoing", 1);
            TestFormRig incoming = CreateForm("Incoming", 2);
            bool closeRequested = false;

            try
            {
                outgoing.Form.OpenForTest();
                Assert.That(outgoing.Form.HoldForTest(() => closeRequested = true), Is.True);
                Assert.That(closeRequested, Is.False);

                incoming.Form.OpenForTest();
                yield return null;

                Assert.That(closeRequested, Is.True);
                Assert.That(incoming.Form.ArrivedFromHandoff, Is.True);
            }
            finally
            {
                DestroyRig(incoming);
                DestroyRig(outgoing);
            }
        }

        [UnityTest]
        public IEnumerator MissingNextForm_UsesTimeoutFallback()
        {
            TestFormRig outgoing = CreateForm("Outgoing", 1);
            bool closeRequested = false;

            try
            {
                outgoing.Form.OpenForTest();
                Assert.That(
                    outgoing.Form.HoldForTest(() => closeRequested = true, 0.05f),
                    Is.True);

                yield return new WaitForSecondsRealtime(0.1f);
                Assert.That(closeRequested, Is.True);
            }
            finally
            {
                DestroyRig(outgoing);
            }
        }

        [UnityTest]
        public IEnumerator RewardHandoffEdges_FadeWithUnscaledTimeAndDisableAfterward()
        {
            var root = new GameObject("Reward", typeof(RectTransform), typeof(RewardForm));
            var edge = new GameObject("HandoffEdgeShade", typeof(RectTransform), typeof(CanvasGroup));
            edge.transform.SetParent(root.transform, false);
            edge.SetActive(false);
            CanvasGroup edgeGroup = edge.GetComponent<CanvasGroup>();
            RewardForm form = root.GetComponent<RewardForm>();
            float originalTimeScale = Time.timeScale;

            try
            {
                SetField(form, "_handoffEdgeGroup", edgeGroup);
                SetHandoffArrival(form, true);
                InvokePrivate(form, "PrepareHandoffEdgeShade");

                Assert.That(edge.activeSelf, Is.True);
                Assert.That(edgeGroup.alpha, Is.EqualTo(1f));
                Assert.That(edgeGroup.interactable, Is.False);
                Assert.That(edgeGroup.blocksRaycasts, Is.True);

                Time.timeScale = 0f;
                InvokePrivate(form, "PlayOpenTransition");
                yield return new WaitForSecondsRealtime(0.09f);

                Assert.That(edge.activeSelf, Is.True);
                Assert.That(edgeGroup.alpha, Is.GreaterThan(0f).And.LessThan(1f));

                yield return new WaitForSecondsRealtime(0.15f);
                Assert.That(edge.activeSelf, Is.False);
                Assert.That(edgeGroup.alpha, Is.Zero);
                Assert.That(edgeGroup.interactable, Is.False);
                Assert.That(edgeGroup.blocksRaycasts, Is.False);
            }
            finally
            {
                Time.timeScale = originalTimeScale;
                DOTween.Kill(form);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private static void SetHandoffArrival(UGuiForm form, bool value)
        {
            PropertyInfo property = typeof(UGuiForm).GetProperty(
                "IsHandoffArrival",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null);
            property.SetValue(form, value);
        }

        private static void InvokePrivate(object target, string name)
        {
            MethodInfo method = target.GetType().GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(target, null);
        }

        private static TestFormRig CreateForm(string name, int serialId)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(HandoffTestForm), typeof(UIForm));
            UIForm uiForm = root.GetComponent<UIForm>();
            var initialize = typeof(UIForm).GetMethod("OnInit");
            Assert.That(initialize, Is.Not.Null);
            initialize.Invoke(uiForm, new object[] { serialId, name, null, false, true, null });
            return new TestFormRig(root, root.GetComponent<HandoffTestForm>());
        }

        private static void DestroyRig(TestFormRig rig)
        {
            if (rig.Root != null)
            {
                rig.Form.CloseForTest();
                UnityEngine.Object.DestroyImmediate(rig.Root);
            }
        }

        private readonly struct TestFormRig
        {
            public TestFormRig(GameObject root, HandoffTestForm form)
            {
                Root = root;
                Form = form;
            }

            public GameObject Root { get; }

            public HandoffTestForm Form { get; }
        }

        private sealed class HandoffTestForm : UGuiForm
        {
            public bool ArrivedFromHandoff => IsHandoffArrival;

            public bool HoldForTest(Action closeAction, float timeout = 1f)
            {
                return HoldUntilNextFormOpens(closeAction, timeout);
            }

            public void OpenForTest()
            {
                UIForm.OnOpen(null);
            }

            public void CloseForTest()
            {
                UIForm.OnClose(false, null);
            }
        }
    }
}
