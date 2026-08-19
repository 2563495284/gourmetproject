using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Board
{
    /// <summary>
    /// 由餐桌格定义构建餐桌（胃）。直接按字符行解析存在格，
    /// 不走 DishShape 归一化，保证坐标与配置一一对应。
    /// </summary>
    public static class TableFragmentBuilder
    {
        public enum FragmentPlacementStatus
        {
            Valid,
            OutOfBounds,
            Overlap,
            Detached,
        }

        public readonly struct PlacementBounds
        {
            public PlacementBounds(int minX, int minY, int maxX, int maxY)
            {
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
            }

            public int MinX { get; }

            public int MinY { get; }

            public int MaxX { get; }

            public int MaxY { get; }

            public int Width => MaxX - MinX + 1;

            public int Height => MaxY - MinY + 1;

            public bool Contains(GridPos pos) => pos.X >= MinX && pos.X <= MaxX && pos.Y >= MinY && pos.Y <= MaxY;
        }

        /// <summary>
        /// 用初始碎片在 maxWidth×maxHeight 包围盒内构建餐桌，碎片左上角对齐到 <paramref name="origin"/>（默认 0,0）。
        /// 超出包围盒的格会被忽略。
        /// </summary>
        public static DiningTable BuildInitial(TableFragmentDef fragment, int maxWidth, int maxHeight, GridPos origin = default)
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

            return new DiningTable(maxWidth, maxHeight, existing);
        }

        /// <summary>计算把碎片实际占格包围盒居中放进最大包围盒时的左上原点。</summary>
        public static GridPos CenteredOrigin(TableFragmentDef fragment, int maxWidth, int maxHeight)
        {
            if (fragment == null)
            {
                throw new ArgumentNullException(nameof(fragment));
            }

            if (maxWidth <= 0 || maxHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxWidth), "Stomach max size must be positive.");
            }

            List<GridPos> cells = FilledCells(fragment);
            if (cells.Count == 0)
            {
                return default;
            }

            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;
            foreach (GridPos cell in cells)
            {
                if (cell.X < minX) minX = cell.X;
                if (cell.Y < minY) minY = cell.Y;
                if (cell.X > maxX) maxX = cell.X;
                if (cell.Y > maxY) maxY = cell.Y;
            }

            int boxW = maxX - minX + 1;
            int boxH = maxY - minY + 1;
            int x = Math.Max(0, (maxWidth - boxW) / 2) - minX;
            int y = Math.Max(0, (maxHeight - boxH) / 2) - minY;
            return new GridPos(x, y);
        }

        public static DiningTable BuildExpanded(
            TableFragmentDef initial,
            IEnumerable<TableFragmentDef> extraFragments,
            int maxWidth,
            int maxHeight)
        {
            if (initial == null)
            {
                throw new ArgumentNullException(nameof(initial));
            }

            var existing = new HashSet<GridPos>();
            AddFragmentCells(initial, new GridPos(0, 0), maxWidth, maxHeight, existing, clipToBounds: true);

            if (extraFragments != null)
            {
                foreach (TableFragmentDef fragment in extraFragments)
                {
                    if (fragment == null || !TryFindAttachment(existing, fragment, maxWidth, maxHeight, out GridPos origin))
                    {
                        continue;
                    }

                    AddFragmentCells(fragment, origin, maxWidth, maxHeight, existing, clipToBounds: false);
                }
            }

            return new DiningTable(maxWidth, maxHeight, existing);
        }

        /// <summary>
        /// 由「初始碎片 + 玩家手动拼贴的放置列表」重建餐桌。等价于无自动附着碎片的 <see cref="BuildFromExpanded"/>。
        /// </summary>
        public static DiningTable BuildFromPlacements(
            TableFragmentDef initial,
            IEnumerable<TableFragmentPlacement> placements,
            Func<string, TableFragmentDef> lookup,
            int maxWidth,
            int maxHeight)
        {
            return BuildFromExpanded(initial, null, placements, lookup, maxWidth, maxHeight);
        }

        /// <summary>
        /// 统一造盘：初始碎片（裁到包围盒） + 自动附着碎片（奖励获得，扫描第一个合法附着点） + 玩家手动拼贴放置（存档的 origin/rotation）。
        /// 自动附着与手动放置都只接受合法（在界内、贴边相邻、不重叠）的落格，非法项跳过以容错。
        /// </summary>
        public static DiningTable BuildFromExpanded(
            TableFragmentDef initial,
            IEnumerable<TableFragmentDef> autoFragments,
            IEnumerable<TableFragmentPlacement> placements,
            Func<string, TableFragmentDef> lookup,
            int maxWidth,
            int maxHeight,
            GridPos initialOrigin = default)
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
            AddFragmentCells(initial, initialOrigin, maxWidth, maxHeight, existing, clipToBounds: true);

            if (autoFragments != null)
            {
                foreach (TableFragmentDef fragment in autoFragments)
                {
                    if (fragment == null || !TryFindAttachment(existing, fragment, maxWidth, maxHeight, out GridPos origin))
                    {
                        continue;
                    }

                    AddFragmentCells(fragment, origin, maxWidth, maxHeight, existing, clipToBounds: false);
                }
            }

            if (placements != null && lookup != null)
            {
                foreach (TableFragmentPlacement placement in placements)
                {
                    TableFragmentDef def = lookup(placement.FragmentId);
                    if (def == null)
                    {
                        continue;
                    }

                    TableFragmentDef rotated = def.Rotated(placement.Rotation);
                    if (!CanPlaceFragmentAt(existing, rotated, 0, placement.Origin, maxWidth, maxHeight))
                    {
                        continue;
                    }

                    AddFragmentCells(rotated, placement.Origin, maxWidth, maxHeight, existing, clipToBounds: false);
                }
            }

            return new DiningTable(maxWidth, maxHeight, existing);
        }

        /// <summary>
        /// 运行期局部坐标造盘：最大胃尺寸只约束当前胃形的局部包围框，DiningTable 画布可更大以容纳负向扩展。
        /// 玩家拼贴碎片使用存档中的 rotation 重建，保证最终餐桌与编辑期预览方向一致。
        /// </summary>
        public static DiningTable BuildFromExpandedLocalBounds(
            TableFragmentDef initial,
            IEnumerable<TableFragmentDef> autoFragments,
            IEnumerable<TableFragmentPlacement> placements,
            Func<string, TableFragmentDef> lookup,
            int maxWidth,
            int maxHeight,
            int canvasWidth,
            int canvasHeight,
            GridPos initialOrigin)
        {
            if (initial == null)
            {
                throw new ArgumentNullException(nameof(initial));
            }
            if (maxWidth <= 0 || maxHeight <= 0 || canvasWidth <= 0 || canvasHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxWidth), "Stomach and canvas sizes must be positive.");
            }

            var existing = new HashSet<GridPos>();
            AddFragmentCells(initial, initialOrigin, canvasWidth, canvasHeight, existing, clipToBounds: true);

            if (autoFragments != null)
            {
                foreach (TableFragmentDef fragment in autoFragments)
                {
                    if (fragment == null || !TryFindAttachmentLocal(existing, fragment, maxWidth, maxHeight, out GridPos origin))
                    {
                        continue;
                    }

                    AddFragmentCells(fragment, origin, canvasWidth, canvasHeight, existing, clipToBounds: false);
                }
            }

            if (placements != null && lookup != null)
            {
                foreach (TableFragmentPlacement placement in placements)
                {
                    TableFragmentDef def = lookup(placement.FragmentId);
                    if (def == null)
                    {
                        continue;
                    }

                    TableFragmentDef rotated = def.Rotated(placement.Rotation);
                    if (GetFragmentPlacementStatusWithinMaxBounds(existing, rotated, placement.Origin, maxWidth, maxHeight) != FragmentPlacementStatus.Valid)
                    {
                        continue;
                    }

                    AddFragmentCells(rotated, placement.Origin, canvasWidth, canvasHeight, existing, clipToBounds: false);
                }
            }

            return new DiningTable(canvasWidth, canvasHeight, existing);
        }

        /// <summary>判断某碎片按 <paramref name="rotation"/> 旋转后能否放在以 <paramref name="origin"/> 为左上的位置（在界内、不重叠、且贴边相邻已有餐桌）。</summary>
        public static bool CanPlaceFragmentAt(
            HashSet<GridPos> existing,
            TableFragmentDef fragment,
            int rotation,
            GridPos origin,
            int maxWidth,
            int maxHeight)
        {
            if (existing == null || fragment == null || maxWidth <= 0 || maxHeight <= 0)
            {
                return false;
            }

            TableFragmentDef rotated = rotation == 0 ? fragment : fragment.Rotated(rotation);
            List<GridPos> cells = FilledCells(rotated);
            return CanPlaceAt(existing, cells, origin, new PlacementBounds(0, 0, maxWidth - 1, maxHeight - 1));
        }

        public static FragmentPlacementStatus GetFragmentPlacementStatus(
            HashSet<GridPos> existing,
            TableFragmentDef fragment,
            GridPos origin,
            PlacementBounds bounds)
        {
            if (existing == null || fragment == null)
            {
                return FragmentPlacementStatus.OutOfBounds;
            }

            return GetPlacementStatus(existing, FilledCells(fragment), origin, bounds);
        }

        public static FragmentPlacementStatus GetFragmentPlacementStatusWithinMaxBounds(
            HashSet<GridPos> existing,
            TableFragmentDef fragment,
            GridPos origin,
            int maxWidth,
            int maxHeight)
        {
            if (existing == null || fragment == null || maxWidth <= 0 || maxHeight <= 0)
            {
                return FragmentPlacementStatus.OutOfBounds;
            }

            return GetPlacementStatusWithinMaxBounds(existing, FilledCells(fragment), origin, maxWidth, maxHeight);
        }

        public static PlacementBounds CenteredBounds(DiningTable board, int maxWidth, int maxHeight)
        {
            return CenteredBounds(ToExistingSet(board), maxWidth, maxHeight);
        }

        public static PlacementBounds CenteredBounds(HashSet<GridPos> existing, int maxWidth, int maxHeight)
        {
            if (maxWidth <= 0 || maxHeight <= 0 || existing == null || existing.Count == 0)
            {
                return new PlacementBounds(0, 0, Math.Max(0, maxWidth - 1), Math.Max(0, maxHeight - 1));
            }

            ExistingBounds(existing, out int minX, out int minY, out int maxX, out int maxY);
            int width = maxX - minX + 1;
            int height = maxY - minY + 1;
            int slackX = Math.Max(0, maxWidth - width);
            int slackY = Math.Max(0, maxHeight - height);
            int boundMinX = minX - slackX / 2;
            int boundMinY = minY - slackY / 2;
            return new PlacementBounds(boundMinX, boundMinY, boundMinX + maxWidth - 1, boundMinY + maxHeight - 1);
        }

        /// <summary>该碎片是否在当前餐桌上存在任意合法放置（枚举朝向 × 原点）。allowRotate=true 时考虑 4 个朝向。</summary>
        public static bool CanAttachAnywhere(DiningTable board, TableFragmentDef fragment, bool allowRotate)
        {
            if (board == null || fragment == null)
            {
                return false;
            }

            HashSet<GridPos> existing = ToExistingSet(board);
            int rotations = allowRotate ? 4 : 1;
            for (int r = 0; r < rotations; r++)
            {
                TableFragmentDef rotated = r == 0 ? fragment : fragment.Rotated(r);
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

        /// <summary>基于当前餐桌存在格判断放置合法性（编辑期便捷重载）。</summary>
        public static bool CanPlaceFragmentAt(DiningTable board, TableFragmentDef fragment, int rotation, GridPos origin)
        {
            if (board == null || fragment == null)
            {
                return false;
            }

            return CanPlaceFragmentAt(ToExistingSet(board), fragment, rotation, origin, board.Width, board.Height);
        }

        /// <summary>把餐桌的「存在格」导出为集合，供放置判定复用。</summary>
        public static HashSet<GridPos> ToExistingSet(DiningTable board)
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
            TableFragmentDef initial,
            IEnumerable<TableFragmentDef> existingFragments,
            TableFragmentDef candidate,
            int maxWidth,
            int maxHeight)
        {
            if (initial == null || candidate == null || maxWidth <= 0 || maxHeight <= 0)
            {
                return false;
            }

            var existing = new HashSet<GridPos>();
            AddFragmentCells(initial, new GridPos(0, 0), maxWidth, maxHeight, existing, clipToBounds: true);
            if (existingFragments != null)
            {
                foreach (TableFragmentDef fragment in existingFragments)
                {
                    if (fragment == null || !TryFindAttachment(existing, fragment, maxWidth, maxHeight, out GridPos origin))
                    {
                        continue;
                    }

                    AddFragmentCells(fragment, origin, maxWidth, maxHeight, existing, clipToBounds: false);
                }
            }

            return TryFindAttachment(existing, candidate, maxWidth, maxHeight, out _);
        }

        public static bool CanAttachAnywhereLocalBounds(DiningTable board, TableFragmentDef fragment, int maxWidth, int maxHeight)
        {
            if (board == null || fragment == null)
            {
                return false;
            }

            HashSet<GridPos> existing = ToExistingSet(board);
            return TryFindAttachmentLocal(existing, fragment, maxWidth, maxHeight, out _);
        }

        /// <summary>
        /// 枚举该碎片在当前餐桌上可拼入的顺时针朝向（0..3）。
        /// 判定使用旋转后形状，不把未旋转定义当作唯一合法朝向。
        /// </summary>
        public static List<int> CollectAttachableRotations(
            DiningTable board,
            TableFragmentDef fragment,
            int maxWidth,
            int maxHeight)
        {
            var rotations = new List<int>(4);
            if (board == null || fragment == null)
            {
                return rotations;
            }

            for (int rotation = 0; rotation < 4; rotation++)
            {
                TableFragmentDef rotated = rotation == 0 ? fragment : fragment.Rotated(rotation);
                if (CanAttachAnywhereLocalBounds(board, rotated, maxWidth, maxHeight))
                {
                    rotations.Add(rotation);
                }
            }

            return rotations;
        }

        private static void AddFragmentCells(
            TableFragmentDef fragment,
            GridPos origin,
            int maxWidth,
            int maxHeight,
            HashSet<GridPos> existing,
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
        }

        private static bool TryFindAttachment(
            HashSet<GridPos> existing,
            TableFragmentDef fragment,
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

        private static bool TryFindAttachmentLocal(
            HashSet<GridPos> existing,
            TableFragmentDef fragment,
            int maxWidth,
            int maxHeight,
            out GridPos origin)
        {
            origin = default;
            if (existing == null || existing.Count == 0 || fragment == null)
            {
                return false;
            }

            ExistingBounds(existing, out int minX, out int minY, out int maxX, out int maxY);
            for (int y = minY - maxHeight; y <= maxY + maxHeight; y++)
            {
                for (int x = minX - maxWidth; x <= maxX + maxWidth; x++)
                {
                    var candidateOrigin = new GridPos(x, y);
                    if (GetFragmentPlacementStatusWithinMaxBounds(existing, fragment, candidateOrigin, maxWidth, maxHeight) == FragmentPlacementStatus.Valid)
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
            return CanPlaceAt(existing, cells, origin, new PlacementBounds(0, 0, maxWidth - 1, maxHeight - 1));
        }

        private static bool CanPlaceAt(
            HashSet<GridPos> existing,
            IReadOnlyList<GridPos> cells,
            GridPos origin,
            PlacementBounds bounds)
        {
            return GetPlacementStatus(existing, cells, origin, bounds) == FragmentPlacementStatus.Valid;
        }

        private static FragmentPlacementStatus GetPlacementStatus(
            HashSet<GridPos> existing,
            IReadOnlyList<GridPos> cells,
            GridPos origin,
            PlacementBounds bounds)
        {
            bool touchesExisting = false;
            foreach (GridPos local in cells)
            {
                GridPos pos = local.Offset(origin.X, origin.Y);
                if (!bounds.Contains(pos))
                {
                    return FragmentPlacementStatus.OutOfBounds;
                }

                if (existing.Contains(pos))
                {
                    return FragmentPlacementStatus.Overlap;
                }

                if (TouchesExisting(existing, pos))
                {
                    touchesExisting = true;
                }
            }

            return touchesExisting ? FragmentPlacementStatus.Valid : FragmentPlacementStatus.Detached;
        }

        private static FragmentPlacementStatus GetPlacementStatusWithinMaxBounds(
            HashSet<GridPos> existing,
            IReadOnlyList<GridPos> cells,
            GridPos origin,
            int maxWidth,
            int maxHeight)
        {
            if (existing == null || existing.Count == 0 || cells == null || cells.Count == 0)
            {
                return FragmentPlacementStatus.Detached;
            }

            ExistingBounds(existing, out int minX, out int minY, out int maxX, out int maxY);
            bool touchesExisting = false;
            foreach (GridPos local in cells)
            {
                GridPos pos = local.Offset(origin.X, origin.Y);
                if (existing.Contains(pos))
                {
                    return FragmentPlacementStatus.Overlap;
                }

                if (TouchesExisting(existing, pos))
                {
                    touchesExisting = true;
                }

                if (pos.X < minX) minX = pos.X;
                if (pos.Y < minY) minY = pos.Y;
                if (pos.X > maxX) maxX = pos.X;
                if (pos.Y > maxY) maxY = pos.Y;
            }

            if (!touchesExisting)
            {
                return FragmentPlacementStatus.Detached;
            }

            return maxX - minX + 1 <= maxWidth && maxY - minY + 1 <= maxHeight
                ? FragmentPlacementStatus.Valid
                : FragmentPlacementStatus.OutOfBounds;
        }

        private static void ExistingBounds(HashSet<GridPos> existing, out int minX, out int minY, out int maxX, out int maxY)
        {
            minX = minY = int.MaxValue;
            maxX = maxY = int.MinValue;
            foreach (GridPos pos in existing)
            {
                if (pos.X < minX) minX = pos.X;
                if (pos.Y < minY) minY = pos.Y;
                if (pos.X > maxX) maxX = pos.X;
                if (pos.Y > maxY) maxY = pos.Y;
            }
        }

        private static bool TouchesExisting(HashSet<GridPos> existing, GridPos pos)
        {
            return existing.Contains(pos.Offset(1, 0))
                || existing.Contains(pos.Offset(-1, 0))
                || existing.Contains(pos.Offset(0, 1))
                || existing.Contains(pos.Offset(0, -1));
        }

        /// <summary>解析碎片的存在格局部坐标列表（'X'/'x'/'1'/'#' 为占格）。供表现层渲染候选形状复用。</summary>
        public static List<GridPos> FilledCells(TableFragmentDef fragment)
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
