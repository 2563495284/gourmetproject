using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Library;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests
{
    /// <summary>初始菜谱生成测试：固定菜品、达到要求初始分、最多次数限制、确定性。</summary>
    public class RecipeRollerTests
    {
        private static GameplayDatabase BuildDb()
        {
            var dishes = new List<DishDef>
            {
                GameplayTestFactory.Dish("rice", new[] { "X" }, deliciousness: 5),
                GameplayTestFactory.Dish("egg", new[] { "XX" }, deliciousness: 8),
                GameplayTestFactory.Dish("rare", new[] { "XX", "XX" }, deliciousness: 20),
            };

            return new GameplayDatabase(
                dishes,
                new List<SkillDef>(),
                new List<FlavorDef>(),
                new List<CellTagDef>(),
                new List<RecipeDef>());
        }

        [Test]
        public void Roll_IncludesFixedDishesAndReachesRequiredScore()
        {
            GameplayDatabase db = BuildDb();
            var pool = new[]
            {
                new RecipeEntryDef("egg", 100f, 0, 8),
                new RecipeEntryDef("rice", 100f, 0, 5),
            };
            var recipe = new RecipeDef(
                "r",
                new[] { "rice" },
                pool,
                requiredInitScore: 30);

            var rng = new RandomService();
            rng.Init("recipe-seed");
            List<string> result = RecipeRoller.Roll(recipe, db, rng.Stream("recipe"));

            Assert.AreEqual("rice", result[0], "fixed dish should be first");

            int rolledScore = 0;
            for (int i = 1; i < result.Count; i++)
            {
                rolledScore += GetEntryScore(pool, result[i]);
            }

            Assert.GreaterOrEqual(rolledScore, 30, "rolled dishes should reach required init score");
        }

        [Test]
        public void Roll_UsesRecipeEntryInitScoreInsteadOfDishStats()
        {
            var dishes = new List<DishDef>
            {
                GameplayTestFactory.Dish("starter", new[] { "X" }, deliciousness: 1),
            };
            var db = new GameplayDatabase(
                dishes,
                new List<SkillDef>(),
                new List<FlavorDef>(),
                new List<CellTagDef>(),
                new List<RecipeDef>());
            var recipe = new RecipeDef(
                "r",
                new string[0],
                new[]
                {
                    new RecipeEntryDef("starter", 100f, 0, 10),
                },
                requiredInitScore: 10);

            var rng = new RandomService();
            rng.Init("init-score");
            List<string> result = RecipeRoller.Roll(recipe, db, rng.Stream("recipe"));

            Assert.AreEqual(1, result.Count, "init score should stop the roll even when deliciousness is lower");
        }

        [Test]
        public void Roll_RespectsMaxCount()
        {
            GameplayDatabase db = BuildDb();
            var recipe = new RecipeDef(
                "r",
                new string[0],
                new[]
                {
                    new RecipeEntryDef("rare", 100f, 1, 20), // 最多 1 次
                    new RecipeEntryDef("rice", 100f, 0, 5),
                },
                requiredInitScore: 100);

            var rng = new RandomService();
            rng.Init(42UL);
            List<string> result = RecipeRoller.Roll(recipe, db, rng.Stream("recipe"));

            int rareCount = 0;
            foreach (string id in result)
            {
                if (id == "rare")
                {
                    rareCount++;
                }
            }

            Assert.LessOrEqual(rareCount, 1, "rare dish must not exceed its maxCount");
        }

        [Test]
        public void Roll_IsDeterministicForSameSeed()
        {
            GameplayDatabase db = BuildDb();
            var recipe = new RecipeDef(
                "r",
                new[] { "rice" },
                new[]
                {
                    new RecipeEntryDef("egg", 80f, 0, 8),
                    new RecipeEntryDef("rare", 40f, 0, 20),
                },
                requiredInitScore: 50);

            var rngA = new RandomService();
            rngA.Init("same");
            var rngB = new RandomService();
            rngB.Init("same");

            List<string> a = RecipeRoller.Roll(recipe, db, rngA.Stream("recipe"));
            List<string> b = RecipeRoller.Roll(recipe, db, rngB.Stream("recipe"));

            CollectionAssert.AreEqual(a, b);
        }

        private static int GetEntryScore(IReadOnlyList<RecipeEntryDef> entries, string dishId)
        {
            foreach (RecipeEntryDef entry in entries)
            {
                if (entry.DishId == dishId)
                {
                    return entry.InitScore;
                }
            }

            return 0;
        }
    }
}
