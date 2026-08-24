using System;
using System.Collections.Generic;
using System.Globalization;
using DG.Tweening;
using GourmetProject.Game.Meta;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    /// 顶部时间轴的纯 UI 门面。只消费视图状态、演出计划与选点请求，不持有 GameRun。
    /// </summary>
    public sealed class TimelineAxisView : MonoBehaviour
    {
        [Header("Theme")]
        [SerializeField] private TimelineAxisTheme _theme;

        [Header("Fixed layers")]
        [SerializeField] private RectTransform _axisContent;
        [SerializeField] private Image _track;
        [SerializeField] private Image _progress;
        [SerializeField] private RectTransform _progressRect;
        [SerializeField] private RectTransform _dayLayer;
        [SerializeField] private RectTransform _nodeLayer;
        [SerializeField] private RectTransform _cursorLayer;
        [SerializeField] private RectTransform _cursor;
        [SerializeField] private Image _cursorImage;
        [SerializeField] private Image _dayBadge;
        [SerializeField] private TMP_Text _currentDayText;

        [Header("Authored prefabs")]
        [SerializeField] private TimelineDayPointView _dayPointPrefab;
        [SerializeField] private TimelineDayNodeGroupView _dayNodeGroupPrefab;
        [SerializeField] private TimelineNodeBubbleView _nodeBubblePrefab;

        [Header("Pool warmup")]
        [SerializeField, Min(0)] private int _warmDayPoints = 10;
        [SerializeField, Min(0)] private int _warmGroups = 6;
        [SerializeField, Min(0)] private int _warmBubbles = 10;

        private sealed class CueWork
        {
            public TimelinePresentationCue Cue;
            public float Speed;
            public Action Complete;
        }

        private readonly Dictionary<int, TimelineDayPointView> _dayPoints = new();
        private readonly Dictionary<int, TimelineDayNodeGroupView> _dayGroups = new();
        private readonly Dictionary<string, TimelineNodeBubbleView> _nodeBubbles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _nodeDays = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _nodeActionIds = new(StringComparer.Ordinal);
        private readonly Stack<TimelineDayPointView> _dayPointPool = new();
        private readonly Stack<TimelineDayNodeGroupView> _groupPool = new();
        private readonly Stack<TimelineNodeBubbleView> _bubblePool = new();
        private readonly Queue<CueWork> _cueQueue = new();
        private readonly List<int> _spareDays = new();
        private readonly TimelineAxisSelectionController _selection = new();

        private TimelineAxisPresentationPlayer _player;
        private TimelineAxisViewState _state = new();
        private Action<TimelineAxisNodeState, GameObject> _decorateNode;
        private TimelineNodeBubbleView _previewBubble;
        private TimelineDayNodeGroupView _previewGroup;
        private int _previewDay = -1;
        private float _displayedDay;
        private CueWork _activeCue;
        private Tween _presentationTween;
        private bool _cueBusy;
        private bool _completing;
        private bool _warmed;

        public TimelineAxisSelectionMode SelectionMode => _selection.Mode;
        public bool HasPreview => _previewBubble != null;
        public int PreviewDay => _previewDay;
        public float DisplayedDay => _displayedDay;
        public bool IsPresenting => _cueBusy || _cueQueue.Count > 0;
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

                return false;
            }
        }

        private void Awake()
        {
            _player = new TimelineAxisPresentationPlayer(
                EnqueueCue,
                state => Render(state, animate: false),
                CompleteVisuals,
                CancelVisuals);
            ApplyTheme();
            WarmPools();
        }

        public void SetNodeDecorator(Action<TimelineAxisNodeState, GameObject> decorator)
        {
            _decorateNode = decorator;
        }

        public void Render(
            TimelineAxisViewState state,
            bool animate = false,
            float animationSpeed = 1f)
        {
            if (state == null)
            {
                return;
            }

            EnsureReady();
            _state = Normalize(state);
            _displayedDay = _state.CurrentDay;
            SyncDayPoints();
            ReconcileNodeGroups(animate, animationSpeed);
            UpdateProgress(_displayedDay, _state.LengthDays);
            ApplySelectionVisuals();
        }

        public void Play(
            TimelineAxisPresentationPlan plan,
            Action onComplete = null,
            float speed = 1f)
        {
            EnsureReady();
            _player.Play(plan, onComplete, Mathf.Max(0.05f, speed));
        }

        public void CompletePresentation()
        {
            EnsureReady();
            _player.Complete();
        }

        public void CancelPresentation()
        {
            EnsureReady();
            _player.Cancel();
        }

        public bool BeginSelection(TimelineAxisSelectionRequest request)
        {
            ClearPreview(animate: false);
            if (!_selection.Begin(request))
            {
                ApplySelectionVisuals();
                return false;
            }

            SyncDayPoints();
            ApplySelectionVisuals();
            return true;
        }

        public bool PromotePreview(string nodeId)
        {
            if (_selection.Mode != TimelineAxisSelectionMode.AddDay
                || string.IsNullOrEmpty(nodeId)
                || _previewBubble == null
                || _previewGroup == null
                || _previewDay < 0
                || !_previewGroup.PromotePreview(nodeId, out TimelineNodeBubbleView bubble))
            {
                return false;
            }

            TimelineAxisNodeState preview = _selection.PreviewNode?.Clone();
            if (preview == null)
            {
                return false;
            }

            preview.Id = nodeId;
            preview.Day = _previewDay;
            BindBubble(bubble, preview, previewState: false);
            _nodeBubbles[nodeId] = bubble;
            _nodeDays[nodeId] = preview.Day;
            _nodeActionIds[nodeId] = preview.ActionId ?? string.Empty;
            _decorateNode?.Invoke(preview.Clone(), bubble.gameObject);

            SetDayPointHighlight(_previewDay, false);
            _previewDay = -1;
            _previewBubble = null;
            _previewGroup = null;
            EndSelection(rebuild: false);
            return true;
        }

        public void EndSelection(bool rebuild = true)
        {
            ClearPreview(rebuild);
            _selection.End();
            foreach (TimelineDayNodeGroupView group in _dayGroups.Values)
            {
                group?.EndNodeTargetMode();
            }

            if (rebuild)
            {
                SyncDayPoints();
                ReconcileNodeGroups(animate: true);
            }
        }

        public void CancelSelection()
        {
            ClearPreview(animate: true);
            _selection.Cancel();
            foreach (TimelineDayNodeGroupView group in _dayGroups.Values)
            {
                group?.EndNodeTargetMode();
            }

            SyncDayPoints();
        }

        public bool TryGetNodeBubble(string nodeId, out TimelineNodeBubbleView bubble)
        {
            return _nodeBubbles.TryGetValue(nodeId ?? string.Empty, out bubble) && bubble != null;
        }

        public TimelineDayNodeGroupView GetDayGroup(int day)
        {
            return _dayGroups.TryGetValue(day, out TimelineDayNodeGroupView group) ? group : null;
        }

        private void ApplyTheme()
        {
            if (_theme == null)
            {
                return;
            }

            SetImage(_track, _theme.Track, Image.Type.Sliced, Color.white);
            SetImage(_progress, _theme.Progress, Image.Type.Sliced, Color.white);

            SetImage(_cursorImage, _theme.Cursor, Image.Type.Simple, Color.white);
            SetImage(_dayBadge, _theme.DayBadge, Image.Type.Sliced, Color.white);
            if (_currentDayText != null)
            {
                _currentDayText.color = _theme.Palette.Ink;
            }
        }

        private static void SetImage(Image image, Sprite sprite, Image.Type type, Color color)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = sprite;
            image.type = type;
            image.color = color;
            image.raycastTarget = false;
        }

        private void EnsureReady()
        {
            if (_player == null)
            {
                Awake();
            }
        }

        private void WarmPools()
        {
            if (_warmed)
            {
                return;
            }

            _warmed = true;
            for (int i = 0; i < _warmDayPoints; i++)
            {
                TimelineDayPointView point = CreateDayPoint();
                if (point != null)
                {
                    ReturnDayPoint(point);
                }
            }

            for (int i = 0; i < _warmGroups; i++)
            {
                TimelineDayNodeGroupView group = CreateGroup();
                if (group != null)
                {
                    ReturnGroup(group);
                }
            }

            for (int i = 0; i < _warmBubbles; i++)
            {
                TimelineNodeBubbleView bubble = CreateBubble();
                if (bubble != null)
                {
                    ReturnBubble(bubble);
                }
            }
        }

        private TimelineAxisViewState Normalize(TimelineAxisViewState source)
        {
            TimelineAxisViewState clone = source.Clone();
            clone.LengthDays = Mathf.Max(1f, clone.LengthDays);
            clone.CurrentDay = Mathf.Clamp(clone.CurrentDay, 0f, clone.LengthDays);
            clone.Nodes.RemoveAll(node => node == null || string.IsNullOrEmpty(node.Id));
            return clone;
        }

        private void SyncDayPoints()
        {
            if (_dayLayer == null)
            {
                return;
            }

            int lastDay = Mathf.Max(1, Mathf.FloorToInt(_state.LengthDays + TimelineMath.Epsilon));
            _spareDays.Clear();
            foreach (int day in _dayPoints.Keys)
            {
                if (day > lastDay)
                {
                    _spareDays.Add(day);
                }
            }

            foreach (int day in _spareDays)
            {
                ReturnDayPoint(_dayPoints[day]);
                _dayPoints.Remove(day);
            }

            for (int day = 0; day <= lastDay; day++)
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

                point.Bind(day, day / _state.LengthDays);
                point.SetHighlighted(day == _previewDay);
                bool targetable = _selection.CanTargetDay(day);
                int captured = day;
                point.ConfigureAddDayTarget(
                    targetable,
                    () => HoverDay(captured),
                    () => ExitDay(captured),
                    () => SelectDay(captured));
            }
        }

        private void ReconcileNodeGroups(bool animate, float speed = 1f)
        {
            if (_nodeLayer == null)
            {
                return;
            }

            var liveIds = new HashSet<string>(StringComparer.Ordinal);
            var orderByDay = new Dictionary<int, List<string>>();
            foreach (TimelineAxisNodeState node in _state.Nodes)
            {
                liveIds.Add(node.Id);
                if (!orderByDay.TryGetValue(node.Day, out List<string> order))
                {
                    order = new List<string>();
                    orderByDay[node.Day] = order;
                }

                order.Add(node.Id);
                TimelineDayNodeGroupView group = GetOrCreateGroup(
                    node.Day,
                    node.Day / _state.LengthDays,
                    animate,
                    speed);
                if (group == null)
                {
                    continue;
                }

                bool moved = false;
                if (_nodeDays.TryGetValue(node.Id, out int oldDay) && oldDay != node.Day
                    && _dayGroups.TryGetValue(oldDay, out TimelineDayNodeGroupView oldGroup)
                    && oldGroup.Extract(node.Id, animate, out TimelineNodeBubbleView movedBubble, speed))
                {
                    group.Add(node.Id, movedBubble, false, animate, true, speed);
                    _nodeBubbles[node.Id] = movedBubble;
                    _nodeDays[node.Id] = node.Day;
                    moved = true;
                }

                bool isNew = !_nodeBubbles.TryGetValue(node.Id, out TimelineNodeBubbleView bubble)
                    || bubble == null;
                if (isNew)
                {
                    bubble = RentBubble(group.transform);
                    if (bubble == null)
                    {
                        continue;
                    }

                    _nodeBubbles[node.Id] = bubble;
                    _nodeDays[node.Id] = node.Day;
                }

                _nodeActionIds.TryGetValue(node.Id, out string previousActionId);
                BindBubble(bubble, node, previewState: false);
                _nodeActionIds[node.Id] = node.ActionId ?? string.Empty;
                if (isNew)
                {
                    group.Add(node.Id, bubble, false, animate, speed: speed);
                    _decorateNode?.Invoke(node.Clone(), bubble.gameObject);
                }
                else if (!moved && animate
                    && !string.Equals(previousActionId, node.ActionId, StringComparison.Ordinal))
                {
                    bubble.PlayChange(speed: speed);
                }
            }

            var removed = new List<string>();
            foreach (string id in _nodeBubbles.Keys)
            {
                if (!liveIds.Contains(id))
                {
                    removed.Add(id);
                }
            }

            foreach (string id in removed)
            {
                if (_nodeDays.TryGetValue(id, out int day)
                    && _dayGroups.TryGetValue(day, out TimelineDayNodeGroupView group))
                {
                    group.Remove(id, animate);
                }

                _nodeBubbles.Remove(id);
                _nodeDays.Remove(id);
                _nodeActionIds.Remove(id);
            }

            foreach (KeyValuePair<int, List<string>> pair in orderByDay)
            {
                if (_dayGroups.TryGetValue(pair.Key, out TimelineDayNodeGroupView group))
                {
                    group.SetOrder(pair.Value, animate, speed);
                }
            }

            RecycleEmptyGroups(animate, speed);
        }

        private void BindBubble(TimelineNodeBubbleView bubble, TimelineAxisNodeState node, bool previewState)
        {
            if (bubble == null || node == null)
            {
                return;
            }

            Sprite icon = _theme?.ResolveIcon(node.IconKey, node.Kind);
            bubble.Bind(
                icon,
                node.Completed,
                node.Executing,
                node.Kind == Game.Meta.ActionDisplayKind.Boss,
                previewState,
                node.Kind == Game.Meta.ActionDisplayKind.Negative);
        }

        private void ApplySelectionVisuals()
        {
            foreach (TimelineDayNodeGroupView group in _dayGroups.Values)
            {
                if (group == null)
                {
                    continue;
                }

                if (_selection.Mode == TimelineAxisSelectionMode.DeleteNode
                    || _selection.Mode == TimelineAxisSelectionMode.ExecuteNode)
                {
                    group.ConfigureNodeTargetMode(
                        _selection.ValidNodeIds,
                        SelectNode,
                        _selection.Mode == TimelineAxisSelectionMode.DeleteNode);
                }
                else
                {
                    group.EndNodeTargetMode();
                }
            }
        }

        private void HoverDay(int day)
        {
            if (!_selection.CanTargetDay(day))
            {
                return;
            }

            if (_previewDay == day && _previewBubble != null)
            {
                return;
            }

            ClearPreview(animate: true);
            _previewDay = day;
            SetDayPointHighlight(day, true);
            _previewGroup = GetOrCreateGroup(day, day / _state.LengthDays, animate: false, speed: 1f);
            _previewBubble = _previewGroup != null ? RentBubble(_previewGroup.transform) : null;
            if (_previewBubble == null)
            {
                _previewGroup = null;
                _previewDay = -1;
                SetDayPointHighlight(day, false);
                return;
            }

            TimelineAxisNodeState preview = _selection.PreviewNode.Clone();
            preview.Day = day;
            BindBubble(_previewBubble, preview, previewState: true);
            _previewBubble.SetRaycastEnabled(false);
            _previewGroup.AddPreview(_previewBubble);
        }

        private void ExitDay(int day)
        {
            if (_previewDay == day && !_selection.IsCommitting)
            {
                ClearPreview(animate: true);
            }
        }

        private void SelectDay(int day)
        {
            if (_previewDay != day || _previewBubble == null)
            {
                HoverDay(day);
            }

            _selection.TryConfirmDay(day);
        }

        private void SelectNode(string nodeId)
        {
            _selection.TryConfirmNode(nodeId);
        }

        private void ClearPreview(bool animate)
        {
            int oldDay = _previewDay;
            _previewDay = -1;
            if (oldDay >= 0)
            {
                SetDayPointHighlight(oldDay, false);
            }

            if (_previewGroup != null)
            {
                _previewGroup.RemovePreview(animate);
            }
            else if (_previewBubble != null)
            {
                TimelineNodeBubbleView bubble = _previewBubble;
                if (animate)
                {
                    bubble.PlayExit(() => ReturnBubble(bubble));
                }
                else
                {
                    ReturnBubble(bubble);
                }
            }

            _previewBubble = null;
            _previewGroup = null;
            RecycleEmptyGroups(animate);
        }

        private void EnqueueCue(TimelinePresentationCue cue, Action complete, float speed)
        {
            if (cue == null)
            {
                complete?.Invoke();
                return;
            }

            _cueQueue.Enqueue(new CueWork
            {
                Cue = cue,
                Speed = Mathf.Max(0.05f, speed),
                Complete = complete,
            });
            if (!_completing)
            {
                PlayNextCue();
            }
        }

        private void PlayNextCue()
        {
            if (_cueBusy || _cueQueue.Count == 0)
            {
                return;
            }

            _cueBusy = true;
            _activeCue = _cueQueue.Dequeue();
            TimelinePresentationCue cue = _activeCue.Cue;
            switch (cue.Kind)
            {
                case TimelinePresentationCueKind.Advance:
                    PlayAdvance(cue, _activeCue.Speed);
                    break;
                case TimelinePresentationCueKind.TriggerStart:
                    PlayNodeCue(cue, true);
                    break;
                case TimelinePresentationCueKind.TriggerComplete:
                    PlayNodeCue(cue, false);
                    break;
                case TimelinePresentationCueKind.Remove:
                case TimelinePresentationCueKind.Skip:
                    PlayRemoval(cue);
                    break;
                case TimelinePresentationCueKind.Replace:
                    if (_nodeBubbles.TryGetValue(cue.NodeId, out TimelineNodeBubbleView bubble)
                        && bubble != null)
                    {
                        bubble.PlayChange(
                            FinishCue,
                            _activeCue.Speed,
                            () => ApplyTarget(cue, false));
                    }
                    else
                    {
                        ApplyTarget(cue, false);
                        FinishCue();
                    }

                    break;
                case TimelinePresentationCueKind.Resize:
                    PlayResize(cue, _activeCue.Speed);
                    break;
                case TimelinePresentationCueKind.Add:
                case TimelinePresentationCueKind.Move:
                    ApplyTarget(cue, true, _activeCue.Speed);
                    _presentationTween = DOVirtual.DelayedCall(
                            0.38f / _activeCue.Speed,
                            FinishCue,
                            true)
                        .SetTarget(this);
                    break;
                default:
                    ApplyTarget(cue, true, _activeCue.Speed);
                    FinishCue();
                    break;
            }
        }

        private void PlayAdvance(TimelinePresentationCue cue, float speed)
        {
            float length = Mathf.Max(1f, cue.TargetState?.LengthDays ?? _state.LengthDays);
            float start = Mathf.Clamp(cue.FromDay, 0f, length);
            float target = Mathf.Clamp(cue.ToDay, 0f, length);
            if (Mathf.Abs(_displayedDay - start) > TimelineMath.Epsilon)
            {
                start = _displayedDay;
            }

            float distance = Mathf.Abs(target - start);
            TimelineAxisMotion motion = _theme?.Motion ?? new TimelineAxisMotion();
            float duration = Mathf.Clamp(
                motion.AdvanceBaseDuration + motion.AdvancePerDayDuration * distance,
                motion.AdvanceBaseDuration,
                motion.AdvanceMaximumDuration) / speed;
            int lastWholeDay = Mathf.FloorToInt(start + TimelineMath.Epsilon);
            _presentationTween = DOTween.To(
                    () => start,
                    value =>
                    {
                        _displayedDay = value;
                        UpdateProgress(value, length);
                        int wholeDay = Mathf.FloorToInt(value + TimelineMath.Epsilon);
                        for (int day = lastWholeDay + 1; day <= wholeDay; day++)
                        {
                            PulseDay(day, speed);
                        }

                        lastWholeDay = wholeDay;
                    },
                    target,
                    duration)
                .SetEase(Ease.InOutCubic)
                .SetUpdate(true)
                .SetTarget(this)
                .OnComplete(() =>
                {
                    _displayedDay = target;
                    UpdateProgress(target, length);
                    FinishCue();
                });
        }

        private void PlayNodeCue(TimelinePresentationCue cue, bool start)
        {
            if (start)
            {
                ApplyTarget(cue, false);
            }

            if (!_nodeBubbles.TryGetValue(cue.NodeId ?? string.Empty, out TimelineNodeBubbleView bubble)
                || bubble == null)
            {
                if (!start)
                {
                    ApplyTarget(cue, false);
                }

                FinishCue();
                return;
            }

            void Finished()
            {
                if (!start)
                {
                    ApplyTarget(cue, false);
                }

                FinishCue();
            }

            if (start)
            {
                bubble.PlayTriggerStart(Finished, _activeCue.Speed);
            }
            else
            {
                bubble.PlayTriggerComplete(Finished, _activeCue.Speed);
            }
        }

        private void PlayRemoval(TimelinePresentationCue cue)
        {
            if (!_nodeBubbles.TryGetValue(cue.NodeId ?? string.Empty, out TimelineNodeBubbleView bubble)
                || bubble == null)
            {
                ApplyTarget(cue, false);
                FinishCue();
                return;
            }

            void Finished()
            {
                ApplyTarget(cue, false);
                FinishCue();
            }

            if (cue.Kind == TimelinePresentationCueKind.Skip)
            {
                bubble.PlaySkip(Finished, _activeCue.Speed);
            }
            else
            {
                bubble.PlayRemove(Finished, _activeCue.Speed);
            }
        }

        private void PlayResize(TimelinePresentationCue cue, float speed)
        {
            if (cue.TargetState == null)
            {
                FinishCue();
                return;
            }

            float from = Mathf.Max(1f, _state.LengthDays);
            TimelineAxisViewState target = Normalize(cue.TargetState);
            Render(target, animate: false);
            float to = target.LengthDays;
            float shown = from;
            void ApplyLength(float length)
            {
                foreach (KeyValuePair<int, TimelineDayPointView> pair in _dayPoints)
                {
                    pair.Value?.SetAxisPosition(pair.Key / length);
                }

                foreach (KeyValuePair<int, TimelineDayNodeGroupView> pair in _dayGroups)
                {
                    pair.Value?.SetAxisPosition(pair.Key / length, false);
                }

                UpdateProgress(target.CurrentDay, length);
            }

            ApplyLength(from);
            _presentationTween = DOTween.To(
                    () => shown,
                    value =>
                    {
                        shown = value;
                        ApplyLength(value);
                    },
                    to,
                    (_theme?.Motion.ResizeDuration ?? 0.38f) / speed)
                .SetEase(Ease.InOutCubic)
                .SetUpdate(true)
                .SetTarget(this)
                .OnComplete(() =>
                {
                    Render(target, false);
                    FinishCue();
                });
        }

        private void ApplyTarget(TimelinePresentationCue cue, bool animate, float speed = 1f)
        {
            if (cue?.TargetState != null)
            {
                Render(cue.TargetState, animate, speed);
            }
        }

        private void FinishCue()
        {
            if (!_cueBusy)
            {
                return;
            }

            _presentationTween = null;
            CueWork finished = _activeCue;
            _activeCue = null;
            _cueBusy = false;
            finished?.Complete?.Invoke();
            PlayNextCue();
        }

        private void CompleteVisuals()
        {
            if (_completing)
            {
                return;
            }

            _completing = true;
            _presentationTween?.Kill(false);
            _presentationTween = null;
            foreach (TimelineNodeBubbleView bubble in _nodeBubbles.Values)
            {
                bubble?.CompletePresentation();
            }

            CueWork active = _activeCue;
            _activeCue = null;
            _cueBusy = false;
            try
            {
                CompleteCueWork(active);
                while (_cueQueue.Count > 0)
                {
                    CompleteCueWork(_cueQueue.Dequeue());
                }
            }
            finally
            {
                _completing = false;
            }
        }

        private void CompleteCueWork(CueWork work)
        {
            if (work == null)
            {
                return;
            }

            if (work.Cue?.TargetState != null)
            {
                Render(work.Cue.TargetState, false);
            }

            work.Complete?.Invoke();
        }

        private void CancelVisuals()
        {
            _presentationTween?.Kill(false);
            _presentationTween = null;
            _cueQueue.Clear();
            _activeCue = null;
            _cueBusy = false;
            foreach (TimelineNodeBubbleView bubble in _nodeBubbles.Values)
            {
                bubble?.CompletePresentation();
            }
        }

        private void UpdateProgress(float day, float length)
        {
            float ratio = Mathf.Clamp01(day / Mathf.Max(1f, length));
            if (_progressRect != null)
            {
                _progressRect.anchorMin = new Vector2(0f, _progressRect.anchorMin.y);
                _progressRect.anchorMax = new Vector2(ratio, _progressRect.anchorMax.y);
                _progressRect.offsetMin = new Vector2(0f, _progressRect.offsetMin.y);
                _progressRect.offsetMax = new Vector2(0f, _progressRect.offsetMax.y);
            }

            if (_cursor != null)
            {
                _cursor.anchorMin = new Vector2(ratio, _cursor.anchorMin.y);
                _cursor.anchorMax = new Vector2(ratio, _cursor.anchorMax.y);
                _cursor.anchoredPosition = new Vector2(0f, _cursor.anchoredPosition.y);
            }

            foreach (KeyValuePair<int, TimelineDayPointView> pair in _dayPoints)
            {
                pair.Value?.SetPassed(pair.Key <= day + TimelineMath.Epsilon);
            }

            if (_currentDayText != null)
            {
                _currentDayText.text =
                    $"第{Mathf.Max(0f, day).ToString("0.0", CultureInfo.InvariantCulture)}天";
            }
        }

        private void PulseDay(int day, float speed)
        {
            if (_dayPoints.TryGetValue(day, out TimelineDayPointView point))
            {
                point?.PlayAdvancePulse(speed);
            }

            foreach (KeyValuePair<string, int> pair in _nodeDays)
            {
                if (pair.Value == day
                    && _nodeBubbles.TryGetValue(pair.Key, out TimelineNodeBubbleView bubble))
                {
                    bubble?.PlayAdvancePulse(speed);
                }
            }
        }

        private void SetDayPointHighlight(int day, bool highlighted)
        {
            if (_dayPoints.TryGetValue(day, out TimelineDayPointView point))
            {
                point?.SetHighlighted(highlighted);
            }
        }

        private TimelineDayPointView RentDayPoint()
        {
            TimelineDayPointView point = _dayPointPool.Count > 0 ? _dayPointPool.Pop() : CreateDayPoint();
            if (point != null)
            {
                point.transform.SetParent(_dayLayer, false);
                point.gameObject.SetActive(true);
                point.Initialize(_theme);
            }

            return point;
        }

        private TimelineDayPointView CreateDayPoint()
        {
            if (_dayPointPrefab == null || _dayLayer == null)
            {
                Debug.LogError($"{nameof(TimelineAxisView)} 缺少日期点 Prefab 或 DayLayer。", this);
                return null;
            }

            return Instantiate(_dayPointPrefab, _dayLayer);
        }

        private void ReturnDayPoint(TimelineDayPointView point)
        {
            if (point == null)
            {
                return;
            }

            point.ResetForPool();
            point.gameObject.SetActive(false);
            _dayPointPool.Push(point);
        }

        private TimelineDayNodeGroupView GetOrCreateGroup(
            int day,
            float axisX,
            bool animate,
            float speed)
        {
            if (_dayGroups.TryGetValue(day, out TimelineDayNodeGroupView existing)
                && existing != null)
            {
                existing.SetAxisPosition(axisX, animate, speed);
                return existing;
            }

            TimelineDayNodeGroupView group = _groupPool.Count > 0 ? _groupPool.Pop() : CreateGroup();
            if (group == null)
            {
                return null;
            }

            group.transform.SetParent(_nodeLayer, false);
            group.gameObject.SetActive(true);
            group.Initialize(day, axisX, ReturnBubble);
            _dayGroups[day] = group;
            return group;
        }

        private TimelineDayNodeGroupView CreateGroup()
        {
            if (_dayNodeGroupPrefab == null || _nodeLayer == null)
            {
                Debug.LogError($"{nameof(TimelineAxisView)} 缺少节点组 Prefab 或 NodeLayer。", this);
                return null;
            }

            return Instantiate(_dayNodeGroupPrefab, _nodeLayer);
        }

        private void ReturnGroup(TimelineDayNodeGroupView group)
        {
            if (group == null)
            {
                return;
            }

            group.ResetForPool();
            group.gameObject.SetActive(false);
            _groupPool.Push(group);
        }

        private void RecycleEmptyGroups(bool animate, float speed = 1f)
        {
            void Collect()
            {
                var empty = new List<int>();
                foreach (KeyValuePair<int, TimelineDayNodeGroupView> pair in _dayGroups)
                {
                    if (pair.Value != null
                        && pair.Value.NodeCount == 0
                        && pair.Value != _previewGroup)
                    {
                        empty.Add(pair.Key);
                    }
                }

                foreach (int day in empty)
                {
                    TimelineDayNodeGroupView group = _dayGroups[day];
                    _dayGroups.Remove(day);
                    ReturnGroup(group);
                }
            }

            if (animate && gameObject.activeInHierarchy)
            {
                DOVirtual.DelayedCall(0.42f / Mathf.Max(0.05f, speed), Collect, true)
                    .SetTarget(this);
            }
            else
            {
                Collect();
            }
        }

        private TimelineNodeBubbleView RentBubble(Transform parent)
        {
            TimelineNodeBubbleView bubble = _bubblePool.Count > 0 ? _bubblePool.Pop() : CreateBubble();
            if (bubble != null)
            {
                bubble.transform.SetParent(parent, false);
                bubble.gameObject.SetActive(true);
                bubble.Initialize(_theme);
            }

            return bubble;
        }

        private TimelineNodeBubbleView CreateBubble()
        {
            if (_nodeBubblePrefab == null || _nodeLayer == null)
            {
                Debug.LogError($"{nameof(TimelineAxisView)} 缺少节点气泡 Prefab 或 NodeLayer。", this);
                return null;
            }

            return Instantiate(_nodeBubblePrefab, _nodeLayer);
        }

        private void ReturnBubble(TimelineNodeBubbleView bubble)
        {
            if (bubble == null || _bubblePool.Contains(bubble))
            {
                return;
            }

            bubble.ResetForPool();
            bubble.transform.SetParent(_nodeLayer, false);
            bubble.gameObject.SetActive(false);
            _bubblePool.Push(bubble);
        }

        private void ClearRuntimeViews()
        {
            ClearPreview(false);
            foreach (TimelineDayNodeGroupView group in _dayGroups.Values)
            {
                ReturnGroup(group);
            }

            _dayGroups.Clear();
            _nodeBubbles.Clear();
            _nodeDays.Clear();
            _nodeActionIds.Clear();
            foreach (TimelineDayPointView point in _dayPoints.Values)
            {
                ReturnDayPoint(point);
            }

            _dayPoints.Clear();
        }

        private void OnDisable()
        {
            CompleteVisuals();
        }

        private void OnDestroy()
        {
            CancelVisuals();
            ClearRuntimeViews();
        }
    }
}
