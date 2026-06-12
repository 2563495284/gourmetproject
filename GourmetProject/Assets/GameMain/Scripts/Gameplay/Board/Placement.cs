using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Board
{
    /// <summary>
    /// 一次合法摆放：选定的朝向形状 + 左上原点格。绝对占格 = 形状各格 + 原点。
    /// </summary>
    public readonly struct Placement
    {
        public Placement(DishShape orientation, int rotationIndex, GridPos origin)
        {
            Orientation = orientation;
            RotationIndex = rotationIndex;
            Origin = origin;
        }

        public DishShape Orientation { get; }

        /// <summary>朝向序号（0=原始，1/2/3=顺时针旋转次数）。</summary>
        public int RotationIndex { get; }

        public GridPos Origin { get; }
    }
}
