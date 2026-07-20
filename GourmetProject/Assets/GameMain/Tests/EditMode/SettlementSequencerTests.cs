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
    }
}
