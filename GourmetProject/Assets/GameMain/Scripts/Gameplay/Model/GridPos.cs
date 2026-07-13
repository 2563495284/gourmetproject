using System;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 餐桌格坐标（列 X、行 Y，原点在左上，Y 向下递增）。纯 C# 结构，避免依赖 UnityEngine.Vector2Int。
    /// </summary>
    [Serializable]
    public readonly struct GridPos : IEquatable<GridPos>
    {
        public readonly int X;
        public readonly int Y;

        public GridPos(int x, int y)
        {
            X = x;
            Y = y;
        }

        public GridPos Offset(int dx, int dy) => new GridPos(X + dx, Y + dy);

        public bool Equals(GridPos other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is GridPos other && Equals(other);

        public override int GetHashCode() => unchecked((X * 397) ^ Y);

        public override string ToString() => $"({X},{Y})";
    }
}
