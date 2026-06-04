using UnityEngine;

namespace GourmetProject.Game.Platformer.Chunks
{
    /// <summary>
    /// [已废弃] 旧版怪物摆放标记。现在怪物由设计师把 Monster_*.prefab 直接摆进预制段，
    /// 运行时 <see cref="LevelAssembler"/> 收集 <see cref="Monsters.MonsterBase"/> 实例驱动。
    /// 保留此类仅为兼容旧预制段引用；AnimationAuthoring 的迁移会把旧 marker 替换成 prefab 实例。
    /// </summary>
    public sealed class MonsterMarker : MonoBehaviour
    {
        [Tooltip("该标记生成的怪物种类")]
        public MonsterKind Kind = MonsterKind.Mosquito;
    }
}
