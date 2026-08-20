using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 池化节点气泡。所有结构由 Prefab 预制，代码只切换状态、驱动 Tween 和更新尾巴。
    /// </summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    public sealed class TimelineNodeBubbleView : MonoBehaviour
    {
        private const float CompletedScale = 0.84f;

        [SerializeField] private RectTransform _rect;
        [SerializeField] private Graphic _hitArea;
        [SerializeField] private TimelineNodeTailGraphic _tail;
        [SerializeField] private Image _shell;
        [SerializeField] private Image _stateRing;
        [SerializeField] private Image _icon;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private TMP_Text _skipStamp;

        private TimelineAxisTheme _theme;
        private RectTransform _dayAnchor;
        private Vector3 _authoredScale = Vector3.one;
        private Vector3 _boundScale = Vector3.one;
        private Color _tailColor = Color.white;
        private float _boundAlpha = 1f;
        private bool _executing;
        private bool _preview;
        private bool _removing;
        private Tween _layoutTween;
        private Tween _visibilityTween;
        private Tween _stateTween;

        public RectTransform Rect => _rect != null ? _rect : transform as RectTransform;
        public bool IsAnimating => (_layoutTween?.IsActive() ?? false)
            || (_visibilityTween?.IsActive() ?? false);

        public void Initialize(TimelineAxisTheme theme)
        {
            EnsureRefs();
            _theme = theme;
            _authoredScale = Rect.localScale;
            _boundScale = _authoredScale;
            if (theme != null)
            {
                if (_shell != null)
                {
                    _shell.sprite = theme.NodeBubble;
                }

                if (_stateRing != null)
                {
                    _stateRing.sprite = theme.NodeBubble;
                }
            }

            if (_skipStamp != null)
            {
                _skipStamp.gameObject.SetActive(false);
            }
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
            TimelineAxisPalette palette = _theme?.Palette ?? new TimelineAxisPalette();
            _preview = preview;
            _executing = executing && !completed && !preview;
            _removing = false;
            _boundAlpha = completed && !preview ? 0.72f : 1f;
            _boundScale = _authoredScale * (completed && !preview ? CompletedScale : 1f);
            _tailColor = preview
                ? palette.Preview
                : (negative ? new Color(palette.Danger.r, palette.Danger.g, palette.Danger.b, 0.78f) : palette.Cream);

            if (_icon != null)
            {
                _icon.sprite = icon;
                _icon.enabled = icon != null;
                _icon.color = completed && !preview
                    ? new Color(0.55f, 0.55f, 0.52f, 0.82f)
                    : Color.white;
            }

            if (_shell != null)
            {
                _shell.color = _tailColor;
            }

            if (_stateRing != null)
            {
                _stateRing.enabled = preview || executing || boss || negative;
                _stateRing.color = preview
                    ? palette.Preview
                    : (negative ? palette.Danger : (executing ? palette.ExecutingGlow : palette.Apricot));
            }

            _canvasGroup.alpha = _boundAlpha;
            _canvasGroup.blocksRaycasts = !preview;
            _canvasGroup.interactable = !preview;
            if (_hitArea != null)
            {
                _hitArea.raycastTarget = !preview;
            }

            if (!_removing)
            {
                Rect.localScale = _boundScale;
            }

            if (_skipStamp != null)
            {
                _skipStamp.gameObject.SetActive(false);
            }

            RefreshStateTween();
            RefreshTail();
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
            float startAngle = Mathf.DeltaAngle(0f, Rect.localEulerAngles.z);
            float targetAngle = startAngle + Mathf.DeltaAngle(startAngle, angle);
            float progress = 0f;
            _layoutTween = DOTween.To(
                    () => progress,
                    value =>
                    {
                        progress = value;
                        Vector2 next = Vector2.LerpUnclamped(startPosition, position, value);
                        if (arc)
                        {
                            next.y += Mathf.Sin(value * Mathf.PI) * 28f;
                        }

                        Rect.anchoredPosition = next;
                        Rect.localRotation = Quaternion.Euler(
                            0f, 0f, Mathf.LerpUnclamped(startAngle, targetAngle, value));
                        RefreshTail();
                    },
                    1f,
                    (arc ? 0.34f : (_theme?.Motion.LayoutDuration ?? 0.20f))
                    / Mathf.Max(0.05f, speed))
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
            Rect.localScale = _boundScale * 0.72f;
            Sequence sequence = DOTween.Sequence().SetUpdate(true).SetTarget(_canvasGroup);
            sequence.Join(_canvasGroup.DOFade(_boundAlpha,
                (_theme?.Motion.EnterDuration ?? 0.16f) / Mathf.Max(0.05f, speed)));
            sequence.Join(Rect.DOScale(_boundScale,
                    (_theme?.Motion.EnterDuration ?? 0.16f) / Mathf.Max(0.05f, speed))
                .SetEase(Ease.OutBack));
            _visibilityTween = sequence;
        }

        public void PlayChange(Action onComplete = null, float speed = 1f, Action onMidpoint = null)
        {
            EnsureRefs();
            _visibilityTween?.Kill();
            float half = (_theme?.Motion.ChangeHalfDuration ?? 0.10f) / Mathf.Max(0.05f, speed);
            Sequence sequence = DOTween.Sequence().SetUpdate(true).SetTarget(_canvasGroup);
            sequence.Append(Rect.DOScaleX(Mathf.Max(0.04f, _boundScale.x * 0.06f), half).SetEase(Ease.InCubic));
            sequence.AppendCallback(() => onMidpoint?.Invoke());
            sequence.Append(Rect.DOScale(_boundScale, half).SetEase(Ease.OutBack));
            sequence.OnComplete(() => onComplete?.Invoke());
            _visibilityTween = sequence;
        }

        public void PlayTriggerStart(Action onComplete, float speed = 1f)
        {
            EnsureRefs();
            _visibilityTween?.Kill();
            if (_stateRing != null)
            {
                _stateRing.enabled = true;
                _stateRing.color = (_theme?.Palette ?? new TimelineAxisPalette()).ExecutingGlow;
            }

            Sequence sequence = DOTween.Sequence().SetUpdate(true).SetTarget(_canvasGroup);
            float duration = 0.34f / Mathf.Max(0.05f, speed);
            sequence.Join(Rect.DOPunchAnchorPos(new Vector2(0f, 8f), duration, 6, 0.5f));
            sequence.Join(Rect.DOPunchScale(_boundScale * 0.12f, duration, 6, 0.55f));
            sequence.OnComplete(() => onComplete?.Invoke());
            _visibilityTween = sequence;
        }

        public void PlayAdvancePulse(float speed = 1f)
        {
            if (_removing)
            {
                return;
            }

            _visibilityTween?.Kill();
            _visibilityTween = Rect.DOPunchScale(
                    _boundScale * 0.16f,
                    (_theme?.Motion.PulseDuration ?? 0.30f) / Mathf.Max(0.05f, speed),
                    5,
                    0.55f)
                .SetUpdate(true)
                .SetTarget(_canvasGroup)
                .OnComplete(() => Rect.localScale = _boundScale);
        }

        public void PlayTriggerComplete(Action onComplete, float speed = 1f)
        {
            _visibilityTween?.Kill();
            Sequence sequence = DOTween.Sequence().SetUpdate(true).SetTarget(_canvasGroup);
            sequence.Append(Rect.DOScale(_boundScale * 1.08f, 0.10f / Mathf.Max(0.05f, speed)));
            sequence.Append(Rect.DOScale(_boundScale, 0.16f / Mathf.Max(0.05f, speed)).SetEase(Ease.OutBack));
            sequence.OnComplete(() => onComplete?.Invoke());
            _visibilityTween = sequence;
        }

        public void PlayRemove(Action onComplete, float speed = 1f)
        {
            BeginExit();
            Sequence sequence = DOTween.Sequence().SetUpdate(true).SetTarget(_canvasGroup);
            float duration = (_theme?.Motion.ExitDuration ?? 0.18f) / Mathf.Max(0.05f, speed);
            sequence.Append(Rect.DOShakeAnchorPos(duration, new Vector2(6f, 0f), 12, 60f, false, true));
            sequence.Append(Rect.DOAnchorPosY(Rect.anchoredPosition.y - 22f, duration).SetEase(Ease.InCubic));
            sequence.Join(Rect.DOScale(_boundScale * 0.66f, duration));
            sequence.Join(_canvasGroup.DOFade(0f, duration));
            sequence.OnComplete(() => onComplete?.Invoke());
            _visibilityTween = sequence;
        }

        public void PlaySkip(Action onComplete, float speed = 1f)
        {
            BeginExit();
            if (_skipStamp != null)
            {
                _skipStamp.gameObject.SetActive(true);
                _skipStamp.alpha = 0f;
                _skipStamp.color = (_theme?.Palette ?? new TimelineAxisPalette()).Ink;
            }

            Sequence sequence = DOTween.Sequence().SetUpdate(true).SetTarget(_canvasGroup);
            float scale = 1f / Mathf.Max(0.05f, speed);
            if (_skipStamp != null)
            {
                _skipStamp.rectTransform.localScale = Vector3.one * 1.45f;
                sequence.Append(_skipStamp.DOFade(1f, 0.07f * scale));
                sequence.Join(_skipStamp.rectTransform.DOScale(0.94f, 0.13f * scale).SetEase(Ease.OutBack));
            }

            sequence.AppendInterval(0.10f * scale);
            sequence.Append(Rect.DOAnchorPosY(Rect.anchoredPosition.y + 24f, 0.20f * scale).SetEase(Ease.InCubic));
            sequence.Join(_canvasGroup.DOFade(0f, 0.20f * scale));
            sequence.OnComplete(() => onComplete?.Invoke());
            _visibilityTween = sequence;
        }

        public void PlayExit(Action onComplete)
        {
            BeginExit();
            Sequence sequence = DOTween.Sequence().SetUpdate(true).SetTarget(_canvasGroup);
            sequence.Join(_canvasGroup.DOFade(0f, 0.12f));
            sequence.Join(Rect.DOScale(_boundScale * 0.65f, 0.12f).SetEase(Ease.InCubic));
            sequence.OnComplete(() => onComplete?.Invoke());
            _visibilityTween = sequence;
        }

        public void SetRaycastEnabled(bool enabled)
        {
            EnsureRefs();
            _canvasGroup.blocksRaycasts = enabled;
            _canvasGroup.interactable = enabled;
            if (_hitArea != null)
            {
                _hitArea.raycastTarget = enabled;
            }
        }

        public void SetNodeTargetState(bool eligible, bool emphasized, bool destructive)
        {
            EnsureRefs();
            TimelineAxisPalette palette = _theme?.Palette ?? new TimelineAxisPalette();
            if (_stateRing != null)
            {
                _stateRing.enabled = eligible || _executing || _preview;
                _stateRing.color = destructive ? palette.Danger : palette.Preview;
                _stateRing.rectTransform.localScale = emphasized ? Vector3.one * 1.10f : Vector3.one;
            }

            if (eligible && emphasized)
            {
                StartStatePulse();
            }
            else
            {
                RefreshStateTween();
            }
        }

        public void ClearNodeTargetState()
        {
            EnsureRefs();
            if (_stateRing != null)
            {
                _stateRing.rectTransform.localScale = Vector3.one;
                _stateRing.enabled = _executing || _preview;
                _stateRing.color = _preview
                    ? (_theme?.Palette.Preview ?? Color.green)
                    : (_theme?.Palette.ExecutingGlow ?? Color.yellow);
            }

            RefreshStateTween();
        }

        public void CompletePresentation()
        {
            EnsureRefs();
            _layoutTween?.Kill(false);
            _visibilityTween?.Kill(false);
            _layoutTween = null;
            _visibilityTween = null;
            _removing = false;
            Rect.localScale = _boundScale;
            Rect.localRotation = Quaternion.identity;
            _canvasGroup.alpha = _boundAlpha;
            _canvasGroup.blocksRaycasts = !_preview;
            if (_skipStamp != null)
            {
                _skipStamp.gameObject.SetActive(false);
            }

            RefreshStateTween();
            RefreshTail();
        }

        public void ResetForPool()
        {
            _layoutTween?.Kill(false);
            _visibilityTween?.Kill(false);
            _stateTween?.Kill(false);
            _layoutTween = null;
            _visibilityTween = null;
            _stateTween = null;
            _dayAnchor = null;
            _executing = false;
            _preview = false;
            _removing = false;
            _canvasGroup.alpha = 1f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;
            Rect.localScale = _authoredScale;
            Rect.localRotation = Quaternion.identity;
            if (_stateRing != null)
            {
                _stateRing.enabled = false;
                _stateRing.rectTransform.localScale = Vector3.one;
            }

            if (_skipStamp != null)
            {
                _skipStamp.gameObject.SetActive(false);
            }
        }

        public void RefreshTail()
        {
            if (_tail == null || _dayAnchor == null)
            {
                return;
            }

            Vector3 world = _dayAnchor.TransformPoint(Vector3.zero);
            // Tail 是独立的全尺寸子 RectTransform。尖端必须换算到 Tail 自己的
            // 局部坐标，否则会多出半个气泡高度的偏移并完全藏进 Shell 后面。
            Vector3 local = _tail.rectTransform.InverseTransformPoint(world);
            _tail.SetTip(new Vector2(local.x, local.y), _tailColor);
        }

        private void BeginExit()
        {
            _layoutTween?.Kill();
            _visibilityTween?.Kill();
            _stateTween?.Kill();
            _removing = true;
            SetRaycastEnabled(false);
        }

        private void RefreshStateTween()
        {
            _stateTween?.Kill(false);
            _stateTween = null;
            if (_executing && !_removing)
            {
                StartStatePulse();
            }
        }

        private void StartStatePulse()
        {
            if (_stateRing == null || !gameObject.activeInHierarchy)
            {
                return;
            }

            _stateTween?.Kill(false);
            _stateRing.rectTransform.localScale = Vector3.one;
            _stateTween = _stateRing.rectTransform
                .DOScale(1.10f, 0.46f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true)
                .SetTarget(_stateRing);
        }

        private void EnsureRefs()
        {
            _rect ??= transform as RectTransform;
            _canvasGroup ??= GetComponent<CanvasGroup>();
            _hitArea ??= GetComponent<Graphic>();
        }

        private void OnDestroy()
        {
            _layoutTween?.Kill(false);
            _visibilityTween?.Kill(false);
            _stateTween?.Kill(false);
        }
    }
}
