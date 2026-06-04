using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Platformer.Monsters;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace GourmetProject.Game.Platformer.Chunks
{
    /// <summary>
    /// 关卡装配器：从 <see cref="ChunkLibrary"/> 按「起点 → 各难度段×n → 休息段 → 终点」结构确定性随机选段，
    /// 实例化并按 Entry/Exit 锚点首尾对齐拼接，沿途读取怪物/检查点/起点标记填充 <see cref="LevelData"/>。
    /// 取代旧的 LevelGenerator + LevelBuilder：地形/碰撞由预制段内 Tilemap(Collider) 承载。
    /// </summary>
    public static class LevelAssembler
    {
        private const string Stream = "level";

        /// <summary>各难度段数量（设计文档 13.2 默认 n1..n5）。</summary>
        private static readonly int[] DifficultyCounts = { 3, 3, 3, 2, 2 };

        public static LevelData Assemble(Transform parent)
        {
            if (!GameApp.Random.IsInitialized) GameApp.Random.Init(string.Empty);
            IRandomStream rng = GameApp.Random.Stream(Stream);

            ChunkLibrary lib = ChunkLibrary.Load();
            var data = new LevelData();

            var root = new GameObject("Chunks").transform;
            root.SetParent(parent, false);

            if (lib.IsEmpty)
            {
                Debug.LogError("[LevelAssembler] Resources/Chunks 下未找到任何预制段（ChunkInfo）。请先制作预制段。");
                data.StartPos = Vector2.zero;
                data.WorldMinX = -GameConst.Px(640f);
                data.WorldMaxX = GameConst.Px(640f);
                data.WorldHeight = GameConst.Px(640f);
                data.FallDeathY = -GameConst.Px(640f);
                return data;
            }

            List<GameObject> sequence = BuildSequence(lib, rng);

            Vector3 cursor = Vector3.zero;   // 下一段入口应对齐到的世界点
            bool hasStart = false;
            var worldBounds = new Bounds();
            bool boundsInit = false;

            for (int i = 0; i < sequence.Count; i++)
            {
                GameObject prefab = sequence[i];
                if (prefab == null) continue;

                GameObject inst = Object.Instantiate(prefab, root);
                ChunkInfo info = inst.GetComponent<ChunkInfo>();

                // 用世界坐标对齐：让本段 Entry 落到 cursor，再把 cursor 推进到本段 Exit。
                Vector3 entryWorld = info != null && info.Entry != null ? info.Entry.position : inst.transform.position;
                if (i == 0)
                {
                    // 起点段：直接落在原点附近（cursor 初值），Entry 作为基准。
                    cursor = entryWorld;
                }
                Vector3 delta = cursor - entryWorld;
                inst.transform.position += delta;

                Vector3 exitWorld = info != null && info.Exit != null ? info.Exit.position : inst.transform.position;
                cursor = exitWorld;

                CollectMarkers(inst, data, ref hasStart);
                Encapsulate(inst, ref worldBounds, ref boundsInit);
            }

            data.PlatformCount = sequence.Count;
            FinalizeBounds(data, worldBounds, boundsInit, root, hasStart);

            return data;
        }

        private static List<GameObject> BuildSequence(ChunkLibrary lib, IRandomStream rng)
        {
            var seq = new List<GameObject>();

            GameObject start = lib.PickStart(rng);
            if (start != null) seq.Add(start);

            for (int d = 1; d <= 5; d++)
            {
                int n = DifficultyCounts[d - 1];
                GameObject last = null;
                for (int k = 0; k < n; k++)
                {
                    GameObject c = lib.PickNormal(d, rng, last);
                    if (c != null)
                    {
                        seq.Add(c);
                        last = c;
                    }
                }

                GameObject rest = lib.PickRest(rng);
                if (rest != null) seq.Add(rest);
            }

            GameObject end = lib.PickEnd(rng);
            if (end != null) seq.Add(end);

            return seq;
        }

        private static void CollectMarkers(GameObject inst, LevelData data, ref bool hasStart)
        {
            // 玩家起点（仅起点段，取第一个）。
            if (!hasStart)
            {
                SpawnMarker sm = inst.GetComponentInChildren<SpawnMarker>(true);
                if (sm != null)
                {
                    Vector3 p = sm.transform.position; // 底边中心
                    data.StartPos = new Vector2(p.x - GameConst.PlayerWidth * 0.5f, p.y);
                    hasStart = true;
                }
            }

            // 怪物（设计师把 Monster_*.prefab 直接摆在预制段里，这里收集实例交给 MonsterManager 驱动）。
            MonsterBase[] monsters = inst.GetComponentsInChildren<MonsterBase>(true);
            foreach (MonsterBase mb in monsters)
            {
                if (mb != null) data.Monsters.Add(mb);
            }

            // 检查点 / 终点（按拼接顺序入列，PreActivate 依赖有序）。
            CheckpointMarker[] cps = inst.GetComponentsInChildren<CheckpointMarker>(true);
            foreach (CheckpointMarker cm in cps)
            {
                data.Checkpoints.Add(new CheckpointData { Pos = cm.transform.position, IsEndpoint = cm.IsEndpoint });
            }
        }

        private static void Encapsulate(GameObject inst, ref Bounds bounds, ref bool init)
        {
            // Tilemap 用 localBounds（由序列化瓦片即时得出，不依赖渲染/物理同步）转世界。
            var tilemaps = inst.GetComponentsInChildren<Tilemap>(true);
            foreach (Tilemap tm in tilemaps)
            {
                Bounds lb = tm.localBounds;
                if (lb.size == Vector3.zero) continue;
                Matrix4x4 m = tm.transform.localToWorldMatrix;
                Vector3 c = m.MultiplyPoint3x4(lb.center);
                Vector3 e = lb.extents;
                // 轴对齐近似：tilemap 无旋转，直接缩放半尺寸。
                Vector3 we = new Vector3(
                    Mathf.Abs(m.MultiplyVector(new Vector3(e.x, 0f, 0f)).x),
                    Mathf.Abs(m.MultiplyVector(new Vector3(0f, e.y, 0f)).y),
                    1f);
                var wb = new Bounds(c, we * 2f);
                if (!init) { bounds = wb; init = true; }
                else bounds.Encapsulate(wb);
            }

            // 其余可见体（地刺精灵等）用 Renderer 包围盒兜底。
            var renderers = inst.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (SpriteRenderer r in renderers)
            {
                if (!init) { bounds = r.bounds; init = true; }
                else bounds.Encapsulate(r.bounds);
            }
        }

        private static void FinalizeBounds(LevelData data, Bounds bounds, bool init, Transform root, bool hasStart)
        {
            if (!init)
            {
                data.WorldMinX = -GameConst.Px(640f);
                data.WorldMaxX = GameConst.Px(640f);
                data.WorldHeight = GameConst.Px(640f);
                data.FallDeathY = -GameConst.Px(640f);
                return;
            }

            float margin = GameConst.Px(160f);
            data.WorldMinX = bounds.min.x;
            data.WorldMaxX = bounds.max.x;
            data.WorldHeight = bounds.max.y;
            data.FallDeathY = bounds.min.y - GameConst.Px(640f);

            if (!hasStart)
            {
                // 兜底：没有 SpawnMarker 时放在底部左侧。
                data.StartPos = new Vector2(bounds.min.x + GameConst.Px(120f), bounds.min.y + GameConst.Tile);
            }

            // —— 左右世界边界墙（Terrain 层固体），防止玩家横向走出 ——
            float top = bounds.max.y + margin;
            float bottom = data.FallDeathY - margin;
            float height = top - bottom;
            float thick = GameConst.Tile;
            CreateBoundaryWall(root, "BoundaryLeft", data.WorldMinX - thick * 0.5f, (top + bottom) * 0.5f, thick, height);
            CreateBoundaryWall(root, "BoundaryRight", data.WorldMaxX + thick * 0.5f, (top + bottom) * 0.5f, thick, height);
        }

        private static void CreateBoundaryWall(Transform parent, string name, float cx, float cy, float w, float h)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(cx, cy, 0f);
            int terrain = LayerMask.NameToLayer(WorldRender.LayerTerrain);
            if (terrain >= 0) go.layer = terrain;
            var box = go.AddComponent<BoxCollider2D>();
            box.size = new Vector2(w, h);
        }
    }
}
