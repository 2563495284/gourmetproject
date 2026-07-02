using System;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Board
{
    /// <summary>
    /// 玩家手动拼贴的一块胃部碎片放置：碎片 id + 顺时针旋转次数(0..3) + 左上原点格。
    /// 用于存档与重建棋盘，保证拼贴结果可复现。
    /// </summary>
    [Serializable]
    public readonly struct StomachFragmentPlacement : IEquatable<StomachFragmentPlacement>
    {
        public StomachFragmentPlacement(string fragmentId, int rotation, GridPos origin)
        {
            FragmentId = fragmentId;
            Rotation = ((rotation % 4) + 4) % 4;
            Origin = origin;
        }

        public string FragmentId { get; }

        /// <summary>顺时针旋转次数，归一化到 0..3。</summary>
        public int Rotation { get; }

        public GridPos Origin { get; }

        public bool Equals(StomachFragmentPlacement other) =>
            FragmentId == other.FragmentId && Rotation == other.Rotation && Origin.Equals(other.Origin);

        public override bool Equals(object obj) => obj is StomachFragmentPlacement other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = FragmentId != null ? FragmentId.GetHashCode() : 0;
                hash = (hash * 397) ^ Rotation;
                hash = (hash * 397) ^ Origin.GetHashCode();
                return hash;
            }
        }

        public override string ToString() => $"{FragmentId}@{Origin} r{Rotation}";
    }
}
