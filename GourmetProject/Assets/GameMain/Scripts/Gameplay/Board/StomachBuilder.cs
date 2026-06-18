using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Board
{
    /// <summary>
    /// 由胃部碎片定义构建棋盘（胃）。直接按字符行解析存在格、按局部 (x,y) 落位格标签，
    /// 不走 DishShape 归一化，保证坐标与配置一一对应。本期只构建「初始胃」，扩胃留待后续。
    /// </summary>
    public static class StomachBuilder
    {
        /// <summary>
        /// 用初始碎片在 maxWidth×maxHeight 包围盒内构建棋盘，碎片左上角对齐到 <paramref name="origin"/>（默认 0,0）。
        /// 超出包围盒的格会被忽略。
        /// </summary>
        public static Board BuildInitial(StomachFragmentDef fragment, int maxWidth, int maxHeight, GridPos origin = default)
        {
            if (fragment == null)
            {
                throw new ArgumentNullException(nameof(fragment));
            }

            if (maxWidth <= 0 || maxHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxWidth), "Stomach max size must be positive.");
            }

            var existing = new List<GridPos>();
            IReadOnlyList<string> rows = fragment.ShapeRows;
            for (int y = 0; y < rows.Count; y++)
            {
                string row = rows[y] ?? string.Empty;
                for (int x = 0; x < row.Length; x++)
                {
                    if (IsFilled(row[x]))
                    {
                        existing.Add(new GridPos(origin.X + x, origin.Y + y));
                    }
                }
            }

            Dictionary<GridPos, IReadOnlyList<string>> cellTags = null;
            foreach (CellTag ct in fragment.CellTags)
            {
                if (string.IsNullOrEmpty(ct.TagId))
                {
                    continue;
                }

                var pos = new GridPos(origin.X + ct.Pos.X, origin.Y + ct.Pos.Y);
                cellTags ??= new Dictionary<GridPos, IReadOnlyList<string>>();
                if (!cellTags.TryGetValue(pos, out IReadOnlyList<string> list))
                {
                    list = new List<string>();
                    cellTags[pos] = list;
                }

                ((List<string>)list).Add(ct.TagId);
            }

            return new Board(maxWidth, maxHeight, existing, cellTags);
        }

        public static Board BuildExpanded(
            StomachFragmentDef initial,
            IEnumerable<StomachFragmentDef> extraFragments,
            int maxWidth,
            int maxHeight)
        {
            if (initial == null)
            {
                throw new ArgumentNullException(nameof(initial));
            }

            var existing = new HashSet<GridPos>();
            var cellTags = new Dictionary<GridPos, IReadOnlyList<string>>();
            AddFragmentCells(initial, new GridPos(0, 0), maxWidth, maxHeight, existing, cellTags, clipToBounds: true);

            if (extraFragments != null)
            {
                foreach (StomachFragmentDef fragment in extraFragments)
                {
                    if (fragment == null || !TryFindAttachment(existing, fragment, maxWidth, maxHeight, out GridPos origin))
                    {
                        continue;
                    }

                    AddFragmentCells(fragment, origin, maxWidth, maxHeight, existing, cellTags, clipToBounds: false);
                }
            }

            return new Board(maxWidth, maxHeight, existing, cellTags);
        }

        public static bool CanAttachFragment(
            StomachFragmentDef initial,
            IEnumerable<StomachFragmentDef> existingFragments,
            StomachFragmentDef candidate,
            int maxWidth,
            int maxHeight)
        {
            if (initial == null || candidate == null || maxWidth <= 0 || maxHeight <= 0)
            {
                return false;
            }

            var existing = new HashSet<GridPos>();
            AddFragmentCells(initial, new GridPos(0, 0), maxWidth, maxHeight, existing, null, clipToBounds: true);
            if (existingFragments != null)
            {
                foreach (StomachFragmentDef fragment in existingFragments)
                {
                    if (fragment == null || !TryFindAttachment(existing, fragment, maxWidth, maxHeight, out GridPos origin))
                    {
                        continue;
                    }

                    AddFragmentCells(fragment, origin, maxWidth, maxHeight, existing, null, clipToBounds: false);
                }
            }

            return TryFindAttachment(existing, candidate, maxWidth, maxHeight, out _);
        }

        private static void AddFragmentCells(
            StomachFragmentDef fragment,
            GridPos origin,
            int maxWidth,
            int maxHeight,
            HashSet<GridPos> existing,
            Dictionary<GridPos, IReadOnlyList<string>> cellTags,
            bool clipToBounds)
        {
            foreach (GridPos local in FilledCells(fragment))
            {
                GridPos pos = local.Offset(origin.X, origin.Y);
                if (pos.X < 0 || pos.Y < 0 || pos.X >= maxWidth || pos.Y >= maxHeight)
                {
                    if (clipToBounds)
                    {
                        continue;
                    }

                    return;
                }

                existing.Add(pos);
            }

            if (cellTags == null)
            {
                return;
            }

            foreach (CellTag ct in fragment.CellTags)
            {
                if (string.IsNullOrEmpty(ct.TagId))
                {
                    continue;
                }

                GridPos pos = ct.Pos.Offset(origin.X, origin.Y);
                if (!existing.Contains(pos))
                {
                    continue;
                }

                if (!cellTags.TryGetValue(pos, out IReadOnlyList<string> list))
                {
                    list = new List<string>();
                    cellTags[pos] = list;
                }

                ((List<string>)list).Add(ct.TagId);
            }
        }

        private static bool TryFindAttachment(
            HashSet<GridPos> existing,
            StomachFragmentDef fragment,
            int maxWidth,
            int maxHeight,
            out GridPos origin)
        {
            origin = default;
            List<GridPos> cells = FilledCells(fragment);
            if (existing == null || existing.Count == 0 || cells.Count == 0)
            {
                return false;
            }

            for (int y = 0; y < maxHeight; y++)
            {
                for (int x = 0; x < maxWidth; x++)
                {
                    var candidateOrigin = new GridPos(x, y);
                    if (CanPlaceAt(existing, cells, candidateOrigin, maxWidth, maxHeight))
                    {
                        origin = candidateOrigin;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool CanPlaceAt(
            HashSet<GridPos> existing,
            IReadOnlyList<GridPos> cells,
            GridPos origin,
            int maxWidth,
            int maxHeight)
        {
            bool touchesExisting = false;
            foreach (GridPos local in cells)
            {
                GridPos pos = local.Offset(origin.X, origin.Y);
                if (pos.X < 0 || pos.Y < 0 || pos.X >= maxWidth || pos.Y >= maxHeight || existing.Contains(pos))
                {
                    return false;
                }

                if (TouchesExisting(existing, pos))
                {
                    touchesExisting = true;
                }
            }

            return touchesExisting;
        }

        private static bool TouchesExisting(HashSet<GridPos> existing, GridPos pos)
        {
            return existing.Contains(pos.Offset(1, 0))
                || existing.Contains(pos.Offset(-1, 0))
                || existing.Contains(pos.Offset(0, 1))
                || existing.Contains(pos.Offset(0, -1));
        }

        private static List<GridPos> FilledCells(StomachFragmentDef fragment)
        {
            var cells = new List<GridPos>();
            IReadOnlyList<string> rows = fragment.ShapeRows;
            for (int y = 0; y < rows.Count; y++)
            {
                string row = rows[y] ?? string.Empty;
                for (int x = 0; x < row.Length; x++)
                {
                    if (IsFilled(row[x]))
                    {
                        cells.Add(new GridPos(x, y));
                    }
                }
            }

            return cells;
        }

        private static bool IsFilled(char c) => c == 'X' || c == 'x' || c == '1' || c == '#';
    }
}
