using System.Collections.Generic;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Library;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.Gameplay
{
    /// <summary>
    /// 把 Luban 配置（cfg.*）适配成纯玩法层定义（GourmetProject.Gameplay.Model.*）。
    /// 适配只在 Game 层做，使玩法逻辑与配置实现解耦、保持可独立单测。
    /// 枚举因数值一一对应，直接按 int 转换。
    /// </summary>
    public static class GameplayContentBuilder
    {
        public static GameplayDatabase BuildDatabase(cfg.Tables tables)
        {
            var dishes = new List<DishDef>(tables.TbDish.DataList.Count);
            foreach (cfg.Dish d in tables.TbDish.DataList)
            {
                dishes.Add(ToDishDef(d));
            }

            var tags = new List<TagDef>(tables.TbTag.DataList.Count);
            foreach (cfg.Tag t in tables.TbTag.DataList)
            {
                tags.Add(ToTagDef(t));
            }

            var recipes = new List<RecipeDef>(tables.TbRecipe.DataList.Count);
            foreach (cfg.Recipe r in tables.TbRecipe.DataList)
            {
                recipes.Add(ToRecipeDef(r));
            }

            return new GameplayDatabase(dishes, tags, recipes);
        }

        public static DishLibrary BuildDishLibrary(GameplayDatabase db)
        {
            return new DishLibrary(new List<DishDef>(db.AllDishes));
        }

        private static DishDef ToDishDef(cfg.Dish d)
        {
            return new DishDef(
                d.Id,
                d.Name,
                d.Deliciousness,
                DishShape.FromRows(d.ShapeRows),
                d.HiddenMin,
                d.HiddenMax,
                d.BaseWeight,
                new List<string>(d.InherentTags),
                d.Icon,
                d.AllowRotate,
                d.MaxRollCount);
        }

        private static TagDef ToTagDef(cfg.Tag t)
        {
            return new TagDef(
                t.Id,
                t.Name,
                t.Desc,
                (TagCategory)(int)t.Category,
                (TagEffectType)(int)t.EffectType,
                t.EffectValue,
                t.EffectParam,
                t.TermId);
        }

        private static RecipeDef ToRecipeDef(cfg.Recipe r)
        {
            var pool = new List<RecipeEntryDef>(r.Pool.Count);
            foreach (cfg.RecipeEntry e in r.Pool)
            {
                pool.Add(new RecipeEntryDef(e.DishId, e.Weight, e.MaxCount));
            }

            return new RecipeDef(r.Id, new List<string>(r.FixedDishes), pool, r.RequiredInitScore);
        }
    }
}
