using GourmetProject.Gameplay.Model;
using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>棋盘逻辑坐标与场景世界坐标之间的统一映射。</summary>
    public sealed class BoardCoordinateMapper
    {
        public BoardCoordinateMapper(int width, int height, float cellSize, float gap, Vector3 center)
        {
            Width = width;
            Height = height;
            CellSize = cellSize;
            Gap = gap;
            Center = center;
        }

        public int Width { get; }

        public int Height { get; }

        public float CellSize { get; }

        public float Gap { get; }

        public float Pitch => CellSize + Gap;

        public Vector3 Center { get; }

        public float WorldWidth => Width * CellSize + Mathf.Max(0, Width - 1) * Gap;

        public float WorldHeight => Height * CellSize + Mathf.Max(0, Height - 1) * Gap;

        public Vector3 CellCenter(GridPos cell)
        {
            float left = Center.x - WorldWidth * 0.5f;
            float top = Center.y + WorldHeight * 0.5f;
            return new Vector3(
                left + cell.X * Pitch + CellSize * 0.5f,
                top - cell.Y * Pitch - CellSize * 0.5f,
                Center.z);
        }

        public GridPos NearestCell(Vector3 worldPosition)
        {
            float left = Center.x - WorldWidth * 0.5f;
            float top = Center.y + WorldHeight * 0.5f;
            int x = Mathf.RoundToInt((worldPosition.x - left - CellSize * 0.5f) / Pitch);
            int y = Mathf.RoundToInt((top - CellSize * 0.5f - worldPosition.y) / Pitch);
            return new GridPos(x, y);
        }
    }
}
