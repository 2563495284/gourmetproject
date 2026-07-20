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
    }
}
