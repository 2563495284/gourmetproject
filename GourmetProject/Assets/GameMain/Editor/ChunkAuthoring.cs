using System.Collections.Generic;
using GourmetProject.Game.Platformer;
using GourmetProject.Game.Platformer.Chunks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

namespace GourmetProject.GameEditor
{
    /// <summary>
    /// 起始预制段库的作者工具：从现有精灵创建 Tile 资产，按内置规格构建 Grid+Tilemap 预制段
    /// （含碰撞体/标记/出入口），存入 Resources/Chunks。运行时由 LevelAssembler 随机拼接。
    /// 菜单：Tools/光影/重建起始预制段库。可重复运行（覆盖同名预制体）。
    /// </summary>
    public static class ChunkAuthoring
    {
        private const string ChunkDir = "Assets/GameMain/Resources/Chunks";
        private const string TileDir = "Assets/GameMain/Art/Tiles";
        private const string MatDir = "Assets/GameMain/Art/Materials";

        private static Tile _platformTile;
        private static Tile _spikeTile;
        private static Material _litMat;

        [MenuItem("Tools/光影/重建起始预制段库")]
        public static void BuildStarterChunks()
        {
            EnsureFolder("Assets/GameMain/Art");
            EnsureFolder(TileDir);
            EnsureFolder(MatDir);
            EnsureFolder("Assets/GameMain/Resources");
            EnsureFolder(ChunkDir);

            _litMat = LoadOrCreateLitMaterial();
            _platformTile = MakeTile("platform", Art.PlatformTile);
            _spikeTile = MakeTile("spike", Art.Spikes);

            foreach (ChunkSpec spec in BuildSpecs())
            {
                BuildAndSave(spec);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ChunkAuthoring] 起始预制段库重建完成。");
        }

        // —— 资产创建 ——

        private static Material LoadOrCreateLitMaterial()
        {
            string path = MatDir + "/ChunkLit.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            mat = new Material(sh) { name = "ChunkLit" };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static Tile MakeTile(string name, string spriteResourcePath)
        {
            string path = TileDir + "/" + name + ".asset";
            Sprite sprite = Resources.Load<Sprite>(spriteResourcePath);
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = sprite;
                tile.colliderType = Tile.ColliderType.Grid;
                AssetDatabase.CreateAsset(tile, path);
            }
            else
            {
                tile.sprite = sprite;
                tile.colliderType = Tile.ColliderType.Grid;
                EditorUtility.SetDirty(tile);
            }
            return tile;
        }

        // —— 预制段构建 ——

