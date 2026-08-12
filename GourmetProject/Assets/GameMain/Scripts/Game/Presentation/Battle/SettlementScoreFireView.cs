using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 结算分数后的程序化火焰。作为整个 ScoreMeter 的背景统一承托三行分数文本；
    /// 轮廓和翻涌完全由 UI Shader 生成，不依赖火焰贴图。
    /// </summary>
    public sealed class SettlementScoreFireView : MonoBehaviour
    {
        private const string ShaderName = "GourmetProject/SettlementScoreFlame";
        private static readonly int FlameTimeId = Shader.PropertyToID("_FlameTime");
        private static readonly int AmountId = Shader.PropertyToID("_Amount");
        private static readonly int Colour1Id = Shader.PropertyToID("_Colour1");
        private static readonly int Colour2Id = Shader.PropertyToID("_Colour2");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");

        [Header("旧粒子（迁移时自动禁用）")]
        [SerializeField] private ParticleSystem _particles;

        [Header("Balatro 风格红焰")]
        [SerializeField] private Color _targetOuter = new(0.68f, 0.14f, 0.025f, 1f);
        [SerializeField] private Color _targetCore = new(1f, 0.48f, 0.08f, 1f);
        [SerializeField] private Color _doubleOuter = new(0.78f, 0.16f, 0.02f, 1f);
        [SerializeField] private Color _doubleCore = new(1f, 0.64f, 0.16f, 1f);
        [SerializeField] private float _targetAmount = 1.65f;
        [SerializeField] private float _doubleTargetAmount = 3.25f;

        private RectTransform _rootRect;
        private Image _flameImage;
        private Material _runtimeMaterial;
        private SettlementPacePhase _phase;
        private float _effectiveSpeed = 1f;
        private float _flameTime;
        private float _currentAmount;
        private float _burstPulse;
        private bool _visible;

        internal SettlementPacePhase Phase => _phase;
        internal int ActiveMoteCount =>
            _visible && _phase != SettlementPacePhase.BelowTarget ? 1 : 0;

        private void Awake()
        {
            EnsureShaderRenderer();
            ResetVisuals();
        }

        /// <summary>把火焰铺满 ScoreMeter，并固定在三行分数文本的后一层。</summary>
        public void BindToScore(RectTransform scoreAnchor)
        {
            RectTransform scoreMeter = scoreAnchor != null
                ? scoreAnchor.parent as RectTransform
                : null;
            if (scoreMeter == null)
            {
                return;
            }

            EnsureShaderRenderer();
            _rootRect.SetParent(scoreMeter, false);
            _rootRect.anchorMin = Vector2.zero;
            _rootRect.anchorMax = Vector2.one;
            _rootRect.pivot = new Vector2(0.5f, 0.5f);
            _rootRect.anchoredPosition = Vector2.zero;
            _rootRect.sizeDelta = new Vector2(-10f, -8f);
            _rootRect.localScale = Vector3.one;
            _rootRect.localRotation = Quaternion.identity;
            _rootRect.SetAsFirstSibling();
        }

        public void Show()
        {
            EnsureShaderRenderer();
            gameObject.SetActive(true);
            _visible = true;
            SetPhase(SettlementPacePhase.BelowTarget, 1f);
        }

        public void Hide()
        {
            _visible = false;
            _phase = SettlementPacePhase.BelowTarget;
            _effectiveSpeed = 1f;
            _flameTime = 0f;
            _currentAmount = 0f;
            _burstPulse = 0f;
            ResetVisuals();
            gameObject.SetActive(false);
        }

        internal void SetPhase(SettlementPacePhase phase, float effectiveSpeed)
        {
            EnsureShaderRenderer();
            _phase = phase;
            _effectiveSpeed = Mathf.Max(0.0001f, effectiveSpeed);
            bool ignited = phase != SettlementPacePhase.BelowTarget;
            _flameImage.gameObject.SetActive(ignited);

            if (!ignited)
            {
                _currentAmount = 0f;
                _burstPulse = 0f;
            }
            else if (_currentAmount <= 0f)
            {
                _currentAmount = phase == SettlementPacePhase.DoubleTarget
                    ? _doubleTargetAmount
                    : _targetAmount;
            }

            ApplyMaterialProperties();
        }

        /// <summary>强结果、升档和最终盖章时提高一小段火势；未达标阶段不会点火。</summary>
        public void Burst(float strength = 1f)
        {
            EnsureShaderRenderer();
            if (_phase == SettlementPacePhase.BelowTarget)
            {
                return;
            }

            if (!_visible)
            {
                gameObject.SetActive(true);
                _visible = true;
            }

            _burstPulse = Mathf.Max(_burstPulse, Mathf.Lerp(0.35f, 1.45f, Mathf.Clamp01(strength)));
            ApplyMaterialProperties();
        }

        private void Update()
        {
            if (!_visible || _phase == SettlementPacePhase.BelowTarget || _runtimeMaterial == null)
            {
                return;
            }

            // 使用结算自己的有效速度推进，暂停时 deltaTime=0，火焰会同步冻结。
            float frameDelta = Time.deltaTime;
            if (frameDelta <= 0f)
            {
                return;
            }

            float animationDelta = frameDelta * _effectiveSpeed;
            _flameTime += animationDelta;
            _burstPulse = Mathf.MoveTowards(_burstPulse, 0f, animationDelta * 2.8f);
            float phaseAmount = _phase == SettlementPacePhase.DoubleTarget
                ? _doubleTargetAmount
                : _targetAmount;
            _currentAmount = Mathf.MoveTowards(_currentAmount, phaseAmount, animationDelta * 4.5f);
            ApplyMaterialProperties();
        }

        private void EnsureShaderRenderer()
        {
            if (_rootRect == null)
            {
                _rootRect = transform as RectTransform;
            }

            if (_particles != null)
            {
                _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _particles.gameObject.SetActive(false);
            }

            if (_flameImage == null)
            {
                var imageObject = new GameObject(
                    "ProceduralFlame",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                RectTransform rect = imageObject.GetComponent<RectTransform>();
                rect.SetParent(_rootRect, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                _flameImage = imageObject.GetComponent<Image>();
                _flameImage.raycastTarget = false;
                _flameImage.color = Color.white;
            }

            if (_runtimeMaterial == null)
            {
                Shader shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    Debug.LogError($"未找到结算火焰 Shader：{ShaderName}", this);
                    return;
                }

                _runtimeMaterial = new Material(shader)
                {
                    name = "SettlementScoreFlame (Runtime)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _runtimeMaterial.SetFloat(SeedId, Mathf.Abs(GetInstanceID()) * 0.01781f + 3.7f);
                _flameImage.material = _runtimeMaterial;
            }
        }

        private void ApplyMaterialProperties()
        {
            if (_runtimeMaterial == null)
            {
                return;
            }

            bool doubleTarget = _phase == SettlementPacePhase.DoubleTarget;
            _runtimeMaterial.SetFloat(FlameTimeId, _flameTime);
            _runtimeMaterial.SetFloat(AmountId, _currentAmount + _burstPulse);
            _runtimeMaterial.SetColor(Colour1Id, doubleTarget ? _doubleOuter : _targetOuter);
            _runtimeMaterial.SetColor(Colour2Id, doubleTarget ? _doubleCore : _targetCore);
        }

        private void ResetVisuals()
        {
            if (_flameImage != null)
            {
                _flameImage.gameObject.SetActive(false);
            }

            ApplyMaterialProperties();
        }

        private void OnDisable()
        {
            if (_visible)
            {
                _visible = false;
                _phase = SettlementPacePhase.BelowTarget;
                _currentAmount = 0f;
                _burstPulse = 0f;
                ResetVisuals();
            }
        }

        private void OnDestroy()
        {
            if (_runtimeMaterial == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_runtimeMaterial);
            }
            else
            {
                DestroyImmediate(_runtimeMaterial);
            }

            _runtimeMaterial = null;
        }
    }
}
