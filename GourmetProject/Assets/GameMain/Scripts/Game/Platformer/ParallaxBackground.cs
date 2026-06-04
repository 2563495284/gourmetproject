using UnityEngine;

namespace GourmetProject.Game.Platformer
{
    /// <summary>
    /// 分层远景背景：多层剪影 sprite，水平轻微视差，纵向完全不跟随镜头（远景固定）。
    /// 使用 unlit 材质不受游戏光照影响；颜色与 <see cref="GameConst.ParallaxLayerColorsNight"/> 同步，
    /// 逗号全图照亮时由 <see cref="SetRevealAll"/> 切换到更亮 palette。
    /// </summary>
    public sealed class ParallaxBackground
    {
        private Transform _container;
        private Camera _cam;
        private bool _revealAll;

        private struct Layer
        {
            public Transform Xform;
            public SpriteRenderer Renderer;
            public float ParallaxX;
            public int ColorIndex;
        }

        private Layer[] _layers;

        public void SetRevealAll(bool reveal)
        {
            if (_revealAll == reveal) return;
            _revealAll = reveal;
            ApplyLayerColors();
        }

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
                sr.drawMode = SpriteDrawMode.Tiled;
                sr.size = new Vector2(coverWidth, coverHeight);
                go.transform.position = new Vector3(0f, worldMinY, 0f);

                _layers[validCount] = new Layer
                {
                    Xform = go.transform,
                    Renderer = sr,
                    ParallaxX = configs[i].px,
                    ColorIndex = validCount,
                };
                validCount++;
            }

            if (validCount < _layers.Length)
                System.Array.Resize(ref _layers, validCount);

            ApplyLayerColors();

            if (_cam != null)
                Tick(_cam.transform.position);
        }

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
            }
        }

        private void ApplyLayerColors()
        {
            if (_layers == null) return;

            Color[] palette = _revealAll
                ? GameConst.ParallaxLayerColorsReveal
                : GameConst.ParallaxLayerColorsNight;

            for (int i = 0; i < _layers.Length; i++)
            {
                SpriteRenderer sr = _layers[i].Renderer;
                if (sr == null) continue;
                int idx = _layers[i].ColorIndex;
                sr.color = idx < palette.Length ? palette[idx] : palette[^1];
            }
        }
    }
}
