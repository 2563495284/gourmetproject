using System.Collections.Generic;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Tags;
using NUnit.Framework;

namespace GourmetProject.Tests
{
    /// <summary>唯一标签 A/B 上限替换规则测试。</summary>
    public class TagComposerTests
    {
        private static GameplayDatabase Db()
        {
            var tags = new List<TagDef>
            {
                GameplayTestFactory.Tag("fresh", TagEffectType.AddFlat, 5f, TagCategory.Inherent),
                GameplayTestFactory.Tag("hearty", TagEffectType.AddFlat, 2f, TagCategory.Inherent),
                GameplayTestFactory.Tag("feastA", TagEffectType.AddMult, 1.1f, TagCategory.UniqueA),
                GameplayTestFactory.Tag("comboA", TagEffectType.AddMult, 1.2f, TagCategory.UniqueA),
                GameplayTestFactory.Tag("goldB", TagEffectType.AddMult, 2f, TagCategory.UniqueB),
                GameplayTestFactory.Tag("silverB", TagEffectType.AddMult, 1.5f, TagCategory.UniqueB),
            };
            return new GameplayDatabase(new List<DishDef>(), tags, new List<RecipeDef>());
        }

        [Test]
        public void Compose_KeepsAllInherentTags()
        {
            GameplayDatabase db = Db();
            List<string> result = TagComposer.Compose(new[] { "fresh", "hearty" }, db);
            CollectionAssert.AreEquivalent(new[] { "fresh", "hearty" }, result);
        }

        [Test]
        public void Compose_SecondUniqueAReplacesFirst()
        {
            GameplayDatabase db = Db();
            List<string> result = TagComposer.Compose(new[] { "fresh", "feastA", "comboA" }, db);

            CollectionAssert.Contains(result, "fresh");
            CollectionAssert.Contains(result, "comboA");
            CollectionAssert.DoesNotContain(result, "feastA");
        }

        [Test]
        public void Compose_AllowsOneUniqueAAndOneUniqueB()
        {
            GameplayDatabase db = Db();
            List<string> result = TagComposer.Compose(new[] { "feastA", "goldB" }, db);
            CollectionAssert.AreEquivalent(new[] { "feastA", "goldB" }, result);
        }

        [Test]
        public void Compose_RemoveUniqueCap_KeepsAllUniques()
        {
            GameplayDatabase db = Db();
            List<string> result = TagComposer.Compose(
                new[] { "feastA", "comboA", "goldB", "silverB" }, db, removeUniqueCap: true);

            CollectionAssert.AreEquivalent(new[] { "feastA", "comboA", "goldB", "silverB" }, result);
        }
    }
}
