using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>材质 Tips 内的单条材质描述：材质名 + 描述。</summary>
    public sealed class FoodMaterialTipItemView : MonoBehaviour
    {
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _descText;

        public void Bind(FoodMaterialTipsEntry material)
        {
            EnsureStructure();

            _nameText.text = material?.Name ?? string.Empty;
            _descText.text = material?.Desc ?? string.Empty;
        }

        private void Awake()
        {
            EnsureStructure();
        }

        private void Reset()
        {
            EnsureStructure();
        }

        private void EnsureStructure()
        {
            RectTransform rect = FoodTipUiUtility.EnsureRect(gameObject);

            var layout = gameObject.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
            {
                layout = gameObject.AddComponent<VerticalLayoutGroup>();
            }

            layout.padding = new RectOffset(4, 4, 0, 0);
            layout.spacing = 2f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = gameObject.GetComponent<ContentSizeFitter>();
            if (fitter == null)
            {
                fitter = gameObject.AddComponent<ContentSizeFitter>();
            }

            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _nameText = FoodTipUiUtility.EnsureTextChild(rect, _nameText, "Name", 20, FontStyle.Bold, TextAnchor.MiddleCenter);
            _descText = FoodTipUiUtility.EnsureTextChild(rect, _descText, "Desc", 17, FontStyle.Normal, TextAnchor.UpperCenter);
            _descText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _descText.verticalOverflow = VerticalWrapMode.Overflow;

            LayoutElement layoutElement = gameObject.GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = gameObject.AddComponent<LayoutElement>();
            }

            layoutElement.minHeight = 58f;
            layoutElement.preferredHeight = -1f;
        }
    }
}
