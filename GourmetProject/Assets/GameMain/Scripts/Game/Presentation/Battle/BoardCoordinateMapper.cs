using GourmetProject.Gameplay.Model;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 棋盘逻辑坐标与场景坐标之间的统一映射。
    /// 布局以 <see cref="Root"/> 的局部帧完成（完整 Width×Height 网格居中于局部原点），
    /// 世界坐标由 <see cref="Root"/> 的 position/scale 决定；因此改变 Root 变换即可整体摆放/居中/缩放棋盘。
    /// </summary>
    public sealed class BoardCoordinateMapper
    {
        public BoardCoordinateMapper(int width, int height, float cellSize, float gap, Transform root)
        {
            Width = width;
            Height = height;
            CellSize = cellSize;
            Gap = gap;
            Root = root;
        }

        public int Width { get; }

        public int Height { get; }

        public float CellSize { get; }

        public float Gap { get; }

        /// <summary>棋盘局部帧的锚点（cells / pieces 均挂在其下，以 localPosition 摆放）。</summary>
        public Transform Root { get; }

        public float Pitch => CellSize + Gap;

        public float WorldWidth => Width * CellSize + Mathf.Max(0, Width - 1) * Gap;

        public float WorldHeight => Height * CellSize + Mathf.Max(0, Height - 1) * Gap;

        /// <summary>棋盘几何中心的世界坐标（即局部原点经 Root 变换）；完整网格居中于局部原点。</summary>
        public Vector3 Center => Root != null ? Root.position : Vector3.zero;

        /// <summary>格中心的「局部坐标」：完整 Width×Height 网格居中于局部原点，Y 行号向下、Unity Y 向上故取反。</summary>
        public Vector3 CellCenterLocal(GridPos cell)
        {
            float left = -WorldWidth * 0.5f;
            float top = WorldHeight * 0.5f;
            return new Vector3(
                left + cell.X * Pitch + CellSize * 0.5f,
                top - cell.Y * Pitch - CellSize * 0.5f,
                0f);
        }

        /// <summary>格中心的世界坐标（局部坐标经 Root 变换）。</summary>
        public Vector3 CellCenter(GridPos cell)
        {
            Vector3 local = CellCenterLocal(cell);
            return Root != null ? Root.TransformPoint(local) : local;
        }

        /// <summary>把世界坐标换算到最近的逻辑格（先反变换到局部帧）。</summary>
        public GridPos NearestCell(Vector3 worldPosition)
        {
            Vector3 local = Root != null ? Root.InverseTransformPoint(worldPosition) : worldPosition;
            float left = -WorldWidth * 0.5f;
            float top = WorldHeight * 0.5f;
            int x = Mathf.RoundToInt((local.x - left - CellSize * 0.5f) / Pitch);
            int y = Mathf.RoundToInt((top - CellSize * 0.5f - local.y) / Pitch);
            return new GridPos(x, y);
        }
    }
}
