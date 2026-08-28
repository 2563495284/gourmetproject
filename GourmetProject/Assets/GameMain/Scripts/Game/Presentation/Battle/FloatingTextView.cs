using GourmetProject.Game.UI.Common;
using TMPro;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 临时结算效果条的排版模板。运行时可见文字由
    /// <see cref="SettlementTextBatchRenderer"/> 统一渲染，本组件不再创建逐条动画实例。
    /// </summary>
    internal sealed class FloatingTextView : MonoBehaviour
    {
        [Header("固定结构（prefab 预拼）")]
        [SerializeField] private SpriteRenderer _background;
        [SerializeField] private TextMeshPro _sourceText;
        [SerializeField] private TextMeshPro _effectText;

        // 保留序列化字段，旧 prefab 无需迁移；批渲染不再直接驱动这个 Renderer。
        [SerializeField] private MeshRenderer _effectMeshRenderer;

        [Header("飘动")]
        [SerializeField] private float _rise = 0.9f;
        [SerializeField] private float _duration = 0.9f;

        private Color _defaultBackgroundColor;
        private Color _defaultSourceColor;
        private Color _defaultEffectColor;
        private bool _defaultSourceActive;
        private bool _defaultsCaptured;

        internal SpriteRenderer Background => _background;
        internal TextMeshPro SourceText => _sourceText;
        internal TextMeshPro EffectText => _effectText;
        internal float DefaultRise => _rise;
        internal float DefaultDuration => _duration;

        private void Awake()
        {
            CaptureDefaults();
        }

        internal void BindForBatch(
            string sourceName,
            string effectText,
            Color? effectColor)
        {
            EnsureDefaults();
            ResetTemplateState();

            if (_sourceText != null)
            {
                bool visible = !string.IsNullOrWhiteSpace(sourceName);
                _sourceText.gameObject.SetActive(visible);
                _sourceText.text = visible ? sourceName : string.Empty;
                _sourceText.ForceMeshUpdate(true, true);
            }

            if (_effectText != null)
            {
                SemanticDescriptionFormatter.Set(_effectText, effectText);
                if (effectColor.HasValue)
                {
                    _effectText.color = effectColor.Value;
                }

                _effectText.ForceMeshUpdate(true, true);
            }
        }

        private void CaptureDefaults()
        {
            _defaultBackgroundColor = _background != null ? _background.color : Color.white;
            _defaultSourceColor = _sourceText != null ? _sourceText.color : Color.white;
            _defaultEffectColor = _effectText != null ? _effectText.color : Color.white;
            _defaultSourceActive = _sourceText != null && _sourceText.gameObject.activeSelf;
            _defaultsCaptured = true;
        }

        private void EnsureDefaults()
        {
            if (!_defaultsCaptured)
            {
                CaptureDefaults();
            }
        }

        private void ResetTemplateState()
        {
            if (_background != null)
            {
                _background.color = _defaultBackgroundColor;
                _background.SetPropertyBlock(null);
            }

            if (_sourceText != null)
            {
                _sourceText.color = _defaultSourceColor;
                _sourceText.text = string.Empty;
                _sourceText.gameObject.SetActive(_defaultSourceActive);
            }

            if (_effectText != null)
            {
                _effectText.color = _defaultEffectColor;
                _effectText.text = string.Empty;
            }

            if (_effectMeshRenderer != null)
            {
                _effectMeshRenderer.SetPropertyBlock(null);
            }
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
