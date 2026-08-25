#if UNITY_EDITOR
using GourmetProject.Game.Presentation.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SettlementFeverScreenViewTests
    {
        [Test]
        public void PhaseParameters_IncreaseFromColdToTargetToDoubleTarget()
        {
            Assert.That(
                SettlementFeverScreenView.OpacityForPhase(SettlementPacePhase.BelowTarget),
                Is.Zero);
            Assert.That(
                SettlementFeverScreenView.SmokeRateForPhase(SettlementPacePhase.BelowTarget),
                Is.Zero);
            Assert.That(
                SettlementFeverScreenView.EmberRateForPhase(SettlementPacePhase.BelowTarget),
                Is.Zero);

            float targetOpacity = SettlementFeverScreenView.OpacityForPhase(
                SettlementPacePhase.TargetReached);
            float doubleOpacity = SettlementFeverScreenView.OpacityForPhase(
                SettlementPacePhase.DoubleTarget);
            float targetSmoke = SettlementFeverScreenView.SmokeRateForPhase(
                SettlementPacePhase.TargetReached);
            float doubleSmoke = SettlementFeverScreenView.SmokeRateForPhase(
                SettlementPacePhase.DoubleTarget);
            float targetEmbers = SettlementFeverScreenView.EmberRateForPhase(
                SettlementPacePhase.TargetReached);
            float doubleEmbers = SettlementFeverScreenView.EmberRateForPhase(
                SettlementPacePhase.DoubleTarget);

            Assert.That(targetOpacity, Is.EqualTo(0.65f).Within(0.001f));
            Assert.That(doubleOpacity, Is.EqualTo(1f).Within(0.001f));
            Assert.That(doubleOpacity, Is.GreaterThan(targetOpacity));
            Assert.That(doubleSmoke, Is.GreaterThan(targetSmoke));
            Assert.That(doubleEmbers, Is.GreaterThan(targetEmbers));
        }

        [Test]
        public void Bind_StretchesNonInteractiveLayerBelowRewardOverlay()
        {
            var hudObject = new GameObject("HudFrame", typeof(RectTransform));
            var boardObject = new GameObject("BoardArea", typeof(RectTransform));
            var rewardObject = new GameObject("RewardSubflowLayer", typeof(RectTransform));
            var feverObject = new GameObject(
                "SettlementFeverScreen",
                typeof(RectTransform),
                typeof(CanvasGroup));
            boardObject.transform.SetParent(hudObject.transform, false);
            rewardObject.transform.SetParent(hudObject.transform, false);
            feverObject.transform.SetParent(hudObject.transform, false);

            try
            {
                SettlementFeverScreenView view =
                    feverObject.AddComponent<SettlementFeverScreenView>();
                RectTransform hud = hudObject.transform as RectTransform;
                view.Bind(hud);

                RectTransform feverRect = feverObject.transform as RectTransform;
                CanvasGroup group = feverObject.GetComponent<CanvasGroup>();
                Assert.That(feverRect.anchorMin, Is.EqualTo(Vector2.zero));
                Assert.That(feverRect.anchorMax, Is.EqualTo(Vector2.one));
                Assert.That(feverRect.anchoredPosition, Is.EqualTo(Vector2.zero));
                Assert.That(feverRect.sizeDelta, Is.EqualTo(Vector2.zero));
                Assert.That(
                    feverObject.transform.GetSiblingIndex(),
                    Is.LessThan(rewardObject.transform.GetSiblingIndex()));
                Assert.That(group.blocksRaycasts, Is.False);
                Assert.That(group.interactable, Is.False);

                Graphic[] graphics = feverObject.GetComponentsInChildren<Graphic>(true);
                Assert.That(graphics, Has.Length.GreaterThanOrEqualTo(3));
                for (int i = 0; i < graphics.Length; i++)
                {
                    Assert.That(graphics[i].raycastTarget, Is.False);
                }

                Assert.That(view.SmokeParticleLimit, Is.EqualTo(24));
                Assert.That(view.EmberParticleLimit, Is.EqualTo(96));
            }
            finally
            {
                Object.DestroyImmediate(hudObject);
            }
        }

        [Test]
        public void HeatShader_IsAvailableToRuntimeShaderLookup()
        {
            Assert.That(
                Shader.Find("GourmetProject/SettlementFeverScreen"),
                Is.Not.Null);
        }

        [Test]
        public void DirectDoubleTarget_RepeatedBurstAndHide_RemainBoundedAndClear()
        {
            var feverObject = new GameObject(
                "SettlementFeverScreen",
                typeof(RectTransform),
                typeof(CanvasGroup));
            try
            {
                SettlementFeverScreenView view =
                    feverObject.AddComponent<SettlementFeverScreenView>();
                view.Show();
                view.SetPhase(SettlementPacePhase.DoubleTarget, 1.8f);

                // 暂停帧不会推进淡入、热浪或粒子发射。
                view.Advance(0f);
                Assert.That(view.CurrentOpacity, Is.Zero);

                view.Advance(0.22f);
                Assert.That(view.Phase, Is.EqualTo(SettlementPacePhase.DoubleTarget));
                Assert.That(view.CurrentOpacity, Is.EqualTo(1f).Within(0.001f));

                for (int i = 0; i < 12; i++)
                {
                    view.Burst(1f);
                }
                Assert.That(
                    view.ActiveMoteCount,
                    Is.LessThanOrEqualTo(view.SmokeParticleLimit + view.EmberParticleLimit));

                view.Hide();
                Assert.That(feverObject.activeSelf, Is.True);
                view.Advance(0.36f);
                Assert.That(feverObject.activeSelf, Is.False);
                Assert.That(view.CurrentOpacity, Is.Zero);
                Assert.That(view.ActiveMoteCount, Is.Zero);
                Assert.That(view.Phase, Is.EqualTo(SettlementPacePhase.BelowTarget));
            }
            finally
            {
                Object.DestroyImmediate(feverObject);
            }
        }

        [Test]
        public void ParentFormDisabled_ResetsFeverBeforeItIsEnabledAgain()
        {
            var hudObject = new GameObject("HudFrame", typeof(RectTransform));
            var feverObject = new GameObject(
                "SettlementFeverScreen",
                typeof(RectTransform),
                typeof(CanvasGroup));
            feverObject.transform.SetParent(hudObject.transform, false);
            try
            {
                SettlementFeverScreenView view =
                    feverObject.AddComponent<SettlementFeverScreenView>();
                view.Bind(hudObject.transform as RectTransform);
                view.Show();
                view.SetPhase(SettlementPacePhase.TargetReached, 1.4f);
                view.Advance(0.22f);
                view.Burst(1f);
                Assert.That(view.ActiveMoteCount, Is.GreaterThan(0));

                hudObject.SetActive(false);
                // EditMode 不派发父层级失活导致的 MonoBehaviour.OnDisable，
                // 直接验证 OnDisable 所调用的确定性复位路径。
                view.ResetForPresentationDisable();
                hudObject.SetActive(true);

                Assert.That(view.CurrentOpacity, Is.Zero);
                Assert.That(view.ActiveMoteCount, Is.Zero);
                Assert.That(view.Phase, Is.EqualTo(SettlementPacePhase.BelowTarget));
            }
            finally
            {
                Object.DestroyImmediate(hudObject);
            }
        }
    }
}
#endif
