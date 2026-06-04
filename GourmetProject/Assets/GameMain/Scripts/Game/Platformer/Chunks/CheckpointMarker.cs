using UnityEngine;

namespace GourmetProject.Game.Platformer.Chunks
{
    /// <summary>
    /// 检查点 / 终点摆放标记（挂在休息段或终点段内的空 GameObject 上）。运行时由
    /// <see cref="LevelAssembler"/> 读取写入 <see cref="LevelData.Checkpoints"/>，由 CheckpointSystem 接管视觉与触发。
    /// </summary>
    public sealed class CheckpointMarker : MonoBehaviour
    {
        [Tooltip("是否为终点（灯塔，触碰即胜利）。否则为普通检查点。")]
        public bool IsEndpoint = false;
    }
}
