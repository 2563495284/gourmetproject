using System;
using GourmetProject.Game.Adapter;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using Luban.SimpleJSON;
using NUnit.Framework;
using GpTagCategory = GourmetProject.Gameplay.Model.TagCategory;
using GpTagEffectType = GourmetProject.Gameplay.Model.TagEffectType;

namespace GourmetProject.Tests
{
    public class GameplayContentBuilderTests
    {
        [Test]
        public void BuildDatabase_MapsDishRecipeTagAndFragmentFields()
        {
            GameplayDatabase db = GameplayContentBuilder.BuildDatabase(BuildTables());

            DishDef dish = db.GetDish("dish_sushi_hot");
            Assert.NotNull(dish);
            Assert.AreEqual("base_sushi", dish.BaseId);
            Assert.AreEqual("寿司", dish.Name);
            Assert.AreEqual(7, dish.Deliciousness);
            Assert.AreEqual(2, dish.Shape.Width);
            Assert.AreEqual(2, dish.Shape.Height);
            Assert.AreEqual(3, dish.Shape.CellCount);
            Assert.AreEqual(3, dish.HiddenMin);
            Assert.AreEqual(9, dish.HiddenMax);
            Assert.AreEqual(2.5f, dish.BaseWeight);
            Assert.AreEqual(12, dish.Price);
            Assert.AreEqual("Icons/Sushi", dish.Icon);
            Assert.IsTrue(dish.AllowRotate);
            CollectionAssert.AreEqual(new[] { "tag_inherent", "tag_a", "tag_b" }, dish.InherentTags);

            TagDef tag = db.GetTag("tag_a");
            Assert.NotNull(tag);
            Assert.AreEqual(GpTagCategory.UniqueA, tag.Category);
            Assert.AreEqual(GpTagEffectType.AddFlat, tag.EffectType);
            Assert.AreEqual(5f, tag.EffectValue);
            Assert.AreEqual("dish", tag.EffectParam);
            Assert.AreEqual("term_hot", tag.TermId);

            RecipeDef recipe = db.GetRecipe("recipe_test");
            Assert.NotNull(recipe);
            Assert.AreEqual(15, recipe.RequiredInitScore);
            CollectionAssert.AreEqual(new[] { "dish_sushi_hot" }, recipe.FixedDishes);
            Assert.AreEqual(1, recipe.Pool.Count);
            Assert.AreEqual("dish_sushi_hot", recipe.Pool[0].DishId);
            Assert.AreEqual(3f, recipe.Pool[0].Weight);
            Assert.AreEqual(2, recipe.Pool[0].MaxCount);
            Assert.AreEqual(6, recipe.Pool[0].InitScore);

            StomachFragmentDef fragment = db.GetFragment("frag_start");
            Assert.NotNull(fragment);
            Assert.AreEqual(1, fragment.HiddenMin);
            Assert.AreEqual(4, fragment.HiddenMax);
            Assert.AreEqual(1.5f, fragment.BaseWeight);
            Assert.AreEqual(8, fragment.Price);
            CollectionAssert.AreEqual(new[] { "XX", "X." }, fragment.ShapeRows);
            Assert.AreEqual(1, fragment.CellTags.Count);
            Assert.AreEqual(new GridPos(0, 1), fragment.CellTags[0].Pos);
            Assert.AreEqual("tag_a", fragment.CellTags[0].TagId);
        }

        [Test]
        public void BuildDatabase_ThrowsWhenDishVariantReferencesMissingBase()
        {
            cfg.Tables tables = BuildTables("tbdishbase", "[]");

            Assert.Throws<InvalidOperationException>(() => GameplayContentBuilder.BuildDatabase(tables));
        }

        private static cfg.Tables BuildTables(string overrideName = null, string overrideJson = null)
        {
            return new cfg.Tables(name =>
            {
                if (name == overrideName)
                {
                    return JSON.Parse(overrideJson);
                }

                return JSON.Parse(JsonFor(name));
            });
        }

        private static string JsonFor(string name)
        {
            switch (name)
            {
                case "tbdishbase":
                    return @"[
  {
    ""id"": ""base_sushi"",
    ""name"": ""寿司"",
    ""deliciousness"": 7,
    ""icon"": ""Icons/Sushi"",
    ""allowRotate"": true,
    ""shapeRows"": [""XX"", "".X""]
  }
]";
                case "tbdishvariant":
                    return @"[
  {
    ""id"": ""dish_sushi_hot"",
    ""baseId"": ""base_sushi"",
    ""aTagId"": ""tag_a"",
    ""bTagId"": ""tag_b"",
    ""baseWeight"": 2.5,
    ""price"": 12,
    ""hiddenRange"": { ""min"": 3, ""max"": 9 },
    ""inherentTags"": [""tag_inherent""]
  }
]";
                case "tbtag":
                    return @"[
  {
    ""id"": ""tag_a"",
    ""name"": ""辣味"",
    ""desc"": ""加 {0} 分"",
    ""category"": 1,
    ""effectType"": 1,
    ""effectValue"": [5],
    ""effectParam"": [""dish""],
    ""termId"": ""term_hot""
  }
]";
                case "tbrecipe":
                    return @"[
  {
    ""id"": ""recipe_test"",
    ""fixedDishes"": [""dish_sushi_hot""],
    ""requiredInitScore"": 15,
    ""pool"": [
      { ""dishId"": ""dish_sushi_hot"", ""weight"": 3, ""maxCount"": 2, ""initScore"": 6 }
    ]
  }
]";
                case "tbstomachfragment":
                    return @"[
  {
    ""id"": ""frag_start"",
    ""baseWeight"": 1.5,
    ""price"": 8,
    ""hiddenRange"": { ""min"": 1, ""max"": 4 },
    ""shapeRows"": [""XX"", ""X.""]
  }
]";
                case "tbfragmentcelltag":
                    return @"[
  {
    ""id"": ""frag_start_0_1"",
    ""fragmentId"": ""frag_start"",
    ""x"": 0,
    ""y"": 1,
    ""tagId"": ""tag_a""
  }
]";
                default:
                    return "[]";
            }
        }
    }
}
