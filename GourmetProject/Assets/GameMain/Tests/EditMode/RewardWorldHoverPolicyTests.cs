using GourmetProject.Game.UI.Meta;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class RewardWorldHoverPolicyTests
    {
        [TestCase(false, false, true)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        public void RewardForm_BlocksBattleWorldHoverOnlyWhileRewardContentIsVisible(
            bool peekHidden,
            bool persistentInspectionActive,
            bool expected)
        {
            Assert.That(
                RewardForm.ShouldBlockBattleWorldHover(
                    peekHidden,
                    persistentInspectionActive),
                Is.EqualTo(expected));
        }
    }
}
