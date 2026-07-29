using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>行动轴节点的 Prefab 视图：图标、程序化尾巴与选择态描边。</summary>
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
        private Color _tailColor = NormalFill;
        private Color _boundOutlineColor = NormalOutline;
        private Vector2 _boundOutlineDistance = new Vector2(1f, -1f);
        private float _boundAlpha = 1f;
        private bool _pulse;
        private bool _removing;
        private Tween _layoutTween;
        private Tween _visibilityTween;

        public RectTransform Rect => _rect != null ? _rect : transform as RectTransform;

        private void Awake()
        {
            EnsureRefs();
            _authoredScale = Rect.localScale;
        }

        public void Bind(Sprite icon, bool completed, bool boss, bool preview, bool negative = false)
        {
            EnsureRefs();
            _icon.sprite = icon;
            _icon.enabled = icon != null;

            _tailColor = preview
                ? PreviewFill
                : (completed ? CompletedFill : (negative ? NegativeFill : NormalFill));
            _boundAlpha = completed && !preview ? 0.74f : 1f;
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
            if (!_removing)
            {
                Rect.localScale = _authoredScale;
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
            Bind(icon, completed, boss, preview, negative: false);
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
            bool animate)
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
                        Rect.anchoredPosition = Vector2.LerpUnclamped(startPosition, position, value);
                        Rect.localRotation = Quaternion.Euler(
                            0f,
                            0f,
                            Mathf.LerpUnclamped(startAngle, targetAngle, value));
                        RefreshTail();
                    },
                    1f,
                    0.20f)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .SetTarget(Rect);
        }

        public void PlayEnter()
        {
            EnsureRefs();
            _visibilityTween?.Kill();
            _removing = false;
            _canvasGroup.alpha = 0f;
            Rect.localScale = _authoredScale * 0.75f;
            float progress = 0f;
            _visibilityTween = DOTween.To(
                    () => progress,
                    value =>
                    {
                        progress = value;
                        _canvasGroup.alpha = value;
                        Rect.localScale = Vector3.LerpUnclamped(
                            _authoredScale * 0.75f,
                            _authoredScale,
                            value);
                    },
                    1f,
                    0.16f)
                .SetEase(Ease.OutBack)
                .SetUpdate(true)
                .SetTarget(_canvasGroup);
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
                            _authoredScale * 0.65f,
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
                    Rect.localScale = _authoredScale;
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
                Rect.localScale = _authoredScale;
            }
        }

        private void Update()
        {
            if (!_pulse || _removing)
            {
                return;
            }

            float scale = 1f + Mathf.Sin(Time.unscaledTime * 7f) * 0.035f;
            Rect.localScale = _authoredScale * scale;
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
