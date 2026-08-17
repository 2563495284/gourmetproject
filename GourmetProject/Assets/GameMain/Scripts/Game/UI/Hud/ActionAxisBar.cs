using System;
using System.Collections.Generic;
using System.Globalization;
using DG.Tweening;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using UnityEngine;
using TMPro;

namespace GourmetProject.Game.UI.Hud
{
    public enum TimelineAxisSelectionMode
    {
        None,
        AddDay,
        DeleteNode,
        ExecuteNode,
    }

    /// <summary>
    /// BattleForm 顶部离散时间轴：整数日期点、同日重叠气泡及消耗品的轴上选点交互。
    /// </summary>
    public sealed class ActionAxisBar : MonoBehaviour
    {
        private const float ProgressDurationMultiplier = 2f;

        [Header("引用")]
        [SerializeField] private RectTransform _container;
        [SerializeField] private RectTransform _positionMarker;
        [SerializeField] private TMP_Text _currentDayText;
        [Tooltip("已过天数进度条；代码只驱动其 anchorMax.x")]
        [SerializeField] private RectTransform _railFill;

        [Header("Prefab")]
        [SerializeField] private TimelineDayPointView _dayPointPrefab;
        [SerializeField] private TimelineDayNodeGroupView _dayNodeGroupPrefab;
        [SerializeField] private TimelineNodeBubbleView _nodeBubblePrefab;

        [Header("节点图标")]
        [SerializeField] private Sprite _shopNodeSprite;
        [SerializeField] private Sprite _interestNodeSprite;
        [SerializeField] private Sprite _bossNodeSprite;
        [SerializeField] private Sprite _eventNodeSprite;

        private readonly Dictionary<int, TimelineDayPointView> _dayPoints =
            new Dictionary<int, TimelineDayPointView>();
        private readonly List<TimelineDayPointView> _dayPointPool =
            new List<TimelineDayPointView>();
        private readonly List<int> _spareDays = new List<int>();
        private readonly Dictionary<int, TimelineDayNodeGroupView> _dayGroups =
            new Dictionary<int, TimelineDayNodeGroupView>();
        private readonly Dictionary<string, TimelineNodeBubbleView> _nodeBubbles =
            new Dictionary<string, TimelineNodeBubbleView>();
        private readonly Dictionary<string, int> _nodeDays = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _nodeActionIds = new Dictionary<string, string>();
        private readonly HashSet<int> _validAddDays = new HashSet<int>();
        private readonly HashSet<string> _targetableNodeIds = new HashSet<string>();
        private readonly Queue<PresentationWork> _presentationQueue = new Queue<PresentationWork>();

        private sealed class PresentationWork
        {
            public TimelinePresentationCue Cue;
            public float Speed;
            public Action OnComplete;
        }

        private GameRun _run;
        private Action<cfg.TimelineNode, GameObject> _onNodeCreated;
        private TimelineAxisSelectionMode _selectionMode;
        private string _previewActionId;
        private int _hoveredPreviewDay = -1;
        private TimelineNodeBubbleView _previewBubble;
        private TimelineDayNodeGroupView _previewGroup;
        private Action<int> _confirmDay;
        private Action<string> _confirmNode;
        private Action _cancelSelection;
        private string _builtTimelineId;
        private string _presentedExecutingNodeId;
        private bool _hasBuiltNodes;
        private bool _commitInProgress;
        private IReadOnlyList<RuntimeTimelineNodeSnapshot> _presentationNodes;
        private float _presentationLengthDays;
        private TimelineAxisViewState _viewState;
        private float _displayedDay;
        private bool _presentationBusy;
        private bool _completingPresentation;
        private PresentationWork _activePresentation;
        private Tween _presentationTween;

