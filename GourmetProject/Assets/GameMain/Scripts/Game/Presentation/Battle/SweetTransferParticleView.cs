using System.Collections.Generic;
using System.Threading;
using DG.Tweening;
using UnityEngine;
using GourmetProject.Game.Visual;
using GourmetProject.Runtime.Pooling;

namespace GourmetProject.Game.Presentation.Battle
{
    internal sealed class SweetTransferParticleView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _renderer;
        [SerializeField] private float _size = 0.22f;
        [SerializeField] private float _minimumArcHeight = 0.22f;
        [SerializeField] private float _arcHeightPerUnit = 0.14f;
        [SerializeField] private Color _color = new(1f, 0.24f, 0.68f, 1f);
        [Header("Afterimage Trail")]
        [SerializeField] private int _afterimageCount = 24;
        [SerializeField] private float _afterimageSpacing = 0.019f;
        [SerializeField, Range(0f, 1f)] private float _afterimageHeadAlpha = 0.48f;
        [SerializeField, Range(0.05f, 1f)] private float _afterimageTailScale = 0.32f;
        [SerializeField] private int _arrivalDotCount = 10;
        [SerializeField] private float _arrivalRadius = 0.36f;

        private Tween _tween;
        private Transform _afterimageRoot;
        private readonly List<SpriteRenderer> _afterimages = new();
        private readonly List<SpriteRenderer> _arrivalDots = new();
        private readonly List<SpriteRenderer> _failureDots = new();
        private float _visualScale = 1f;
        private Color _defaultColor;

        private void Awake()
        {
            _defaultColor = _color;
        }

        public static async Awaitable PlayAsync(
            SweetTransferParticleView prefab,
            Transform parent,
            Vector3 start,
            Vector3 end,
            float duration,
            CancellationToken cancellationToken,
            Color? colorOverride = null,
            float visualScale = 1f,
            GameObjectPool pool = null)
        {
            SweetTransferParticleView view = Begin(
                prefab,
                parent,
                start,
                end,
                duration,
                colorOverride,
                visualScale,
                pool);
            if (view == null)
            {
                return;
            }

            try
            {
                await view.WaitAsync(cancellationToken);
            }
            finally
            {
                if (view != null)
                {
                    Release(view, pool);
                }
            }
        }

        /// <summary>
        /// 同步创建并启动飞行。不要把起飞放进 async PlayAsync 再逐个 await：
        /// Unity Awaitable 会把启动绑在第一次 await 上，看起来就像一颗飞完才飞下一颗。
        /// </summary>
        internal static SweetTransferParticleView Begin(
            SweetTransferParticleView prefab,
            Transform parent,
            Vector3 start,
            Vector3 end,
            float duration,
            Color? colorOverride = null,
            float visualScale = 1f,
            GameObjectPool pool = null)
        {
            if (prefab == null)
            {
                return null;
            }

            SweetTransferParticleView view = pool != null
                ? pool.Get<SweetTransferParticleView>(parent)
                : Instantiate(prefab, parent);
            if (colorOverride.HasValue)
            {
                view._color = colorOverride.Value;
            }

            view._visualScale = Mathf.Max(0.0001f, visualScale);
            view.StartFlight(start, end, duration);
            return view;
        }

        internal Awaitable WaitAsync(CancellationToken cancellationToken)
        {
            return PresentationTween.AwaitCompletionAsync(_tween, cancellationToken);
        }

        public static async Awaitable PlayFailureAsync(
            SweetTransferParticleView prefab,
            Transform parent,
            Vector3 anchor,
            float duration,
            CancellationToken cancellationToken,
            float visualScale = 1f,
            GameObjectPool pool = null)
        {
            if (prefab == null)
            {
                return;
            }

            SweetTransferParticleView view = pool != null
                ? pool.Get<SweetTransferParticleView>(parent)
                : Instantiate(prefab, parent);
            try
            {
                view._visualScale = Mathf.Max(0.0001f, visualScale);
                await view.PlayFailureInternalAsync(anchor, duration, cancellationToken);
            }
            finally
            {
                if (view != null)
                {
                    Release(view, pool);
                }
            }
        }

