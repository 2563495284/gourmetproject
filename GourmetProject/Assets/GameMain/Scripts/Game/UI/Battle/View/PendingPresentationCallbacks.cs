using System;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 检查层淡入/淡出等待点。强制拆掉动画时也必须把回调补上，
    /// 否则被动演出会一直占着输入锁。
    /// </summary>
    internal sealed class PendingPresentationCallbacks
    {
        private Action _shown;
        private Action _hidden;

        public void SetShown(Action shown)
        {
            _shown = shown;
        }

        public void SetHidden(Action hidden)
        {
            _hidden = hidden;
        }

        public void CompleteShown()
        {
            Action action = _shown;
            _shown = null;
            action?.Invoke();
        }

        public void CompleteHidden()
        {
            Action action = _hidden;
            _hidden = null;
            action?.Invoke();
        }

        public void CompleteAll()
        {
            CompleteShown();
            CompleteHidden();
        }
    }
}
