using UnityEngine;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 行动轴商店节点 hover Tips（原型图第 2 张）：
    /// 标题「商店」+ 说明框（消耗金币购买菜品 / 道具 / 胃部碎片，优化菜谱）+ 底部「节点天数：{天}」。
    /// 固定结构在 ShopNodeTipView.prefab，内容由 <see cref="Bind"/> 数据驱动。
    /// </summary>
    public sealed class ShopNodeTipView : ActionTipView
    {
        private const string DefaultEmoji = "\U0001F6CD";
        private const string DefaultTitle = "商店";
        private const string DefaultDesc = "消耗金币购买菜品、道具、胃部碎片，优化菜谱";

        /// <summary>用默认商店文案 + 节点天数绑定。</summary>
        public void Bind(int nodeDays, Sprite icon = null)
        {
            Bind(DefaultTitle, DefaultDesc, nodeDays, icon);
        }

        /// <summary>字段级绑定。</summary>
        public void Bind(string title, string desc, int nodeDays, Sprite icon = null)
        {
            ApplyTexts(title, desc);
            ApplyFooter($"节点天数：{nodeDays}天");
        }
    }
}
