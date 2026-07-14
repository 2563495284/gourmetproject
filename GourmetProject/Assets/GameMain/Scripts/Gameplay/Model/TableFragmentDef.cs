using System;
using System.Collections.Generic;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 餐桌碎片定义（也用作「初始胃」的形状来源）。纯数据，由 Game 层从 Luban 适配生成。
    /// <see cref="ShapeRows"/> 保留原始字符行（不做归一化），使 <see cref="CellMaterial"/> 的 (x,y) 与之一一对应。
    /// </summary>
    public sealed class TableFragmentDef
    {
        public TableFragmentDef(
            string id,
            IReadOnlyList<string> shapeRows,
            int hiddenMin,
            int hiddenMax,
            float baseWeight,
            int price,
            IReadOnlyList<string> materialIds,
            IReadOnlyList<CellMaterial> materials)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            ShapeRows = shapeRows ?? throw new ArgumentNullException(nameof(shapeRows));
            HiddenMin = hiddenMin;
            HiddenMax = hiddenMax;
            BaseWeight = baseWeight;
            Price = price;
            MaterialIds = materialIds ?? Array.Empty<string>();
            CellMaterials = materials ?? Array.Empty<CellMaterial>();
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

        /// <summary>该碎片开包时可随机落位的材质 id 列表。</summary>
        public IReadOnlyList<string> MaterialIds { get; }

        /// <summary>强化格标签：每个元素 = 该碎片某格 (x,y) 挂的一个标签 id。</summary>
        public IReadOnlyList<CellMaterial> CellMaterials { get; }

        public TableFragmentDef WithCellMaterials(IReadOnlyList<CellMaterial> materials)
        {
            return new TableFragmentDef(Id, ShapeRows, HiddenMin, HiddenMax, BaseWeight, Price, MaterialIds, materials);
        }

        /// <summary>
        /// 顺时针旋转 <paramref name="times"/> 个 90°（按 4 取模），返回新的碎片定义。
        /// 存在格与 <see cref="CellMaterials"/> 位置用同一公式同步旋转，保持一一对应。id/隐藏分/权重/价格不变。
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

            var newTags = new List<CellMaterial>(CellMaterials.Count);
            foreach (CellMaterial ct in CellMaterials)
            {
                int nx = (h - 1) - ct.Pos.Y;
                int ny = ct.Pos.X;
                newTags.Add(new CellMaterial(new GridPos(nx, ny), ct.MaterialId));
            }

            return new TableFragmentDef(Id, newRows, HiddenMin, HiddenMax, BaseWeight, Price, MaterialIds, newTags);
        }

        private static bool IsFilled(char c) => c == 'X' || c == 'x' || c == '1' || c == '#';
    }

    /// <summary>碎片某一格的强化标签：局部坐标 + 标签 id。</summary>
    public readonly struct CellMaterial
    {
        public CellMaterial(GridPos pos, string tagId)
        {
            Pos = pos;
            MaterialId = tagId;
        }

        public GridPos Pos { get; }

        public string MaterialId { get; }
    }
}
