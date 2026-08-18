using System;
using System.Collections.Generic;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 被动演出队列：同一时间只跑一条。完成和取消都必须回到空闲，
    /// 避免输入锁或切页等待永远不结束。
    /// </summary>
    internal sealed class PassivePresentationScheduler
    {
        private readonly Queue<PassivePresentationWork> _queue = new();
        private Action<bool> _setLocked;
        private PassivePresentationWork _active;
        private int _version;
        private Action _onIdle;

        public PassivePresentationScheduler(Action<bool> setLocked = null)
        {
            _setLocked = setLocked;
        }

        public void BindLock(Action<bool> setLocked)
        {
            _setLocked = setLocked;
        }

        public bool IsBusy => _active != null || _queue.Count > 0;

        public int Version => _version;

        public void Enqueue(Action<Action> start, Action cancel = null)
        {
            if (start == null)
            {
                cancel?.Invoke();
                if (!IsBusy)
                {
                    NotifyIdle();
                }

                return;
            }

            _queue.Enqueue(new PassivePresentationWork
            {
                Start = start,
                Cancel = cancel,
            });
            if (_active == null)
            {
                StartNext();
            }
        }

        public void CancelAll()
        {
            _version++;
            _active?.Cancel?.Invoke();
            _active = null;
            while (_queue.Count > 0)
            {
                _queue.Dequeue().Cancel?.Invoke();
            }

            _setLocked?.Invoke(false);
            NotifyIdle();
        }

        public void WhenIdle(Action onIdle)
        {
            if (onIdle == null)
            {
                return;
            }

            if (!IsBusy)
            {
                onIdle();
                return;
            }

            _onIdle += onIdle;
        }

        private void StartNext()
        {
            if (_queue.Count == 0)
            {
                _active = null;
                _setLocked?.Invoke(false);
                NotifyIdle();
                return;
            }

            _active = _queue.Dequeue();
            _setLocked?.Invoke(true);
            int version = ++_version;
            bool completed = false;
            _active.Start(() =>
            {
                if (completed || version != _version)
                {
                    return;
                }

                completed = true;
                _active = null;
                StartNext();
            });
        }

        private void NotifyIdle()
        {
            Action idle = _onIdle;
            _onIdle = null;
            idle?.Invoke();
        }

        private sealed class PassivePresentationWork
        {
            public Action<Action> Start;
            public Action Cancel;
        }
    }
}
