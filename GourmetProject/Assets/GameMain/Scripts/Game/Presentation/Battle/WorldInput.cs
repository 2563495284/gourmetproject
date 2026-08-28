using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    internal static class WorldInput
    {
        private static readonly List<RaycastResult> UiRaycastResults = new List<RaycastResult>();

        private static PointerEventData _pointerEventData;
        private static EventSystem _pointerEventSystem;
        private static int _pointerOverUiFrame = -1;
        private static bool _pointerOverUi;

        public static bool PrimaryPressedThisFrame
            => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !PointerOverUi;

        public static bool PrimaryReleasedThisFrame
            => Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame && !PointerOverUi;

        public static bool PrimaryHeld
            => Mouse.current != null && Mouse.current.leftButton.isPressed;

        public static Vector2 MouseScreen
            => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

        public static bool SecondaryPressedThisFrame
            => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame && !PointerOverUi;

        public static bool SecondaryHeld
            => Mouse.current != null && Mouse.current.rightButton.isPressed && !PointerOverUi;

        public static bool SecondaryReleasedThisFrame
            => Mouse.current != null && Mouse.current.rightButton.wasReleasedThisFrame;

        public static bool RotatePressedThisFrame
            => Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;

        /// <summary>出菜口待摆食物的拿起/放下快捷键；不受鼠标是否位于 UI 上影响。</summary>
        public static bool ServingOutletShortcutPressedThisFrame
            => Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;

        /// <summary>本帧鼠标滚轮竖直增量（向上为正）。用于餐桌编辑页旋转选中碎片。</summary>
        public static float ScrollDelta
            => Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;

        public static bool PointerOverUi
        {
            get
            {
                int frame = Time.frameCount;
                if (_pointerOverUiFrame == frame)
                {
                    return _pointerOverUi;
                }

                _pointerOverUiFrame = frame;
                _pointerOverUi = RaycastPointerOverUi();
                return _pointerOverUi;
            }
        }

        public static GameObject PointerClickHandler
        {
            get
            {
                if (!PointerOverUi)
                {
                    return null;
                }

                for (int i = 0; i < UiRaycastResults.Count; i++)
                {
                    GameObject handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(
                        UiRaycastResults[i].gameObject);
                    if (handler != null)
                    {
                        return handler;
                    }
                }

                return null;
            }
        }

        public static Vector3 MouseWorld(Camera camera)
        {
            if (camera == null || Mouse.current == null)
            {
                return Vector3.zero;
            }

            Vector2 screen = Mouse.current.position.ReadValue();
            var mouse = new Vector3(screen.x, screen.y, -camera.transform.position.z);
            Vector3 world = camera.ScreenToWorldPoint(mouse);
            world.z = 0f;
            return world;
        }

        private static bool RaycastPointerOverUi()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || Mouse.current == null)
            {
                return false;
            }

            if (_pointerEventData == null || _pointerEventSystem != eventSystem)
            {
                _pointerEventSystem = eventSystem;
                _pointerEventData = new PointerEventData(eventSystem);
            }
            else
            {
                _pointerEventData.Reset();
            }

            Vector2 screen = Mouse.current.position.ReadValue();
            _pointerEventData.position = screen;

            UiRaycastResults.Clear();
            eventSystem.RaycastAll(_pointerEventData, UiRaycastResults);
            return UiRaycastResults.Count > 0;
        }
    }
}
