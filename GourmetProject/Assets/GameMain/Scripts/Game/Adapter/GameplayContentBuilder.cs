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
            var recipeGroups = new Dictionary<string, cfg.RecipeGroup>(System.StringComparer.Ordinal);
            foreach (cfg.RecipeGroup group in tables.TbRecipeGroup.DataList)
            {
                recipeGroups[group.Id] = group;
            }

            foreach (cfg.Recipe r in tables.TbRecipe.DataList)
            {
                recipes.Add(ToRecipeDef(r, recipeGroups));
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
            var fragments = new List<TableFragmentDef>(tables.TbTableFragment.DataList.Count);
            foreach (cfg.TableFragment f in tables.TbTableFragment.DataList)
            {
                var shapeRows = new List<string>(f.ShapeRows);
                fragments.Add(new TableFragmentDef(
                    f.Id,
                    shapeRows,
                    f.HiddenRange.Min,
                    f.HiddenRange.Max,
                    f.BaseWeight,
                    new List<string>(f.MaterialIds),
                    new List<CellMaterial>()));
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
        /// 描述由各子技能占位符模板按序回填拼接。
        /// 专有名词 termId 挂在各子技能上（可 | 分隔多值），此处按首次出现顺序聚合去重到 <see cref="SkillDef.TermIds"/>；
        /// 技能不再有单独的术语标题，Name 置空。
        /// </summary>
        private static SkillDef ToSkillDef(
            cfg.Skill s, Dictionary<string, cfg.SubSkill> subSkillIndex, cfg.Tables tables)
        {
            List<string> subIds = SplitPipeList(s.SubSkills);
            var rules = new List<SkillRuleDef>(subIds.Count);
            var parts = new List<string>(subIds.Count);
            var termIds = new List<string>();
            for (int order = 0; order < subIds.Count; order++)
            {
                string subId = subIds[order];
                if (!subSkillIndex.TryGetValue(subId, out cfg.SubSkill ss))
                {
                    throw new System.InvalidOperationException(
                        $"技能 '{s.Id}' 引用了不存在的子技能 '{subId}'。");
                }

                List<string> ruleTermIds = SplitPipeList(ss.TermId);
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
                    ss.ActionParam,
                    ruleTermIds);

                rules.Add(rule);
                parts.Add(SkillDescComposer.ComposeComponent(ss.DescTemplate, rule, ss.Signed));

                foreach (string termId in ruleTermIds)
                {
                    if (!termIds.Contains(termId))
                    {
                        termIds.Add(termId);
                    }
                }
            }

            string desc = SkillDescComposer.ComposeSkill(parts);

            return new SkillDef(s.Id, string.Empty, desc, termIds, rules, parts);
        }

        private static FlavorDef ToFlavorDef(cfg.Flavor f)
        {
            var effectType = (FlavorEffectType)(int)f.EffectType;
            return new FlavorDef(
                f.Id,
                f.Name,
                EffectDescFormatter.Format(f.Desc, f.EffectValue, signed: !effectType.IsMultiplier()),
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
                EffectDescFormatter.Format(c.Desc, c.EffectValue, signed: !effectType.IsMultiplier()),
                effectType,
                c.EffectValue,
                c.EffectParam,
                c.TermId);
        }

        private static RecipeDef ToRecipeDef(
            cfg.Recipe r,
            IReadOnlyDictionary<string, cfg.RecipeGroup> configuredGroups)
        {
            List<string> groupIds = SplitPipeList(r.GroupIds);
            var groups = new List<RecipeGroupDef>(groupIds.Count);
            foreach (string groupId in groupIds)
            {
                if (!configuredGroups.TryGetValue(groupId, out cfg.RecipeGroup configuredGroup))
                {
                    throw new System.InvalidOperationException(
                        $"菜谱 '{r.Id}' 引用了不存在的随机小组 '{groupId}'。");
                }

                var pool = new List<RecipeEntryDef>(configuredGroup.Pool.Count);
                foreach (cfg.RecipeEntry entry in configuredGroup.Pool)
                {
                    pool.Add(new RecipeEntryDef(entry.DishId, entry.Weight, entry.MaxCount));
                }

                groups.Add(new RecipeGroupDef(configuredGroup.Id, pool));
            }

            var plans = new List<RecipeRollPlanDef>(r.RollPlans.Count);
            foreach (cfg.RecipeRollPlan configuredPlan in r.RollPlans)
            {
                plans.Add(new RecipeRollPlanDef(
                    $"{r.Id}_plan_{plans.Count + 1}",
                    configuredPlan.Weight,
                    ParseCommaIntList(r.Id, configuredPlan.GroupCounts)));
            }

            return new RecipeDef(r.Id, SplitPipeList(r.FixedDishes), groups, plans);
        }

        private static List<int> ParseCommaIntList(string recipeId, string value)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return result;
            }

            foreach (string item in value.Split(','))
            {
                string trimmed = item.Trim();
                if (!int.TryParse(trimmed, out int count))
                {
                    throw new System.InvalidOperationException(
                        $"菜谱 '{recipeId}' 的数量方案包含非法数量 '{trimmed}'。");
                }

                result.Add(count);
            }

            return result;
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
