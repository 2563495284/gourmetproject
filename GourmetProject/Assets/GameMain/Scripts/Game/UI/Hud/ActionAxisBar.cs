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
    }

    /// <summary>
    /// BattleForm 顶部离散行动轴：整数日期点、当前进度、节点气泡及主动道具的轴上选点交互。
    /// </summary>
    public sealed class ActionAxisBar : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private RectTransform _container;
        [SerializeField] private RectTransform _positionMarker;
        [SerializeField] private Text _remainingDaysText;
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
        [SerializeField] private float _nodeIconHeight = 26f;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly Dictionary<int, Image> _dayDots = new Dictionary<int, Image>();
        private readonly Dictionary<string, TimelineNodeBubbleView> _nodeBubbles =
            new Dictionary<string, TimelineNodeBubbleView>();
        private readonly HashSet<int> _validAddDays = new HashSet<int>();
        private readonly HashSet<string> _deletableNodeIds = new HashSet<string>();

        private GameRun _run;
        private Action<cfg.TimelineNode, GameObject> _onNodeCreated;
        private TimelineAxisSelectionMode _selectionMode;
        private string _previewActionId;
        private int _selectedDay = -1;
        private string _selectedNodeId;
        private TimelineNodeBubbleView _previewBubble;
        private Text _confirmLabel;
        private Button _confirmButton;
        private Action<int> _confirmDay;
        private Action<string> _confirmNode;
        private Action _cancelSelection;
        private Font _cachedFont;
        private Sprite _whiteSprite;
        private Sprite _panelSprite;

        public TimelineAxisSelectionMode SelectionMode => _selectionMode;

        public void Build(GameRun run, Action<cfg.TimelineNode, GameObject> onNodeCreated = null)
        {
            _run = run;
            if (onNodeCreated != null)
            {
                _onNodeCreated = onNodeCreated;
            }

            RebuildVisuals();
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

            _run = run;
            _selectionMode = TimelineAxisSelectionMode.AddDay;
            _previewActionId = actionId;
            _confirmDay = onConfirm;
            _confirmNode = null;
            _cancelSelection = onCancel;
            _selectedDay = -1;
            _selectedNodeId = string.Empty;
            _validAddDays.Clear();
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

            RebuildVisuals();
            return true;
        }

        public bool BeginDeleteNodeSelection(
            GameRun run,
            IEnumerable<string> nodeIds,
            Action<string> onConfirm,
            Action onCancel)
        {
            if (run == null || onConfirm == null)
            {
                return false;
            }

            _run = run;
            _selectionMode = TimelineAxisSelectionMode.DeleteNode;
            _confirmNode = onConfirm;
            _confirmDay = null;
            _cancelSelection = onCancel;
            _selectedDay = -1;
            _selectedNodeId = string.Empty;
            _deletableNodeIds.Clear();
            if (nodeIds != null)
            {
                foreach (string id in nodeIds)
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        _deletableNodeIds.Add(id);
                    }
                }
            }

            if (_deletableNodeIds.Count == 0)
            {
                EndSelection(rebuild: false);
                return false;
            }

            RebuildVisuals();
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
            _selectionMode = TimelineAxisSelectionMode.None;
            _previewActionId = string.Empty;
            _selectedDay = -1;
            _selectedNodeId = string.Empty;
            _validAddDays.Clear();
            _deletableNodeIds.Clear();
            _confirmDay = null;
            _confirmNode = null;
            _cancelSelection = null;
            if (rebuild)
            {
                RebuildVisuals();
            }
        }

        private void RebuildVisuals()
        {
            ClearSpawned();
            if (_run == null || _container == null)
            {
                return;
            }

            _whiteSprite = _whiteSprite != null ? _whiteSprite : Resources.Load<Sprite>("Sprites/UI/white");
            _panelSprite = _panelSprite != null ? _panelSprite : Resources.Load<Sprite>("Sprites/UI/ui_panel_card");

            float length = Mathf.Max(1f, _run.TimelineLengthDays);
            int wholeDays = Mathf.Max(1, Mathf.FloorToInt(length + TimelineMath.Epsilon));
            float ratio = Mathf.Clamp01(_run.CurrentDay / length);

            BuildRail(ratio);
            BuildDayPoints(wholeDays, length);
            BuildNodeBubbles(length);
            PositionMarker(ratio);
            RefreshRemainingDays(length);
            if (_selectionMode != TimelineAxisSelectionMode.None)
            {
                BuildSelectionConfirmBar();
            }
        }

        private void BuildRail(float ratio)
        {
            Image baseRail = CreateImage("AxisRail", _container, _whiteSprite);
            SetAnchoredRect(baseRail.rectTransform, new Vector2(0f, 0.27f), new Vector2(1f, 0.27f), new Vector2(0f, 6f));
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
                SetAnchoredRect(hit.rectTransform, new Vector2(x, 0.27f), new Vector2(x, 0.27f), new Vector2(46f, 58f));
                hit.color = new Color(1f, 1f, 1f, 0.001f);
                hit.raycastTarget = _selectionMode == TimelineAxisSelectionMode.AddDay && _validAddDays.Contains(day);

                Image dot = CreateImage($"DayDot_{day}", hit.rectTransform, _whiteSprite);
                SetAnchoredRect(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(14f, 14f));
                dot.color = day <= _run.CurrentDay + TimelineMath.Epsilon
                    ? _fillColor
                    : _tickColor;
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
                    TimelineAxisPointerTarget pointer = hit.gameObject.AddComponent<TimelineAxisPointerTarget>();
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

        private void BuildNodeBubbles(float length)
        {
            var stackByDay = new Dictionary<int, int>();
            foreach (cfg.TimelineNode node in TimelineService.GetNodes(_run))
            {
                if (node == null)
                {
                    continue;
                }

                int stack = stackByDay.TryGetValue(node.Day, out int existing) ? existing : 0;
                stackByDay[node.Day] = stack + 1;
                cfg.GameAction action = TimelineService.NodeAction(_run, node);
                ActionDisplayKind kind = ActionDisplay.KindOf(_run.Tables, action);
                TimelineNodeBubbleView bubble = CreateBubble($"NodeBubble_{node.Id}");
                if (bubble == null)
                {
                    continue;
                }

                bubble.Bind(
                    NodeSprite(kind),
                    _run.IsNodeTriggered(node.Id),
                    kind == ActionDisplayKind.Boss,
                    preview: false,
                    Mathf.Clamp01(node.Day / length),
                    stack);
                _nodeBubbles[node.Id] = bubble;
                if (_selectionMode != TimelineAxisSelectionMode.DeleteNode)
                {
                    _onNodeCreated?.Invoke(node, bubble.gameObject);
                }

                if (_selectionMode == TimelineAxisSelectionMode.DeleteNode)
                {
                    string capturedId = node.Id;
                    bool eligible = _deletableNodeIds.Contains(capturedId);
                    bubble.BindPointer(
                        () => HoverDeleteNode(capturedId),
                        () => ExitDeleteNode(capturedId),
                        () => SelectDeleteNode(capturedId));
                    bubble.SetDeleteState(eligible, selected: false, hovered: false);
                }
            }
        }

        private void HoverAddDay(int day)
        {
            if (_selectedDay >= 0 || !_validAddDays.Contains(day))
            {
                return;
            }

            ShowAddPreview(day);
            SetDayDotHighlight(day, true);
        }

        private void ExitAddDay(int day)
        {
            if (_selectedDay >= 0)
            {
                return;
            }

            DestroyPreviewBubble();
            SetDayDotHighlight(day, false);
        }

        private void SelectAddDay(int day)
        {
            if (!_validAddDays.Contains(day))
            {
                return;
            }

            if (_selectedDay >= 0)
            {
                SetDayDotHighlight(_selectedDay, false);
            }

            _selectedDay = day;
            ShowAddPreview(day);
            SetDayDotHighlight(day, true);
            RefreshSelectionConfirmBar();
        }

        private void ShowAddPreview(int day)
        {
            DestroyPreviewBubble();
            cfg.GameAction action = _run.Tables.TbAction.GetOrDefault(_previewActionId);
            ActionDisplayKind kind = ActionDisplay.KindOf(_run.Tables, action);
            int stack = 0;
            foreach (cfg.TimelineNode node in TimelineService.GetNodes(_run))
            {
                if (node.Day == day)
                {
                    stack++;
                }
            }

            _previewBubble = CreateBubble("NodeBubble_Preview");
            if (_previewBubble == null)
            {
                return;
            }

            _previewBubble.Bind(
                NodeSprite(kind),
                completed: false,
                kind == ActionDisplayKind.Boss,
                preview: true,
                Mathf.Clamp01(day / Mathf.Max(1f, _run.TimelineLengthDays)),
                stack);
        }

        private void HoverDeleteNode(string nodeId)
        {
            if (!_deletableNodeIds.Contains(nodeId) || !string.IsNullOrEmpty(_selectedNodeId))
            {
                return;
            }

            RefreshDeleteVisuals(nodeId);
        }

        private void ExitDeleteNode(string nodeId)
        {
            if (string.IsNullOrEmpty(_selectedNodeId))
            {
                RefreshDeleteVisuals(string.Empty);
            }
        }

        private void SelectDeleteNode(string nodeId)
        {
            if (!_deletableNodeIds.Contains(nodeId))
            {
                return;
            }

            _selectedNodeId = nodeId;
            RefreshDeleteVisuals(nodeId);
            RefreshSelectionConfirmBar();
        }

        private void RefreshDeleteVisuals(string hoveredNodeId)
        {
            foreach (KeyValuePair<string, TimelineNodeBubbleView> pair in _nodeBubbles)
            {
                bool eligible = _deletableNodeIds.Contains(pair.Key);
                pair.Value.SetDeleteState(
                    eligible,
                    pair.Key == _selectedNodeId,
                    string.IsNullOrEmpty(_selectedNodeId) && pair.Key == hoveredNodeId);
            }
        }

        private void BuildSelectionConfirmBar()
        {
            Image panel = CreateImage("AxisSelectionConfirm", _container, _panelSprite);
            panel.type = _panelSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            panel.color = new Color(1f, 0.96f, 0.84f, 0.98f);
            panel.raycastTarget = true;
            RectTransform panelRect = panel.rectTransform;
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.sizeDelta = new Vector2(390f, 42f);
            panelRect.anchoredPosition = new Vector2(0f, -9f);
            panel.transform.SetAsLastSibling();

            _confirmLabel = CreateText("Prompt", panelRect);
            _confirmLabel.alignment = TextAnchor.MiddleLeft;
            _confirmLabel.color = _dayTextColor;
            _confirmLabel.resizeTextForBestFit = true;
            _confirmLabel.resizeTextMinSize = 10;
            _confirmLabel.resizeTextMaxSize = 16;
            _confirmLabel.raycastTarget = false;
            _confirmLabel.rectTransform.anchorMin = Vector2.zero;
            _confirmLabel.rectTransform.anchorMax = Vector2.one;
            _confirmLabel.rectTransform.offsetMin = new Vector2(12f, 5f);
            _confirmLabel.rectTransform.offsetMax = new Vector2(-154f, -5f);

            _confirmButton = CreateButton("Confirm", panelRect, "确定", new Color(0.30f, 0.72f, 0.31f, 1f));
            SetButtonRect(_confirmButton, -80f);
            _confirmButton.onClick.AddListener(ConfirmSelection);

            Button cancel = CreateButton("Cancel", panelRect, "取消", new Color(0.74f, 0.31f, 0.26f, 1f));
            SetButtonRect(cancel, -16f);
            cancel.onClick.AddListener(CancelSelection);
            RefreshSelectionConfirmBar();
        }

        private void RefreshSelectionConfirmBar()
        {
            if (_confirmLabel == null || _confirmButton == null)
            {
                return;
            }

            bool ready;
            if (_selectionMode == TimelineAxisSelectionMode.AddDay)
            {
                ready = _selectedDay >= 0;
                _confirmLabel.text = ready
                    ? $"确定添加到第 {_selectedDay} 天？"
                    : "移动鼠标到未来日期，预览新增节点";
            }
            else
            {
                ready = !string.IsNullOrEmpty(_selectedNodeId);
                _confirmLabel.text = ready
                    ? "确定删除高亮节点？"
                    : "选择一个红色描边的未结算节点";
            }

            _confirmButton.interactable = ready;
        }

        private void ConfirmSelection()
        {
            if (_selectionMode == TimelineAxisSelectionMode.AddDay && _selectedDay >= 0)
            {
                _confirmDay?.Invoke(_selectedDay);
            }
            else if (_selectionMode == TimelineAxisSelectionMode.DeleteNode && !string.IsNullOrEmpty(_selectedNodeId))
            {
                _confirmNode?.Invoke(_selectedNodeId);
            }
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

        private TimelineNodeBubbleView CreateBubble(string objectName)
        {
            TimelineNodeBubbleView prefab = _nodeBubblePrefab != null
                ? _nodeBubblePrefab
                : Resources.Load<TimelineNodeBubbleView>("Prefabs/UI/Hud/TimelineNodeBubbleView");
            if (prefab == null)
            {
                Debug.LogError($"{nameof(ActionAxisBar)} 缺少 TimelineNodeBubbleView Prefab。", this);
                return null;
            }

            TimelineNodeBubbleView view = Instantiate(prefab, _container);
            view.gameObject.name = objectName;
            view.transform.localScale = Vector3.one;
            _spawned.Add(view.gameObject);
            return view;
        }

        private Image CreateImage(string objectName, Transform parent, Sprite sprite)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            if (parent == _container)
            {
                _spawned.Add(go);
            }

            return image;
        }

        private Text CreateText(string objectName, Transform parent)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = ResolveFont();
            if (parent == _container)
            {
                _spawned.Add(go);
            }

            return text;
        }

        private Button CreateButton(string objectName, Transform parent, string label, Color tint)
        {
            Image image = CreateImage(objectName, parent, Resources.Load<Sprite>("Sprites/UI/ui_btn_primary_compact"));
            image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = tint;
            image.raycastTarget = true;
            Button button = image.gameObject.AddComponent<Button>();
            Text text = CreateText("Label", image.transform);
            text.text = label;
            text.color = Color.white;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        private static void SetButtonRect(Button button, float right)
        {
            RectTransform rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(58f, 30f);
            rect.anchoredPosition = new Vector2(right, 0f);
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

            float halfWidth = (_positionMarker.anchorMax.x - _positionMarker.anchorMin.x) * 0.5f;
            _positionMarker.anchorMin = new Vector2(mapped - halfWidth, _positionMarker.anchorMin.y);
            _positionMarker.anchorMax = new Vector2(mapped + halfWidth, _positionMarker.anchorMax.y);
            _positionMarker.anchoredPosition = new Vector2(0f, _positionMarker.anchoredPosition.y);
        }

        private void RefreshRemainingDays(float length)
        {
            if (_remainingDaysText != null)
            {
                float remaining = Mathf.Max(0f, length - _run.CurrentDay);
                _remainingDaysText.text = $"{remaining.ToString("0.#", CultureInfo.InvariantCulture)}天";
            }
        }

        private void DestroyPreviewBubble()
        {
            if (_previewBubble == null)
            {
                return;
            }

            _spawned.Remove(_previewBubble.gameObject);
            Destroy(_previewBubble.gameObject);
            _previewBubble = null;
        }

        private void ClearSpawned()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }

            _spawned.Clear();
            _dayDots.Clear();
            _nodeBubbles.Clear();
            _previewBubble = null;
            _confirmLabel = null;
            _confirmButton = null;
        }

        private Font ResolveFont()
        {
            if (_remainingDaysText != null && _remainingDaysText.font != null)
            {
                return _remainingDaysText.font;
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

        private static string NodeLabel(ActionDisplayKind kind)
        {
            return kind switch
            {
                ActionDisplayKind.Boss => "BOSS",
                ActionDisplayKind.Interest => "利息",
                ActionDisplayKind.Shop => "商店",
                ActionDisplayKind.Reward => "奖励",
                ActionDisplayKind.Event => "事件",
                _ => "节点行动",
            };
        }

        private Sprite NodeSprite(ActionDisplayKind kind)
        {
            return kind switch
            {
                ActionDisplayKind.Boss => _bossNodeSprite != null
                    ? _bossNodeSprite
                    : Resources.Load<Sprite>("Sprites/UI/icon_axis_boss"),
                ActionDisplayKind.Interest => _interestNodeSprite != null
                    ? _interestNodeSprite
                    : Resources.Load<Sprite>("Sprites/UI/icon_axis_interest"),
                ActionDisplayKind.Shop => _shopNodeSprite != null
                    ? _shopNodeSprite
                    : Resources.Load<Sprite>("Sprites/UI/icon_axis_shop"),
                ActionDisplayKind.Event or ActionDisplayKind.Reward => _eventNodeSprite != null
                    ? _eventNodeSprite
                    : Resources.Load<Sprite>("Sprites/UI/icon_axis_event"),
                _ => null,
            };
        }
    }
}
