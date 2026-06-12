using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Data
{
    /// <summary>
    /// 玩法静态数据库：菜品/标签/菜谱定义的只读查询入口。
    /// 由 Game 层从 Luban 表适配填充，玩法逻辑只读取，从而与配置实现解耦、可独立单测。
    /// </summary>
    public sealed class GameplayDatabase
    {
        private readonly Dictionary<string, DishDef> _dishes;
        private readonly Dictionary<string, TagDef> _tags;
        private readonly Dictionary<string, RecipeDef> _recipes;

        public GameplayDatabase(
            IEnumerable<DishDef> dishes,
            IEnumerable<TagDef> tags,
            IEnumerable<RecipeDef> recipes)
        {
            _dishes = ToMap(dishes, d => d.Id, nameof(dishes));
            _tags = ToMap(tags, t => t.Id, nameof(tags));
            _recipes = ToMap(recipes, r => r.Id, nameof(recipes));
        }

        public IReadOnlyCollection<DishDef> AllDishes => _dishes.Values;

        public IReadOnlyCollection<TagDef> AllTags => _tags.Values;

        public DishDef GetDish(string id) => _dishes.TryGetValue(id, out DishDef d) ? d : null;

        public TagDef GetTag(string id) => _tags.TryGetValue(id, out TagDef t) ? t : null;

        public RecipeDef GetRecipe(string id) => _recipes.TryGetValue(id, out RecipeDef r) ? r : null;

        public bool TryGetDish(string id, out DishDef dish) => _dishes.TryGetValue(id, out dish);

        private static Dictionary<string, T> ToMap<T>(IEnumerable<T> items, Func<T, string> keySelector, string paramName)
        {
            if (items == null)
            {
                throw new ArgumentNullException(paramName);
            }

            var map = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (T item in items)
            {
                string key = keySelector(item);
                map[key] = item;
            }

            return map;
        }
    }
}
