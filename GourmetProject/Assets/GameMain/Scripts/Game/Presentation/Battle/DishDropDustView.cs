using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 落桌瞬间的一次性灰尘。使用 Unity ParticleSystem 复刻参考 Godot 粒子材质中的
    /// 随机散射、鼠标速度影响、重力、阻尼、随机缩放/旋转与颜色生命周期。
    /// </summary>
    public sealed class DishDropDustView : MonoBehaviour
    {
        private const int ParticleCount = 16;

        [SerializeField] private Texture2D _particleTexture;
        [SerializeField] private Color _lightColor = new(0.88f, 0.79f, 0.65f, 0.82f);
        [SerializeField] private Color _darkColor = new(0.48f, 0.38f, 0.29f, 0.72f);
        [SerializeField] private float _pointerVelocityInfluence = 0.16f;
        [SerializeField] private float _gravity = 0.30f;
        [SerializeField] private float _damping = 0.58f;

        private ParticleSystem _particles;
        private ParticleSystemRenderer _particleRenderer;
        private Material _runtimeMaterial;

        public static void Play(
            DishDropDustView prefab,
            Transform parent,
            Vector3 centerWorld,
            Vector2 footprintWorldSize,
            Vector2 pointerVelocityWorld)
        {
            DishDropDustView view;
            if (prefab != null)
            {
                view = Instantiate(prefab, parent);
            }
            else
            {
                var host = new GameObject("DishDropDust");
                if (parent != null)
                {
                    host.transform.SetParent(parent, false);
                }

                view = host.AddComponent<DishDropDustView>();
            }

            view.PlayInternal(centerWorld, footprintWorldSize, pointerVelocityWorld);
        }

        private void PlayInternal(
            Vector3 centerWorld,
            Vector2 footprintWorldSize,
            Vector2 pointerVelocityWorld)
        {
            EnsureParticles();
            transform.position = centerWorld;

            float cellReference = Mathf.Max(
                0.05f,
                Mathf.Min(
                    Mathf.Max(0.05f, footprintWorldSize.x),
                    Mathf.Max(0.05f, footprintWorldSize.y)));
            float baseSize = cellReference * 0.15f;
            float radialSpeed = cellReference * 1.15f;
            Vector2 halfEmission = footprintWorldSize * 0.34f;

            _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            for (int i = 0; i < ParticleCount; i++)
            {
                Vector2 direction = Random.insideUnitCircle;
                if (direction.sqrMagnitude < 0.001f)
                {
                    direction = Vector2.up;
                }

                direction.Normalize();
                direction.y = Mathf.Abs(direction.y) * 0.75f + 0.12f;
                Vector2 velocity = direction * Random.Range(radialSpeed * 0.35f, radialSpeed)
                    + pointerVelocityWorld * _pointerVelocityInfluence;
                Vector2 offset = new(
                    Random.Range(-halfEmission.x, halfEmission.x),
                    Random.Range(-halfEmission.y, halfEmission.y) * 0.35f);

                var emit = new ParticleSystem.EmitParams
                {
                    position = centerWorld + (Vector3)offset,
                    velocity = velocity,
                    startColor = Color.Lerp(_darkColor, _lightColor, Random.value),
                    startLifetime = Random.Range(0.4f, 0.7f),
                    startSize = baseSize * Random.Range(0.65f, 1.45f),
                    rotation = Random.Range(0f, Mathf.PI * 2f),
                    angularVelocity = Random.Range(-4.5f, 4.5f),
                };
                _particles.Emit(emit, 1);
            }

            _particles.Play();
        }

        private void EnsureParticles()
        {
            if (_particles == null)
            {
                _particles = GetComponent<ParticleSystem>();
            }

            if (_particles == null)
            {
                _particles = gameObject.AddComponent<ParticleSystem>();
            }

            if (_particleRenderer == null)
            {
                _particleRenderer = GetComponent<ParticleSystemRenderer>();
            }

            _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = _particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.08f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 32;
            main.gravityModifier = _gravity;
            main.stopAction = ParticleSystemStopAction.Destroy;

            ParticleSystem.EmissionModule emission = _particles.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = _particles.shape;
            shape.enabled = false;

            ParticleSystem.LimitVelocityOverLifetimeModule limit = _particles.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 10f;
            limit.dampen = Mathf.Clamp01(_damping);

            ParticleSystem.SizeOverLifetimeModule size = _particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.65f),
                    new Keyframe(0.28f, 1.12f),
                    new Keyframe(1f, 0.18f)));

            ParticleSystem.ColorOverLifetimeModule color = _particles.colorOverLifetime;
            color.enabled = true;
            var alpha = new Gradient
            {
                alphaKeys = new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(0f, 1f),
                },
                colorKeys = new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
            };
            color.color = alpha;

            if (_particleRenderer != null)
            {
                _particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                _particleRenderer.sortingLayerName = BattleSorting.Fx;
                _particleRenderer.sortingOrder = BattleSorting.OrderFloatingText - 5;
                EnsureMaterial();
            }
        }

        private void EnsureMaterial()
        {
            if (_runtimeMaterial != null || _particleRenderer == null)
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                return;
            }

            _runtimeMaterial = new Material(shader)
            {
                name = "DishDropDust_Runtime",
                mainTexture = _particleTexture != null
                    ? _particleTexture
                    : BattleShadow.DiffuseShadowSprite.texture,
            };
            _particleRenderer.sharedMaterial = _runtimeMaterial;
        }

        private void OnDestroy()
        {
            if (_runtimeMaterial != null)
            {
                Destroy(_runtimeMaterial);
                _runtimeMaterial = null;
            }
        }
    }
}
