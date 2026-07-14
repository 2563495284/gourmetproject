using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>食物 Tips 内复用的小卡片：标题 + 描述。</summary>
    public sealed class FoodTipCardView : MonoBehaviour
    {
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _descText;

        public void Bind(string title, string desc)
        {
            EnsureStructure();

            bool hasTitle = !string.IsNullOrEmpty(title);
            _titleText.gameObject.SetActive(hasTitle);
            _titleText.text = title ?? string.Empty;
            _descText.text = desc ?? string.Empty;
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
            FoodTipUiUtility.EnsurePanelImage(gameObject, new Color(1f, 1f, 1f, 0.96f));

            var layout = gameObject.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
            {
                layout = gameObject.AddComponent<VerticalLayoutGroup>();
            }

            layout.padding = new RectOffset(10, 10, 8, 10);
            layout.spacing = 5f;
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

            _titleText = FoodTipUiUtility.EnsureTextChild(rect, _titleText, "Title", 22, FontStyle.Bold, TextAnchor.MiddleCenter);
            _descText = FoodTipUiUtility.EnsureTextChild(rect, _descText, "Desc", 18, FontStyle.Normal, TextAnchor.UpperCenter);
            _descText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _descText.verticalOverflow = VerticalWrapMode.Overflow;
        }
    }
}
