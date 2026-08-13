using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>风味标签条目：根据风味名切换 PSD 原稿圆底与 TMP 字色。</summary>
    public sealed class FoodFlavorTagView : MonoBehaviour
    {
        [Serializable]
        private struct FlavorStyle
        {
            [SerializeField] private string _label;
            [SerializeField] private Sprite _background;
            [SerializeField] private Color _textColor;

            public string Label => _label;
            public Sprite Background => _background;
            public Color TextColor => _textColor;
        }

        [SerializeField] private Image _backgroundImage;
        [SerializeField] private TMP_Text _labelText;
        [SerializeField] private FlavorStyle[] _styles = Array.Empty<FlavorStyle>();
        [SerializeField] private Sprite _fallbackBackground;
        [SerializeField] private Color _fallbackTextColor = Color.black;

        public void Bind(string label)
        {
            if (!ValidateReferences())
            {
                return;
            }

            string normalizedLabel = label?.Trim() ?? string.Empty;
            _labelText.text = normalizedLabel;

            if (TryGetStyle(normalizedLabel, out FlavorStyle style))
            {
                _backgroundImage.sprite = style.Background;
                _labelText.color = style.TextColor;
                return;
            }

            _backgroundImage.sprite = _fallbackBackground;
            _labelText.color = _fallbackTextColor;
        }

        private void Awake()
        {
            ValidateReferences();
        }

        private void Reset()
        {
            ValidateReferences();
        }

        private bool ValidateReferences()
        {
            if (_backgroundImage != null && _labelText != null && _styles != null && _styles.Length > 0)
            {
                return true;
            }

            Debug.LogError($"{nameof(FoodFlavorTagView)} on '{name}' has incomplete prefab style references.", this);
            return false;
        }

        private bool TryGetStyle(string label, out FlavorStyle style)
        {
            for (int i = 0; i < _styles.Length; i++)
            {
                if (string.Equals(_styles[i].Label, label, StringComparison.Ordinal))
                {
                    style = _styles[i];
                    return true;
                }
            }

            style = default;
            return false;
        }
    }
}