        private async Awaitable PlayFailureInternalAsync(
            Vector3 anchor,
            float duration,
            CancellationToken cancellationToken)
        {
            EnsureRenderer();
            if (_renderer == null)
            {
                return;
            }

            _renderer.sprite = BattleShadow.SoftShadowSprite;
            SpriteRenderStyle.ApplyUnlitMaterial(_renderer);
            BattleSorting.Apply(_renderer, BattleSorting.Fx, BattleSorting.OrderFloatingText - 1);
            transform.position = anchor;
            float size = _size * _visualScale;

            EnsurePoints(
                _failureDots,
                Mathf.Max(5, _arrivalDotCount / 2),
                "Failed",
                BattleSorting.OrderFloatingText - 2,
                0.42f);

            float safeDuration = Mathf.Max(0.0001f, duration);
            _tween = DOVirtual.Float(0f, 1f, safeDuration, progress =>
                {
                    float t = Mathf.Clamp01(progress);
                    float recoil = Mathf.Sin(t * Mathf.PI * 5f) * (1f - t) * size * 0.22f;
                    transform.position = anchor + Vector3.right * recoil;
                    transform.localScale = Vector3.one * size * Mathf.Lerp(1.05f, 0.08f, t * t);
                    Color core = _color;
                    core.a *= 1f - t;
                    _renderer.color = core;

                    for (int i = 0; i < _failureDots.Count; i++)
                    {
                        float angle = Mathf.PI * 2f * i / Mathf.Max(1, _failureDots.Count);
                        float radius = Mathf.Sin(t * Mathf.PI) * size * 1.25f;
                        SpriteRenderer dot = _failureDots[i];
                        dot.transform.position = anchor
                            + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                        Color dotColor = _color;
                        dotColor.a *= (1f - t) * 0.72f;
                        dot.color = dotColor;
                    }
                })
                .SetEase(Ease.InCubic)
                .SetLink(gameObject);

            await PresentationTween.AwaitCompletionAsync(_tween, cancellationToken);
        }

