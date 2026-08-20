using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Common
{
    /// <summary>单个可复用 Toast 的布局与“渐显上浮、停留、渐隐”演出。</summary>
    public sealed class ToastPresenter : MonoBehaviour
    {
        private const float LeftPadding = 36f;
        private const float RightPadding = 32f;
        private const float IconGap = 18f;
        private const float VerticalTextPadding = 16f;
        private const float TailSize = 32f;
        private const float TailOverlap = 14f;

        [SerializeField] private ToastTheme _theme;
        [SerializeField] private RectTransform _rect;
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private Image _background;
        [SerializeField] private Image _tail;
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _message;

        private Sequence _sequence;
        private Action _onComplete;

        internal bool IsPlaying => _sequence != null && _sequence.IsActive();
        internal float Alpha => _group != null ? _group.alpha : 0f;
        internal float PositionY => _rect != null ? _rect.anchoredPosition.y : 0f;
        internal float Scale => _rect != null ? _rect.localScale.x : 1f;
        internal Sprite CurrentIcon => _icon != null ? _icon.sprite : null;
        internal string CurrentMessage => _message != null ? _message.text : string.Empty;

        private void Awake()
        {
            ValidateReferences();
            ResetVisual();
        }

        private void OnDisable()
        {
            Cancel(false);
        }

        internal void Play(ToastRequest request, Action onComplete)
        {
            Cancel(false);
            ValidateReferences();

            _onComplete = onComplete;
            _background.sprite = _theme.Bubble;
            _tail.sprite = _theme.Tail;
            _icon.sprite = _theme.ResolveIcon(request.Kind);
            _message.color = _theme.TextColor;
            _message.text = request.Message;
            ConfigureLayout(request.Message);

            _rect.anchoredPosition = new Vector2(0f, _theme.EnterOffsetY);
            _rect.localScale = Vector3.one;
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            _sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(gameObject)
                .Append(_group.DOFade(1f, _theme.EnterDuration).SetEase(Ease.OutQuad))
                .Join(_rect.DOAnchorPosY(0f, _theme.EnterDuration).SetEase(Ease.OutCubic))
                .AppendInterval(_theme.HoldDuration)
                .Append(_group.DOFade(0f, _theme.ExitDuration).SetEase(Ease.InQuad))
                .OnComplete(Finish);
        }

        internal void CompleteImmediately()
        {
            _sequence?.Complete(true);
        }

        internal void Cancel(bool invokeCompletion)
        {
            Sequence sequence = _sequence;
            _sequence = null;
            sequence?.Kill(false);

            Action callback = _onComplete;
            _onComplete = null;
            ResetVisual();
            if (invokeCompletion)
            {
                callback?.Invoke();
            }
        }

        internal void ResetVisual()
        {
            if (_group != null)
            {
                _group.alpha = 0f;
                _group.interactable = false;
                _group.blocksRaycasts = false;
            }

            if (_rect != null)
            {
                _rect.anchoredPosition = new Vector2(0f, _theme != null ? _theme.EnterOffsetY : -16f);
                _rect.localScale = Vector3.one;
            }
        }

        private void ConfigureLayout(string text)
        {
            float fixedWidth = LeftPadding + _theme.IconSize + IconGap + RightPadding;
            float maximumTextWidth = Mathf.Max(1f, _theme.MaximumWidth - fixedWidth);
            Vector2 oneLine = _message.GetPreferredValues(text, 10000f, _theme.SingleLineHeight);
            float preferredTextWidth = Mathf.Max(1f, Mathf.Ceil(oneLine.x));
            bool wraps = preferredTextWidth > maximumTextWidth;
            float width = Mathf.Clamp(fixedWidth + preferredTextWidth,
                _theme.MinimumWidth,
                _theme.MaximumWidth);
            float height = wraps ? _theme.DoubleLineHeight : _theme.SingleLineHeight;
            _rect.sizeDelta = new Vector2(width, height);

            RectTransform iconRect = _icon.rectTransform;
            iconRect.sizeDelta = Vector2.one * _theme.IconSize;
            iconRect.anchoredPosition = new Vector2(
                -width * 0.5f + LeftPadding + _theme.IconSize * 0.5f,
                0f);

            float textLeft = -width * 0.5f + LeftPadding + _theme.IconSize + IconGap;
            float textRight = width * 0.5f - RightPadding;
            RectTransform messageRect = _message.rectTransform;
            messageRect.sizeDelta = new Vector2(
                Mathf.Max(1f, textRight - textLeft),
                height - VerticalTextPadding * 2f);
            messageRect.anchoredPosition = new Vector2((textLeft + textRight) * 0.5f, 0f);

            RectTransform tailRect = _tail.rectTransform;
            tailRect.sizeDelta = Vector2.one * TailSize;
            tailRect.anchoredPosition = new Vector2(
                0f,
                -height * 0.5f - TailSize * 0.5f + TailOverlap);
        }

        private void Finish()
        {
            _sequence = null;
            Action callback = _onComplete;
            _onComplete = null;
            ResetVisual();
            callback?.Invoke();
        }

        private void ValidateReferences()
        {
            Require(_theme, nameof(_theme));
            Require(_rect, nameof(_rect));
            Require(_group, nameof(_group));
            Require(_background, nameof(_background));
            Require(_tail, nameof(_tail));
            Require(_icon, nameof(_icon));
            Require(_message, nameof(_message));
        }

        private static void Require(UnityEngine.Object value, string fieldName)
        {
            if (value == null)
            {
                throw new MissingReferenceException(
                    $"ToastPresenter requires serialized reference '{fieldName}'.");
            }
        }
    }
}
