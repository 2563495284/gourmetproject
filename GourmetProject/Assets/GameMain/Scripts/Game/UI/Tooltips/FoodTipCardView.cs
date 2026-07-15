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
            if (!ValidateReferences())
            {
                return;
            }

            bool hasTitle = !string.IsNullOrEmpty(title);
            _titleText.gameObject.SetActive(hasTitle);
            _titleText.text = title ?? string.Empty;
            _descText.text = desc ?? string.Empty;
        }

        private void Awake()
        {
            ValidateReferences();
        }

        private void Reset()
        {
            ValidateReferences();
        }

        private bool ValidateReferences()
        {
            bool valid = true;
            valid &= ReportMissing(_titleText, nameof(_titleText));
            valid &= ReportMissing(_descText, nameof(_descText));
            return valid;
        }

        private bool ReportMissing(Object reference, string fieldName)
        {
            if (reference != null)
            {
                return true;
            }

            Debug.LogError($"{nameof(FoodTipCardView)} on '{name}' is missing prefab reference '{fieldName}'.", this);
            return false;
        }
    }
}
