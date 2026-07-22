using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>结算期间常驻在菜品顶部的美味值标签。</summary>
    internal sealed class DishValueBadgeView : MonoBehaviour
    {
        [Header("固定结构（prefab 预拼）")]
        [SerializeField] private SpriteRenderer _background;
        [SerializeField] private SpriteRenderer _icon;

        private TextMesh _valueText;
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

        public static DishValueBadgeView Spawn(
            DishValueBadgeView prefab,
            Transform parent,
            Vector3 worldPos,
            string text,
            float? characterSize = null)
        {
            if (prefab == null)
            {
                Debug.LogError($"{nameof(DishValueBadgeView)} 缺少 prefab。");
                return null;
            }

            DishValueBadgeView view = Instantiate(prefab, parent);
            view.transform.position = worldPos;
            view._sortingOrder = WorldLabelSorting.NextOrder();
            view.SetValue(text, characterSize);
            return view;
        }

        public void SetValue(string text, float? characterSize = null)
        {
            TextMesh value = EnsureValueText();
            if (value == null)
            {
                return;
            }

            value.text = text;
            ApplySortingOrder();
        }

        private TextMesh EnsureValueText()
        {
            if (_valueText != null)
            {
                return _valueText;
            }

            _valueText = GetComponent<TextMesh>();
            if (_valueText == null)
            {
                Debug.LogError($"{nameof(DishValueBadgeView)} prefab 缺少 TextMesh。", this);
                return null;
            }

            return _valueText;
        }

        private void ApplySortingOrder()
        {
            BattleSorting.Apply(GetComponent<MeshRenderer>(), BattleSorting.Fx, _sortingOrder + 2);
            BattleSorting.Apply(_background, BattleSorting.Fx, _sortingOrder);
            BattleSorting.Apply(_icon, BattleSorting.Fx, _sortingOrder + 3);
        }
    }
}
