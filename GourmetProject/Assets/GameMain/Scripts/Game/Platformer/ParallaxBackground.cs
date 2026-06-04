using UnityEngine;

namespace GourmetProject.Game.Platformer
{
    /// <summary>
    /// 分层远景背景：多层剪影 sprite，水平轻微视差，纵向完全不跟随镜头（远景固定）。
    /// 使用 unlit 材质不受游戏光照影响，始终可见，模拟《地狱边境》的大气景深效果。
    /// </summary>
    public sealed class ParallaxBackground
    {
        private Transform _container;
        private Camera _cam;

        private struct Layer
        {
            public Transform Xform;
            public SpriteRenderer Renderer;
            public float ParallaxX;
        }

        private Layer[] _layers;

        /// <summary>
        /// 根据关卡数据构建背景层。每层铺满整个关卡高度，纵向固定，仅水平视差滚动。
        /// </summary>
        public void Build(Transform parent, Camera cam, LevelData level)
        {
            _cam = cam;
            _container = new GameObject("ParallaxBackground").transform;
            _container.SetParent(parent, false);

            float worldMinY = level.StartPos.y - 10f;
            float worldMaxY = (level.Checkpoints.Count > 0
                ? level.Checkpoints[^1].Pos.y
                : level.WorldHeight) + 10f;
            float coverHeight = worldMaxY - worldMinY;
            float coverWidth = level.WorldMaxX - level.WorldMinX + 30f;

            // 4 层远景：parallaxX 0=完全静态（天空），越大越跟随镜头
            // 若新 Limbo 风格素材不存在，回退到现有 background 素材
            var configs = new (string primary, string fallback, float px)[]
            {
                ("Sprites/Backgrounds/bg_sky",           "Sprites/Backgrounds/star_background",        0f),
                ("Sprites/Backgrounds/bg_far_mountains", "Sprites/Backgrounds/mountain_silhouette",   0.05f),
                ("Sprites/Backgrounds/bg_mid_forest",    "Sprites/Backgrounds/mountain_silhouette",   0.10f),
                ("Sprites/Backgrounds/bg_near_trees",    "Sprites/Backgrounds/mountain_silhouette",   0.18f),
            };

            _layers = new Layer[configs.Length];
            int validCount = 0;

            for (int i = 0; i < configs.Length; i++)
            {
                Sprite sprite = Art.Load(configs[i].primary)
                    ?? Art.Load(configs[i].fallback);
                if (sprite == null) continue;

                var go = new GameObject($"BgLayer_{i}");
                go.transform.SetParent(_container, false);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = -100 + i;
                // unlit 远景：不受游戏光照影响，始终保持可见
                // 但稍微压暗以融入黑暗氛围
                sr.color = new Color(0.25f, 0.28f, 0.32f, 1f);

                // Tiled 模式：用小素材平铺覆盖整个关卡区域
                sr.drawMode = SpriteDrawMode.Tiled;
                sr.size = new Vector2(coverWidth, coverHeight);

                // 所有层的 Y 固定在关卡最低点，纵向绝不跟随相机
                go.transform.position = new Vector3(0f, worldMinY, 0f);

                _layers[validCount] = new Layer
                {
                    Xform = go.transform,
                    Renderer = sr,
                    ParallaxX = configs[i].px,
                };
                validCount++;
            }

            if (validCount < _layers.Length)
                System.Array.Resize(ref _layers, validCount);

            // 初始对齐
            if (_cam != null)
                Tick(_cam.transform.position);
        }

        /// <summary>每帧在相机跟随之后调用。仅更新水平视差，纵向保持固定。</summary>
        public void Tick(Vector2 cameraCenter)
        {
            if (_layers == null) return;

            for (int i = 0; i < _layers.Length; i++)
            {
                Layer layer = _layers[i];
                if (layer.Xform == null) continue;
                Vector3 pos = layer.Xform.position;
                pos.x = cameraCenter.x * layer.ParallaxX;
                layer.Xform.position = pos;
                // Y 不更新 —— 远景固定
            }
        }
    }
}
