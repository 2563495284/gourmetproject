using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI;
using GourmetProject.Runtime;

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
            if (_overlay != null && !_overlay.IsPresentationReady) return;
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
                if (_overlay == null)
                {
                    Debug.LogError("Tutorial overlay could not be created; closing the current tutorial sequence.");
                    CloseForPageChange();
                    return;
                }
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
    internal sealed class TutorialOverlayView
    {
        private const string PrefabResourcePath = "Prefabs/UI/Tutorial/TutorialOverlay";
        private const float DialogueBaseScale = 0.5f;
        private const float RevealDuration = 0.22f;
        private const float CharactersPerSecond = 30f;
        private const float LayoutMargin = 12f;
        private const float HoleGap = 18f;
        private readonly GameObject _instance;
        private readonly RectTransform _root;
        private readonly Image[] _masks;
        private readonly Image _holeBlocker;
        private readonly RectTransform _tip;
        private readonly RectTransform _dialoguePanel;
        private readonly Image _mascot;
        private readonly TMP_Text _message;
        private readonly CanvasGroup _tipCanvasGroup;
        private readonly Vector3 _dialoguePanelBaseScale;
        private readonly Vector3 _mascotBaseScale;
        private readonly TutorialOverlayClickSurface[] _clickSurfaces;
        private Action _advance;
        private TutorialStepDefinition _step;
        private Rect _lastHole;
        private bool _lastHadHole;
        private Vector2Int _lastScreenSize;
        private Tween _presentationTween;
        private bool _presentationReady;

        public Transform transform => _instance != null ? _instance.transform : null;
        internal bool IsPresentationReady => _presentationReady;
        internal Rect CurrentHole => _lastHole;
        internal Rect CurrentDialogueBounds
        {
            get
            {
                Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(_tip);
                float scale = _tip.localScale.x;
                return TipRect(
                    _tip.anchoredPosition,
                    (Vector2)bounds.min * scale,
                    (Vector2)bounds.max * scale);
            }
        }

        public static TutorialOverlayView Create()
        {
            GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
            if (prefab == null)
            {
                Debug.LogError($"Tutorial overlay prefab is missing at Resources/{PrefabResourcePath}.");
                return null;
            }

            var tutorialGroup = GameApp.UI?.GetUIGroup(UIForms.GroupTutorial);
            if (!(tutorialGroup?.Helper is Component helper))
            {
                Debug.LogError($"Tutorial UI group '{UIForms.GroupTutorial}' is unavailable.");
                return null;
            }

            return CreateUnderParent(prefab, helper.transform);
        }

        internal static TutorialOverlayView CreateForTests(Transform parent)
        {
            GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
            return prefab != null && parent != null ? CreateUnderParent(prefab, parent) : null;
        }

        private static TutorialOverlayView CreateUnderParent(GameObject prefab, Transform parent)
        {
            GameObject instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            RectTransform root = instance.transform as RectTransform;
            if (root == null)
            {
                Debug.LogError("Tutorial overlay prefab root must be a RectTransform.");
                UnityEngine.Object.Destroy(instance);
                return null;
            }

            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            root.localScale = Vector3.one;
            instance.transform.SetAsLastSibling();
            return new TutorialOverlayView(instance);
        }

        private TutorialOverlayView(GameObject instance)
        {
            _instance = instance ?? throw new ArgumentNullException(nameof(instance));
            _root = Require<RectTransform>(instance.transform, string.Empty);
            _masks = new[]
            {
                Require<Image>(instance.transform, "Mask0"),
                Require<Image>(instance.transform, "Mask1"),
                Require<Image>(instance.transform, "Mask2"),
                Require<Image>(instance.transform, "Mask3"),
            };
            _holeBlocker = Require<Image>(instance.transform, "HoleInputBlocker");
            _tip = Require<RectTransform>(instance.transform, "DangDangDialogue");
            _dialoguePanel = Require<RectTransform>(instance.transform, "DangDangDialogue/DialoguePanel");
            _mascot = Require<Image>(instance.transform, "DangDangDialogue/DangDang");
            _message = Require<TMP_Text>(instance.transform, "DangDangDialogue/DialoguePanel/Message");
            _tipCanvasGroup = _tip.GetComponent<CanvasGroup>();
            if (_tipCanvasGroup == null)
                _tipCanvasGroup = _tip.gameObject.AddComponent<CanvasGroup>();
            _dialoguePanelBaseScale = _dialoguePanel.localScale;
            _mascotBaseScale = _mascot.rectTransform.localScale;
            _clickSurfaces = instance.GetComponentsInChildren<TutorialOverlayClickSurface>(includeInactive: true);
            if (_clickSurfaces.Length == 0)
                throw new InvalidOperationException("Tutorial overlay prefab does not provide any click surfaces.");
            foreach (TutorialOverlayClickSurface surface in _clickSurfaces) surface.Bind(OnOverlayClicked);
            Canvas.willRenderCanvases += RefreshTrackedLayout;
        }

        public void Show(TutorialStepDefinition step, int stepIndex, int stepCount, Action advance)
        {
            _instance.SetActive(true);
            _step = step;
            _advance = advance;
            StopPresentation(complete: false);
            _presentationReady = false;
            _message.text = step.Message;
            _message.maxVisibleCharacters = 0;
            _mascot.sprite = LoadMascot(step.Pose);
            _mascot.enabled = _mascot.sprite != null;
            Canvas.ForceUpdateCanvases();
            Vector2Int canvasSize = GetCanvasSize();
            bool hasHole = TryResolveHole(step.Anchors, out Rect hole);
            if (hasHole) ApplyHole(hole);
            else ApplyFallback(ShouldBlockFallback());
            PositionTip(hole, hasHole);
            _lastHadHole = hasHole;
            _lastHole = hole;
            _lastScreenSize = canvasSize;
            PlayPresentation();
        }

        private void RefreshTrackedLayout()
        {
            if (_step == null || _instance == null || !_instance.activeInHierarchy) return;
            bool hasHole = TryResolveHole(_step.Anchors, out Rect hole);
            Vector2Int screen = GetCanvasSize();
            if (screen == _lastScreenSize && hasHole == _lastHadHole && (!hasHole || Approximately(hole, _lastHole))) return;
            if (hasHole) ApplyHole(hole);
            else ApplyFallback(ShouldBlockFallback());
            PositionTip(hole, hasHole);
            _lastHadHole = hasHole;
            _lastHole = hole;
            _lastScreenSize = screen;
        }

        private static bool Approximately(Rect a, Rect b) =>
            Mathf.Abs(a.x - b.x) < 0.5f && Mathf.Abs(a.y - b.y) < 0.5f
            && Mathf.Abs(a.width - b.width) < 0.5f && Mathf.Abs(a.height - b.height) < 0.5f;

        public void Hide()
        {
            StopPresentation(complete: false);
            if (_instance != null) _instance.SetActive(false);
        }

        public void Dispose()
        {
            Canvas.willRenderCanvases -= RefreshTrackedLayout;
            StopPresentation(complete: false);
            foreach (TutorialOverlayClickSurface surface in _clickSurfaces) surface?.Unbind();
            if (_instance != null) UnityEngine.Object.Destroy(_instance);
        }

        private void OnOverlayClicked()
        {
            if (!_presentationReady)
            {
                CompletePresentationImmediately();
                return;
            }

            if (_step?.Mode == TutorialAdvanceMode.Continue) _advance?.Invoke();
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
                Canvas targetCanvas = target.GetComponentInParent<Canvas>();
                Camera targetCamera = targetCanvas != null && targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? targetCanvas.worldCamera
                    : null;
                Canvas overlayCanvas = _root.GetComponentInParent<Canvas>();
                Camera overlayCamera = overlayCanvas != null && overlayCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? overlayCanvas.worldCamera
                    : null;
                Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
                Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
                for (int i = 0; i < corners.Length; i++)
                {
                    Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(targetCamera, corners[i]);
                    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            _root,
                            screenPoint,
                            overlayCamera,
                            out Vector2 localPoint))
                        continue;
                    localPoint -= _root.rect.min;
                    min = Vector2.Min(min, localPoint);
                    max = Vector2.Max(max, localPoint);
                }

                if (float.IsInfinity(min.x) || float.IsInfinity(min.y)) continue;
                Rect rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
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
                Vector2Int canvasSize = GetCanvasSize();
                result = Rect.MinMaxRect(
                    Mathf.Clamp(result.xMin - padding, 0f, canvasSize.x),
                    Mathf.Clamp(result.yMin - padding, 0f, canvasSize.y),
                    Mathf.Clamp(result.xMax + padding, 0f, canvasSize.x),
                    Mathf.Clamp(result.yMax + padding, 0f, canvasSize.y));
            }
            return any;
        }

        private void ApplyHole(Rect hole)
        {
            Vector2Int canvasSize = GetCanvasSize();
            SetMask(_masks[0].rectTransform, 0f, 0f, hole.xMin, canvasSize.y);
            SetMask(_masks[1].rectTransform, hole.xMax, 0f, canvasSize.x, canvasSize.y);
            SetMask(_masks[2].rectTransform, hole.xMin, 0f, hole.xMax, hole.yMin);
            SetMask(_masks[3].rectTransform, hole.xMin, hole.yMax, hole.xMax, canvasSize.y);
            bool blockHole = !_presentationReady || !_step.AllowTargetInteraction;
            _holeBlocker.gameObject.SetActive(blockHole);
            if (blockHole)
                SetMask(_holeBlocker.rectTransform, hole.xMin, hole.yMin, hole.xMax, hole.yMax);
        }

        private void ApplyFallback(bool blockInput)
        {
            for (int i = 0; i < _masks.Length; i++) _masks[i].gameObject.SetActive(i == 0 && blockInput);
            Vector2Int canvasSize = GetCanvasSize();
            if (blockInput) SetMask(_masks[0].rectTransform, 0f, 0f, canvasSize.x, canvasSize.y);
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

        private bool ShouldBlockFallback() =>
            !_presentationReady || _step == null || !_step.AllowTargetInteraction;

        private void PositionTip(Rect hole, bool hasHole)
        {
            Vector2Int canvasSize = GetCanvasSize();
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(_tip);
            float naturalWidth = Mathf.Max(1f, bounds.size.x);
            float referenceScale = Mathf.Max(0.62f, canvasSize.y / 1080f);
            float widthFitScale = Mathf.Max(0.62f, (canvasSize.x - LayoutMargin * 2f) / naturalWidth);
            float scale = DialogueBaseScale * Mathf.Min(referenceScale, widthFitScale);
            _tip.localScale = Vector3.one * scale;
            _tip.anchorMin = _tip.anchorMax = Vector2.zero;

            Vector2 scaledMin = (Vector2)bounds.min * scale;
            Vector2 scaledMax = (Vector2)bounds.max * scale;
            Vector2 scaledCenter = (Vector2)bounds.center * scale;
            if (!hasHole)
            {
                Vector2 centered = new Vector2(canvasSize.x * 0.5f, canvasSize.y * 0.5f) - scaledCenter;
                _tip.anchoredPosition = ClampTipPosition(centered, scaledMin, scaledMax, canvasSize);
                return;
            }

            Vector2[] candidates =
            {
                new(hole.center.x - scaledCenter.x, hole.yMax + HoleGap - scaledMin.y),
                new(hole.center.x - scaledCenter.x, hole.yMin - HoleGap - scaledMax.y),
                new(hole.xMax + HoleGap - scaledMin.x, hole.center.y - scaledCenter.y),
                new(hole.xMin - HoleGap - scaledMax.x, hole.center.y - scaledCenter.y),
            };

            Vector2 best = ClampTipPosition(candidates[0], scaledMin, scaledMax, canvasSize);
            float bestOverlap = OverlapArea(TipRect(best, scaledMin, scaledMax), hole);
            for (int i = 1; i < candidates.Length && bestOverlap > 0.01f; i++)
            {
                Vector2 candidate = ClampTipPosition(candidates[i], scaledMin, scaledMax, canvasSize);
                float overlap = OverlapArea(TipRect(candidate, scaledMin, scaledMax), hole);
                if (overlap < bestOverlap)
                {
                    best = candidate;
                    bestOverlap = overlap;
                }
            }

            _tip.anchoredPosition = best;
        }

        private static Vector2 ClampTipPosition(
            Vector2 position,
            Vector2 scaledMin,
            Vector2 scaledMax,
            Vector2Int canvasSize)
        {
            float minX = LayoutMargin - scaledMin.x;
            float maxX = canvasSize.x - LayoutMargin - scaledMax.x;
            float minY = LayoutMargin - scaledMin.y;
            float maxY = canvasSize.y - LayoutMargin - scaledMax.y;
            return new Vector2(
                minX <= maxX ? Mathf.Clamp(position.x, minX, maxX) : canvasSize.x * 0.5f,
                minY <= maxY ? Mathf.Clamp(position.y, minY, maxY) : canvasSize.y * 0.5f);
        }

        private static Rect TipRect(Vector2 position, Vector2 scaledMin, Vector2 scaledMax) =>
            Rect.MinMaxRect(
                position.x + scaledMin.x,
                position.y + scaledMin.y,
                position.x + scaledMax.x,
                position.y + scaledMax.y);

        private static float OverlapArea(Rect a, Rect b)
        {
            float width = Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin));
            float height = Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
            return width * height;
        }

        private void PlayPresentation()
        {
            _tipCanvasGroup.alpha = 0f;
            _dialoguePanel.localScale = _dialoguePanelBaseScale * 0.9f;
            _mascot.rectTransform.localScale = _mascotBaseScale * 0.9f;
            _message.ForceMeshUpdate();
            int characterCount = _message.textInfo.characterCount;
            float typingDuration = characterCount / CharactersPerSecond;

            Sequence sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetTarget(_instance)
                .Append(_tipCanvasGroup.DOFade(1f, RevealDuration).SetEase(Ease.OutQuad))
                .Join(_dialoguePanel.DOScale(_dialoguePanelBaseScale, RevealDuration).SetEase(Ease.OutBack))
                .Join(_mascot.rectTransform.DOScale(_mascotBaseScale, RevealDuration).SetEase(Ease.OutBack));
            if (characterCount > 0)
            {
                sequence.Append(DOVirtual.Int(
                        0,
                        characterCount,
                        Mathf.Max(0.05f, typingDuration),
                        value => _message.maxVisibleCharacters = value)
                    .SetEase(Ease.Linear));
            }

            _presentationTween = sequence.OnComplete(MarkPresentationReady);
        }

        private void CompletePresentationImmediately()
        {
            StopPresentation(complete: false);
            MarkPresentationReady();
        }

        private void MarkPresentationReady()
        {
            _presentationTween = null;
            _tipCanvasGroup.alpha = 1f;
            _dialoguePanel.localScale = _dialoguePanelBaseScale;
            _mascot.rectTransform.localScale = _mascotBaseScale;
            _message.maxVisibleCharacters = int.MaxValue;
            _presentationReady = true;
            if (_lastHadHole) ApplyHole(_lastHole);
            else ApplyFallback(ShouldBlockFallback());
        }

        private void StopPresentation(bool complete)
        {
            if (_presentationTween == null) return;
            _presentationTween.Kill(complete);
            _presentationTween = null;
        }

        private Vector2Int GetCanvasSize()
        {
            Rect rect = _root.rect;
            int width = Mathf.RoundToInt(rect.width);
            int height = Mathf.RoundToInt(rect.height);
            return new Vector2Int(width > 0 ? width : Screen.width, height > 0 ? height : Screen.height);
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

        private static Transform RequireTransform(Transform root, string path)
        {
            Transform result = string.IsNullOrEmpty(path) ? root : root.Find(path);
            if (result == null) throw new InvalidOperationException($"Tutorial overlay prefab is missing '{path}'.");
            return result;
        }

        private static T Require<T>(Transform root, string path) where T : Component
        {
            Transform target = RequireTransform(root, path);
            T component = target.GetComponent<T>();
            if (component == null)
                throw new InvalidOperationException($"Tutorial overlay prefab node '{path}' is missing {typeof(T).Name}.");
            return component;
        }
    }
}
