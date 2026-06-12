using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Library;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests
{
    /// <summary>菜品库隐藏分加权随机测试：候选过滤、确定性、过滤器、退化均匀。</summary>
    public class DishLibraryTests
    {
        private static DishLibrary BuildLibrary()
        {
            return new DishLibrary(new List<DishDef>
            {
                GameplayTestFactory.Dish("low", new[] { "X" }, hiddenMin: 0, hiddenMax: 10),
                GameplayTestFactory.Dish("mid", new[] { "X" }, hiddenMin: 5, hiddenMax: 25),
                GameplayTestFactory.Dish("high", new[] { "X" }, hiddenMin: 30, hiddenMax: 60),
            });
        }

        [Test]
        public void Candidates_OnlyIncludesDishesCoveringRequiredHidden()
        {
            DishLibrary lib = BuildLibrary();

            List<DishDef> at8 = lib.Candidates(8);
            CollectionAssert.AreEquivalent(new[] { "low", "mid" }, Ids(at8));

            List<DishDef> at40 = lib.Candidates(40);
            CollectionAssert.AreEquivalent(new[] { "high" }, Ids(at40));

            List<DishDef> at100 = lib.Candidates(100);
            Assert.AreEqual(0, at100.Count);
        }

        [Test]
        public void Roll_IsDeterministicForSameSeed()
        {
            var rngA = new RandomService();
            rngA.Init("dish-seed");
            var rngB = new RandomService();
            rngB.Init("dish-seed");

            DishLibrary libA = BuildLibrary();
            DishLibrary libB = BuildLibrary();

            for (int i = 0; i < 50; i++)
            {
                DishDef a = libA.Roll(rngA.Stream("dish"), 8);
                DishDef b = libB.Roll(rngB.Stream("dish"), 8);
                Assert.AreEqual(a.Id, b.Id, $"roll diverged at {i}");
            }
        }

        [Test]
        public void Roll_RespectsFilter()
        {
            var rng = new RandomService();
            rng.Init(123UL);
            DishLibrary lib = BuildLibrary();

            for (int i = 0; i < 30; i++)
            {
                DishDef d = lib.Roll(rng.Stream("dish"), 8, candidate => candidate.Id != "low");
                Assert.AreEqual("mid", d.Id);
            }
        }

        [Test]
        public void Roll_ReturnsNullWhenNoCandidates()
        {
            var rng = new RandomService();
            rng.Init(1UL);
            DishLibrary lib = BuildLibrary();

            Assert.IsNull(lib.Roll(rng.Stream("dish"), 999));
        }

        private static List<string> Ids(List<DishDef> dishes)
        {
            var ids = new List<string>();
            foreach (DishDef d in dishes)
            {
                ids.Add(d.Id);
            }

            return ids;
        }
    }
}
