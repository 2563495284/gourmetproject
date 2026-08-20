using System;
using System.Collections.Generic;

namespace GourmetProject.Game.UI.Common
{
    internal readonly struct ToastRequest : IEquatable<ToastRequest>
    {
        public ToastRequest(string message, ToastKind kind)
        {
            Message = message;
            Kind = kind;
        }

        public string Message { get; }
        public ToastKind Kind { get; }

        public bool Equals(ToastRequest other)
        {
            return Kind == other.Kind
                && string.Equals(Message, other.Message, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is ToastRequest other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Message, Kind);
        }
    }

    /// <summary>
    /// 纯数据 FIFO。活动项不占等待队列容量；队列满时淘汰最旧的等待项，保留最新反馈。
    /// </summary>
    internal sealed class ToastQueue
    {
        public const int DefaultCapacity = 8;

        private readonly int _capacity;
        private readonly LinkedList<ToastRequest> _pending = new LinkedList<ToastRequest>();
        private ToastRequest? _active;

        public ToastQueue(int capacity = DefaultCapacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _capacity = capacity;
        }

        public int PendingCount => _pending.Count;
        public bool HasActive => _active.HasValue;
        public ToastRequest? Active => _active;

        public bool TryEnqueue(string message, ToastKind kind)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            var request = new ToastRequest(message.Trim(), kind);
            if ((_active.HasValue && _active.Value.Equals(request)) || Contains(request))
            {
                return false;
            }

            if (_pending.Count >= _capacity)
            {
                _pending.RemoveFirst();
            }

            _pending.AddLast(request);
            return true;
        }

        public bool TryDequeue(out ToastRequest request)
        {
            if (_active.HasValue || _pending.First == null)
            {
                request = default;
                return false;
            }

            request = _pending.First.Value;
            _pending.RemoveFirst();
            _active = request;
            return true;
        }

        public void CompleteActive()
        {
            _active = null;
        }

        public void Clear()
        {
            _pending.Clear();
            _active = null;
        }

        private bool Contains(ToastRequest request)
        {
            for (LinkedListNode<ToastRequest> node = _pending.First;
                 node != null;
                 node = node.Next)
            {
                if (node.Value.Equals(request))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
