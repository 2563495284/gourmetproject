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

        public void Bind(string header, string body, Color theme)
        {
            _headerText.text = header ?? string.Empty;
            SemanticDescriptionFormatter.Set(_bodyText, body);
            _headerText.ForceMeshUpdate(true, true);
            _bodyText.ForceMeshUpdate(true, true);

            Color textColor = SettlementColorPalette.TextFor(theme);
            _headerText.color = SettlementColorPalette.WithAlpha(textColor, 0.82f);
            _bodyText.color = textColor;

            BattleSorting.Apply(
                _headerText,
                BattleSorting.Fx,
                BattleSorting.OrderFloatingText + 2);
            BattleSorting.Apply(
                _bodyText,
                BattleSorting.Fx,
                BattleSorting.OrderFloatingText + 3);
        }
    }
}
