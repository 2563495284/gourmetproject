using System;
using System.Collections.Generic;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 菜品定义。纯数据，由 Game 层从 Luban TbDish 适配生成。
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
            IReadOnlyList<string> inherentTags,
            string icon,
            bool allowRotate,
            int maxRollCount)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name;
            Deliciousness = deliciousness;
            Shape = shape ?? throw new ArgumentNullException(nameof(shape));
            HiddenMin = hiddenMin;
            HiddenMax = hiddenMax;
            BaseWeight = baseWeight;
            InherentTags = inherentTags ?? Array.Empty<string>();
            Icon = icon ?? string.Empty;
            AllowRotate = allowRotate;
            MaxRollCount = maxRollCount;
        }

        public string Id { get; }

        public string Name { get; }

        /// <summary>美味度（基础分数）。同时作为菜谱生成时的「初始分」。</summary>
        public int Deliciousness { get; }

        public DishShape Shape { get; }

        public int HiddenMin { get; }

        public int HiddenMax { get; }

        /// <summary>隐藏分均值 b（用于菜品库加权随机）。</summary>
        public float HiddenMean => (HiddenMin + HiddenMax) * 0.5f;

        public float BaseWeight { get; }

        public IReadOnlyList<string> InherentTags { get; }

        public string Icon { get; }

        public bool AllowRotate { get; }

        /// <summary>最多被随机次数；0 表示不限。</summary>
        public int MaxRollCount { get; }

        /// <summary>要求隐藏分是否落在本菜品隐藏分范围内。</summary>
        public bool CoversHiddenScore(int requiredHidden)
            => requiredHidden >= HiddenMin && requiredHidden <= HiddenMax;
    }
}
