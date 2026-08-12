using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 结算总分后的 UI 原生火焰。使用固定对象池在 Overlay Canvas 内绘制，
    /// 避免普通 ParticleSystemRenderer 无法参与 Canvas 合成的问题。
    /// </summary>
    public sealed class SettlementScoreFireView : MonoBehaviour
    {
        private const int DefaultPoolSize = 16;
        private const string MoteTexturePath = "Particles/Dust1";

        [Header("旧粒子（仅用于迁移时自动禁用）")]
        [SerializeField] private ParticleSystem _particles;

        [Header("UI 火焰")]
        [SerializeField, Min(4)] private int _poolSize = DefaultPoolSize;
        [SerializeField] private float _targetEmissionPerSecond = 8f;
        [SerializeField] private float _doubleTargetEmissionPerSecond = 18f;
        [SerializeField] private Color _emberColor = new(1f, 0.38f, 0.025f, 0.66f);
        [SerializeField] private Color _flameColor = new(1f, 0.72f, 0.06f, 0.80f);
        [SerializeField] private Color _hotColor = new(1f, 0.96f, 0.48f, 0.86f);

        private readonly List<FireMote> _motes = new(DefaultPoolSize);
        private RectTransform _rootRect;
        private Image _glow;
        private Sprite _runtimeSprite;
        private SettlementPacePhase _phase;
        private float _speed = 1f;
        private float _spawnAccumulator;
        private float _glowPulse;
        private int _nextMote;
        private bool _visible;

        internal SettlementPacePhase Phase => _phase;
        internal int ActiveMoteCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _motes.Count; i++)
                {
                    if (_motes[i].Active)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        private void Awake()
        {
            EnsureUiPool();
            ClearMotes();
            _visible = false;
        }

        /// <summary>把火焰移到分数表内部并排在当前分数文字后面。</summary>
        public void BindToScore(RectTransform scoreAnchor)
        {
            if (scoreAnchor == null || scoreAnchor.parent == null)
            {
                return;
            }

            EnsureUiPool();
            _rootRect.SetParent(scoreAnchor.parent, false);
            _rootRect.anchorMin = scoreAnchor.anchorMin;
            _rootRect.anchorMax = scoreAnchor.anchorMax;
            _rootRect.pivot = scoreAnchor.pivot;
            _rootRect.anchoredPosition = scoreAnchor.anchoredPosition;
            _rootRect.sizeDelta = new Vector2(170f, 110f);
            _rootRect.localScale = Vector3.one;
            _rootRect.localRotation = Quaternion.identity;
            _rootRect.SetSiblingIndex(scoreAnchor.GetSiblingIndex());
        }

        public void Show()
        {
            EnsureUiPool();
            gameObject.SetActive(true);
            _visible = true;
            SetPhase(SettlementPacePhase.BelowTarget, 1f);
        }

        public void Hide()
        {
            _visible = false;
            _phase = SettlementPacePhase.BelowTarget;
            _speed = 1f;
            _spawnAccumulator = 0f;
            _glowPulse = 0f;
            ClearMotes();
            if (_glow != null)
            {
                _glow.color = Color.clear;
            }

            gameObject.SetActive(false);
        }

        internal void SetPhase(SettlementPacePhase phase, float effectiveSpeed)
        {
            EnsureUiPool();
            _phase = phase;
            _speed = Mathf.Max(0.0001f, effectiveSpeed);
            if (phase == SettlementPacePhase.BelowTarget)
            {
                _spawnAccumulator = 0f;
                ClearMotes();
            }
        }

        /// <summary>强结果、升档和最终盖章时的瞬时爆燃；未达标阶段不会点火。</summary>
        public void Burst(float strength = 1f)
        {
            EnsureUiPool();
            if (_phase == SettlementPacePhase.BelowTarget)
            {
                return;
            }

            if (!_visible)
            {
                gameObject.SetActive(true);
                _visible = true;
            }

            float normalized = Mathf.Clamp01(strength);
            int maxBurst = _phase == SettlementPacePhase.DoubleTarget ? 16 : 8;
            int count = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(3f, maxBurst, normalized)));
            for (int i = 0; i < count; i++)
            {
                SpawnMote(burst: true);
            }

            _glowPulse = Mathf.Max(_glowPulse, Mathf.Lerp(0.18f, 0.55f, normalized));
        }

        private void Update()
        {
            if (!_visible || _phase == SettlementPacePhase.BelowTarget)
            {
                return;
            }

            // 结算暂停通过 Time.timeScale=0 实现，因此使用 deltaTime 会同步冻结火焰。
            float realDelta = Time.deltaTime;
            if (realDelta <= 0f)
            {
                return;
            }

            float emission = _phase == SettlementPacePhase.DoubleTarget
                ? _doubleTargetEmissionPerSecond
                : _targetEmissionPerSecond;
            _spawnAccumulator += realDelta * emission;
            while (_spawnAccumulator >= 1f)
            {
                _spawnAccumulator -= 1f;
                SpawnMote(burst: false);
            }

            float animationDelta = realDelta * _speed;
            for (int i = 0; i < _motes.Count; i++)
            {
                UpdateMote(_motes[i], animationDelta);
            }

            UpdateGlow(animationDelta);
        }

        private void EnsureUiPool()
        {
            if (_rootRect == null)
            {
                _rootRect = transform as RectTransform;
                if (_rootRect == null)
                {
                    _rootRect = gameObject.AddComponent<RectTransform>();
                }
            }

            if (_particles != null)
            {
                _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _particles.gameObject.SetActive(false);
            }

            if (_runtimeSprite == null)
            {
                Texture2D texture = Resources.Load<Texture2D>(MoteTexturePath);
                if (texture != null)
                {
                    _runtimeSprite = Sprite.Create(
                        texture,
                        new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        100f);
                    _runtimeSprite.name = "SettlementFireMoteSprite";
                }
            }

            if (_glow == null)
            {
                var glowObject = new GameObject(
                    "FireGlow",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                RectTransform rect = glowObject.GetComponent<RectTransform>();
                rect.SetParent(_rootRect, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(0f, -7f);
                rect.sizeDelta = new Vector2(116f, 58f);
                rect.SetAsFirstSibling();
                _glow = glowObject.GetComponent<Image>();
                _glow.sprite = _runtimeSprite;
                _glow.raycastTarget = false;
                _glow.preserveAspect = false;
                _glow.color = Color.clear;
            }

            int desiredCount = Mathf.Max(4, _poolSize);
            while (_motes.Count < desiredCount)
            {
                int index = _motes.Count;
                var moteObject = new GameObject(
                    $"FireMote{index:00}",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                RectTransform rect = moteObject.GetComponent<RectTransform>();
                rect.SetParent(_rootRect, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(26f, 48f);
                Image image = moteObject.GetComponent<Image>();
                image.sprite = _runtimeSprite;
                image.raycastTarget = false;
                image.preserveAspect = false;
                image.color = Color.clear;
                moteObject.SetActive(false);
                _motes.Add(new FireMote(rect, image));
            }
        }

        private void SpawnMote(bool burst)
        {
            if (_motes.Count == 0 || _runtimeSprite == null)
            {
                return;
            }

            FireMote mote = _motes[_nextMote];
            _nextMote = (_nextMote + 1) % _motes.Count;
            float stage = _phase == SettlementPacePhase.DoubleTarget ? 1f : 0.55f;
            mote.Active = true;
            mote.Age = burst ? Random.Range(0f, 0.06f) : 0f;
            mote.Lifetime = Random.Range(0.48f, 0.74f);
            mote.Start = new Vector2(Random.Range(-43f, 43f), Random.Range(-22f, -5f));
            float rise = Random.Range(56f, 82f) + stage * Random.Range(12f, 28f);
            mote.End = mote.Start + new Vector2(Random.Range(-20f, 20f), rise);
            mote.StartScale = Random.Range(0.22f, 0.38f) + stage * 0.10f;
            mote.PeakScale = Random.Range(0.52f, 0.78f) + stage * 0.16f;
            mote.Rotation = Random.Range(-22f, 22f);
            mote.RotationSpeed = Random.Range(-95f, 95f);
            mote.StartColor = _phase == SettlementPacePhase.DoubleTarget
                ? Color.Lerp(_flameColor, _hotColor, Random.Range(0.62f, 0.94f))
                : Color.Lerp(_flameColor, _hotColor, Random.Range(0.18f, 0.52f));
            mote.EndColor = Color.Lerp(_emberColor, _flameColor, 0.35f);
            mote.Rect.anchoredPosition = mote.Start;
            mote.Rect.localScale = Vector3.one * mote.StartScale;
            mote.Rect.localRotation = Quaternion.Euler(0f, 0f, mote.Rotation);
            mote.Image.color = Color.clear;
            mote.Image.gameObject.SetActive(true);
        }

        private static void UpdateMote(FireMote mote, float deltaTime)
        {
            if (!mote.Active)
            {
                return;
            }

            mote.Age += deltaTime;
            float t = Mathf.Clamp01(mote.Age / mote.Lifetime);
            float eased = 1f - (1f - t) * (1f - t);
            mote.Rect.anchoredPosition = Vector2.LerpUnclamped(mote.Start, mote.End, eased);
            float envelope = Mathf.Sin(t * Mathf.PI);
            float scale = Mathf.Lerp(mote.StartScale, mote.PeakScale, envelope);
            mote.Rect.localScale = Vector3.one * scale;
            mote.Rect.localRotation = Quaternion.Euler(
                0f,
                0f,
                mote.Rotation + mote.RotationSpeed * mote.Age);
            Color color = Color.Lerp(mote.StartColor, mote.EndColor, t);
            color.a *= Mathf.Pow(Mathf.Max(0f, envelope), 0.65f);
            mote.Image.color = color;

            if (t >= 1f)
            {
                DeactivateMote(mote);
            }
        }

        private void UpdateGlow(float deltaTime)
        {
            if (_glow == null)
            {
                return;
            }

            _glowPulse = Mathf.MoveTowards(_glowPulse, 0f, deltaTime * 1.8f);
            float baseAlpha = _phase == SettlementPacePhase.DoubleTarget ? 0.30f : 0.18f;
            Color color = _phase == SettlementPacePhase.DoubleTarget ? _hotColor : _flameColor;
            color.a = Mathf.Clamp01(baseAlpha + _glowPulse);
            _glow.color = color;
            float pulse = 1f + 0.055f * Mathf.Sin(Time.time * _speed * 9f) + _glowPulse * 0.22f;
            _glow.rectTransform.localScale = new Vector3(pulse, pulse, 1f);
        }

        private void ClearMotes()
        {
            for (int i = 0; i < _motes.Count; i++)
            {
                DeactivateMote(_motes[i]);
            }

            _nextMote = 0;
        }

        private static void DeactivateMote(FireMote mote)
        {
            mote.Active = false;
            mote.Age = 0f;
            mote.Image.color = Color.clear;
            mote.Image.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            if (_visible)
            {
                _visible = false;
                ClearMotes();
            }
        }

        private void OnDestroy()
        {
            if (_runtimeSprite != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_runtimeSprite);
                }
                else
                {
                    DestroyImmediate(_runtimeSprite);
                }

                _runtimeSprite = null;
            }
        }

        private sealed class FireMote
        {
            public FireMote(RectTransform rect, Image image)
            {
                Rect = rect;
                Image = image;
            }

            public RectTransform Rect { get; }
            public Image Image { get; }
            public bool Active;
            public float Age;
            public float Lifetime;
            public Vector2 Start;
            public Vector2 End;
            public float StartScale;
            public float PeakScale;
            public float Rotation;
            public float RotationSpeed;
            public Color StartColor;
            public Color EndColor;
        }
    }
}
