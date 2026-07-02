using UnityEngine;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 行动轴利息节点 hover Tips（原型图第 3 张）：
    /// 标题「收取利息」+ 规则框（每有 N 枚金币，获得 M 枚，最高可获得 K 枚）+ 底部「节点天数：{天}」。
    /// 固定结构在 InterestNodeTipView.prefab，内容由 <see cref="Bind"/> 数据驱动。
    /// </summary>
    public sealed class InterestNodeTipView : ActionTipView
    {
        private const string DefaultEmoji = "\U0001F4B0";
        private const string Title = "收取利息";

        /// <summary>
        /// 规则参数绑定：每 <paramref name="perGold"/> 枚金币产 <paramref name="gainPer"/> 枚，
        /// 上限 <paramref name="maxGain"/> 枚；<paramref name="nodeDays"/> 为节点占用天数。
        /// </summary>
        public void Bind(int perGold, int gainPer, int maxGain, int nodeDays, Sprite icon = null)
        {
            string desc = $"每有{perGold}枚金币，获得{gainPer}枚，最高可获得{maxGain}枚";
            Bind(desc, nodeDays, icon);
        }

        /// <summary>直接给规则描述 + 天数绑定。</summary>
        public void Bind(string desc, int nodeDays, Sprite icon = null)
        {
            ApplyIcon(icon, DefaultEmoji);
            ApplyTexts(Title, desc);
            ApplyFooter($"节点天数：{nodeDays}天");
        }
    }
}
