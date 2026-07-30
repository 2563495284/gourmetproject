using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Library
{
    /// <summary>
    /// 初始菜谱生成（遵循策划文档）：
    /// 1) 固定菜品总会进入菜谱；
    /// 2) 先按权重选择一套数量方案；
    /// 3) 数量方案按菜谱配置的 groupIds 顺序指定各小组的抽取数量；
    /// 4) 每个小组内按权重「放回」随机，受每菜最多次数限制。
    /// 结果为菜谱牌组的菜品 id 列表（固定在前，各小组结果依次在后，顺序稳定可复现）。
    /// </summary>
    public static class RecipeRoller
    {
        /// <summary>
        /// 收集菜谱最终可能包含的菜品。固定菜品与所有可达随机候选按配置首次出现
        /// 顺序合并去重；不会实际消耗随机流。
        /// </summary>
        public static List<string> CollectPossibleDishIds(RecipeDef recipe)
        {
            if (recipe == null)
            {
                throw new ArgumentNullException(nameof(recipe));
            }

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string fixedDish in recipe.FixedDishes)
            {
                AddCandidate(result, seen, fixedDish);
            }

            var reachableGroups = new bool[recipe.Groups.Count];
            foreach (RecipeRollPlanDef plan in recipe.RollPlans)
            {
                if (plan == null || plan.Weight <= 0f)
                {
                    continue;
                }

                int count = Math.Min(
                    reachableGroups.Length,
                    plan.GroupCounts.Count);
                for (int groupIndex = 0; groupIndex < count; groupIndex++)
                {
                    if (plan.GroupCounts[groupIndex] > 0)
                    {
                        reachableGroups[groupIndex] = true;
                    }
                }
            }

            for (int groupIndex = 0;
                 groupIndex < recipe.Groups.Count;
                 groupIndex++)
            {
                if (!reachableGroups[groupIndex])
                {
                    continue;
                }

                RecipeGroupDef group = recipe.Groups[groupIndex];
                if (group == null)
                {
                    continue;
                }

                foreach (RecipeEntryDef entry in group.Pool)
                {
                    if (entry != null && entry.Weight > 0f)
                    {
                        AddCandidate(result, seen, entry.DishId);
                    }
                }
            }

            return result;
        }

        public static List<string> Roll(RecipeDef recipe, GameplayDatabase db, IRandomStream stream)
        {
            if (recipe == null)
            {
                throw new ArgumentNullException(nameof(recipe));
            }

            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            var result = new List<string>();
            foreach (string fixedDish in recipe.FixedDishes)
            {
                result.Add(fixedDish);
            }

            if (recipe.RollPlans.Count == 0)
            {
                return result;
            }

            RecipeRollPlanDef plan = PickPlan(recipe.RollPlans, stream);
            if (plan.GroupCounts.Count != recipe.Groups.Count)
            {
                throw new InvalidOperationException(
                    $"菜谱 '{recipe.Id}' 的数量方案 '{plan.Id}' 配置了 {plan.GroupCounts.Count} 个数量，" +
                    $"但菜谱共有 {recipe.Groups.Count} 个小组。");
            }

            var rolledCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int groupIndex = 0; groupIndex < recipe.Groups.Count; groupIndex++)
            {
                RecipeGroupDef group = recipe.Groups[groupIndex];
                int targetCount = plan.GroupCounts[groupIndex];
                if (targetCount < 0)
                {
                    throw new InvalidOperationException(
                        $"菜谱 '{recipe.Id}' 的数量方案 '{plan.Id}' 在小组 '{group.Id}' 配置了负数 {targetCount}。");
                }

                for (int i = 0; i < targetCount; i++)
                {
                    List<RecipeEntryDef> available = CollectAvailable(group, rolledCounts);
                    if (available.Count == 0)
                    {
                        throw new InvalidOperationException(
                            $"菜谱 '{recipe.Id}' 的小组 '{group.Id}' 需要抽取 {targetCount} 个菜品，" +
                            $"但抽到第 {i + 1} 个时池已因权重或 maxCount 耗尽。");
                    }

                    RecipeEntryDef picked = PickEntry(available, stream);
                    result.Add(picked.DishId);
                    rolledCounts.TryGetValue(picked.DishId, out int count);
                    rolledCounts[picked.DishId] = count + 1;
                }
            }

            return result;
        }

        private static void AddCandidate(
            List<string> result,
            HashSet<string> seen,
            string dishId)
        {
            if (!string.IsNullOrEmpty(dishId) && seen.Add(dishId))
            {
                result.Add(dishId);
            }
        }

        private static RecipeRollPlanDef PickPlan(IReadOnlyList<RecipeRollPlanDef> plans, IRandomStream stream)
        {
            var weights = new List<float>(plans.Count);
            foreach (RecipeRollPlanDef plan in plans)
            {
                weights.Add(Math.Max(0f, plan.Weight));
            }

            return plans[stream.WeightedPickIndex(weights)];
        }

        private static RecipeEntryDef PickEntry(IReadOnlyList<RecipeEntryDef> entries, IRandomStream stream)
        {
            var weights = new List<float>(entries.Count);
            foreach (RecipeEntryDef entry in entries)
            {
                weights.Add(Math.Max(0f, entry.Weight));
            }

            return entries[stream.WeightedPickIndex(weights)];
        }

        private static List<RecipeEntryDef> CollectAvailable(
            RecipeGroupDef group,
            IReadOnlyDictionary<string, int> rolledCounts)
        {
            var available = new List<RecipeEntryDef>();
            foreach (RecipeEntryDef entry in group.Pool)
            {
                if (entry.Weight <= 0f)
                {
                    continue;
                }

                if (entry.MaxCount > 0)
                {
                    rolledCounts.TryGetValue(entry.DishId, out int count);
                    if (count >= entry.MaxCount)
                    {
                        continue;
                    }
                }

                available.Add(entry);
            }

            return available;
        }
    }
}
