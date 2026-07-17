using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 模块 3：食物名字、技能、风味标签。风味可空/可多，容器按内容自适应。
    /// </summary>
    public sealed class FoodSummaryTipsView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Text _nameText;
        [SerializeField] private RectTransform _skillsContent;
        [SerializeField] private RectTransform _flavorContent;
        [SerializeField] private FoodTipCardView _skillCardPrefab;
        [SerializeField] private FoodFlavorTagView _flavorTagPrefab;

        public void Bind(FoodSummaryTipsData data)
        {
            if (!ValidateReferences())
            {
                return;
            }

            data ??= FoodSummaryTipsData.Empty;

            _nameText.text = data.FoodName;
            BuildSkills(data.Skills, data.SkillsDisabled);
            BuildFlavors(data.Flavors);
            Show();
        }

        public void Show()
        {
            gameObject.SetActive(true);
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }
        }

        public void Hide()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }

            gameObject.SetActive(false);
        }

        private void Awake()
        {
            ValidateReferences();
        }

        private void Reset()
        {
            ValidateReferences();
        }

        private void BuildSkills(IReadOnlyList<FoodInfoEntry> skills, bool debuffed)
        {
            FoodTipUiUtility.ClearChildren(_skillsContent);
            int count = skills != null ? skills.Count : 0;
            _skillsContent.gameObject.SetActive(count > 0);

            for (int i = 0; i < count; i++)
            {
                FoodInfoEntry skill = skills[i];
                FoodTipCardView card = Instantiate(_skillCardPrefab, _skillsContent, false);
                card.name = $"Skill_{i}";
                card.Bind(skill.Title, skill.Desc, debuffed);
            }
        }

        private void BuildFlavors(IReadOnlyList<string> flavors)
        {
            FoodTipUiUtility.ClearChildren(_flavorContent);
            int count = flavors != null ? flavors.Count : 0;
            _flavorContent.gameObject.SetActive(count > 0);
            LayoutElement flavorRootLayout = _flavorContent.gameObject.GetComponent<LayoutElement>();
            if (count > 0)
            {
                int rows = Mathf.CeilToInt(count / 3f);
                float preferredHeight = rows * 44f + Mathf.Max(0, rows - 1) * 8f;
                if (flavorRootLayout != null)
                {
                    flavorRootLayout.minHeight = preferredHeight;
                    flavorRootLayout.preferredHeight = preferredHeight;
                }
            }

            for (int i = 0; i < count; i++)
            {
                FoodFlavorTagView tag = Instantiate(_flavorTagPrefab, _flavorContent, false);
                tag.name = $"Flavor_{i}";
                tag.Bind(flavors[i]);
            }
        }

        private bool ValidateReferences()
        {
            bool valid = true;
            valid &= ReportMissing(_nameText, nameof(_nameText));
            valid &= ReportMissing(_skillsContent, nameof(_skillsContent));
            valid &= ReportMissing(_flavorContent, nameof(_flavorContent));
            valid &= ReportMissing(_skillCardPrefab, nameof(_skillCardPrefab));
            valid &= ReportMissing(_flavorTagPrefab, nameof(_flavorTagPrefab));
            return valid;
        }

        private bool ReportMissing(Object reference, string fieldName)
        {
            if (reference != null)
            {
                return true;
            }

            Debug.LogError($"{nameof(FoodSummaryTipsView)} on '{name}' is missing prefab reference '{fieldName}'.", this);
            return false;
        }
    }
}
