using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 时间轴推进时的聚焦演出：用全屏暗幕隔离其他 HUD，并把时间轴暂时移动到屏幕中央。
    /// </summary>
    internal sealed class TimelineAxisFocusPresenter
    {
        private const float EnterDuration = 0.64f;
        // private const float PostEnterDelay = 0.6f;
        private const float PreExitDelay = 0.5f;
        private const float ExitDuration = 0.78f;
        private static readonly Color BackdropColor = new Color(0f, 0f, 0f, 0.76f);

        private readonly RectTransform _axis;
        private readonly CanvasGroup _axisGroup;
        private RectTransform _backdropRect;
        private CanvasGroup _backdropGroup;
        private Tween _transition;
        private Vector2 _restPosition;
        private int _restSiblingIndex;
        private bool _restInteractable;
        private bool _restBlocksRaycasts;
        private bool _active;

        public TimelineAxisFocusPresenter(RectTransform axis, CanvasGroup axisGroup)
        {
            _axis = axis;
            _axisGroup = axisGroup;
        }

        public bool CanPresent => _axis != null && _axis.parent is RectTransform;

        public void Enter(Action onComplete)
        {
            if (!CanPresent)
            {
                onComplete?.Invoke();
                return;
            }

            Cancel();
            EnsureBackdrop();
            if (_backdropRect == null || _backdropGroup == null)
            {
                onComplete?.Invoke();
                return;
            }

            _active = true;
            _restPosition = _axis.anchoredPosition;
            _restSiblingIndex = _axis.GetSiblingIndex();
            if (_axisGroup != null)
            {
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
                // .AppendInterval(PostEnterDelay)
                .OnComplete(() =>
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

        private void RestoreImmediately()
        {
            _active = false;
            if (_axis != null)
            {
                _axis.anchoredPosition = _restPosition;
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
    }
}
