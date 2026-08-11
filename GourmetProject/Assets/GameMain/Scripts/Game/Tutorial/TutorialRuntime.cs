using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Tutorial
{
    /// <summary>Stable string-id registry used by both prefab UI and dynamically spawned cards.</summary>
    public static class TutorialAnchorRegistry
    {
        private static readonly Dictionary<string, RectTransform> Anchors = new(StringComparer.Ordinal);

        public static void Register(string id, RectTransform target)
        {
            if (!string.IsNullOrEmpty(id) && target != null) Anchors[id] = target;
        }

        public static void Unregister(string id, RectTransform target = null)
        {
            if (string.IsNullOrEmpty(id) || !Anchors.TryGetValue(id, out RectTransform current)) return;
            if (target == null || current == target) Anchors.Remove(id);
        }

        public static bool TryGet(string id, out RectTransform target)
        {
            if (Anchors.TryGetValue(id ?? string.Empty, out target) && target != null && target.gameObject.activeInHierarchy)
                return true;
            target = null;
            return false;
        }

        public static void ClearDead()
        {
            var dead = new List<string>();
            foreach (KeyValuePair<string, RectTransform> pair in Anchors)
                if (pair.Value == null) dead.Add(pair.Key);
            foreach (string id in dead) Anchors.Remove(id);
        }
    }

    /// <summary>页面向教程暴露的可等待 UI 操作。处理器必须且只能在操作完成后调用 done。</summary>
    public static class TutorialCommandRegistry
    {
        private static readonly Dictionary<string, Action<Action>> Handlers = new(StringComparer.Ordinal);

        public static void Register(string id, Action<Action> handler)
        {
            if (!string.IsNullOrEmpty(id) && handler != null) Handlers[id] = handler;
        }

        public static void Unregister(string id, Action<Action> handler = null)
        {
            if (string.IsNullOrEmpty(id) || !Handlers.TryGetValue(id, out Action<Action> current)) return;
            if (handler == null || current == handler) Handlers.Remove(id);
        }

        public static void Execute(string id, Action done)
        {
            if (string.IsNullOrEmpty(id) || !Handlers.TryGetValue(id, out Action<Action> handler))
            {
                done?.Invoke();
                return;
            }

            bool completed = false;
            void CompleteOnce()
            {
                if (completed) return;
                completed = true;
                done?.Invoke();
            }

            try
            {
                handler(CompleteOnce);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                CompleteOnce();
            }
        }
    }

    /// <summary>
    /// Event-driven tutorial coordinator. A sequence is persisted before playback; completion is flushed immediately.
    /// If play is interrupted it remains pending and restarts at step zero on the next compatible page.
    /// </summary>
    public static class TutorialRuntime
    {
        private static TutorialSequenceDefinition _sequence;
        private static int _stepIndex;
        private static Action _onComplete;
        private static TutorialOverlayView _overlay;
        private static bool _transitioning;
        private static int _operationVersion;

        public static bool IsPlaying => _sequence != null;
        public static string CurrentId => _sequence?.Id ?? string.Empty;

        public static bool Play(string id, Action onComplete = null)
        {
            if (string.IsNullOrEmpty(id) || TutorialProgressService.IsCompleted(id))
            {
                onComplete?.Invoke();
                return false;
            }

            TutorialSequenceDefinition definition = TutorialCatalog.Get(id);
            if (definition == null || definition.Steps.Count == 0)
            {
                TutorialProgressService.Complete(id);
                onComplete?.Invoke();
                return false;
            }

            return Play(definition, onComplete);
        }

        public static bool PlayResultHeart(bool isWin, Action onComplete = null)
        {
            // 旧版结算说明已由胜败共享的红心说明取代，顺手清掉中断存档中的旧完成状态。
            if (!TutorialProgressService.IsCompleted(TutorialId.Settlement))
                TutorialProgressService.Complete(TutorialId.Settlement);

            if (TutorialProgressService.IsCompleted(TutorialId.ResultHeart))
            {
                onComplete?.Invoke();
                return false;
            }

            if (IsPlaying)
            {
                // 结果流程不能被教程阻塞；本次未能播放时，下一次结算仍会再次尝试。
                onComplete?.Invoke();
                return false;
            }

            return Play(TutorialCatalog.BuildResultHeart(isWin), onComplete);
        }

        private static bool Play(TutorialSequenceDefinition definition, Action onComplete)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Id) || definition.Steps.Count == 0)
            {
                onComplete?.Invoke();
                return false;
            }

            TutorialProgressService.Enqueue(definition.Id);
            if (_sequence != null)
            {
                // Later hooks are already persisted and will be drained after the core sequence.
                return false;
            }

            _sequence = definition;
            _stepIndex = 0;
            _onComplete = onComplete;
            _transitioning = false;
            _operationVersion++;
            ShowCurrent();
            return true;
        }

        public static void Publish(string signal)
        {
            if (_transitioning || _sequence == null || _stepIndex < 0 || _stepIndex >= _sequence.Steps.Count) return;
            TutorialStepDefinition step = _sequence.Steps[_stepIndex];
            if (step.Mode == TutorialAdvanceMode.Signal
                && string.Equals(step.Signal, signal, StringComparison.Ordinal))
                Advance();
        }

        public static void EnqueueHook(string id)
        {
            if (string.IsNullOrEmpty(id) || TutorialProgressService.IsCompleted(id)) return;
            TutorialProgressService.Enqueue(id);
            if (TutorialProgressService.IsCompleted(TutorialId.CoreComplete) && !IsPlaying) DrainPending();
        }

        public static void ObserveContentAcquired(RunContentAcquisition acquisition)
        {
            string hook = ContentHookFor(acquisition);
            if (!string.IsNullOrEmpty(hook)) EnqueueHook(hook);
        }

        internal static string ContentHookFor(RunContentAcquisition acquisition)
        {
            if (acquisition == null) return string.Empty;
            if (acquisition.Kind == RunContentAcquisitionKind.DishFlavor) return TutorialId.Flavor;
            if (acquisition.Kind == RunContentAcquisitionKind.TableMaterial) return TutorialId.Material;
            if (acquisition.Kind != RunContentAcquisitionKind.Item) return string.Empty;
            if (acquisition.ItemKind == cfg.ItemKind.Passive) return TutorialId.PassiveItem;
            if (string.Equals(acquisition.ItemEffectType, ItemEffectTypes.AddFlavor, StringComparison.Ordinal)
                || string.Equals(acquisition.ItemEffectType, ItemEffectTypes.EnhanceFlavor, StringComparison.Ordinal))
                return TutorialId.Flavor;
            if (string.Equals(acquisition.ItemEffectType, ItemEffectTypes.AddMaterial, StringComparison.Ordinal))
                return TutorialId.Material;
            if (acquisition.ActiveItemCategory == cfg.ActiveItemCategory.Adjust) return TutorialId.Adjustment;
            return string.Empty;
        }

        public static void ObserveItemShown(ItemDefinition item)
        {
            if (item?.Kind == cfg.ItemKind.Active && item.ActiveItemCategory == cfg.ActiveItemCategory.Adjust)
                EnqueueHook(TutorialId.Adjustment);
        }

        public static void DrainPending()
        {
            if (IsPlaying || !TutorialProgressService.IsCompleted(TutorialId.CoreComplete)) return;
            foreach (string id in TutorialProgressService.Pending())
            {
                // 旧版失败说明不再单独播放；新的胜败结果共用 ResultHeart。
                if (string.Equals(id, TutorialId.Failure, StringComparison.Ordinal))
                {
                    TutorialProgressService.Complete(id);
                    continue;
                }

                if (!TutorialId.IsCore(id) && Play(id, DrainPending)) return;
            }
        }

        public static void CloseForPageChange()
        {
            _operationVersion++;
            if (_sequence != null && _stepIndex >= 0 && _stepIndex < _sequence.Steps.Count)
                TutorialCommandRegistry.Execute(_sequence.Steps[_stepIndex].ExitCommand, null);
            _overlay?.Dispose();
            _overlay = null;
            _sequence = null;
            _stepIndex = 0;
            _onComplete = null;
            _transitioning = false;
        }

        private static void ShowCurrent()
        {
            if (_sequence == null) return;
            _transitioning = true;
            int version = _operationVersion;
            TutorialSequenceDefinition sequence = _sequence;
            int index = _stepIndex;
            TutorialStepDefinition step = _sequence.Steps[_stepIndex];
            TutorialCommandRegistry.Execute(step.EnterCommand, () =>
            {
                if (version != _operationVersion || _sequence != sequence || _stepIndex != index) return;
                EnsureOverlay();
                _transitioning = false;
                _overlay.Show(
                    step,
                    index,
                    sequence.Steps.Count,
                    step.Mode == TutorialAdvanceMode.Continue ? Advance : null);
            });
        }

        private static void Advance()
        {
            if (_transitioning || _sequence == null) return;
            _transitioning = true;
            int version = _operationVersion;
            TutorialSequenceDefinition sequence = _sequence;
            TutorialStepDefinition step = sequence.Steps[_stepIndex];
            TutorialCommandRegistry.Execute(step.ExitCommand, () =>
            {
                if (version != _operationVersion || _sequence != sequence) return;
                _stepIndex++;
                _transitioning = false;
                if (_stepIndex < sequence.Steps.Count)
                {
                    ShowCurrent();
                    return;
                }

                CompleteSequence();
            });
        }

        private static void CompleteSequence()
        {
            if (_sequence == null) return;
            string completed = _sequence.Id;
            Action callback = _onComplete;
            _overlay?.Hide();
            _sequence = null;
            _stepIndex = 0;
            _onComplete = null;
            _transitioning = false;
            TutorialProgressService.Complete(completed);
            if (string.Equals(completed, TutorialId.TimelineNode, StringComparison.Ordinal))
                TutorialProgressService.Complete(TutorialId.CoreComplete);
            callback?.Invoke();
            if (TutorialProgressService.IsCompleted(TutorialId.CoreComplete)) DrainPending();
        }

        private static void EnsureOverlay()
        {
            if (_overlay != null) return;
            _overlay = TutorialOverlayView.Create();
        }
    }

    /// <summary>铛铛引导层：四块遮罩保留高亮挖孔，并按步骤决定挖孔是否可交互。</summary>
    internal sealed class TutorialOverlayView : MonoBehaviour
    {
        private const float TipWidth = 900f;
        private const float TipHeight = 270f;
        private RectTransform _root;
        private readonly List<Image> _masks = new();
        private readonly List<Image> _progressDots = new();
        private Image _holeBlocker;
        private RectTransform _tip;
        private Image _mascot;
        private TMP_Text _message;
        private Button _continue;
        private Action _advance;
        private TutorialStepDefinition _step;
        private Rect _lastHole;
        private bool _lastHadHole;
        private Vector2Int _lastScreenSize;

        public static TutorialOverlayView Create()
        {
            var go = new GameObject("TutorialOverlay", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(TutorialOverlayView));
            DontDestroyOnLoad(go);
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32000;
            RectTransform root = go.GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            TutorialOverlayView view = go.GetComponent<TutorialOverlayView>();
            view.Build(root);
            return view;
        }

        private void Build(RectTransform root)
        {
            _root = root;
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("Mask" + i, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(root, false);
                Image image = go.GetComponent<Image>();
                image.color = new Color(0f, 0f, 0f, 0.72f);
                image.raycastTarget = true;
                _masks.Add(image);
            }

            var blockerGo = new GameObject("HoleInputBlocker", typeof(RectTransform), typeof(Image));
            blockerGo.transform.SetParent(root, false);
            _holeBlocker = blockerGo.GetComponent<Image>();
            _holeBlocker.color = new Color(1f, 1f, 1f, 0.001f);
            _holeBlocker.raycastTarget = true;
            _holeBlocker.gameObject.SetActive(false);

            var tipGo = new GameObject("DangDangDialogue", typeof(RectTransform));
            tipGo.transform.SetParent(root, false);
            _tip = tipGo.GetComponent<RectTransform>();
            _tip.anchorMin = _tip.anchorMax = new Vector2(0.5f, 0.5f);
            _tip.sizeDelta = new Vector2(TipWidth, TipHeight);

            var panelGo = new GameObject("DialoguePanel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(_tip, false);
            RectTransform panelRect = panelGo.GetComponent<RectTransform>();
            SetCenteredRect(panelRect, new Vector2(80f, -4f), new Vector2(740f, 222f));
            Image panel = panelGo.GetComponent<Image>();
            panel.sprite = LoadSprite("Sprites/UI/Tutorial/Components/dialogue_panel_base");
            panel.color = panel.sprite != null ? Color.white : new Color(0.96f, 0.90f, 0.74f, 0.99f);
            panel.raycastTarget = true;

            var mascotGo = new GameObject("DangDang", typeof(RectTransform), typeof(Image));
            mascotGo.transform.SetParent(_tip, false);
            RectTransform mascotRect = mascotGo.GetComponent<RectTransform>();
            SetCenteredRect(mascotRect, new Vector2(-348f, 4f), new Vector2(270f, 270f));
            _mascot = mascotGo.GetComponent<Image>();
            _mascot.preserveAspect = true;
            _mascot.raycastTarget = false;

            var nameplateGo = new GameObject("Nameplate", typeof(RectTransform), typeof(Image));
            nameplateGo.transform.SetParent(panelRect, false);
            RectTransform nameplateRect = nameplateGo.GetComponent<RectTransform>();
            SetCenteredRect(nameplateRect, new Vector2(-232f, 76f), new Vector2(190f, 50f));
            Image nameplate = nameplateGo.GetComponent<Image>();
            nameplate.sprite = LoadSprite("Sprites/UI/Tutorial/Components/dialogue_nameplate");
            nameplate.color = nameplate.sprite != null ? Color.white : new Color(0.76f, 0.43f, 0.14f, 1f);
            nameplate.raycastTarget = false;

            var nameGo = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
            nameGo.transform.SetParent(nameplateRect, false);
            Stretch(nameGo.GetComponent<RectTransform>());
            TMP_Text name = nameGo.GetComponent<TextMeshProUGUI>();
            name.text = "铛铛";
            name.fontSize = 27f;
            name.fontStyle = FontStyles.Bold;
            name.color = new Color(0.22f, 0.11f, 0.04f, 1f);
            name.alignment = TextAlignmentOptions.Center;
            name.raycastTarget = false;

            var textGo = new GameObject("Message", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(panelRect, false);
            RectTransform textRect = textGo.GetComponent<RectTransform>();
            SetCenteredRect(textRect, new Vector2(45f, 10f), new Vector2(570f, 118f));
            _message = textGo.GetComponent<TextMeshProUGUI>();
            _message.fontSize = 27f;
            _message.color = new Color(0.20f, 0.12f, 0.06f, 1f);
            _message.alignment = TextAlignmentOptions.MidlineLeft;
            _message.enableWordWrapping = true;
            _message.raycastTarget = false;

            var buttonGo = new GameObject("Continue", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(panelRect, false);
            RectTransform buttonRect = buttonGo.GetComponent<RectTransform>();
            SetCenteredRect(buttonRect, new Vector2(245f, -76f), new Vector2(150f, 52f));
            Image buttonImage = buttonGo.GetComponent<Image>();
            buttonImage.sprite = LoadSprite("Sprites/UI/Tutorial/Components/dialogue_continue_button");
            buttonImage.color = buttonImage.sprite != null ? Color.white : new Color(0.94f, 0.64f, 0.18f, 1f);
            _continue = buttonGo.GetComponent<Button>();
            _continue.onClick.AddListener(() => _advance?.Invoke());
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(buttonGo.transform, false);
            RectTransform labelRect = labelGo.GetComponent<RectTransform>();
            Stretch(labelRect);
            TMP_Text label = labelGo.GetComponent<TextMeshProUGUI>();
            label.text = "继续";
            label.fontSize = 25f;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.18f, 0.10f, 0.04f, 1f);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            var progressGo = new GameObject("Progress", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            progressGo.transform.SetParent(panelRect, false);
            RectTransform progressRect = progressGo.GetComponent<RectTransform>();
            SetCenteredRect(progressRect, new Vector2(-55f, -78f), new Vector2(300f, 30f));
            HorizontalLayoutGroup progress = progressGo.GetComponent<HorizontalLayoutGroup>();
            progress.spacing = 7f;
            progress.childAlignment = TextAnchor.MiddleCenter;
            progress.childControlWidth = false;
            progress.childControlHeight = false;
            progress.childForceExpandWidth = false;
            progress.childForceExpandHeight = false;
        }

        public void Show(TutorialStepDefinition step, int stepIndex, int stepCount, Action advance)
        {
            gameObject.SetActive(true);
            _step = step;
            _advance = advance;
            _message.text = step.Message;
            _mascot.sprite = LoadMascot(step.Pose);
            _mascot.enabled = _mascot.sprite != null;
            _continue.gameObject.SetActive(step.Mode == TutorialAdvanceMode.Continue);
            RefreshProgress(stepIndex, stepCount);
            Canvas.ForceUpdateCanvases();
            bool hasHole = TryResolveHole(step.Anchors, out Rect hole);
            if (hasHole) ApplyHole(hole);
            else ApplyFallback(step.Mode == TutorialAdvanceMode.Continue);
            PositionTip(hasHole ? hole : new Rect(Screen.width * 0.5f, Screen.height * 0.5f, 0f, 0f));
            _lastHadHole = hasHole;
            _lastHole = hole;
            _lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        }

        private void LateUpdate()
        {
            if (_step == null) return;
            bool hasHole = TryResolveHole(_step.Anchors, out Rect hole);
            Vector2Int screen = new(Screen.width, Screen.height);
            if (screen == _lastScreenSize && hasHole == _lastHadHole && (!hasHole || Approximately(hole, _lastHole))) return;
            if (hasHole) ApplyHole(hole);
            else ApplyFallback(_step.Mode == TutorialAdvanceMode.Continue);
            PositionTip(hasHole ? hole : new Rect(Screen.width * 0.5f, Screen.height * 0.5f, 0f, 0f));
            _lastHadHole = hasHole;
            _lastHole = hole;
            _lastScreenSize = screen;
        }

        private static bool Approximately(Rect a, Rect b) =>
            Mathf.Abs(a.x - b.x) < 0.5f && Mathf.Abs(a.y - b.y) < 0.5f
            && Mathf.Abs(a.width - b.width) < 0.5f && Mathf.Abs(a.height - b.height) < 0.5f;

        public void Hide()
        {
            if (this != null) gameObject.SetActive(false);
        }

        public void Dispose()
        {
            if (this != null) Destroy(gameObject);
        }

        private bool TryResolveHole(IReadOnlyList<string> ids, out Rect result)
        {
            result = default;
            bool any = false;
            if (ids == null) return false;
            foreach (string id in ids)
            {
                if (!TutorialAnchorRegistry.TryGet(id, out RectTransform target)) continue;
                Vector3[] corners = new Vector3[4];
                target.GetWorldCorners(corners);
                Camera camera = target.GetComponentInParent<Canvas>()?.worldCamera;
                for (int i = 0; i < corners.Length; i++) corners[i] = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
                Rect rect = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
                if (!any) { result = rect; any = true; }
                else
                {
                    result = Rect.MinMaxRect(Mathf.Min(result.xMin, rect.xMin), Mathf.Min(result.yMin, rect.yMin),
                        Mathf.Max(result.xMax, rect.xMax), Mathf.Max(result.yMax, rect.yMax));
                }
            }
            if (any)
            {
                const float padding = 16f;
                result = Rect.MinMaxRect(result.xMin - padding, result.yMin - padding, result.xMax + padding, result.yMax + padding);
            }
            return any;
        }

        private void ApplyHole(Rect hole)
        {
            SetMask(_masks[0].rectTransform, 0f, 0f, hole.xMin, Screen.height);
            SetMask(_masks[1].rectTransform, hole.xMax, 0f, Screen.width, Screen.height);
            SetMask(_masks[2].rectTransform, hole.xMin, 0f, hole.xMax, hole.yMin);
            SetMask(_masks[3].rectTransform, hole.xMin, hole.yMax, hole.xMax, Screen.height);
            _holeBlocker.gameObject.SetActive(!_step.AllowTargetInteraction);
            if (!_step.AllowTargetInteraction)
                SetMask(_holeBlocker.rectTransform, hole.xMin, hole.yMin, hole.xMax, hole.yMax);
        }

        private void ApplyFallback(bool blockInput)
        {
            for (int i = 0; i < _masks.Count; i++) _masks[i].gameObject.SetActive(i == 0 && blockInput);
            if (blockInput) SetMask(_masks[0].rectTransform, 0f, 0f, Screen.width, Screen.height);
            _holeBlocker.gameObject.SetActive(false);
        }

        private void SetMask(RectTransform rect, float xMin, float yMin, float xMax, float yMax)
        {
            rect.gameObject.SetActive(xMax > xMin && yMax > yMin);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(xMin, yMin);
            rect.sizeDelta = new Vector2(Mathf.Max(0f, xMax - xMin), Mathf.Max(0f, yMax - yMin));
        }

        private void PositionTip(Rect hole)
        {
            float referenceScale = Mathf.Max(0.62f, Screen.height / 1080f);
            float widthFitScale = Mathf.Max(0.62f, (Screen.width - 24f) / TipWidth);
            float scale = Mathf.Min(referenceScale, widthFitScale);
            _tip.localScale = Vector3.one * scale;
            float halfWidth = TipWidth * scale * 0.5f;
            float halfHeight = TipHeight * scale * 0.5f;
            float x = Mathf.Clamp(hole.center.x, halfWidth + 12f, Screen.width - halfWidth - 12f);
            float y = hole.yMin > TipHeight * scale + 28f
                ? hole.yMin - halfHeight - 18f
                : hole.yMax + halfHeight + 18f;
            y = Mathf.Clamp(y, halfHeight + 12f, Screen.height - halfHeight - 12f);
            _tip.anchorMin = _tip.anchorMax = Vector2.zero;
            _tip.anchoredPosition = new Vector2(x, y);
        }

        private void RefreshProgress(int current, int count)
        {
            count = Mathf.Max(1, count);
            Transform parent = _progressDots.Count > 0
                ? _progressDots[0].transform.parent
                : _continue.transform.parent.Find("Progress");
            while (_progressDots.Count < count)
            {
                var dotGo = new GameObject($"Dot{_progressDots.Count + 1}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                dotGo.transform.SetParent(parent, false);
                dotGo.GetComponent<RectTransform>().sizeDelta = new Vector2(19f, 19f);
                LayoutElement layout = dotGo.GetComponent<LayoutElement>();
                layout.preferredWidth = 19f;
                layout.preferredHeight = 19f;
                Image dot = dotGo.GetComponent<Image>();
                dot.preserveAspect = true;
                dot.raycastTarget = false;
                _progressDots.Add(dot);
            }

            Sprite active = LoadSprite("Sprites/UI/Tutorial/Components/dialogue_progress_active");
            Sprite inactive = LoadSprite("Sprites/UI/Tutorial/Components/dialogue_progress_inactive");
            for (int i = 0; i < _progressDots.Count; i++)
            {
                Image dot = _progressDots[i];
                dot.gameObject.SetActive(i < count);
                if (i < count) dot.sprite = i == current ? active : inactive;
            }
        }

        private static Sprite LoadMascot(TutorialMascotPose pose)
        {
            string asset = pose switch
            {
                TutorialMascotPose.PointRight => "dangdang_point_right",
                TutorialMascotPose.Remind => "dangdang_remind",
                TutorialMascotPose.Think => "dangdang_think",
                TutorialMascotPose.Wave => "dangdang_wave",
                TutorialMascotPose.Celebrate => "dangdang_celebrate",
                _ => "dangdang_explain",
            };
            return LoadSprite($"Sprites/UI/Tutorial/Mascot/{asset}");
        }

        private static Sprite LoadSprite(string path)
        {
            Sprite sprite = Resources.Load<Sprite>(path);
            if (sprite != null) return sprite;
            Sprite[] sprites = Resources.LoadAll<Sprite>(path);
            return sprites != null && sprites.Length > 0 ? sprites[0] : null;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void SetCenteredRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
