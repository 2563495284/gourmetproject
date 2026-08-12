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
        private const float TipWidth = 900f;
        private const float TipHeight = 270f;
        private readonly GameObject _instance;
        private readonly RectTransform _root;
        private readonly Image[] _masks;
        private readonly Image _holeBlocker;
        private readonly RectTransform _tip;
        private readonly Image _mascot;
        private readonly TMP_Text _message;
        private readonly TutorialOverlayClickSurface[] _clickSurfaces;
        private Action _advance;
        private TutorialStepDefinition _step;
        private Rect _lastHole;
        private bool _lastHadHole;
        private Vector2Int _lastScreenSize;

        public Transform transform => _instance != null ? _instance.transform : null;

        public static TutorialOverlayView Create()
        {
            GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
            if (prefab == null)
            {
                Debug.LogError($"Tutorial overlay prefab is missing at Resources/{PrefabResourcePath}.");
                return null;
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            UnityEngine.Object.DontDestroyOnLoad(instance);
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
            _mascot = Require<Image>(instance.transform, "DangDangDialogue/DangDang");
            _message = Require<TMP_Text>(instance.transform, "DangDangDialogue/DialoguePanel/Message");
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
            _message.text = step.Message;
            _mascot.sprite = LoadMascot(step.Pose);
            _mascot.enabled = _mascot.sprite != null;
            Canvas.ForceUpdateCanvases();
            Vector2Int canvasSize = GetCanvasSize();
            bool hasHole = TryResolveHole(step.Anchors, out Rect hole);
            if (hasHole) ApplyHole(hole);
            else ApplyFallback(step.Mode == TutorialAdvanceMode.Continue);
            PositionTip(hasHole ? hole : new Rect(canvasSize.x * 0.5f, canvasSize.y * 0.5f, 0f, 0f));
            _lastHadHole = hasHole;
            _lastHole = hole;
            _lastScreenSize = canvasSize;
        }

        private void RefreshTrackedLayout()
        {
            if (_step == null || _instance == null || !_instance.activeInHierarchy) return;
            bool hasHole = TryResolveHole(_step.Anchors, out Rect hole);
            Vector2Int screen = GetCanvasSize();
            if (screen == _lastScreenSize && hasHole == _lastHadHole && (!hasHole || Approximately(hole, _lastHole))) return;
            if (hasHole) ApplyHole(hole);
            else ApplyFallback(_step.Mode == TutorialAdvanceMode.Continue);
            PositionTip(hasHole ? hole : new Rect(screen.x * 0.5f, screen.y * 0.5f, 0f, 0f));
            _lastHadHole = hasHole;
            _lastHole = hole;
            _lastScreenSize = screen;
        }

        private static bool Approximately(Rect a, Rect b) =>
            Mathf.Abs(a.x - b.x) < 0.5f && Mathf.Abs(a.y - b.y) < 0.5f
            && Mathf.Abs(a.width - b.width) < 0.5f && Mathf.Abs(a.height - b.height) < 0.5f;

        public void Hide()
        {
            if (_instance != null) _instance.SetActive(false);
        }

        public void Dispose()
        {
            Canvas.willRenderCanvases -= RefreshTrackedLayout;
            foreach (TutorialOverlayClickSurface surface in _clickSurfaces) surface?.Unbind();
            if (_instance != null) UnityEngine.Object.Destroy(_instance);
        }

        private void OnOverlayClicked()
        {
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
            Vector2Int canvasSize = GetCanvasSize();
            SetMask(_masks[0].rectTransform, 0f, 0f, hole.xMin, canvasSize.y);
            SetMask(_masks[1].rectTransform, hole.xMax, 0f, canvasSize.x, canvasSize.y);
            SetMask(_masks[2].rectTransform, hole.xMin, 0f, hole.xMax, hole.yMin);
            SetMask(_masks[3].rectTransform, hole.xMin, hole.yMax, hole.xMax, canvasSize.y);
            _holeBlocker.gameObject.SetActive(!_step.AllowTargetInteraction);
            if (!_step.AllowTargetInteraction)
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

        private void PositionTip(Rect hole)
        {
            Vector2Int canvasSize = GetCanvasSize();
            float referenceScale = Mathf.Max(0.62f, canvasSize.y / 1080f);
            float widthFitScale = Mathf.Max(0.62f, (canvasSize.x - 24f) / TipWidth);
            float scale = Mathf.Min(referenceScale, widthFitScale);
            _tip.localScale = Vector3.one * scale;
            float halfWidth = TipWidth * scale * 0.5f;
            float halfHeight = TipHeight * scale * 0.5f;
            float x = Mathf.Clamp(hole.center.x, halfWidth + 12f, canvasSize.x - halfWidth - 12f);
            float y = hole.yMin > TipHeight * scale + 28f
                ? hole.yMin - halfHeight - 18f
                : hole.yMax + halfHeight + 18f;
            y = Mathf.Clamp(y, halfHeight + 12f, canvasSize.y - halfHeight - 12f);
            _tip.anchorMin = _tip.anchorMax = Vector2.zero;
            _tip.anchoredPosition = new Vector2(x, y);
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
