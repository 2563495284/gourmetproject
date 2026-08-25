using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>绘制星级评鉴专属的高密度四角星，不创建持续性的粒子 GameObject。</summary>
    [DisallowMultipleComponent]
    public sealed class WeekEventCardStarburstGraphic : MaskableGraphic
    {
        private const int DefaultMaxParticles = 28;

        [SerializeField, Min(1)] private int _maxParticles = DefaultMaxParticles;
        [SerializeField] private Vector2 _spawnInterval = new Vector2(0.07f, 0.12f);
        [SerializeField] private Vector2 _burstInterval = new Vector2(0.78f, 1.08f);
        [SerializeField] private Vector2 _lifetime = new Vector2(0.7f, 1.25f);
        [SerializeField] private Vector2 _particleSize = new Vector2(7f, 16f);
        [SerializeField] private Color _goldColor = new Color32(255, 190, 46, 255);
        [SerializeField] private Color _whiteGoldColor = new Color32(255, 248, 196, 255);
        [SerializeField] private Color _violetColor = new Color32(207, 85, 255, 245);

        private readonly List<StarParticle> _particles = new List<StarParticle>(DefaultMaxParticles);
        private System.Random _random;
        private float _spawnTimer;
        private float _burstTimer;
        private bool _starEvaluation;
        private bool _highlighted;

        public int ActiveParticleCount => _particles.Count;
        public bool IsStarEvaluation => _starEvaluation;

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
            ResetTimers();
            SetVerticesDirty();
        }

        protected override void OnDisable()
        {
            ClearParticles();
            base.OnDisable();
        }

#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();
            ConfigureGraphic();
        }
