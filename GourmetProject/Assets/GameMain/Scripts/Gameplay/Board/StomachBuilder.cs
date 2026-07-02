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

        /// <summary>
        /// 由「初始碎片 + 玩家手动拼贴的放置列表」重建棋盘。等价于无自动附着碎片的 <see cref="BuildFromExpanded"/>。
        /// </summary>
        public static Board BuildFromPlacements(
            StomachFragmentDef initial,
            IEnumerable<StomachFragmentPlacement> placements,
            Func<string, StomachFragmentDef> lookup,
            int maxWidth,
            int maxHeight)
        {
            return BuildFromExpanded(initial, null, placements, lookup, maxWidth, maxHeight);
        }

        /// <summary>
        /// 统一造盘：初始碎片（裁到包围盒） + 自动附着碎片（奖励获得，扫描第一个合法附着点） + 玩家手动拼贴放置（存档的 origin/rotation）。
        /// 自动附着与手动放置都只接受合法（在界内、贴边相邻、不重叠）的落格，非法项跳过以容错。
        /// </summary>
        public static Board BuildFromExpanded(
            StomachFragmentDef initial,
            IEnumerable<StomachFragmentDef> autoFragments,
            IEnumerable<StomachFragmentPlacement> placements,
            Func<string, StomachFragmentDef> lookup,
            int maxWidth,
            int maxHeight)
        {
            if (initial == null)
            {
                throw new ArgumentNullException(nameof(initial));
            }

            if (maxWidth <= 0 || maxHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxWidth), "Stomach max size must be positive.");
            }

            var existing = new HashSet<GridPos>();
            var cellTags = new Dictionary<GridPos, IReadOnlyList<string>>();
            AddFragmentCells(initial, new GridPos(0, 0), maxWidth, maxHeight, existing, cellTags, clipToBounds: true);

            if (autoFragments != null)
            {
                foreach (StomachFragmentDef fragment in autoFragments)
                {
                    if (fragment == null || !TryFindAttachment(existing, fragment, maxWidth, maxHeight, out GridPos origin))
                    {
                        continue;
                    }

                    AddFragmentCells(fragment, origin, maxWidth, maxHeight, existing, cellTags, clipToBounds: false);
                }
            }

            if (placements != null && lookup != null)
            {
                foreach (StomachFragmentPlacement placement in placements)
                {
                    StomachFragmentDef def = lookup(placement.FragmentId);
                    if (def == null)
                    {
                        continue;
                    }

                    StomachFragmentDef rotated = def.Rotated(placement.Rotation);
                    if (!CanPlaceFragmentAt(existing, rotated, 0, placement.Origin, maxWidth, maxHeight))
                    {
                        continue;
                    }

                    AddFragmentCells(rotated, placement.Origin, maxWidth, maxHeight, existing, cellTags, clipToBounds: false);
                }
            }

            return new Board(maxWidth, maxHeight, existing, cellTags);
        }

        /// <summary>判断某碎片按 <paramref name="rotation"/> 旋转后能否放在以 <paramref name="origin"/> 为左上的位置（在界内、不重叠、且贴边相邻已有胃）。</summary>
        public static bool CanPlaceFragmentAt(
            HashSet<GridPos> existing,
            StomachFragmentDef fragment,
            int rotation,
            GridPos origin,
            int maxWidth,
            int maxHeight)
        {
            if (existing == null || fragment == null || maxWidth <= 0 || maxHeight <= 0)
            {
                return false;
            }

            StomachFragmentDef rotated = rotation == 0 ? fragment : fragment.Rotated(rotation);
            List<GridPos> cells = FilledCells(rotated);
            return CanPlaceAt(existing, cells, origin, maxWidth, maxHeight);
        }

        /// <summary>该碎片是否在当前棋盘上存在任意合法放置（枚举朝向 × 原点）。allowRotate=true 时考虑 4 个朝向。</summary>
        public static bool CanAttachAnywhere(Board board, StomachFragmentDef fragment, bool allowRotate)
        {
            if (board == null || fragment == null)
            {
                return false;
            }

            HashSet<GridPos> existing = ToExistingSet(board);
            int rotations = allowRotate ? 4 : 1;
            for (int r = 0; r < rotations; r++)
            {
                StomachFragmentDef rotated = r == 0 ? fragment : fragment.Rotated(r);
                for (int y = 0; y < board.Height; y++)
                {
                    for (int x = 0; x < board.Width; x++)
                    {
                        if (CanPlaceFragmentAt(existing, rotated, 0, new GridPos(x, y), board.Width, board.Height))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>基于当前棋盘存在格判断放置合法性（编辑期便捷重载）。</summary>
        public static bool CanPlaceFragmentAt(Board board, StomachFragmentDef fragment, int rotation, GridPos origin)
        {
            if (board == null || fragment == null)
            {
                return false;
            }

            return CanPlaceFragmentAt(ToExistingSet(board), fragment, rotation, origin, board.Width, board.Height);
        }

        /// <summary>把棋盘的「存在格」导出为集合，供放置判定复用。</summary>
        public static HashSet<GridPos> ToExistingSet(Board board)
        {
            var set = new HashSet<GridPos>();
            if (board == null)
            {
                return set;
            }

            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    var p = new GridPos(x, y);
                    if (board.Exists(p))
                    {
                        set.Add(p);
                    }
                }
            }

            return set;
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

        /// <summary>解析碎片的存在格局部坐标列表（'X'/'x'/'1'/'#' 为占格）。供表现层渲染候选形状复用。</summary>
        public static List<GridPos> FilledCells(StomachFragmentDef fragment)
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
