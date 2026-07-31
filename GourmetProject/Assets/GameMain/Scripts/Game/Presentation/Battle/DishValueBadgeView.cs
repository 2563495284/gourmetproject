using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>常驻在菜品顶部的美味值标签。</summary>
    internal sealed class DishValueBadgeView : MonoBehaviour
    {
        [Header("固定结构（prefab 预拼）")]
        [SerializeField] private SpriteRenderer _background;
        [SerializeField] private SpriteRenderer _icon;
        [SerializeField] private MeshRenderer _valueMeshRenderer;
        [SerializeField] private TextMesh _valueText;

        private string _sortingLayer = BattleSorting.Fx;
        private int _sortingOrder = BattleSorting.OrderFloatingText;

        public float PanelHeight
        {
            get
            {
                if (_background == null || _background.sprite == null)
                {
                    return 0f;
                }

                return _background.sprite.bounds.size.y * Mathf.Abs(_background.transform.localScale.y);
            }
        }

        public void SetValue(string text)
        {
            if (_valueText != null)
            {
                _valueText.text = text;
            }

            ApplySortingOrder();
        }

        public void ConfigureSorting(string sortingLayer, int sortingOrder)
        {
            _sortingLayer = sortingLayer;
            _sortingOrder = sortingOrder;
            ApplySortingOrder();
        }

        private void ApplySortingOrder()
        {
            BattleSorting.Apply(_valueMeshRenderer, _sortingLayer, _sortingOrder + 2);
            BattleSorting.Apply(_background, _sortingLayer, _sortingOrder);
            BattleSorting.Apply(_icon, _sortingLayer, _sortingOrder + 3);
        }
    }
}
