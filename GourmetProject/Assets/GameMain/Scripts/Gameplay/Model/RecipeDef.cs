using System;
using System.Collections.Generic;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>菜谱随机池的一项：菜品 id + 权重 + 最多被选次数（0 不限）。</summary>
    public sealed class RecipeEntryDef
    {
        public RecipeEntryDef(string dishId, float weight, int maxCount)
        {
            DishId = dishId;
            Weight = weight;
            MaxCount = maxCount;
        }

        public string DishId { get; }

        public float Weight { get; }

        public int MaxCount { get; }
    }

    /// <summary>
    /// 菜谱定义：固定菜品 + 加权放回随机池 + 要求初始分。由 RecipeRoller 实例化成具体菜谱牌组。
    /// </summary>
    public sealed class RecipeDef
    {
        public RecipeDef(
            string id,
            IReadOnlyList<string> fixedDishes,
            IReadOnlyList<RecipeEntryDef> pool,
            int requiredInitScore)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            FixedDishes = fixedDishes ?? Array.Empty<string>();
            Pool = pool ?? Array.Empty<RecipeEntryDef>();
            RequiredInitScore = requiredInitScore;
        }

        public string Id { get; }

        public IReadOnlyList<string> FixedDishes { get; }

        public IReadOnlyList<RecipeEntryDef> Pool { get; }

        public int RequiredInitScore { get; }
    }
}
