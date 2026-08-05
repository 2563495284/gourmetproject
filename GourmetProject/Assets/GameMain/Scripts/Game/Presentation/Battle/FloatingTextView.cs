using DG.Tweening;
using UnityEngine;
using TMPro;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>结算效果条：来源名在上，效果文字显示在底板内，上浮淡出后自毁。</summary>
    internal sealed class FloatingTextView : MonoBehaviour
    {
        [Header("固定结构（prefab 预拼）")]
        [SerializeField] private SpriteRenderer _background;
        [SerializeField] private TextMeshPro _sourceText;

        [SerializeField] private TextMeshPro _effectText;

        [SerializeField] private MeshRenderer _effectMeshRenderer;
        [Header("飘动")]
        [SerializeField] private float _rise = 0.9f;
        [SerializeField] private float _duration = 0.9f;

        private Tween _tween;
        private int _sortingOrder = BattleSorting.OrderFloatingText;

        public static void Spawn(
            FloatingTextView prefab,
            Transform parent,
            Vector3 worldPos,
            string text,
            float? rise = null,
            float? duration = null)
        {
            SpawnEffect(prefab, parent, worldPos, string.Empty, text, rise, duration);
        }

        public static void SpawnEffect(
            FloatingTextView prefab,
            Transform parent,
            Vector3 worldPos,
            string sourceName,
            string effectText,
            float? rise = null,
            float? duration = null,
            Color? effectColor = null,
            float delay = 0f)
        {
            if (prefab == null)
            {
                Debug.LogError($"{nameof(FloatingTextView)} 缺少 prefab。");
                return;
            }

            FloatingTextView view = Instantiate(prefab, parent);
            view.transform.position = worldPos;
            view._sortingOrder = WorldLabelSorting.NextOrder();
            view.PlayEffect(sourceName, effectText, rise, duration, effectColor, delay);
        }

        private void PlayEffect(
            string sourceName,
            string effectText,
            float? rise,
            float? duration,
            Color? effectColor,
            float delay)
        {
            KillAnimation();

            _effectText.text = effectText;
            if (effectColor.HasValue)
            {
                _effectText.color = effectColor.Value;
            }
            ConfigureSourceText(sourceName);
            ApplySortingOrder();
            Animate(_effectText, transform.position, rise ?? _rise, duration ?? _duration, delay);
        }

        private void OnDestroy()
        {
            KillAnimation();
        }

        private void ConfigureSourceText(string sourceName)
        {
            if (_sourceText == null)
            {
                return;
            }

            bool visible = !string.IsNullOrWhiteSpace(sourceName);
            _sourceText.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            _sourceText.text = sourceName;
        }

        private void ApplySortingOrder()
        {
            if (_effectText != null)
            {
                BattleSorting.Apply(_effectText, BattleSorting.Fx, _sortingOrder + 2);
            }
            else
            {
                BattleSorting.Apply(_effectMeshRenderer, BattleSorting.Fx, _sortingOrder + 2);
            }

            BattleSorting.Apply(_background, BattleSorting.Fx, _sortingOrder);
            BattleSorting.Apply(_sourceText, BattleSorting.Fx, _sortingOrder + 2);
        }

        private void Animate(TextMeshPro effect, Vector3 start, float rise, float duration, float delay)
        {
            Color effectColor = effect.color;
            Color backgroundColor = _background != null ? _background.color : Color.clear;
            Color sourceColor = _sourceText != null ? _sourceText.color : Color.clear;
            if (delay > 0f)
            {
                effect.color = WithAlpha(effectColor, 0f);
                if (_background != null)
                {
                    _background.color = WithAlpha(backgroundColor, 0f);
                }

                if (_sourceText != null)
                {
                    _sourceText.color = WithAlpha(sourceColor, 0f);
                }
            }

            _tween = DOVirtual.Float(0f, 1f, Mathf.Max(0.0001f, duration), t =>
                {
                    if (effect == null)
                    {
                        return;
                    }

                    transform.position = start + new Vector3(0f, rise * t, 0f);
                    float alpha = 1f - t;
                    effect.color = WithAlpha(effectColor, alpha);
                    if (_background != null)
                    {
                        _background.color = WithAlpha(backgroundColor, alpha);
                    }

                    if (_sourceText != null)
                    {
                        _sourceText.color = WithAlpha(sourceColor, alpha);
                    }
                })
                .SetDelay(Mathf.Max(0f, delay))
                .OnStart(() =>
                {
                    if (effect != null)
                    {
                        effect.color = effectColor;
                    }

                    if (_background != null)
                    {
                        _background.color = backgroundColor;
                    }

                    if (_sourceText != null)
                    {
                        _sourceText.color = sourceColor;
                    }
                })
                .SetEase(Ease.Linear)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    if (this != null)
                    {
                        Destroy(gameObject);
                    }
                });
        }

        private void KillAnimation()
        {
            if (_tween != null && _tween.IsActive())
            {
                _tween.Kill(false);
            }

            _tween = null;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a *= alpha;
            return color;
        }
    }

    internal static class WorldLabelSorting
    {
        private const int OrderRange = 10000;
        private static int _nextOrderOffset;

        public static int NextOrder()
        {
            int order = BattleSorting.OrderFloatingText + _nextOrderOffset;
            _nextOrderOffset = (_nextOrderOffset + 1) % OrderRange;
            return order;
        }
    }
}
