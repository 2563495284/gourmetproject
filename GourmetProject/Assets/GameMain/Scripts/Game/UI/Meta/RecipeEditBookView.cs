using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 编辑菜谱态中的一本菜谱。作为 Drop 目标接收从其它菜谱拖来的菜品。
    /// </summary>
    public sealed class RecipeEditBookView : MonoBehaviour, IDropHandler
    {
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _capacityText;
        [SerializeField] private RectTransform _dishContainer;

        private int _bookIndex;
        private Action<RecipeEditDishView, int> _onDishDropped;
        private Action<RewardDishChoiceCardView, int> _onChoiceDropped;

        public RectTransform DishContainer => _dishContainer;

        public void Bind(
            int bookIndex,
            string title,
            string capacity,
            Action<RecipeEditDishView, int> onDishDropped,
            Action<RewardDishChoiceCardView, int> onChoiceDropped = null)
        {
            _bookIndex = bookIndex;
            _onDishDropped = onDishDropped;
            _onChoiceDropped = onChoiceDropped;

            if (_titleText != null)
            {
                _titleText.text = title ?? string.Empty;
            }

            if (_capacityText != null)
            {
                _capacityText.text = capacity ?? string.Empty;
            }
        }

        public void OnDrop(PointerEventData eventData)
        {
            RecipeEditDishView dish = eventData.pointerDrag == null
                ? null
                : eventData.pointerDrag.GetComponentInParent<RecipeEditDishView>();
            if (dish != null)
            {
                _onDishDropped?.Invoke(dish, _bookIndex);
                return;
            }

            RewardDishChoiceCardView choice = eventData.pointerDrag == null
                ? null
                : eventData.pointerDrag.GetComponentInParent<RewardDishChoiceCardView>();
            if (choice != null)
            {
                _onChoiceDropped?.Invoke(choice, _bookIndex);
            }
        }
    }
}
