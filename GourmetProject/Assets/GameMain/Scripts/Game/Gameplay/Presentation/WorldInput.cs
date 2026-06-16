using UnityEngine;
using UnityEngine.InputSystem;

namespace GourmetProject.Game.Gameplay.Presentation
{
    internal static class WorldInput
    {
        public static bool PrimaryPressedThisFrame
            => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        public static bool PrimaryReleasedThisFrame
            => Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;

        public static bool SecondaryPressedThisFrame
            => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;

        public static bool RotatePressedThisFrame
            => Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;

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
    }
}
