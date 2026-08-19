using System;
using System.Collections.Generic;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 餐桌格定义（也用作初始餐桌形状来源）。纯数据，由 Game 层从 Luban 适配生成。
    /// <see cref="ShapeRows"/> 保留原始字符行（不做归一化）。
    /// </summary>
    public sealed class TableFragmentDef
    {
        public TableFragmentDef(
            string id,
            IReadOnlyList<string> shapeRows,
            int hiddenMin,
            int hiddenMax,
            float baseWeight)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            ShapeRows = shapeRows ?? throw new ArgumentNullException(nameof(shapeRows));
            HiddenMin = hiddenMin;
            HiddenMax = hiddenMax;
            BaseWeight = baseWeight;
        }

        public string Id { get; }

        /// <summary>原始字符行：'X'/'x'/'1'/'#' 为存在格，其余为空。</summary>
        public IReadOnlyList<string> ShapeRows { get; }

        public int HiddenMin { get; }

        public int HiddenMax { get; }

        /// <summary>隐藏分均值（用于碎片库加权随机）。</summary>
        public float HiddenMean => (HiddenMin + HiddenMax) * 0.5f;

        public float BaseWeight { get; }

        /// <summary>
        /// 顺时针旋转 <paramref name="times"/> 个 90°（按 4 取模），返回新的碎片定义。
        /// id/隐藏分/权重不变。
        /// </summary>
        public TableFragmentDef Rotated(int times)
        {
            int t = ((times % 4) + 4) % 4;
            TableFragmentDef current = this;
            for (int i = 0; i < t; i++)
            {
                current = current.Rotate90();
            }

            return current;
        }

        /// <summary>顺时针旋转 90°：新 x = (行数-1) - 旧 y，新 y = 旧 x（与 DishShape.Rotate90 一致）。</summary>
        private TableFragmentDef Rotate90()
        {
            int h = ShapeRows.Count;
            int w = 0;
            for (int y = 0; y < h; y++)
            {
                int len = ShapeRows[y]?.Length ?? 0;
                if (len > w)
                {
                    w = len;
                }
            }

            int newW = h;
            int newH = w;
            var grid = new char[newH][];
            for (int y = 0; y < newH; y++)
            {
                grid[y] = new char[newW];
                for (int x = 0; x < newW; x++)
                {
                    grid[y][x] = '.';
                }
            }

            for (int y = 0; y < h; y++)
            {
                string row = ShapeRows[y] ?? string.Empty;
                for (int x = 0; x < row.Length; x++)
                {
                    if (!IsFilled(row[x]))
                    {
                        continue;
                    }

                    int nx = (h - 1) - y;
                    int ny = x;
                    grid[ny][nx] = 'X';
                }
            }

            var newRows = new string[newH];
            for (int y = 0; y < newH; y++)
            {
                newRows[y] = new string(grid[y]);
            }

            return new TableFragmentDef(Id, newRows, HiddenMin, HiddenMax, BaseWeight);
        }

        private static bool IsFilled(char c) => c == 'X' || c == 'x' || c == '1' || c == '#';
    }
}
