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
            var bases = new Dictionary<string, cfg.DishBase>(tables.TbDishBase.DataList.Count);
            foreach (cfg.DishBase b in tables.TbDishBase.DataList)
            {
                bases[b.Id] = b;
            }

            var dishes = new List<DishDef>(tables.TbDishVariant.DataList.Count);
            foreach (cfg.DishVariant v in tables.TbDishVariant.DataList)
            {
                if (!bases.TryGetValue(v.BaseId, out cfg.DishBase b))
                {
                    throw new System.InvalidOperationException(
                        $"菜品变体 '{v.Id}' 引用了不存在的本体 baseId '{v.BaseId}'。");
                }

                dishes.Add(ToDishDef(v, b));
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

            var fragments = BuildFragments(tables);

            return new GameplayDatabase(dishes, tags, recipes, fragments);
        }

        private static List<StomachFragmentDef> BuildFragments(cfg.Tables tables)
        {
            // 先按 fragmentId 归集格标签（分开配置的 TbFragmentCellTag）。
            var cellTagsByFragment = new Dictionary<string, List<CellTag>>();
            foreach (cfg.FragmentCellTag ct in tables.TbFragmentCellTag.DataList)
            {
                if (!cellTagsByFragment.TryGetValue(ct.FragmentId, out List<CellTag> list))
                {
                    list = new List<CellTag>();
                    cellTagsByFragment[ct.FragmentId] = list;
                }

                list.Add(new CellTag(new GridPos(ct.X, ct.Y), ct.TagId));
            }

            var fragments = new List<StomachFragmentDef>(tables.TbStomachFragment.DataList.Count);
            foreach (cfg.StomachFragment f in tables.TbStomachFragment.DataList)
            {
                cellTagsByFragment.TryGetValue(f.Id, out List<CellTag> cellTags);
                fragments.Add(new StomachFragmentDef(
                    f.Id,
                    new List<string>(f.ShapeRows),
                    f.HiddenMin,
                    f.HiddenMax,
                    f.BaseWeight,
                    f.Price,
                    cellTags ?? new List<CellTag>()));
            }

            return fragments;
        }

        public static DishLibrary BuildDishLibrary(GameplayDatabase db)
        {
            return new DishLibrary(new List<DishDef>(db.AllDishes));
        }

        private static DishDef ToDishDef(cfg.DishVariant v, cfg.DishBase b)
        {
            // 种子标签 = 本体固有标签 + 变体唯一标签 A/B（空串视为槽位为空）。
            var seedTags = new List<string>(v.InherentTags);
            if (!string.IsNullOrEmpty(v.ATagId))
            {
                seedTags.Add(v.ATagId);
            }

            if (!string.IsNullOrEmpty(v.BTagId))
            {
                seedTags.Add(v.BTagId);
            }

            return new DishDef(
                v.Id,
                b.Name,
                b.Deliciousness,
                DishShape.FromRows(b.ShapeRows),
                v.HiddenMin,
                v.HiddenMax,
                v.BaseWeight,
                seedTags,
                b.Icon,
                b.AllowRotate,
                v.MaxRollCount,
                b.Id,
                v.Price);
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
