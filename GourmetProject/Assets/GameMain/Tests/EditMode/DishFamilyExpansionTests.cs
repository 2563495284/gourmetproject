using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Library;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DishFamilyExpansionTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void BuildDatabase_ExpandsEachConfiguredDishFamily()
        {
            int expectedDishCount = 0;
            var referencedBaseIds = new HashSet<string>();
            foreach (cfg.DishVariant family in
                     _tables.TbDishVariant.DataList)
            {
                referencedBaseIds.Add(family.BaseId);
                expectedDishCount++;
                DishDef unflavored = _database.GetDish(family.Id);
                Assert.That(unflavored, Is.Not.Null, family.Id);
                Assert.That(unflavored.BaseId, Is.EqualTo(family.BaseId));
                Assert.That(unflavored.FlavorId, Is.Empty);
                Assert.That(
                    unflavored.BaseWeight,
                    Is.EqualTo(family.BaseWeight));
                Assert.That(unflavored.Price, Is.EqualTo(family.Price));
                Assert.That(
                    unflavored.HiddenMin,
                    Is.EqualTo(family.HiddenRange.Min));
                Assert.That(
                    unflavored.HiddenMax,
                    Is.EqualTo(family.HiddenRange.Max));

                foreach (string flavorId in SplitPipeList(
                             family.FlavorIds))
                {
                    expectedDishCount++;
                    string generatedId = $"{family.Id}{flavorId}";
                    DishDef flavored = _database.GetDish(generatedId);
                    Assert.That(flavored, Is.Not.Null, generatedId);
                    Assert.That(flavored.BaseId, Is.EqualTo(family.BaseId));
                    Assert.That(flavored.FlavorId, Is.EqualTo(flavorId));
                    Assert.That(
                        flavored.BaseWeight,
                        Is.EqualTo(family.FlavoredBaseWeight));
                    Assert.That(
                        flavored.Price,
                        Is.EqualTo(family.FlavoredPrice));
                    Assert.That(
                        flavored.HiddenMin,
                        Is.EqualTo(family.FlavoredHiddenRange.Min));
                    Assert.That(
                        flavored.HiddenMax,
                        Is.EqualTo(family.FlavoredHiddenRange.Max));
                }
            }

            foreach (cfg.DishBase configured in _tables.TbDishBase.DataList)
            {
                if (!referencedBaseIds.Contains(configured.Id))
                {
                    expectedDishCount++;
                    Assert.That(_database.GetDish(configured.Id), Is.Not.Null, configured.Id);
                }
            }

            Assert.That(
                _database.AllDishes.Count,
                Is.EqualTo(expectedDishCount));
        }

        [Test]
        public void SortOrders_AreCarriedIntoGameplayDefinitions()
        {
            foreach (cfg.DishBase configured in
                     _tables.TbDishBase.DataList)
            {
                DishDef dish = _database.GetDish(configured.Id);
                Assert.That(dish, Is.Not.Null, configured.Id);
                Assert.That(
                    dish.SortOrder,
                    Is.EqualTo(configured.SortOrder));
            }

            foreach (cfg.Flavor configured in
                     _tables.TbFlavor.DataList)
            {
                FlavorDef flavor = _database.GetFlavor(configured.Id);
                Assert.That(flavor, Is.Not.Null, configured.Id);
                Assert.That(
                    flavor.SortOrder,
                    Is.EqualTo(configured.SortOrder));
            }
        }

        [Test]
        public void GluttonInitialRecipe_AllCandidatesExistAndRollsFourteenOrFifteenDishes()
        {
            RecipeDef recipe = _database.GetRecipe("recipe_glutton");
            Assert.That(recipe, Is.Not.Null);

            foreach (string dishId in RecipeRoller.CollectPossibleDishIds(recipe))
            {
                Assert.That(
                    _database.GetDish(dishId),
                    Is.Not.Null,
                    $"初始食谱引用了未生成的食物 '{dishId}'。");
            }

            bool rolledFourteen = false;
            bool rolledFifteen = false;
            for (ulong seed = 1; seed <= 128; seed++)
            {
                List<string> rolled =
                    RecipeRoller.Roll(recipe, _database, new Xoshiro256SS(seed));
                rolledFourteen |= rolled.Count == 14;
                rolledFifteen |= rolled.Count == 15;
                Assert.That(
                    rolled.Count == 14 || rolled.Count == 15,
                    Is.True,
                    $"初始食谱数量应为 14 或 15，实际为 {rolled.Count}。");

                for (int arrow = 1; arrow <= 4; arrow++)
                {
                    Assert.That(
                        rolled.Count(id => id == $"arrow_cookie_{arrow}"),
                        Is.EqualTo(3));
                }
            }

            Assert.That(rolledFourteen, Is.True);
            Assert.That(rolledFifteen, Is.True);
        }

        [Test]
        public void RecipeReadonlyBookDisplayOrder_SortsWithoutChangingSourceIndices()
        {
            var run = new GameRun(
                _tables,
                _database,
                _tables.TbCharacter.DataList[0].Id,
                "recipe-readonly-sort-tests");
            var entries = new List<RecipeBookSlot>
            {
                new RecipeBookSlot("cupcaket_bitter"),
                new RecipeBookSlot("jellyt_sour"),
                new RecipeBookSlot("jelly"),
                new RecipeBookSlot("jellyt_sweet"),
                new RecipeBookSlot("cupcake"),
                new RecipeBookSlot("jelly"),
            };
            entries[5].AddFlavor("t_salty");

            var gameObject = new GameObject("RecipeReadonlyBookViewTest");
            try
            {
                var view =
                    gameObject.AddComponent<RecipeReadonlyBookView>();
                typeof(RecipeReadonlyBookView)
                    .GetField(
                        "_run",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(view, run);
                MethodInfo buildOrder =
                    typeof(RecipeReadonlyBookView).GetMethod(
                        "BuildDishDisplayOrder",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.That(buildOrder, Is.Not.Null);
                var order = (List<int>)buildOrder.Invoke(
                    view,
                    new object[] { entries });

                Assert.That(order, Is.EqualTo(new[] { 2, 3, 1, 5, 4, 0 }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void AddRecipeFlavor_UsesTotalLimitAndReplacesOldestFlavor()
        {
            var run = new GameRun(
                _tables,
                _database,
                _tables.TbCharacter.DataList[0].Id,
                "recipe-flavor-limit-tests");
            run.AcquireItem(
                "item_flavor_double_slot",
                fallbackGold: 0,
                fireOnAcquire: false);
            Assert.That(run.FoodFlavorLimit, Is.EqualTo(2));

            Assert.That(run.AddBonusDish("jellyt_sour"), Is.True);
            int dishIndex = run.RecipeEntries.Count - 1;

            Assert.That(run.AddRecipeFlavor(dishIndex, "t_sweet"), Is.True);
            Assert.That(
                run.GetRecipeFlavorIds(dishIndex),
                Is.EqualTo(new[] { "t_sour", "t_sweet" }));

            Assert.That(run.AddRecipeFlavor(dishIndex, "t_rust"), Is.True);
            Assert.That(
                run.GetRecipeFlavorIds(dishIndex),
                Is.EqualTo(new[] { "t_sweet", "t_rust" }));
            Assert.That(
                run.RecipeEntries[dishIndex].DishId,
                Is.EqualTo("jelly"));
        }

        [Test]
        public void RecipeFlavorEffectSource_ReadsAllPersistedUnservedFlavors()
        {
            var snapshot = new ScoreSnapshot(
                new DiningTable(1, 1),
                _database,
                unservedRecipeDishes: new[]
                {
                    new UnservedRecipeDish(
                        0,
                        "jelly",
                        new[] { "t_sour", "t_salty" }),
                });
            var collector = new ScoreEffectCollector();

            new RecipeFlavorEffectSource().CollectEffects(snapshot, collector);

            Assert.That(collector.Entries.Count, Is.EqualTo(2));
            Assert.That(
                collector.Entries.Select(entry => entry.Source.Id),
                Is.EqualTo(new[] { "t_sour", "t_salty" }));
        }

        [Test]
        public void RecipePossibleDishes_CollectsReachableCandidatesOnce()
        {
            var recipe = new RecipeDef(
                "candidate_test",
                new[] { "fixed", "shared" },
                new[]
                {
                    new RecipeGroupDef(
                        "group_a",
                        new[]
                        {
                            new RecipeEntryDef("shared", 1f, 1),
                            new RecipeEntryDef("a", 1f, 1),
                            new RecipeEntryDef("zero_weight", 0f, 1),
                        }),
                    new RecipeGroupDef(
                        "group_b",
                        new[]
                        {
                            new RecipeEntryDef("b", 1f, 1),
                            new RecipeEntryDef("a", 1f, 1),
                        }),
                    new RecipeGroupDef(
                        "group_unreachable",
                        new[]
                        {
                            new RecipeEntryDef("unreachable", 1f, 1),
                        }),
                },
                new[]
                {
                    new RecipeRollPlanDef(
                        "plan_a",
                        1f,
                        new[] { 1, 0, 0 }),
                    new RecipeRollPlanDef(
                        "plan_b",
                        2f,
                        new[] { 0, 2, 0 }),
                    new RecipeRollPlanDef(
                        "zero_weight_plan",
                        0f,
                        new[] { 0, 0, 1 }),
                });

            List<string> candidates =
                RecipeRoller.CollectPossibleDishIds(recipe);

            Assert.That(
                candidates,
                Is.EqualTo(new[] { "fixed", "shared", "a", "b" }));
        }

        private static List<string> SplitPipeList(string value)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(value))
            {
                return result;
            }

            foreach (string item in value.Split('|'))
            {
                string trimmed = item.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    result.Add(trimmed);
                }
            }

            return result;
        }
    }
}
