#if UNITY_EDITOR
using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Meta;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class RewardDirectRowPresentationTests
    {
        [TestCase(cfg.RewardKind.DishChoice)]
        [TestCase(cfg.RewardKind.PassiveItemChoice)]
        [TestCase(cfg.RewardKind.ActiveItemGrant)]
        [TestCase(cfg.RewardKind.ActiveItemStrengthen)]
        [TestCase(cfg.RewardKind.ActiveItemAdjust)]
        public void ConcreteReward_UsesItsOwnIdentityAndClaimPrompt(cfg.RewardKind kind)
        {
            var choice = new RewardChoice(kind, "reward_id", "奖励名字", "原始效果描述");
            var group = new RewardChoiceGroup("奖励类别", new[] { choice }, 1, description: "类别说明");

            Assert.That(RewardForm.UsesConcreteRewardPresentation(kind), Is.True);
            Assert.That(RewardForm.BuildDirectChoiceBaseDescription(choice, group), Is.EqualTo("领取奖励"));
        }

        [TestCase(cfg.RewardKind.None)]
        [TestCase(cfg.RewardKind.Gold)]
        [TestCase(cfg.RewardKind.FragmentChoice)]
        public void OtherRewardKinds_DoNotUseConcreteRewardPresentation(cfg.RewardKind kind)
        {
            Assert.That(RewardForm.UsesConcreteRewardPresentation(kind), Is.False);
        }

        [Test]
        public void GoldReward_KeepsItsAmountDescription()
        {
            RewardChoice choice = RewardChoice.Gold(40);

            Assert.That(
                RewardForm.BuildDirectChoiceBaseDescription(choice, null),
                Is.EqualTo("领取后获得[gold]金币+40[/gold]。"));
        }

        [Test]
        public void ChoicePrompt_OverridesSemanticColorsWithPureWhite()
        {
            string formatted = SemanticDescriptionFormatter.FormatPureWhite(
                "选择 1 件[term]装饰品[/term]或[term]消耗品[/term]");

            Assert.That(formatted, Does.Not.Contain(SemanticDescriptionFormatter.TermColor));
            Assert.That(formatted, Does.Contain(SemanticDescriptionFormatter.PureWhiteColor));
        }
    }
}
#endif
