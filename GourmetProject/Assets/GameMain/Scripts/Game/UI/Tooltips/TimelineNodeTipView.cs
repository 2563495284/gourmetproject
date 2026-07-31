namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 行动轴节点共用的 hover Tip。
    /// 普通节点展示行动配置的名称 / 描述，Boss 节点由调用方传入预览后的 Debuff 信息。
    /// 版式与道具 Tip 一致，只展示标题与描述。
    /// </summary>
    public sealed class TimelineNodeTipView : ActionTipView
    {
        public void Bind(string title, string desc)
        {
            ApplyTexts(title, desc);
            ApplyFooter(null);
        }
    }
}
