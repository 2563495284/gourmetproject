using System;
using System.Collections.Generic;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class FoodTipsDataFactoryTests
    {
        [Test]
        public void AppendSkillEntries_TwoSubSkills_CreatesTwoCardEntries()
        {
            SkillDef skill = Skill(
                "combined_skill",
                "合并描述",
                new[] { "子技能一", "子技能二" });
            var entries = new List<FoodInfoEntry>();

            FoodTipsDataFactory.AppendSkillEntries(entries, skill, "来源技能");

            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].Title, Is.EqualTo("来源技能"));
            Assert.That(entries[0].Desc, Is.EqualTo("子技能一"));
            Assert.That(entries[1].Title, Is.EqualTo("来源技能"));
            Assert.That(entries[1].Desc, Is.EqualTo("子技能二"));
        }

        [Test]
        public void AppendSkillEntries_NoSubSkillDescriptions_UsesAggregateDescription()
        {
            SkillDef skill = Skill("legacy_skill", "旧技能描述", null);
            var entries = new List<FoodInfoEntry>();

            FoodTipsDataFactory.AppendSkillEntries(entries, skill);

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Desc, Is.EqualTo("旧技能描述"));
        }

        private static SkillDef Skill(
            string id,
            string desc,
            IReadOnlyList<string> ruleDescs)
        {
            return new SkillDef(
                id,
                string.Empty,
                desc,
                Array.Empty<string>(),
                ruleDescs: ruleDescs);
        }
    }
}
