using System;
using System.Collections.Generic;
using GameFramework.Event;
using UnityGameFramework.Runtime;

namespace GourmetProject.Runtime.Event
{
    /// <summary>
    /// 事件订阅管理器（开箱即用）。集中记录通过它建立的所有订阅，便于在界面/流程关闭时
    /// 一次性退订，避免漏退导致的悬挂回调。与玩法无关。
    /// </summary>
    public sealed class EventSubscriptions
    {
        private readonly EventComponent _event;
        private readonly List<KeyValuePair<int, EventHandler<GameEventArgs>>> _subscriptions =
            new List<KeyValuePair<int, EventHandler<GameEventArgs>>>();

        public EventSubscriptions(EventComponent eventComponent)
        {
            _event = eventComponent ?? throw new ArgumentNullException(nameof(eventComponent));
        }

        public void Subscribe(int id, EventHandler<GameEventArgs> handler)
        {
            _event.Subscribe(id, handler);
            _subscriptions.Add(new KeyValuePair<int, EventHandler<GameEventArgs>>(id, handler));
        }

        public void UnsubscribeAll()
        {
            for (int i = 0; i < _subscriptions.Count; i++)
            {
                _event.Unsubscribe(_subscriptions[i].Key, _subscriptions[i].Value);
            }

            _subscriptions.Clear();
        }
    }
}
