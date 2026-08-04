namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 时间轴节点共用的 hover Tip。
    /// 普通节点展示行动配置的名称 / 描述，星级评鉴节点由调用方传入预览后的 Debuff 信息。
    /// 版式与装饰品和消耗品 Tip 一致，只展示标题与描述。
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
