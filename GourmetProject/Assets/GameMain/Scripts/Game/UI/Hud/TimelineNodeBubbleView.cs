using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>时间轴节点的 Prefab 视图：图标、程序化尾巴与选择态描边。</summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup), typeof(TimelineAxisPointerTarget))]
    public sealed class TimelineNodeBubbleView : MonoBehaviour
    {
        private static readonly Color NormalFill = new Color(1f, 0.94f, 0.80f, 0.98f);
        private static readonly Color CompletedFill = new Color(0.72f, 0.70f, 0.65f, 0.78f);
        private static readonly Color PreviewFill = new Color(0.74f, 1f, 0.92f, 0.78f);
        private static readonly Color NegativeFill = new Color(1f, 0.78f, 0.74f, 0.96f);
        private static readonly Color WarningColor = new Color(0.92f, 0.20f, 0.18f, 1f);
        private static readonly Color TargetColor = new Color(0.02f, 0.82f, 0.66f, 1f);
        private static readonly Color NormalOutline = new Color(0.29f, 0.17f, 0.08f, 0.72f);
        private static readonly Color CompletedOutline = new Color(0.30f, 0.29f, 0.27f, 0.58f);
        private static readonly Color BossOutline = new Color(1f, 0.63f, 0.08f, 0.95f);
        private static readonly Color PreviewOutline = new Color(0.02f, 0.82f, 0.66f, 0.95f);
        private static readonly Color NormalIcon = Color.white;
        private static readonly Color CompletedIcon = new Color(0.56f, 0.56f, 0.56f, 0.82f);
        private static readonly Color ExecutingGlowMin = new Color(1f, 0.62f, 0.08f, 0.34f);
        private static readonly Color ExecutingGlowMax = new Color(1f, 0.84f, 0.32f, 0.82f);
        private const float CompletedScale = 0.82f;

        [Header("Prefab 引用")]
        [SerializeField] private RectTransform _rect;
        [SerializeField] private Graphic _hitArea;
        [SerializeField] private Image _tail;
        [SerializeField] private TimelineNodeTailGraphic _tailGraphic;
        [SerializeField] private Image _icon;
        [SerializeField] private Outline _outline;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private TimelineAxisPointerTarget _pointer;

        private RectTransform _dayAnchor;
        private Vector3 _authoredScale = Vector3.one;
        private Vector3 _boundScale = Vector3.one;
        private Color _tailColor = NormalFill;
        private Color _boundOutlineColor = NormalOutline;
        private Vector2 _boundOutlineDistance = new Vector2(1f, -1f);
        private float _boundAlpha = 1f;
        private bool _pulse;
        private bool _executing;
        private bool _removing;
        private Outline _executingGlow;
        private Tween _layoutTween;
        private Tween _visibilityTween;
        private TMP_Text _skipStamp;

        public RectTransform Rect => _rect != null ? _rect : transform as RectTransform;

        public bool IsAnimating => (_layoutTween?.IsActive() ?? false)
            || (_visibilityTween?.IsActive() ?? false);

        private void Awake()
        {
            EnsureRefs();
            _authoredScale = Rect.localScale;
            _boundScale = _authoredScale;
        }

        public void Bind(
            Sprite icon,
            bool completed,
            bool executing,
            bool boss,
            bool preview,
            bool negative = false)
        {
            EnsureRefs();
            _icon.sprite = icon;
            _icon.enabled = icon != null;
            _icon.color = completed && !preview ? CompletedIcon : NormalIcon;

            _tailColor = preview
                ? PreviewFill
                : (completed ? CompletedFill : (negative ? NegativeFill : NormalFill));
            _boundAlpha = completed && !preview ? 0.74f : 1f;
            _boundScale = _authoredScale * (completed && !preview ? CompletedScale : 1f);
            _executing = executing && !completed && !preview;
            _canvasGroup.alpha = _boundAlpha;
            _canvasGroup.blocksRaycasts = !preview;
            _hitArea.raycastTarget = !preview;
            _outline.enabled = true;
            _boundOutlineColor = preview
                ? PreviewOutline
                : (boss
                    ? BossOutline
                    : (completed ? CompletedOutline : (negative ? WarningColor : NormalOutline)));
            _boundOutlineDistance =
                preview || boss ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
            _outline.effectColor = _boundOutlineColor;
            _outline.effectDistance = _boundOutlineDistance;
            _executingGlow.enabled = _executing;
            if (_executing)
            {
                _executingGlow.effectColor = ExecutingGlowMax;
                _executingGlow.effectDistance = new Vector2(4f, -4f);
            }

            if (!_removing)
            {
                Rect.localScale = _boundScale;
            }

            RefreshTail();
        }

        /// <summary>兼容旧调用；新布局由 TimelineDayNodeGroupView 接管。</summary>
        public void Bind(
            Sprite icon,
            bool completed,
            bool boss,
            bool preview,
            float x,
            int stackIndex)
        {
            Bind(icon, completed, executing: false, boss, preview, negative: false);
            float axisX = Mathf.Clamp01(x);
            Rect.anchorMin = new Vector2(axisX, 0.48f);
            Rect.anchorMax = new Vector2(axisX, 0.48f);
            Rect.pivot = new Vector2(0.5f, 0f);
            Rect.anchoredPosition = new Vector2(0f, 20f + stackIndex * 43f);
        }

        public void SetLayout(
            RectTransform dayAnchor,
            Vector2 position,
            float angle,
            bool animate,
            bool arc = false,
            float speed = 1f)
        {
            EnsureRefs();
            _dayAnchor = dayAnchor;
            Rect.anchorMin = new Vector2(0.5f, 0f);
            Rect.anchorMax = new Vector2(0.5f, 0f);
            Rect.pivot = new Vector2(0.5f, 0f);

            _layoutTween?.Kill();
            if (!animate || !gameObject.activeInHierarchy)
            {
                Rect.anchoredPosition = position;
                Rect.localRotation = Quaternion.Euler(0f, 0f, angle);
                RefreshTail();
                return;
            }

            Vector2 startPosition = Rect.anchoredPosition;
            float startAngle = NormalizeAngle(Rect.localEulerAngles.z);
            float targetAngle = Mathf.DeltaAngle(startAngle, angle) + startAngle;
            float progress = 0f;
            _layoutTween = DOTween.To(
                    () => progress,
                    value =>
                    {
                        progress = value;
                        Vector2 next = Vector2.LerpUnclamped(startPosition, position, value);
                        if (arc)
                        {
                            next.y += Mathf.Sin(value * Mathf.PI) * 30f;
                            float landing = value > 0.78f
                                ? Mathf.Sin((value - 0.78f) / 0.22f * Mathf.PI) * 0.07f
                                : 0f;
                            Rect.localScale = _boundScale * (1f + landing);
                        }

                        Rect.anchoredPosition = next;
                        Rect.localRotation = Quaternion.Euler(
                            0f,
                            0f,
                            Mathf.LerpUnclamped(startAngle, targetAngle, value));
                        RefreshTail();
                    },
                    1f,
                    (arc ? 0.34f : 0.20f) / Mathf.Max(0.05f, speed))
                .SetEase(arc ? Ease.InOutSine : Ease.OutCubic)
                .SetUpdate(true)
                .SetTarget(Rect)
                .OnComplete(() => Rect.localScale = _boundScale);
        }

        public void PlayEnter(float speed = 1f)
        {
            EnsureRefs();
            _visibilityTween?.Kill();
            _removing = false;
            _canvasGroup.alpha = 0f;
            Rect.localScale = _boundScale * 0.75f;
            float progress = 0f;
            _visibilityTween = DOTween.To(
                    () => progress,
                    value =>
                    {
                        progress = value;
                        _canvasGroup.alpha = value;
                        Rect.localScale = Vector3.LerpUnclamped(
                            _boundScale * 0.75f,
                            _boundScale,
                            value);
                    },
                    1f,
                    0.16f / Mathf.Max(0.05f, speed))
                .SetEase(Ease.OutBack)
                .SetUpdate(true)
                .SetTarget(_canvasGroup);
        }

        public void PlayChange(
            Action onComplete = null,
            float speed = 1f,
            Action onMidpoint = null)
        {
            EnsureRefs();
            _visibilityTween?.Kill();
            Rect.localScale = _boundScale;
            Sequence sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetTarget(_canvasGroup);
            float durationScale = 1f / Mathf.Max(0.05f, speed);
            sequence.Append(Rect.DOScaleX(Mathf.Max(0.04f, _boundScale.x * 0.06f), 0.10f * durationScale)
                .SetEase(Ease.InCubic));
            sequence.AppendCallback(() =>
            {
                onMidpoint?.Invoke();
                Rect.localScale = new Vector3(
                    Mathf.Max(0.04f, _boundScale.x * 0.06f),
                    _boundScale.y,
                    _boundScale.z);
                _outline.enabled = true;
                _outline.effectColor = NormalOutline;
                _outline.effectDistance = new Vector2(5f, -5f);
            });
            sequence.Append(Rect.DOScale(_boundScale * 1.08f, 0.12f * durationScale).SetEase(Ease.OutBack));
            sequence.Append(Rect.DOScale(_boundScale, 0.08f * durationScale).SetEase(Ease.OutCubic));
            sequence.OnComplete(() =>
            {
                Rect.localScale = _boundScale;
                _outline.effectColor = _boundOutlineColor;
                _outline.effectDistance = _boundOutlineDistance;
                onComplete?.Invoke();
            });
            _visibilityTween = sequence;
        }

        public void PlayTriggerStart(Action onComplete, float speed = 1f)
        {
            EnsureRefs();
            _visibilityTween?.Kill();
            _removing = false;
            _executingGlow.enabled = true;
            _executingGlow.effectColor = ExecutingGlowMax;
            _executingGlow.effectDistance = new Vector2(5f, -5f);
            Sequence sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetTarget(_canvasGroup);
            float durationScale = 1f / Mathf.Max(0.05f, speed);
            sequence.Join(Rect.DOPunchAnchorPos(new Vector2(0f, 8f), 0.36f * durationScale, 6, 0.50f));
            sequence.Join(Rect.DOPunchScale(_boundScale * 0.12f, 0.36f * durationScale, 6, 0.55f));
            sequence.Append(DOTween.To(
                () => 0f,
                value =>
                {
                    float pulse = Mathf.Sin(value * Mathf.PI * 2f) * 0.5f + 0.5f;
                    float distance = Mathf.Lerp(3f, 6f, pulse);
                    _executingGlow.effectDistance = new Vector2(distance, -distance);
                },
                2f,
                0.22f * durationScale));
            sequence.OnComplete(() => onComplete?.Invoke());
            _visibilityTween = sequence;
        }

        public void PlayTriggerComplete(Action onComplete, float speed = 1f)
        {
            EnsureRefs();
            _visibilityTween?.Kill();
            Sequence sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetTarget(_canvasGroup);
            float durationScale = 1f / Mathf.Max(0.05f, speed);
            sequence.Append(Rect.DOScale(_boundScale * 1.08f, 0.10f * durationScale).SetEase(Ease.OutCubic));
            sequence.Append(Rect.DOScale(_boundScale, 0.16f * durationScale).SetEase(Ease.OutBack));
            sequence.Join(_canvasGroup.DOFade(_boundAlpha, 0.16f * durationScale));
            sequence.OnComplete(() =>
            {
                _executingGlow.enabled = _executing;
                onComplete?.Invoke();
            });
            _visibilityTween = sequence;
        }

        public void PlayRemove(Action onComplete, float speed = 1f)
        {
            EnsureRefs();
            BeginSemanticExit();
            _outline.enabled = true;
            _outline.effectColor = WarningColor;
            _outline.effectDistance = new Vector2(3f, -3f);
            Sequence sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetTarget(_canvasGroup);
            float durationScale = 1f / Mathf.Max(0.05f, speed);
            sequence.Append(Rect.DOShakeAnchorPos(0.18f * durationScale, new Vector2(7f, 0f), 14, 70f, false, true));
            sequence.Append(Rect.DOAnchorPosY(Rect.anchoredPosition.y - 24f, 0.18f * durationScale).SetEase(Ease.InCubic));
            sequence.Join(Rect.DOScale(_boundScale * 0.68f, 0.18f * durationScale));
            sequence.Join(_canvasGroup.DOFade(0f, 0.18f * durationScale));
            sequence.OnComplete(() => onComplete?.Invoke());
            _visibilityTween = sequence;
        }

        public void PlaySkip(Action onComplete, float speed = 1f)
        {
            EnsureRefs();
            BeginSemanticExit();
            EnsureSkipStamp();
            _icon.color = new Color(0.64f, 0.60f, 0.49f, 0.82f);
            _outline.enabled = true;
            _outline.effectColor = new Color(0.58f, 0.47f, 0.24f, 0.95f);
            _skipStamp.rectTransform.localScale = Vector3.one * 1.8f;
            _skipStamp.alpha = 0f;
            Sequence sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetTarget(_canvasGroup);
            float durationScale = 1f / Mathf.Max(0.05f, speed);
            sequence.Append(_skipStamp.DOFade(1f, 0.06f * durationScale));
            sequence.Join(_skipStamp.rectTransform.DOScale(0.92f, 0.12f * durationScale).SetEase(Ease.OutBack));
            sequence.AppendInterval(0.12f * durationScale);
            sequence.Append(Rect.DOAnchorPosY(Rect.anchoredPosition.y + 26f, 0.20f * durationScale).SetEase(Ease.InCubic));
            sequence.Join(Rect.DOLocalRotate(new Vector3(0f, 0f, 10f), 0.20f * durationScale));
            sequence.Join(_canvasGroup.DOFade(0f, 0.20f * durationScale));
            sequence.OnComplete(() => onComplete?.Invoke());
            _visibilityTween = sequence;
        }

        public void CompletePresentation()
        {
            EnsureRefs();
            _layoutTween?.Kill(complete: false);
            _visibilityTween?.Kill(complete: false);
            _layoutTween = null;
            _visibilityTween = null;
            _removing = false;
            _pulse = false;
            _canvasGroup.alpha = _boundAlpha;
            _canvasGroup.blocksRaycasts = true;
            Rect.localScale = _boundScale;
            Rect.localRotation = Quaternion.identity;
            _outline.enabled = true;
            _outline.effectColor = _boundOutlineColor;
            _outline.effectDistance = _boundOutlineDistance;
            _executingGlow.enabled = _executing;
            if (_skipStamp != null)
            {
                Destroy(_skipStamp.gameObject);
                _skipStamp = null;
            }
        }

        private void BeginSemanticExit()
        {
            _layoutTween?.Kill();
            _visibilityTween?.Kill();
            _pulse = false;
            _removing = true;
            _canvasGroup.blocksRaycasts = false;
            _hitArea.raycastTarget = false;
        }

        private void EnsureSkipStamp()
        {
            if (_skipStamp != null)
            {
                return;
            }

            var go = new GameObject(
                "SkipStamp",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(Rect, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(72f, 30f);
            rect.anchoredPosition = new Vector2(0f, 3f);
            rect.localRotation = Quaternion.Euler(0f, 0f, -10f);
            _skipStamp = go.GetComponent<TMP_Text>();
            _skipStamp.text = "跳过";
            _skipStamp.font = TMP_Settings.defaultFontAsset;
            _skipStamp.fontSize = 20f;
            _skipStamp.fontStyle = FontStyles.Bold;
            _skipStamp.alignment = TextAlignmentOptions.Center;
            _skipStamp.color = new Color(0.43f, 0.32f, 0.14f, 1f);
            _skipStamp.raycastTarget = false;
            go.transform.SetAsLastSibling();
        }

        public void PlayExit(Action onComplete)
        {
            EnsureRefs();
            _layoutTween?.Kill();
            _visibilityTween?.Kill();
            _pulse = false;
            _removing = true;
            _canvasGroup.blocksRaycasts = false;
            _hitArea.raycastTarget = false;
            float startAlpha = _canvasGroup.alpha;
            Vector3 startScale = Rect.localScale;
            float progress = 0f;
            _visibilityTween = DOTween.To(
                    () => progress,
                    value =>
                    {
                        progress = value;
                        _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, value);
                        Rect.localScale = Vector3.LerpUnclamped(
                            startScale,
                            _boundScale * 0.65f,
                            value);
                    },
                    1f,
                    0.12f)
                .SetEase(Ease.InCubic)
                .SetUpdate(true)
                .SetTarget(_canvasGroup)
                .OnComplete(() => onComplete?.Invoke());
        }

        public void SetRaycastEnabled(bool enabled)
        {
            EnsureRefs();
            _canvasGroup.blocksRaycasts = enabled;
            _canvasGroup.interactable = enabled;
            _hitArea.raycastTarget = enabled;
        }

        public void BindPointer(Action entered, Action exited, Action clicked)
        {
            EnsureRefs();
            _pointer.Bind(
                entered,
                exited,
                data =>
                {
                    if (data.button == PointerEventData.InputButton.Left)
                    {
                        clicked?.Invoke();
                    }
                });
        }

        public void SetNodeTargetState(bool eligible, bool emphasized, bool destructive)
        {
            EnsureRefs();
            if (!eligible)
            {
                _pulse = false;
                _canvasGroup.alpha = 0.32f;
                _outline.enabled = false;
                if (!_removing)
                {
                    Rect.localScale = _boundScale;
                }

                return;
            }

            _canvasGroup.alpha = 1f;
            _pulse = emphasized;
            _outline.enabled = true;
            Color color = destructive ? WarningColor : TargetColor;
            _outline.effectColor = emphasized
                ? color
                : new Color(color.r, color.g, color.b, 0.68f);
            _outline.effectDistance = emphasized ? new Vector2(3f, -3f) : new Vector2(1f, -1f);
        }

        public void ClearNodeTargetState()
        {
            EnsureRefs();
            _pulse = false;
            _canvasGroup.alpha = _boundAlpha;
            _outline.enabled = true;
            _outline.effectColor = _boundOutlineColor;
            _outline.effectDistance = _boundOutlineDistance;
            if (!_removing)
            {
                Rect.localScale = _boundScale;
            }
        }

        private void Update()
        {
            if (_removing)
            {
                return;
            }

            if (_pulse)
            {
                float scale = 1f + Mathf.Sin(Time.unscaledTime * 7f) * 0.035f;
                Rect.localScale = _boundScale * scale;
            }

            if (_executing && _executingGlow != null)
            {
                float pulse = Mathf.Sin(Time.unscaledTime * 5f) * 0.5f + 0.5f;
                _executingGlow.effectColor =
                    Color.LerpUnclamped(ExecutingGlowMin, ExecutingGlowMax, pulse);
                float distance = Mathf.LerpUnclamped(3f, 5f, pulse);
                _executingGlow.effectDistance = new Vector2(distance, -distance);
            }
        }

        private void LateUpdate()
        {
            RefreshTail();
        }

        private void OnDestroy()
        {
            _layoutTween?.Kill();
            _visibilityTween?.Kill();
        }

        private void RefreshTail()
        {
            if (_tailGraphic == null || _dayAnchor == null)
            {
                return;
            }

            Vector3 worldTarget = _dayAnchor.TransformPoint(Vector3.zero);
            Vector2 localTarget = _tailGraphic.rectTransform.InverseTransformPoint(worldTarget);
            _tailGraphic.SetTip(localTarget, _tailColor);
        }

        private void EnsureRefs()
        {
            _rect ??= transform as RectTransform;
            _canvasGroup ??= GetComponent<CanvasGroup>();
            _pointer ??= GetComponent<TimelineAxisPointerTarget>();
            _hitArea ??= GetComponent<Graphic>();
            if (_icon != null && _outline != null && _executingGlow == null)
            {
                Outline[] outlines = _icon.GetComponents<Outline>();
                foreach (Outline outline in outlines)
                {
                    if (outline != _outline)
                    {
                        _executingGlow = outline;
                        break;
                    }
                }

                if (_executingGlow == null)
                {
                    _executingGlow = _icon.gameObject.AddComponent<Outline>();
                }

                _executingGlow.enabled = false;
            }

            if (_tail != null)
            {
                _tail.enabled = false;
                if (_tailGraphic == null)
                {
                    _tailGraphic = _tail.GetComponent<TimelineNodeTailGraphic>();
                    if (_tailGraphic == null)
                    {
                        _tailGraphic = _tail.gameObject.AddComponent<TimelineNodeTailGraphic>();
                    }
                }
            }

            if (_tailGraphic != null)
            {
                RectTransform tailRect = _tailGraphic.rectTransform;
                tailRect.anchorMin = Vector2.zero;
                tailRect.anchorMax = Vector2.one;
                tailRect.pivot = new Vector2(0.5f, 0.5f);
                tailRect.offsetMin = Vector2.zero;
                tailRect.offsetMax = Vector2.zero;
                tailRect.localRotation = Quaternion.identity;
                _tailGraphic.raycastTarget = false;
            }
        }

        private static float NormalizeAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
