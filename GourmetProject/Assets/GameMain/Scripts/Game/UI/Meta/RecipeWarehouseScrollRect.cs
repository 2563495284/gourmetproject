using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 仓库双轴 ScrollRect：普通滚轮纵向，按住 Shift 时滚轮横向。
    /// </summary>
    public sealed class RecipeWarehouseScrollRect : ScrollRect
    {
        public override void OnScroll(PointerEventData data)
        {
            bool shiftPressed = Keyboard.current != null
                && (Keyboard.current.leftShiftKey.isPressed
                    || Keyboard.current.rightShiftKey.isPressed);
            if (!shiftPressed)
            {
                base.OnScroll(data);
                return;
            }

            bool previousHorizontal = horizontal;
            bool previousVertical = vertical;
            horizontal = true;
            vertical = false;
            base.OnScroll(data);
            horizontal = previousHorizontal;
            vertical = previousVertical;
        }
    }
}
