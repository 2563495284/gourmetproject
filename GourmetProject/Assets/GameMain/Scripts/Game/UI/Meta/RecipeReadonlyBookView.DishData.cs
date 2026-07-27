using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;

namespace GourmetProject.Game.UI.Meta
{
    public sealed partial class RecipeReadonlyBookView
    {
        private bool TryBuildRecipeTarget(
            RecipeEditDishView dish,
            out ActiveTarget target)
        {
            target = default;
            if (_run == null || dish == null)
            {
                return false;
            }

            IReadOnlyList<RecipeBookSlot> entries =
                EntriesForBook(dish.BookIndex);
            if (dish.DishIndex < 0 || dish.DishIndex >= entries.Count)
            {
                return false;
            }

            RecipeBookSlot slot = entries[dish.DishIndex];
            target = new ActiveTarget(
                slot.DishId,
                dish.BookIndex,
                dish.DishIndex,
                cfg.ItemTargetKind.RecipeDish);
            return true;
        }

        private RecipeBookSlot RecipeSlot(ActiveTarget target)
        {
            if (_run == null || target.X != 0)
            {
                return null;
            }

            IReadOnlyList<RecipeBookSlot> entries =
                EntriesForBook(target.X);
            return target.Y >= 0 && target.Y < entries.Count
                ? entries[target.Y]
                : null;
        }

        private FoodTipsData BuildRecipeDishTipsData(
            DishDef def,
            RecipeBookSlot slot)
        {
            List<string> skillIds =
                ComposeSkillIds(def, slot?.ExtraSkillIds);
            List<string> flavorIds =
                ComposeFlavorIds(def, slot?.ExtraFlavorIds);
            var skills = new List<FoodInfoEntry>();
            foreach (string skillId in skillIds)
            {
                SkillDef skill = _run.Database.GetSkill(skillId);
                if (skill != null)
                {
                    skills.Add(new FoodInfoEntry(skill.Name, skill.Desc));
                }
            }

            var flavorNames = new List<string>();
            var flavorDetails = new List<FoodInfoEntry>();
            foreach (string flavorId in flavorIds)
            {
                FlavorDef flavor = _run.Database.GetFlavor(flavorId);
                if (flavor == null)
                {
                    continue;
                }

                flavorNames.Add(flavor.Name);
                flavorDetails.Add(
                    new FoodInfoEntry(flavor.Name, flavor.Desc));
            }

            var summary = new FoodSummaryTipsData(
                def.Name,
                skills,
                flavorNames);
            float multiplier = slot != null ? slot.ScoreMultiplier : 1f;
            float score = def.Deliciousness
                + (slot != null ? slot.ScoreFlatBonus : 0f);
            return new FoodTipsData(
                summary,
                new FoodScoreTipsData(score, multiplier),
                Array.Empty<FoodMaterialTipsEntry>(),
                flavorDetails,
                Array.Empty<FoodInfoEntry>(),
                BuildRecipeSpecialTags(skillIds));
        }

        private IReadOnlyList<FoodInfoEntry> BuildRecipeSpecialTags(
            IReadOnlyList<string> skillIds)
        {
            if (skillIds == null || _run?.Database == null)
            {
                return Array.Empty<FoodInfoEntry>();
            }

            var termIds = new List<string>();
            foreach (string skillId in skillIds)
            {
                SkillDef skill = _run.Database.GetSkill(skillId);
                AddUniqueRange(termIds, skill?.TermIds);
            }

            var tags = new List<FoodInfoEntry>(termIds.Count);
            foreach (string termId in termIds)
            {
                cfg.Term term =
                    GameApp.Config?.Tables?.TbTerm?.GetOrDefault(termId);
                tags.Add(
                    term != null
                        ? new FoodInfoEntry(term.Name, term.Desc)
                        : new FoodInfoEntry(termId, string.Empty));
            }

            return tags;
        }

        private static void AddUniqueRange(
            List<string> list,
            IReadOnlyList<string> values)
        {
            if (list == null || values == null)
            {
                return;
            }

            foreach (string value in values)
            {
                if (!string.IsNullOrEmpty(value) && !list.Contains(value))
                {
                    list.Add(value);
                }
            }
        }

        private static List<string> ComposeSkillIds(
            DishDef def,
            IReadOnlyList<string> extraSkillIds)
        {
            var ids = new List<string>();
            if (def?.SkillIds != null)
            {
                ids.AddRange(def.SkillIds);
            }

            if (extraSkillIds != null)
            {
                ids.AddRange(extraSkillIds);
            }

            return ids;
        }

        private static List<string> ComposeFlavorIds(
            DishDef def,
            IReadOnlyList<string> extraFlavorIds)
        {
            var ids = new List<string>();
            if (def != null && !string.IsNullOrEmpty(def.FlavorId))
            {
                ids.Add(def.FlavorId);
            }

            if (extraFlavorIds != null)
            {
                ids.AddRange(extraFlavorIds);
            }

            return ids;
        }

        private static string DishName(cfg.Tables tables, string dishId)
        {
            cfg.DishVariant variant =
                tables.TbDishVariant.GetOrDefault(dishId);
            if (variant != null)
            {
                cfg.DishBase baseDish =
                    tables.TbDishBase.GetOrDefault(variant.BaseId);
                if (baseDish != null)
                {
                    return baseDish.Name;
                }
            }

            return dishId;
        }

        private string DishShapeText(string dishId)
        {
            DishDef dish = _run?.Database.GetDish(dishId);
            return dish?.Shape == null
                ? string.Empty
                : $"{dish.Shape.Width}x{dish.Shape.Height}";
        }
    }
}
