using System;
using System.Collections.Generic;
using System.Linq;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 菜品的占格形状（多连块）。以一组归一化后的格子偏移表示，左上对齐到 (0,0)。
    /// 形状不可变；旋转返回新的实例。
    /// </summary>
    public sealed class DishShape
    {
        private readonly GridPos[] _cells;

        public DishShape(IEnumerable<GridPos> cells)
        {
            if (cells == null)
            {
                throw new ArgumentNullException(nameof(cells));
            }

            _cells = Normalize(cells);
            if (_cells.Length == 0)
            {
                throw new ArgumentException("DishShape must contain at least one cell.", nameof(cells));
            }

            Width = _cells.Max(c => c.X) + 1;
            Height = _cells.Max(c => c.Y) + 1;
        }

        /// <summary>归一化后的占格列表（左上对齐，按 行优先 排序，保证可复现）。</summary>
        public IReadOnlyList<GridPos> Cells => _cells;

        public int CellCount => _cells.Length;

        public int Width { get; }

        public int Height { get; }

        /// <summary>
        /// 用字符行解析形状：'X'/'x'/'1' 为占格，其余字符（'.'/空格/'0'）视为空。
        /// 行从上到下、列从左到右。
        /// </summary>
        public static DishShape FromRows(IReadOnlyList<string> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                throw new ArgumentException("rows must be non-empty.", nameof(rows));
            }

            var cells = new List<GridPos>();
            for (int y = 0; y < rows.Count; y++)
            {
                string row = rows[y] ?? string.Empty;
                for (int x = 0; x < row.Length; x++)
                {
                    char c = row[x];
                    if (c == 'X' || c == 'x' || c == '1' || c == '#')
                    {
                        cells.Add(new GridPos(x, y));
                    }
                }
            }

            return new DishShape(cells);
        }

        /// <summary>顺时针旋转 90 度，返回归一化后的新形状。</summary>
        public DishShape Rotate90()
        {
            // 顺时针：新 x = (Height-1) - oldY，新 y = oldX。
            var rotated = new List<GridPos>(_cells.Length);
            foreach (GridPos c in _cells)
            {
                rotated.Add(new GridPos((Height - 1) - c.Y, c.X));
            }

            return new DishShape(rotated);
        }

        /// <summary>返回该形状的全部不同朝向（最多 4 个，去重）。allowRotate=false 时仅返回自身。</summary>
        public IReadOnlyList<DishShape> GetOrientations(bool allowRotate)
        {
            var result = new List<DishShape> { this };
            if (!allowRotate)
            {
                return result;
            }

            DishShape current = this;
            for (int i = 0; i < 3; i++)
            {
                current = current.Rotate90();
                if (!result.Any(s => s.CellsEqual(current)))
                {
                    result.Add(current);
                }
            }

            return result;
        }

        private bool CellsEqual(DishShape other)
        {
            if (other._cells.Length != _cells.Length)
            {
                return false;
            }

            for (int i = 0; i < _cells.Length; i++)
            {
                if (!_cells[i].Equals(other._cells[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static GridPos[] Normalize(IEnumerable<GridPos> cells)
        {
            var distinct = new List<GridPos>();
            foreach (GridPos c in cells)
            {
                if (!distinct.Contains(c))
                {
                    distinct.Add(c);
                }
            }

            if (distinct.Count == 0)
            {
                return Array.Empty<GridPos>();
            }

            int minX = distinct.Min(c => c.X);
            int minY = distinct.Min(c => c.Y);

            return distinct
                .Select(c => new GridPos(c.X - minX, c.Y - minY))
                .OrderBy(c => c.Y)
                .ThenBy(c => c.X)
                .ToArray();
        }
    }
}
