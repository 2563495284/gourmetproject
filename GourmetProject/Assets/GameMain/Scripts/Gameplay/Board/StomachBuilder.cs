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

        private static bool IsFilled(char c) => c == 'X' || c == 'x' || c == '1' || c == '#';
    }
}
