using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Development
{
    /// <summary>只使用纯 UI 快照的时间轴表现实验室，不读取或修改当前 GameRun。</summary>
    public sealed class TimelinePresentationLabForm : UGuiForm
    {
        private static readonly Color Paper = new Color(0.96f, 0.88f, 0.69f, 1f);
        private static readonly Color Ink = new Color(0.25f, 0.12f, 0.045f, 1f);
        private static readonly Color ButtonFill = new Color(0.86f, 0.64f, 0.27f, 1f);

        private TimelineAxisView _axis;
        private TimelineAxisViewState _state;
        private TMP_Text _status;
        private TMP_Text _speedLabel;
        private float _speed = 1f;
        private int _createdNodeSerial;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            BuildVisualTree();
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            ResetLab();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            _axis?.CancelPresentation();
            base.OnClose(isShutdown, userData);
        }

        private void BuildVisualTree()
        {
            Image background = GetComponent<Image>();
            if (background == null)
            {
                background = gameObject.AddComponent<Image>();
            }

            background.color = new Color(0.13f, 0.075f, 0.035f, 0.97f);
            background.raycastTarget = true;

            RectTransform paper = CreateRect("Paper", CachedTransform, new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.95f));
            Image paperImage = paper.gameObject.AddComponent<Image>();
            paperImage.color = Paper;
            paperImage.raycastTarget = true;
            Outline paperOutline = paper.gameObject.AddComponent<Outline>();
            paperOutline.effectColor = Ink;
            paperOutline.effectDistance = new Vector2(5f, -5f);

            TMP_Text title = CreateText("Title", paper, "时间轴表现实验室", 42f, TextAlignmentOptions.Center);
            SetRect(title.rectTransform, new Vector2(0.18f, 0.88f), new Vector2(0.82f, 0.98f));
            title.color = Ink;
            title.fontStyle = FontStyles.Bold;

            Button close = CreateButton("Close", paper, "关闭", Close);
            SetRect((RectTransform)close.transform, new Vector2(0.84f, 0.89f), new Vector2(0.95f, 0.96f));

            RectTransform axisHost = CreateRect("AxisHost", paper, new Vector2(0.06f, 0.63f), new Vector2(0.94f, 0.87f));
            Image axisBackdrop = axisHost.gameObject.AddComponent<Image>();
            axisBackdrop.color = new Color(1f, 0.97f, 0.83f, 0.72f);
            axisBackdrop.raycastTarget = false;
            TimelineAxisView prefab = Resources.Load<TimelineAxisView>("Prefabs/UI/Hud/TimelineAxisView");
            if (prefab != null)
            {
                _axis = Instantiate(prefab, axisHost, false);
                SetRect((RectTransform)_axis.transform, Vector2.zero, Vector2.one);
            }
            else
            {
                TMP_Text missing = CreateText(
                    "MissingAxis",
                    axisHost,
                    "缺少 TimelineAxisView 预制体",
                    28f,
                    TextAlignmentOptions.Center);
                SetRect(missing.rectTransform, Vector2.zero, Vector2.one);
                missing.color = Color.red;
            }

            _status = CreateText("Status", paper, string.Empty, 24f, TextAlignmentOptions.Center);
            SetRect(_status.rectTransform, new Vector2(0.08f, 0.54f), new Vector2(0.92f, 0.62f));
            _status.color = Ink;

            RectTransform controls = CreateRect("Controls", paper, new Vector2(0.07f, 0.09f), new Vector2(0.93f, 0.53f));
            var grid = controls.gameObject.AddComponent<GridLayoutGroup>();
            grid.padding = new RectOffset(12, 12, 12, 12);
            grid.spacing = new Vector2(14f, 14f);
            grid.cellSize = new Vector2(280f, 72f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            grid.childAlignment = TextAnchor.MiddleCenter;

            CreateButton("Reset", controls, "重置", ResetLab);
            CreateButton("AdvanceHalf", controls, "推进0.5天", () => PlayAdvance(0.5f));
            CreateButton("AdvanceOne", controls, "推进1天", () => PlayAdvance(1f));
            CreateButton("AdvanceThree", controls, "推进3天", () => PlayAdvance(3f));
            CreateButton("Trigger", controls, "触发节点", TriggerNextNode);
            CreateButton("Add", controls, "新增节点", () => AddNode());
            CreateButton("Replace", controls, "修改行动", () => ReplaceNode());
            CreateButton("Move", controls, "移动节点", () => MoveNode());
            CreateButton("Remove", controls, "删除节点", () => RemoveNode(skipped: false));
            CreateButton("Skip", controls, "跳过节点", () => RemoveNode(skipped: true));
            CreateButton("Extend", controls, "延长时间轴", () => ResizeAxis(1f));
            CreateButton("Shrink", controls, "缩短时间轴", () => ResizeAxis(-1f));
            CreateButton("RunAll", controls, "连续演示", RunAll);
            Button speed = CreateButton("Speed", controls, "倍速1×", CycleSpeed);
            _speedLabel = speed.GetComponentInChildren<TMP_Text>();
            CreateButton("Complete", controls, "立即完成", () => _axis?.CompletePresentation());
            CreateButton("SelectAdd", controls, "选择加日期", BeginAddSelection);
            CreateButton("SelectDelete", controls, "选择删除", BeginDeleteSelection);
            CreateButton("SelectExecute", controls, "选择执行", BeginExecuteSelection);
        }

        private void ResetLab()
        {
            _axis?.CancelPresentation();
            _createdNodeSerial = 0;
            _state = new TimelineAxisViewState
            {
                LengthDays = 7f,
                CurrentDay = 0f,
            };
            _state.Nodes.Add(Node("lab_shop", 1, "act_shop", ActionDisplayKind.Shop));
            TimelineAxisNodeState completed = Node("lab_completed", 2, "act_reward", ActionDisplayKind.Reward);
            completed.Completed = true;
            _state.Nodes.Add(completed);
            _state.Nodes.Add(Node("lab_interest_a", 3, "act_interest", ActionDisplayKind.Interest));
            TimelineAxisNodeState executing = Node("lab_interest_b", 3, "act_reward", ActionDisplayKind.Reward);
            executing.Executing = true;
            _state.Nodes.Add(executing);
            _state.Nodes.Add(Node("lab_negative", 3, "act_negative", ActionDisplayKind.Negative));
            _state.Nodes.Add(Node("lab_event", 5, "act_event", ActionDisplayKind.Event));
            _state.Nodes.Add(Node("lab_boss", 7, "act_boss", ActionDisplayKind.Boss));
            _axis?.Render(_state, animate: false);
            SetStatus("独立演示数据已重置；不会修改当前对局或存档。", null);
        }

        private void PlayAdvance(float delta, Action onComplete = null)
        {
            if (_axis == null || _state == null)
            {
                onComplete?.Invoke();
                return;
            }

            float start = _state.CurrentDay;
            float target = Mathf.Clamp(start + delta, 0f, _state.LengthDays);
            TimelineAxisViewState working = _state.Clone();
            IReadOnlyList<TimelineAxisNodeState> stops = TimelineAxisPresentationPlanner.DueStops(
                working,
                start,
                target);
            var cues = new List<TimelinePresentationCue>();
            float cursor = start;
            foreach (TimelineAxisNodeState stop in stops)
            {
                TimelineAxisViewState arrived = working.Clone();
                arrived.CurrentDay = stop.Day;
                TimelineAxisNodeState arrivedNode = arrived.FindNode(stop.Id);
                if (arrivedNode != null)
                {
                    arrivedNode.Executing = true;
                }

                cues.Add(TimelinePresentationCue.Advance(cursor, stop.Day, stop.Id, arrived));
                cues.Add(TimelinePresentationCue.Node(TimelinePresentationCueKind.TriggerStart, stop.Id, arrived));

                working = arrived.Clone();
                TimelineAxisNodeState completed = working.FindNode(stop.Id);
                if (completed != null)
                {
                    completed.Executing = false;
                    completed.Completed = true;
                }

                cues.Add(TimelinePresentationCue.Node(
                    TimelinePresentationCueKind.TriggerComplete,
                    stop.Id,
                    working.Clone()));
                cursor = stop.Day;
            }

            working.CurrentDay = target;
            if (target > cursor + 0.0001f)
            {
                cues.Add(TimelinePresentationCue.Advance(cursor, target, null, working.Clone()));
            }

            _state = working;
            PlayCues(cues, $"进度推进至第{target:0.0}天", onComplete);
        }

        private void TriggerNextNode()
        {
            TimelineAxisNodeState node = FirstNode(n => !n.Completed);
            if (node == null)
            {
                SetStatus("没有未完成节点。", null);
                return;
            }

            TimelineAxisViewState executing = _state.Clone();
            executing.FindNode(node.Id).Executing = true;
            TimelineAxisViewState completed = executing.Clone();
            TimelineAxisNodeState completedNode = completed.FindNode(node.Id);
            completedNode.Executing = false;
            completedNode.Completed = true;
            _state = completed;
            PlayCues(
                new[]
                {
                    TimelinePresentationCue.Node(TimelinePresentationCueKind.TriggerStart, node.Id, executing),
                    TimelinePresentationCue.Node(TimelinePresentationCueKind.TriggerComplete, node.Id, completed),
                },
                $"节点 {node.Id} 已触发");
        }

        private void AddNode(Action onComplete = null)
        {
            Mutate(
                after =>
                {
                    int day = Mathf.Clamp(Mathf.CeilToInt(after.CurrentDay) + 1, 1, Mathf.FloorToInt(after.LengthDays));
                    int serial = ++_createdNodeSerial;
                    after.Nodes.Add(Node(
                        $"lab_added_{serial}",
                        day,
                        $"lab_added_action_{serial}",
                        serial % 2 == 0 ? ActionDisplayKind.Interest : ActionDisplayKind.Event));
                },
                skipped: false,
                targetNodeId: null,
                "新增节点",
                onComplete);
        }

        private void ReplaceNode(Action onComplete = null)
        {
            TimelineAxisNodeState node = FirstNode(_ => true);
            if (node == null)
            {
                onComplete?.Invoke();
                return;
            }

            Mutate(
                after =>
                {
                    TimelineAxisNodeState changed = after.FindNode(node.Id);
                    changed.ActionId = changed.ActionId + "_changed";
                    changed.Kind = changed.Kind == ActionDisplayKind.Event
                        ? ActionDisplayKind.Interest
                        : ActionDisplayKind.Event;
                },
                false,
                node.Id,
                "修改节点行动",
                onComplete);
        }

        private void MoveNode(Action onComplete = null)
        {
            TimelineAxisNodeState node = FirstNode(n => n.Id != "lab_boss");
            if (node == null)
            {
                onComplete?.Invoke();
                return;
            }

            Mutate(
                after => after.FindNode(node.Id).Day = Mathf.Clamp(
                    node.Day + 2 > after.LengthDays ? node.Day - 1 : node.Day + 2,
                    1,
                    Mathf.FloorToInt(after.LengthDays)),
                false,
                node.Id,
                "移动节点",
                onComplete);
        }

        private void RemoveNode(bool skipped, Action onComplete = null)
        {
            TimelineAxisNodeState node = FirstNode(n => n.Id != "lab_boss");
            if (node == null)
            {
                onComplete?.Invoke();
                return;
            }

            Mutate(
                after => after.Nodes.RemoveAll(n => n.Id == node.Id),
                skipped,
                node.Id,
                skipped ? "跳过节点" : "删除节点",
                onComplete);
        }

        private void ResizeAxis(float delta, Action onComplete = null)
        {
            Mutate(
                after =>
                {
                    after.LengthDays = Mathf.Clamp(after.LengthDays + delta, 4f, 10f);
                    TimelineAxisNodeState boss = after.FindNode("lab_boss");
                    if (boss != null)
                    {
                        boss.Day = Mathf.FloorToInt(after.LengthDays);
                    }

                    after.CurrentDay = Mathf.Min(after.CurrentDay, after.LengthDays);
                },
                false,
                "lab_boss",
                delta > 0f ? "延长时间轴" : "缩短时间轴",
                onComplete);
        }

        private void Mutate(
            Action<TimelineAxisViewState> mutation,
            bool skipped,
            string targetNodeId,
            string label,
            Action onComplete)
        {
            TimelineAxisViewState before = _state.Clone();
            TimelineAxisViewState after = _state.Clone();
            mutation(after);
            TimelineAxisPresentationPlan plan = TimelineAxisPresentationPlanner.BuildMutation(
                before,
                after,
                skipped,
                targetNodeId);
            _state = after;
            PlayPlan(plan, label, onComplete);
        }

        private void RunAll()
        {
            ResetLab();
            PlayAdvance(3f, () => ReplaceNode(() => MoveNode(() => AddNode(() =>
                RemoveNode(skipped: false, () => RemoveNode(skipped: true, () =>
                    ResizeAxis(1f)))))));
        }

        private void PlayCues(
            IReadOnlyList<TimelinePresentationCue> cues,
            string label,
            Action onComplete = null)
        {
            if (_axis == null || cues == null || cues.Count == 0)
            {
                _axis?.Render(_state, animate: false);
                SetStatus(label, onComplete);
                return;
            }

            _axis.Play(
                new TimelineAxisPresentationPlan(cues, _state),
                () => SetStatus(label, onComplete),
                _speed);
        }

        private void PlayPlan(
            TimelineAxisPresentationPlan plan,
            string label,
            Action onComplete = null)
        {
            if (_axis == null || plan == null || plan.IsEmpty)
            {
                _axis?.Render(_state, false);
                SetStatus(label, onComplete);
                return;
            }

            _axis.Play(plan, () => SetStatus(label, onComplete), _speed);
        }

        private void BeginAddSelection()
        {
            TimelineAxisNodeState preview = Node(
                TimelineAxisSelectionController.PreviewId,
                0,
                "lab_preview",
                ActionDisplayKind.Reward);
            _axis?.BeginSelection(TimelineAxisSelectionRequest.AddDay(
                preview,
                new[] { 1, 4, 6 },
                day => SetStatus($"选择了第{day}天（点击节点旁日期刻度）", null),
                () => SetStatus("已取消加日期选择", null)));
            SetStatus("加日期选择态：悬停 1/4/6 日刻度可看预览", null);
        }

        private void BeginDeleteSelection()
        {
            _axis?.BeginSelection(TimelineAxisSelectionRequest.Nodes(
                TimelineAxisSelectionMode.DeleteNode,
                new[] { "lab_shop", "lab_event" },
                id => SetStatus($"选择删除 {id}", null),
                () => SetStatus("已取消删除选择", null)));
            SetStatus("删除节点选择态", null);
        }

        private void BeginExecuteSelection()
        {
            _axis?.BeginSelection(TimelineAxisSelectionRequest.Nodes(
                TimelineAxisSelectionMode.ExecuteNode,
                new[] { "lab_interest_a", "lab_boss" },
                id => SetStatus($"选择执行 {id}", null),
                () => SetStatus("已取消执行选择", null)));
            SetStatus("执行节点选择态", null);
        }

        private void SetStatus(string label, Action onComplete)
        {
            if (_status != null)
            {
                _status.text = $"{label}  ·  第{(_state?.CurrentDay ?? 0f):0.0}天 / {(_state?.LengthDays ?? 0f):0.0}天";
            }

            onComplete?.Invoke();
        }

        private void CycleSpeed()
        {
            _speed = _speed < 0.75f ? 1f : (_speed < 1.5f ? 2f : 0.5f);
            if (_speedLabel != null)
            {
                _speedLabel.text = $"倍速{_speed:0.#}×";
            }
        }

        private TimelineAxisNodeState FirstNode(Predicate<TimelineAxisNodeState> predicate)
        {
            return _state?.Nodes.Find(node => node != null && predicate(node));
        }

        private void Close()
        {
            GameApp.UI.CloseUIForm(UIForm);
        }

        private static TimelineAxisNodeState Node(
            string id,
            int day,
            string actionId,
            ActionDisplayKind kind)
        {
            return new TimelineAxisNodeState
            {
                Id = id,
                Day = day,
                ActionId = actionId,
                Kind = kind,
                IconKey = TimelineAxisIconKeys.ForKind(kind),
            };
        }

        private static RectTransform CreateRect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            SetRect(rect, anchorMin, anchorMax);
            return rect;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static TMP_Text CreateText(
            string name,
            Transform parent,
            string value,
            float size,
            TextAlignmentOptions alignment)
        {
            var go = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            TMP_Text text = go.GetComponent<TMP_Text>();
            text.text = value;
            text.font = Resources.Load<TMP_FontAsset>("Fonts/AlimamaShuHeiTi-Bold SDF")
                ?? TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Ink;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateButton(
            string name,
            Transform parent,
            string label,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = ButtonFill;
            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = Ink;
            outline.effectDistance = new Vector2(2f, -2f);
            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            TMP_Text text = CreateText("Label", go.transform, label, 24f, TextAlignmentOptions.Center);
            SetRect(text.rectTransform, Vector2.zero, Vector2.one);
            text.fontStyle = FontStyles.Bold;
            return button;
        }
    }
}