        private void StartFlight(
            Vector3 start,
            Vector3 end,
            float duration)
        {
            EnsureRenderer();
            if (_renderer == null)
            {
                return;
            }

            _renderer.sprite = BattleShadow.SoftShadowSprite;
            _renderer.color = _color;
            SpriteRenderStyle.ApplyUnlitMaterial(_renderer);
            BattleSorting.Apply(_renderer, BattleSorting.Fx, BattleSorting.OrderFloatingText - 1);

            EnsureAfterimages(Mathf.Max(0, _afterimageCount));
            EnsurePoints(
                _arrivalDots,
                Mathf.Max(0, _arrivalDotCount),
                "Arrival",
                BattleSorting.OrderFloatingText - 1,
                0.46f);

            float size = _size * _visualScale;
            transform.position = start;
            transform.localScale = Vector3.one * (size * 0.55f);
            float distance = Vector2.Distance(start, end);
            Vector3 control = (start + end) * 0.5f
                + Vector3.up * Mathf.Max(
                    _minimumArcHeight * _visualScale,
                    distance * _arcHeightPerUnit);

            _tween = DOVirtual.Float(0f, 1f, Mathf.Max(0.0001f, duration), progress =>
                {
                    if (this == null || _renderer == null)
                    {
                        return;
                    }

                    float t = Mathf.Clamp01(progress);
                    float travel = Mathf.Clamp01(t / 0.80f);
                    transform.position = Bezier(start, control, end, travel);

                    float pulse = Mathf.Sin(travel * Mathf.PI);
                    transform.localScale = Vector3.one * (size * Mathf.Lerp(0.55f, 1.22f, pulse));

                    float fadeIn = Mathf.Clamp01(travel / 0.12f);
                    float fadeOut = Mathf.Clamp01((0.88f - t) / 0.10f);
                    Color color = _color;
                    color.a *= Mathf.Min(fadeIn, fadeOut);
                    _renderer.color = color;

                    float safeAfterimageSpacing = Mathf.Max(0.005f, _afterimageSpacing);
                    for (int i = 0; i < _afterimages.Count; i++)
                    {
                        SpriteRenderer afterimage = _afterimages[i];
                        if (afterimage == null)
                        {
                            continue;
                        }

                        float delay = (i + 1) * safeAfterimageSpacing;
                        float pointT = travel - delay;
                        if (pointT <= 0f || t >= 0.95f)
                        {
                            afterimage.color = Color.clear;
                            continue;
                        }

                        float age = (i + 1f) / (_afterimages.Count + 1f);
                        afterimage.transform.position = Bezier(start, control, end, pointT);

                        // 每个副本保持它所代表的“过去一帧”的尺寸，再随年龄逐渐缩小、变淡。
                        // 这会读成粒子本体的残影，而不是一条连接起终点的实体色带。
                        float echoPulse = Mathf.Sin(pointT * Mathf.PI);
                        float echoSize = size * Mathf.Lerp(0.55f, 1.22f, echoPulse);
                        float ageScale = Mathf.Lerp(1f, _afterimageTailScale, age);
                        afterimage.transform.localScale = Vector3.one * (echoSize * ageScale);

                        float spawnFade = Mathf.Clamp01(pointT / (safeAfterimageSpacing * 1.5f));
                        float arrivalFade = Mathf.Clamp01((0.95f - t) / 0.12f);
                        float ageFade = Mathf.Pow(1f - age, 0.9f);
                        Color afterimageColor = Color.Lerp(_color, Color.white, (1f - age) * 0.16f);
                        afterimageColor.a = _color.a
                            * _afterimageHeadAlpha
                            * ageFade
                            * spawnFade
                            * arrivalFade;
                        afterimage.color = afterimageColor;
                    }

                    float arrival = Mathf.Clamp01((t - 0.70f) / 0.30f);
                    float radius = Mathf.Lerp(
                        size * 0.18f,
                        Mathf.Max(size, _arrivalRadius * _visualScale),
                        arrival);
                    float ringAlpha = Mathf.Sin(arrival * Mathf.PI) * 0.82f;
                    for (int i = 0; i < _arrivalDots.Count; i++)
                    {
                        SpriteRenderer dot = _arrivalDots[i];
                        if (dot == null)
                        {
                            continue;
                        }

                        float angle = Mathf.PI * 2f * i / Mathf.Max(1, _arrivalDots.Count);
                        dot.transform.position = end + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                        Color dotColor = new Color(1f, 0.78f, 0.94f, ringAlpha);
                        dot.color = dotColor;
                    }
                })
                .SetEase(Ease.InOutSine)
                .SetLink(gameObject);
        }

        internal void PrepareForReuse()
        {
            ResetVisuals();
            _color = _defaultColor;
            _visualScale = 1f;
        }

        internal void ResetForPool()
        {
            ResetVisuals();
            _color = _defaultColor;
            _visualScale = 1f;
        }

        internal void WarmupForPool()
        {
            EnsureRenderer();
            EnsureAfterimages(Mathf.Max(0, _afterimageCount));
            EnsurePoints(
                _arrivalDots,
                Mathf.Max(0, _arrivalDotCount),
                "Arrival",
                BattleSorting.OrderFloatingText - 1,
                0.46f);
            EnsurePoints(
                _failureDots,
                Mathf.Max(5, _arrivalDotCount / 2),
                "Failed",
                BattleSorting.OrderFloatingText - 2,
                0.42f);
            ResetForPool();
        }

        private void EnsureAfterimageRoot()
        {
            if (_afterimageRoot == null)
            {
                GameObject root = new("SweetTransferAfterimages");
                _afterimageRoot = root.transform;
            }

            _afterimageRoot.SetParent(transform.parent, false);
            _afterimageRoot.gameObject.SetActive(true);
        }

