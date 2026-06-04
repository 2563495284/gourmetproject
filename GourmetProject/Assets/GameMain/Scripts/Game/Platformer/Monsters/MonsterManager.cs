using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.Platformer.Monsters
{
    /// <summary>
    /// 怪物管理器：怪物由设计师以 Monster_*.prefab 直接摆进 chunk，关卡拼接时被 <see cref="Chunks.LevelAssembler"/>
    /// 收集为 <see cref="MonsterBase"/> 实例列表，这里只负责注册（统一 Init）与逐帧驱动，不再程序化实例化。
    /// </summary>
    public sealed class MonsterManager : MonoBehaviour
    {
        private readonly List<MonsterBase> _monsters = new List<MonsterBase>();

        public void Register(GameWorld world, IReadOnlyList<MonsterBase> monsters)
        {
            if (monsters == null) return;
            for (int i = 0; i < monsters.Count; i++)
            {
                MonsterBase m = monsters[i];
                if (m == null) continue;
                m.Init(world);
                _monsters.Add(m);
            }
        }

        public void Tick(float dt, Vector2 playerCenter, bool lighterOn, float visionRadius)
        {
            for (int i = 0; i < _monsters.Count; i++)
            {
                _monsters[i].Tick(dt, playerCenter, lighterOn, visionRadius);
            }
        }
    }
}
