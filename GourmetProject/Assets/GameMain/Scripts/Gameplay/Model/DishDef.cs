using System;
using System.Collections.Generic;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 食物定义（即随机食物库的一个条目）。纯数据，由 Game 层把 Luban 的
    /// 本体表 TbDishBase 与变体表 TbDishVariant join 后适配生成。
    /// 同一 <see cref="BaseId"/> 的不同 DishDef 即「带不同标签视为不同食物」。
    /// </summary>
    public sealed class DishDef
    {
        public DishDef(
            string id,
            string name,
            int deliciousness,
            DishShape shape,
            int hiddenMin,
            int hiddenMax,
            float baseWeight,
            IReadOnlyList<string> skillIds,
            string flavorId,
            bool allowRotate,
            string baseId = null,
            int price = 0,
            int rotationIndex = 0,
            string category = null,
            int countAs = 1,
            int sortOrder = 0)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name;
            Deliciousness = deliciousness;
            Shape = shape ?? throw new ArgumentNullException(nameof(shape));
            HiddenMin = hiddenMin;
            HiddenMax = hiddenMax;
            BaseWeight = baseWeight;
            SkillIds = skillIds ?? Array.Empty<string>();
            FlavorId = flavorId ?? string.Empty;
            AllowRotate = allowRotate;
            BaseId = baseId ?? id;
            Price = price;
            RotationIndex = ((rotationIndex % 4) + 4) % 4;
            Category = category ?? string.Empty;
            CountAs = countAs < 1 ? 1 : countAs;
            SortOrder = sortOrder;
        }

        public string Id { get; }

        /// <summary>所属本体 id（TbDishBase.id）；未指定时回退为自身 Id。</summary>
        public string BaseId { get; }

        public string Name { get; }

        /// <summary>美味值（基础分数），用于技能结算与分数结算。</summary>
        public int Deliciousness { get; }

        public DishShape Shape { get; }

        public int HiddenMin { get; }

        public int HiddenMax { get; }

        /// <summary>隐藏分均值 b（用于食物库加权随机）。</summary>
        public float HiddenMean => (HiddenMin + HiddenMax) * 0.5f;

        public float BaseWeight { get; }

        /// <summary>购买价格（商店）。</summary>
        public int Price { get; }

        /// <summary>
        /// 食物初始技能 id 列表（数量无上限）。创建餐桌实例时整份带入，运行时可被装饰品和消耗品追加/修改。
        /// </summary>
        public IReadOnlyList<string> SkillIds { get; }

        /// <summary>
        /// 食物初始风味 id（单槽，可空）。再次获得风味会替换原风味，特殊装饰品和消耗品可解除单槽上限。
        /// </summary>
        public string FlavorId { get; }

        /// <summary>是否带有风味。</summary>
        public bool HasFlavor => !string.IsNullOrEmpty(FlavorId);

        public bool AllowRotate { get; }

        /// <summary>
        /// 变体固定旋转朝向（0=原始，1/2/3=顺时针 90° 的次数）。<see cref="AllowRotate"/> 为 false 时，
        /// 自动上菜只以该朝向摆放；为 true 时该值不生效（枚举全部朝向），两者预期互斥。
        /// </summary>
        public int RotationIndex { get; }

        /// <summary>食物分类（如 cake）；空串=无分类。供分类检测/定向。</summary>
        public string Category { get; }

        /// <summary>是否属于某分类。</summary>
        public bool IsCategory(string category)
            => !string.IsNullOrEmpty(category) && string.Equals(Category, category, StringComparison.OrdinalIgnoreCase);

        /// <summary>「视为食物数」基础值（默认 1）；技能计数时按此累加。</summary>
        public int CountAs { get; }

        /// <summary>食谱中的展示顺序；数值越小越靠前。</summary>
        public int SortOrder { get; }

        /// <summary>要求隐藏分是否落在本食物隐藏分范围内。</summary>
        public bool CoversHiddenScore(int requiredHidden)
            => requiredHidden >= HiddenMin && requiredHidden <= HiddenMax;
    }
}
