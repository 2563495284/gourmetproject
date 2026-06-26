using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 结算总分旁的火焰表现：随结算加速从小火苗涨成更快、更旺的火。
    /// 固定结构优先在场景或 prefab 里预拼，缺粒子时运行时补齐兜底。
    /// </summary>
    public sealed class SettlementScoreFireView : MonoBehaviour
    {
        [Header("固定结构（场景/prefab 预拼，运行时引用）")]
        [SerializeField] private ParticleSystem _particles;

        [Header("火焰强度")]
        [SerializeField] private float _minEmission = 8f;
        [SerializeField] private float _maxEmission = 70f;
        [SerializeField] private float _minStartSize = 0.08f;
        [SerializeField] private float _maxStartSize = 0.32f;
        [SerializeField] private float _minLifetime = 0.18f;
        [SerializeField] private float _maxLifetime = 0.42f;
        [SerializeField] private float _minStartSpeed = 0.2f;
        [SerializeField] private float _maxStartSpeed = 1.1f;

        [Header("晃动")]
        [SerializeField] private float _maxWobbleDegrees = 8f;
        [SerializeField] private float _maxWobbleDistance = 0.06f;
        [SerializeField] private float _baseWobbleCyclesPerSecond = 1.2f;

        [Header("颜色")]
        [SerializeField] private Color _bottomColor = new Color(1f, 0.35f, 0.04f, 0.95f);
        [SerializeField] private Color _topColor = new Color(1f, 0.92f, 0.18f, 0.75f);

        private Vector3 _baseLocalPosition;
        private Quaternion _baseLocalRotation;
        private float _intensity;
        private float _speed = 1f;
        private float _wobbleTime;
        private bool _visible;
        private ParticleSystem _configuredParticles;

        private void Awake()
        {
            CaptureBaseTransform();
            EnsureRefs();
            Hide();
        }

        public void Show()
        {
            CaptureBaseTransform();
            EnsureRefs();
            gameObject.SetActive(true);
            _visible = true;
            SetIntensity(0f, 1f);
            _particles.Clear(true);
            _particles.Play(true);
        }

        public void Hide()
        {
            _visible = false;
            if (_particles != null)
            {
                _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            transform.localPosition = _baseLocalPosition;
            transform.localRotation = _baseLocalRotation;
            gameObject.SetActive(false);
        }

        public void SetIntensity(float normalized, float speed)
        {
            EnsureRefs();
            _intensity = Mathf.Clamp01(normalized);
            _speed = Mathf.Max(0.0001f, speed);

            ParticleSystem.MainModule main = _particles.main;
            main.startSize = Mathf.Lerp(_minStartSize, _maxStartSize, _intensity);
            main.startLifetime = Mathf.Lerp(_minLifetime, _maxLifetime, _intensity);
            main.startSpeed = Mathf.Lerp(_minStartSpeed, _maxStartSpeed, _intensity);
            main.simulationSpeed = Mathf.Lerp(0.85f, 1.45f, _intensity) * _speed;

            ParticleSystem.EmissionModule emission = _particles.emission;
            emission.rateOverTime = Mathf.Lerp(_minEmission, _maxEmission, _intensity);
        }

        private void Update()
        {
            if (!_visible)
            {
                return;
            }

            float wobbleSpeed = _baseWobbleCyclesPerSecond * Mathf.Lerp(1f, 2.4f, _intensity) * _speed;
            _wobbleTime += Time.unscaledDeltaTime * wobbleSpeed;
            float wave = Mathf.Sin(_wobbleTime * Mathf.PI * 2f);
            float distance = _maxWobbleDistance * _intensity;
            float degrees = _maxWobbleDegrees * _intensity;
            transform.localPosition = _baseLocalPosition + new Vector3(wave * distance, Mathf.Abs(wave) * distance * 0.35f, 0f);
            transform.localRotation = _baseLocalRotation * Quaternion.Euler(0f, 0f, wave * degrees);
        }

        private void CaptureBaseTransform()
        {
            _baseLocalPosition = transform.localPosition;
            _baseLocalRotation = transform.localRotation;
        }

        private void EnsureRefs()
        {
            if (_particles == null)
            {
                _particles = GetComponentInChildren<ParticleSystem>(true);
            }

            if (_particles == null)
            {
                var go = new GameObject("FireParticles");
                go.transform.SetParent(transform, false);
                _particles = go.AddComponent<ParticleSystem>();
            }

            if (_configuredParticles != _particles)
            {
                ConfigureParticles(_particles);
                _configuredParticles = _particles;
            }
        }

        private void ConfigureParticles(ParticleSystem particles)
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 0.6f;
            main.startColor = new ParticleSystem.MinMaxGradient(_bottomColor, _topColor);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            main.gravityModifier = -0.03f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 16f;
            shape.radius = 0.08f;

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(_topColor, 0f),
                    new GradientColorKey(_bottomColor, 0.55f),
                    new GradientColorKey(new Color(0.45f, 0.04f, 0.01f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            BattleSorting.Apply(renderer, BattleSorting.Fx, BattleSorting.OrderScoreFire);
            if (renderer.sharedMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    renderer.sharedMaterial = new Material(shader);
                }
            }
        }
    }
}
