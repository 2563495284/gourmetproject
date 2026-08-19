using System;
using System.Collections.Generic;
using BreakInfinity;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.Save;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using GourmetProject.Runtime;

namespace GourmetProject.Game.UI.Meta
{
    public sealed partial class RecipeReadonlyBookView
    {
        internal static int ResolveRecipeDishDisplayValue(
            DishDef def,
            RecipeBookSlot slot)
        {
            if (def == null)
            {
                return 0;
            }

            BigDouble score = def.Deliciousness
                + (slot != null ? slot.ScoreFlatBonus : 0f);
            BigDouble multiplier = slot != null ? slot.ScoreMultiplier : BigDouble.One;
            return BigNumberSaveData.ToLegacyInt(DishScore.CeilContribution(score, multiplier));
        }

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
            RecipeBookSlot slot,
            RecipeReadonlyDishEntry readonlyEntry = null)
        {
            List<string> skillIds =
                ComposeSkillIds(def, slot?.ExtraSkillIds);
            List<string> flavorIds =
                ComposeFlavorIds(def, slot?.ExtraFlavorIds);
            var skills = new List<FoodInfoEntry>();
            foreach (string skillId in skillIds)
            {
                SkillDef skill = Database.GetSkill(skillId);
                if (skill != null)
                {
                    FoodTipsDataFactory.AppendSkillEntries(skills, skill);
                }
            }

            var flavorNames = new List<string>();
            var flavorDetails = new List<FoodInfoEntry>();
            foreach (string flavorId in flavorIds)
            {
                FlavorDef flavor = Database.GetFlavor(flavorId);
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
                flavorNames,
                readonlyEntry?.SkillsDisabled == true,
                countAs: FoodTipsDataFactory.ResolveIntrinsicCountAs(
                    def,
                    skillIds,
                    flavorIds,
                    Database));
            BigDouble multiplier = slot != null ? slot.ScoreMultiplier : BigDouble.One;
            BigDouble score = def.Deliciousness
                + (slot != null ? slot.ScoreFlatBonus : 0f);
            return new FoodTipsData(
                summary,
                new FoodScoreTipsData(score, multiplier),
                flavorDetails,
                Array.Empty<FoodInfoEntry>(),
                BuildRecipeSpecialTags(
                    skillIds,
                    readonlyEntry?.ExcludedFromScore == true));
        }

        private IReadOnlyList<FoodInfoEntry> BuildRecipeSpecialTags(
            IReadOnlyList<string> skillIds,
            bool excludedFromScore = false)
        {
            if (skillIds == null || Database == null)
            {
                return excludedFromScore
                    ? new[]
                    {
                        new FoodInfoEntry(
                            "不参与计分",
                            "本场 星级评鉴修正：该食物上菜后不会计入美味值。"),
                    }
                    : Array.Empty<FoodInfoEntry>();
            }

            var termIds = new List<string>();
            foreach (string skillId in skillIds)
            {
                SkillDef skill = Database.GetSkill(skillId);
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

            if (excludedFromScore)
            {
                tags.Add(new FoodInfoEntry(
                    "不参与计分",
                    "本场 星级评鉴修正：该食物上菜后不会计入美味值。"));
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

        /// <summary>
        /// 返回用于生成 UI 的原始食谱索引顺序。视觉上先按食物本体排序，
        /// 同一本体内按食物进入食谱的先后顺序展示；绑定到视图的索引仍是原始索引，
        /// 避免删除/装饰品和消耗品目标因排序而错位。
        /// </summary>
        private List<int> BuildDishDisplayOrder(
            IReadOnlyList<RecipeBookSlot> entries)
        {
            var items = new List<RecipeDisplaySortItem>(
                entries?.Count ?? 0);
            if (entries == null || Database == null)
            {
                return new List<int>();
            }

            for (int index = 0; index < entries.Count; index++)
            {
                RecipeBookSlot slot = entries[index];
                DishDef dish = slot == null
                    ? null
                    : Database.GetDish(slot.DishId);
                items.Add(
                    new RecipeDisplaySortItem(
                        index,
                        dish?.SortOrder ?? int.MaxValue,
                        dish?.BaseId ?? slot?.DishId ?? string.Empty));
            }

            items.Sort(CompareRecipeDisplaySortItems);
            var result = new List<int>(items.Count);
            foreach (RecipeDisplaySortItem item in items)
            {
                result.Add(item.OriginalIndex);
            }

            return result;
        }

        private static int CompareRecipeDisplaySortItems(
            RecipeDisplaySortItem left,
            RecipeDisplaySortItem right)
        {
            int compare = left.DishSortOrder.CompareTo(
                right.DishSortOrder);
            if (compare != 0)
            {
                return compare;
            }

            compare = StringComparer.Ordinal.Compare(
                left.BaseId,
                right.BaseId);
            if (compare != 0)
            {
                return compare;
            }

            return left.OriginalIndex.CompareTo(right.OriginalIndex);
        }

        private sealed class RecipeDisplaySortItem
        {
            public RecipeDisplaySortItem(
                int originalIndex,
                int dishSortOrder,
                string baseId)
            {
                OriginalIndex = originalIndex;
                DishSortOrder = dishSortOrder;
                BaseId = baseId;
            }

            public int OriginalIndex { get; }

            public int DishSortOrder { get; }

            public string BaseId { get; }
        }

        private string DishShapeText(string dishId)
        {
            DishDef dish = Database?.GetDish(dishId);
            return dish?.Shape == null
                ? string.Empty
                : $"{dish.Shape.Width}x{dish.Shape.Height}";
        }
    }
}