        public TimelineAxisSelectionMode SelectionMode => _selectionMode;
        public bool HasPreview => _previewBubble != null;
        public int PreviewDay => _hoveredPreviewDay;
        public float DisplayedDay => _displayedDay;
        public bool IsPresenting => _presentationBusy || _presentationQueue.Count > 0;
        public bool HasActivePresentationTweens
        {
            get
            {
                if (_presentationTween?.IsActive() ?? false)
                {
                    return true;
                }

                foreach (TimelineDayNodeGroupView group in _dayGroups.Values)
                {
                    if (group != null && group.IsAnimating)
                    {
                        return true;
                    }
                }

                foreach (TimelineNodeBubbleView bubble in _nodeBubbles.Values)
                {
                    if (bubble != null && bubble.IsAnimating)
                    {
                        return true;
                    }
                }

                foreach (TimelineDayPointView point in _dayPoints.Values)
                {
                    if (point != null && point.IsAnimating)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public TimelineDayNodeGroupView GetDayGroup(int day)
        {
            return _dayGroups.TryGetValue(day, out TimelineDayNodeGroupView group) ? group : null;
        }

        public bool TryGetNodeBubble(string nodeId, out TimelineNodeBubbleView bubble)
        {
            bubble = null;
            return !string.IsNullOrEmpty(nodeId)
                && _nodeBubbles.TryGetValue(nodeId, out bubble)
                && bubble != null;
        }

        public void Build(
            GameRun run,
            Action<cfg.TimelineNode, GameObject> onNodeCreated = null,
            string executingNodeId = null,
            float? currentDayOverride = null)
        {
            _presentationNodes = null;
            _presentationLengthDays = 0f;
            bool timelineChanged = _run != run
                || !string.Equals(_builtTimelineId, run?.CurrentTimelineId, StringComparison.Ordinal);
            if (timelineChanged)
            {
                ClearPreview(animate: false);
                ClearGroups();
                _builtTimelineId = run?.CurrentTimelineId;
                _hasBuiltNodes = false;
            }

            BindState(
                CreateState(
                    run,
                    null,
                    run?.TimelineLengthDays ?? 1f,
                    currentDayOverride ?? run?.CurrentDay ?? 0f,
                    executingNodeId),
                run,
                onNodeCreated,
                animate: _hasBuiltNodes && !timelineChanged);
        }

        public void BuildPresentation(
            GameRun run,
            IReadOnlyList<RuntimeTimelineNodeSnapshot> nodes,
            float lengthDays,
            Action<cfg.TimelineNode, GameObject> onNodeCreated = null,
            string executingNodeId = null,
            bool animate = false)
        {
            _presentationNodes = nodes ?? Array.Empty<RuntimeTimelineNodeSnapshot>();
            _presentationLengthDays = Mathf.Max(1f, lengthDays);

            if (!animate)
            {
                ClearPreview(animate: false);
                ClearGroups();
                _builtTimelineId = run?.CurrentTimelineId;
                _hasBuiltNodes = false;
            }

            BindState(
                CreateState(run, _presentationNodes, _presentationLengthDays, run?.CurrentDay ?? 0f, executingNodeId),
                run,
                onNodeCreated,
                animate);
        }

        public void BindState(
            TimelineAxisViewState state,
            GameRun run = null,
            Action<cfg.TimelineNode, GameObject> onNodeCreated = null,
            bool animate = false,
            float animationSpeed = 1f)
        {
            if (state == null)
            {
                return;
            }

            _run = run;
            _viewState = state.Clone();
            _viewState.LengthDays = Mathf.Max(1f, _viewState.LengthDays);
            _displayedDay = Mathf.Clamp(_viewState.CurrentDay, 0f, _viewState.LengthDays);
            _presentedExecutingNodeId = string.Empty;
            if (onNodeCreated != null)
            {
                _onNodeCreated = onNodeCreated;
            }

            RebuildAxisChrome();
            ReconcileNodeGroups(animate, animationSpeed);
            _hasBuiltNodes = true;
            ApplySelectionMode();
        }

        public void PlayCue(
            TimelinePresentationCue cue,
            Action onComplete = null,
            float speed = 1f)
        {
            if (cue == null)
            {
                onComplete?.Invoke();
                return;
            }

            _presentationQueue.Enqueue(new PresentationWork
            {
                Cue = cue,
                Speed = Mathf.Max(0.05f, speed),
                OnComplete = onComplete,
            });
            if (_completingPresentation)
            {
                return;
            }

            PlayNextCue();
        }

        public void CompletePresentation()
        {
            if (_completingPresentation)
            {
                return;
            }

            _completingPresentation = true;
            _presentationTween?.Kill(complete: false);
            _presentationTween = null;
            foreach (TimelineNodeBubbleView bubble in _nodeBubbles.Values)
            {
                bubble?.CompletePresentation();
            }

            PresentationWork active = _activePresentation;
            _activePresentation = null;
            _presentationBusy = false;
            try
            {
                CompleteWork(active);
                while (_presentationQueue.Count > 0)
                {
                    CompleteWork(_presentationQueue.Dequeue());
                }
            }
            finally
            {
                _completingPresentation = false;
            }
        }

        private void CompleteWork(PresentationWork work)
        {
            if (work == null)
            {
                return;
            }

            if (work.Cue?.TargetState != null)
            {
                BindState(work.Cue.TargetState, _run, _onNodeCreated, animate: false);
            }

            work.OnComplete?.Invoke();
        }

        /// <summary>中断所有表现而不触发表现回调；页面销毁或实验室重置时使用。</summary>
        public void CancelPresentation()
        {
            AbortPresentation();
        }

        private void PlayNextCue()
        {
            if (_presentationBusy || _presentationQueue.Count == 0)
            {
                return;
            }

            _presentationBusy = true;
            _activePresentation = _presentationQueue.Dequeue();
            TimelinePresentationCue cue = _activePresentation.Cue;
            switch (cue.Kind)
            {
                case TimelinePresentationCueKind.Advance:
                    PlayAdvance(cue, _activePresentation.Speed);
                    break;
                case TimelinePresentationCueKind.TriggerStart:
                    PlayNodeCue(cue, start: true);
                    break;
                case TimelinePresentationCueKind.TriggerComplete:
                    PlayNodeCue(cue, start: false);
                    break;
                case TimelinePresentationCueKind.Remove:
                case TimelinePresentationCueKind.Skip:
                    PlayRemovalCue(cue);
                    break;
                case TimelinePresentationCueKind.Replace:
                    if (_nodeBubbles.TryGetValue(cue.NodeId, out TimelineNodeBubbleView changed)
                        && changed != null)
                    {
                        changed.PlayChange(
                            FinishActiveCue,
                            _activePresentation.Speed,
                            () => ApplyTargetState(cue, animate: false));
                    }
                    else
                    {
                        ApplyTargetState(cue, animate: false);
                        FinishActiveCue();
                    }
                    break;
                case TimelinePresentationCueKind.Add:
                case TimelinePresentationCueKind.Move:
                    ApplyTargetState(cue, animate: true, _activePresentation.Speed);
                    _presentationTween = DOVirtual.DelayedCall(
                            0.38f / _activePresentation.Speed,
                            FinishActiveCue,
                            ignoreTimeScale: true)
                        .SetTarget(this);
                    break;
                case TimelinePresentationCueKind.Resize:
                    PlayResize(cue, _activePresentation.Speed);
                    break;
                default:
                    ApplyTargetState(cue, animate: true, _activePresentation.Speed);
                    FinishActiveCue();
                    break;
            }
        }

        private void PlayAdvance(TimelinePresentationCue cue, float speed)
        {
            float length = Mathf.Max(1f, cue.TargetState?.LengthDays ?? _viewState?.LengthDays ?? 1f);
            float start = Mathf.Clamp(cue.FromDay, 0f, length);
            float target = Mathf.Clamp(cue.ToDay, 0f, length);
            if (Mathf.Abs(_displayedDay - start) > TimelineMath.Epsilon)
            {
                start = _displayedDay;
            }

            float distance = Mathf.Abs(target - start);
            float duration = Mathf.Clamp(0.38f + 0.18f * distance, 0.45f, 1.10f)
                * ProgressDurationMultiplier
                / speed;
            int lastWholeDay = Mathf.FloorToInt(start + TimelineMath.Epsilon);
            _presentationTween = DOTween.To(
                    () => start,
                    value =>
                    {
                        _displayedDay = value;
                        UpdateProgressVisual(value, length);
                        int wholeDay = Mathf.FloorToInt(value + TimelineMath.Epsilon);
                        if (wholeDay > lastWholeDay)
                        {
                            for (int day = lastWholeDay + 1; day <= wholeDay; day++)
                            {
                                PulseDayPoint(day, speed);
                                PulseNodesAtDay(day, speed);
                            }

                            lastWholeDay = wholeDay;
                        }
                    },
                    target,
                    duration)
                .SetEase(Ease.InOutCubic)
                .SetUpdate(true)
                .SetTarget(this)
                .OnComplete(() =>
                {
                    _displayedDay = target;
                    if (cue.TargetState != null)
                    {
                        _viewState = cue.TargetState.Clone();
                        _viewState.CurrentDay = target;
                    }

                    UpdateProgressVisual(target, length);
                    bool landedOnWholeDay = distance > TimelineMath.Epsilon
                        && Mathf.Abs(target - Mathf.Round(target)) <= TimelineMath.Epsilon;
                    if (landedOnWholeDay)
                    {
                        _presentationTween = DOVirtual.DelayedCall(
                                0.12f / speed,
                                FinishActiveCue,
                                ignoreTimeScale: true)
                            .SetTarget(this);
                    }
                    else
                    {
                        FinishActiveCue();
                    }
                });
        }

        private void PlayNodeCue(TimelinePresentationCue cue, bool start)
        {
            if (start)
            {
                ApplyTargetState(cue, animate: false);
            }

            if (!_nodeBubbles.TryGetValue(cue.NodeId ?? string.Empty, out TimelineNodeBubbleView bubble)
                || bubble == null)
            {
                if (!start)
                {
                    ApplyTargetState(cue, animate: false);
                }

                FinishActiveCue();
                return;
            }

            Action finished = () =>
            {
                if (!start)
                {
                    ApplyTargetState(cue, animate: false);
                }

                FinishActiveCue();
            };
            if (start)
            {
                bubble.PlayTriggerStart(finished, _activePresentation.Speed);
            }
            else
            {
                bubble.PlayTriggerComplete(finished, _activePresentation.Speed);
            }
        }

        private void PlayResize(TimelinePresentationCue cue, float speed)
        {
            if (cue?.TargetState == null)
            {
                FinishActiveCue();
                return;
            }

            float fromLength = Mathf.Max(1f, _viewState?.LengthDays ?? cue.FromDay);
            float toLength = Mathf.Max(1f, cue.TargetState.LengthDays);
            TimelineAxisViewState target = cue.TargetState.Clone();
            BindState(target, _run, _onNodeCreated, animate: false);
            float currentDay = Mathf.Clamp(target.CurrentDay, 0f, toLength);

            void ApplyLength(float displayLength)
            {
                displayLength = Mathf.Max(1f, displayLength);
                foreach (KeyValuePair<int, TimelineDayPointView> pair in _dayPoints)
                {
                    pair.Value?.SetAxisPosition(Mathf.Clamp01(pair.Key / displayLength));
                }

                foreach (KeyValuePair<int, TimelineDayNodeGroupView> pair in _dayGroups)
                {
                    pair.Value?.SetAxisPosition(
                        Mathf.Clamp01(pair.Key / displayLength),
                        animate: false);
                }

                UpdateProgressVisual(currentDay, displayLength);
            }

            ApplyLength(fromLength);
            float displayedLength = fromLength;
            _presentationTween = DOTween.To(
                    () => displayedLength,
                    value =>
                    {
                        displayedLength = value;
                        ApplyLength(value);
                    },
                    toLength,
                    0.38f / Mathf.Max(0.05f, speed))
                .SetEase(Ease.InOutCubic)
                .SetUpdate(true)
                .SetTarget(this)
                .OnComplete(() =>
                {
                    BindState(target, _run, _onNodeCreated, animate: false);
                    FinishActiveCue();
                });
        }

        private void PlayRemovalCue(TimelinePresentationCue cue)
        {
            if (!_nodeBubbles.TryGetValue(cue.NodeId ?? string.Empty, out TimelineNodeBubbleView bubble)
                || bubble == null)
            {
                ApplyTargetState(cue, animate: false);
                FinishActiveCue();
                return;
            }

            Action finished = () =>
            {
                ApplyTargetState(cue, animate: false);
                FinishActiveCue();
            };
            if (cue.Kind == TimelinePresentationCueKind.Skip)
            {
                bubble.PlaySkip(finished, _activePresentation.Speed);
            }
            else
            {
                bubble.PlayRemove(finished, _activePresentation.Speed);
            }
        }

        private void ApplyTargetState(
            TimelinePresentationCue cue,
            bool animate,
            float animationSpeed = 1f)
        {
            if (cue?.TargetState != null)
            {
                BindState(cue.TargetState, _run, _onNodeCreated, animate, animationSpeed);
            }
        }

        private void FinishActiveCue()
        {
            if (!_presentationBusy)
            {
                return;
            }

            _presentationTween = null;
            PresentationWork finished = _activePresentation;
            _activePresentation = null;
            _presentationBusy = false;
            finished?.OnComplete?.Invoke();
            PlayNextCue();
        }

        internal static TimelineAxisViewState CreateState(
            GameRun run,
            IReadOnlyList<RuntimeTimelineNodeSnapshot> snapshots,
            float lengthDays,
            float currentDay,
            string executingNodeId)
        {
            var state = new TimelineAxisViewState
            {
                LengthDays = Mathf.Max(1f, lengthDays),
                CurrentDay = Mathf.Max(0f, currentDay),
            };
            if (run == null)
            {
                return state;
            }

            if (snapshots != null)
            {
                foreach (RuntimeTimelineNodeSnapshot snapshot in snapshots)
                {
                    AddStateNode(run, state, snapshot?.Id, snapshot?.Day ?? 0, snapshot?.ActionId, executingNodeId);
                }

                return state;
            }

            foreach (cfg.TimelineNode node in TimelineService.GetNodes(run))
            {
                if (node != null)
                {
                    AddStateNode(run, state, node.Id, node.Day, node.ActionId, executingNodeId);
                }
            }

            return state;
        }

        private static void AddStateNode(
            GameRun run,
            TimelineAxisViewState state,
            string id,
            int day,
            string actionId,
            string executingNodeId)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            cfg.GameAction action = run.Tables?.TbAction.GetOrDefault(actionId);
            state.Nodes.Add(new TimelineAxisNodeState
            {
                Id = id,
                Day = day,
                ActionId = actionId ?? string.Empty,
                Kind = ActionDisplay.KindOf(run.Tables, action),
                Completed = run.IsNodeTriggered(id),
                Executing = run.IsTimelineNodeExecutionInProgress(id)
                    || string.Equals(id, executingNodeId, StringComparison.Ordinal),
            });
        }

        public bool BeginAddDaySelection(
            GameRun run,
            string actionId,
            IEnumerable<int> validDays,
            Action<int> onConfirm,
            Action onCancel)
        {
            if (run == null || string.IsNullOrEmpty(actionId) || onConfirm == null)
            {
                return false;
            }

            ClearPreview(animate: false);
            _run = run;
            _selectionMode = TimelineAxisSelectionMode.AddDay;
            _previewActionId = actionId;
            _confirmDay = onConfirm;
            _confirmNode = null;
            _cancelSelection = onCancel;
            _commitInProgress = false;
            _validAddDays.Clear();
            _targetableNodeIds.Clear();
            if (validDays != null)
            {
                foreach (int day in validDays)
                {
                    _validAddDays.Add(day);
                }
            }

            if (_validAddDays.Count == 0)
            {
                EndSelection(rebuild: false);
                return false;
            }

            RebuildAxisChrome();
            ReconcileNodeGroups(animate: false);
            ApplySelectionMode();
            return true;
        }

        public bool BeginDeleteNodeSelection(
            GameRun run,
            IEnumerable<string> nodeIds,
            Action<string> onConfirm,
            Action onCancel)
        {
            return BeginNodeSelection(
                run,
                nodeIds,
                TimelineAxisSelectionMode.DeleteNode,
                onConfirm,
                onCancel);
        }

        public bool BeginExecuteNodeSelection(
            GameRun run,
            IEnumerable<string> nodeIds,
            Action<string> onConfirm,
            Action onCancel)
        {
            return BeginNodeSelection(
                run,
                nodeIds,
                TimelineAxisSelectionMode.ExecuteNode,
                onConfirm,
                onCancel);
        }

        private bool BeginNodeSelection(
            GameRun run,
            IEnumerable<string> nodeIds,
            TimelineAxisSelectionMode mode,
            Action<string> onConfirm,
            Action onCancel)
        {
            if (run == null || onConfirm == null)
            {
                return false;
            }

            ClearPreview(animate: false);
            _run = run;
            _selectionMode = mode;
            _previewActionId = string.Empty;
            _confirmNode = onConfirm;
            _confirmDay = null;
            _cancelSelection = onCancel;
            _commitInProgress = false;
            _validAddDays.Clear();
            _targetableNodeIds.Clear();
            if (nodeIds != null)
            {
                foreach (string id in nodeIds)
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        _targetableNodeIds.Add(id);
                    }
                }
            }

            RebuildAxisChrome();
            ReconcileNodeGroups(animate: false);
            ApplySelectionMode();
            return true;
        }

