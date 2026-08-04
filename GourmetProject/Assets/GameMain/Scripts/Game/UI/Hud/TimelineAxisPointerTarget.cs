using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>时间轴日期点与节点气泡共用的轻量指针事件转发器。</summary>
    public sealed class TimelineAxisPointerTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private Action _entered;
        private Action _exited;
        private Action<PointerEventData> _clicked;

        public void Bind(Action entered, Action exited, Action<PointerEventData> clicked)
        {
            _entered = entered;
            _exited = exited;
            _clicked = clicked;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _entered?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _exited?.Invoke();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            _clicked?.Invoke(eventData);
        }
    }
}
