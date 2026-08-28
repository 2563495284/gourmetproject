using System;
using System.Collections.Generic;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Tutorial
{
    /// <summary>
    /// 暂存已写入持有栏的教学物品，直到对应的获得表现抵达目标槽位后再通知教程系统。
    /// </summary>
    internal sealed class TutorialAcquiredItemPresentationGate
    {
        private readonly Action<RunContentAcquisition> _onPresented;
        private readonly Queue<RunContentAcquisition> _pending = new();

        public TutorialAcquiredItemPresentationGate(Action<RunContentAcquisition> onPresented)
        {
            _onPresented = onPresented ?? throw new ArgumentNullException(nameof(onPresented));
        }

        public bool HasPending => _pending.Count > 0;

        public void Stage(RunContentAcquisition acquisition)
        {
            if (acquisition != null)
            {
                _pending.Enqueue(acquisition);
            }
        }

        public void Complete()
        {
            while (_pending.Count > 0)
            {
                _onPresented(_pending.Dequeue());
            }
        }

        public void Clear()
        {
            _pending.Clear();
        }
    }
}
