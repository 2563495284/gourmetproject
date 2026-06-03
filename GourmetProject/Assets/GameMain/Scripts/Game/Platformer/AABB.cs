using UnityEngine;

namespace GourmetProject.Game.Platformer
{
    /// <summary>
    /// 轴对齐矩形（世界单位）。以左下角 (MinX, MinY) + 宽高描述，用于自定义分轴扫描碰撞。
    /// </summary>
    public struct AABB
    {
        public float MinX;
        public float MinY;
        public float Width;
        public float Height;

        public AABB(float minX, float minY, float width, float height)
        {
            MinX = minX;
            MinY = minY;
            Width = width;
            Height = height;
        }

        public float MaxX => MinX + Width;
        public float MaxY => MinY + Height;
        public float CenterX => MinX + Width * 0.5f;
        public float CenterY => MinY + Height * 0.5f;
        public Vector2 Center => new Vector2(CenterX, CenterY);

        public bool Overlaps(in AABB other)
        {
            return MinX < other.MaxX && MaxX > other.MinX &&
                   MinY < other.MaxY && MaxY > other.MinY;
        }

        public bool Contains(Vector2 p)
        {
            return p.x >= MinX && p.x <= MaxX && p.y >= MinY && p.y <= MaxY;
        }
    }
}
