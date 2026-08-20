using System;
using System.Globalization;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>池化的整数日期刻度；自身直接处理指针事件，不依赖运行时补组件。</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class TimelineDayPointView : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerClickHandler
    {
        [SerializeField] private RectTransform _rect;
        [SerializeField] private Graphic _hitArea;
        [SerializeField] private Image _tick;
        [SerializeField] private TMP_Text _dayLabel;

        private TimelineAxisTheme _theme;
        private Action _entered;
        private Action _exited;
        private Action _clicked;
        private Vector2 _authoredTickSize;
        private bool _captured;
        private bool _passed;
        private bool _highlighted;

        public int Day { get; private set; }
        public bool IsAnimating => _tick != null && DOTween.IsTweening(_tick.rectTransform);

        public void Initialize(TimelineAxisTheme theme)
        {
            EnsureRefs();
            _theme = theme;
            if (_tick != null && theme != null)
            {
                _tick.sprite = theme.Tick;
                _tick.type = Image.Type.Simple;
            }

            ApplyVisual();
        }

        public void Bind(int day, float axisX)
        {
            EnsureRefs();
            Day = day;
            gameObject.name = $"DayPoint_{day}";
            if (_dayLabel != null)
            {
                _dayLabel.gameObject.SetActive(day > 0);
                _dayLabel.text = day.ToString(CultureInfo.InvariantCulture);
                if (_theme != null)
                {
                    _dayLabel.color = _theme.Palette.Ink;
                }
            }

            SetAxisPosition(axisX);
            SetHighlighted(false);
        }

        public void SetAxisPosition(float axisX)
        {
            EnsureRefs();
            axisX = Mathf.Clamp01(axisX);
            _rect.anchorMin = new Vector2(axisX, _rect.anchorMin.y);
            _rect.anchorMax = new Vector2(axisX, _rect.anchorMax.y);
        }

        public void SetPassed(bool passed)
        {
            _passed = passed;
            ApplyVisual();
        }

        public void SetHighlighted(bool highlighted)
        {
            _highlighted = highlighted;
            ApplyVisual();
        }

        public void ConfigureAddDayTarget(bool interactive, Action entered, Action exited, Action clicked)
        {
            EnsureRefs();
            _entered = interactive ? entered : null;
            _exited = interactive ? exited : null;
            _clicked = interactive ? clicked : null;
            if (_hitArea != null)
            {
                _hitArea.raycastTarget = interactive;
            }
        }

        public void PlayAdvancePulse(float speed)
        {
            EnsureRefs();
            if (_tick == null)
            {
                return;
            }

            RectTransform tick = _tick.rectTransform;
            tick.DOKill();
            tick.localScale = Vector3.one;
            tick.DOPunchScale(new Vector3(0.24f, -0.14f, 0f),
                    0.16f / Mathf.Max(0.05f, speed), 4, 0.45f)
                .SetUpdate(true)
                .SetTarget(tick);
        }

        public void ResetForPool()
        {
            KillTweens();
            ConfigureAddDayTarget(false, null, null, null);
            _highlighted = false;
            _passed = false;
            ApplyVisual();
        }

        public void KillTweens()
        {
            if (_tick == null)
            {
                return;
            }

            _tick.rectTransform.DOKill(complete: false);
            _tick.rectTransform.localScale = Vector3.one;
        }

        public void OnPointerEnter(PointerEventData eventData) => _entered?.Invoke();
        public void OnPointerExit(PointerEventData eventData) => _exited?.Invoke();

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                _clicked?.Invoke();
            }
        }

        private void ApplyVisual()
        {
            EnsureRefs();
            if (_tick == null)
            {
                return;
            }

            TimelineAxisPalette palette = _theme?.Palette;
            _tick.color = _highlighted
                ? (palette?.Preview ?? new Color32(142, 216, 182, 255))
                : (_passed
                    ? (palette?.Sage ?? new Color32(139, 191, 122, 255))
                    : (palette?.Future ?? new Color32(91, 57, 38, 90)));
            _tick.rectTransform.sizeDelta = _highlighted
                ? _authoredTickSize * 1.35f
                : _authoredTickSize;
        }

        private void EnsureRefs()
        {
            _rect ??= transform as RectTransform;
            _hitArea ??= GetComponent<Graphic>();
            if (_tick != null && !_captured)
            {
                _authoredTickSize = _tick.rectTransform.sizeDelta;
                _captured = true;
            }
        }

        private void OnDestroy() => KillTweens();
    }
}
