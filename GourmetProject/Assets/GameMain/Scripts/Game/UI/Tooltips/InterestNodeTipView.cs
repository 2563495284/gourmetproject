using UnityEngine;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 行动轴利息节点 hover Tips（原型图第 3 张）：
    /// 标题与说明框读取 action.xlsx 的 name / desc，底部显示「节点天数：{天}」。
    /// 固定结构在 InterestNodeTipView.prefab，内容由 <see cref="Bind"/> 数据驱动。
    /// </summary>
    public sealed class InterestNodeTipView : ActionTipView
    {
        /// <summary>用行动表配置的名称、描述和节点天数绑定。</summary>
        public void Bind(string title, string desc, int nodeDays, Sprite icon = null)
        {
            ApplyTexts(title, desc);
            ApplyFooter($"节点天数：{nodeDays}天");
        }
    }
}
