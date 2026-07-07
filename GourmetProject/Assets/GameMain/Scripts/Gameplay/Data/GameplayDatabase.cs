using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Data
{
    /// <summary>
    /// 玩法静态数据库：菜品/技能/风味/格子标签/菜谱定义的只读查询入口。
    /// 由 Game 层从 Luban 表适配填充，玩法逻辑只读取，从而与配置实现解耦、可独立单测。
    /// </summary>
    public sealed class GameplayDatabase
    {
        private readonly Dictionary<string, DishDef> _dishes;
        private readonly Dictionary<string, SkillDef> _skills;
        private readonly Dictionary<string, FlavorDef> _flavors;
        private readonly Dictionary<string, CellTagDef> _cellTags;
        private readonly Dictionary<string, RecipeDef> _recipes;
        private readonly Dictionary<string, StomachFragmentDef> _fragments;
        private readonly List<CakeLayerBuffDef> _cakeLayerBuffs;

        public GameplayDatabase(
            IEnumerable<DishDef> dishes,
            IEnumerable<SkillDef> skills,
            IEnumerable<FlavorDef> flavors,
            IEnumerable<CellTagDef> cellTags,
            IEnumerable<RecipeDef> recipes,
            IEnumerable<StomachFragmentDef> fragments = null,
            IEnumerable<CakeLayerBuffDef> cakeLayerBuffs = null)
        {
            _dishes = ToMap(dishes, d => d.Id, nameof(dishes));
            _skills = ToMap(skills, s => s.Id, nameof(skills));
            _flavors = ToMap(flavors, f => f.Id, nameof(flavors));
            _cellTags = ToMap(cellTags, c => c.Id, nameof(cellTags));
            _recipes = ToMap(recipes, r => r.Id, nameof(recipes));
            _fragments = ToMap(fragments ?? System.Array.Empty<StomachFragmentDef>(), f => f.Id, nameof(fragments));

            _cakeLayerBuffs = new List<CakeLayerBuffDef>(cakeLayerBuffs ?? System.Array.Empty<CakeLayerBuffDef>());
            _cakeLayerBuffs.Sort((a, b) =>
            {
                int cmp = a.Threshold.CompareTo(b.Threshold);
                return cmp != 0 ? cmp : a.Order.CompareTo(b.Order);
            });
        }

        public IReadOnlyCollection<DishDef> AllDishes => _dishes.Values;

        /// <summary>欢乐蛋糕层数分段 buff（按阈值/顺序升序）。空表示未配置。</summary>
        public IReadOnlyList<CakeLayerBuffDef> CakeLayerBuffs => _cakeLayerBuffs;

        public IReadOnlyCollection<SkillDef> AllSkills => _skills.Values;

        public IReadOnlyCollection<FlavorDef> AllFlavors => _flavors.Values;

        public IReadOnlyCollection<CellTagDef> AllCellTags => _cellTags.Values;

        public IReadOnlyCollection<StomachFragmentDef> AllFragments => _fragments.Values;

        public DishDef GetDish(string id) => _dishes.TryGetValue(id, out DishDef d) ? d : null;

        public SkillDef GetSkill(string id) => id != null && _skills.TryGetValue(id, out SkillDef s) ? s : null;

        public FlavorDef GetFlavor(string id) => id != null && _flavors.TryGetValue(id, out FlavorDef f) ? f : null;

        public CellTagDef GetCellTag(string id) => id != null && _cellTags.TryGetValue(id, out CellTagDef c) ? c : null;

        public RecipeDef GetRecipe(string id) => _recipes.TryGetValue(id, out RecipeDef r) ? r : null;

        public StomachFragmentDef GetFragment(string id) => id != null && _fragments.TryGetValue(id, out StomachFragmentDef f) ? f : null;

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
