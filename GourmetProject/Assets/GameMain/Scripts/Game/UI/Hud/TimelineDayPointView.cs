using System;
using System.Globalization;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 时间轴上的单个整数日期点：命中区 + 圆点 + 天序号。
    /// 结构与样式在 TimelineDayPointView.prefab 上授权，代码只驱动轴向位置、已过/高亮状态与推进脉冲。
    /// </summary>
    [RequireComponent(typeof(RectTransform), typeof(TimelineAxisPointerTarget))]
    public sealed class TimelineDayPointView : MonoBehaviour
    {
        [Header("Prefab 引用")]
        [SerializeField] private RectTransform _rect;
        [SerializeField] private Graphic _hitArea;
        [SerializeField] private Image _dot;
        [SerializeField] private TMP_Text _dayLabel;
        [SerializeField] private TimelineAxisPointerTarget _pointer;

        [Header("圆点样式")]
        [SerializeField] private Color _passedColor = new Color(0.55f, 0.85f, 0.45f, 0.85f);
        [SerializeField] private Color _futureColor = new Color(0.15f, 0.12f, 0.08f, 0.35f);
        [SerializeField] private Color _highlightColor = new Color(0.02f, 0.86f, 0.67f, 1f);
        [Tooltip("选点高亮时圆点相对 Prefab 尺寸的放大倍率")]
        [SerializeField] private float _highlightSizeScale = 1.5f;

        [Header("推进脉冲")]
        [SerializeField] private Vector3 _pulseStrength = new Vector3(0.28f, -0.18f, 0f);
        [SerializeField] private float _pulseDuration = 0.12f;
        [SerializeField] private int _pulseVibrato = 4;
        [SerializeField] private float _pulseElasticity = 0.45f;

        private Vector2 _authoredDotSize;
        private bool _dotSizeCaptured;
        private bool _passed;
        private bool _highlighted;

        public int Day { get; private set; }

        public bool IsAnimating => _dot != null && DOTween.IsTweening(_dot.rectTransform);

        private void Awake()
        {
            EnsureRefs();
        }

        /// <summary>绑定日期与轴向位置。天 0 只显示圆点，不显示序号。</summary>
        public void Bind(int day, float axisX)
        {
            EnsureRefs();
            Day = day;
            gameObject.name = $"DayPoint_{day}";
            if (_dayLabel != null)
            {
                bool hasLabel = day > 0;
                _dayLabel.gameObject.SetActive(hasLabel);
                if (hasLabel)
                {
                    _dayLabel.text = day.ToString(CultureInfo.InvariantCulture);
                }
            }

            SetAxisPosition(axisX);
            SetHighlighted(false);
        }

        public void SetAxisPosition(float axisX)
        {
            EnsureRefs();
            if (_rect == null)
            {
                return;
            }

            axisX = Mathf.Clamp01(axisX);
            _rect.anchorMin = new Vector2(axisX, _rect.anchorMin.y);
            _rect.anchorMax = new Vector2(axisX, _rect.anchorMax.y);
        }

        public void SetPassed(bool passed)
        {
            _passed = passed;
            ApplyDotVisual();
        }

        public void SetHighlighted(bool highlighted)
        {
            _highlighted = highlighted;
            ApplyDotVisual();
        }

        /// <summary>配置加天选点交互；非目标日期不接收射线，也不保留回调。</summary>
        public void ConfigureAddDayTarget(
            bool interactive,
            Action entered,
            Action exited,
            Action clicked)
        {
            EnsureRefs();
            if (_hitArea != null)
            {
                _hitArea.raycastTarget = interactive;
            }

            if (_pointer == null)
            {
                return;
            }

            if (!interactive)
            {
                _pointer.Bind(null, null, null);
                return;
            }

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

        /// <summary>时间游标跨过该日期时的一次性脉冲。</summary>
        public void PlayAdvancePulse(float speed)
        {
            EnsureRefs();
            if (_dot == null)
            {
                return;
            }

            RectTransform dot = _dot.rectTransform;
            dot.DOKill();
            dot.localScale = Vector3.one;
            dot.DOPunchScale(
                    _pulseStrength,
                    _pulseDuration / Mathf.Max(0.05f, speed),
                    _pulseVibrato,
                    _pulseElasticity)
                .SetUpdate(true)
                .SetTarget(dot);
        }

        public void KillTweens()
        {
            if (_dot != null)
            {
                _dot.rectTransform.DOKill(complete: false);
                _dot.rectTransform.localScale = Vector3.one;
            }
        }

        private void OnDestroy()
        {
            KillTweens();
        }

        private void ApplyDotVisual()
        {
            EnsureRefs();
            if (_dot == null)
            {
                return;
            }

            _dot.color = _highlighted
                ? _highlightColor
                : (_passed ? _passedColor : _futureColor);
            _dot.rectTransform.sizeDelta = _highlighted
                ? _authoredDotSize * Mathf.Max(0.01f, _highlightSizeScale)
                : _authoredDotSize;
        }

        private void EnsureRefs()
        {
            _rect ??= transform as RectTransform;
            _pointer ??= GetComponent<TimelineAxisPointerTarget>();
            _hitArea ??= GetComponent<Graphic>();
            if (_dot != null && !_dotSizeCaptured)
            {
                _authoredDotSize = _dot.rectTransform.sizeDelta;
                _dotSizeCaptured = true;
            }
        }
    }
}
