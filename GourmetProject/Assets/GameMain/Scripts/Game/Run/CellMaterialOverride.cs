using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.Run
{
    /// <summary>
    /// 玩家用「铺台小票」为某个餐桌格设置的永久材质覆盖。
    /// 每个坐标最多一条，拼桌时替换碎片自带材质；入档持久化。
    /// </summary>
    public readonly struct CellMaterialOverride
    {
        public CellMaterialOverride(GridPos pos, string materialId)
        {
            Pos = pos;
            MaterialId = materialId ?? string.Empty;
        }

        public GridPos Pos { get; }

        public string MaterialId { get; }
    }
}
