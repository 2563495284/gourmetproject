using System;
using System.Collections;
using System.Reflection;
using DG.Tweening;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Runtime.Pooling;
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

    public sealed class BattlePoolingLifecyclePlayModeTests
    {
        [UnityTest]
        public IEnumerator DropDust_NaturalStopReturnsToPoolAndReplayKeepsParametersStable()
        {
            var root = new GameObject("DropDustPlayModePool");
            var prefabObject = new GameObject("DropDustPrefab");
            ParticleSystem particles = prefabObject.AddComponent<ParticleSystem>();
            DishDropDustView prefab = prefabObject.AddComponent<DishDropDustView>();
            SetField(prefab, "_particles", particles);

            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.04f;
            main.startLifetime = 0.02f;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            prefabObject.SetActive(false);

            var pool = new GameObjectPool(
                prefabObject,
                root.transform,
                prewarm: 2,
                maxInactive: 8,
                onGet: go => go.GetComponent<DishDropDustView>()?.PrepareForReuse(),
                onRelease: go => go.GetComponent<DishDropDustView>()?.ResetForPool());
            try
            {
                DishDropDustView first = pool.Get<DishDropDustView>();
                first.Play(Vector3.zero, new Vector2(2f, 3f), 4, Vector2.zero, pool.Release);
                float firstSize = first.GetComponent<ParticleSystem>().main.startSizeMultiplier;
                yield return new WaitForSeconds(0.15f);
                Assert.That(pool.CountInactive, Is.EqualTo(2));

                DishDropDustView second = pool.Get<DishDropDustView>();
                second.Play(Vector3.zero, new Vector2(2f, 3f), 4, Vector2.zero, pool.Release);
                Assert.That(
                    second.GetComponent<ParticleSystem>().main.startSizeMultiplier,
                    Is.EqualTo(firstSize).Within(0.0001f));
                yield return new WaitForSeconds(0.15f);

                Assert.That(pool.CountAll, Is.EqualTo(2));
                Assert.That(pool.CountInactive, Is.EqualTo(2));
            }
            finally
            {
                pool.Clear();
                UnityEngine.Object.DestroyImmediate(prefabObject);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator SweetTransfer_ReplayAcrossFramesReusesInstanceAndChildren()
        {
            var root = new GameObject("SweetTransferPlayModePool");
            var prefabObject = new GameObject(
                "SweetTransferPrefab",
                typeof(SpriteRenderer),
                typeof(SweetTransferParticleView));
            SweetTransferParticleView prefab = prefabObject.GetComponent<SweetTransferParticleView>();
            prefabObject.SetActive(false);
            var pool = new GameObjectPool(
                prefabObject,
                root.transform,
                prewarm: 1,
                maxInactive: 2,
                onGet: go => go.GetComponent<SweetTransferParticleView>()?.PrepareForReuse(),
                onRelease: go => go.GetComponent<SweetTransferParticleView>()?.WarmupForPool());
            try
            {
                SweetTransferParticleView first = SweetTransferParticleView.Begin(
                    prefab,
                    root.transform,
                    Vector3.zero,
                    Vector3.one,
                    0.05f,
                    pool: pool);
                int rendererCount = root.GetComponentsInChildren<SpriteRenderer>(true).Length;
                yield return new WaitForSeconds(0.08f);
                pool.Release(first);

                SweetTransferParticleView second = SweetTransferParticleView.Begin(
                    prefab,
                    root.transform,
                    Vector3.zero,
                    Vector3.one,
                    0.05f,
                    pool: pool);
                yield return new WaitForSeconds(0.08f);

                Assert.That(second, Is.SameAs(first));
                Assert.That(rendererCount, Is.EqualTo(40));
                Assert.That(
                    root.GetComponentsInChildren<SpriteRenderer>(true).Length,
                    Is.EqualTo(rendererCount));
                pool.Release(second);
                Assert.That(pool.CountAll, Is.EqualTo(1));
            }
            finally
            {
                pool.Clear();
                UnityEngine.Object.DestroyImmediate(prefabObject);
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
    }
}
