using System.Reflection;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SettlementSequencerTests
    {
        [Test]
        public void ZeroValueDishSkillStillBuildsSettlementCue()
        {
            var line = new ScoreLine(
                ScorePhase.DishSkills,
                ScoreLineKind.DishFlat,
                new ScoreSource(ScoreSourceType.DishSkill, "pudding_empty_table", "布丁", 1, "pudding"),
                1,
                "pudding",
                null,
                0f,
                0f,
                0f,
                "布丁: 美味度 -0");

            MethodInfo method = typeof(SettlementSequencer).GetMethod(
                "TryBuildCue",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object[] args = { line, null };
            bool built = (bool)method.Invoke(null, args);

            Assert.That(built, Is.True);
            Assert.That(args[1], Is.Not.Null);
        }

        [Test]
        public void ZeroValueWithoutExecutedSkillRemainsFiltered()
        {
            var line = new ScoreLine(
                ScorePhase.Final,
                ScoreLineKind.FinalFlat,
                ScoreSource.FinalModifier("none", "无变化"),
                0,
                string.Empty,
                null,
                0f,
                0f,
                0f,
                "无变化");

            MethodInfo method = typeof(SettlementSequencer).GetMethod(
                "TryBuildCue",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object[] args = { line, null };
            bool built = (bool)method.Invoke(null, args);

            Assert.That(built, Is.False);
            Assert.That(args[1], Is.Null);
        }

        [Test]
        public void SweetTransferBatchesScopeTargetsButKeepsRecipientsSeparate()
        {
            MethodInfo method = typeof(SettlementSequencer).GetMethod(
                "BuildDishSkillBatchKey",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            ScoreLine recipientTwoSelf = SweetTransferLine(2, 2);
            ScoreLine recipientTwoNeighbor = SweetTransferLine(2, 3);
            ScoreLine recipientSixSelf = SweetTransferLine(6, 6);

            string recipientTwoKey = (string)method.Invoke(null, new object[] { recipientTwoSelf });
            string recipientTwoNeighborKey = (string)method.Invoke(null, new object[] { recipientTwoNeighbor });
            string recipientSixKey = (string)method.Invoke(null, new object[] { recipientSixSelf });

            Assert.That(recipientTwoKey, Is.Not.Null.And.Not.Empty);
            Assert.That(recipientTwoNeighborKey, Is.EqualTo(recipientTwoKey));
            Assert.That(recipientSixKey, Is.Not.EqualTo(recipientTwoKey));
        }

        [Test]
        public void DishSkillFeedbackDistinguishesActiveAndPassiveFlatBonus()
        {
            Assert.That(FeedbackKind(DishSkillLine(ScoreLineKind.DishFlat, 1, 1)), Is.EqualTo(SettlementDishFeedbackKind.ActiveFlatBonus));
            Assert.That(FeedbackKind(DishSkillLine(ScoreLineKind.DishFlat, 1, 2)), Is.EqualTo(SettlementDishFeedbackKind.PassiveFlatBonus));
        }

        [Test]
        public void DishSkillFeedbackDistinguishesMultiplierModes()
        {
            Assert.That(FeedbackKind(DishSkillLine(ScoreLineKind.DishMultiplier, 1, 1)), Is.EqualTo(SettlementDishFeedbackKind.ActiveMultiplier));
            Assert.That(FeedbackKind(DishSkillLine(ScoreLineKind.DishMultiplier, 1, 2)), Is.EqualTo(SettlementDishFeedbackKind.PassiveMultiplier));
            Assert.That(FeedbackKind(DishSkillLine(ScoreLineKind.DishMultiplierAdd, 1, 1)), Is.EqualTo(SettlementDishFeedbackKind.ActiveMultiplierAdd));
            Assert.That(FeedbackKind(DishSkillLine(ScoreLineKind.DishMultiplierAdd, 1, 2)), Is.EqualTo(SettlementDishFeedbackKind.PassiveMultiplierAdd));
        }

        [Test]
        public void SweetTransferFeedbackPreservesActiveAndPassiveModes()
        {
            Assert.That(FeedbackKind(SweetTransferLine(2, 2)), Is.EqualTo(SettlementDishFeedbackKind.ActiveFlatBonus));
            Assert.That(FeedbackKind(SweetTransferLine(2, 3)), Is.EqualTo(SettlementDishFeedbackKind.PassiveFlatBonus));
        }

        [Test]
        public void SweetTransferBaseBatchKeepsOriginalOwnerAsVisualSource()
        {
            MethodInfo method = typeof(SettlementSequencer).GetMethod(
                "ResolveSweetTransferSourceDishId",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(SettlementScopeSignal), typeof(string) },
                null);
            Assert.That(method, Is.Not.Null);

            var scope = new SettlementScopeSignal(7, 2, null);
            int sourceDishId = (int)method.Invoke(null, new object[] { scope, "base:skill|A<甜蜜传递>" });

            Assert.That(sourceDishId, Is.EqualTo(7));
        }

        [Test]
        public void CopySkillFeedbackUsesDedicatedKind()
        {
            var line = new ScoreLine(
                ScorePhase.DishSkills,
                ScoreLineKind.CopySkill,
                new ScoreSource(ScoreSourceType.DishSkill, "copy", "复制", 1, "copy_dish"),
                1,
                "copy_dish",
                null,
                1f,
                0f,
                1f,
                "复制技能 +1");

            Assert.That(FeedbackKind(line), Is.EqualTo(SettlementDishFeedbackKind.CopySkillTriggered));
        }

        [Test]
        public void SweetTransferExtraSettlementCueRevealsTransferredCard()
        {
            var line = new ScoreLine(
                ScorePhase.DishSkills,
                ScoreLineKind.ExtraSettlement,
                new ScoreSource(
                    ScoreSourceType.DishSkill,
                    "sk_nougat",
                    "牛轧糖<甜蜜传递>",
                    5,
                    "daifuku"),
                5,
                "daifuku",
                null,
                1f,
                0f,
                1f,
                "牛轧糖<甜蜜传递>: 技能额外触发 +1 次");

            object cue = BuildCue(line);
            PropertyInfo revealProperty = cue.GetType().GetProperty("Reveal");
            Assert.That(revealProperty, Is.Not.Null);

            var reveal = (SettlementRevealSignal)revealProperty.GetValue(cue);
            Assert.That(reveal.DishInstanceId, Is.EqualTo(5));
            Assert.That(reveal.TransferredDelta, Is.EqualTo(1));
            Assert.That(reveal.IsEmpty, Is.False);
        }

        private static ScoreLine SweetTransferLine(int recipientInstanceId, int affectedInstanceId)
        {
            return new ScoreLine(
                ScorePhase.DishSkills,
                ScoreLineKind.DishFlat,
                new ScoreSource(
                    ScoreSourceType.DishSkill,
                    "sk_toffee",
                    "太妃糖<甜蜜传递>",
                    recipientInstanceId,
                    "lollipop"),
                affectedInstanceId,
                "target",
                null,
                3f,
                0f,
                3f,
                "太妃糖<甜蜜传递>: 美味度 +3");
        }

        private static ScoreLine DishSkillLine(ScoreLineKind kind, int sourceInstanceId, int affectedInstanceId)
        {
            return new ScoreLine(
                ScorePhase.DishSkills,
                kind,
                new ScoreSource(
                    ScoreSourceType.DishSkill,
                    "sk_bonus",
                    "周围加成",
                    sourceInstanceId,
                    "source"),
                affectedInstanceId,
                "target",
                null,
                kind == ScoreLineKind.DishMultiplier ? 2f : 3f,
                0f,
                kind == ScoreLineKind.DishMultiplier ? 2f : 3f,
                "周围加成");
        }

        private static SettlementDishFeedbackKind FeedbackKind(ScoreLine line)
        {
            MethodInfo method = typeof(SettlementSequencer).GetMethod(
                "BuildDishFeedbackKind",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return (SettlementDishFeedbackKind)method.Invoke(null, new object[] { line });
        }

        private static object BuildCue(ScoreLine line)
        {
            MethodInfo method = typeof(SettlementSequencer).GetMethod(
                "TryBuildCue",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object[] args = { line, null };
            bool built = (bool)method.Invoke(null, args);
            Assert.That(built, Is.True);
            Assert.That(args[1], Is.Not.Null);
            return args[1];
        }
    }
}