        private static void BuildAndSave(ChunkSpec spec)
        {
            var root = new GameObject(spec.Name);
            root.AddComponent<Grid>();

            // 地形 Tilemap（Terrain 层，Tilemap+Composite 碰撞 + 阴影）。
            var tmGo = new GameObject("Terrain");
            tmGo.transform.SetParent(root.transform, false);
            SetLayer(tmGo, WorldRender.LayerTerrain);
            var tm = tmGo.AddComponent<Tilemap>();
            var tr = tmGo.AddComponent<TilemapRenderer>();
            tr.sharedMaterial = _litMat;

            foreach (RectInt r in spec.Solids) Paint(tm, r, _platformTile);

            var rb = tmGo.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Static;
            var tc = tmGo.AddComponent<TilemapCollider2D>();
            tc.compositeOperation = Collider2D.CompositeOperation.Merge;
            var cc = tmGo.AddComponent<CompositeCollider2D>();
            cc.geometryType = CompositeCollider2D.GeometryType.Polygons;
            TryAddShadow(tmGo);

            // 地刺（Spike 层，触发体）。
            foreach (RectInt r in spec.Spikes) CreateSpike(root.transform, r);

            // 起点 / 出入口 / 怪物 / 检查点标记。
            if (spec.Spawn.HasValue)
            {
                var sm = NewChild(root.transform, "Spawn", spec.Spawn.Value);
                sm.AddComponent<SpawnMarker>();
            }

            Transform entry = NewChild(root.transform, "Entry", new Vector2(0f, 0f)).transform;
            Transform exit = NewChild(root.transform, "Exit", new Vector2(0f, spec.Height)).transform;

            // 注意：怪物不再由此工具程序化生成。怪物由设计师把 Monster_*.prefab 直接拖进
            // Resources/Chunks/*.prefab 摆放，运行时 LevelAssembler 收集 MonsterBase 实例驱动。
            // 重跑本工具会覆盖同名预制段（含已摆放的怪物），仅用于地形 bootstrap。

            foreach (var c in spec.Checkpoints)
            {
                var go = NewChild(root.transform, c.Item2 ? "Endpoint" : "Checkpoint", c.Item1);
                go.AddComponent<CheckpointMarker>().IsEndpoint = c.Item2;
            }

            var info = root.AddComponent<ChunkInfo>();
            info.Role = spec.Role;
            info.Difficulty = spec.Difficulty;
            info.ThemeTag = spec.Theme;
            info.Entry = entry;
            info.Exit = exit;

            string path = ChunkDir + "/" + spec.Name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        private static void Paint(Tilemap tm, RectInt r, TileBase tile)
        {
            for (int x = r.xMin; x < r.xMax; x++)
                for (int y = r.yMin; y < r.yMax; y++)
                    tm.SetTile(new Vector3Int(x, y, 0), tile);
        }

        private static void CreateSpike(Transform parent, RectInt r)
        {
            var go = new GameObject("Spike");
            go.transform.SetParent(parent, false);
            SetLayer(go, WorldRender.LayerSpike);
            var center = new Vector2(r.xMin + r.width * 0.5f, r.yMin + r.height * 0.5f);
            go.transform.localPosition = new Vector3(center.x, center.y, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Resources.Load<Sprite>(Art.Spikes);
            sr.sharedMaterial = _litMat;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(r.width, r.height);
            sr.sortingOrder = 1;

            var box = go.AddComponent<BoxCollider2D>();
            box.size = new Vector2(r.width, r.height);
            box.isTrigger = true;
        }

        private static void TryAddShadow(GameObject go)
        {
            try
            {
                var sc = go.AddComponent<ShadowCaster2D>();
                sc.selfShadows = false;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[ChunkAuthoring] ShadowCaster2D 添加失败（忽略）: " + e.Message);
            }
        }

        private static GameObject NewChild(Transform parent, string name, Vector2 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(localPos.x, localPos.y, 0f);
            return go;
        }

        private static void SetLayer(GameObject go, string layerName)
        {
            int l = LayerMask.NameToLayer(layerName);
            if (l >= 0) go.layer = l;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string leaf = path.Substring(slash + 1);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // —— 规格定义 ——

        private sealed class ChunkSpec
        {
            public string Name;
            public ChunkRole Role;
            public int Difficulty;
            public int Height;
            public string Theme;
            public Vector2? Spawn;
            public readonly List<RectInt> Solids = new List<RectInt>();
            public readonly List<RectInt> Spikes = new List<RectInt>();
            public readonly List<(MonsterKind, Vector2)> Monsters = new List<(MonsterKind, Vector2)>();
            public readonly List<(Vector2, bool)> Checkpoints = new List<(Vector2, bool)>();

            public ChunkSpec Solid(int x, int y, int w) { Solids.Add(new RectInt(x, y, w, 1)); return this; }
            public ChunkSpec Block(int x, int y, int w, int h) { Solids.Add(new RectInt(x, y, w, h)); return this; }
            public ChunkSpec Spike(int x, int y, int w) { Spikes.Add(new RectInt(x, y, w, 1)); return this; }
            public ChunkSpec Mon(MonsterKind k, float x, float y) { Monsters.Add((k, new Vector2(x, y))); return this; }
            public ChunkSpec Cp(float x, float y, bool end) { Checkpoints.Add((new Vector2(x, y), end)); return this; }
        }

        private static List<ChunkSpec> BuildSpecs()
        {
            var list = new List<ChunkSpec>();

            // —— 起点段 ——
            var start = New("Chunk_Start", ChunkRole.Start, 0, 10, "intro");
            start.Spawn = new Vector2(0f, 0f);
            start.Block(-7, -1, 14, 1).Solid(3, 2, 4).Solid(-6, 4, 4).Solid(1, 6, 4).Solid(-2, 9, 4);
            list.Add(start);

            // —— 难度 1 ——
            list.Add(Land(New("Chunk_D1a", ChunkRole.Normal, 1, 10, "cave"))
                .Solid(2, 2, 4).Solid(-6, 5, 4).Solid(-2, 9, 4)
                .Mon(MonsterKind.Mosquito, 4f, 4f));
            list.Add(Land(New("Chunk_D1b", ChunkRole.Normal, 1, 9, "forest"))
                .Solid(-6, 3, 4).Solid(2, 5, 4).Solid(-2, 8, 4)
                .Mon(MonsterKind.Mosquito, -4f, 5f));

            // —— 难度 2 ——
            list.Add(Land(New("Chunk_D2a", ChunkRole.Normal, 2, 11, "cave"))
                .Solid(3, 2, 3).Solid(-6, 5, 3).Solid(2, 8, 3).Solid(-2, 10, 4)
                .Spike(-5, 6, 1).Mon(MonsterKind.Moth, 4f, 4f));
            list.Add(Land(New("Chunk_D2b", ChunkRole.Normal, 2, 10, "forest"))
                .Solid(-6, 3, 3).Solid(3, 6, 3).Solid(-2, 9, 4)
                .Spike(4, 7, 1).Mon(MonsterKind.LightEater, -4f, 5f));

            // —— 难度 3 ——
            list.Add(Land(New("Chunk_D3a", ChunkRole.Normal, 3, 12, "cave"))
                .Solid(3, 3, 3).Solid(-7, 6, 3).Solid(2, 9, 3).Solid(-2, 11, 4)
                .Spike(-6, 7, 1).Mon(MonsterKind.Vine, 4f, 3f).Mon(MonsterKind.LightScale, -6f, 9f));
            list.Add(Land(New("Chunk_D3b", ChunkRole.Normal, 3, 11, "forest"))
                .Solid(-6, 3, 3).Solid(2, 6, 3).Solid(-2, 10, 4)
                .Mon(MonsterKind.Firefly, 3f, 8f).Mon(MonsterKind.AmbushSpider, -4f, 5f));

            // —— 难度 4 ——
            list.Add(Land(New("Chunk_D4a", ChunkRole.Normal, 4, 13, "ruin"))
                .Solid(3, 3, 3).Solid(-7, 6, 2).Solid(3, 9, 2).Solid(-2, 12, 4)
                .Spike(3, 4, 1).Mon(MonsterKind.FogWraith, -5f, 8f).Mon(MonsterKind.EchoBat, 4f, 5f));

            // —— 难度 5 ——
            list.Add(Land(New("Chunk_D5a", ChunkRole.Normal, 5, 14, "ruin"))
                .Solid(-7, 3, 2).Solid(4, 6, 2).Solid(-7, 9, 2).Solid(3, 11, 2).Solid(-2, 13, 4)
                .Spike(-6, 4, 1).Mon(MonsterKind.StoneEye, -5f, 5f)
                .Mon(MonsterKind.LightShadowBug, 5f, 8f).Mon(MonsterKind.Shadow, 4f, 12f));

            // —— 休息段（检查点）——
            var rest = New("Chunk_Rest", ChunkRole.Rest, 0, 3, "rest");
            rest.Block(-6, -1, 12, 1).Solid(-3, 2, 6).Cp(0f, 5f, false);
            list.Add(rest);

            // —— 终点段（灯塔）——
            var end = New("Chunk_End", ChunkRole.End, 0, 3, "summit");
            end.Block(-6, -1, 12, 1).Solid(-4, 2, 8).Cp(0f, 5f, true);
            list.Add(end);

            return list;
        }

        private static ChunkSpec New(string name, ChunkRole role, int diff, int h, string theme)
        {
            return new ChunkSpec { Name = name, Role = role, Difficulty = diff, Height = h, Theme = theme };
        }

        /// <summary>给段添加底部落脚平台（top 在 y=0，与上一段顶平台对齐）。</summary>
        private static ChunkSpec Land(ChunkSpec s)
        {
            s.Block(-3, -1, 6, 1);
            return s;
        }
    }
}
