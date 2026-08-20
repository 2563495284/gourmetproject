using System;
using System.Collections.Generic;

namespace GourmetProject.Game.UI.Hud
{
    public sealed class TimelineAxisSelectionRequest
    {
        private TimelineAxisSelectionRequest() { }

        public TimelineAxisSelectionMode Mode { get; private set; }
        public TimelineAxisNodeState PreviewNode { get; private set; }
        public IReadOnlyCollection<int> ValidDays { get; private set; }
        public IReadOnlyCollection<string> ValidNodeIds { get; private set; }
        public Action<int> ConfirmDay { get; private set; }
        public Action<string> ConfirmNode { get; private set; }
        public Action Cancel { get; private set; }

        public static TimelineAxisSelectionRequest AddDay(
            TimelineAxisNodeState previewNode,
            IReadOnlyCollection<int> validDays,
            Action<int> confirm,
            Action cancel)
        {
            return new TimelineAxisSelectionRequest
            {
                Mode = TimelineAxisSelectionMode.AddDay,
                PreviewNode = previewNode?.Clone(),
                ValidDays = validDays,
                ConfirmDay = confirm,
                Cancel = cancel,
            };
        }

        public static TimelineAxisSelectionRequest Nodes(
            TimelineAxisSelectionMode mode,
            IReadOnlyCollection<string> validNodeIds,
            Action<string> confirm,
            Action cancel)
        {
            if (mode != TimelineAxisSelectionMode.DeleteNode
                && mode != TimelineAxisSelectionMode.ExecuteNode)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            return new TimelineAxisSelectionRequest
            {
                Mode = mode,
                ValidNodeIds = validNodeIds,
                ConfirmNode = confirm,
                Cancel = cancel,
            };
        }
    }

    /// <summary>无 Unity 依赖的时间轴选点状态机。</summary>
    public sealed class TimelineAxisSelectionController
    {
        public const string PreviewId = "__preview__";

        private readonly HashSet<int> _validDays = new HashSet<int>();
        private readonly HashSet<string> _validNodeIds = new HashSet<string>(StringComparer.Ordinal);
        private TimelineAxisSelectionRequest _request;
        private bool _committing;

        public TimelineAxisSelectionMode Mode => _request?.Mode ?? TimelineAxisSelectionMode.None;
        public TimelineAxisNodeState PreviewNode => _request?.PreviewNode;
        public IReadOnlyCollection<int> ValidDays => _validDays;
        public IReadOnlyCollection<string> ValidNodeIds => _validNodeIds;
        public bool IsCommitting => _committing;
        public bool IsActive => _request != null;

        public bool Begin(TimelineAxisSelectionRequest request)
        {
            End();
            if (request == null)
            {
                return false;
            }

            if (request.Mode == TimelineAxisSelectionMode.AddDay)
            {
                if (request.PreviewNode == null || request.ConfirmDay == null)
                {
                    return false;
                }

                if (request.ValidDays != null)
                {
                    foreach (int day in request.ValidDays)
                    {
                        _validDays.Add(day);
                    }
                }

                if (_validDays.Count == 0)
                {
                    _validDays.Clear();
                    return false;
                }
            }
            else
            {
                if ((request.Mode != TimelineAxisSelectionMode.DeleteNode
                        && request.Mode != TimelineAxisSelectionMode.ExecuteNode)
                    || request.ConfirmNode == null)
                {
                    return false;
                }

                if (request.ValidNodeIds != null)
                {
                    foreach (string id in request.ValidNodeIds)
                    {
                        if (!string.IsNullOrEmpty(id))
                        {
                            _validNodeIds.Add(id);
                        }
                    }
                }

                if (_validNodeIds.Count == 0)
                {
                    _validNodeIds.Clear();
                    return false;
                }
            }

            _request = request;
            return true;
        }

        public bool CanTargetDay(int day)
        {
            return Mode == TimelineAxisSelectionMode.AddDay
                && !_committing
                && _validDays.Contains(day);
        }

        public bool CanTargetNode(string nodeId)
        {
            return (Mode == TimelineAxisSelectionMode.DeleteNode
                    || Mode == TimelineAxisSelectionMode.ExecuteNode)
                && !_committing
                && !string.IsNullOrEmpty(nodeId)
                && _validNodeIds.Contains(nodeId);
        }

        public bool TryConfirmDay(int day)
        {
            if (!CanTargetDay(day) || _request?.ConfirmDay == null)
            {
                return false;
            }

            _committing = true;
            try
            {
                _request.ConfirmDay(day);
                return true;
            }
            finally
            {
                if (Mode == TimelineAxisSelectionMode.AddDay)
                {
                    _committing = false;
                }
            }
        }

        public bool TryConfirmNode(string nodeId)
        {
            if (!CanTargetNode(nodeId) || _request?.ConfirmNode == null)
            {
                return false;
            }

            _committing = true;
            try
            {
                _request.ConfirmNode(nodeId);
                return true;
            }
            finally
            {
                if (Mode == TimelineAxisSelectionMode.DeleteNode
                    || Mode == TimelineAxisSelectionMode.ExecuteNode)
                {
                    _committing = false;
                }
            }
        }

        public void Cancel()
        {
            Action callback = _request?.Cancel;
            End();
            callback?.Invoke();
        }

        public void End()
        {
            _request = null;
            _committing = false;
            _validDays.Clear();
            _validNodeIds.Clear();
        }
    }
}
