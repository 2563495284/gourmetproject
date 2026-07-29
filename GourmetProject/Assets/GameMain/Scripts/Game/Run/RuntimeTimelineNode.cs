namespace GourmetProject.Game.Run
{
    /// <summary>
    /// 当前周行动轴节点快照。周开始时从配置复制，之后被被动/主动道具直接改写并随存档保存。
    /// 与配置 <c>cfg.TimelineNode</c> 语义一致。
    /// </summary>
    public readonly struct RuntimeTimelineNode
    {
        public RuntimeTimelineNode(
            string id,
            string timelineId,
            int day,
            string actionId,
            string sourceItemId = "",
            bool weekEndAnchored = false)
        {
            Id = id ?? string.Empty;
            TimelineId = timelineId ?? string.Empty;
            Day = day;
            ActionId = actionId ?? string.Empty;
            SourceItemId = sourceItemId ?? string.Empty;
            WeekEndAnchored = weekEndAnchored;
        }

        public string Id { get; }

        public string TimelineId { get; }

        public int Day { get; }

        public string ActionId { get; }

        public string SourceItemId { get; }

        public bool WeekEndAnchored { get; }

        public RuntimeTimelineNode WithDay(int day)
        {
            return new RuntimeTimelineNode(Id, TimelineId, day, ActionId, SourceItemId, WeekEndAnchored);
        }

        public RuntimeTimelineNode WithActionId(string actionId)
        {
            return new RuntimeTimelineNode(Id, TimelineId, Day, actionId, SourceItemId, WeekEndAnchored);
        }
    }
}
