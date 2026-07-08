using System;
using GourmetProject.Game.Adapter;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using Luban.SimpleJSON;
using NUnit.Framework;
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
            Assert.IsTrue(dish.AllowRotate);
            CollectionAssert.AreEqual(new[] { "tag_inherent" }, dish.SkillIds);
            Assert.AreEqual("tag_a", dish.FlavorId);
            Assert.AreEqual("sushi", dish.Category);
            Assert.AreEqual(2, dish.CountAs);

            SkillDef skill = db.GetSkill("tag_inherent");
            Assert.NotNull(skill);
            Assert.IsTrue(skill.HasRules);
            Assert.AreEqual(SkillActionType.AddMult, skill.Rules[0].ActionType);
            Assert.AreEqual(1.5f, skill.Rules[0].ActionValue);
            Assert.AreEqual("贡献 ×1.5", skill.Desc);
            // 标题取术语名（termId 非空时），本例 termId 为空 → 空标题。
            Assert.AreEqual(string.Empty, skill.Name);

            FlavorDef flavor = db.GetFlavor("tag_a");
            Assert.NotNull(flavor);
            Assert.AreEqual(GpTagEffectType.AddFlat, flavor.EffectType);
            Assert.AreEqual(5f, flavor.EffectValue);
            Assert.AreEqual("dish", flavor.EffectParam);
            Assert.AreEqual("term_hot", flavor.TermId);

            CellTagDef cellTag = db.GetCellTag("tag_a");
            Assert.NotNull(cellTag);
            Assert.AreEqual(GpTagEffectType.AddMult, cellTag.EffectType);
            Assert.AreEqual(2f, cellTag.EffectValue);

            RecipeDef recipe = db.GetRecipe("recipe_test");
            Assert.NotNull(recipe);
            Assert.AreEqual(15, recipe.RequiredInitScore);
            CollectionAssert.AreEqual(new[] { "dish_sushi_hot", "dish_sushi_cold" }, recipe.FixedDishes);
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
    ""allowRotate"": true,
    ""skills"": ""tag_inherent"",
    ""category"": ""sushi"",
    ""countAs"": 2,
    ""shapeRows"": [""XX"", "".X""]
  }
]";
                case "tbdishvariant":
                    return @"[
  {
    ""id"": ""dish_sushi_hot"",
    ""baseId"": ""base_sushi"",
    ""flavorId"": ""tag_a"",
    ""baseWeight"": 2.5,
    ""price"": 12,
    ""hiddenRange"": { ""min"": 3, ""max"": 9 },
    ""rotation"": 0
  },
  {
    ""id"": ""dish_sushi_cold"",
    ""baseId"": ""base_sushi"",
    ""flavorId"": """",
    ""baseWeight"": 1.5,
    ""price"": 9,
    ""hiddenRange"": { ""min"": 2, ""max"": 6 },
    ""rotation"": 2
  }
]";
                case "tbskill":
                    return @"[
  {
    ""id"": ""tag_inherent"",
    ""termId"": """",
    ""descOverride"": """",
    ""subSkills"": ""ss_test_mult""
  }
]";
                case "tbsubskill":
                    return @"[
  {
    ""id"": ""ss_test_mult"",
    ""trigger"": 0,
    ""condType"": 0,
    ""condScope"": 0,
    ""condUnit"": 0,
    ""condMode"": 0,
    ""condParam"": """",
    ""actionType"": 2,
    ""actionScope"": 0,
    ""actionCount"": 0,
    ""actionValue"": [1.5],
    ""actionParam"": [],
    ""isPassive"": false,
    ""signed"": false,
    ""descTemplate"": ""贡献 ×{0}""
  }
]";
                case "tbflavor":
                    return @"[
  {
    ""id"": ""tag_a"",
    ""name"": ""辣味"",
    ""desc"": ""加 {0} 分"",
    ""effectType"": 1,
    ""effectValue"": [5],
    ""effectParam"": [""dish""],
    ""termId"": ""term_hot""
  }
]";
                case "tbcelltag":
                    return @"[
  {
    ""id"": ""tag_a"",
    ""name"": ""黄金格"",
    ""desc"": ""贡献 ×{0}"",
    ""effectType"": 2,
    ""effectValue"": [2],
    ""effectParam"": [],
    ""termId"": """"
  }
]";
                case "tbrecipe":
                    return @"[
  {
    ""id"": ""recipe_test"",
    ""fixedDishes"": ""dish_sushi_hot|dish_sushi_cold"",
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
