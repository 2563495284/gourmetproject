namespace cfg
{
    public sealed partial class GameEvent
    {
        /// <summary>
        /// 第一个分类是事件的主分类，负责权重加成、保底计数和触发反馈。
        /// </summary>
        public ActionBehavior PrimaryEventType =>
            EventTypes != null && EventTypes.Count > 0
                ? EventTypes[0]
                : ActionBehavior.Event;

        /// <summary>
        /// 事件是否属于指定分类。多分类只改变池成员关系，不会让事件在同一池中出现多份。
        /// </summary>
        public bool HasEventType(ActionBehavior eventType)
        {
            if (EventTypes == null)
            {
                return false;
            }

            for (int i = 0; i < EventTypes.Count; i++)
            {
                if (EventTypes[i] == eventType)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>是否属于 act_event 合并抽取支持的任一事件分类。</summary>
        public bool IsActionEventPoolMember =>
            HasEventType(ActionBehavior.Event)
            || HasEventType(ActionBehavior.Reward)
            || HasEventType(ActionBehavior.Negative);
    }
}
