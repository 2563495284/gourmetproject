using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GourmetProject.Game.Tutorial
{
    /// <summary>Prefab-owned click surface used by informational tutorial steps.</summary>
    public sealed class TutorialOverlayClickSurface : MonoBehaviour, IPointerClickHandler
    {
        private Action _onClick;

        public void Bind(Action onClick) => _onClick = onClick;

        public void Unbind() => _onClick = null;

        public void OnPointerClick(PointerEventData eventData) => _onClick?.Invoke();
    }
}
