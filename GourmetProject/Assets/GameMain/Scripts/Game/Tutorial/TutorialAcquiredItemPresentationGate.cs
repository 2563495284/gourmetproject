using System;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Tutorial
{
    /// <summary>
    /// 暂存已写入持有栏的教学物品，直到对应的获得表现抵达目标槽位后再通知教程系统。
    /// </summary>
    internal sealed class TutorialAcquiredItemPresentationGate
    {
        private readonly Action<RunContentAcquisition> _onPresented;
        private RunContentAcquisition _pending;

        public TutorialAcquiredItemPresentationGate(Action<RunContentAcquisition> onPresented)
        {
            _onPresented = onPresented ?? throw new ArgumentNullException(nameof(onPresented));
        }

        public bool HasPending => _pending != null;

        public void Stage(RunContentAcquisition acquisition)
        {
            _pending = acquisition;
        }

        public void Complete()
        {
            RunContentAcquisition acquisition = _pending;
            if (acquisition == null)
            {
                return;
            }

            _pending = null;
            _onPresented(acquisition);
        }

        public void Clear()
        {
            _pending = null;
        }
    }
}
