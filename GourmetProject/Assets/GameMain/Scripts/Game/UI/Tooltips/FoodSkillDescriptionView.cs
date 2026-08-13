using GourmetProject.Game.Visual;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>Summary 内专用的技能描述卡。</summary>
    public sealed class FoodSkillDescriptionView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _descText;

        public float PreferredDescWidth => _descText != null ? _descText.preferredWidth : 0f;

        public float PreferredDescWidthFor(string value)
        {
            return _descText != null
                ? _descText.GetPreferredValues(value ?? string.Empty).x
                : 0f;
        }

        public void Bind(string desc, bool debuffed = false)
        {
            if (!ValidateReferences())
            {
                return;
            }

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
                if (graphic == null || graphic is TMP_Text)
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

            Debug.LogError($"{nameof(FoodSkillDescriptionView)} on '{name}' is missing prefab reference '{fieldName}'.", this);
            return false;
        }
    }
}
