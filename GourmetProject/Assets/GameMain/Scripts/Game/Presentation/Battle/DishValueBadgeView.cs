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
        private Color _backgroundColorBeforeDim;
        private Color _valueBackingColorBeforeDim;
        private Color _iconColorBeforeDim;
        private Color _textColorBeforeDim;

        internal float CurrentAlpha => _valueText != null
            ? _valueText.color.a
            : (_background != null ? _background.color.a : 1f);

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

        public void SetValue(string text)
        {
            if (_valueText != null)
            {
                _valueText.text = text;
                // 食物候选图标会在同一帧内改值并立即 Camera.Render 到 RenderTexture。
                // TMP 默认延迟到后续渲染阶段重建网格，会让共享预览 Rig 拍到上一张卡的数字。
                _valueText.ForceMeshUpdate(true, true);
            }

            ApplySortingOrder();
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
            if (dimmed)
            {
                if (_background != null)
                {
                    _backgroundColorBeforeDim = _background.color;
                    _background.color = WithAlphaMultiplier(
                        _backgroundColorBeforeDim,
                        0.5f);
                }

                if (_icon != null)
                {
                    _iconColorBeforeDim = _icon.color;
                    _icon.color = WithAlphaMultiplier(
                        _iconColorBeforeDim,
                        0.5f);
                }

                if (_valueBacking != null)
                {
                    _valueBackingColorBeforeDim = _valueBacking.color;
                    _valueBacking.color = WithAlphaMultiplier(
                        _valueBackingColorBeforeDim,
                        0.5f);
                }

                if (_valueText != null)
                {
                    _textColorBeforeDim = _valueText.color;
                    _valueText.color = WithAlphaMultiplier(
                        _textColorBeforeDim,
                        0.5f);
                }

                return;
            }

            if (_background != null)
            {
                _background.color = _backgroundColorBeforeDim;
            }

            if (_icon != null)
            {
                _icon.color = _iconColorBeforeDim;
            }

            if (_valueBacking != null)
            {
                _valueBacking.color = _valueBackingColorBeforeDim;
            }

            if (_valueText != null)
            {
                _valueText.color = _textColorBeforeDim;
            }
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
            SetDimmed(false);
        }
    }
}
