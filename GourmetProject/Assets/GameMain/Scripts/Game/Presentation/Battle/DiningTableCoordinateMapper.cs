using GourmetProject.Gameplay.Model;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 餐桌逻辑坐标与场景坐标之间的统一映射。
    /// 布局以 <see cref="Root"/> 的局部帧完成（完整 Width×Height 网格居中于局部原点），
    /// 世界坐标由 <see cref="Root"/> 的 position/scale 决定；因此改变 Root 变换即可整体摆放/居中/缩放餐桌。
    /// </summary>
    public sealed class DiningTableCoordinateMapper
    {
        public DiningTableCoordinateMapper(int width, int height, float cellSize, float gap, Transform root)
        {
            Configure(width, height, cellSize, gap, root);
        }

        internal void Configure(int width, int height, float cellSize, float gap, Transform root)
        {
            Width = width;
            Height = height;
            CellSize = cellSize;
            Gap = gap;
            Root = root;
        }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public float CellSize { get; private set; }

        public float Gap { get; private set; }

        /// <summary>餐桌局部帧的锚点（cells / pieces 均挂在其下，以 localPosition 摆放）。</summary>
        public Transform Root { get; private set; }

        public float Pitch => CellSize + Gap;

        public float WorldWidth => Width * CellSize + Mathf.Max(0, Width - 1) * Gap;

        public float WorldHeight => Height * CellSize + Mathf.Max(0, Height - 1) * Gap;

        /// <summary>餐桌几何中心的世界坐标（即局部原点经 Root 变换）；完整网格居中于局部原点。</summary>
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

        /// <summary>不依赖 Collider，直接计算一个格子的世界空间 AABB。</summary>
        public Bounds CellWorldBounds(GridPos cell)
        {
            Vector3 center = CellCenterLocal(cell);
            float half = CellSize * 0.5f;
            Vector3 bottomLeft = TransformLocalPoint(center + new Vector3(-half, -half, 0f));
            Vector3 topLeft = TransformLocalPoint(center + new Vector3(-half, half, 0f));
            Vector3 topRight = TransformLocalPoint(center + new Vector3(half, half, 0f));
            Vector3 bottomRight = TransformLocalPoint(center + new Vector3(half, -half, 0f));
            var bounds = new Bounds(bottomLeft, Vector3.zero);
            bounds.Encapsulate(topLeft);
            bounds.Encapsulate(topRight);
            bounds.Encapsulate(bottomRight);
            return bounds;
        }

        /// <summary>按格子的完整点击范围判断世界点命中，语义等同原先的 BoxCollider2D。</summary>
        public bool ContainsWorldPoint(GridPos cell, Vector3 worldPoint)
        {
            Vector3 local = Root != null ? Root.InverseTransformPoint(worldPoint) : worldPoint;
            Vector3 center = CellCenterLocal(cell);
            float half = CellSize * 0.5f;
            return local.x >= center.x - half
                && local.x <= center.x + half
                && local.y >= center.y - half
                && local.y <= center.y + half;
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

        private Vector3 TransformLocalPoint(Vector3 local)
        {
            return Root != null ? Root.TransformPoint(local) : local;
        }
    }
}
