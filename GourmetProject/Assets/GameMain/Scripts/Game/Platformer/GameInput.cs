using UnityEngine.InputSystem;

namespace GourmetProject.Game.Platformer
{
    /// <summary>
    /// 玩法输入封装（新版 Input System，直接读取 Keyboard.current）。
    /// 设计文档第三节：A/D 移动、空格跳、按住 R 打火机、T 自杀、逗号全图照亮；
    /// F 附墙 / C 缓降 / Shift 冲刺为技能键（随技能系统期接入）。
    /// </summary>
    public static class GameInput
    {
        public static float MoveX
        {
            get
            {
                var kb = Keyboard.current;
                if (kb == null) return 0f;
                float x = 0f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
                return x;
            }
        }

        public static bool JumpPressed
        {
            get
            {
                var kb = Keyboard.current;
                return kb != null && (kb.spaceKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame);
            }
        }

        public static bool JumpReleased
        {
            get
            {
                var kb = Keyboard.current;
                return kb != null && (kb.spaceKey.wasReleasedThisFrame || kb.wKey.wasReleasedThisFrame || kb.upArrowKey.wasReleasedThisFrame);
            }
        }

        public static bool LighterHeld
        {
            get
            {
                var kb = Keyboard.current;
                return kb != null && kb.rKey.isPressed;
            }
        }

        public static bool WallStickHeld
        {
            get
            {
                var kb = Keyboard.current;
                return kb != null && kb.fKey.isPressed;
            }
        }

        public static bool SuicidePressed
        {
            get
            {
                var kb = Keyboard.current;
                return kb != null && kb.tKey.wasPressedThisFrame;
            }
        }

        public static bool RevealTogglePressed
        {
            get
            {
                var kb = Keyboard.current;
                return kb != null && kb.commaKey.wasPressedThisFrame;
            }
        }
    }
}
