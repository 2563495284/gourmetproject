using System;
using System.Collections.Generic;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 菜品定义（即随机菜品库的一个条目）。纯数据，由 Game 层把 Luban 的
    /// 本体表 TbDishBase 与变体表 TbDishVariant join 后适配生成。
    /// 同一 <see cref="BaseId"/> 的不同 DishDef 即「带不同标签视为不同菜品」。
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
            string icon,
            bool allowRotate,
            string baseId = null,
            int price = 0)
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
            Icon = icon ?? string.Empty;
            AllowRotate = allowRotate;
            BaseId = baseId ?? id;
            Price = price;
        }

        public string Id { get; }

        /// <summary>所属本体 id（TbDishBase.id）；未指定时回退为自身 Id。</summary>
        public string BaseId { get; }

        public string Name { get; }

        /// <summary>美味度（基础分数），用于技能结算与分数结算。</summary>
        public int Deliciousness { get; }

        public DishShape Shape { get; }

        public int HiddenMin { get; }

        public int HiddenMax { get; }

        /// <summary>隐藏分均值 b（用于菜品库加权随机）。</summary>
        public float HiddenMean => (HiddenMin + HiddenMax) * 0.5f;

        public float BaseWeight { get; }

        /// <summary>购买价格（商店）。</summary>
        public int Price { get; }

        /// <summary>
        /// 菜品初始技能 id 列表（数量无上限）。创建棋盘实例时整份带入，运行时可被道具追加/修改。
        /// </summary>
        public IReadOnlyList<string> SkillIds { get; }

        /// <summary>
        /// 菜品初始风味 id（单槽，可空）。再次获得风味会替换原风味，特殊道具可解除单槽上限。
        /// </summary>
        public string FlavorId { get; }

        /// <summary>是否带有风味。</summary>
        public bool HasFlavor => !string.IsNullOrEmpty(FlavorId);

        public string Icon { get; }

        public bool AllowRotate { get; }

        /// <summary>要求隐藏分是否落在本菜品隐藏分范围内。</summary>
        public bool CoversHiddenScore(int requiredHidden)
            => requiredHidden >= HiddenMin && requiredHidden <= HiddenMax;
    }
}