        public void CancelSelection()
        {
            if (_selectionMode == TimelineAxisSelectionMode.None)
            {
                return;
            }

            Action cancel = _cancelSelection;
            EndSelection();
            cancel?.Invoke();
        }

        public void EndSelection(bool rebuild = true)
        {
            ClearPreview(animate: rebuild);
            _selectionMode = TimelineAxisSelectionMode.None;
            _previewActionId = string.Empty;
            _validAddDays.Clear();
            _targetableNodeIds.Clear();
            _confirmDay = null;
            _confirmNode = null;
            _cancelSelection = null;
            _commitInProgress = false;

            foreach (TimelineDayNodeGroupView group in _dayGroups.Values)
            {
                group?.EndNodeTargetMode();
            }

            if (rebuild)
            {
                RebuildAxisChrome();
                ReconcileNodeGroups(animate: true);
            }
        }

        public bool CommitAddDayPreview(string nodeId)
        {
            if (_selectionMode != TimelineAxisSelectionMode.AddDay
                || string.IsNullOrEmpty(nodeId)
                || _previewBubble == null
                || _previewGroup == null
                || _hoveredPreviewDay < 0
                || !_previewGroup.PromotePreview(nodeId, out TimelineNodeBubbleView bubble))
            {
                return false;
            }

            int day = _hoveredPreviewDay;
            cfg.GameAction action = _run?.Tables?.TbAction?.GetOrDefault(_previewActionId);
            ActionDisplayKind kind = ActionDisplay.KindOf(_run?.Tables, action);
            bubble.Bind(
                NodeSprite(kind),
                completed: false,
                executing: false,
                kind == ActionDisplayKind.Boss,
                preview: false,
                negative: kind == ActionDisplayKind.Negative);

            _nodeBubbles[nodeId] = bubble;
            _nodeDays[nodeId] = day;
            _nodeActionIds[nodeId] = _previewActionId ?? string.Empty;
            cfg.TimelineNode runtimeNode = _run != null
                ? TimelineService.GetNode(_run, nodeId)
                : null;
            _onNodeCreated?.Invoke(runtimeNode, bubble.gameObject);

            SetDayPointHighlight(day, false);
            _hoveredPreviewDay = -1;
            _previewBubble = null;
            _previewGroup = null;
            EndSelection(rebuild: false);
            return true;
        }

