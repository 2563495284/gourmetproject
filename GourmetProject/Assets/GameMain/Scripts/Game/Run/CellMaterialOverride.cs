using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.Run
{
    /// <summary>
    /// 玩家用「铺台小票」为某个餐桌格永久附加的材质覆盖。
    /// 拼桌时按坐标 append 进 DiningTable 材质表，与碎片自带材质叠加；入档持久化。
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
