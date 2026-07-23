using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>只接收出餐口待摆放食物的鼠标进出，避免面板其它区域误触发食物 Tips。</summary>
    public sealed class ServingOutletDishHoverTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Action _onEntered;
        private Action _onExited;
        private bool _hovered;

        public void Bind(Action onEntered, Action onExited)
        {
            _onEntered = onEntered;
            _onExited = onExited;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_hovered)
            {
                return;
            }

            _hovered = true;
            _onEntered?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            CancelHover();
        }

        public void CancelHover()
        {
            if (!_hovered)
            {
                return;
            }

            _hovered = false;
            _onExited?.Invoke();
        }

        private void OnDisable()
        {
            CancelHover();
        }
    }
}
