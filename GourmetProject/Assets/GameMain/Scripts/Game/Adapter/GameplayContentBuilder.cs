using System.Collections.Generic;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Library;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.Adapter
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

            Dictionary<string, List<SkillRuleDef>> rulesBySkill = BuildSkillRules(tables);
            var skills = new List<SkillDef>(tables.TbSkill.DataList.Count);
            foreach (cfg.Skill s in tables.TbSkill.DataList)
            {
                rulesBySkill.TryGetValue(s.Id, out List<SkillRuleDef> rules);
                skills.Add(ToSkillDef(s, rules));
            }

            var flavors = new List<FlavorDef>(tables.TbFlavor.DataList.Count);
            foreach (cfg.Flavor f in tables.TbFlavor.DataList)
            {
                flavors.Add(ToFlavorDef(f));
            }

            var cellTags = new List<CellTagDef>(tables.TbCellTag.DataList.Count);
            foreach (cfg.CellTag c in tables.TbCellTag.DataList)
            {
                cellTags.Add(ToCellTagDef(c));
            }

            var recipes = new List<RecipeDef>(tables.TbRecipe.DataList.Count);
            foreach (cfg.Recipe r in tables.TbRecipe.DataList)
            {
                recipes.Add(ToRecipeDef(r));
            }

            var fragments = BuildFragments(tables);

            var cakeLayerBuffs = new List<CakeLayerBuffDef>(tables.TbCakeLayerBuff.DataList.Count);
            foreach (cfg.CakeLayerBuff c in tables.TbCakeLayerBuff.DataList)
            {
                cakeLayerBuffs.Add(new CakeLayerBuffDef(
                    c.Id,
                    c.Order,
                    c.Threshold,
                    c.Category,
                    (SkillActionType)(int)c.EffectType,
                    c.ValuePerLayer,
                    c.Desc));
            }

            return new GameplayDatabase(dishes, skills, flavors, cellTags, recipes, fragments, cakeLayerBuffs);
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
                    f.HiddenRange.Min,
                    f.HiddenRange.Max,
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
            return new DishDef(
                v.Id,
                b.Name,
                b.Deliciousness,
                DishShape.FromRows(b.ShapeRows),
                v.HiddenRange.Min,
                v.HiddenRange.Max,
                v.BaseWeight,
                SplitPipeList(b.Skills),
                v.FlavorId,
                b.Icon,
                b.AllowRotate,
                b.Id,
                v.Price,
                (int)v.Rotation,
                b.Category,
                b.CountAs);
        }

        private static Dictionary<string, List<SkillRuleDef>> BuildSkillRules(cfg.Tables tables)
        {
            var bySkill = new Dictionary<string, List<SkillRuleDef>>();
            foreach (cfg.SkillRule r in tables.TbSkillRule.DataList)
            {
                if (!bySkill.TryGetValue(r.SkillId, out List<SkillRuleDef> list))
                {
                    list = new List<SkillRuleDef>();
                    bySkill[r.SkillId] = list;
                }

                list.Add(new SkillRuleDef(
                    r.Id,
                    r.SkillId,
                    r.Order,
                    (SkillTrigger)(int)r.Trigger,
                    (SkillConditionType)(int)r.CondType,
                    (SkillScope)(int)r.CondScope,
                    (CountUnit)(int)r.CondUnit,
                    (CountMode)(int)r.CondMode,
                    (CompareOp)(int)r.CondCompare,
                    r.CondThreshold,
                    r.CondParam,
                    (SkillActionType)(int)r.ActionType,
                    (SkillScope)(int)r.ActionScope,
                    r.ActionCount,
                    r.ActionValue,
                    r.ActionParam));
            }

            foreach (List<SkillRuleDef> list in bySkill.Values)
            {
                list.Sort((a, b) => a.Order.CompareTo(b.Order));
            }

            return bySkill;
        }

        private static SkillDef ToSkillDef(cfg.Skill s, List<SkillRuleDef> rules)
        {
            return new SkillDef(
                s.Id,
                s.Name,
                s.Desc,
                s.TermId,
                rules);
        }

        private static FlavorDef ToFlavorDef(cfg.Flavor f)
        {
            var effectType = (TagEffectType)(int)f.EffectType;
            return new FlavorDef(
                f.Id,
                f.Name,
                TagDescFormatter.Format(f.Desc, f.EffectValue, signed: !effectType.IsMultiplier()),
                effectType,
                f.EffectValue,
                f.EffectParam,
                f.TermId);
        }

        private static CellTagDef ToCellTagDef(cfg.CellTag c)
        {
            var effectType = (TagEffectType)(int)c.EffectType;
            return new CellTagDef(
                c.Id,
                c.Name,
                TagDescFormatter.Format(c.Desc, c.EffectValue, signed: !effectType.IsMultiplier()),
                effectType,
                c.EffectValue,
                c.EffectParam,
                c.TermId);
        }

        private static RecipeDef ToRecipeDef(cfg.Recipe r)
        {
            var pool = new List<RecipeEntryDef>(r.Pool.Count);
            foreach (cfg.RecipeEntry e in r.Pool)
            {
                pool.Add(new RecipeEntryDef(e.DishId, e.Weight, e.MaxCount, e.InitScore));
            }

            return new RecipeDef(r.Id, SplitPipeList(r.FixedDishes), pool, r.RequiredInitScore);
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