        private void RebuildAxisChrome()
        {
            if (_viewState == null || _container == null)
            {
                return;
            }

            float length = Mathf.Max(1f, _viewState.LengthDays);
            int wholeDays = Mathf.Max(1, Mathf.FloorToInt(length + TimelineMath.Epsilon));
            SyncDayPoints(wholeDays, length);
            UpdateProgressVisual(_displayedDay, length);
            BringGroupsToFront();
        }

        private void SyncDayPoints(int wholeDays, float length)
        {
            _spareDays.Clear();
            foreach (int day in _dayPoints.Keys)
            {
                if (day > wholeDays)
                {
                    _spareDays.Add(day);
                }
            }

            foreach (int day in _spareDays)
            {
                RecycleDayPoint(_dayPoints[day]);
                _dayPoints.Remove(day);
            }

            for (int day = 0; day <= wholeDays; day++)
            {
                if (!_dayPoints.TryGetValue(day, out TimelineDayPointView point) || point == null)
                {
                    point = RentDayPoint();
                    if (point == null)
                    {
                        return;
                    }

                    _dayPoints[day] = point;
                }

                point.Bind(day, Mathf.Clamp01(day / length));
                point.SetHighlighted(day == _hoveredPreviewDay);
                bool targetable = _selectionMode == TimelineAxisSelectionMode.AddDay
                    && _validAddDays.Contains(day)
                    && !_commitInProgress;
                int capturedDay = day;
                point.ConfigureAddDayTarget(
                    targetable,
                    () => HoverAddDay(capturedDay),
                    () => ExitAddDay(capturedDay),
                    () => SelectAddDay(capturedDay));
            }
        }

