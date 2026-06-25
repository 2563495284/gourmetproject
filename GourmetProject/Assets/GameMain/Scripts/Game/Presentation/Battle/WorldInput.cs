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

        public static bool SecondaryPressedThisFrame
            => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame && !PointerOverUi;

        public static bool SecondaryHeld
            => Mouse.current != null && Mouse.current.rightButton.isPressed && !PointerOverUi;

        public static bool SecondaryReleasedThisFrame
            => Mouse.current != null && Mouse.current.rightButton.wasReleasedThisFrame;

        public static bool RotatePressedThisFrame
            => Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;

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
