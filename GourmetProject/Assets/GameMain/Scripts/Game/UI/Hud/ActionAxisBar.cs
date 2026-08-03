using System;
using System.Collections.Generic;
using System.Globalization;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using UnityEngine;
using UnityEngine.EventSystems;
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
    /// BattleForm 顶部离散行动轴：整数日期点、同日重叠气泡及主动道具的轴上选点交互。
    /// </summary>
    public sealed class ActionAxisBar : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private RectTransform _container;
        [SerializeField] private RectTransform _positionMarker;
        [SerializeField] private Text _currentDayText;
        [SerializeField] private Image _fillTemplate;
        [SerializeField] private Image _tickTemplate;
        [SerializeField] private Text _dayLabelTemplate;
        [SerializeField] private Image _nodeIconTemplate;
        [SerializeField] private Text _nodeLabelTemplate;
        [SerializeField] private TimelineNodeBubbleView _nodeBubblePrefab;

        [Header("节点图标")]
        [SerializeField] private Sprite _shopNodeSprite;
        [SerializeField] private Sprite _interestNodeSprite;
        [SerializeField] private Sprite _bossNodeSprite;
        [SerializeField] private Sprite _eventNodeSprite;

        [Header("样式")]
        [SerializeField] private Color _fillColor = new Color(0.30f, 0.76f, 0.28f, 0.95f);
        [SerializeField] private Color _tickColor = new Color(1f, 0.66f, 0.08f, 1f);
        [SerializeField] private Color _dayTextColor = new Color(0.22f, 0.12f, 0.07f, 1f);

        private readonly List<GameObject> _axisSpawned = new List<GameObject>();
        private readonly Dictionary<int, Image> _dayDots = new Dictionary<int, Image>();
        private readonly Dictionary<int, TimelineDayNodeGroupView> _dayGroups =
            new Dictionary<int, TimelineDayNodeGroupView>();
        private readonly Dictionary<string, TimelineNodeBubbleView> _nodeBubbles =
            new Dictionary<string, TimelineNodeBubbleView>();
        private readonly Dictionary<string, int> _nodeDays = new Dictionary<string, int>();
        private readonly HashSet<int> _validAddDays = new HashSet<int>();
        private readonly HashSet<string> _targetableNodeIds = new HashSet<string>();

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
        private Font _cachedFont;
        private Sprite _whiteSprite;
        private string _builtTimelineId;
        private string _presentedExecutingNodeId;
        private bool _hasBuiltNodes;
        private bool _commitInProgress;

        public TimelineAxisSelectionMode SelectionMode => _selectionMode;
        public bool HasPreview => _previewBubble != null;
        public int PreviewDay => _hoveredPreviewDay;

        public TimelineDayNodeGroupView GetDayGroup(int day)
        {
            return _dayGroups.TryGetValue(day, out TimelineDayNodeGroupView group) ? group : null;
        }

        public void Build(
            GameRun run,
            Action<cfg.TimelineNode, GameObject> onNodeCreated = null,
            string executingNodeId = null)
        {
            bool timelineChanged = _run != run
                || !string.Equals(_builtTimelineId, run?.CurrentTimelineId, StringComparison.Ordinal);
            _run = run;
            _presentedExecutingNodeId = executingNodeId ?? string.Empty;
            if (onNodeCreated != null)
            {
                _onNodeCreated = onNodeCreated;
            }

            if (timelineChanged)
            {
                ClearPreview(animate: false);
                ClearGroups();
                _builtTimelineId = run?.CurrentTimelineId;
                _hasBuiltNodes = false;
            }

            RebuildAxisChrome();
            ReconcileNodeGroups(animate: _hasBuiltNodes && !timelineChanged);
            _hasBuiltNodes = run != null;
            ApplySelectionMode();
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

        private void RebuildAxisChrome()
        {
            ClearAxisChrome();
            if (_run == null || _container == null)
            {
                return;
            }

            _whiteSprite ??= Resources.Load<Sprite>("Sprites/UI/white");
            float length = Mathf.Max(1f, _run.TimelineLengthDays);
            int wholeDays = Mathf.Max(1, Mathf.FloorToInt(length + TimelineMath.Epsilon));
            float ratio = Mathf.Clamp01(_run.CurrentDay / length);

            BuildRail(ratio);
            BuildDayPoints(wholeDays, length);
            PositionMarker(ratio);
            RefreshCurrentDay();
            BringGroupsToFront();
        }

        private void BuildRail(float ratio)
        {
            Image baseRail = CreateImage("AxisRail", _container, _whiteSprite);
            SetAnchoredRect(
                baseRail.rectTransform,
                new Vector2(0f, 0.27f),
                new Vector2(1f, 0.27f),
                new Vector2(0f, 6f));
            baseRail.color = new Color(0.38f, 0.28f, 0.18f, 0.28f);
            baseRail.raycastTarget = false;
            baseRail.transform.SetAsFirstSibling();

            Image elapsed = CreateImage("AxisElapsed", _container, _whiteSprite);
            elapsed.rectTransform.anchorMin = new Vector2(0f, 0.27f);
            elapsed.rectTransform.anchorMax = new Vector2(ratio, 0.27f);
            elapsed.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            elapsed.rectTransform.sizeDelta = new Vector2(0f, 7f);
            elapsed.rectTransform.anchoredPosition = Vector2.zero;
            elapsed.color = _fillColor;
            elapsed.raycastTarget = false;
            elapsed.transform.SetSiblingIndex(1);
        }

        private void BuildDayPoints(int wholeDays, float length)
        {
            for (int day = 0; day <= wholeDays; day++)
            {
                float x = Mathf.Clamp01(day / length);
                Image hit = CreateImage($"DayHit_{day}", _container, _whiteSprite);
                SetAnchoredRect(
                    hit.rectTransform,
                    new Vector2(x, 0.27f),
                    new Vector2(x, 0.27f),
                    new Vector2(46f, 58f));
                hit.color = new Color(1f, 1f, 1f, 0.001f);
                hit.raycastTarget = _selectionMode == TimelineAxisSelectionMode.AddDay
                    && _validAddDays.Contains(day)
                    && !_commitInProgress;

                Image dot = CreateImage($"DayDot_{day}", hit.rectTransform, _whiteSprite);
                SetAnchoredRect(
                    dot.rectTransform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(14f, 14f));
                dot.color = day <= _run.CurrentDay + TimelineMath.Epsilon ? _fillColor : _tickColor;
                dot.raycastTarget = false;
                Outline outline = dot.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0.25f, 0.14f, 0.07f, 0.50f);
                outline.effectDistance = new Vector2(1f, -1f);
                _dayDots[day] = dot;

                if (day > 0)
                {
                    Text label = CreateText($"DayLabel_{day}", _container);
                    label.text = day.ToString(CultureInfo.InvariantCulture);
                    label.color = _dayTextColor;
                    label.alignment = TextAnchor.UpperCenter;
                    label.resizeTextForBestFit = true;
                    label.resizeTextMinSize = 9;
                    label.resizeTextMaxSize = 17;
                    label.raycastTarget = false;
                    SetAnchoredRect(
                        label.rectTransform,
                        new Vector2(x, 0.27f),
                        new Vector2(x, 0.27f),
                        new Vector2(34f, 24f),
                        new Vector2(0f, -24f));
                }

                if (hit.raycastTarget)
                {
                    int capturedDay = day;
                    TimelineAxisPointerTarget pointer =
                        hit.gameObject.AddComponent<TimelineAxisPointerTarget>();
                    pointer.Bind(
                        () => HoverAddDay(capturedDay),
                        () => ExitAddDay(capturedDay),
                        data =>
                        {
                            if (data.button == PointerEventData.InputButton.Left)
                            {
                                SelectAddDay(capturedDay);
                            }
                        });
                }
            }
        }

        private void ReconcileNodeGroups(bool animate)
        {
            if (_run == null || _container == null)
            {
                return;
            }

            float length = Mathf.Max(1f, _run.TimelineLengthDays);
            List<cfg.TimelineNode> nodes = TimelineService.GetNodes(_run);
            var liveIds = new HashSet<string>();
            var orderByDay = new Dictionary<int, List<string>>();

            foreach (cfg.TimelineNode node in nodes)
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
                    GetOrCreateDayGroup(node.Day, Mathf.Clamp01(node.Day / length));
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

                cfg.GameAction action = TimelineService.NodeAction(_run, node);
                ActionDisplayKind kind = ActionDisplay.KindOf(_run.Tables, action);
                bool executing = _run.IsTimelineNodeExecutionInProgress(node.Id)
                    || string.Equals(
                        node.Id,
                        _presentedExecutingNodeId,
                        StringComparison.Ordinal);
                bubble.Bind(
                    NodeSprite(kind),
                    _run.IsNodeTriggered(node.Id),
                    executing,
                    kind == ActionDisplayKind.Boss,
                    preview: false,
                    negative: kind == ActionDisplayKind.Negative);
                if (isNew)
                {
                    group.Add(node.Id, bubble, preview: false, animate);
                    if (_selectionMode != TimelineAxisSelectionMode.DeleteNode
                        && _selectionMode != TimelineAxisSelectionMode.ExecuteNode)
                    {
                        _onNodeCreated?.Invoke(node, bubble.gameObject);
                    }
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
            }

            foreach (KeyValuePair<int, List<string>> pair in orderByDay)
            {
                if (_dayGroups.TryGetValue(pair.Key, out TimelineDayNodeGroupView group))
                {
                    group.SetOrder(pair.Value, animate);
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
            SetDayDotHighlight(day, true);

            cfg.GameAction action = _run.Tables.TbAction.GetOrDefault(_previewActionId);
            ActionDisplayKind kind = ActionDisplay.KindOf(_run.Tables, action);
            float length = Mathf.Max(1f, _run.TimelineLengthDays);
            _previewGroup = GetOrCreateDayGroup(day, Mathf.Clamp01(day / length));
            _previewBubble = CreateBubble(_previewGroup.transform);
            if (_previewBubble == null)
            {
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
                SetDayDotHighlight(previousDay, false);
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

        private TimelineDayNodeGroupView GetOrCreateDayGroup(int day, float axisX)
        {
            if (_dayGroups.TryGetValue(day, out TimelineDayNodeGroupView existing)
                && existing != null)
            {
                return existing;
            }

            var go = new GameObject(
                $"DayNodeGroup_{day}",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(TimelineDayNodeGroupView));
            go.transform.SetParent(_container, false);
            TimelineDayNodeGroupView group = go.GetComponent<TimelineDayNodeGroupView>();
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

        private void SetDayDotHighlight(int day, bool highlighted)
        {
            if (!_dayDots.TryGetValue(day, out Image dot))
            {
                return;
            }

            dot.color = highlighted
                ? new Color(0.02f, 0.86f, 0.67f, 1f)
                : (day <= _run.CurrentDay + TimelineMath.Epsilon ? _fillColor : _tickColor);
            dot.rectTransform.sizeDelta = highlighted ? new Vector2(21f, 21f) : new Vector2(14f, 14f);
        }

        private Image CreateImage(string objectName, Transform parent, Sprite sprite)
        {
            var go = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            if (parent == _container)
            {
                _axisSpawned.Add(go);
            }

            return image;
        }

        private Text CreateText(string objectName, Transform parent)
        {
            var go = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = ResolveFont();
            if (parent == _container)
            {
                _axisSpawned.Add(go);
            }

            return text;
        }

        private static void SetAnchoredRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 size,
            Vector2? position = null)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position ?? Vector2.zero;
            rect.localScale = Vector3.one;
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

        private void RefreshCurrentDay()
        {
            if (_currentDayText != null)
            {
                float currentDay = Mathf.Max(0f, _run.CurrentDay);
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

        private void ClearAxisChrome()
        {
            foreach (GameObject go in _axisSpawned)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }

            _axisSpawned.Clear();
            _dayDots.Clear();
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
            _previewBubble = null;
            _previewGroup = null;
            _hoveredPreviewDay = -1;
        }

        private Font ResolveFont()
        {
            if (_currentDayText != null && _currentDayText.font != null)
            {
                return _currentDayText.font;
            }

            if (_cachedFont == null)
            {
                _cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_cachedFont == null)
                {
                    _cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
            }

            return _cachedFont;
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

        private void OnDestroy()
        {
            ClearPreview(animate: false);
            ClearAxisChrome();
            ClearGroups();
        }
    }
}