        private TimelineDayPointView RentDayPoint()
        {
            while (_dayPointPool.Count > 0)
            {
                int last = _dayPointPool.Count - 1;
                TimelineDayPointView pooled = _dayPointPool[last];
                _dayPointPool.RemoveAt(last);
                if (pooled != null)
                {
                    pooled.gameObject.SetActive(true);
                    return pooled;
                }
            }

            TimelineDayPointView prefab = _dayPointPrefab != null
                ? _dayPointPrefab
                : Resources.Load<TimelineDayPointView>("Prefabs/UI/Hud/TimelineDayPointView");
            if (prefab == null)
            {
                Debug.LogError($"{nameof(ActionAxisBar)} 缺少 TimelineDayPointView Prefab。", this);
                return null;
            }

            return Instantiate(prefab, _container);
        }

        private void RecycleDayPoint(TimelineDayPointView point)
        {
            if (point == null)
            {
                return;
            }

            point.KillTweens();
            point.ConfigureAddDayTarget(false, null, null, null);
            point.gameObject.SetActive(false);
            _dayPointPool.Add(point);
        }

        private void ReconcileNodeGroups(bool animate, float animationSpeed = 1f)
        {
            if (_viewState == null || _container == null)
            {
                return;
            }

            float length = Mathf.Max(1f, _viewState.LengthDays);
            IReadOnlyList<TimelineAxisNodeState> nodes = _viewState.Nodes;
            var liveIds = new HashSet<string>();
            var orderByDay = new Dictionary<int, List<string>>();

            foreach (TimelineAxisNodeState node in nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.Id))
                {
                    continue;
                }

