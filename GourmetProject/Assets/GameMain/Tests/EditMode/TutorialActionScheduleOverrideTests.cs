using GourmetProject.Game.Tutorial;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class TutorialActionScheduleOverrideTests
    {
        [TestCase("tutorial_first")]
        [TestCase("tutorial_second")]
        public void ModifyTargetScore_TutorialForcedActions_HalvesRequirement(string actionGroupId)
        {
            Assert.That(
                TutorialActionScheduleOverride.ModifyTargetScore(
                    isTutorialRun: true,
                    actionGroupId,
                    targetScore: 100),
                Is.EqualTo(50));
        }

        [Test]
        public void ModifyTargetScore_OddRequirement_RoundsHalfAwayFromZero()
        {
            Assert.That(
                TutorialActionScheduleOverride.ModifyTargetScore(
                    isTutorialRun: true,
                    actionGroupId: "tutorial_first",
                    targetScore: 99),
                Is.EqualTo(50));
        }

        [TestCase(false, "tutorial_first")]
        [TestCase(true, "other_group")]
        public void ModifyTargetScore_OutsideForcedTutorialActions_DoesNotChangeRequirement(
            bool isTutorialRun,
            string actionGroupId)
        {
            Assert.That(
                TutorialActionScheduleOverride.ModifyTargetScore(
                    isTutorialRun,
                    actionGroupId,
                    targetScore: 100),
                Is.EqualTo(100));
        }
    }
}
