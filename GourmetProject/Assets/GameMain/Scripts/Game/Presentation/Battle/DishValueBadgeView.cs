using UnityEngine;
using TMPro;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>常驻在菜品顶部的美味值标签。</summary>
    internal sealed class DishValueBadgeView : MonoBehaviour
    {
        [Header("固定结构（prefab 预拼）")]
        [SerializeField] private SpriteRenderer _background;
        [SerializeField] private SpriteRenderer _icon;
        [SerializeField] private MeshRenderer _valueMeshRenderer;
        [SerializeField] private TextMeshPro _valueText;

        private string _sortingLayer = BattleSorting.Fx;
        private int _sortingOrder = BattleSorting.OrderFloatingText;

        /// <summary>Badge 根节点到最高可见 Sprite 边缘的本地距离。</summary>
        public float TopExtent
        {
            get
            {
                return Mathf.Max(
                    SpriteTopExtent(_background),
                    SpriteTopExtent(_icon));
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

        private static float SpriteTopExtent(SpriteRenderer renderer)
        {
            if (renderer == null || renderer.sprite == null)
            {
                return 0f;
            }

            Transform spriteTransform = renderer.transform;
            return spriteTransform.localPosition.y
                + renderer.sprite.bounds.max.y
                * Mathf.Abs(spriteTransform.localScale.y);
        }
    }
}
