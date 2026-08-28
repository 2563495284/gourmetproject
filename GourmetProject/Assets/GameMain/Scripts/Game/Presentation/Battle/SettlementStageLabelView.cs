using GourmetProject.Game.UI.Common;
using TMPro;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>结算舞台标签的 prefab 结构与运行时内容绑定。</summary>
    public sealed class SettlementStageLabelView : MonoBehaviour
    {
        [Header("固定结构（prefab 预拼）")]
        [SerializeField] private TextMeshPro _headerText;
        [SerializeField] private TextMeshPro _bodyText;

        public TextMeshPro HeaderText => _headerText;
        public TextMeshPro BodyText => _bodyText;

        private Color _defaultHeaderColor;
        private Color _defaultBodyColor;

        private void Awake()
        {
            _defaultHeaderColor = _headerText != null ? _headerText.color : Color.white;
            _defaultBodyColor = _bodyText != null ? _bodyText.color : Color.white;
        }

        public void Bind(
            string header,
            string body,
            Color theme,
            Color? headerSemanticColor = null,
            int sortingOrder = -1)
        {
            BindContent(header, body, theme, headerSemanticColor);

            int order = sortingOrder >= 0 ? sortingOrder : BattleSorting.OrderFloatingText;
            BattleSorting.Apply(
                _headerText,
                BattleSorting.Fx,
                order + 2);
            BattleSorting.Apply(
                _bodyText,
                BattleSorting.Fx,
                order + 3);
        }

        internal void BindForBatch(
            string header,
            string body,
            Color theme,
            Color? headerSemanticColor)
        {
            BindContent(header, body, theme, headerSemanticColor);
        }

        private void BindContent(
            string header,
            string body,
            Color theme,
            Color? headerSemanticColor)
        {
            _headerText.text = header ?? string.Empty;
            SemanticDescriptionFormatter.Set(_bodyText, body);
            Color textColor = SettlementColorPalette.TextFor(theme);
            Color headerColor = headerSemanticColor.HasValue
                ? SettlementColorPalette.ResultHeaderTextFor(headerSemanticColor.Value)
                : textColor;
            _headerText.color = SettlementColorPalette.WithAlpha(headerColor, 1f);
            _bodyText.color = textColor;
            _headerText.ForceMeshUpdate(true, true);
            _bodyText.ForceMeshUpdate(true, true);
        }

        internal void PrepareForReuse()
        {
            ResetReusableState();
        }

        private void ResetReusableState()
        {
            if (_headerText != null)
            {
                _headerText.text = string.Empty;
                _headerText.color = _defaultHeaderColor;
            }

            if (_bodyText != null)
            {
                _bodyText.text = string.Empty;
                _bodyText.color = _defaultBodyColor;
            }
        }
    }
}
