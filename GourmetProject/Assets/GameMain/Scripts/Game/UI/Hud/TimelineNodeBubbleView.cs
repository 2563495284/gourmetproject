using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>行动轴节点的气泡视图：圆角卡片、图标、标题、箭头尾巴与选择态描边。</summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup), typeof(TimelineAxisPointerTarget))]
    public sealed class TimelineNodeBubbleView : MonoBehaviour
    {
        private static readonly Color NormalFill = new Color(1f, 0.94f, 0.80f, 0.98f);
        private static readonly Color CompletedFill = new Color(0.72f, 0.70f, 0.65f, 0.78f);
        private static readonly Color PreviewFill = new Color(0.74f, 1f, 0.92f, 0.78f);
        private static readonly Color WarningColor = new Color(0.92f, 0.20f, 0.18f, 1f);

        [Header("Prefab 引用")]
        [SerializeField] private RectTransform _rect;
        [SerializeField] private Image _tail;
        [SerializeField] private Image _icon;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private TimelineAxisPointerTarget _pointer;

        private bool _pulse;
        private Vector3 _baseScale = Vector3.one;

        public RectTransform Rect => _rect;

        public void Bind(
            Sprite icon,
            bool completed,
            bool boss,
            bool preview,
            float x,
            int stackIndex)
        {
            _icon.sprite = icon;
            _icon.enabled = icon != null;
            float clampedX = Mathf.Clamp(x, 0.065f, 0.935f);
            _rect.anchorMin = new Vector2(clampedX, 0.48f);
            _rect.anchorMax = new Vector2(clampedX, 0.48f);
            _rect.pivot = new Vector2(0.5f, 0f);
            _rect.sizeDelta = boss ? new Vector2(126f, 62f) : new Vector2(108f, 48f);
            _rect.anchoredPosition = new Vector2(0f, 20f + stackIndex * 43f);
            float parentWidth = (_rect.parent as RectTransform)?.rect.width ?? 0f;
            ((RectTransform)_tail.transform).anchoredPosition = new Vector2(
                (x - clampedX) * parentWidth,
                -5f);

            Color fill = preview ? PreviewFill : (completed ? CompletedFill : NormalFill);
            _tail.color = fill;
            _canvasGroup.alpha = completed && !preview ? 0.74f : 1f;
            _baseScale = boss ? Vector3.one * 1.06f : Vector3.one;
            transform.localScale = _baseScale;
        }

        public void BindPointer(Action entered, Action exited, Action clicked)
        {
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

        public void SetDeleteState(bool eligible, bool selected, bool hovered)
        {
            _canvasGroup.alpha = eligible ? 1f : 0.32f;
            _pulse = eligible && (selected || hovered);
            if (!_pulse)
            {
                transform.localScale = _baseScale;
            }
        }

        private void Update()
        {
            if (!_pulse)
            {
                return;
            }

            float scale = 1f + Mathf.Sin(Time.unscaledTime * 7f) * 0.035f;
            transform.localScale = _baseScale * scale;
        }

    }
}
