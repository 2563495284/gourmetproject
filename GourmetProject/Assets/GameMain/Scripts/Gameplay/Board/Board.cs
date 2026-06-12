using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Board
{
    /// <summary>
    /// 局内棋盘。维护占用网格与已摆放菜品实例，提供合法摆放查询与相邻/空位统计。
    /// 不含随机与计分逻辑，便于独立单测。
    /// </summary>
    public sealed class Board
    {
        public const int Empty = -1;

        private readonly int[] _cells; // 存放占用该格的实例 Id，Empty 表示空。
        private readonly List<DishInstance> _dishes = new List<DishInstance>();

        public Board(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Board size must be positive.");
            }

            Width = width;
            Height = height;
            _cells = new int[width * height];
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i] = Empty;
            }
        }

        public int Width { get; }

        public int Height { get; }

        public int CellCapacity => Width * Height;

        public IReadOnlyList<DishInstance> Dishes => _dishes;

        public int DishCount => _dishes.Count;

        public bool InBounds(GridPos p) => p.X >= 0 && p.X < Width && p.Y >= 0 && p.Y < Height;

        public bool IsEmpty(GridPos p) => InBounds(p) && _cells[Index(p)] == Empty;

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

        public int EmptyCellCount => CellCapacity - OccupiedCellCount;

        /// <summary>判断某朝向形状能否放在以 origin 为左上的位置（全部格在界内且为空）。</summary>
        public bool CanPlace(DishShape orientation, GridPos origin)
        {
            if (orientation == null)
            {
                return false;
            }

            foreach (GridPos cell in orientation.Cells)
            {
                GridPos abs = cell.Offset(origin.X, origin.Y);
                if (!InBounds(abs) || _cells[Index(abs)] != Empty)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>枚举某菜品在当前棋盘上的全部合法摆放（朝向 × 原点）。</summary>
        public List<Placement> FindValidPlacements(DishDef def)
        {
            if (def == null)
            {
                throw new ArgumentNullException(nameof(def));
            }

            var placements = new List<Placement>();
            IReadOnlyList<DishShape> orientations = def.Shape.GetOrientations(def.AllowRotate);

            for (int r = 0; r < orientations.Count; r++)
            {
                DishShape shape = orientations[r];
                for (int y = 0; y <= Height - shape.Height; y++)
                {
                    for (int x = 0; x <= Width - shape.Width; x++)
                    {
                        var origin = new GridPos(x, y);
                        if (CanPlace(shape, origin))
                        {
                            placements.Add(new Placement(shape, r, origin));
                        }
                    }
                }
            }

            return placements;
        }

        public bool CanFit(DishDef def) => FindValidPlacements(def).Count > 0;

        /// <summary>放置一个已构造好的实例。若占格非法或已被占用则抛出。</summary>
        public void Place(DishInstance dish)
        {
            if (dish == null)
            {
                throw new ArgumentNullException(nameof(dish));
            }

            foreach (GridPos cell in dish.OccupiedCells)
            {
                if (!InBounds(cell) || _cells[Index(cell)] != Empty)
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
            yield return p.Offset(1, 0);
            yield return p.Offset(-1, 0);
            yield return p.Offset(0, 1);
            yield return p.Offset(0, -1);
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
