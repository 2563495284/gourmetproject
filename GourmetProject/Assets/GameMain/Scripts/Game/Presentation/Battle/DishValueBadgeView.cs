using UnityEngine;
using TMPro;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>常驻在食物顶部的美味值标签。</summary>
    internal sealed class DishValueBadgeView : MonoBehaviour
    {
        [Header("固定结构（prefab 预拼）")]
        [SerializeField] private SpriteRenderer _background;
        [SerializeField] private SpriteRenderer _valueBacking;
        [SerializeField] private SpriteRenderer _icon;
        [SerializeField] private MeshRenderer _valueMeshRenderer;
        [SerializeField] private TextMeshPro _valueText;

        private string _sortingLayer = BattleSorting.Fx;
        private int _sortingOrder = BattleSorting.OrderFloatingText;
        private bool _dimmed;
        private bool _chapterFocused;
        private float _valueAlpha = 1f;
        private bool _presentationBaseColorsCaptured;
        private Color _backgroundPresentationBaseColor;
        private Color _valueBackingPresentationBaseColor;
        private Color _iconPresentationBaseColor;
        private Color _textPresentationBaseColor;

        internal float CurrentAlpha => _valueAlpha;

        /// <summary>Badge 根节点到最高可见 Sprite 边缘的本地距离。</summary>
        public float TopExtent
        {
            get
            {
                return Mathf.Max(
                    SpriteTopExtent(_background),
                    SpriteTopExtent(_valueBacking),
                    SpriteTopExtent(_icon));
            }
        }

        public void SetValue(string text, bool forceMeshUpdate = false)
        {
            if (_valueText != null)
            {
                _valueText.text = text;
                if (forceMeshUpdate)
                {
                    // 食物候选图标会在同一帧内改值并立即 Camera.Render 到 RenderTexture。
                    // 普通世界徽章不走这里，避免结算开场批量改值时同步重建全部 TMP 网格。
                    _valueText.ForceMeshUpdate(true, true);
                    ApplySortingOrder();
                }
            }
        }

        public void ConfigureSorting(string sortingLayer, int sortingOrder)
        {
            _sortingLayer = sortingLayer;
            _sortingOrder = sortingOrder;
            ApplySortingOrder();
        }

        public void SetDimmed(bool dimmed)
        {
            if (_dimmed == dimmed)
            {
                return;
            }

            _dimmed = dimmed;
            ApplyPresentationColors();
        }

        public void SetChapterFocused(bool focused)
        {
            if (_chapterFocused == focused)
            {
                return;
            }

            _chapterFocused = focused;
            ApplyPresentationColors();
        }

        /// <summary>只调整美味值数字的透明度，保留 Badge 外框和图标。</summary>
        public void SetValueAlpha(float alpha)
        {
            float clamped = Mathf.Clamp01(alpha);
            if (Mathf.Approximately(_valueAlpha, clamped))
            {
                return;
            }

            _valueAlpha = clamped;
            ApplyPresentationColors();
        }

        private void ApplyPresentationColors()
        {
            CapturePresentationBaseColors();
            Color gold = SettlementColorPalette.BaseScore;
            ApplyRendererColor(_background, ChapterTint(_backgroundPresentationBaseColor, gold, 0.30f));
            ApplyRendererColor(_valueBacking, ChapterTint(_valueBackingPresentationBaseColor, gold, 0.65f));
            ApplyRendererColor(_icon, ChapterTint(_iconPresentationBaseColor, gold, 0.45f));
            if (_valueText != null)
            {
                Color color = ChapterTint(_textPresentationBaseColor, gold, 0.20f);
                if (_dimmed)
                {
                    color = WithAlphaMultiplier(color, 0.5f);
                }

                _valueText.color = WithAlphaMultiplier(color, _valueAlpha);
            }
        }

        private void CapturePresentationBaseColors()
        {
            if (_presentationBaseColorsCaptured)
            {
                return;
            }

            _presentationBaseColorsCaptured = true;
            _backgroundPresentationBaseColor = _background != null ? _background.color : Color.white;
            _valueBackingPresentationBaseColor = _valueBacking != null ? _valueBacking.color : Color.white;
            _iconPresentationBaseColor = _icon != null ? _icon.color : Color.white;
            _textPresentationBaseColor = _valueText != null ? _valueText.color : Color.white;
        }

        private void ApplyRendererColor(SpriteRenderer renderer, Color color)
        {
            if (renderer != null)
            {
                renderer.color = _dimmed ? WithAlphaMultiplier(color, 0.5f) : color;
            }
        }

        private Color ChapterTint(Color baseColor, Color tint, float amount)
        {
            if (!_chapterFocused)
            {
                return baseColor;
            }

            float alpha = baseColor.a;
            Color color = Color.Lerp(baseColor, tint, amount);
            color.a = alpha;
            return color;
        }

        private void ApplySortingOrder()
        {
            if (_valueText != null)
            {
                BattleSorting.Apply(_valueText, _sortingLayer, _sortingOrder + 2);
            }
            else
            {
                BattleSorting.Apply(_valueMeshRenderer, _sortingLayer, _sortingOrder + 2);
            }

            BattleSorting.Apply(_background, _sortingLayer, _sortingOrder);
            BattleSorting.Apply(_valueBacking, _sortingLayer, _sortingOrder + 1);
            BattleSorting.Apply(_icon, _sortingLayer, _sortingOrder + 3);
        }

        private static float SpriteTopExtent(SpriteRenderer renderer)
        {
            if (renderer == null || renderer.sprite == null)
            {
                return 0f;
            }

            Transform spriteTransform = renderer.transform;
            return spriteTransform.localPosition.y
                + renderer.sprite.bounds.max.y
                * Mathf.Abs(spriteTransform.localScale.y);
        }

        private static Color WithAlphaMultiplier(Color color, float multiplier)
        {
            color.a *= multiplier;
            return color;
        }

        private void OnDisable()
        {
            SetChapterFocused(false);
            SetDimmed(false);
            SetValueAlpha(1f);
        }
    }
}
