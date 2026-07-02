using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 编辑菜谱态底部垃圾桶。接收菜品卡 Drop 后请求删除。
    /// </summary>
    public sealed class RecipeTrashDropZone : MonoBehaviour, IDropHandler
    {
        private Action<RecipeEditDishView> _onDishDropped;

        public void Bind(Action<RecipeEditDishView> onDishDropped)
        {
            _onDishDropped = onDishDropped;
        }

        public void OnDrop(PointerEventData eventData)
        {
            RecipeEditDishView dish = eventData.pointerDrag == null
                ? null
                : eventData.pointerDrag.GetComponent<RecipeEditDishView>();
            if (dish != null)
            {
                _onDishDropped?.Invoke(dish);
            }
        }
    }
}
