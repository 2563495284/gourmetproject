using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Gameplay.Tests
{
    [TestFixture]
    public class SkillDescComposerTests
    {
        private static SkillRuleDef Rule(
            SkillActionType actionType = SkillActionType.AddFlat,
            SkillConditionType condType = SkillConditionType.None,
            SkillScope condScope = SkillScope.Self,
            SkillScope actionScope = SkillScope.Self,
            CountUnit condUnit = CountUnit.Instances,
            CountMode condMode = CountMode.Per,
            string condParam = "",
            int actionCount = 0,
            float[] actionValues = null,
            string[] actionParams = null)
        {
            return new SkillRuleDef(
                "r", "s", 0, SkillTrigger.OnSettle,
                condType, condScope, condUnit, condMode, condParam,
                actionType, actionScope, actionCount,
                actionValues ?? new[] { 0f },
                actionParams ?? System.Array.Empty<string>());
        }
    }
}
