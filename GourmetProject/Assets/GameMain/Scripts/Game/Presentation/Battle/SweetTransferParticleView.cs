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

        private Tween _tween;

        public static async Awaitable PlayAsync(
            SweetTransferParticleView prefab,
            Transform parent,
            Vector3 start,
            Vector3 end,
            float duration,
            CancellationToken cancellationToken)
        {
            if (prefab == null)
            {
                return;
            }

            SweetTransferParticleView view = Instantiate(prefab, parent);
            try
            {
                await view.PlayInternalAsync(start, end, duration, cancellationToken);
            }
            finally
            {
                if (view != null)
                {
                    Destroy(view.gameObject);
                }
            }
        }

        private async Awaitable PlayInternalAsync(
            Vector3 start,
            Vector3 end,
            float duration,
            CancellationToken cancellationToken)
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

            transform.position = start;
            transform.localScale = Vector3.one * (_size * 0.55f);
            float distance = Vector2.Distance(start, end);
            Vector3 control = (start + end) * 0.5f
                + Vector3.up * Mathf.Max(_minimumArcHeight, distance * _arcHeightPerUnit);

            _tween = DOVirtual.Float(0f, 1f, Mathf.Max(0.0001f, duration), progress =>
                {
                    if (this == null || _renderer == null)
                    {
                        return;
                    }

                    float t = Mathf.Clamp01(progress);
                    float inverse = 1f - t;
                    transform.position = inverse * inverse * start
                        + 2f * inverse * t * control
                        + t * t * end;

                    float pulse = Mathf.Sin(t * Mathf.PI);
                    transform.localScale = Vector3.one * (_size * Mathf.Lerp(0.55f, 1.22f, pulse));

                    float fadeIn = Mathf.Clamp01(t / 0.12f);
                    float fadeOut = Mathf.Clamp01((1f - t) / 0.18f);
                    Color color = _color;
                    color.a *= Mathf.Min(fadeIn, fadeOut);
                    _renderer.color = color;
                })
                .SetEase(Ease.InOutSine)
                .SetLink(gameObject);

            await PresentationTween.AwaitCompletionAsync(_tween, cancellationToken);
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
