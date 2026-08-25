using DG.Tweening;
using GourmetProject.Gameplay.Battle;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 经营挑战左下角的 Overlay 垃圾桶。只负责显示剩余次数与屏幕点命中，
    /// 实际丢弃由 BattleSession/BattleWorldController 完成。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class FoodDiscardBinView : MonoBehaviour
    {
        [SerializeField] private Image _binImage;
        [SerializeField] private TMP_Text _remainingText;
        [SerializeField] private TMP_Text _hintText;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Color _normalColor = Color.white;
        [SerializeField] private Color _hoverColor = new Color(1f, 0.45f, 0.32f, 1f);

        private BattleSession _session;
        private bool _dragHovered;
        private Tween _acceptedTween;
        private Vector3 _binBaseScale = Vector3.one;
        private bool _binBaseScaleCaptured;

        private void Awake()
        {
            CaptureBinBaseScale();
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
            }

            if (visible)
            {
                RefreshVisual();
            }
            else
            {
                _dragHovered = false;
            }
        }

        public void Bind(BattleSession session)
        {
            _session = session;
            RefreshVisual();
        }

        public bool CanAcceptDropAt(Vector2 screenPoint)
        {
            if (!isActiveAndEnabled
                || _session == null
                || _session.FoodDiscardsRemaining <= 0)
            {
                return false;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            return RectTransformUtility.RectangleContainsScreenPoint(
                (RectTransform)transform,
                screenPoint,
                eventCamera);
        }

        /// <summary>拖拽菜品落桶时使用图标中心，而不是整个提示面板中心。</summary>
        public Vector2 GetDiscardAnimationTargetScreenPosition()
        {
            RectTransform target = _binImage != null
                ? _binImage.rectTransform
                : transform as RectTransform;
            if (target == null)
            {
                return Vector2.zero;
            }

            Canvas canvas = target.GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            return RectTransformUtility.WorldToScreenPoint(
                eventCamera,
                target.TransformPoint(target.rect.center));
        }

        /// <summary>菜品飞入时让桶口先收再弹，强化“被接住”的落点反馈。</summary>
        public void PlayAcceptedAnimation()
        {
            CaptureBinBaseScale();
            if (_binImage == null)
            {
                return;
            }

            _acceptedTween?.Kill();
            RectTransform target = _binImage.rectTransform;
            target.localScale = _binBaseScale;
            _acceptedTween = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(gameObject)
                .Append(target
                    .DOScale(_binBaseScale * 0.9f, 0.07f)
                    .SetEase(Ease.InQuad))
                .Append(target
                    .DOScale(_binBaseScale * 1.14f, 0.1f)
                    .SetEase(Ease.OutBack))
                .Append(target
                    .DOScale(_binBaseScale, 0.11f)
                    .SetEase(Ease.OutQuad))
                .OnComplete(() => _acceptedTween = null);
        }

        public void SetDragHovered(bool hovered)
        {
            hovered &= _session != null && _session.FoodDiscardsRemaining > 0;
            if (_dragHovered == hovered)
            {
                return;
            }

            _dragHovered = hovered;
            RefreshVisual();
        }

        private void RefreshVisual()
        {
            int remaining = _session?.FoodDiscardsRemaining ?? 0;
            bool enabled = remaining > 0;

            if (_remainingText != null)
            {
                _remainingText.text = $"可丢弃 {remaining}";
            }

            if (_hintText != null)
            {
                _hintText.text = !enabled
                    ? "本局不可丢弃"
                    : _dragHovered
                        ? "松开丢弃"
                        : "拖到这里丢弃";
                _hintText.color = _dragHovered ? _hoverColor : Color.white;
            }

            if (_binImage != null)
            {
                _binImage.color = _dragHovered ? _hoverColor : _normalColor;
                if (_acceptedTween == null)
                {
                    CaptureBinBaseScale();
                    _binImage.rectTransform.localScale = _dragHovered
                        ? _binBaseScale * 1.08f
                        : _binBaseScale;
                }
            }

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = enabled ? 1f : 0.35f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }
        }

        private void CaptureBinBaseScale()
        {
            if (_binBaseScaleCaptured || _binImage == null)
            {
                return;
            }

            _binBaseScale = _binImage.rectTransform.localScale;
            _binBaseScaleCaptured = true;
        }

        private void OnDisable()
        {
            Tween acceptedTween = _acceptedTween;
            _acceptedTween = null;
            acceptedTween?.Kill();
            if (_binImage != null)
            {
                CaptureBinBaseScale();
                _binImage.rectTransform.localScale = _binBaseScale;
            }
        }
    }
}
