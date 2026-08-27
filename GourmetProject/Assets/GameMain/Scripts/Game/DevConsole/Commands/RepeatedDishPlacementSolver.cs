using System;
using System.Collections.Generic;
using System.Numerics;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.DevConsole.Commands
{
    /// <summary>
    /// 在当前餐桌空位中选择最大数量的同形状固定朝向摆位。
    /// 搜索只处理几何占格，不创建实例或触发任何玩法副作用。
    /// </summary>
    internal static class RepeatedDishPlacementSolver
    {
        public static IReadOnlyList<Placement> Solve(DiningTable table, DishDef dish)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            if (dish == null)
            {
                throw new ArgumentNullException(nameof(dish));
            }

            return new Search(table, dish).Solve();
        }

        private sealed class Search
        {
            private readonly int _width;
            private readonly int _cellCount;
            private readonly int _dishCellCount;
            private readonly bool[] _available;
            private readonly int[] _availableSuffix;
            private readonly List<Candidate>[] _candidatesByFirstCell;
            private readonly Dictionary<SearchState, int> _memo =
                new Dictionary<SearchState, int>();

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
                    _candidatesByFirstCell[candidate.FirstCellIndex] ??=
                        new List<Candidate>();
                    _candidatesByFirstCell[candidate.FirstCellIndex].Add(candidate);
                }
            }

            public IReadOnlyList<Placement> Solve()
            {
                if (_cellCount == 0 || _dishCellCount <= 0)
                {
                    return Array.Empty<Placement>();
                }

                int count = BestCount(0, BigInteger.Zero);
                if (count == 0)
                {
                    return Array.Empty<Placement>();
                }

                var result = new List<Placement>(count);
                BuildSolution(0, BigInteger.Zero, result);
                return result;
            }

            private int BestCount(int cursor, BigInteger occupied)
            {
                Normalize(ref cursor, ref occupied);
                if (cursor >= _cellCount)
                {
                    return 0;
                }

                var state = new SearchState(cursor, occupied);
                if (_memo.TryGetValue(state, out int cached))
                {
                    return cached;
                }

                int upperBound = RemainingFreeCellCount(cursor, occupied) / _dishCellCount;
                if (upperBound == 0)
                {
                    _memo[state] = 0;
                    return 0;
                }

                int best = 0;
                List<Candidate> candidates = _candidatesByFirstCell[cursor];
                if (candidates != null)
                {
                    BigInteger cursorBit = BigInteger.One << cursor;
                    foreach (Candidate candidate in candidates)
                    {
                        if ((occupied & candidate.Mask) != BigInteger.Zero)
                        {
                            continue;
                        }

                        BigInteger nextOccupied = (occupied | candidate.Mask) & ~cursorBit;
                        int candidateCount = 1 + BestCount(cursor + 1, nextOccupied);
                        if (candidateCount > best)
                        {
                            best = candidateCount;
                            if (best == upperBound)
                            {
                                _memo[state] = best;
                                return best;
                            }
                        }
                    }
                }

                best = Math.Max(best, BestCount(cursor + 1, occupied));
                _memo[state] = best;
                return best;
            }

            private void BuildSolution(
                int cursor,
                BigInteger occupied,
                List<Placement> result)
            {
                Normalize(ref cursor, ref occupied);
                if (cursor >= _cellCount)
                {
                    return;
                }

                int best = BestCount(cursor, occupied);
                if (best <= 0)
                {
                    return;
                }

                List<Candidate> candidates = _candidatesByFirstCell[cursor];
                if (candidates != null)
                {
                    BigInteger cursorBit = BigInteger.One << cursor;
                    foreach (Candidate candidate in candidates)
                    {
                        if ((occupied & candidate.Mask) != BigInteger.Zero)
                        {
                            continue;
                        }

                        BigInteger nextOccupied = (occupied | candidate.Mask) & ~cursorBit;
                        if (1 + BestCount(cursor + 1, nextOccupied) != best)
                        {
                            continue;
                        }

                        result.Add(candidate.Placement);
                        BuildSolution(cursor + 1, nextOccupied, result);
                        return;
                    }
                }

                BuildSolution(cursor + 1, occupied, result);
            }

            private Candidate BuildCandidate(Placement placement)
            {
                BigInteger mask = BigInteger.Zero;
                int firstCellIndex = int.MaxValue;
                foreach (GridPos local in placement.Orientation.Cells)
                {
                    GridPos cell = local.Offset(
                        placement.Origin.X,
                        placement.Origin.Y);
                    int index = cell.Y * _width + cell.X;
                    mask |= BigInteger.One << index;
                    firstCellIndex = Math.Min(firstCellIndex, index);
                }

                return new Candidate(placement, mask, firstCellIndex);
            }

            private void Normalize(ref int cursor, ref BigInteger occupied)
            {
                while (cursor < _cellCount)
                {
                    if (!_available[cursor])
                    {
                        cursor++;
                        continue;
                    }

                    BigInteger bit = BigInteger.One << cursor;
                    if ((occupied & bit) == BigInteger.Zero)
                    {
                        break;
                    }

                    occupied &= ~bit;
                    cursor++;
                }
            }

            private int RemainingFreeCellCount(int cursor, BigInteger occupied)
            {
                int count = _availableSuffix[cursor];
                for (int index = cursor; index < _cellCount; index++)
                {
                    if ((occupied & (BigInteger.One << index)) != BigInteger.Zero)
                    {
                        count--;
                    }
                }

                return count;
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
        }

        private readonly struct Candidate
        {
            public Candidate(Placement placement, BigInteger mask, int firstCellIndex)
            {
                Placement = placement;
                Mask = mask;
                FirstCellIndex = firstCellIndex;
            }

            public Placement Placement { get; }

            public BigInteger Mask { get; }

            public int FirstCellIndex { get; }
        }

        private readonly struct SearchState : IEquatable<SearchState>
        {
            public SearchState(int cursor, BigInteger occupied)
            {
                Cursor = cursor;
                Occupied = occupied;
            }

            private int Cursor { get; }

            private BigInteger Occupied { get; }

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
    }
}
