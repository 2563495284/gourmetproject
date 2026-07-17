using GourmetProject.Game.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>食物 Tips 内复用的小卡片：标题 + 描述。</summary>
    public sealed class FoodTipCardView : MonoBehaviour
    {
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _descText;

        public void Bind(string title, string desc, bool debuffed = false)
        {
            if (!ValidateReferences())
            {
                return;
            }

            bool hasTitle = !string.IsNullOrEmpty(title);
            _titleText.gameObject.SetActive(hasTitle);
            _titleText.text = title ?? string.Empty;
            _descText.text = desc ?? string.Empty;
            SetDebuffed(debuffed);
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

        private void SetDebuffed(bool debuffed)
        {
            Graphic[] graphics = GetComponentsInChildren<Graphic>(true);
            bool appliedToBackground = false;
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null || graphic is Text)
                {
                    continue;
                }

                SetDebuffMaterial(graphic, debuffed);
                appliedToBackground = true;
            }

            if (appliedToBackground)
            {
                return;
            }

            for (int i = 0; i < graphics.Length; i++)
            {
                SetDebuffMaterial(graphics[i], debuffed);
            }
        }

        private static void SetDebuffMaterial(Graphic graphic, bool debuffed)
        {
            if (graphic == null)
            {
                return;
            }

            if (debuffed)
            {
                DebuffVisualStyle.ApplyToGraphic(graphic);
            }
            else if (DebuffVisualStyle.IsAppliedToGraphic(graphic))
            {
                DebuffVisualStyle.ClearGraphic(graphic);
            }
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
