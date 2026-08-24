using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Common
{
    /// <summary>在一个 Overlay Canvas Graphic 内批量绘制 StarCard 的轻量金色闪点。</summary>
    public sealed class StarCardSparkleGraphic : MaskableGraphic
    {
        private const int DefaultMaxParticles = 18;

        [SerializeField, Min(1)] private int _maxParticles = DefaultMaxParticles;
        [SerializeField, Min(0.05f)] private float _ambientInterval = 0.65f;
        [SerializeField] private Color _warmColor = new(1f, 0.62f, 0.12f, 0.92f);
        [SerializeField] private Color _brightColor = new(1f, 0.96f, 0.72f, 1f);

        private readonly List<SparkParticle> _particles = new(DefaultMaxParticles);
        private RectTransform[] _starRects = Array.Empty<RectTransform>();
        private System.Random _random;
        private int _earnedStars;
        private float _ambientTimer;

        public int ParticleCount => _particles.Count;

        public int EarnedStars => _earnedStars;

        public int BurstInvocationCount { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            color = Color.white;
            _random = new System.Random(unchecked(Environment.TickCount * 397 ^ GetInstanceID()));
        }

        public void SetProgress(int earnedStars, RectTransform[] starRects)
        {
            _starRects = starRects ?? Array.Empty<RectTransform>();
            _earnedStars = Mathf.Clamp(earnedStars, 0, _starRects.Length);
            if (_earnedStars == 0)
            {
                _ambientTimer = 0f;
                _particles.Clear();
                SetVerticesDirty();
            }
        }

        public void Burst(RectTransform starRect, int count = 12)
        {
            if (!isActiveAndEnabled || starRect == null)
            {
                return;
            }

            EnsureRandom();
            BurstInvocationCount++;
            Vector2 center = StarCenter(starRect);
            int safeCount = Mathf.Clamp(count, 0, Mathf.Max(0, _maxParticles - _particles.Count));
            for (int i = 0; i < safeCount; i++)
            {
                float angle = Mathf.PI * 2f * i / Mathf.Max(1, safeCount) + Range(-0.18f, 0.18f);
                float speed = Range(24f, 52f);
                AddParticle(new SparkParticle
                {
                    Position = center + Direction(angle) * Range(1f, 7f),
                    Velocity = Direction(angle) * speed,
                    Age = 0f,
                    Lifetime = Range(0.38f, 0.72f),
                    Size = Range(4f, 8.5f),
                    Rotation = Range(0f, 90f),
                    AngularVelocity = Range(-150f, 150f),
                    Brightness = Range(0.45f, 1f),
                });
            }

            SetVerticesDirty();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            raycastTarget = false;
            _ambientTimer = Range(0f, Mathf.Max(0.05f, _ambientInterval));
            SetVerticesDirty();
        }

        protected override void OnDisable()
        {
            _particles.Clear();
            _ambientTimer = 0f;
            SetVerticesDirty();
            base.OnDisable();
        }

        private void Update()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            float deltaTime = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.05f);
            bool dirty = false;
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                SparkParticle particle = _particles[i];
                particle.Age += deltaTime;
                if (particle.Age >= particle.Lifetime)
                {
                    _particles.RemoveAt(i);
                    dirty = true;
                    continue;
                }

                particle.Position += particle.Velocity * deltaTime;
                particle.Velocity *= Mathf.Pow(0.35f, deltaTime);
                particle.Rotation += particle.AngularVelocity * deltaTime;
                _particles[i] = particle;
                dirty = true;
            }

            if (_earnedStars > 0 && _starRects.Length > 0)
            {
                _ambientTimer -= deltaTime;
                if (_ambientTimer <= 0f)
                {
                    _ambientTimer += Mathf.Max(0.05f, _ambientInterval);
                    SpawnAmbient();
                    dirty = true;
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
                SparkParticle particle = _particles[i];
                float remaining = Mathf.Clamp01(1f - particle.Age / Mathf.Max(0.001f, particle.Lifetime));
                float fade = Mathf.SmoothStep(0f, 1f, Mathf.Min(remaining * 2.5f, 1f));
                float pulse = 0.72f + Mathf.Sin(particle.Age * 18f + i) * 0.28f;
                float halfSize = particle.Size * Mathf.Lerp(0.45f, 1f, remaining) * 0.5f;
                Color particleColor = Color.Lerp(_warmColor, _brightColor, particle.Brightness);
                particleColor.a *= fade * Mathf.Max(0.35f, pulse);
                AddDiamond(vertexHelper, particle.Position, halfSize, particle.Rotation, particleColor);
            }
        }

        private void SpawnAmbient()
        {
            if (_particles.Count >= Mathf.Max(1, _maxParticles) || _earnedStars <= 0)
            {
                return;
            }

            EnsureRandom();
            int index = _random.Next(0, Mathf.Min(_earnedStars, _starRects.Length));
            RectTransform starRect = _starRects[index];
            if (starRect == null)
            {
                return;
            }

            Vector2 center = StarCenter(starRect);
            float angle = Range(0f, Mathf.PI * 2f);
            AddParticle(new SparkParticle
            {
                Position = center + Direction(angle) * Range(16f, 29f),
                Velocity = new Vector2(Range(-4f, 4f), Range(4f, 11f)),
                Age = 0f,
                Lifetime = Range(0.55f, 0.95f),
                Size = Range(2.5f, 6f),
                Rotation = Range(0f, 90f),
                AngularVelocity = Range(-45f, 45f),
                Brightness = Range(0.25f, 1f),
            });
        }

        private void AddParticle(SparkParticle particle)
        {
            if (_particles.Count < Mathf.Max(1, _maxParticles))
            {
                _particles.Add(particle);
            }
        }

        private Vector2 StarCenter(RectTransform starRect)
        {
            return rectTransform.InverseTransformPoint(starRect.TransformPoint(starRect.rect.center));
        }

        private void EnsureRandom()
        {
            _random ??= new System.Random(unchecked(Environment.TickCount * 397 ^ GetInstanceID()));
        }

        private float Range(float min, float max)
        {
            EnsureRandom();
            return min + (float)_random.NextDouble() * (max - min);
        }

        private static Vector2 Direction(float radians)
        {
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        private static void AddDiamond(
            VertexHelper helper,
            Vector2 center,
            float halfSize,
            float rotationDegrees,
            Color color)
        {
            float radians = rotationDegrees * Mathf.Deg2Rad;
            Vector2 right = new(Mathf.Cos(radians), Mathf.Sin(radians));
            Vector2 up = new(-right.y, right.x);
            right *= halfSize;
            up *= halfSize * 1.55f;

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

        private struct SparkParticle
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
