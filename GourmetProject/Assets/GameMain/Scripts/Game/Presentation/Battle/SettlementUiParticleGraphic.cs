using System;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 将标准 ParticleSystem 的模拟结果批量绘制进 Overlay Canvas。
    /// ParticleSystemRenderer 只负责承载粒子系统且始终关闭，实际渲染由本 Graphic 完成。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SettlementUiParticleGraphic : MaskableGraphic
    {
        internal enum ParticleVisualKind
        {
            Glow,
            Core,
            Tongue,
            Ember,
        }

        internal readonly struct Source
        {
            public Source(
                ParticleSystem system,
                ParticleVisualKind kind,
                Vector2 scale,
                float alpha,
                bool alignToVelocity)
            {
                System = system;
                Kind = kind;
                Scale = scale;
                Alpha = alpha;
                AlignToVelocity = alignToVelocity;
            }

            public ParticleSystem System { get; }

            public ParticleVisualKind Kind { get; }

            public Vector2 Scale { get; }

            public float Alpha { get; }

            public bool AlignToVelocity { get; }
        }

        private const int AtlasSize = 128;
        private const int CellSize = AtlasSize / 2;
        private const int MaxParticlesPerSystem = 96;
        private static Texture2D s_atlas;

        private readonly ParticleSystem.Particle[] _particleBuffer =
            new ParticleSystem.Particle[MaxParticlesPerSystem];

        private Source[] _sources = Array.Empty<Source>();
        private Texture _atlas;
        private float _heightScale = 1f;
        private float _widthScale = 1f;
        private Vector2 _effectCenter = new(0f, -38f);

        public override Texture mainTexture => _atlas != null ? _atlas : s_WhiteTexture;

        internal int DrawnParticleCount { get; private set; }

        internal static Texture2D SharedAtlas
        {
            get
            {
                if (s_atlas == null)
                {
                    s_atlas = BuildAtlas();
                }

                return s_atlas;
            }
        }

        internal void Configure(Texture atlas, Material drawMaterial, params Source[] sources)
        {
            _atlas = atlas;
            material = drawMaterial;
            _sources = sources ?? Array.Empty<Source>();
            raycastTarget = false;
            color = Color.white;
            SetMaterialDirty();
            SetVerticesDirty();
        }

        internal void SetPulse(float widthScale, float heightScale)
        {
            widthScale = Mathf.Max(0.01f, widthScale);
            heightScale = Mathf.Max(0.01f, heightScale);
            if (Mathf.Abs(_widthScale - widthScale) < 0.001f
                && Mathf.Abs(_heightScale - heightScale) < 0.001f)
            {
                return;
            }

            _widthScale = widthScale;
            _heightScale = heightScale;
            SetVerticesDirty();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            SetVerticesDirty();
        }

        private void LateUpdate()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            for (int i = 0; i < _sources.Length; i++)
            {
                ParticleSystem system = _sources[i].System;
                if (system != null && (system.particleCount > 0 || system.isEmitting))
                {
                    SetVerticesDirty();
                    return;
                }
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            DrawnParticleCount = 0;
            if (_atlas == null)
            {
                return;
            }

            for (int sourceIndex = 0; sourceIndex < _sources.Length; sourceIndex++)
            {
                Source source = _sources[sourceIndex];
                ParticleSystem system = source.System;
                if (system == null || !system.gameObject.activeInHierarchy)
                {
                    continue;
                }

                int count = Mathf.Min(system.GetParticles(_particleBuffer), _particleBuffer.Length);
                Rect uv = UvFor(source.Kind);
                for (int i = 0; i < count; i++)
                {
                    AddParticleQuad(vh, source, system, _particleBuffer[i], uv);
                }

                DrawnParticleCount += count;
            }
        }

        private void AddParticleQuad(
            VertexHelper vh,
            Source source,
            ParticleSystem system,
            ParticleSystem.Particle particle,
            Rect uv)
        {
            Vector3 worldPosition = system.transform.TransformPoint(particle.position);
            Vector2 center = rectTransform.InverseTransformPoint(worldPosition);
            center = _effectCenter + new Vector2(
                (center.x - _effectCenter.x) * _widthScale,
                (center.y - _effectCenter.y) * _heightScale);

            // ScoreMeter 的目标火团宽高约 170×175；粒子遮罩保留透明边缘，视觉尺寸需
            // 比 ParticleSystem 的逻辑 size 多约 20%，否则实色轮廓会显得偏小。
            float currentSize = particle.GetCurrentSize(system) * CanvasScaleCompensation(system) * 1.2f;
            float halfWidth = currentSize * source.Scale.x * _widthScale * 0.5f;
            float halfHeight = currentSize * source.Scale.y * _heightScale * 0.5f;
            float angle = particle.rotation;
            if (source.AlignToVelocity)
            {
                Vector3 worldVelocity = system.transform.TransformVector(particle.totalVelocity);
                Vector2 localVelocity = rectTransform.InverseTransformVector(worldVelocity);
                if (localVelocity.sqrMagnitude > 0.01f)
                {
                    angle = Mathf.Atan2(localVelocity.y, localVelocity.x) * Mathf.Rad2Deg - 90f;
                    float speedStretch = Mathf.Clamp(localVelocity.magnitude / 150f, 0f, 0.55f);
                    halfHeight *= 1f + speedStretch;
                }
            }

            float radians = angle * Mathf.Deg2Rad;
            Vector2 right = new(Mathf.Cos(radians), Mathf.Sin(radians));
            Vector2 up = new(-right.y, right.x);
            right *= halfWidth;
            up *= halfHeight;

            Color32 current = particle.GetCurrentColor(system);
            Color vertexColor = (Color)current * color;
            vertexColor.a *= source.Alpha;
            Color32 finalColor = vertexColor;
            int first = vh.currentVertCount;
            AddVert(vh, center - right - up, finalColor, new Vector2(uv.xMin, uv.yMin));
            AddVert(vh, center - right + up, finalColor, new Vector2(uv.xMin, uv.yMax));
            AddVert(vh, center + right + up, finalColor, new Vector2(uv.xMax, uv.yMax));
            AddVert(vh, center + right - up, finalColor, new Vector2(uv.xMax, uv.yMin));
            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first + 2, first + 3, first);
        }

        private static void AddVert(VertexHelper vh, Vector2 position, Color32 color, Vector2 uv)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = color;
            vertex.uv0 = uv;
            vh.AddVert(vertex);
        }

        private float CanvasScaleCompensation(ParticleSystem system)
        {
            // ParticleSystem 的尺寸处于本地单位；Overlay Canvas 的顶层缩放（例如 0.5）
            // 会让位置转换正确但视觉尺寸再缩一次，因此用粒子与 Graphic 的相对缩放补偿。
            Vector3 systemScale = system.transform.lossyScale;
            Vector3 graphicScale = rectTransform.lossyScale;
            float relativeX = Mathf.Abs(graphicScale.x) > 0.0001f
                ? Mathf.Abs(systemScale.x / graphicScale.x)
                : 1f;
            float relativeY = Mathf.Abs(graphicScale.y) > 0.0001f
                ? Mathf.Abs(systemScale.y / graphicScale.y)
                : 1f;
            return Mathf.Sqrt(Mathf.Max(0.0001f, relativeX * relativeY));
        }

        private static Rect UvFor(ParticleVisualKind kind)
        {
            const float inset = 2f / AtlasSize;
            int x = kind is ParticleVisualKind.Core or ParticleVisualKind.Ember ? 1 : 0;
            int y = kind is ParticleVisualKind.Tongue or ParticleVisualKind.Ember ? 1 : 0;
            float xMin = x * 0.5f + inset;
            float yMin = y * 0.5f + inset;
            return new Rect(xMin, yMin, 0.5f - inset * 2f, 0.5f - inset * 2f);
        }

        private static Texture2D BuildAtlas()
        {
            var texture = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, false, true)
            {
                name = "Settlement UI Particle Atlas (Runtime)",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[AtlasSize * AtlasSize];
            for (int y = 0; y < AtlasSize; y++)
            {
                for (int x = 0; x < AtlasSize; x++)
                {
                    int cellX = x / CellSize;
                    int cellY = y / CellSize;
                    float u = ((x % CellSize) + 0.5f) / CellSize * 2f - 1f;
                    float v = ((y % CellSize) + 0.5f) / CellSize * 2f - 1f;
                    float alpha = cellY == 0
                        ? cellX == 0 ? GlowMask(u, v) : CoreMask(u, v)
                        : cellX == 0 ? TongueMask(u, v) : EmberMask(u, v);
                    byte value = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f);
                    pixels[y * AtlasSize + x] = new Color32(255, 255, 255, value);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static float GlowMask(float x, float y)
        {
            float distance = Mathf.Sqrt(x * x + y * y);
            return Mathf.Pow(Mathf.Clamp01(1f - distance), 2.15f);
        }

        private static float CoreMask(float x, float y)
        {
            // 轻微不对称的圆润火团，不预先携带任何完整火焰轮廓。
            float wobble = 0.055f * Mathf.Sin(y * 5.4f + 0.8f);
            float nx = (x + wobble) / Mathf.Lerp(0.92f, 0.76f, Mathf.Clamp01((y + 1f) * 0.5f));
            float ny = y / 0.94f;
            float distance = Mathf.Pow(Mathf.Pow(Mathf.Abs(nx), 2.6f) + Mathf.Pow(Mathf.Abs(ny), 2.6f), 1f / 2.6f);
            return SmoothEdge(distance, 0.83f, 1f);
        }

        private static float TongueMask(float x, float y)
        {
            float t = Mathf.Clamp01((y + 1f) * 0.5f);
            float bend = 0.13f * Mathf.Sin(t * 3.7f + 0.45f) * t;
            float width = Mathf.Lerp(0.70f, 0.31f, t) * (1f - 0.08f * Mathf.Sin(t * 9f));
            float side = Mathf.Abs(x - bend) / Mathf.Max(0.12f, width);
            float cap = Mathf.Abs(y) < 0.68f
                ? 0f
                : (Mathf.Abs(y) - 0.68f) / 0.32f;
            float distance = Mathf.Sqrt(side * side + cap * cap);
            return SmoothEdge(distance, 0.82f, 1f);
        }

        private static float EmberMask(float x, float y)
        {
            float dx = Mathf.Max(Mathf.Abs(x) - 0.46f, 0f);
            float dy = Mathf.Max(Mathf.Abs(y) - 0.67f, 0f);
            float roundedDistance = Mathf.Sqrt(dx * dx + dy * dy) / 0.28f;
            float boxDistance = Mathf.Max(Mathf.Abs(x) / 0.74f, Mathf.Abs(y) / 0.90f);
            return SmoothEdge(Mathf.Max(roundedDistance, boxDistance * 0.82f), 0.72f, 1f);
        }

        private static float SmoothEdge(float value, float solid, float edge)
        {
            return 1f - Mathf.SmoothStep(solid, edge, value);
        }
    }
}
