using System.Collections.Generic;
using System.Threading;
using DG.Tweening;
using UnityEngine;
using GourmetProject.Game.Visual;

namespace GourmetProject.Game.Presentation.Battle
{
    internal sealed class SweetTransferParticleView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _renderer;
        [SerializeField] private float _size = 0.22f;
        [SerializeField] private float _minimumArcHeight = 0.22f;
        [SerializeField] private float _arcHeightPerUnit = 0.14f;
        [SerializeField] private Color _color = new(1f, 0.24f, 0.68f, 1f);
        [SerializeField] private int _trailPointCount = 6;
        [SerializeField] private int _arrivalDotCount = 10;
        [SerializeField] private float _trailSpacing = 0.075f;
        [SerializeField] private float _arrivalRadius = 0.36f;

        private Tween _tween;
        private float _visualScale = 1f;

        public static async Awaitable PlayAsync(
            SweetTransferParticleView prefab,
            Transform parent,
            Vector3 start,
            Vector3 end,
            float duration,
            CancellationToken cancellationToken,
            Color? colorOverride = null,
            float visualScale = 1f)
        {
            SweetTransferParticleView view = Begin(
                prefab,
                parent,
                start,
                end,
                duration,
                colorOverride,
                visualScale);
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
                    Destroy(view.gameObject);
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
            float visualScale = 1f)
        {
            if (prefab == null)
            {
                return null;
            }

            SweetTransferParticleView view = Instantiate(prefab, parent);
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
            float visualScale = 1f)
        {
            if (prefab == null)
            {
                return;
            }

            SweetTransferParticleView view = Instantiate(prefab, parent);
            try
            {
                view._visualScale = Mathf.Max(0.0001f, visualScale);
                await view.PlayFailureInternalAsync(anchor, duration, cancellationToken);
            }
            finally
            {
                if (view != null)
                {
                    Destroy(view.gameObject);
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

            var fadingDots = new List<SpriteRenderer>();
            for (int i = 0; i < Mathf.Max(5, _arrivalDotCount / 2); i++)
            {
                fadingDots.Add(CreatePoint($"Failed_{i}", BattleSorting.OrderFloatingText - 2, 0.42f));
            }

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

                    for (int i = 0; i < fadingDots.Count; i++)
                    {
                        float angle = Mathf.PI * 2f * i / Mathf.Max(1, fadingDots.Count);
                        float radius = Mathf.Sin(t * Mathf.PI) * size * 1.25f;
                        SpriteRenderer dot = fadingDots[i];
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

            var trailPoints = new List<SpriteRenderer>();
            for (int i = 0; i < Mathf.Max(0, _trailPointCount); i++)
            {
                trailPoints.Add(CreatePoint($"Trail_{i}", BattleSorting.OrderFloatingText - 2, 0.72f));
            }

            var arrivalDots = new List<SpriteRenderer>();
            for (int i = 0; i < Mathf.Max(0, _arrivalDotCount); i++)
            {
                SpriteRenderer dot = CreatePoint($"Arrival_{i}", BattleSorting.OrderFloatingText - 1, 0.46f);
                dot.color = Color.clear;
                arrivalDots.Add(dot);
            }

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

                    for (int i = 0; i < trailPoints.Count; i++)
                    {
                        SpriteRenderer trail = trailPoints[i];
                        if (trail == null)
                        {
                            continue;
                        }

                        float delay = (i + 1) * Mathf.Max(0.01f, _trailSpacing);
                        float pointT = travel - delay;
                        if (pointT <= 0f || t >= 0.90f)
                        {
                            trail.color = Color.clear;
                            continue;
                        }

                        trail.transform.position = Bezier(start, control, end, Mathf.Clamp01(pointT));
                        float trailFade = 1f - (i + 1f) / (trailPoints.Count + 1f);
                        Color trailColor = _color;
                        trailColor.a *= 0.68f * trailFade * Mathf.Clamp01((0.90f - t) / 0.10f);
                        trail.color = trailColor;
                    }

                    float arrival = Mathf.Clamp01((t - 0.70f) / 0.30f);
                    float radius = Mathf.Lerp(
                        size * 0.18f,
                        Mathf.Max(size, _arrivalRadius * _visualScale),
                        arrival);
                    float ringAlpha = Mathf.Sin(arrival * Mathf.PI) * 0.82f;
                    for (int i = 0; i < arrivalDots.Count; i++)
                    {
                        SpriteRenderer dot = arrivalDots[i];
                        if (dot == null)
                        {
                            continue;
                        }

                        float angle = Mathf.PI * 2f * i / Mathf.Max(1, arrivalDots.Count);
                        dot.transform.position = end + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                        Color dotColor = new Color(1f, 0.78f, 0.94f, ringAlpha);
                        dot.color = dotColor;
                    }
                })
                .SetEase(Ease.InOutSine)
                .SetLink(gameObject);
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
        }
    }
}
