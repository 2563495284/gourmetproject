using System.Collections.Generic;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SkillDescComposerTests
    {
        [Test]
        public void ComposeComponent_FillsActionScopeValueAndCategory()
        {
            SkillRuleDef rule = Rule(
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                SkillActionType.CopySkill,
                SkillScope.Self,
                3f,
                "cat:cake");

            string desc = SkillDescComposer.ComposeComponent(
                "{ascope}获得 {0} 种不同的{cat}技能",
                rule,
                signed: false);

            Assert.That(desc, Is.EqualTo("获得 3 种不同的蛋糕技能"));
        }

        [Test]
        public void ComposeComponent_FillsConditionScopeUnitAndSignedValue()
        {
            SkillRuleDef rule = Rule(
                SkillConditionType.DishCount,
                SkillScope.LeftAndSelf,
                CountUnit.Instances,
                SkillActionType.AddLayer,
                SkillScope.CakeBuff,
                2f);

            string desc = SkillDescComposer.ComposeComponent(
                "{cscope}每有 1 {unit}食物\n欢乐蛋糕 {0} 层",
                rule,
                signed: true);

            Assert.That(desc, Is.EqualTo("左侧及自身每有 1 个食物\n欢乐蛋糕 +2 层"));
        }

        [Test]
        public void ComposeComponent_SupportsDollarTokensAndSignedTierValues()
        {
            SkillRuleDef rule = new SkillRuleDef(
                "rule",
                "skill",
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.CategoryCount,
                SkillScope.All,
                CountUnit.Instances,
                CountMode.Reach,
                "cake;tiers:3|5|8",
                SkillActionType.AddLayer,
                SkillScope.CakeBuff,
                0,
                new[] { 0f },
                new[] { "cat:cake", "tiervals:15|30|50" });

            string desc = SkillDescComposer.ComposeComponent(
                "${cat}数量达 ${tiers} 时\n欢乐蛋糕 ${tiervals} 层",
                rule,
                signed: true);

            Assert.That(desc, Is.EqualTo("蛋糕数量达 3/5/8 时\n欢乐蛋糕 +15/+30/+50 层"));
        }

        private static SkillRuleDef Rule(
            SkillConditionType condType,
            SkillScope condScope,
            CountUnit condUnit,
            SkillActionType actionType,
            SkillScope actionScope,
            float actionValue,
            params string[] actionParams)
        {
            return new SkillRuleDef(
                "rule",
                "skill",
                0,
                SkillTrigger.OnSettle,
                condType,
                condScope,
                condUnit,
                CountMode.Per,
                string.Empty,
                actionType,
                actionScope,
                0,
                new[] { actionValue },
                actionParams ?? new string[0]);
        }
    }
}
