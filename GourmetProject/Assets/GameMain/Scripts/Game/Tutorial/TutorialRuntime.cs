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

            TutorialProgressService.Enqueue(id);
            if (_sequence != null)
            {
                // Later hooks are already persisted and will be drained after the core sequence.
                return false;
            }

            _sequence = definition;
            _stepIndex = 0;
            _onComplete = onComplete;
            ShowCurrent();
            return true;
        }

        public static void Publish(string signal)
        {
            if (_sequence == null || _stepIndex < 0 || _stepIndex >= _sequence.Steps.Count) return;
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
            if (acquisition == null) return;
            if (acquisition.Kind == RunContentAcquisitionKind.DishFlavor)
            {
                EnqueueHook(TutorialId.Flavor);
                return;
            }
            if (acquisition.Kind == RunContentAcquisitionKind.TableMaterial)
            {
                EnqueueHook(TutorialId.Material);
                return;
            }
            if (acquisition.Kind != RunContentAcquisitionKind.Item) return;
            if (acquisition.ItemKind == cfg.ItemKind.Passive)
            {
                EnqueueHook(TutorialId.PassiveItem);
                return;
            }
            if (string.Equals(acquisition.ItemEffectType, ItemEffectTypes.AddFlavor, StringComparison.Ordinal)
                || string.Equals(acquisition.ItemEffectType, ItemEffectTypes.EnhanceFlavor, StringComparison.Ordinal))
                EnqueueHook(TutorialId.Flavor);
            else if (string.Equals(acquisition.ItemEffectType, ItemEffectTypes.AddMaterial, StringComparison.Ordinal))
                EnqueueHook(TutorialId.Material);
            else if (acquisition.ActiveItemCategory == cfg.ActiveItemCategory.Adjust)
                EnqueueHook(TutorialId.Adjustment);
        }

        public static void DrainPending()
        {
            if (IsPlaying || !TutorialProgressService.IsCompleted(TutorialId.CoreComplete)) return;
            foreach (string id in TutorialProgressService.Pending())
            {
                if (!TutorialId.IsCore(id) && Play(id, DrainPending)) return;
            }
        }

        public static void CloseForPageChange()
        {
            _overlay?.Dispose();
            _overlay = null;
            _sequence = null;
            _stepIndex = 0;
            _onComplete = null;
        }

        private static void ShowCurrent()
        {
            if (_sequence == null) return;
            EnsureOverlay();
            TutorialStepDefinition step = _sequence.Steps[_stepIndex];
            _overlay.Show(step, step.Mode == TutorialAdvanceMode.Continue ? Advance : null);
        }

        private static void Advance()
        {
            if (_sequence == null) return;
            _stepIndex++;
            if (_stepIndex < _sequence.Steps.Count)
            {
                ShowCurrent();
                return;
            }

            string completed = _sequence.Id;
            Action callback = _onComplete;
            _overlay?.Hide();
            _sequence = null;
            _stepIndex = 0;
            _onComplete = null;
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

    /// <summary>Four-panel rectangular mask: focused targets remain outside raycast-blocking graphics.</summary>
    internal sealed class TutorialOverlayView : MonoBehaviour
    {
        private RectTransform _root;
        private readonly List<Image> _masks = new();
        private RectTransform _tip;
        private TMP_Text _message;
        private Button _continue;
        private Action _advance;
        private TutorialStepDefinition _step;
        private Rect _lastHole;
        private bool _lastHadHole;

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

            var tipGo = new GameObject("Tip", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            tipGo.transform.SetParent(root, false);
            _tip = tipGo.GetComponent<RectTransform>();
            _tip.anchorMin = _tip.anchorMax = new Vector2(0.5f, 0.5f);
            _tip.sizeDelta = new Vector2(640f, 160f);
            Image panel = tipGo.GetComponent<Image>();
            panel.color = new Color(0.12f, 0.09f, 0.06f, 0.98f);
            VerticalLayoutGroup layout = tipGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(30, 30, 24, 20);
            layout.spacing = 14f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = tipGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textGo = new GameObject("Message", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(_tip, false);
            _message = textGo.GetComponent<TextMeshProUGUI>();
            _message.fontSize = 30f;
            _message.color = Color.white;
            _message.alignment = TextAlignmentOptions.MidlineLeft;
            _message.enableWordWrapping = true;

            var buttonGo = new GameObject("Continue", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonGo.transform.SetParent(_tip, false);
            buttonGo.GetComponent<Image>().color = new Color(0.94f, 0.64f, 0.18f, 1f);
            LayoutElement buttonLayout = buttonGo.GetComponent<LayoutElement>();
            buttonLayout.preferredHeight = 54f;
            buttonLayout.preferredWidth = 180f;
            _continue = buttonGo.GetComponent<Button>();
            _continue.onClick.AddListener(() => _advance?.Invoke());
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(buttonGo.transform, false);
            RectTransform labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            TMP_Text label = labelGo.GetComponent<TextMeshProUGUI>();
            label.text = "继续";
            label.fontSize = 28f;
            label.color = Color.black;
            label.alignment = TextAlignmentOptions.Center;
        }

        public void Show(TutorialStepDefinition step, Action advance)
        {
            gameObject.SetActive(true);
            _step = step;
            _advance = advance;
            _message.text = step.Message;
            _continue.gameObject.SetActive(step.Mode == TutorialAdvanceMode.Continue);
            Canvas.ForceUpdateCanvases();
            bool hasHole = TryResolveHole(step.Anchors, out Rect hole);
            if (hasHole) ApplyHole(hole);
            else ApplyFallback(step.Mode == TutorialAdvanceMode.Continue);
            PositionTip(hasHole ? hole : new Rect(Screen.width * 0.5f, Screen.height * 0.5f, 0f, 0f));
            _lastHadHole = hasHole;
            _lastHole = hole;
        }

        private void LateUpdate()
        {
            if (_step == null) return;
            bool hasHole = TryResolveHole(_step.Anchors, out Rect hole);
            if (hasHole == _lastHadHole && (!hasHole || Approximately(hole, _lastHole))) return;
            if (hasHole) ApplyHole(hole);
            else ApplyFallback(_step.Mode == TutorialAdvanceMode.Continue);
            PositionTip(hasHole ? hole : new Rect(Screen.width * 0.5f, Screen.height * 0.5f, 0f, 0f));
            _lastHadHole = hasHole;
            _lastHole = hole;
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
        }

        private void ApplyFallback(bool blockInput)
        {
            for (int i = 0; i < _masks.Count; i++) _masks[i].gameObject.SetActive(i == 0 && blockInput);
            if (blockInput) SetMask(_masks[0].rectTransform, 0f, 0f, Screen.width, Screen.height);
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
            float x = Mathf.Clamp(hole.center.x, 340f, Screen.width - 340f);
            float y = hole.yMin > 260f ? hole.yMin - 120f : hole.yMax + 120f;
            y = Mathf.Clamp(y, 130f, Screen.height - 130f);
            _tip.anchorMin = _tip.anchorMax = Vector2.zero;
            _tip.anchoredPosition = new Vector2(x, y);
        }
    }
}
