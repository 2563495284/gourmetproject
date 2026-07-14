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

        public void Bind(FoodSummaryTipsData data)
        {
            EnsureStructure();
            data ??= FoodSummaryTipsData.Empty;

            _nameText.text = data.FoodName;
            BuildSkills(data.Skills);
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
            EnsureStructure();
        }

        private void Reset()
        {
            EnsureStructure();
        }

        private void BuildSkills(IReadOnlyList<FoodInfoEntry> skills)
        {
            FoodTipUiUtility.ClearChildren(_skillsContent);
            int count = skills != null ? skills.Count : 0;
            _skillsContent.gameObject.SetActive(count > 0);

            for (int i = 0; i < count; i++)
            {
                FoodInfoEntry skill = skills[i];
                RectTransform cardRect = FoodTipUiUtility.CreateChild(_skillsContent, $"Skill_{i}");
                var card = cardRect.gameObject.AddComponent<FoodTipCardView>();
                card.Bind(skill.Title, skill.Desc);
            }
        }

        private void BuildFlavors(IReadOnlyList<string> flavors)
        {
            FoodTipUiUtility.ClearChildren(_flavorContent);
            int count = flavors != null ? flavors.Count : 0;
            _flavorContent.gameObject.SetActive(count > 0);
            LayoutElement flavorRootLayout = _flavorContent.gameObject.GetComponent<LayoutElement>() ?? _flavorContent.gameObject.AddComponent<LayoutElement>();
            if (count > 0)
            {
                int rows = Mathf.CeilToInt(count / 3f);
                flavorRootLayout.minHeight = rows * 44f + Mathf.Max(0, rows - 1) * 8f;
                flavorRootLayout.preferredHeight = flavorRootLayout.minHeight;
            }

            for (int i = 0; i < count; i++)
            {
                RectTransform tag = FoodTipUiUtility.CreateChild(_flavorContent, $"Flavor_{i}");
                FoodTipUiUtility.EnsurePanelImage(tag.gameObject, new Color(1f, 1f, 1f, 0.96f));
                Text text = FoodTipUiUtility.EnsureTextChild(tag, null, "Label", 20, FontStyle.Normal, TextAnchor.MiddleCenter);
                text.text = flavors[i] ?? string.Empty;

                LayoutElement layout = tag.gameObject.GetComponent<LayoutElement>() ?? tag.gameObject.AddComponent<LayoutElement>();
                layout.minWidth = 72f;
                layout.preferredWidth = 96f;
                layout.minHeight = 44f;
                layout.preferredHeight = 44f;
            }
        }

        private void EnsureStructure()
        {
            RectTransform root = FoodTipUiUtility.EnsureRect(gameObject);
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 330f);
            FoodTipUiUtility.EnsurePanelImage(gameObject, new Color(1f, 1f, 1f, 0.96f));
            _canvasGroup ??= gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            var layout = gameObject.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
            {
                layout = gameObject.AddComponent<VerticalLayoutGroup>();
            }

            layout.padding = new RectOffset(12, 12, 10, 12);
            layout.spacing = 10f;
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

            _nameText = FoodTipUiUtility.EnsureTextChild(root, _nameText, "Name", 26, FontStyle.Bold, TextAnchor.MiddleCenter);

            if (_skillsContent == null)
            {
                _skillsContent = root.Find("Skills") as RectTransform;
                if (_skillsContent == null)
                {
                    _skillsContent = FoodTipUiUtility.CreateChild(root, "Skills");
                }
            }

            var skillsLayout = _skillsContent.gameObject.GetComponent<VerticalLayoutGroup>();
            if (skillsLayout == null)
            {
                skillsLayout = _skillsContent.gameObject.AddComponent<VerticalLayoutGroup>();
            }

            skillsLayout.spacing = 8f;
            skillsLayout.childControlWidth = true;
            skillsLayout.childControlHeight = true;
            skillsLayout.childForceExpandWidth = true;
            skillsLayout.childForceExpandHeight = false;

            var skillsFitter = _skillsContent.gameObject.GetComponent<ContentSizeFitter>();
            if (skillsFitter == null)
            {
                skillsFitter = _skillsContent.gameObject.AddComponent<ContentSizeFitter>();
            }

            skillsFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            skillsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (_flavorContent == null)
            {
                _flavorContent = root.Find("Flavors") as RectTransform;
                if (_flavorContent == null)
                {
                    _flavorContent = FoodTipUiUtility.CreateChild(root, "Flavors");
                }
            }

            var flavorLayout = _flavorContent.gameObject.GetComponent<GridLayoutGroup>();
            if (flavorLayout == null)
            {
                flavorLayout = _flavorContent.gameObject.AddComponent<GridLayoutGroup>();
            }

            flavorLayout.cellSize = new Vector2(96f, 44f);
            flavorLayout.spacing = new Vector2(8f, 8f);
            flavorLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            flavorLayout.constraintCount = 3;
            flavorLayout.childAlignment = TextAnchor.MiddleCenter;

            LayoutElement flavorsElement = _flavorContent.gameObject.GetComponent<LayoutElement>() ?? _flavorContent.gameObject.AddComponent<LayoutElement>();
            flavorsElement.minHeight = 44f;
            flavorsElement.preferredHeight = 44f;
        }
    }
}
