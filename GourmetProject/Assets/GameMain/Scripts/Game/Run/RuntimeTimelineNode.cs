namespace GourmetProject.Game.Run
{
    /// <summary>
    /// 运行时动态追加到行动轴的节点（如「奖励单」加的奖励节点）。
    /// 与配置 <c>cfg.TimelineNode</c> 语义一致，但由玩家道具在局内生成、随周清空。
    /// </summary>
    public readonly struct RuntimeTimelineNode
    {
        public RuntimeTimelineNode(string id, string timelineId, int day, string actionId)
        {
            Id = id ?? string.Empty;
            TimelineId = timelineId ?? string.Empty;
            Day = day;
            ActionId = actionId ?? string.Empty;
        }

        public string Id { get; }

        public string TimelineId { get; }

        public int Day { get; }

        public string ActionId { get; }
    }
}
