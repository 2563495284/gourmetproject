using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 结算分数后的卡通高能粒子火焰。标准 ParticleSystem 负责模拟，UGUI Graphic
    /// 将结果绘制到 Overlay Canvas，使火焰稳定处于 ScoreMeter 三行文本之后。
    /// </summary>
    public sealed class SettlementScoreFireView : MonoBehaviour
    {
        private const string ParticleShaderName = "GourmetProject/SettlementUiParticle";
        private const float TargetCoreRate = 30f;
        private const float TargetTongueRate = 16f;
        private const float TargetEmberRate = 2f;
        private const float DoubleCoreRate = 46f;
        private const float DoubleTongueRate = 27f;
        private const float DoubleEmberRate = 6f;

        [Header("粒子系统")]
        [FormerlySerializedAs("_particles")]
        [SerializeField] private ParticleSystem _coreParticles;
        [SerializeField] private ParticleSystem _tongueParticles;
        [SerializeField] private ParticleSystem _emberParticles;
        [SerializeField] private SettlementFeverScreenView _screenFever;

        [Header("卡通高能火配色")]
        [SerializeField] private Color _coreHot = new(1f, 0.73f, 0.25f, 1f);
        [SerializeField] private Color _coreOrange = new(1f, 0.36f, 0.045f, 1f);
        [SerializeField] private Color _tongueOrange = new(1f, 0.27f, 0.035f, 1f);
        [SerializeField] private Color _tongueRed = new(0.88f, 0.105f, 0.025f, 1f);
        [SerializeField] private Color _emberColor = new(1f, 0.31f, 0.035f, 1f);

        private RectTransform _rootRect;
        private SettlementUiParticleGraphic _glowGraphic;
        private SettlementUiParticleGraphic _bodyGraphic;
        private Material _glowMaterial;
        private Material _bodyMaterial;
        private SettlementPacePhase _phase;
        private float _effectiveSpeed = 1f;
        private float _coreAccumulator;
        private float _tongueAccumulator;
        private float _emberAccumulator;
        private float _burstCompression;
        private float _burstPulse;
        private float _queuedBurstStrength;
        private int _burstDelayFrames;
        private uint _randomState = 0x7A4D21E9u;
        private bool _particlesConfigured;
        private bool _visible;

        internal SettlementPacePhase Phase => _phase;

        internal int ActiveMoteCount => ParticleCount(_coreParticles)
            + ParticleCount(_tongueParticles)
            + ParticleCount(_emberParticles);

        internal float EffectiveSimulationSpeed => _effectiveSpeed;

        internal float CoreEmissionRate => RateForPhase(TargetCoreRate, DoubleCoreRate);

        internal float TongueEmissionRate => RateForPhase(TargetTongueRate, DoubleTongueRate);

        internal float EmberEmissionRate => RateForPhase(TargetEmberRate, DoubleEmberRate);

        internal bool CoreEmitterRunning => IsRunning(_coreParticles);

        internal bool TongueEmitterRunning => IsRunning(_tongueParticles);

        internal bool EmberEmitterRunning => IsRunning(_emberParticles);

        internal SettlementFeverScreenView ScreenFever => _screenFever;

        private void Awake()
        {
            EnsureParticleRenderer();
            ResetVisuals();
        }

        /// <summary>把火焰铺进 ScoreMeter，并固定为三行分数文本的背景层。</summary>
        public void BindToScore(RectTransform scoreAnchor, RectTransform presentationRoot = null)
        {
            EnsureScreenFever(presentationRoot);
            RectTransform scoreMeter = scoreAnchor != null
                ? scoreAnchor.parent as RectTransform
                : null;
            if (scoreMeter == null)
            {
                return;
            }

            EnsureParticleRenderer();
            _rootRect.SetParent(scoreMeter, false);
            _rootRect.anchorMin = Vector2.zero;
            _rootRect.anchorMax = Vector2.one;
            _rootRect.pivot = new Vector2(0.5f, 0.5f);
            _rootRect.anchoredPosition = Vector2.zero;
            _rootRect.sizeDelta = new Vector2(-8f, 8f);
            _rootRect.localScale = Vector3.one;
            _rootRect.localRotation = Quaternion.identity;
            _rootRect.SetAsFirstSibling();
            StretchGraphic(_glowGraphic);
            StretchGraphic(_bodyGraphic);
        }

        public void Show()
        {
            gameObject.SetActive(true);
            EnsureParticleRenderer();
            _screenFever?.Show();
            _visible = true;
            SetPhase(SettlementPacePhase.BelowTarget, 1f);
        }

        public void Hide()
        {
            _screenFever?.Hide();
            _visible = false;
            _phase = SettlementPacePhase.BelowTarget;
            _effectiveSpeed = 1f;
            ResetVisuals();
            gameObject.SetActive(false);
        }

        internal void SetPhase(SettlementPacePhase phase, float effectiveSpeed)
        {
            EnsureParticleRenderer();
            bool wasIgnited = _phase != SettlementPacePhase.BelowTarget;
            _phase = phase;
            _effectiveSpeed = Mathf.Max(0.0001f, effectiveSpeed);
            SetSimulationSpeed(_coreParticles, _effectiveSpeed);
            SetSimulationSpeed(_tongueParticles, _effectiveSpeed);
            SetSimulationSpeed(_emberParticles, _effectiveSpeed);
            _screenFever?.SetPhase(phase, _effectiveSpeed);

            bool ignited = phase != SettlementPacePhase.BelowTarget;
            _glowGraphic.gameObject.SetActive(ignited);
            _bodyGraphic.gameObject.SetActive(ignited);
            if (!ignited)
            {
                StopAndClearParticles();
                ResetRuntimeState();
                return;
            }

            StartParticleSystem(_coreParticles);
            StartParticleSystem(_tongueParticles);
            StartParticleSystem(_emberParticles);
            if (!wasIgnited && ActiveMoteCount == 0)
            {
                // 立即提供连续火种；正式爆燃仍在下一帧经过一次压缩后发生。
                EmitCore(7, 0.72f);
                EmitTongue(4, 0.64f);
            }

            RefreshGraphics();
        }

        /// <summary>强结果、升档和最终盖章时触发压缩后上喷的爆燃。</summary>
        public void Burst(float strength = 1f)
        {
            EnsureParticleRenderer();
            if (_phase == SettlementPacePhase.BelowTarget)
            {
                return;
            }

            if (!_visible)
            {
                gameObject.SetActive(true);
                _visible = true;
            }

            float clamped = Mathf.Clamp01(strength);
            _screenFever?.Burst(clamped);
            _queuedBurstStrength = Mathf.Max(_queuedBurstStrength, clamped);
            _burstCompression = Mathf.Max(_burstCompression, Mathf.Lerp(0.10f, 0.14f, clamped));
            _burstDelayFrames = Mathf.Max(_burstDelayFrames, 1);
            ApplyPulseScale();
        }

        private void Update()
        {
            if (!_visible || _phase == SettlementPacePhase.BelowTarget)
            {
                return;
            }

            float frameDelta = Time.deltaTime;
            if (frameDelta <= 0f)
            {
                return;
            }

            float animationDelta = frameDelta * _effectiveSpeed;
            EmitContinuous(animationDelta);
            if (_burstDelayFrames > 0)
            {
                _burstDelayFrames--;
            }
            else if (_queuedBurstStrength > 0f)
            {
                EmitBurst(_queuedBurstStrength);
                _queuedBurstStrength = 0f;
                _burstCompression = 0f;
            }

            _burstCompression = Mathf.MoveTowards(_burstCompression, 0f, animationDelta * 2.4f);
            _burstPulse = Mathf.MoveTowards(_burstPulse, 0f, animationDelta * 2.9f);
            ApplyPulseScale();
        }

        private void EmitContinuous(float animationDelta)
        {
            _coreAccumulator += CoreEmissionRate * animationDelta;
            _tongueAccumulator += TongueEmissionRate * animationDelta;
            _emberAccumulator += EmberEmissionRate * animationDelta;
            int coreCount = ConsumeWholeParticles(ref _coreAccumulator);
            int tongueCount = ConsumeWholeParticles(ref _tongueAccumulator);
            int emberCount = ConsumeWholeParticles(ref _emberAccumulator);
            if (coreCount > 0)
            {
                EmitCore(coreCount, 0f);
            }

            if (tongueCount > 0)
            {
                EmitTongue(tongueCount, 0f);
            }

            if (emberCount > 0)
            {
                EmitEmber(emberCount, 0f);
            }
        }

        private void EmitBurst(float strength)
        {
            bool highest = _phase == SettlementPacePhase.DoubleTarget;
            int coreCount = Mathf.RoundToInt(Mathf.Lerp(4f, highest ? 13f : 10f, strength));
            int tongueCount = Mathf.RoundToInt(Mathf.Lerp(5f, highest ? 19f : 15f, strength));
            int emberCount = Mathf.RoundToInt(Mathf.Lerp(1f, highest ? 10f : 7f, strength));
            EmitCore(coreCount, Mathf.Lerp(0.45f, 1f, strength));
            EmitTongue(tongueCount, Mathf.Lerp(0.55f, 1f, strength));
            EmitEmber(emberCount, Mathf.Lerp(0.45f, 1f, strength));
            _burstPulse = Mathf.Max(_burstPulse, Mathf.Lerp(0.35f, 1f, strength));
            RefreshGraphics();
        }

        private void EmitCore(int count, float burst)
        {
            if (_coreParticles == null)
            {
                return;
            }

            bool highest = _phase == SettlementPacePhase.DoubleTarget;
            for (int i = 0; i < count; i++)
            {
                float sideBias = NextSigned();
                float verticalJitter = Next01();
                float warmChance = Next01();
                var emit = new ParticleSystem.EmitParams
                {
                    position = new Vector3(
                        sideBias * (highest ? 50f : 42f) * Mathf.Lerp(0.25f, 1f, Next01()),
                        -54f + verticalJitter * 20f + Mathf.Abs(sideBias) * 3f,
                        0f),
                    velocity = new Vector3(
                        sideBias * RandomRange(4f, highest ? 19f : 14f),
                        RandomRange(highest ? 64f : 54f, highest ? 96f : 82f) * (1f + burst * 0.32f),
                        0f),
                    startLifetime = RandomRange(0.55f, highest ? 0.79f : 0.72f),
                    startSize = RandomRange(highest ? 59f : 53f, highest ? 82f : 74f) * (1f + burst * 0.12f),
                    startColor = warmChance < 0.14f
                        ? Color.Lerp(_coreHot, Color.white, 0.12f)
                        : Color.Lerp(_coreOrange, _coreHot, RandomRange(0.08f, 0.52f)),
                    rotation = RandomRange(-16f, 16f),
                    angularVelocity = RandomRange(-26f, 26f),
                    randomSeed = NextUInt(),
                };
                _coreParticles.Emit(emit, 1);
            }
        }

        private void EmitTongue(int count, float burst)
        {
            if (_tongueParticles == null)
            {
                return;
            }

            bool highest = _phase == SettlementPacePhase.DoubleTarget;
            for (int i = 0; i < count; i++)
            {
                float side = NextSigned();
                float alternating = ((i + (int)(_randomState & 1u)) & 1) == 0 ? -1f : 1f;
                float x = Mathf.Lerp(side, alternating, 0.38f)
                    * (highest ? 57f : 47f)
                    * Mathf.Lerp(0.32f, 1f, Next01());
                var emit = new ParticleSystem.EmitParams
                {
                    position = new Vector3(x, RandomRange(-53f, -31f) + Mathf.Abs(x) * 0.04f, 0f),
                    velocity = new Vector3(
                        -x * RandomRange(0.08f, 0.22f) + NextSigned() * (highest ? 23f : 17f),
                        RandomRange(highest ? 92f : 78f, highest ? 145f : 124f) * (1f + burst * 0.42f),
                        0f),
                    startLifetime = RandomRange(0.52f, highest ? 0.82f : 0.74f),
                    startSize = RandomRange(highest ? 39f : 35f, highest ? 57f : 51f) * (1f + burst * 0.15f),
                    startColor = Color.Lerp(_tongueRed, _tongueOrange, RandomRange(0.24f, 0.90f)),
                    rotation = RandomRange(-13f, 13f),
                    angularVelocity = RandomRange(-34f, 34f),
                    randomSeed = NextUInt(),
                };
                _tongueParticles.Emit(emit, 1);
            }
        }

        private void EmitEmber(int count, float burst)
        {
            if (_emberParticles == null)
            {
                return;
            }

            bool highest = _phase == SettlementPacePhase.DoubleTarget;
            for (int i = 0; i < count; i++)
            {
                float x = NextSigned() * (highest ? 48f : 38f) * Mathf.Lerp(0.3f, 1f, Next01());
                var emit = new ParticleSystem.EmitParams
                {
                    position = new Vector3(x, RandomRange(-10f, 24f), 0f),
                    velocity = new Vector3(
                        NextSigned() * RandomRange(15f, highest ? 42f : 33f),
                        RandomRange(highest ? 145f : 128f, highest ? 220f : 184f) * (1f + burst * 0.35f),
                        0f),
                    startLifetime = RandomRange(0.42f, highest ? 0.87f : 0.73f),
                    startSize = RandomRange(highest ? 7f : 6f, highest ? 13f : 11f),
                    startColor = Color.Lerp(_tongueRed, _emberColor, RandomRange(0.45f, 1f)),
                    rotation = RandomRange(-24f, 24f),
                    angularVelocity = RandomRange(-75f, 75f),
                    randomSeed = NextUInt(),
                };
                _emberParticles.Emit(emit, 1);
            }
        }

        private void EnsureParticleRenderer()
        {
            _rootRect ??= transform as RectTransform;
            if (_rootRect == null)
            {
                return;
            }

            if (!_particlesConfigured)
            {
                _coreParticles = EnsureParticleSystem(_coreParticles, "CoreParticles", 72, 0xA1207B3Du);
                _tongueParticles = EnsureParticleSystem(_tongueParticles, "TongueParticles", 64, 0x53C911E7u);
                _emberParticles = EnsureParticleSystem(_emberParticles, "EmberParticles", 28, 0xE02B6C41u);
                ConfigureCoreSystem(_coreParticles);
                ConfigureTongueSystem(_tongueParticles);
                ConfigureEmberSystem(_emberParticles);
                _particlesConfigured = true;
            }
            EnsureMaterials();

            _glowGraphic = EnsureGraphic(_glowGraphic, "ParticleGlow");
            _bodyGraphic = EnsureGraphic(_bodyGraphic, "ParticleBody");
            _glowGraphic.transform.SetAsFirstSibling();
            _bodyGraphic.transform.SetSiblingIndex(1);
            Texture atlas = SettlementUiParticleGraphic.SharedAtlas;
            _glowGraphic.Configure(
                atlas,
                _glowMaterial,
                new SettlementUiParticleGraphic.Source(
                    _coreParticles,
                    SettlementUiParticleGraphic.ParticleVisualKind.Glow,
                    new Vector2(1.52f, 1.14f),
                    0.15f,
                    false));
            _bodyGraphic.Configure(
                atlas,
                _bodyMaterial,
                new SettlementUiParticleGraphic.Source(
                    _coreParticles,
                    SettlementUiParticleGraphic.ParticleVisualKind.Core,
                    new Vector2(1.12f, 0.90f),
                    1f,
                    false),
                new SettlementUiParticleGraphic.Source(
                    _tongueParticles,
                    SettlementUiParticleGraphic.ParticleVisualKind.Tongue,
                    new Vector2(0.91f, 1.46f),
                    1f,
                    true),
                new SettlementUiParticleGraphic.Source(
                    _emberParticles,
                    SettlementUiParticleGraphic.ParticleVisualKind.Ember,
                    new Vector2(0.72f, 1.12f),
                    0.95f,
                    true));
        }

        private void EnsureScreenFever(RectTransform presentationRoot)
        {
            if (presentationRoot == null)
            {
                return;
            }

            if (_screenFever == null)
            {
                Transform existing = presentationRoot.Find("SettlementFeverScreen");
                _screenFever = existing != null
                    ? existing.GetComponent<SettlementFeverScreenView>()
                    : null;
            }

            if (_screenFever == null)
            {
                var feverObject = new GameObject(
                    "SettlementFeverScreen",
                    typeof(RectTransform),
                    typeof(CanvasGroup));
                feverObject.transform.SetParent(presentationRoot, false);
                _screenFever = feverObject.AddComponent<SettlementFeverScreenView>();
            }

            _screenFever.Bind(presentationRoot);
        }

        private ParticleSystem EnsureParticleSystem(
            ParticleSystem existing,
            string objectName,
            int maxParticles,
            uint seed)
        {
            ParticleSystem system = existing;
            if (system == null)
            {
                Transform child = _rootRect.Find(objectName);
                system = child != null ? child.GetComponent<ParticleSystem>() : null;
            }

            if (system == null)
            {
                var childObject = new GameObject(objectName, typeof(RectTransform), typeof(ParticleSystem));
                childObject.transform.SetParent(_rootRect, false);
                system = childObject.GetComponent<ParticleSystem>();
            }

            system.gameObject.name = objectName;
            system.gameObject.SetActive(true);
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.transform.localPosition = Vector3.zero;
            system.transform.localRotation = Quaternion.identity;
            system.transform.localScale = Vector3.one;
            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 8f;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.useUnscaledTime = false;
            main.simulationSpeed = _effectiveSpeed;
            system.useAutoRandomSeed = false;
            system.randomSeed = seed;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }

            return system;
        }

        private void ConfigureCoreSystem(ParticleSystem system)
        {
            if (system == null)
            {
                return;
            }

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.34f),
                new Keyframe(0.12f, 1f),
                new Keyframe(0.62f, 0.78f),
                new Keyframe(1f, 0.08f)));
            var color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(BuildAlphaGradient(0.95f, 0.88f, 0f));
            ConfigureNoise(system, 13f, 0.48f, 0.54f);
        }

        private void ConfigureTongueSystem(ParticleSystem system)
        {
            if (system == null)
            {
                return;
            }

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.42f),
                new Keyframe(0.10f, 1f),
                new Keyframe(0.52f, 0.74f),
                new Keyframe(1f, 0.03f)));
            var color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(BuildAlphaGradient(0.92f, 0.82f, 0f));
            ConfigureNoise(system, 24f, 0.62f, 0.82f);
        }

        private void ConfigureEmberSystem(ParticleSystem system)
        {
            if (system == null)
            {
                return;
            }

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.72f),
                new Keyframe(0.22f, 1f),
                new Keyframe(1f, 0.12f)));
            var color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(BuildAlphaGradient(0.92f, 0.68f, 0f));
            ConfigureNoise(system, 17f, 0.76f, 1.1f);
            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = 0.16f;
        }

        private static void ConfigureNoise(ParticleSystem system, float strength, float frequency, float scroll)
        {
            var noise = system.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = strength;
            noise.strengthY = strength * 0.08f;
            noise.strengthZ = 0f;
            noise.frequency = frequency;
            noise.scrollSpeed = scroll;
            noise.damping = true;
            noise.octaveCount = 2;
            noise.octaveMultiplier = 0.48f;
            noise.octaveScale = 1.85f;
            noise.quality = ParticleSystemNoiseQuality.Medium;
        }

        private void EnsureMaterials()
        {
            Shader shader = Shader.Find(ParticleShaderName);
            if (shader == null)
            {
                Debug.LogError($"未找到结算 UI 粒子 Shader：{ParticleShaderName}", this);
                return;
            }

            if (_bodyMaterial == null)
            {
                _bodyMaterial = CreateRuntimeMaterial(shader, "Settlement UI Particle Body");
                _bodyMaterial.SetFloat("_SrcBlend", (float)BlendMode.One);
                _bodyMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                _bodyMaterial.SetFloat("_Additive", 0f);
            }

            if (_glowMaterial == null)
            {
                _glowMaterial = CreateRuntimeMaterial(shader, "Settlement UI Particle Glow");
                _glowMaterial.SetFloat("_SrcBlend", (float)BlendMode.One);
                _glowMaterial.SetFloat("_DstBlend", (float)BlendMode.One);
                _glowMaterial.SetFloat("_Additive", 1f);
            }
        }

        private static Material CreateRuntimeMaterial(Shader shader, string materialName)
        {
            return new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        private SettlementUiParticleGraphic EnsureGraphic(
            SettlementUiParticleGraphic existing,
            string objectName)
        {
            if (existing != null)
            {
                return existing;
            }

            Transform child = _rootRect.Find(objectName);
            SettlementUiParticleGraphic graphic = child != null
                ? child.GetComponent<SettlementUiParticleGraphic>()
                : null;
            if (graphic == null)
            {
                var graphicObject = new GameObject(
                    objectName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(SettlementUiParticleGraphic));
                graphicObject.transform.SetParent(_rootRect, false);
                graphic = graphicObject.GetComponent<SettlementUiParticleGraphic>();
            }

            StretchGraphic(graphic);
            graphic.raycastTarget = false;
            return graphic;
        }

        private static void StretchGraphic(Graphic graphic)
        {
            if (graphic == null)
            {
                return;
            }

            RectTransform rect = graphic.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private static Gradient BuildAlphaGradient(float startAlpha, float middleAlpha, float endAlpha)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(startAlpha, 0.08f),
                    new GradientAlphaKey(middleAlpha, 0.58f),
                    new GradientAlphaKey(endAlpha, 1f),
                });
            return gradient;
        }

        private void ApplyPulseScale()
        {
            if (_bodyGraphic == null || _glowGraphic == null)
            {
                return;
            }

            float compression = _burstCompression > 0f && _queuedBurstStrength > 0f
                ? Mathf.Clamp01(_burstCompression * 8f)
                : 0f;
            float width = 1f + _burstPulse * 0.08f + compression * 0.06f;
            float height = 1f + _burstPulse * 0.23f - compression * 0.14f;
            _bodyGraphic.SetPulse(width, height);
            _glowGraphic.SetPulse(width * 1.02f, Mathf.Lerp(1f, height, 0.72f));
        }

        private void ResetVisuals()
        {
            EnsureParticleRenderer();
            StopAndClearParticles();
            ResetRuntimeState();
            if (_glowGraphic != null)
            {
                _glowGraphic.gameObject.SetActive(false);
            }

            if (_bodyGraphic != null)
            {
                _bodyGraphic.gameObject.SetActive(false);
            }
        }

        private void ResetRuntimeState()
        {
            _coreAccumulator = 0f;
            _tongueAccumulator = 0f;
            _emberAccumulator = 0f;
            _burstCompression = 0f;
            _burstPulse = 0f;
            _queuedBurstStrength = 0f;
            _burstDelayFrames = 0;
            ApplyPulseScale();
        }

        private void StopAndClearParticles()
        {
            StopAndClear(_coreParticles);
            StopAndClear(_tongueParticles);
            StopAndClear(_emberParticles);
            RefreshGraphics();
        }

        private static void StopAndClear(ParticleSystem system)
        {
            if (system != null)
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private static void StartParticleSystem(ParticleSystem system)
        {
            if (system != null && !system.isPlaying)
            {
                system.Play(true);
            }
        }

        private static void SetSimulationSpeed(ParticleSystem system, float speed)
        {
            if (system != null)
            {
                var main = system.main;
                main.simulationSpeed = speed;
            }
        }

        private void RefreshGraphics()
        {
            _glowGraphic?.SetVerticesDirty();
            _bodyGraphic?.SetVerticesDirty();
        }

        private float RateForPhase(float targetRate, float doubleRate)
        {
            return _phase switch
            {
                SettlementPacePhase.TargetReached => targetRate,
                SettlementPacePhase.DoubleTarget => doubleRate,
                _ => 0f,
            };
        }

        private static bool IsRunning(ParticleSystem system)
        {
            return system != null && system.isPlaying;
        }

        private static int ParticleCount(ParticleSystem system)
        {
            return system != null ? system.particleCount : 0;
        }

        private static int ConsumeWholeParticles(ref float accumulator)
        {
            int count = Mathf.FloorToInt(accumulator);
            accumulator -= count;
            return count;
        }

        private float RandomRange(float minimum, float maximum)
        {
            return Mathf.Lerp(minimum, maximum, Next01());
        }

        private float NextSigned()
        {
            return Next01() * 2f - 1f;
        }

        private float Next01()
        {
            return (NextUInt() & 0x00FFFFFFu) / 16777216f;
        }

        private uint NextUInt()
        {
            uint value = _randomState;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _randomState = value == 0u ? 0x7A4D21E9u : value;
            return _randomState;
        }

        private void OnDisable()
        {
            _visible = false;
            _phase = SettlementPacePhase.BelowTarget;
            _effectiveSpeed = 1f;
            StopAndClearParticles();
            ResetRuntimeState();
        }

        private void OnDestroy()
        {
            DestroyRuntimeMaterial(ref _bodyMaterial);
            DestroyRuntimeMaterial(ref _glowMaterial);
        }

        private static void DestroyRuntimeMaterial(ref Material material)
        {
            if (material == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }

            material = null;
        }
    }
}
