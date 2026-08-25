using System;
using System.Collections;
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
