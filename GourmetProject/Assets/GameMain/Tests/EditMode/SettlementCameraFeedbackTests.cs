using GourmetProject.Game.Presentation.Battle;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SettlementCameraFeedbackTests
    {
        [Test]
        public void BelowTarget_DisablesCameraFollowAndShake()
        {
            Assert.That(
                SettlementSequencer.CameraFollowFactorFor(SettlementPacePhase.BelowTarget),
                Is.Zero);
            Assert.That(
                SettlementSequencer.CameraTransitionShakeStrengthFor(SettlementPacePhase.BelowTarget),
                Is.Zero);
            Assert.That(
                SettlementSequencer.CameraFinaleShakeStrengthFor(SettlementPacePhase.BelowTarget),
                Is.Zero);
            Assert.That(
                SettlementSequencer.CameraHomeScaleFor(SettlementPacePhase.BelowTarget),
                Is.EqualTo(1f));
            Assert.That(
                SettlementSequencer.CameraFocusScaleFor(SettlementPacePhase.BelowTarget),
                Is.EqualTo(1f));
            Assert.That(
                SettlementSequencer.CameraTransitionPunchScaleFor(SettlementPacePhase.BelowTarget),
                Is.EqualTo(1f));
            Assert.That(
                SettlementSequencer.CameraFinaleScaleFor(SettlementPacePhase.BelowTarget),
                Is.EqualTo(1f));

            foreach (SettlementImpactTier tier in System.Enum.GetValues(typeof(SettlementImpactTier)))
            {
                Assert.That(
                    SettlementSequencer.CameraImpactStrengthFor(
                        SettlementPacePhase.BelowTarget,
                        tier),
                    Is.Zero);
            }
        }

        [Test]
        public void TargetReached_UsesPreviousBelowTargetStrengths()
        {
            Assert.That(
                SettlementSequencer.CameraFollowFactorFor(SettlementPacePhase.TargetReached),
                Is.EqualTo(0.10f));
            Assert.That(
                SettlementSequencer.CameraTransitionShakeStrengthFor(SettlementPacePhase.TargetReached),
                Is.Zero);
            Assert.That(
                SettlementSequencer.CameraFinaleShakeStrengthFor(SettlementPacePhase.TargetReached),
                Is.Zero);
            Assert.That(
                SettlementSequencer.CameraHomeScaleFor(SettlementPacePhase.TargetReached),
                Is.EqualTo(1f));
            Assert.That(
                SettlementSequencer.CameraFocusScaleFor(SettlementPacePhase.TargetReached),
                Is.EqualTo(0.985f));
            Assert.That(
                SettlementSequencer.CameraTransitionPunchScaleFor(SettlementPacePhase.TargetReached),
                Is.EqualTo(1f));
            Assert.That(
                SettlementSequencer.CameraFinaleScaleFor(SettlementPacePhase.TargetReached),
                Is.EqualTo(0.992f));
            AssertImpactStrengths(
                SettlementPacePhase.TargetReached,
                0f,
                0f,
                0.025f,
                0.05f,
                0.06f);
        }

        [Test]
        public void DoubleTarget_UsesPreviousTargetReachedStrengths()
        {
            Assert.That(
                SettlementSequencer.CameraFollowFactorFor(SettlementPacePhase.DoubleTarget),
                Is.EqualTo(0.14f));
            Assert.That(
                SettlementSequencer.CameraTransitionShakeStrengthFor(SettlementPacePhase.DoubleTarget),
                Is.EqualTo(0.085f));
            Assert.That(
                SettlementSequencer.CameraFinaleShakeStrengthFor(SettlementPacePhase.DoubleTarget),
                Is.EqualTo(0.08f));
            Assert.That(
                SettlementSequencer.CameraHomeScaleFor(SettlementPacePhase.DoubleTarget),
                Is.EqualTo(0.985f));
            Assert.That(
                SettlementSequencer.CameraFocusScaleFor(SettlementPacePhase.DoubleTarget),
                Is.EqualTo(0.965f));
            Assert.That(
                SettlementSequencer.CameraTransitionPunchScaleFor(SettlementPacePhase.DoubleTarget),
                Is.EqualTo(0.978f));
            Assert.That(
                SettlementSequencer.CameraFinaleScaleFor(SettlementPacePhase.DoubleTarget),
                Is.EqualTo(0.97f));
            AssertImpactStrengths(
                SettlementPacePhase.DoubleTarget,
                0f,
                0.025f,
                0.055f,
                0.075f,
                0.09f);
        }

        private static void AssertImpactStrengths(
            SettlementPacePhase phase,
            float baseStrength,
            float normalStrength,
            float strongStrength,
            float chainStrength,
            float finaleStrength)
        {
            Assert.That(
                SettlementSequencer.CameraImpactStrengthFor(phase, SettlementImpactTier.Base),
                Is.EqualTo(baseStrength));
            Assert.That(
                SettlementSequencer.CameraImpactStrengthFor(phase, SettlementImpactTier.Normal),
                Is.EqualTo(normalStrength));
            Assert.That(
                SettlementSequencer.CameraImpactStrengthFor(phase, SettlementImpactTier.Strong),
                Is.EqualTo(strongStrength));
            Assert.That(
                SettlementSequencer.CameraImpactStrengthFor(phase, SettlementImpactTier.Chain),
                Is.EqualTo(chainStrength));
            Assert.That(
                SettlementSequencer.CameraImpactStrengthFor(phase, SettlementImpactTier.Finale),
                Is.EqualTo(finaleStrength));
        }
    }
}
