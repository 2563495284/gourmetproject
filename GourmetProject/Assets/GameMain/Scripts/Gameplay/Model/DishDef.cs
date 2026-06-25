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
            int initScore,
            DishShape shape,
            int hiddenMin,
            int hiddenMax,
            float baseWeight,
            IReadOnlyList<string> inherentTags,
            string icon,
            bool allowRotate,
            string baseId = null,
            int price = 0)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name;
            Deliciousness = deliciousness;
            InitScore = initScore;
            Shape = shape ?? throw new ArgumentNullException(nameof(shape));
            HiddenMin = hiddenMin;
            HiddenMax = hiddenMax;
            BaseWeight = baseWeight;
            InherentTags = inherentTags ?? Array.Empty<string>();
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

        /// <summary>初始菜谱生成时累计的分值。</summary>
        public int InitScore { get; }

        public DishShape Shape { get; }

        public int HiddenMin { get; }

        public int HiddenMax { get; }

        /// <summary>隐藏分均值 b（用于菜品库加权随机）。</summary>
        public float HiddenMean => (HiddenMin + HiddenMax) * 0.5f;

        public float BaseWeight { get; }

        /// <summary>购买价格（商店）。</summary>
        public int Price { get; }

        /// <summary>
        /// 种子标签集合：本体固有标签 + 变体的唯一标签 A/B。创建棋盘实例时整份交给
        /// TagComposer，由其按 category 去重并执行唯一 A/B 的「上限 1、再次获得替换」规则。
        /// </summary>
        public IReadOnlyList<string> InherentTags { get; }

        public string Icon { get; }

        public bool AllowRotate { get; }

        /// <summary>要求隐藏分是否落在本菜品隐藏分范围内。</summary>
        public bool CoversHiddenScore(int requiredHidden)
            => requiredHidden >= HiddenMin && requiredHidden <= HiddenMax;
    }
}
