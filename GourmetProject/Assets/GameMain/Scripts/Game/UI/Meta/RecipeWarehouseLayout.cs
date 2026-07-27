using System;
using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 统一菜谱仓库的纯逻辑矩形排布器。
    /// 保持输入顺序，按行优先 first-fit 放置，不依赖任何场景对象。
    /// </summary>
    public static class RecipeWarehouseLayout
    {
        public readonly struct Placement
        {
            public Placement(Vector2Int position, Vector2Int size)
            {
                Position = position;
                Size = size;
            }

            public Vector2Int Position { get; }

            public Vector2Int Size { get; }
        }

        public sealed class Result
        {
            public Result(IReadOnlyList<Placement> placements, int columns, int rows)
            {
                Placements = placements ?? Array.Empty<Placement>();
                Columns = Mathf.Max(1, columns);
                Rows = Mathf.Max(1, rows);
            }

            public IReadOnlyList<Placement> Placements { get; }

            public int Columns { get; }

            public int Rows { get; }
        }

        public static Result Pack(IReadOnlyList<Vector2Int> itemSizes, float targetAspect)
        {
            if (itemSizes == null || itemSizes.Count == 0)
            {
                return new Result(Array.Empty<Placement>(), 1, 1);
            }

            int totalArea = 0;
            int widest = 1;
            var normalized = new Vector2Int[itemSizes.Count];
            for (int i = 0; i < itemSizes.Count; i++)
            {
                Vector2Int size = itemSizes[i];
                size.x = Mathf.Max(1, size.x);
                size.y = Mathf.Max(1, size.y);
                normalized[i] = size;
                widest = Mathf.Max(widest, size.x);
                totalArea += size.x * size.y;
            }

            float aspect = Mathf.Clamp(targetAspect, 0.25f, 4f);
            int columns = Mathf.Max(
                widest,
                Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, totalArea) * aspect)));
            var occupied = new List<bool[]>();
            var placements = new Placement[normalized.Length];
            int usedColumns = 0;
            int usedRows = 0;

            for (int i = 0; i < normalized.Length; i++)
            {
                Vector2Int size = normalized[i];
                Vector2Int position = FindFirstFree(occupied, columns, size);
                MarkOccupied(occupied, columns, position, size);
                placements[i] = new Placement(position, size);
                usedColumns = Mathf.Max(usedColumns, position.x + size.x);
                usedRows = Mathf.Max(usedRows, position.y + size.y);
            }

            return new Result(
                placements,
                Mathf.Max(columns, usedColumns),
                Mathf.Max(1, usedRows));
        }

        private static Vector2Int FindFirstFree(
            List<bool[]> occupied,
            int columns,
            Vector2Int size)
        {
            for (int y = 0;; y++)
            {
                EnsureRows(occupied, columns, y + size.y);
                for (int x = 0; x <= columns - size.x; x++)
                {
                    if (CanPlace(occupied, x, y, size))
                    {
                        return new Vector2Int(x, y);
                    }
                }
            }
        }

        private static bool CanPlace(
            IReadOnlyList<bool[]> occupied,
            int startX,
            int startY,
            Vector2Int size)
        {
            for (int y = startY; y < startY + size.y; y++)
            {
                bool[] row = occupied[y];
                for (int x = startX; x < startX + size.x; x++)
                {
                    if (row[x])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static void MarkOccupied(
            List<bool[]> occupied,
            int columns,
            Vector2Int position,
            Vector2Int size)
        {
            EnsureRows(occupied, columns, position.y + size.y);
            for (int y = position.y; y < position.y + size.y; y++)
            {
                bool[] row = occupied[y];
                for (int x = position.x; x < position.x + size.x; x++)
                {
                    row[x] = true;
                }
            }
        }

        private static void EnsureRows(List<bool[]> occupied, int columns, int count)
        {
            while (occupied.Count < count)
            {
                occupied.Add(new bool[columns]);
            }
        }
    }
}
