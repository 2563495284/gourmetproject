using System;
using System.Collections.Generic;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>菜谱小组随机池的一项：菜品 id + 权重 + 最多被选次数（0 不限）。</summary>
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

    /// <summary>一个可随机菜品小组。数量方案按 Recipe.Groups 中的顺序与小组一一对应。</summary>
    public sealed class RecipeGroupDef
    {
        public RecipeGroupDef(string id, IReadOnlyList<RecipeEntryDef> pool)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Pool = pool ?? Array.Empty<RecipeEntryDef>();
        }

        public string Id { get; }

        public IReadOnlyList<RecipeEntryDef> Pool { get; }
    }

    /// <summary>一套初始菜谱数量方案：每项是对应小组要抽取的数量，方案本身按权重选择。</summary>
    public sealed class RecipeRollPlanDef
    {
        public RecipeRollPlanDef(string id, float weight, IReadOnlyList<int> groupCounts)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Weight = weight;
            GroupCounts = groupCounts ?? Array.Empty<int>();
        }

        public string Id { get; }

        public float Weight { get; }

        public IReadOnlyList<int> GroupCounts { get; }
    }

    /// <summary>
    /// 菜谱定义：固定菜品 + 多个随机小组 + 带权数量方案。由 RecipeRoller 实例化成具体菜谱牌组。
    /// </summary>
    public sealed class RecipeDef
    {
        public RecipeDef(
            string id,
            IReadOnlyList<string> fixedDishes,
            IReadOnlyList<RecipeGroupDef> groups,
            IReadOnlyList<RecipeRollPlanDef> rollPlans)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            FixedDishes = fixedDishes ?? Array.Empty<string>();
            Groups = groups ?? Array.Empty<RecipeGroupDef>();
            RollPlans = rollPlans ?? Array.Empty<RecipeRollPlanDef>();
        }

        public string Id { get; }

        public IReadOnlyList<string> FixedDishes { get; }

        public IReadOnlyList<RecipeGroupDef> Groups { get; }

        public IReadOnlyList<RecipeRollPlanDef> RollPlans { get; }
    }
}