        private void EnsureAfterimages(int count)
        {
            EnsureAfterimageRoot();
            while (_afterimages.Count < count)
            {
                _afterimages.Add(CreateAfterimage($"Afterimage_{_afterimages.Count}"));
            }

            SetRendererRangeActive(_afterimages, count);
        }

        private void EnsurePoints(
            List<SpriteRenderer> points,
            int count,
            string namePrefix,
            int sortingOrder,
            float relativeScale)
        {
            while (points.Count < count)
            {
                points.Add(CreatePoint(
                    $"{namePrefix}_{points.Count}",
                    sortingOrder,
                    relativeScale));
            }

            SetRendererRangeActive(points, count);
            for (int i = 0; i < count; i++)
            {
                points[i].color = Color.clear;
            }
        }

        private SpriteRenderer CreateAfterimage(string name)
        {
            GameObject echo = new(name);
            echo.transform.SetParent(_afterimageRoot, false);
            SpriteRenderer renderer = echo.AddComponent<SpriteRenderer>();
            renderer.sprite = BattleShadow.SoftShadowSprite;
            renderer.color = Color.clear;
            SpriteRenderStyle.ApplyUnlitMaterial(renderer);
            BattleSorting.Apply(renderer, BattleSorting.Fx, BattleSorting.OrderFloatingText - 2);
            return renderer;
        }

        private SpriteRenderer CreatePoint(string name, int sortingOrder, float relativeScale)
        {
            GameObject point = new(name);
            point.transform.SetParent(transform, false);
            point.transform.localScale = Vector3.one * Mathf.Max(0.01f, relativeScale);
            SpriteRenderer renderer = point.AddComponent<SpriteRenderer>();
            renderer.sprite = BattleShadow.SoftShadowSprite;
            renderer.color = _color;
            SpriteRenderStyle.ApplyUnlitMaterial(renderer);
            BattleSorting.Apply(renderer, BattleSorting.Fx, sortingOrder);
            return renderer;
        }

        private static void SetRendererRangeActive(List<SpriteRenderer> renderers, int activeCount)
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer != null)
                {
                    renderer.gameObject.SetActive(i < activeCount);
                }
            }
        }

        private void ResetVisuals()
        {
            _tween?.Kill(false);
            _tween = null;
            if (_renderer != null)
            {
                _renderer.color = Color.clear;
            }

            ClearRenderers(_afterimages);
            ClearRenderers(_arrivalDots);
            ClearRenderers(_failureDots);
            if (_afterimageRoot != null)
            {
                _afterimageRoot.gameObject.SetActive(false);
            }
        }

        private static void ClearRenderers(List<SpriteRenderer> renderers)
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer != null)
                {
                    renderer.color = Color.clear;
                    renderer.gameObject.SetActive(false);
                }
            }
        }

        private static void Release(SweetTransferParticleView view, GameObjectPool pool)
        {
            if (view == null)
            {
                return;
            }

            if (pool != null)
            {
                pool.Release(view);
            }
            else if (Application.isPlaying)
            {
                Destroy(view.gameObject);
            }
            else
            {
                DestroyImmediate(view.gameObject);
            }
        }

        private static Vector3 Bezier(Vector3 start, Vector3 control, Vector3 end, float t)
        {
            float clamped = Mathf.Clamp01(t);
            float inverse = 1f - clamped;
            return inverse * inverse * start
                + 2f * inverse * clamped * control
                + clamped * clamped * end;
        }

        private void EnsureRenderer()
        {
            if (_renderer == null)
            {
                _renderer = GetComponent<SpriteRenderer>();
            }

            if (_renderer == null)
            {
                Debug.LogError($"{nameof(SweetTransferParticleView)} prefab 缺少 SpriteRenderer。", this);
            }
        }

        private void OnDestroy()
        {
            _tween?.Kill();
            _tween = null;

            if (_afterimageRoot != null)
            {
                GameObject root = _afterimageRoot.gameObject;
                _afterimageRoot = null;
                if (Application.isPlaying)
                {
                    Destroy(root);
                }
                else
                {
                    DestroyImmediate(root);
                }
            }
        }
    }
}
