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
    /// 2) 其余从随机池按权重「放回」随机，受每菜最多次数限制；
    /// 3) 每随机一道，累加已随机菜品的初始分（= 美味度），达到要求初始分即停止。
    /// 结果为菜谱牌组的菜品 id 列表（固定在前，随机在后，顺序稳定可复现）。
    /// </summary>
    public static class RecipeRoller
    {
        private const int SafetyIterationCap = 1000;

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

            var rolledCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            int rolledScore = 0;
            int iterations = 0;

            while (rolledScore < recipe.RequiredInitScore && iterations++ < SafetyIterationCap)
            {
                List<RecipeEntryDef> available = CollectAvailable(recipe, rolledCounts);
                if (available.Count == 0)
                {
                    break; // 池已耗尽（全部达到上限），无法继续随机。
                }

                var weights = new List<float>(available.Count);
                foreach (RecipeEntryDef entry in available)
                {
                    weights.Add(Math.Max(0f, entry.Weight));
                }

                int pickIndex = stream.WeightedPickIndex(weights);
                RecipeEntryDef picked = available[pickIndex];

                result.Add(picked.DishId);
                rolledCounts.TryGetValue(picked.DishId, out int c);
                rolledCounts[picked.DishId] = c + 1;

                DishDef dish = db.GetDish(picked.DishId);
                rolledScore += dish?.Deliciousness ?? 0;
            }

            return result;
        }

        private static List<RecipeEntryDef> CollectAvailable(RecipeDef recipe, IReadOnlyDictionary<string, int> rolledCounts)
        {
            var available = new List<RecipeEntryDef>();
            foreach (RecipeEntryDef entry in recipe.Pool)
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
