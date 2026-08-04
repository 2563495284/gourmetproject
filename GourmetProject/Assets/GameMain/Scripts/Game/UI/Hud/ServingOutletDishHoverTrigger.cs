using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>只接收出菜口待摆放食物的鼠标进出，避免面板其它区域误触发食物 Tips。</summary>
    public sealed class ServingOutletDishHoverTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
    {
        private Func<bool> _tryEnter;
        private Action _onExited;
        private bool _pointerInside;
        private bool _tipsShown;

        public void Bind(Func<bool> tryEnter, Action onExited)
        {
            _tryEnter = tryEnter;
            _onExited = onExited;
            TryShowTips();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _pointerInside = true;
            TryShowTips();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            CancelHover();
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            if (_pointerInside)
            {
                TryShowTips();
            }
        }

        public void CancelHover()
        {
            _pointerInside = false;
            if (!_tipsShown)
            {
                return;
            }

            _tipsShown = false;
            _onExited?.Invoke();
        }

        private void LateUpdate()
        {
            if (_pointerInside)
            {
                TryShowTips();
            }
        }

        private void TryShowTips()
        {
            if (!_pointerInside || _tipsShown || _tryEnter == null)
            {
                return;
            }

            _tipsShown = _tryEnter.Invoke();
        }

        private void OnDisable()
        {
            CancelHover();
        }
    }
}
