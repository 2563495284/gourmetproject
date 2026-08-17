using System;
using GourmetProject.Game.UI.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>风味详情卡：按风味切换主底、描述底和标题色。</summary>
    public sealed class FoodFlavorDetailView : MonoBehaviour
    {
        [Serializable]
        private struct FlavorStyle
        {
            [SerializeField] private string _label;
            [SerializeField] private Sprite _background;
            [SerializeField] private Sprite _descriptionBackground;
            [SerializeField] private Color _titleColor;

            public string Label => _label;
            public Sprite Background => _background;
            public Sprite DescriptionBackground => _descriptionBackground;
            public Color TitleColor => _titleColor;
        }

        [SerializeField] private Image _backgroundImage;
        [SerializeField] private Image _descriptionBackgroundImage;
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _descriptionText;
        [SerializeField] private FlavorStyle[] _styles = Array.Empty<FlavorStyle>();
        [SerializeField] private Sprite _fallbackBackground;
        [SerializeField] private Sprite _fallbackDescriptionBackground;
        [SerializeField] private Color _fallbackTitleColor = Color.black;

        public void Bind(string title, string description)
        {
            if (!ValidateReferences())
            {
                return;
            }

            string normalizedTitle = title?.Trim() ?? string.Empty;
            _titleText.text = normalizedTitle;
            SemanticDescriptionFormatter.Set(_descriptionText, description);

            if (TryGetStyle(normalizedTitle, out FlavorStyle style))
            {
                ApplyStyle(style.Background, style.DescriptionBackground, style.TitleColor);
                return;
            }

            ApplyStyle(_fallbackBackground, _fallbackDescriptionBackground, _fallbackTitleColor);
        }

        private void Awake()
        {
            ValidateReferences();
        }

        private void Reset()
        {
            ValidateReferences();
        }

        private void ApplyStyle(Sprite background, Sprite descriptionBackground, Color titleColor)
        {
            _backgroundImage.sprite = background;
            _descriptionBackgroundImage.sprite = descriptionBackground;
            _titleText.color = titleColor;
            _descriptionText.color = new Color32(0xFC, 0xFF, 0xCE, 0xFF);
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

        private bool ValidateReferences()
        {
            bool valid = _backgroundImage != null
                && _descriptionBackgroundImage != null
                && _titleText != null
                && _descriptionText != null
                && _styles != null
                && _styles.Length > 0;
            if (!valid)
            {
                Debug.LogError($"{nameof(FoodFlavorDetailView)} on '{name}' has incomplete prefab references.", this);
            }

            return valid;
        }
    }
}
