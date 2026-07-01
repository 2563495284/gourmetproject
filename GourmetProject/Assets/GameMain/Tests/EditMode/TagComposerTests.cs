using System.Collections.Generic;
using GourmetProject.Gameplay.Tags;
using NUnit.Framework;

namespace GourmetProject.Tests
{
    /// <summary>技能追加与风味单槽替换规则测试。</summary>
    public class TagComposerTests
    {
        [Test]
        public void ComposeSkills_KeepsAllSkillsInOrder()
        {
            List<string> result = TagComposer.ComposeSkills(new[] { "fresh", "hearty" });
            CollectionAssert.AreEqual(new[] { "fresh", "hearty" }, result);
        }

        [Test]
        public void ComposeSkills_DropsEmptyIds()
        {
            List<string> result = TagComposer.ComposeSkills(new[] { "fresh", "", null, "spicy" });
            CollectionAssert.AreEqual(new[] { "fresh", "spicy" }, result);
        }

        [Test]
        public void ComposeFlavor_SingleSlot_LastReplacesEarlier()
        {
            string result = TagComposer.ComposeFlavor(new[] { "feast", "golden" });
            Assert.AreEqual("golden", result);
        }

        [Test]
        public void ComposeFlavor_EmptyWhenNone()
        {
            Assert.AreEqual(string.Empty, TagComposer.ComposeFlavor(new[] { "", null }));
        }

        [Test]
        public void ComposeFlavors_Capped_KeepsOnlyLast()
        {
            List<string> result = TagComposer.ComposeFlavors(new[] { "feast", "golden" });
            CollectionAssert.AreEqual(new[] { "golden" }, result);
        }

        [Test]
        public void ComposeFlavors_RemoveCap_KeepsAll()
        {
            List<string> result = TagComposer.ComposeFlavors(new[] { "feast", "golden" }, removeFlavorCap: true);
            CollectionAssert.AreEqual(new[] { "feast", "golden" }, result);
        }
    }
}