                liveIds.Add(node.Id);
                if (!orderByDay.TryGetValue(node.Day, out List<string> order))
                {
                    order = new List<string>();
                    orderByDay[node.Day] = order;
                }

                order.Add(node.Id);
                TimelineDayNodeGroupView group =
                    GetOrCreateDayGroup(
                        node.Day,
                        Mathf.Clamp01(node.Day / length),
                        animate,
                        animationSpeed);
                if (group == null)
                {
                    continue;
                }

                bool moved = false;
                TimelineNodeBubbleView movedBubble = null;
                if (_nodeDays.TryGetValue(node.Id, out int previousDay)
                    && previousDay != node.Day)
                {
                    if (_dayGroups.TryGetValue(previousDay, out TimelineDayNodeGroupView previousGroup))
                    {
                        moved = previousGroup.Extract(
                            node.Id,
                            animate,
                            out movedBubble,
                            animationSpeed);
                    }

                    if (moved)
                    {
                        group.Add(
                            node.Id,
                            movedBubble,
                            preview: false,
                            animate: animate,
                            preserveWorldPosition: true,
                            speed: animationSpeed);
                        _nodeBubbles[node.Id] = movedBubble;
                        _nodeDays[node.Id] = node.Day;
                    }
                }

                bool isNew = !_nodeBubbles.TryGetValue(node.Id, out TimelineNodeBubbleView bubble)
                    || bubble == null;
                if (isNew)
                {
                    bubble = CreateBubble(group.transform);
                    if (bubble == null)
                    {
                        continue;
                    }

                    _nodeBubbles[node.Id] = bubble;
                    _nodeDays[node.Id] = node.Day;
                }

                _nodeActionIds.TryGetValue(node.Id, out string previousActionId);
                ActionDisplayKind kind = node.Kind;
                bubble.Bind(
                    NodeSprite(kind),
                    node.Completed,
                    node.Executing,
                    kind == ActionDisplayKind.Boss,
                    preview: false,
                    negative: kind == ActionDisplayKind.Negative);
                _nodeActionIds[node.Id] = node.ActionId ?? string.Empty;
                if (isNew)
                {
                    group.Add(
                        node.Id,
                        bubble,
                        preview: false,
                        animate,
                        speed: animationSpeed);
                    if (_selectionMode != TimelineAxisSelectionMode.DeleteNode
                        && _selectionMode != TimelineAxisSelectionMode.ExecuteNode)
                    {
                        cfg.TimelineNode runtimeNode = _run != null
                            ? TimelineService.GetNode(_run, node.Id)
                            : null;
                        _onNodeCreated?.Invoke(runtimeNode, bubble.gameObject);
                    }
                }
                else if (!moved
                    && animate
                    && !string.Equals(previousActionId, node.ActionId, StringComparison.Ordinal))
                {
                    bubble.PlayChange(speed: animationSpeed);
                }
            }

            var removedIds = new List<string>();
            foreach (string existingId in _nodeBubbles.Keys)
            {
                if (!liveIds.Contains(existingId))
                {
                    removedIds.Add(existingId);
                }
            }

            foreach (string removedId in removedIds)
            {
                if (_nodeDays.TryGetValue(removedId, out int day)
                    && _dayGroups.TryGetValue(day, out TimelineDayNodeGroupView group))
                {
                    group.Remove(removedId, animate);
                }

                _nodeBubbles.Remove(removedId);
                _nodeDays.Remove(removedId);
                _nodeActionIds.Remove(removedId);
            }

            foreach (KeyValuePair<int, List<string>> pair in orderByDay)
            {
                if (_dayGroups.TryGetValue(pair.Key, out TimelineDayNodeGroupView group))
                {
                    group.SetOrder(pair.Value, animate, animationSpeed);
                }
            }

