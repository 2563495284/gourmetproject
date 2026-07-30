namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 行动轴节点共用的 hover Tip。
    /// 普通节点展示行动配置的名称 / 描述，Boss 节点由调用方传入预览后的 Debuff 信息。
    /// </summary>
    public sealed class TimelineNodeTipView : ActionTipView
    {
        public void Bind(string title, string desc, string footer)
        {
            ApplyTexts(title, desc);
            ApplyFooter(footer);
        }
    }
}
