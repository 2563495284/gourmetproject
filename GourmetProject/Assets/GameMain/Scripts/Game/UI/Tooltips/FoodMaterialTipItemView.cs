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
            if (!ValidateReferences())
            {
                return;
            }

            _nameText.text = material?.Name ?? string.Empty;
            _descText.text = material?.Desc ?? string.Empty;
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
            valid &= ReportMissing(_nameText, nameof(_nameText));
            valid &= ReportMissing(_descText, nameof(_descText));
            return valid;
        }

        private bool ReportMissing(Object reference, string fieldName)
        {
            if (reference != null)
            {
                return true;
            }

            Debug.LogError($"{nameof(FoodMaterialTipItemView)} on '{name}' is missing prefab reference '{fieldName}'.", this);
            return false;
        }
    }
}