            BringGroupsToFront();
        }

        private void ApplySelectionMode()
        {
            foreach (TimelineDayNodeGroupView group in _dayGroups.Values)
            {
                if (group == null)
                {
                    continue;
                }

                if (_selectionMode == TimelineAxisSelectionMode.DeleteNode
                    || _selectionMode == TimelineAxisSelectionMode.ExecuteNode)
                {
                    group.ConfigureNodeTargetMode(
                        _targetableNodeIds,
                        SelectNode,
                        destructive: _selectionMode == TimelineAxisSelectionMode.DeleteNode);
                }
                else
                {
                    group.EndNodeTargetMode();
                }
            }
        }

        private void HoverAddDay(int day)
        {
            if (_commitInProgress || !_validAddDays.Contains(day))
            {
                return;
            }

            if (_hoveredPreviewDay == day && _previewBubble != null)
            {
                return;
            }

            ClearPreview(animate: true);
            _hoveredPreviewDay = day;
            SetDayPointHighlight(day, true);

            cfg.GameAction action = _run.Tables.TbAction.GetOrDefault(_previewActionId);
            ActionDisplayKind kind = ActionDisplay.KindOf(_run.Tables, action);
            float length = Mathf.Max(1f, _run.TimelineLengthDays);
            _previewGroup = GetOrCreateDayGroup(day, Mathf.Clamp01(day / length));
            _previewBubble = _previewGroup != null
                ? CreateBubble(_previewGroup.transform)
                : null;
            if (_previewBubble == null)
            {
                _previewGroup = null;
                SetDayPointHighlight(day, false);
                _hoveredPreviewDay = -1;
                return;
            }

            _previewBubble.Bind(
                NodeSprite(kind),
                completed: false,
                executing: false,
                kind == ActionDisplayKind.Boss,
                preview: true);
            _previewBubble.SetRaycastEnabled(false);
            _previewGroup.AddPreview(_previewBubble);
            BringGroupsToFront();
        }

        private void ExitAddDay(int day)
        {
            if (_hoveredPreviewDay != day || _commitInProgress)
            {
                return;
            }

            ClearPreview(animate: true);
        }

        private void SelectAddDay(int day)
        {
            if (_commitInProgress || !_validAddDays.Contains(day) || _confirmDay == null)
            {
                return;
            }

            if (_hoveredPreviewDay != day || _previewBubble == null)
            {
                HoverAddDay(day);
            }

            _commitInProgress = true;
            Action<int> confirm = _confirmDay;
            confirm(day);
            if (_selectionMode == TimelineAxisSelectionMode.AddDay)
            {
                _commitInProgress = false;
            }
        }

        private void SelectNode(string nodeId)
        {
            if (_commitInProgress
                || string.IsNullOrEmpty(nodeId)
                || !_targetableNodeIds.Contains(nodeId)
                || _confirmNode == null)
            {
                return;
            }

            _commitInProgress = true;
            Action<string> confirm = _confirmNode;
            confirm(nodeId);
            if (_selectionMode == TimelineAxisSelectionMode.DeleteNode
                || _selectionMode == TimelineAxisSelectionMode.ExecuteNode)
            {
                _commitInProgress = false;
            }
        }

        private void ClearPreview(bool animate)
        {
            int previousDay = _hoveredPreviewDay;
            _hoveredPreviewDay = -1;
            if (previousDay >= 0)
            {
                SetDayPointHighlight(previousDay, false);
            }

            if (_previewGroup != null)
            {
                _previewGroup.RemovePreview(animate);
            }
            else if (_previewBubble != null)
            {
                if (animate)
                {
                    TimelineNodeBubbleView bubble = _previewBubble;
                    bubble.PlayExit(() =>
                    {
                        if (bubble != null)
                        {
                            Destroy(bubble.gameObject);
                        }
                    });
                }
                else
                {
                    Destroy(_previewBubble.gameObject);
                }
            }

            _previewBubble = null;
            _previewGroup = null;
        }

        private TimelineDayNodeGroupView GetOrCreateDayGroup(
            int day,
            float axisX,
            bool animate = false,
            float animationSpeed = 1f)
        {
            if (_dayGroups.TryGetValue(day, out TimelineDayNodeGroupView existing)
                && existing != null)
            {
                existing.SetAxisPosition(axisX, animate, animationSpeed);
                return existing;
            }

            TimelineDayNodeGroupView prefab = _dayNodeGroupPrefab != null
                ? _dayNodeGroupPrefab
                : Resources.Load<TimelineDayNodeGroupView>(
                    "Prefabs/UI/Hud/TimelineDayNodeGroupView");
            if (prefab == null)
            {
                Debug.LogError(
                    $"{nameof(ActionAxisBar)} 缺少 TimelineDayNodeGroupView Prefab。",
                    this);
                return null;
            }

            TimelineDayNodeGroupView group = Instantiate(prefab, _container);
            group.Initialize(day, axisX);
            _dayGroups[day] = group;
            return group;
        }

        private TimelineNodeBubbleView CreateBubble(Transform parent)
        {
            TimelineNodeBubbleView prefab = _nodeBubblePrefab != null
                ? _nodeBubblePrefab
                : Resources.Load<TimelineNodeBubbleView>("Prefabs/UI/Hud/TimelineNodeBubbleView");
            if (prefab == null)
            {
                Debug.LogError($"{nameof(ActionAxisBar)} 缺少 TimelineNodeBubbleView Prefab。", this);
                return null;
            }

            return Instantiate(prefab, parent);
        }

        private void SetDayPointHighlight(int day, bool highlighted)
        {
            if (_dayPoints.TryGetValue(day, out TimelineDayPointView point))
            {
                point?.SetHighlighted(highlighted);
            }
        }

        private void PositionMarker(float ratio)
        {
            if (_positionMarker == null)
            {
                return;
            }

            float mapped = ratio;
            if (_positionMarker.parent == _container.parent)
            {
                mapped = _container.anchorMin.x + ratio * (_container.anchorMax.x - _container.anchorMin.x);
            }

            float halfWidth =
                (_positionMarker.anchorMax.x - _positionMarker.anchorMin.x) * 0.5f;
            _positionMarker.anchorMin =
                new Vector2(mapped - halfWidth, _positionMarker.anchorMin.y);
            _positionMarker.anchorMax =
                new Vector2(mapped + halfWidth, _positionMarker.anchorMax.y);
            _positionMarker.anchoredPosition =
                new Vector2(0f, _positionMarker.anchoredPosition.y);
        }

        private void UpdateProgressVisual(float day, float length)
        {
            float ratio = Mathf.Clamp01(day / Mathf.Max(1f, length));
            if (_railFill != null)
            {
                _railFill.anchorMax = new Vector2(ratio, _railFill.anchorMax.y);
            }

            PositionMarker(ratio);
            foreach (KeyValuePair<int, TimelineDayPointView> pair in _dayPoints)
            {
                pair.Value?.SetPassed(pair.Key <= day + TimelineMath.Epsilon);
            }

            RefreshCurrentDay();
        }

        private void PulseDayPoint(int day, float speed)
        {
            if (_dayPoints.TryGetValue(day, out TimelineDayPointView point))
            {
                point?.PlayAdvancePulse(speed);
            }
        }

        private void PulseNodesAtDay(int day, float speed)
        {
            foreach (KeyValuePair<string, int> pair in _nodeDays)
            {
                if (pair.Value != day
                    || !_nodeBubbles.TryGetValue(pair.Key, out TimelineNodeBubbleView bubble)
                    || bubble == null)
                {
                    continue;
                }

                bubble.PlayAdvancePulse(speed);
            }
        }

        private void RefreshCurrentDay()
        {
            if (_currentDayText != null)
            {
                float currentDay = Mathf.Max(0f, _displayedDay);
                _currentDayText.text =
                    $"第{currentDay.ToString("0.#", CultureInfo.InvariantCulture)}天";
            }
        }

        private void BringGroupsToFront()
        {
            foreach (TimelineDayNodeGroupView group in _dayGroups.Values)
            {
                group?.transform.SetAsLastSibling();
            }
        }

        private void DestroyDayPoints()
        {
            foreach (TimelineDayPointView point in _dayPoints.Values)
            {
                if (point != null)
                {
                    point.KillTweens();
                    Destroy(point.gameObject);
                }
            }

            foreach (TimelineDayPointView pooled in _dayPointPool)
            {
                if (pooled != null)
                {
                    Destroy(pooled.gameObject);
                }
            }

            _dayPoints.Clear();
            _dayPointPool.Clear();
        }

        private void ClearGroups()
        {
            foreach (TimelineDayNodeGroupView group in _dayGroups.Values)
            {
                if (group != null)
                {
                    Destroy(group.gameObject);
                }
            }

            _dayGroups.Clear();
            _nodeBubbles.Clear();
            _nodeDays.Clear();
            _nodeActionIds.Clear();
            _previewBubble = null;
            _previewGroup = null;
            _hoveredPreviewDay = -1;
        }

        private Sprite NodeSprite(ActionDisplayKind kind)
        {
            Sprite configured = kind switch
            {
                ActionDisplayKind.Boss => _bossNodeSprite,
                ActionDisplayKind.Interest => _interestNodeSprite,
                ActionDisplayKind.Shop => _shopNodeSprite,
                ActionDisplayKind.Event
                    or ActionDisplayKind.Reward
                    or ActionDisplayKind.Negative
                    or ActionDisplayKind.Slot => _eventNodeSprite,
                _ => null,
            };

            if (configured != null)
            {
                return configured;
            }

            string resourceName = NodeSpriteResourceName(kind);
            return string.IsNullOrEmpty(resourceName)
                ? null
                : Resources.Load<Sprite>($"Sprites/UI/{resourceName}");
        }

        internal static string NodeSpriteResourceName(ActionDisplayKind kind)
        {
            return kind switch
            {
                ActionDisplayKind.Boss => "icon_axis_boss",
                ActionDisplayKind.Interest => "icon_axis_interest",
                ActionDisplayKind.Shop => "icon_axis_shop",
                ActionDisplayKind.Event
                    or ActionDisplayKind.Reward
                    or ActionDisplayKind.Negative
                    or ActionDisplayKind.Slot => "icon_axis_event",
                _ => string.Empty,
            };
        }

        private void OnDisable()
        {
            AbortPresentation(convergeToFinalState: true);
        }

        private void AbortPresentation(bool convergeToFinalState = false)
        {
            TimelineAxisViewState finalState = _activePresentation?.Cue?.TargetState;
            if (convergeToFinalState)
            {
                foreach (PresentationWork work in _presentationQueue)
                {
                    finalState = work.Cue?.TargetState ?? finalState;
                }
            }

            _presentationTween?.Kill(complete: false);
            _presentationTween = null;
            _presentationQueue.Clear();
            _activePresentation = null;
            _presentationBusy = false;
            foreach (TimelineNodeBubbleView bubble in _nodeBubbles.Values)
            {
                bubble?.CompletePresentation();
            }

            if (convergeToFinalState && finalState != null)
            {
                BindState(finalState, _run, _onNodeCreated, animate: false);
            }
        }

        private void OnDestroy()
        {
            AbortPresentation();
            ClearPreview(animate: false);
            DestroyDayPoints();
            ClearGroups();
        }
    }
}
