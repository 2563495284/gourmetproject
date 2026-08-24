using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>在 WeekEventCardView 的单个 UI Graphic 中绘制火热营业的稀疏上飘火星。</summary>
    [DisallowMultipleComponent]
    public sealed class WeekEventCardEmberGraphic : MaskableGraphic
    {
        private const int DefaultMaxParticles = 14;

        [SerializeField, Min(1)] private int _maxParticles = DefaultMaxParticles;
        [SerializeField] private Vector2 _spawnInterval = new Vector2(0.18f, 0.28f);
        [SerializeField] private Vector2 _lifetime = new Vector2(0.75f, 1.15f);
        [SerializeField] private Vector2 _particleSize = new Vector2(5f, 10f);
        [SerializeField] private Color _warmColor = new Color32(255, 92, 24, 235);
        [SerializeField] private Color _brightColor = new Color32(255, 231, 136, 255);

        private readonly List<EmberParticle> _particles = new List<EmberParticle>(DefaultMaxParticles);
        private System.Random _random;
        private float _spawnTimer;
        private bool _hot;

        public int ActiveParticleCount => _particles.Count;

        protected override void Awake()
        {
            base.Awake();
            ConfigureGraphic();
            EnsureRandom();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            ConfigureGraphic();
            _spawnTimer = NextSpawnInterval();
            SetVerticesDirty();
        }

        protected override void OnDisable()
        {
            ClearParticles();
            base.OnDisable();
        }

        protected override void Reset()
        {
            base.Reset();
            ConfigureGraphic();
        }

        public void SetHot(bool hot)
        {
            if (_hot == hot)
            {
                if (!hot && _particles.Count > 0)
                {
                    ClearParticles();
                }

                return;
            }

            _hot = hot;
            _spawnTimer = NextSpawnInterval();
            if (!hot)
            {
                ClearParticles();
            }
        }

        private void Update()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            float deltaTime = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.05f);
            bool dirty = AdvanceParticles(deltaTime);

            if (_hot)
            {
                _spawnTimer -= deltaTime;
                if (_spawnTimer <= 0f)
                {
                    _spawnTimer += NextSpawnInterval();
                    int spawnCount = Range(0f, 1f) < 0.38f ? 2 : 1;
                    for (int i = 0; i < spawnCount; i++)
                    {
                        dirty |= SpawnParticle();
                    }
                }
            }

            if (dirty)
            {
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            for (int i = 0; i < _particles.Count; i++)
            {
                EmberParticle particle = _particles[i];
                float normalizedAge = particle.Age / Mathf.Max(0.001f, particle.Lifetime);
                float remaining = Mathf.Clamp01(1f - normalizedAge);
                float fadeIn = Mathf.Clamp01(normalizedAge / 0.14f);
                float fadeOut = Mathf.Clamp01(remaining / 0.42f);
                float twinkle = 0.78f + Mathf.Sin(particle.Age * 17f + i * 0.83f) * 0.22f;
                float halfSize = particle.Size * Mathf.Lerp(0.6f, 0.95f, remaining) * 0.5f;

                Color particleColor = Color.Lerp(_warmColor, _brightColor, particle.Brightness);
                particleColor.a *= fadeIn * fadeOut * twinkle;
                AddDiamond(vertexHelper, particle.Position, halfSize, particle.Rotation, particleColor);
            }
        }

        private bool AdvanceParticles(float deltaTime)
        {
            bool dirty = false;
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                EmberParticle particle = _particles[i];
                particle.Age += deltaTime;
                if (particle.Age >= particle.Lifetime)
                {
                    _particles.RemoveAt(i);
                    dirty = true;
                    continue;
                }

                particle.Position += particle.Velocity * deltaTime;
                particle.Velocity.x *= Mathf.Pow(0.72f, deltaTime);
                particle.Rotation += particle.AngularVelocity * deltaTime;
                _particles[i] = particle;
                dirty = true;
            }

            return dirty;
        }

        private bool SpawnParticle()
        {
            if (_particles.Count >= Mathf.Max(1, _maxParticles))
            {
                return false;
            }

            EnsureRandom();
            Rect rect = rectTransform.rect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return false;
            }

            Vector2 position;
            float edgeRoll = Range(0f, 1f);
            if (edgeRoll < 0.58f)
            {
                position = new Vector2(
                    Range(rect.xMin + rect.width * 0.08f, rect.xMax - rect.width * 0.08f),
                    Range(rect.yMin - 2f, rect.yMin + 12f));
            }
            else
            {
                bool left = edgeRoll < 0.79f;
                position = new Vector2(
                    left ? rect.xMin + Range(-3f, 7f) : rect.xMax + Range(-7f, 3f),
                    Range(rect.yMin + rect.height * 0.08f, rect.yMin + rect.height * 0.54f));
            }

            _particles.Add(new EmberParticle
            {
                Position = position,
                Velocity = new Vector2(Range(-11f, 11f), Range(42f, 75f)),
                Age = 0f,
                Lifetime = Range(_lifetime.x, _lifetime.y),
                Size = Range(_particleSize.x, _particleSize.y),
                Rotation = Range(0f, 90f),
                AngularVelocity = Range(-65f, 65f),
                Brightness = Range(0.3f, 1f),
            });
            return true;
        }

        private void ClearParticles()
        {
            if (_particles.Count == 0)
            {
                return;
            }

            _particles.Clear();
            SetVerticesDirty();
        }

        private float NextSpawnInterval()
        {
            return Range(_spawnInterval.x, _spawnInterval.y);
        }

        private void ConfigureGraphic()
        {
            raycastTarget = false;
            color = Color.white;
        }

        private void EnsureRandom()
        {
            _random ??= new System.Random(unchecked(Environment.TickCount * 397 ^ GetInstanceID()));
        }

        private float Range(float min, float max)
        {
            EnsureRandom();
            float safeMin = Mathf.Min(min, max);
            float safeMax = Mathf.Max(min, max);
            return safeMin + (float)_random.NextDouble() * (safeMax - safeMin);
        }

        private static void AddDiamond(
            VertexHelper helper,
            Vector2 center,
            float halfSize,
            float rotationDegrees,
            Color color)
        {
            float radians = rotationDegrees * Mathf.Deg2Rad;
            Vector2 right = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * halfSize;
            Vector2 up = new Vector2(-right.y, right.x) * 1.65f;

            int first = helper.currentVertCount;
            AddVertex(helper, center - up, color, new Vector2(0.5f, 0f));
            AddVertex(helper, center - right, color, new Vector2(0f, 0.5f));
            AddVertex(helper, center + up, color, new Vector2(0.5f, 1f));
            AddVertex(helper, center + right, color, new Vector2(1f, 0.5f));
            helper.AddTriangle(first, first + 1, first + 2);
            helper.AddTriangle(first + 2, first + 3, first);
        }

        private static void AddVertex(VertexHelper helper, Vector2 position, Color color, Vector2 uv)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = color;
            vertex.uv0 = uv;
            helper.AddVert(vertex);
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            _maxParticles = Mathf.Max(1, _maxParticles);
            _spawnInterval = ClampRange(_spawnInterval, 0.05f);
            _lifetime = ClampRange(_lifetime, 0.05f);
            _particleSize = ClampRange(_particleSize, 0.5f);
            ConfigureGraphic();
        }

        private static Vector2 ClampRange(Vector2 range, float minimum)
        {
            float min = Mathf.Max(minimum, Mathf.Min(range.x, range.y));
            float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
            return new Vector2(min, max);
        }

        private struct EmberParticle
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public float Age;
            public float Lifetime;
            public float Size;
            public float Rotation;
            public float AngularVelocity;
            public float Brightness;
        }
    }
}
