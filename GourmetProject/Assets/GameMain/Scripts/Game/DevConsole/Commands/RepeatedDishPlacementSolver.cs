using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.DevConsole.Commands
{
    /// <summary>
    /// 在当前餐桌空位中选择尽可能多的同形状固定朝向摆位。
    /// 先生成一个确定性的贪心结果，再在固定节点预算内继续改进，避免 GM 命令阻塞主线程。
    /// </summary>
    internal static class RepeatedDishPlacementSolver
    {
        internal const int SearchNodeLimit = 50000;

        public static IReadOnlyList<Placement> Solve(DiningTable table, DishDef dish)
            => SolveWithDiagnostics(table, dish).Placements;

        internal static RepeatedDishPlacementResult SolveWithDiagnostics(
            DiningTable table,
            DishDef dish)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            if (dish == null)
            {
                throw new ArgumentNullException(nameof(dish));
            }

            if (table.Width * table.Height > CellMask.Capacity)
            {
                return GreedyFallback(table, dish);
            }

            return new Search(table, dish).Solve();
        }

        private static RepeatedDishPlacementResult GreedyFallback(
            DiningTable table,
            DishDef dish)
        {
            var occupied = new HashSet<GridPos>();
            var result = new List<Placement>();
            List<Placement> candidates = table.FindValidPlacements(dish);
            candidates.Sort(ComparePlacements);
            foreach (Placement candidate in candidates)
            {
                bool overlaps = false;
                foreach (GridPos local in candidate.Orientation.Cells)
                {
                    if (occupied.Contains(local.Offset(
                            candidate.Origin.X,
                            candidate.Origin.Y)))
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (overlaps)
                {
                    continue;
                }

                result.Add(candidate);
                foreach (GridPos local in candidate.Orientation.Cells)
                {
                    occupied.Add(local.Offset(candidate.Origin.X, candidate.Origin.Y));
                }
            }

            return new RepeatedDishPlacementResult(result, true, 0);
        }

        private sealed class Search
        {
            private readonly int _width;
            private readonly int _cellCount;
            private readonly int _dishCellCount;
            private readonly bool[] _available;
            private readonly int[] _availableSuffix;
            private readonly List<Candidate> _allCandidates = new List<Candidate>();
            private readonly List<Candidate>[] _candidatesByFirstCell;
            private readonly Dictionary<SearchState, int> _bestSeenAtState =
                new Dictionary<SearchState, int>();
            private readonly List<Placement> _current = new List<Placement>();
            private List<Placement> _best = new List<Placement>();
            private int _searchNodes;
            private bool _truncated;

            public Search(DiningTable table, DishDef dish)
            {
                _width = table.Width;
                _cellCount = table.Width * table.Height;
                _dishCellCount = dish.Shape.CellCount;
                _available = new bool[_cellCount];
                _availableSuffix = new int[_cellCount + 1];
                _candidatesByFirstCell = new List<Candidate>[_cellCount];

                for (int y = 0; y < table.Height; y++)
                {
                    for (int x = 0; x < table.Width; x++)
                    {
                        int index = y * table.Width + x;
                        _available[index] = table.IsEmpty(new GridPos(x, y));
                    }
                }

                for (int index = _cellCount - 1; index >= 0; index--)
                {
                    _availableSuffix[index] = _availableSuffix[index + 1]
                        + (_available[index] ? 1 : 0);
                }

                List<Placement> placements = table.FindValidPlacements(dish);
                placements.Sort(ComparePlacements);
                foreach (Placement placement in placements)
                {
                    Candidate candidate = BuildCandidate(placement);
                    _allCandidates.Add(candidate);
                    _candidatesByFirstCell[candidate.FirstCellIndex] ??=
                        new List<Candidate>();
                    _candidatesByFirstCell[candidate.FirstCellIndex].Add(candidate);
                }
            }

            public RepeatedDishPlacementResult Solve()
            {
                if (_cellCount == 0 || _dishCellCount <= 0 || _allCandidates.Count == 0)
                {
                    return new RepeatedDishPlacementResult(
                        Array.Empty<Placement>(),
                        false,
                        0);
                }

                BuildInitialGreedySolution();
                int absoluteUpperBound = _availableSuffix[0] / _dishCellCount;
                if (_best.Count < absoluteUpperBound)
                {
                    Explore(0, CellMask.Empty);
                }

                return new RepeatedDishPlacementResult(
                    _best,
                    _truncated,
                    _searchNodes);
            }

            private void BuildInitialGreedySolution()
            {
                CellMask occupied = CellMask.Empty;
                foreach (Candidate candidate in _allCandidates)
                {
                    if (occupied.Overlaps(candidate.Mask))
                    {
                        continue;
                    }

                    occupied = occupied.Or(candidate.Mask);
                    _best.Add(candidate.Placement);
                }
            }

            private void Explore(int cursor, CellMask occupied)
            {
                if (_searchNodes >= SearchNodeLimit)
                {
                    _truncated = true;
                    return;
                }

                _searchNodes++;
                Normalize(ref cursor, ref occupied);
                UpdateBest();
                if (cursor >= _cellCount)
                {
                    return;
                }

                int remainingFreeCells = _availableSuffix[cursor] - occupied.PopCount;
                int upperBound = _current.Count + remainingFreeCells / _dishCellCount;
                if (upperBound <= _best.Count)
                {
                    return;
                }

                var state = new SearchState(cursor, occupied);
                if (_bestSeenAtState.TryGetValue(state, out int bestSeen)
                    && bestSeen >= _current.Count)
                {
                    return;
                }

                _bestSeenAtState[state] = _current.Count;

                List<Candidate> candidates = _candidatesByFirstCell[cursor];
                if (candidates != null)
                {
                    foreach (Candidate candidate in candidates)
                    {
                        if (occupied.Overlaps(candidate.Mask))
                        {
                            continue;
                        }

                        _current.Add(candidate.Placement);
                        Explore(
                            cursor + 1,
                            occupied.Or(candidate.Mask).Without(cursor));
                        _current.RemoveAt(_current.Count - 1);
                        if (_searchNodes >= SearchNodeLimit)
                        {
                            _truncated = true;
                            return;
                        }
                    }
                }

                Explore(cursor + 1, occupied);
            }

            private void UpdateBest()
            {
                if (_current.Count <= _best.Count)
                {
                    return;
                }

                _best = new List<Placement>(_current);
            }

            private Candidate BuildCandidate(Placement placement)
            {
                CellMask mask = CellMask.Empty;
                int firstCellIndex = int.MaxValue;
                foreach (GridPos local in placement.Orientation.Cells)
                {
                    GridPos cell = local.Offset(
                        placement.Origin.X,
                        placement.Origin.Y);
                    int index = cell.Y * _width + cell.X;
                    mask = mask.With(index);
                    firstCellIndex = Math.Min(firstCellIndex, index);
                }

                return new Candidate(placement, mask, firstCellIndex);
            }

            private void Normalize(ref int cursor, ref CellMask occupied)
            {
                while (cursor < _cellCount)
                {
                    if (!_available[cursor])
                    {
                        cursor++;
                        continue;
                    }

                    if (!occupied.Contains(cursor))
                    {
                        break;
                    }

                    occupied = occupied.Without(cursor);
                    cursor++;
                }
            }
        }

        private static int ComparePlacements(Placement left, Placement right)
        {
            int y = left.Origin.Y.CompareTo(right.Origin.Y);
            if (y != 0)
            {
                return y;
            }

            int x = left.Origin.X.CompareTo(right.Origin.X);
            return x != 0
                ? x
                : left.RotationIndex.CompareTo(right.RotationIndex);
        }

        private readonly struct Candidate
        {
            public Candidate(Placement placement, CellMask mask, int firstCellIndex)
            {
                Placement = placement;
                Mask = mask;
                FirstCellIndex = firstCellIndex;
            }

            public Placement Placement { get; }

            public CellMask Mask { get; }

            public int FirstCellIndex { get; }
        }

        private readonly struct SearchState : IEquatable<SearchState>
        {
            public SearchState(int cursor, CellMask occupied)
            {
                Cursor = cursor;
                Occupied = occupied;
            }

            private int Cursor { get; }

            private CellMask Occupied { get; }

            public bool Equals(SearchState other)
                => Cursor == other.Cursor && Occupied.Equals(other.Occupied);

            public override bool Equals(object obj)
                => obj is SearchState other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Cursor * 397) ^ Occupied.GetHashCode();
                }
            }
        }

        private readonly struct CellMask : IEquatable<CellMask>
        {
            public const int Capacity = 192;

            public static readonly CellMask Empty = default;

            private readonly ulong _low;
            private readonly ulong _middle;
            private readonly ulong _high;

            private CellMask(ulong low, ulong middle, ulong high)
            {
                _low = low;
                _middle = middle;
                _high = high;
            }

            public int PopCount => CountBits(_low) + CountBits(_middle) + CountBits(_high);

            public bool Contains(int index)
            {
                ulong bit = 1UL << (index & 63);
                return index < 64
                    ? (_low & bit) != 0
                    : index < 128
                        ? (_middle & bit) != 0
                        : (_high & bit) != 0;
            }

            public bool Overlaps(CellMask other)
                => (_low & other._low) != 0
                    || (_middle & other._middle) != 0
                    || (_high & other._high) != 0;

            public CellMask Or(CellMask other)
                => new CellMask(
                    _low | other._low,
                    _middle | other._middle,
                    _high | other._high);

            public CellMask With(int index)
            {
                ulong bit = 1UL << (index & 63);
                return index < 64
                    ? new CellMask(_low | bit, _middle, _high)
                    : index < 128
                        ? new CellMask(_low, _middle | bit, _high)
                        : new CellMask(_low, _middle, _high | bit);
            }

            public CellMask Without(int index)
            {
                ulong bit = ~(1UL << (index & 63));
                return index < 64
                    ? new CellMask(_low & bit, _middle, _high)
                    : index < 128
                        ? new CellMask(_low, _middle & bit, _high)
                        : new CellMask(_low, _middle, _high & bit);
            }

            public bool Equals(CellMask other)
                => _low == other._low
                    && _middle == other._middle
                    && _high == other._high;

            public override bool Equals(object obj)
                => obj is CellMask other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _low.GetHashCode();
                    hash = (hash * 397) ^ _middle.GetHashCode();
                    return (hash * 397) ^ _high.GetHashCode();
                }
            }

            private static int CountBits(ulong value)
            {
                value -= (value >> 1) & 0x5555555555555555UL;
                value = (value & 0x3333333333333333UL)
                    + ((value >> 2) & 0x3333333333333333UL);
                return (int)(unchecked((value + (value >> 4))
                    & 0x0F0F0F0F0F0F0F0FUL) * 0x0101010101010101UL >> 56);
            }
        }
    }

    internal sealed class RepeatedDishPlacementResult
    {
        public RepeatedDishPlacementResult(
            IReadOnlyList<Placement> placements,
            bool truncated,
            int searchNodes)
        {
            Placements = placements ?? Array.Empty<Placement>();
            Truncated = truncated;
            SearchNodes = searchNodes;
        }

        public IReadOnlyList<Placement> Placements { get; }

        public bool Truncated { get; }

        public int SearchNodes { get; }
    }
}
