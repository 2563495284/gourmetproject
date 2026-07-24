using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 美食战斗左下角的世界空间垃圾桶。只负责显示剩余次数与屏幕点命中，
    /// 实际丢弃由 BattleSession/BattleWorldController 完成。
    /// </summary>
    [RequireComponent(typeof(Canvas), typeof(CanvasGroup))]
    public sealed class FoodDiscardBinView : MonoBehaviour
    {
        [SerializeField] private Image _binImage;
        [SerializeField] private Text _remainingText;
        [SerializeField] private Text _hintText;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Color _normalColor = Color.white;
        [SerializeField] private Color _hoverColor = new Color(1f, 0.45f, 0.32f, 1f);

        private BattleSession _session;
        private Camera _worldCamera;
        private Canvas _worldCanvas;
        private bool _dragHovered;

        public void ConfigureWorldSpace(Camera worldCamera)
        {
            _worldCamera = worldCamera != null ? worldCamera : Camera.main;
            _worldCanvas = GetComponent<Canvas>();
            if (_worldCanvas == null)
            {
                Debug.LogError($"{nameof(FoodDiscardBinView)} 缺少 World Space Canvas。", this);
                return;
            }

            _worldCanvas.renderMode = RenderMode.WorldSpace;
            _worldCanvas.worldCamera = _worldCamera;
            _worldCanvas.overrideSorting = true;
            _worldCanvas.sortingLayerName = BattleSorting.WorldUi;
            _worldCanvas.sortingOrder = 1;
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
                || _session?.PreparedServe == null
                || _session.FoodDiscardsRemaining <= 0)
            {
                return false;
            }

            Camera eventCamera = _worldCanvas != null && _worldCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _worldCamera
                : null;
            return RectTransformUtility.RectangleContainsScreenPoint(
                (RectTransform)transform,
                screenPoint,
                eventCamera);
        }

        public void SetDragHovered(bool hovered)
        {
            hovered &= _session?.PreparedServe != null && _session.FoodDiscardsRemaining > 0;
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
                _binImage.rectTransform.localScale = _dragHovered ? Vector3.one * 1.08f : Vector3.one;
            }

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = enabled ? 1f : 0.35f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }
        }
    }
}
