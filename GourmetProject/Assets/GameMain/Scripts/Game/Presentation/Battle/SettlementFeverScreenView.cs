using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 结算达标后的全屏高温氛围：程序化热浪/边缘火光，以及由标准 ParticleSystem
    /// 驱动、通过 UGUI 绘制的烟团与火星。它只消费结算节奏，不参与任何计分逻辑。
    /// </summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    public sealed class SettlementFeverScreenView : MonoBehaviour
    {
        private const string HeatShaderName = "GourmetProject/SettlementFeverScreen";
        private const string ParticleShaderName = "GourmetProject/SettlementUiParticle";
        private const float TargetOpacity = 0.65f;
        private const float DoubleTargetOpacity = 1f;
        private const float TargetSmokeRate = 2.1f;
        private const float DoubleTargetSmokeRate = 4.0f;
        private const float TargetEmberRate = 16f;
        private const float DoubleTargetEmberRate = 34f;
        private const float FadeInDuration = 0.22f;
        private const float FadeOutDuration = 0.35f;
        private const float BurstDuration = 0.18f;
        private const int MaxSmokeParticles = 24;
        private const int MaxEmberParticles = 96;

        private static readonly string[] OverlayLayerNames =
        {
            "RewardSubflowLayer",
            "InspectionLayer",
            "FragmentEditLayer",
            "CenterTransitionCover",
        };

        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int PulseId = Shader.PropertyToID("_Pulse");
        private static readonly int FeverTimeId = Shader.PropertyToID("_FeverTime");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");

        private RectTransform _rootRect;
        private CanvasGroup _canvasGroup;
        private Image _heatImage;
        private ParticleSystem _smokeParticles;
        private ParticleSystem _emberParticles;
        private SettlementUiParticleGraphic _smokeGraphic;
        private SettlementUiParticleGraphic _emberGraphic;
        private Material _heatMaterial;
        private Material _smokeMaterial;
        private Material _emberMaterial;
        private SettlementPacePhase _phase;
        private float _effectiveSpeed = 1f;
        private float _smokeAccumulator;
        private float _emberAccumulator;
        private float _effectTime;
        private float _burstPulse;
        private float _fadeStart;
        private float _fadeTarget;
        private float _fadeElapsed;
        private float _fadeDuration;
        private uint _randomState = 0xC44F03A7u;
        private bool _visualsReady;
        private bool _hideWhenFadeCompletes;

        internal SettlementPacePhase Phase => _phase;

        internal float CurrentOpacity => _canvasGroup != null ? _canvasGroup.alpha : 0f;

        internal float SmokeEmissionRate => SmokeRateForPhase(_phase);

        internal float EmberEmissionRate => EmberRateForPhase(_phase);

        internal int ActiveMoteCount => ParticleCount(_smokeParticles) + ParticleCount(_emberParticles);

        internal int SmokeParticleLimit => MaxSmokeParticles;

        internal int EmberParticleLimit => MaxEmberParticles;

        private void Awake()
        {
            EnsureVisuals();
            ResetImmediate(deactivate: true);
        }

        /// <summary>铺满战斗 HudFrame，并插在奖励、检查和页面转场层之前。</summary>
        public void Bind(RectTransform presentationRoot)
        {
            if (presentationRoot == null)
            {
                return;
            }

            EnsureVisuals();
            _rootRect.SetParent(presentationRoot, false);
            Stretch(_rootRect);
            PlaceBelowOverlayLayers(presentationRoot);
        }

        public void Show()
        {
            EnsureVisuals();
            gameObject.SetActive(true);
            _hideWhenFadeCompletes = false;
            _phase = SettlementPacePhase.BelowTarget;
            _effectiveSpeed = 1f;
            StopAndClearParticles();
            ResetRuntimeState();
            SetOpacityImmediate(0f);
            RefreshMaterial();
        }

        /// <summary>停止发射并保留短暂余焰，随后在 0.35 秒内淡出和清空。</summary>
        public void Hide()
        {
            if (!_visualsReady || !gameObject.activeSelf)
            {
                ResetImmediate(deactivate: true);
                return;
            }

            _phase = SettlementPacePhase.BelowTarget;
            _effectiveSpeed = 1f;
            SetSimulationSpeed(_smokeParticles, _effectiveSpeed);
            SetSimulationSpeed(_emberParticles, _effectiveSpeed);
            _smokeAccumulator = 0f;
            _emberAccumulator = 0f;
            _hideWhenFadeCompletes = true;
            BeginFade(0f, FadeOutDuration);
        }

        internal void SetPhase(SettlementPacePhase phase, float effectiveSpeed)
        {
            EnsureVisuals();
            _phase = phase;
            _effectiveSpeed = Mathf.Max(0.0001f, effectiveSpeed);
            SetSimulationSpeed(_smokeParticles, _effectiveSpeed);
            SetSimulationSpeed(_emberParticles, _effectiveSpeed);

            float targetOpacity = OpacityForPhase(phase);
            if (phase == SettlementPacePhase.BelowTarget)
            {
                _smokeAccumulator = 0f;
                _emberAccumulator = 0f;
                StopAndClearParticles();
                BeginFade(0f, FadeOutDuration);
                return;
            }

            gameObject.SetActive(true);
            bool wasCold = CurrentOpacity <= 0.001f && ActiveMoteCount == 0;
            _hideWhenFadeCompletes = false;
            StartParticleSystem(_smokeParticles);
            StartParticleSystem(_emberParticles);
            BeginFade(targetOpacity, FadeInDuration);
            if (wasCold)
            {
                EmitSmoke(4, 0.58f);
                EmitEmbers(18, 0.68f);
            }
            RefreshGraphics();
        }

        /// <summary>结算强结果、升档和最终确认时同步触发全屏热浪脉冲与火星喷发。</summary>
        public void Burst(float strength = 1f)
        {
            if (_phase == SettlementPacePhase.BelowTarget)
            {
                return;
            }

            EnsureVisuals();
            gameObject.SetActive(true);
            float clamped = Mathf.Clamp01(strength);
            _burstPulse = Mathf.Max(_burstPulse, clamped);
            EmitSmoke(Mathf.RoundToInt(Mathf.Lerp(2f, 6f, clamped)), clamped);
            EmitEmbers(Mathf.RoundToInt(Mathf.Lerp(14f, 44f, clamped)), clamped);
            RefreshGraphics();
            RefreshMaterial();
        }

        private void Update()
        {
            if (!_visualsReady || !gameObject.activeSelf)
            {
                return;
            }

            Advance(Time.deltaTime);
        }

        internal void Advance(float frameDelta)
        {
            if (frameDelta > 0f)
            {
                UpdateFade(frameDelta);
                float animationDelta = frameDelta * _effectiveSpeed;
                _effectTime += animationDelta;
                EmitContinuous(animationDelta);
                _burstPulse = Mathf.MoveTowards(
                    _burstPulse,
                    0f,
                    frameDelta / Mathf.Max(0.0001f, BurstDuration));
            }

            RefreshMaterial();
        }

        private void EmitContinuous(float animationDelta)
        {
            if (_phase == SettlementPacePhase.BelowTarget)
            {
                return;
            }

            _smokeAccumulator += SmokeEmissionRate * animationDelta;
            _emberAccumulator += EmberEmissionRate * animationDelta;
            int smokeCount = ConsumeWholeParticles(ref _smokeAccumulator);
            int emberCount = ConsumeWholeParticles(ref _emberAccumulator);
            if (smokeCount > 0)
            {
                EmitSmoke(smokeCount, 0f);
            }
            if (emberCount > 0)
            {
                EmitEmbers(emberCount, 0f);
            }
        }

        private void EmitSmoke(int count, float burst)
        {
            if (_smokeParticles == null || count <= 0)
            {
                return;
            }

            Rect rect = _rootRect.rect;
            float width = Mathf.Max(320f, rect.width);
            float height = Mathf.Max(180f, rect.height);
            bool hottest = _phase == SettlementPacePhase.DoubleTarget;
            for (int i = 0; i < count; i++)
            {
                float side = NextSigned();
                float horizontalBias = Mathf.Sign(side) * Mathf.Pow(Mathf.Abs(side), 0.58f);
                float baseAlpha = hottest ? 0.19f : 0.135f;
                var emit = new ParticleSystem.EmitParams
                {
                    position = new Vector3(
                        horizontalBias * width * RandomRange(0.22f, 0.53f),
                        -height * RandomRange(0.22f, 0.51f),
                        0f),
                    velocity = new Vector3(
                        -horizontalBias * RandomRange(4f, 24f) + NextSigned() * 12f,
                        RandomRange(42f, hottest ? 92f : 72f) * (1f + burst * 0.28f),
                        0f),
                    startLifetime = RandomRange(2.5f, hottest ? 4.4f : 3.8f),
                    startSize = Mathf.Min(width, height * 1.35f)
                        * RandomRange(hottest ? 0.28f : 0.24f, hottest ? 0.52f : 0.44f),
                    startColor = Color.Lerp(
                        new Color(0.23f, 0.018f, 0.002f, baseAlpha),
                        new Color(0.70f, 0.075f, 0.006f, baseAlpha * 0.82f),
                        RandomRange(0.12f, 0.72f)),
                    rotation = RandomRange(-180f, 180f),
                    angularVelocity = RandomRange(-8f, 8f),
                    randomSeed = NextUInt(),
                };
                _smokeParticles.Emit(emit, 1);
            }
        }

        private void EmitEmbers(int count, float burst)
        {
            if (_emberParticles == null || count <= 0)
            {
                return;
            }

            Rect rect = _rootRect.rect;
            float width = Mathf.Max(320f, rect.width);
            float height = Mathf.Max(180f, rect.height);
            bool hottest = _phase == SettlementPacePhase.DoubleTarget;
            for (int i = 0; i < count; i++)
            {
                // 爆发时把更多火星直接铺到两侧的不同高度，避免刚升档的前几帧
                // 只有屏幕下沿发亮；持续发射仍主要从底部升起，保留热气上涌方向。
                bool fromSide = Next01() < Mathf.Lerp(0.34f, 0.56f, burst);
                float side = NextSigned() < 0f ? -1f : 1f;
                float x = fromSide
                    ? side * width * RandomRange(0.47f, 0.53f)
                    : NextSigned() * width * RandomRange(0.06f, 0.50f);
                float y = fromSide
                    ? NextSigned() * height * RandomRange(0.08f, 0.46f)
                    : -height * RandomRange(0.43f, 0.52f);
                float inwardVelocity = fromSide ? -side * RandomRange(22f, 64f) : 0f;
                float hotChance = Next01();
                var emit = new ParticleSystem.EmitParams
                {
                    position = new Vector3(x, y, 0f),
                    velocity = new Vector3(
                        inwardVelocity + NextSigned() * RandomRange(8f, hottest ? 34f : 25f),
                        RandomRange(hottest ? 155f : 125f, hottest ? 280f : 225f)
                            * (1f + burst * 0.34f),
                        0f),
                    startLifetime = RandomRange(0.75f, hottest ? 1.85f : 1.55f),
                    startSize = RandomRange(hottest ? 9f : 8f, hottest ? 21f : 17f)
                        * (1f + burst * 0.18f),
                    startColor = hotChance < 0.18f
                        ? new Color(1f, 0.78f, 0.22f, 0.98f)
                        : Color.Lerp(
                            new Color(1f, 0.16f, 0.015f, 0.88f),
                            new Color(1f, 0.52f, 0.035f, 0.96f),
                            RandomRange(0.18f, 0.90f)),
                    rotation = RandomRange(-28f, 28f),
                    angularVelocity = RandomRange(-75f, 75f),
                    randomSeed = NextUInt(),
                };
                _emberParticles.Emit(emit, 1);
            }
        }

        private void EnsureVisuals()
        {
            if (_visualsReady)
            {
                return;
            }

            _rootRect ??= transform as RectTransform;
            _canvasGroup ??= GetComponent<CanvasGroup>();
            if (_rootRect == null || _canvasGroup == null)
            {
                return;
            }

            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.ignoreParentGroups = false;
            _heatImage = EnsureImage(_heatImage, "HeatLayer");
            _smokeParticles = EnsureParticleSystem(
                _smokeParticles,
                "SmokeParticles",
                MaxSmokeParticles,
                0x8F2A1C61u);
            _emberParticles = EnsureParticleSystem(
                _emberParticles,
                "ScreenEmberParticles",
                MaxEmberParticles,
                0x361DB54Fu);
            ConfigureSmokeSystem(_smokeParticles);
            ConfigureEmberSystem(_emberParticles);
            EnsureMaterials();
            _smokeGraphic = EnsureParticleGraphic(_smokeGraphic, "SmokeGraphic");
            _emberGraphic = EnsureParticleGraphic(_emberGraphic, "EmberGraphic");

            _heatImage.transform.SetAsFirstSibling();
            _smokeGraphic.transform.SetSiblingIndex(1);
            _emberGraphic.transform.SetSiblingIndex(2);
            Texture atlas = SettlementUiParticleGraphic.SharedAtlas;
            _smokeGraphic.Configure(
                atlas,
                _smokeMaterial,
                new SettlementUiParticleGraphic.Source(
                    _smokeParticles,
                    SettlementUiParticleGraphic.ParticleVisualKind.Glow,
                    new Vector2(1.72f, 1.05f),
                    0.70f,
                    false));
            _emberGraphic.Configure(
                atlas,
                _emberMaterial,
                new SettlementUiParticleGraphic.Source(
                    _emberParticles,
                    SettlementUiParticleGraphic.ParticleVisualKind.Ember,
                    new Vector2(0.62f, 1.48f),
                    0.96f,
                    true));
            _visualsReady = true;
        }

        private Image EnsureImage(Image existing, string objectName)
        {
            if (existing != null)
            {
                return existing;
            }

            Transform child = _rootRect.Find(objectName);
            Image image = child != null ? child.GetComponent<Image>() : null;
            if (image == null)
            {
                var imageObject = new GameObject(
                    objectName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                imageObject.transform.SetParent(_rootRect, false);
                image = imageObject.GetComponent<Image>();
            }

            Stretch(image.rectTransform);
            image.color = Color.white;
            image.raycastTarget = false;
            image.maskable = false;
            return image;
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
                var particleObject = new GameObject(
                    objectName,
                    typeof(RectTransform),
                    typeof(ParticleSystem));
                particleObject.transform.SetParent(_rootRect, false);
                system = particleObject.GetComponent<ParticleSystem>();
            }

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

        private static void ConfigureSmokeSystem(ParticleSystem system)
        {
            if (system == null)
            {
                return;
            }

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.42f),
                new Keyframe(0.18f, 0.90f),
                new Keyframe(0.70f, 1f),
                new Keyframe(1f, 0.72f)));
            var color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(BuildAlphaGradient(0.82f, 0.62f));
            var noise = system.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = 24f;
            noise.strengthY = 5f;
            noise.strengthZ = 0f;
            noise.frequency = 0.30f;
            noise.scrollSpeed = 0.22f;
            noise.damping = true;
            noise.octaveCount = 2;
            noise.octaveMultiplier = 0.48f;
            noise.octaveScale = 1.82f;
            noise.quality = ParticleSystemNoiseQuality.Medium;
        }

        private static void ConfigureEmberSystem(ParticleSystem system)
        {
            if (system == null)
            {
                return;
            }

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.32f),
                new Keyframe(0.10f, 1f),
                new Keyframe(0.72f, 0.76f),
                new Keyframe(1f, 0.08f)));
            var color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(BuildAlphaGradient(0.95f, 0.74f));
            var noise = system.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = 22f;
            noise.strengthY = 2f;
            noise.strengthZ = 0f;
            noise.frequency = 0.72f;
            noise.scrollSpeed = 0.96f;
            noise.damping = true;
            noise.octaveCount = 2;
            noise.octaveMultiplier = 0.46f;
            noise.octaveScale = 1.76f;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = 0.10f;
        }

        private void EnsureMaterials()
        {
            Shader heatShader = Shader.Find(HeatShaderName);
            if (heatShader == null)
            {
                if (_heatImage != null)
                {
                    _heatImage.enabled = false;
                }
                Debug.LogError($"未找到结算全屏热浪 Shader：{HeatShaderName}", this);
            }
            else
            {
                _heatMaterial ??= CreateRuntimeMaterial(heatShader, "Settlement Fever Screen Heat");
                _heatImage.material = _heatMaterial;
                _heatImage.enabled = true;
            }

            Shader particleShader = Shader.Find(ParticleShaderName);
            if (particleShader == null)
            {
                Debug.LogError($"未找到结算 UI 粒子 Shader：{ParticleShaderName}", this);
                return;
            }

            if (_smokeMaterial == null)
            {
                _smokeMaterial = CreateRuntimeMaterial(particleShader, "Settlement Fever Screen Smoke");
                _smokeMaterial.SetFloat("_SrcBlend", (float)BlendMode.One);
                _smokeMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                _smokeMaterial.SetFloat("_Additive", 0f);
            }
            if (_emberMaterial == null)
            {
                _emberMaterial = CreateRuntimeMaterial(particleShader, "Settlement Fever Screen Embers");
                _emberMaterial.SetFloat("_SrcBlend", (float)BlendMode.One);
                _emberMaterial.SetFloat("_DstBlend", (float)BlendMode.One);
                _emberMaterial.SetFloat("_Additive", 1f);
            }
        }

        private SettlementUiParticleGraphic EnsureParticleGraphic(
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

            Stretch(graphic.rectTransform);
            graphic.raycastTarget = false;
            graphic.maskable = false;
            return graphic;
        }

        private void BeginFade(float target, float duration)
        {
            _fadeStart = CurrentOpacity;
            _fadeTarget = Mathf.Clamp01(target);
            _fadeElapsed = 0f;
            _fadeDuration = Mathf.Max(0.0001f, duration);
            if (Mathf.Abs(_fadeStart - _fadeTarget) <= 0.0001f)
            {
                SetOpacityImmediate(_fadeTarget);
            }
        }

        private void UpdateFade(float frameDelta)
        {
            if (_canvasGroup == null || Mathf.Abs(_canvasGroup.alpha - _fadeTarget) <= 0.0001f)
            {
                CompleteFadeIfNeeded();
                return;
            }

            _fadeElapsed += frameDelta;
            float normalized = Mathf.Clamp01(_fadeElapsed / _fadeDuration);
            float eased = normalized * normalized * (3f - 2f * normalized);
            _canvasGroup.alpha = Mathf.Lerp(_fadeStart, _fadeTarget, eased);
            if (normalized >= 1f)
            {
                _canvasGroup.alpha = _fadeTarget;
                CompleteFadeIfNeeded();
            }
        }

        private void CompleteFadeIfNeeded()
        {
            if (!_hideWhenFadeCompletes || CurrentOpacity > 0.0001f)
            {
                return;
            }

            StopAndClearParticles();
            ResetRuntimeState();
            _hideWhenFadeCompletes = false;
            gameObject.SetActive(false);
        }

        private void RefreshMaterial()
        {
            if (_heatMaterial == null || _rootRect == null)
            {
                return;
            }

            Rect rect = _rootRect.rect;
            float aspect = Mathf.Abs(rect.height) > 0.0001f
                ? Mathf.Clamp(Mathf.Abs(rect.width / rect.height), 0.45f, 3.2f)
                : 16f / 9f;
            // CanvasGroup 负责 0→0.65→1 的总强度和淡入淡出；Shader 内保持单位强度，
            // 避免达标档被 alpha 再平方一次而显得过弱。
            _heatMaterial.SetFloat(IntensityId, 1f);
            _heatMaterial.SetFloat(PulseId, _burstPulse);
            _heatMaterial.SetFloat(FeverTimeId, _effectTime);
            _heatMaterial.SetFloat(AspectId, aspect);
        }

        private void RefreshGraphics()
        {
            _smokeGraphic?.SetVerticesDirty();
            _emberGraphic?.SetVerticesDirty();
        }

        private void StopAndClearParticles()
        {
            StopAndClear(_smokeParticles);
            StopAndClear(_emberParticles);
            RefreshGraphics();
        }

        private void ResetRuntimeState()
        {
            _smokeAccumulator = 0f;
            _emberAccumulator = 0f;
            _effectTime = 0f;
            _burstPulse = 0f;
            _fadeStart = CurrentOpacity;
            _fadeTarget = CurrentOpacity;
            _fadeElapsed = 0f;
            _fadeDuration = 0f;
        }

        private void ResetImmediate(bool deactivate)
        {
            _hideWhenFadeCompletes = false;
            _phase = SettlementPacePhase.BelowTarget;
            _effectiveSpeed = 1f;
            StopAndClearParticles();
            SetOpacityImmediate(0f);
            ResetRuntimeState();
            RefreshMaterial();
            if (deactivate && gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        private void SetOpacityImmediate(float value)
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = Mathf.Clamp01(value);
            }
            _fadeStart = CurrentOpacity;
            _fadeTarget = CurrentOpacity;
            _fadeElapsed = 0f;
            _fadeDuration = 0f;
        }

        private void PlaceBelowOverlayLayers(RectTransform presentationRoot)
        {
            int targetIndex = presentationRoot.childCount - 1;
            for (int i = 0; i < presentationRoot.childCount; i++)
            {
                Transform child = presentationRoot.GetChild(i);
                if (child == transform || !IsOverlayLayer(child.name))
                {
                    continue;
                }
                targetIndex = Mathf.Min(targetIndex, child.GetSiblingIndex());
            }
            _rootRect.SetSiblingIndex(Mathf.Max(0, targetIndex));
        }

        private static bool IsOverlayLayer(string objectName)
        {
            for (int i = 0; i < OverlayLayerNames.Length; i++)
            {
                if (string.Equals(objectName, OverlayLayerNames[i], System.StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static void Stretch(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private static Gradient BuildAlphaGradient(float peakAlpha, float middleAlpha)
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
                    new GradientAlphaKey(peakAlpha, 0.10f),
                    new GradientAlphaKey(middleAlpha, 0.68f),
                    new GradientAlphaKey(0f, 1f),
                });
            return gradient;
        }

        private static Material CreateRuntimeMaterial(Shader shader, string materialName)
        {
            return new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        internal static float OpacityForPhase(SettlementPacePhase phase) => phase switch
        {
            SettlementPacePhase.TargetReached => TargetOpacity,
            SettlementPacePhase.DoubleTarget => DoubleTargetOpacity,
            _ => 0f,
        };

        internal static float SmokeRateForPhase(SettlementPacePhase phase) => phase switch
        {
            SettlementPacePhase.TargetReached => TargetSmokeRate,
            SettlementPacePhase.DoubleTarget => DoubleTargetSmokeRate,
            _ => 0f,
        };

        internal static float EmberRateForPhase(SettlementPacePhase phase) => phase switch
        {
            SettlementPacePhase.TargetReached => TargetEmberRate,
            SettlementPacePhase.DoubleTarget => DoubleTargetEmberRate,
            _ => 0f,
        };

        private static int ParticleCount(ParticleSystem system) =>
            system != null ? system.particleCount : 0;

        private static int ConsumeWholeParticles(ref float accumulator)
        {
            int count = Mathf.FloorToInt(accumulator);
            accumulator -= count;
            return count;
        }

        private static void StartParticleSystem(ParticleSystem system)
        {
            if (system != null && !system.isPlaying)
            {
                system.Play(true);
            }
        }

        private static void StopAndClear(ParticleSystem system)
        {
            if (system != null)
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
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

        private float RandomRange(float minimum, float maximum) =>
            Mathf.Lerp(minimum, maximum, Next01());

        private float NextSigned() => Next01() * 2f - 1f;

        private float Next01() => (NextUInt() & 0x00FFFFFFu) / 16777216f;

        private uint NextUInt()
        {
            uint value = _randomState;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _randomState = value == 0u ? 0xC44F03A7u : value;
            return _randomState;
        }

        private void OnDisable()
        {
            ResetForPresentationDisable();
        }

        internal void ResetForPresentationDisable()
        {
            if (_visualsReady)
            {
                // BattleForm/HudFrame 可能绕过 Hide 直接关闭。此处同步复位，
                // 防止页面再次启用时残留淡出进度、热浪或历史粒子。
                ResetImmediate(deactivate: false);
            }
        }

        private void OnDestroy()
        {
            DestroyRuntimeMaterial(ref _heatMaterial);
            DestroyRuntimeMaterial(ref _smokeMaterial);
            DestroyRuntimeMaterial(ref _emberMaterial);
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
