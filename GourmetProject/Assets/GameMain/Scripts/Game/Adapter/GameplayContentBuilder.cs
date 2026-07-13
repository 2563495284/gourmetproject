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

            Dictionary<string, cfg.SubSkill> subSkillIndex = BuildSubSkillIndex(tables);
            var skills = new List<SkillDef>(tables.TbSkill.DataList.Count);
            foreach (cfg.Skill s in tables.TbSkill.DataList)
            {
                skills.Add(ToSkillDef(s, subSkillIndex, tables));
            }

            var flavors = new List<FlavorDef>(tables.TbFlavor.DataList.Count);
            foreach (cfg.Flavor f in tables.TbFlavor.DataList)
            {
                flavors.Add(ToFlavorDef(f));
            }

            var materials = new List<MaterialDef>(tables.TbMaterial.DataList.Count);
            foreach (cfg.Material c in tables.TbMaterial.DataList)
            {
                materials.Add(ToMaterialDef(c));
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

            return new GameplayDatabase(dishes, skills, flavors, materials, recipes, fragments, cakeLayerBuffs);
        }

        private static List<TableFragmentDef> BuildFragments(cfg.Tables tables)
        {
            // 先按 fragmentId 归集格标签（分开配置的 TbFragmentMaterial）。
            var materialsByFragment = new Dictionary<string, List<CellMaterial>>();
            foreach (cfg.FragmentMaterial ct in tables.TbFragmentMaterial.DataList)
            {
                if (!materialsByFragment.TryGetValue(ct.FragmentId, out List<CellMaterial> list))
                {
                    list = new List<CellMaterial>();
                    materialsByFragment[ct.FragmentId] = list;
                }

                list.Add(new CellMaterial(new GridPos(ct.X, ct.Y), ct.MaterialId));
            }

            var fragments = new List<TableFragmentDef>(tables.TbTableFragment.DataList.Count);
            foreach (cfg.TableFragment f in tables.TbTableFragment.DataList)
            {
                materialsByFragment.TryGetValue(f.Id, out List<CellMaterial> materials);
                fragments.Add(new TableFragmentDef(
                    f.Id,
                    new List<string>(f.ShapeRows),
                    f.HiddenRange.Min,
                    f.HiddenRange.Max,
                    f.BaseWeight,
                    f.Price,
                    materials ?? new List<CellMaterial>()));
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
                b.AllowRotate,
                b.Id,
                v.Price,
                (int)v.Rotation,
                b.Category,
                b.CountAs);
        }

        /// <summary>把 TbSubSkill（合并后=具体子技能）建成 id→行 的索引，供技能正向引用。</summary>
        private static Dictionary<string, cfg.SubSkill> BuildSubSkillIndex(cfg.Tables tables)
        {
            var index = new Dictionary<string, cfg.SubSkill>(tables.TbSubSkill.DataList.Count);
            foreach (cfg.SubSkill ss in tables.TbSubSkill.DataList)
            {
                index[ss.Id] = ss;
            }

            return index;
        }

        /// <summary>
        /// 把「技能(TbSkill) 正向引用的有序子技能列表」合成为运行时 SkillRuleDef（order=列表下标），
        /// 描述取 descOverride，否则由各子技能占位符模板按序回填拼接。
        /// 标题 Name 取术语名（termId 非空时），否则空串。
        /// </summary>
        private static SkillDef ToSkillDef(
            cfg.Skill s, Dictionary<string, cfg.SubSkill> subSkillIndex, cfg.Tables tables)
        {
            List<string> subIds = SplitPipeList(s.SubSkills);
            var rules = new List<SkillRuleDef>(subIds.Count);
            var parts = new List<string>(subIds.Count);
            for (int order = 0; order < subIds.Count; order++)
            {
                string subId = subIds[order];
                if (!subSkillIndex.TryGetValue(subId, out cfg.SubSkill ss))
                {
                    throw new System.InvalidOperationException(
                        $"技能 '{s.Id}' 引用了不存在的子技能 '{subId}'。");
                }

                var rule = new SkillRuleDef(
                    $"{s.Id}#{order}",
                    s.Id,
                    order,
                    (SkillTrigger)(int)ss.Trigger,
                    (SkillConditionType)(int)ss.CondType,
                    (SkillScope)(int)ss.CondScope,
                    (CountUnit)(int)ss.CondUnit,
                    (CountMode)(int)ss.CondMode,
                    ss.CondParam,
                    (SkillActionType)(int)ss.ActionType,
                    (SkillScope)(int)ss.ActionScope,
                    ss.ActionCount,
                    ss.ActionValue,
                    ss.ActionParam);

                rules.Add(rule);
                parts.Add(SkillDescComposer.ComposeComponent(ss.DescTemplate, rule, ss.Signed));
            }

            string desc = SkillDescComposer.ComposeSkill(parts);

            string name = string.Empty;
            if (!string.IsNullOrEmpty(s.TermId))
            {
                cfg.Term term = tables.TbTerm.GetOrDefault(s.TermId);
                if (term != null)
                {
                    name = term.Name;
                }
            }

            return new SkillDef(s.Id, name, desc, s.TermId, rules, parts);
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

        private static MaterialDef ToMaterialDef(cfg.Material c)
        {
            var effectType = (MaterialEffectType)(int)c.EffectType;
            return new MaterialDef(
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
