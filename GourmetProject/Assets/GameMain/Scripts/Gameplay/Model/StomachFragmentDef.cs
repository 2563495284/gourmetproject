using System;
using System.Collections.Generic;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 胃部碎片定义（也用作「初始胃」的形状来源）。纯数据，由 Game 层从 Luban 适配生成。
    /// <see cref="ShapeRows"/> 保留原始字符行（不做归一化），使 <see cref="CellTag"/> 的 (x,y) 与之一一对应。
    /// </summary>
    public sealed class StomachFragmentDef
    {
        public StomachFragmentDef(
            string id,
            IReadOnlyList<string> shapeRows,
            int hiddenMin,
            int hiddenMax,
            float baseWeight,
            int price,
            IReadOnlyList<CellTag> cellTags)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            ShapeRows = shapeRows ?? throw new ArgumentNullException(nameof(shapeRows));
            HiddenMin = hiddenMin;
            HiddenMax = hiddenMax;
            BaseWeight = baseWeight;
            Price = price;
            CellTags = cellTags ?? Array.Empty<CellTag>();
        }

        public string Id { get; }

        /// <summary>原始字符行：'X'/'x'/'1'/'#' 为存在格，其余为空。</summary>
        public IReadOnlyList<string> ShapeRows { get; }

        public int HiddenMin { get; }

        public int HiddenMax { get; }

        /// <summary>隐藏分均值（用于碎片库加权随机；本期仅配置不抽取）。</summary>
        public float HiddenMean => (HiddenMin + HiddenMax) * 0.5f;

        public float BaseWeight { get; }

        public int Price { get; }

        /// <summary>强化格标签：每个元素 = 该碎片某格 (x,y) 挂的一个标签 id。</summary>
        public IReadOnlyList<CellTag> CellTags { get; }
    }

    /// <summary>碎片某一格的强化标签：局部坐标 + 标签 id。</summary>
    public readonly struct CellTag
    {
        public CellTag(GridPos pos, string tagId)
        {
            Pos = pos;
            TagId = tagId;
        }

        public GridPos Pos { get; }

        public string TagId { get; }
    }
}
