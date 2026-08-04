using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.Balance
{
    public static class BuildArchetypeClassifier
    {
        public static ArchetypeClassification Classify(GameRun run, float dualThreshold = 0.15f)
        {
            return Classify(run?.RecipeEntries, run?.Database, dualThreshold);
        }

        public static ArchetypeClassification Classify(IReadOnlyList<RecipeBookSlot> recipe, GameplayDatabase db, float dualThreshold = 0.15f)
        {
            var values = new Dictionary<BuildArchetype, float>
            {
                [BuildArchetype.SweetTransfer] = 0f,
                [BuildArchetype.Count] = 0f,
                [BuildArchetype.Cake] = 0f,
            };
            if (recipe != null && db != null)
            {
                foreach (RecipeBookSlot slot in recipe)
                {
                    DishDef dish = db.GetDish(slot.DishId);
                    if (dish == null) continue;
                    if (dish.IsCategory("cake")) values[BuildArchetype.Cake] += 2f;
                    foreach (string skillId in dish.SkillIds.Concat(slot.ExtraSkillIds))
                    {
                        SkillDef skill = db.GetSkill(skillId);
                        if (skill == null) continue;
                        foreach (SkillRuleDef rule in skill.Rules) ScoreRule(rule, values);
                    }
                }
            }
            var ordered = values.OrderByDescending(v => v.Value).ThenBy(v => v.Key).ToList();
            var result = new ArchetypeClassification { Primary = ordered[0].Key };
            result.Scores.AddRange(ordered.Select(v => new ArchetypeScore { Archetype = v.Key, Score = v.Value }));
            result.Active.Add(ordered[0].Key);
            float lead = ordered[0].Value <= 0f ? 0f : (ordered[0].Value - ordered[1].Value) / ordered[0].Value;
            if (lead < Math.Max(0f, dualThreshold)) result.Active.Add(ordered[1].Key);
            return result;
        }

        private static void ScoreRule(SkillRuleDef rule, IDictionary<BuildArchetype, float> values)
        {
            if (rule == null) return;
            if (rule.ActionType == SkillActionType.TransferSkills || rule.ActionType == SkillActionType.TriggerSweetTransfer)
                values[BuildArchetype.SweetTransfer] += 5f;
            if (rule.CondType == SkillConditionType.SkillCount)
                values[BuildArchetype.SweetTransfer] += 3f;

            if (rule.ActionType == SkillActionType.AddCountAs)
                values[BuildArchetype.Count] += 5f;
            if (rule.CondType == SkillConditionType.DishCount || rule.CondType == SkillConditionType.RecipeCount ||
                rule.CondType == SkillConditionType.SameDish || rule.CondType == SkillConditionType.SameKindInMeal ||
                rule.CondType == SkillConditionType.SameKindInRun)
                values[BuildArchetype.Count] += 3f;

            if (rule.ActionType == SkillActionType.AddLayer || rule.ActionType == SkillActionType.ConsumeLayer)
                values[BuildArchetype.Cake] += 5f;
            if (rule.CondType == SkillConditionType.LayerCount)
                values[BuildArchetype.Cake] += 3f;
            if (rule.CondType == SkillConditionType.CategoryCount &&
                rule.CondParam.IndexOf("cake", StringComparison.OrdinalIgnoreCase) >= 0)
                values[BuildArchetype.Cake] += 3f;
        }

        public static float DishAffinity(DishDef dish, GameplayDatabase db, IReadOnlyList<BuildArchetype> active)
        {
            if (dish == null || db == null || active == null || active.Count == 0) return 0f;
            var fake = new[] { new RecipeBookSlot(dish.Id) };
            ArchetypeClassification c = Classify(fake, db, 0f);
            float score = 0f;
            foreach (ArchetypeScore s in c.Scores) if (active.Contains(s.Archetype)) score += s.Score;
            return score + dish.Deliciousness * 0.01f - dish.Shape.CellCount * 0.15f;
        }
    }
}
