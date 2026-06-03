using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.Platformer.Monsters
{
    /// <summary>
    /// 怪物管理器：按关卡 spawn 点生成怪物并统一驱动。首版支持蚊群与暗影，其余种类可在此扩展。
    /// </summary>
    public sealed class MonsterManager : MonoBehaviour
    {
        private readonly List<MonsterBase> _monsters = new List<MonsterBase>();
        private GameWorld _world;

        public void Build(GameWorld world, LevelData data)
        {
            _world = world;
            foreach (MonsterSpawn spawn in data.Spawns)
            {
                var go = new GameObject($"Monster_{spawn.Kind}");
                go.transform.SetParent(transform, false);

                MonsterBase monster = spawn.Kind switch
                {
                    MonsterKind.Mosquito => go.AddComponent<Mosquito>(),
                    MonsterKind.Shadow => go.AddComponent<Shadow>(),
                    _ => go.AddComponent<Mosquito>(),
                };

                monster.Init(world, spawn.Pos);
                _monsters.Add(monster);
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
