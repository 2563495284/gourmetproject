using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 时间轴推进/跨周时的聚焦演出：用全屏暗幕隔离其他 HUD，并把时间轴暂时移动到屏幕中央。
    /// </summary>
    internal sealed class TimelineAxisFocusPresenter
    {
        private const float EnterDuration = 0.64f;
        private const float OldContentExitDuration = 0.42f;
        private const float OldContentExitScale = 0.82f;
        private const float WeekTitleEnterDuration = 0.2f;
        private const float WeekTitleHoldDuration = 1.2f;
        private const float WeekTitleExitDuration = 0.2f;
        private const float WeekTitleEnterScale = 0.9f;
        private const float WeekTitleExitScale = 1.05f;
        private const float HiddenSwapHoldDuration = 0.08f;
        private const float NewContentEnterDuration = 0.56f;
        private const float NewContentEnterScale = 1.08f;
        private const float PreExitDelay = 0.5f;
        private const float ExitDuration = 0.78f;
        private static readonly Color BackdropColor = new Color(0f, 0f, 0f, 0.76f);
        private static readonly Color32 WeekTitleOutlineColor = new Color32(0, 0, 0, 220);
        private const float WeekTitleOutlineWidth = 0.18f;

        private readonly RectTransform _axis;
        private readonly CanvasGroup _axisGroup;
        private readonly TMP_Text _weekTitleStyleSource;
        private RectTransform _backdropRect;
        private CanvasGroup _backdropGroup;
        private RectTransform _weekTitleRect;
        private TMP_Text _weekTitleText;
        private CanvasGroup _weekTitleGroup;
        private Tween _transition;
        private Vector2 _restPosition;
        private Vector3 _restScale;
        private float _restAlpha;
        private int _restSiblingIndex;
        private bool _restInteractable;
        private bool _restBlocksRaycasts;
        private bool _active;

        public TimelineAxisFocusPresenter(
            RectTransform axis,
            CanvasGroup axisGroup,
            TMP_Text weekTitleStyleSource = null)
        {
            _axis = axis;
            _axisGroup = axisGroup;
            _weekTitleStyleSource = weekTitleStyleSource;
        }

        public bool CanPresent =>
            _axis != null
            && _axisGroup != null
            && _axis.parent is RectTransform;

        public void Enter(Action onComplete)
        {
            if (!CanPresent)
            {
                onComplete?.Invoke();
                return;
            }

            Cancel();
            EnsureBackdrop();
            EnsureWeekTitle();
            if (_backdropRect == null || _backdropGroup == null)
            {
                onComplete?.Invoke();
                return;
            }

            _active = true;
            _restPosition = _axis.anchoredPosition;
            _restScale = _axis.localScale;
            _restSiblingIndex = _axis.GetSiblingIndex();
            if (_axisGroup != null)
            {
                _restAlpha = _axisGroup.alpha;
                _restInteractable = _axisGroup.interactable;
                _restBlocksRaycasts = _axisGroup.blocksRaycasts;
                _axisGroup.interactable = false;
                _axisGroup.blocksRaycasts = false;
            }

            _backdropRect.gameObject.SetActive(true);
            _backdropRect.SetAsLastSibling();
            _axis.SetAsLastSibling();
            _backdropGroup.alpha = 0f;
            _backdropGroup.interactable = false;
            _backdropGroup.blocksRaycasts = true;

            _transition = DOTween.Sequence()
                .SetUpdate(true)
                .SetTarget(_axis)
                .Join(_backdropGroup.DOFade(1f, EnterDuration).SetEase(Ease.OutCubic))
                .Join(_axis.DOAnchorPosY(CenteredAnchoredY(_axis), EnterDuration).SetEase(Ease.OutCubic))
                .OnComplete(() =>
                {
                    _transition = null;
                    onComplete?.Invoke();
                });
        }

        /// <summary>
        /// 在时间轴保持居中的状态下，把旧内容完整退场；只有完全透明后才替换内容，
        /// 显示新周标题，再把新内容作为一整条时间轴淡入。
        /// </summary>
        public void SwapWeekContent(int weekIndex, Action replaceContent, Action onComplete)
        {
            if (!_active || !CanPresent)
            {
                replaceContent?.Invoke();
                onComplete?.Invoke();
                return;
            }

            _transition?.Kill(complete: false);
            HideWeekTitle();
            bool showWeekTitle = EnsureWeekTitle();
            Vector3 oldExitScale = ScaledRest(OldContentExitScale);
            Vector3 newEnterScale = ScaledRest(NewContentEnterScale);
            Vector3 newContentScale = newEnterScale;
            float newContentAlpha = 0f;

            Sequence sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetTarget(_axis)
                .Append(_axis
                    .DOScale(oldExitScale, OldContentExitDuration)
                    .SetEase(Ease.InOutSine));
            if (_axisGroup != null)
            {
                sequence.Join(_axisGroup
                    .DOFade(0f, OldContentExitDuration)
                    .SetEase(Ease.InOutSine));
            }

            sequence.AppendCallback(() =>
            {
                replaceContent?.Invoke();
                _axis.localScale = newEnterScale;
                if (_axisGroup != null)
                {
                    _axisGroup.alpha = 0f;
                }

                if (showWeekTitle)
                {
                    ShowWeekTitle(weekIndex);
                }
            });

            if (showWeekTitle)
            {
                sequence.Append(_weekTitleGroup
                    .DOFade(1f, WeekTitleEnterDuration)
                    .SetEase(Ease.OutCubic));
                sequence.Join(_weekTitleRect
                    .DOScale(Vector3.one, WeekTitleEnterDuration)
                    .SetEase(Ease.OutBack));
                sequence.AppendInterval(WeekTitleHoldDuration);
                sequence.Append(_weekTitleGroup
                    .DOFade(0f, WeekTitleExitDuration)
                    .SetEase(Ease.InCubic));
                sequence.Join(_weekTitleRect
                    .DOScale(Vector3.one * WeekTitleExitScale, WeekTitleExitDuration)
                    .SetEase(Ease.InCubic));
                sequence.AppendCallback(() =>
                {
                    HideWeekTitle();
                    _axis.SetAsLastSibling();
                });
            }

            // 至少保留一个完整渲染帧的全透明状态，避免替换后同帧推进导致新轴首帧已经可见。
            sequence.AppendInterval(HiddenSwapHoldDuration);
            sequence.Append(DOTween.To(
                    () => newContentScale,
                    value =>
                    {
                        newContentScale = value;
                        _axis.localScale = value;
                    },
                    _restScale,
                    NewContentEnterDuration)
                .SetEase(Ease.InOutSine));
            if (_axisGroup != null)
            {
                sequence.Join(DOTween.To(
                        () => newContentAlpha,
                        value =>
                        {
                            newContentAlpha = value;
                            _axisGroup.alpha = value;
                        },
                        _restAlpha,
                        NewContentEnterDuration)
                    .SetEase(Ease.InOutSine));
            }

            _transition = sequence.OnComplete(() =>
            {
                _transition = null;
                onComplete?.Invoke();
            });
        }

        public void Exit(Action onComplete)
        {
            if (!_active)
            {
                onComplete?.Invoke();
                return;
            }

            _transition?.Kill(complete: false);
            HideWeekTitle();
            _axis.SetAsLastSibling();
            _transition = DOTween.Sequence()
                .SetUpdate(true)
                .SetTarget(_axis)
                .AppendInterval(PreExitDelay)
                .Append(_backdropGroup.DOFade(0f, ExitDuration).SetEase(Ease.InCubic))
                .Join(_axis.DOAnchorPos(_restPosition, ExitDuration).SetEase(Ease.InOutCubic))
                .OnComplete(() =>
                {
                    _transition = null;
                    RestoreImmediately();
                    onComplete?.Invoke();
                });
        }

        public void Cancel()
        {
            _transition?.Kill(complete: false);
            _transition = null;
            HideWeekTitle();
            if (_active)
            {
                RestoreImmediately();
            }
        }

        internal static float CenteredAnchoredY(RectTransform axis)
        {
            if (axis == null || !(axis.parent is RectTransform parent))
            {
                return axis != null ? axis.anchoredPosition.y : 0f;
            }

            float anchorY = (axis.anchorMin.y + axis.anchorMax.y) * 0.5f;
            float anchorReferenceY = Mathf.Lerp(parent.rect.yMin, parent.rect.yMax, anchorY);
            return parent.rect.center.y - anchorReferenceY;
        }

        private void EnsureBackdrop()
        {
            if (_backdropRect != null && _backdropRect.parent == _axis.parent)
            {
                return;
            }

            var backdrop = new GameObject(
                "TimelineAxisFocusBackdrop",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            _backdropRect = backdrop.GetComponent<RectTransform>();
            _backdropRect.SetParent(_axis.parent, false);
            _backdropRect.anchorMin = Vector2.zero;
            _backdropRect.anchorMax = Vector2.one;
            _backdropRect.offsetMin = Vector2.zero;
            _backdropRect.offsetMax = Vector2.zero;
            _backdropRect.pivot = new Vector2(0.5f, 0.5f);

            Image image = backdrop.GetComponent<Image>();
            image.color = BackdropColor;
            image.raycastTarget = true;

            _backdropGroup = backdrop.GetComponent<CanvasGroup>();
            _backdropGroup.alpha = 0f;
            _backdropGroup.interactable = false;
            _backdropGroup.blocksRaycasts = false;
            backdrop.SetActive(false);
        }

        private bool EnsureWeekTitle()
        {
            if (_axis == null || !(_axis.parent is RectTransform parent))
            {
                return false;
            }

            if (_weekTitleRect != null && _weekTitleText != null && _weekTitleGroup != null)
            {
                if (_weekTitleRect.parent != parent)
                {
                    _weekTitleRect.SetParent(parent, false);
                }

                return true;
            }

            if (_weekTitleStyleSource == null)
            {
                return false;
            }

            _weekTitleText = UnityEngine.Object.Instantiate(
                _weekTitleStyleSource,
                parent,
                false);
            _weekTitleText.name = "TimelineAxisWeekLabel";
            _weekTitleText.text = string.Empty;
            _weekTitleText.alignment = TextAlignmentOptions.Center;
            _weekTitleText.enableAutoSizing = false;
            _weekTitleText.fontSize = 56f;
            _weekTitleText.fontStyle = FontStyles.Normal;
            _weekTitleText.fontWeight = FontWeight.Regular;
            _weekTitleText.color = Color.white;
            _weekTitleText.outlineColor = WeekTitleOutlineColor;
            _weekTitleText.outlineWidth = WeekTitleOutlineWidth;
            _weekTitleText.overflowMode = TextOverflowModes.Overflow;
            _weekTitleText.raycastTarget = false;

            _weekTitleRect = _weekTitleText.rectTransform;
            _weekTitleRect.anchorMin = new Vector2(0.5f, 0.5f);
            _weekTitleRect.anchorMax = new Vector2(0.5f, 0.5f);
            _weekTitleRect.pivot = new Vector2(0.5f, 0.5f);
            _weekTitleRect.anchoredPosition = Vector2.zero;
            _weekTitleRect.sizeDelta = new Vector2(480f, 120f);
            _weekTitleRect.localRotation = Quaternion.identity;
            _weekTitleRect.localScale = Vector3.one;

            _weekTitleGroup = _weekTitleText.GetComponent<CanvasGroup>();
            if (_weekTitleGroup == null)
            {
                _weekTitleGroup = _weekTitleText.gameObject.AddComponent<CanvasGroup>();
            }

            _weekTitleGroup.alpha = 0f;
            _weekTitleGroup.interactable = false;
            _weekTitleGroup.blocksRaycasts = false;
            _weekTitleText.gameObject.SetActive(false);
            return true;
        }

        private void ShowWeekTitle(int weekIndex)
        {
            if (_weekTitleText == null || _weekTitleRect == null || _weekTitleGroup == null)
            {
                return;
            }

            _weekTitleText.text = $"第{Mathf.Max(1, weekIndex)}周";
            _weekTitleText.fontStyle = FontStyles.Normal;
            _weekTitleText.fontWeight = FontWeight.Regular;
            _weekTitleText.color = Color.white;
            _weekTitleText.outlineColor = WeekTitleOutlineColor;
            _weekTitleText.outlineWidth = WeekTitleOutlineWidth;
            _weekTitleGroup.alpha = 0f;
            _weekTitleRect.localScale = Vector3.one * WeekTitleEnterScale;
            _weekTitleText.gameObject.SetActive(true);
            _weekTitleRect.SetAsLastSibling();
        }

        private void HideWeekTitle()
        {
            if (_weekTitleGroup != null)
            {
                _weekTitleGroup.alpha = 0f;
                _weekTitleGroup.interactable = false;
                _weekTitleGroup.blocksRaycasts = false;
            }

            if (_weekTitleRect != null)
            {
                _weekTitleRect.localScale = Vector3.one;
            }

            if (_weekTitleText != null)
            {
                _weekTitleText.text = string.Empty;
                _weekTitleText.gameObject.SetActive(false);
            }
        }

        private void RestoreImmediately()
        {
            _active = false;
            HideWeekTitle();
            if (_axis != null)
            {
                _axis.anchoredPosition = _restPosition;
                _axis.localScale = _restScale;
                if (_axis.parent != null)
                {
                    _axis.SetSiblingIndex(Mathf.Clamp(
                        _restSiblingIndex,
                        0,
                        Mathf.Max(0, _axis.parent.childCount - 1)));
                }
            }

            if (_axisGroup != null)
            {
                _axisGroup.alpha = _restAlpha;
                _axisGroup.interactable = _restInteractable;
                _axisGroup.blocksRaycasts = _restBlocksRaycasts;
            }

            if (_backdropGroup != null)
            {
                _backdropGroup.alpha = 0f;
                _backdropGroup.interactable = false;
                _backdropGroup.blocksRaycasts = false;
            }

            if (_backdropRect != null)
            {
                _backdropRect.gameObject.SetActive(false);
            }
        }

        private Vector3 ScaledRest(float scale)
        {
            return new Vector3(
                _restScale.x * scale,
                _restScale.y * scale,
                _restScale.z);
        }
    }
}
