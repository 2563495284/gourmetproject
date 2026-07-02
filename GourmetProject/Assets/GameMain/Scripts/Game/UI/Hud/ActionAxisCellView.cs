using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 行动轴上的单个天格视图：底框 + 天序号 + 可选节点图标（商店/利息/Boss/事件）。
    /// 固定结构在 ActionAxisCellView.prefab，由 <see cref="ActionAxisBar"/> 按行动轴长度数据驱动实例化。
    /// </summary>
    public sealed class ActionAxisCellView : MonoBehaviour
    {
        [SerializeField] private Image _background;
        [SerializeField] private Text _dayText;
        [SerializeField] private Text _nodeIcon;

        private static readonly Color PassedColor = new Color(0.72f, 0.90f, 0.70f, 1f);
        private static readonly Color FutureColor = new Color(0.97f, 0.96f, 0.92f, 1f);

        /// <summary>
        /// 绑定一格。<paramref name="day"/> 天序号（1 起）；<paramref name="passed"/> 是否已过（含当前天）；
        /// <paramref name="nodeLabel"/> 节点图标文案（空表示普通格）。
        /// </summary>
        public void Bind(int day, bool passed, string nodeLabel)
        {
            if (_dayText != null)
            {
                _dayText.text = day.ToString();
            }

            if (_background != null)
            {
                _background.color = passed ? PassedColor : FutureColor;
            }

            if (_nodeIcon != null)
            {
                bool hasNode = !string.IsNullOrEmpty(nodeLabel);
                _nodeIcon.gameObject.SetActive(hasNode);
                _nodeIcon.text = hasNode ? nodeLabel : string.Empty;
            }
        }
    }
}