#endif

        public void SetStarEvaluation(bool active)
        {
            if (_starEvaluation == active)
            {
                if (!active && _particles.Count > 0)
                {
                    ClearParticles();
                }

                return;
            }

            _starEvaluation = active;
            ResetTimers();
            if (!active)
            {
                _highlighted = false;
                ClearParticles();
            }

            SetVerticesDirty();
        }

        public void SetHighlighted(bool highlighted)
        {
            if (_highlighted == highlighted)
            {
                return;
            }

            _highlighted = highlighted;
            SetVerticesDirty();
        }

        public void TriggerBurst()
        {
            if (!_starEvaluation || !isActiveAndEnabled)
            {
                return;
            }

            bool dirty = false;
            for (int i = 0; i < 12; i++)
            {
                dirty |= SpawnParticle(true);
            }

            if (dirty)
            {
                SetVerticesDirty();
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
            if (_starEvaluation)
            {
                _spawnTimer -= deltaTime;
                if (_spawnTimer <= 0f)
                {
                    _spawnTimer += NextInterval(_spawnInterval);
                    int spawnCount = _highlighted ? 3 : RangeInt(2, 4);
                    for (int i = 0; i < spawnCount; i++)
                    {
                        dirty |= SpawnParticle(false);
                    }
                }

                _burstTimer -= deltaTime;
                if (_burstTimer <= 0f)
                {
                    _burstTimer += NextInterval(_burstInterval);
                    for (int i = 0; i < 6; i++)
                    {
                        dirty |= SpawnParticle(true);
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
            if (!_starEvaluation)
            {
                return;
            }

            for (int i = 0; i < _particles.Count; i++)
            {
                StarParticle particle = _particles[i];
                float normalizedAge = particle.Age / Mathf.Max(0.001f, particle.Lifetime);
                float remaining = Mathf.Clamp01(1f - normalizedAge);
                float fadeIn = Mathf.Clamp01(normalizedAge / 0.1f);
                float fadeOut = Mathf.Clamp01(remaining / 0.34f);
                float twinkle = 0.72f + Mathf.Sin(particle.Age * 24f + i * 1.17f) * 0.28f;
                float size = particle.Size * Mathf.Lerp(0.58f, 1.12f, remaining);
                float emphasis = _highlighted ? 1.22f : 1f;

                Color particleColor = Color.Lerp(_goldColor, _whiteGoldColor, particle.Brightness);
                particleColor = Color.Lerp(particleColor, _violetColor, particle.VioletAmount);
                particleColor.a *= fadeIn * fadeOut * twinkle * emphasis;
                AddFourPointStar(
                    vertexHelper,
                    particle.Position,
                    size,
                    particle.Rotation,
                    particleColor);
            }
        }

        private bool AdvanceParticles(float deltaTime)
        {
            bool dirty = false;
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                StarParticle particle = _particles[i];
                particle.Age += deltaTime;
                if (particle.Age >= particle.Lifetime)
                {
                    _particles.RemoveAt(i);
                    dirty = true;
                    continue;
                }

                particle.Position += particle.Velocity * deltaTime;
                particle.Velocity *= Mathf.Pow(0.88f, deltaTime);
                particle.Rotation += particle.AngularVelocity * deltaTime;
                _particles[i] = particle;
                dirty = true;
            }

            return dirty;
        }

        private bool SpawnParticle(bool burst)
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

            float sideRoll = Range(0f, 4f);
            Vector2 position;
            Vector2 normal;
            Vector2 tangent;
            if (sideRoll < 1f)
            {
                position = new Vector2(Range(rect.xMin, rect.xMax), rect.yMax - Range(0f, 10f));
                normal = Vector2.up;
                tangent = Vector2.right;
            }
            else if (sideRoll < 2f)
            {
                position = new Vector2(rect.xMax - Range(0f, 9f), Range(rect.yMin, rect.yMax));
                normal = Vector2.right;
                tangent = Vector2.up;
            }
            else if (sideRoll < 3f)
            {
                position = new Vector2(Range(rect.xMin, rect.xMax), rect.yMin + Range(0f, 10f));
                normal = Vector2.down;
                tangent = Vector2.right;
            }
            else
            {
                position = new Vector2(rect.xMin + Range(0f, 9f), Range(rect.yMin, rect.yMax));
                normal = Vector2.left;
                tangent = Vector2.up;
            }

            float outwardSpeed = burst ? Range(58f, 96f) : Range(30f, 65f);
            Vector2 velocity = normal * outwardSpeed + tangent * Range(-28f, 28f) + Vector2.up * Range(8f, 26f);
            _particles.Add(new StarParticle
            {
                Position = position,
                Velocity = velocity,
                Age = 0f,
                Lifetime = Range(_lifetime.x, _lifetime.y) * (burst ? 0.78f : 1f),
                Size = Range(_particleSize.x, _particleSize.y) * (burst ? 1.18f : 1f),
                Rotation = Range(0f, 90f),
                AngularVelocity = Range(-130f, 130f),
                Brightness = Range(0.38f, 1f),
                VioletAmount = Range(0f, 1f) < 0.34f ? Range(0.18f, 0.58f) : 0f,
            });
            return true;
        }

        private void ResetTimers()
        {
            _spawnTimer = NextInterval(_spawnInterval);
            _burstTimer = NextInterval(_burstInterval);
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

        private float NextInterval(Vector2 interval)
        {
            return Range(interval.x, interval.y);
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

        private int RangeInt(int minInclusive, int maxExclusive)
        {
            EnsureRandom();
            return _random.Next(minInclusive, maxExclusive);
        }

        private static void AddFourPointStar(
            VertexHelper helper,
            Vector2 center,
            float size,
            float rotationDegrees,
            Color color)
        {
            int centerIndex = helper.currentVertCount;
            AddVertex(helper, center, color);
            float rotation = rotationDegrees * Mathf.Deg2Rad;
            for (int i = 0; i < 8; i++)
            {
                float radians = rotation + i * Mathf.PI / 4f;
                float radius = i % 2 == 0 ? size : size * 0.24f;
                // 纵向略拉长，获得比普通菱形火星更醒目的四角星轮廓。
                Vector2 offset = new Vector2(Mathf.Cos(radians) * radius, Mathf.Sin(radians) * radius * 1.32f);
                AddVertex(helper, center + offset, color);
            }

            for (int i = 0; i < 8; i++)
            {
                helper.AddTriangle(centerIndex, centerIndex + 1 + i, centerIndex + 1 + (i + 1) % 8);
            }
        }

        private static void AddVertex(VertexHelper helper, Vector2 position, Color color)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = color;
            helper.AddVert(vertex);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            _maxParticles = Mathf.Max(1, _maxParticles);
            _spawnInterval = ClampRange(_spawnInterval, 0.03f);
            _burstInterval = ClampRange(_burstInterval, 0.2f);
            _lifetime = ClampRange(_lifetime, 0.05f);
            _particleSize = ClampRange(_particleSize, 0.5f);
            ConfigureGraphic();
        }
#endif

        private static Vector2 ClampRange(Vector2 range, float minimum)
        {
            float min = Mathf.Max(minimum, Mathf.Min(range.x, range.y));
            float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
            return new Vector2(min, max);
        }

        private struct StarParticle
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public float Age;
            public float Lifetime;
            public float Size;
            public float Rotation;
            public float AngularVelocity;
            public float Brightness;
            public float VioletAmount;
        }
    }
}
