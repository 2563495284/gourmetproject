using UnityEngine;

namespace GourmetProject.Game.Platformer.Chunks
{
    /// <summary>
    /// 玩家初始点标记（仅起点段需要一个）。标记位置为玩家碰撞盒「底边中心」的世界坐标，
    /// <see cref="LevelAssembler"/> 据此换算成左下角写入 <see cref="LevelData.StartPos"/>。
    /// </summary>
    public sealed class SpawnMarker : MonoBehaviour
    {
    }
}
