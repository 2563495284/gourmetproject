using UnityEngine;
using UnityEngine.EventSystems;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 通用「悬停显示 Tips」口子：挂在任意可悬停的 UI 元素上（行动轴节点格 / 道具槽等），
    /// 指针进入时 <see cref="ActionTipView.Show"/> 目标 Tips，离开时 <see cref="ActionTipView.Hide"/>。
    ///
    /// 只负责显隐 + 跟随定位，不关心 Tips 内容——内容由持有方在 <see cref="Show"/> 前
    /// 调用对应子类的 Bind 填好。<see cref="_tip"/> 可在 Inspector 预先指定，也可运行时
    /// 通过 <see cref="SetTip"/> 注入共享的 Tips 实例。
    /// </summary>
    public sealed class TipHoverTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("要显隐的 Tips 视图；留空则运行时用 SetTip 注入。")]
        [SerializeField] private ActionTipView _tip;

        [Tooltip("是否让 Tips 跟随指针（在同一 Canvas 下按屏幕坐标定位）。")]
        [SerializeField] private bool _followPointer = true;

        [Tooltip("跟随指针时相对指针的像素偏移。")]
        [SerializeField] private Vector2 _pointerOffset = new Vector2(24f, -24f);

        private RectTransform _tipRect;
        private Canvas _canvas;

        /// <summary>运行时注入 / 替换目标 Tips（多个触发器可共用一个 Tips 实例）。</summary>
        public void SetTip(ActionTipView tip)
        {
            _tip = tip;
            _tipRect = tip != null ? tip.transform as RectTransform : null;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_tip == null)
            {
                return;
            }

            if (_followPointer)
            {
                PositionAt(eventData);
            }

            _tip.transform.SetAsLastSibling();
            _tip.Show();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_tip != null)
            {
                _tip.Hide();
            }
        }

        private void PositionAt(PointerEventData eventData)
        {
            if (_tipRect == null)
            {
                _tipRect = _tip.transform as RectTransform;
            }

            if (_tipRect == null)
            {
                return;
            }

            if (_canvas == null)
            {
                _canvas = _tip.GetComponentInParent<Canvas>();
            }

            var parent = _tipRect.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            Camera cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, cam, out Vector2 local))
            {
                _tipRect.anchoredPosition = local + _pointerOffset;
            }
        }
    }
}
