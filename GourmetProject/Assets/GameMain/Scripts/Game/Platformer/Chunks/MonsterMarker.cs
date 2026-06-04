using UnityEngine;

namespace GourmetProject.Game.Platformer.Chunks
{
    /// <summary>
    /// 怪物摆放标记（挂在预制段内的空 GameObject 上）。运行时由 <see cref="LevelAssembler"/> 读取，
    /// 把世界坐标 + 种类写入 <see cref="LevelData.Spawns"/>，再由 MonsterManager 实例化。
    /// 取代旧的程序化 SpawnRules：怪物直接摆在预制段里。
    /// </summary>
    public sealed class MonsterMarker : MonoBehaviour
    {
        [Tooltip("该标记生成的怪物种类")]
        public MonsterKind Kind = MonsterKind.Mosquito;
    }
}
