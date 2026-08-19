using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Board
{
    /// <summary>
    /// 局内餐桌（胃）。在最大 Width×Height 包围盒内，每格有三态：不存在(胃外)/存在且空/被占用。
    /// 不含随机与计分逻辑，便于独立单测。
    /// </summary>
    public sealed class DiningTable
    {
        public const int Empty = -1;

        private readonly int[] _cells;       // 占用该格的实例 Id，Empty 表示空。
        private readonly bool[] _exists;      // 该格是否属于胃。
        private readonly bool[] _disabled;    // 临时禁用格：存在但不可上菜。
        private readonly List<DishInstance> _dishes = new List<DishInstance>();
        private int _existingCount;

        /// <summary>构造一个全存在的矩形餐桌。</summary>
        public DiningTable(int width, int height)
            : this(width, height, null)
        {
        }

        /// <summary>
        /// 构造不规则餐桌。<paramref name="existingCells"/> 为 null 时所有格都存在（矩形）；
        /// 否则仅列出的格存在。
        /// </summary>
        public DiningTable(
            int width,
            int height,
            IEnumerable<GridPos> existingCells)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "DiningTable size must be positive.");
            }

            Width = width;
            Height = height;
            _cells = new int[width * height];
            _exists = new bool[width * height];
            _disabled = new bool[width * height];

            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i] = Empty;
            }

            if (existingCells == null)
            {
                for (int i = 0; i < _exists.Length; i++)
                {
                    _exists[i] = true;
                }

                _existingCount = _cells.Length;
            }
            else
            {
                foreach (GridPos cell in existingCells)
                {
                    if (!InBounds(cell))
                    {
                        continue;
                    }

                    int idx = Index(cell);
                    if (!_exists[idx])
                    {
                        _exists[idx] = true;
                        _existingCount++;
                    }
                }
            }
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>存在格总数（容量）。不规则胃下小于 Width×Height。</summary>
        public int CellCapacity => _existingCount;

        public IReadOnlyList<DishInstance> Dishes => _dishes;

        public int DishCount => _dishes.Count;

        public bool InBounds(GridPos p) => p.X >= 0 && p.X < Width && p.Y >= 0 && p.Y < Height;

        /// <summary>该格是否属于胃（在界内且被标记为存在）。</summary>
        public bool Exists(GridPos p) => InBounds(p) && _exists[Index(p)];

        public bool IsDisabled(GridPos p) => Exists(p) && _disabled[Index(p)];

        public void SetExists(GridPos p, bool exists)
        {
            if (!InBounds(p))
            {
                return;
            }

            int idx = Index(p);
            if (!exists && _cells[idx] != Empty)
            {
                throw new InvalidOperationException($"Cannot remove occupied cell {p}.");
            }

            if (_exists[idx] == exists)
            {
                return;
            }

            _exists[idx] = exists;
            _disabled[idx] = false;
            if (exists)
            {
                _existingCount++;
            }
            else
            {
                _existingCount--;
            }
        }

        public void SetDisabled(GridPos p, bool disabled)
        {
            if (!Exists(p))
            {
                return;
            }

            int idx = Index(p);
            if (disabled && _cells[idx] != Empty)
            {
                throw new InvalidOperationException($"Cannot disable occupied cell {p}.");
            }

            _disabled[idx] = disabled;
        }

        public List<GridPos> ExistingCells()
        {
            var cells = new List<GridPos>();
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    var p = new GridPos(x, y);
                    if (Exists(p))
                    {
                        cells.Add(p);
                    }
                }
            }

            return cells;
        }

        /// <summary>
        /// 求所有「存在格」的最小包围盒（闭区间），用于把不规则/偏置的胃整体居中显示。
        /// 没有任何存在格时返回 false。
        /// </summary>
        public bool TryGetExistingBounds(out int minX, out int minY, out int maxX, out int maxY)
        {
            minX = minY = int.MaxValue;
            maxX = maxY = int.MinValue;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (!_exists[y * Width + x])
                    {
                        continue;
                    }

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            return maxX >= minX;
        }

        /// <summary>该格是否存在且未被占用。</summary>
        public bool IsEmpty(GridPos p) => Exists(p) && !_disabled[Index(p)] && _cells[Index(p)] == Empty;

        public int OccupiedCellCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _cells.Length; i++)
                {
                    if (_cells[i] != Empty)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>存在且空的格数。</summary>
        public int EmptyCellCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _cells.Length; i++)
                {
                    if (_exists[i] && !_disabled[i] && _cells[i] == Empty)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>判断某朝向形状能否放在以 origin 为左上的位置（全部格存在且为空）。</summary>
        public bool CanPlace(DishShape orientation, GridPos origin)
        {
            if (orientation == null)
            {
                return false;
            }

            // DishShape.Cells is exposed as IReadOnlyList but backed by an array. Iterating it
            // through foreach boxes the array enumerator on Mono; CanPlace is called millions of
            // times by bounded placement preview, so that tiny allocation dominated long GM runs.
            // Indexing preserves the exact cell order and placement semantics without allocating.
            IReadOnlyList<GridPos> cells = orientation.Cells;
            for (int i = 0; i < cells.Count; i++)
            {
                GridPos cell = cells[i];
                GridPos abs = cell.Offset(origin.X, origin.Y);
                if (!Exists(abs) || _disabled[Index(abs)] || _cells[Index(abs)] != Empty)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>枚举某食物以基础朝向（rotation=0）在当前餐桌上的全部合法原点。</summary>
        public List<Placement> FindValidPlacements(DishDef def)
        {
            if (def == null)
            {
                throw new ArgumentNullException(nameof(def));
            }

            var placements = new List<Placement>();
            AddPlacementsForOrientation(def.Shape, 0, placements);

            return placements;
        }

        /// <summary>
        /// 「麻」专用：把食物基础形状**逆时针**旋转 <paramref name="ccwSteps"/> 个 90°，
        /// 并以该唯一朝向枚举全部合法原点。返回空列表表示旋转后放不下。
        /// </summary>
        public List<Placement> FindValidPlacementsRotatedCcw(DishDef def, int ccwSteps)
        {
            if (def == null)
            {
                throw new ArgumentNullException(nameof(def));
            }

            int rotationIndex = (4 - (ccwSteps % 4)) % 4;
            var placements = new List<Placement>();
            AddPlacementsForOrientation(def.Shape.RotatedBy(rotationIndex), rotationIndex, placements);
            return placements;
        }

        /// <summary>
        /// 以指定的固定朝向枚举全部合法原点。用于临时桌食物放回餐桌等必须保留当前朝向的场景。
        /// </summary>
        public List<Placement> FindValidPlacements(DishShape orientation, int rotationIndex)
        {
            if (orientation == null)
            {
                throw new ArgumentNullException(nameof(orientation));
            }

            var placements = new List<Placement>();
            AddPlacementsForOrientation(orientation, rotationIndex, placements);
            return placements;
        }

        private void AddPlacementsForOrientation(DishShape shape, int rotationIndex, List<Placement> placements)
        {
            for (int y = 0; y <= Height - shape.Height; y++)
            {
                for (int x = 0; x <= Width - shape.Width; x++)
                {
                    var origin = new GridPos(x, y);
                    if (CanPlace(shape, origin))
                    {
                        placements.Add(new Placement(shape, rotationIndex, origin));
                    }
                }
            }
        }

        public bool CanFit(DishDef def)
        {
            if (def == null)
            {
                throw new ArgumentNullException(nameof(def));
            }

            return CanFit(def.Shape);
        }

        /// <summary>判断固定朝向是否至少有一个合法位置；找到首个位置后立即返回。</summary>
        public bool CanFit(DishShape shape)
        {
            if (shape == null)
            {
                throw new ArgumentNullException(nameof(shape));
            }

            for (int y = 0; y <= Height - shape.Height; y++)
            {
                for (int x = 0; x <= Width - shape.Width; x++)
                {
                    if (CanPlace(shape, new GridPos(x, y)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>放置一个已构造好的实例。若占格非法或已被占用则抛出。</summary>
        public void Place(DishInstance dish)
        {
            if (dish == null)
            {
                throw new ArgumentNullException(nameof(dish));
            }

            foreach (GridPos cell in dish.OccupiedCells)
            {
                if (!Exists(cell) || _cells[Index(cell)] != Empty)
                {
                    throw new InvalidOperationException($"Cannot place dish '{dish.Def.Id}' at occupied/out-of-bounds cell {cell}.");
                }
            }

            foreach (GridPos cell in dish.OccupiedCells)
            {
                _cells[Index(cell)] = dish.Id;
            }

            _dishes.Add(dish);
        }

        public void Clear()
        {
            _dishes.Clear();
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i] = Empty;
            }
        }

        /// <summary>
        /// 移除一个已放置实例：把它占据的格子还原为空，并从食物列表中剔除。
        /// 从餐桌移除食物；移动时先移除，再调用 <see cref="Place"/> 放到新位置。
        /// </summary>
        public void RemoveDish(DishInstance dish)
        {
            if (dish == null)
            {
                return;
            }

            foreach (GridPos cell in dish.OccupiedCells)
            {
                if (InBounds(cell) && _cells[Index(cell)] == dish.Id)
                {
                    _cells[Index(cell)] = Empty;
                }
            }

            _dishes.RemoveAll(d => d.Id == dish.Id);
        }

        /// <summary>返回占用该格的实例（空格返回 null）。</summary>
        public DishInstance DishAt(GridPos p)
        {
            if (!InBounds(p))
            {
                return null;
            }

            int id = _cells[Index(p)];
            return id == Empty ? null : FindById(id);
        }

        /// <summary>与给定实例「相邻」（至少一条公共边）的其它实例集合。</summary>
        public List<DishInstance> GetAdjacentDishes(DishInstance dish)
        {
            var result = new List<DishInstance>();
            if (dish == null)
            {
                return result;
            }

            var seen = new HashSet<int>();
            foreach (GridPos cell in dish.OccupiedCells)
            {
                foreach (GridPos n in Neighbors(cell))
                {
                    DishInstance other = DishAt(n);
                    if (other != null && other.Id != dish.Id && seen.Add(other.Id))
                    {
                        result.Add(other);
                    }
                }
            }

            return result;
        }

        public int GetAdjacentDishCount(DishInstance dish) => GetAdjacentDishes(dish).Count;

        private static IEnumerable<GridPos> Neighbors(GridPos p)
        {
            return new[]
            {
                p.Offset(1, 0),
                p.Offset(-1, 0),
                p.Offset(0, 1),
                p.Offset(0, -1),
            };
        }

        private DishInstance FindById(int id)
        {
            for (int i = 0; i < _dishes.Count; i++)
            {
                if (_dishes[i].Id == id)
                {
                    return _dishes[i];
                }
            }

            return null;
        }

        private int Index(GridPos p) => p.Y * Width + p.X;
    }
}
