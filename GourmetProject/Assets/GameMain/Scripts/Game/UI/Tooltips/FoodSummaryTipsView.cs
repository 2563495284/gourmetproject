using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 模块 3：食物名字、技能、风味标签。风味可空/可多，容器按内容自适应。
    /// </summary>
    public sealed class FoodSummaryTipsView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private RectTransform _duplicateView;
        [SerializeField] private RectTransform _countAsView;
        [SerializeField] private TMP_Text _countAsText;
        [SerializeField] private RectTransform _skillsContent;
        [SerializeField] private RectTransform _flavorContent;
        [SerializeField] private FoodTipCardView _skillCardPrefab;
        [SerializeField] private FoodFlavorTagView _flavorTagPrefab;

        private const int MaxFlavorColumns = 1;
        private const float FlavorCellHeight = 44f;
        private const float FlavorCellWidth = 96f;
        private const float FlavorRowSpacing = 8f;
        private const float MaxWidth = 330f;
        private const float SummaryHorizontalPadding = 24f;
        private const float SkillCardHorizontalPadding = 20f;
        private const float SkillDescPanelHorizontalPadding = 20f;
        private const string MinWidthSampleText = "十十十十十十十十十十";

        public void Bind(FoodSummaryTipsData data)
        {
            if (!ValidateReferences())
            {
                return;
            }

            data ??= FoodSummaryTipsData.Empty;

            _nameText.text = data.FoodName;
            _duplicateView.gameObject.SetActive(data.IsTemporaryCopy);
            bool showCountAs = data.CountAs > 1;
            _countAsView.gameObject.SetActive(showCountAs);
            _countAsText.text = data.CountAs.ToString();
            float skillsTextWidth = BuildSkills(data.Skills, data.SkillsDisabled);
            BuildFlavors(data.Flavors);
            ResizeToContent(skillsTextWidth, data.Flavors);
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

        private float BuildSkills(IReadOnlyList<FoodInfoEntry> skills, bool debuffed)
        {
            FoodTipUiUtility.ClearChildren(_skillsContent);
            int count = skills != null ? skills.Count : 0;
            _skillsContent.gameObject.SetActive(count > 0);

            float maxTextWidth = 0f;
            for (int i = 0; i < count; i++)
            {
                FoodInfoEntry skill = skills[i];
                FoodTipCardView card = Instantiate(_skillCardPrefab, _skillsContent, false);
                card.name = $"Skill_{i}";
                card.Bind(skill.Title, skill.Desc, debuffed);
                maxTextWidth = Mathf.Max(maxTextWidth, card.PreferredDescWidth, card.PreferredTitleWidth);
            }

            return maxTextWidth;
        }

        private void BuildFlavors(IReadOnlyList<string> flavors)
        {
            FoodTipUiUtility.ClearChildren(_flavorContent);
            int count = flavors != null ? flavors.Count : 0;
            _flavorContent.gameObject.SetActive(count > 0);
            LayoutElement flavorRootLayout = _flavorContent.gameObject.GetComponent<LayoutElement>();
            GridLayoutGroup flavorGrid = _flavorContent.gameObject.GetComponent<GridLayoutGroup>();
            if (count > 0)
            {
                int columns = Mathf.Clamp(count, 1, MaxFlavorColumns);
                int rows = Mathf.CeilToInt(count / (float)columns);
                float preferredHeight = rows * FlavorCellHeight + Mathf.Max(0, rows - 1) * FlavorRowSpacing;
                if (flavorGrid != null)
                {
                    flavorGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                    flavorGrid.constraintCount = columns;
                }

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

        private void ResizeToContent(float skillsTextWidth, IReadOnlyList<string> flavors)
        {
            if (transform is not RectTransform rect)
            {
                return;
            }

            float preferredWidth = _nameText != null
                ? _nameText.preferredWidth + SummaryHorizontalPadding
                : 0f;

            preferredWidth = Mathf.Max(preferredWidth, MinWidthForTenDescCharacters());

            if (skillsTextWidth > 0f)
            {
                preferredWidth = Mathf.Max(
                    preferredWidth,
                    skillsTextWidth
                    + SummaryHorizontalPadding
                    + SkillCardHorizontalPadding
                    + SkillDescPanelHorizontalPadding);
            }

            if (flavors != null && flavors.Count > 0)
            {
                preferredWidth = Mathf.Max(preferredWidth, FlavorCellWidth + SummaryHorizontalPadding);
            }

            float width = Mathf.Min(preferredWidth, MaxWidth);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        }

        private float MinWidthForTenDescCharacters()
        {
            if (_skillCardPrefab == null)
            {
                return 0f;
            }

            return _skillCardPrefab.PreferredDescWidthFor(MinWidthSampleText)
                + SummaryHorizontalPadding
                + SkillCardHorizontalPadding
                + SkillDescPanelHorizontalPadding;
        }

        private bool ValidateReferences()
        {
            bool valid = true;
            valid &= ReportMissing(_nameText, nameof(_nameText));
            valid &= ReportMissing(_duplicateView, nameof(_duplicateView));
            valid &= ReportMissing(_countAsView, nameof(_countAsView));
            valid &= ReportMissing(_countAsText, nameof(_countAsText));
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
