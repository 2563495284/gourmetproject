using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 事件页面中部面板：先展示事件选项，选择后改为展示以结果文本为标签的结束按钮。
    /// 规则与结算由 WeekLoopController / EventService 驱动，本组件只负责数据绑定和点击回调。
    /// </summary>
    public sealed class EventPagePanel : MonoBehaviour
    {
        private const float ResultButtonMinWidth = 128f;
        private const float ResultButtonHorizontalPadding = 56f;
        private const float OptionHeight = 100f;
        private const float RequirementHeight = 28f;
        private const float RequirementInset = 8f;
        // 给顶部常驻时间轴留出空间，避免节点和进度线压在事件插画上。
        private static readonly Vector2 IllustrationAnchorMin = new(0.05f, 0.465f);
        private static readonly Vector2 IllustrationAnchorMax = new(0.95f, 0.845f);

        [SerializeField] private Image _backgroundImage;
        [SerializeField] private Sprite _defaultBackgroundSprite;
        [SerializeField] private Image _illustrationImage;
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _descriptionText;
        [SerializeField] private RectTransform _optionsRoot;
        [SerializeField] private Button _optionButtonTemplate;
        private readonly List<Button> _spawnedButtons = new();
        private bool _resolved;

        public void Open(
            string title,
            string description,
            string resultButtonText,
            string bgSpritePath,
            IReadOnlyList<string> options,
            IReadOnlyList<string> optionRequirements,
            IReadOnlyList<bool> optionEnabled,
            Action<int> onPick,
            Action onEnd)
        {
            ClearButtons();
            _resolved = false;
            gameObject.SetActive(true);

            SetText(_titleText, title);
            SetText(_descriptionText, description);
            SetVisible(_descriptionText, !string.IsNullOrWhiteSpace(description));
            SetIllustration(bgSpritePath);

            int count = options?.Count ?? 0;
            bool showResultButton = !string.IsNullOrWhiteSpace(resultButtonText) || count == 0;
            if (_optionsRoot != null)
            {
                _optionsRoot.gameObject.SetActive(showResultButton || count > 0);
            }

            for (int i = 0; i < count; i++)
            {
                bool interactable = optionEnabled == null || i >= optionEnabled.Count || optionEnabled[i];
                string requirement = optionRequirements != null && i < optionRequirements.Count
                    ? optionRequirements[i]
                    : string.Empty;
                CreateOption(i, options[i], requirement, interactable, onPick);
            }

            if (showResultButton)
            {
                CreateResultButton(resultButtonText, onEnd, styleAsOption: count > 0);
            }
        }

        public void Close()
        {
            ClearButtons();
            gameObject.SetActive(false);
        }

        private void CreateOption(
            int index,
            string label,
            string requirement,
            bool interactable,
            Action<int> onPick)
        {
            if (_optionsRoot == null || _optionButtonTemplate == null)
            {
                return;
            }

            Button button = Instantiate(_optionButtonTemplate, _optionsRoot);
            button.gameObject.name = $"EventOption_{index + 1}";
            button.gameObject.SetActive(true);
            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = string.IsNullOrWhiteSpace(label) ? "继续" : label;
                ApplyOptionVisual(button, text, requirement, interactable);
            }

            button.onClick.RemoveAllListeners();
            button.interactable = interactable;
            if (interactable)
            {
                button.onClick.AddListener(() =>
                {
                    if (_resolved)
                    {
                        return;
                    }

                    _resolved = true;
                    ClearButtons();
                    onPick?.Invoke(index);
                });
            }
            _spawnedButtons.Add(button);
        }

        private void ApplyOptionVisual(
            Button button,
            TMP_Text title,
            string requirement,
            bool interactable)
        {
            bool hasRequirement = !string.IsNullOrWhiteSpace(requirement);
            if (button.transform is RectTransform buttonRect)
            {
                buttonRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, OptionHeight);
            }

            OptionPalette palette = ResolveOptionPalette();
            Image rootImage = button.targetGraphic as Image ?? button.GetComponent<Image>();
            if (rootImage != null)
            {
                button.targetGraphic = rootImage;
                Color main = interactable ? palette.Main : palette.DisabledMain;
                ColorBlock colors = button.colors;
                colors.normalColor = main;
                colors.highlightedColor = Color.Lerp(main, Color.white, 0.1f);
                colors.pressedColor = Color.Lerp(main, Color.black, 0.14f);
                colors.selectedColor = colors.highlightedColor;
                colors.disabledColor = palette.DisabledMain;
                colors.colorMultiplier = 1f;
                colors.fadeDuration = 0.08f;
                button.colors = colors;
                rootImage.color = Color.white;
            }

            title.color = interactable ? palette.Title : palette.DisabledTitle;
            title.fontWeight = FontWeight.Medium;
            title.fontSize = hasRequirement ? 28f : 30f;
            title.fontSizeMax = title.fontSize;
            ConfigureTextRect(title.rectTransform, hasRequirement);

            if (!hasRequirement)
            {
                return;
            }

            Image requirementBackground = CreateRequirementBackground(
                button.transform,
                rootImage != null ? rootImage.sprite : null);
            requirementBackground.color = interactable ? palette.Requirement : palette.DisabledRequirement;

            TMP_Text requirementText = Instantiate(title, button.transform);
            requirementText.gameObject.name = "RequirementText";
            requirementText.text = requirement;
            requirementText.color = interactable ? palette.RequirementText : palette.DisabledRequirementText;
            requirementText.fontWeight = FontWeight.Regular;
            requirementText.fontSize = 19f;
            requirementText.fontSizeMax = 19f;
            requirementText.fontSizeMin = 12f;
            requirementText.alignment = TextAlignmentOptions.Center;
            ConfigureRequirementRect(requirementText.rectTransform);
            requirementText.transform.SetAsLastSibling();
        }

        private static Image CreateRequirementBackground(Transform parent, Sprite sprite)
        {
            var gameObject = new GameObject(
                "RequirementBackground",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            gameObject.layer = parent.gameObject.layer;
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            ConfigureRequirementRect(rect);
            gameObject.transform.SetAsFirstSibling();

            Image image = gameObject.GetComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.raycastTarget = false;
            return image;
        }

        private static void ConfigureTextRect(RectTransform rect, bool hasRequirement)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(8f, hasRequirement
                ? RequirementInset + RequirementHeight + 4f
                : 4f);
            rect.offsetMax = new Vector2(-8f, -4f);
        }

        private static void ConfigureRequirementRect(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(RequirementInset, RequirementInset);
            rect.offsetMax = new Vector2(-RequirementInset, RequirementInset + RequirementHeight);
        }

        private static OptionPalette ResolveOptionPalette()
        {
            return new OptionPalette(
                Hex("C4EDAC"), Hex("438F47"), Hex("173D1C"), Hex("FFFAF0"),
                Hex("D6D8D2"), Hex("5D625B"), Hex("737870"), Hex("F4F5F1"));
        }

        private static Color Hex(string value)
        {
            return ColorUtility.TryParseHtmlString($"#{value}", out Color color)
                ? color
                : Color.white;
        }

        private readonly struct OptionPalette
        {
            public OptionPalette(
                Color main,
                Color requirement,
                Color title,
                Color requirementText,
                Color disabledMain,
                Color disabledRequirement,
                Color disabledTitle,
                Color disabledRequirementText)
            {
                Main = main;
                Requirement = requirement;
                Title = title;
                RequirementText = requirementText;
                DisabledMain = disabledMain;
                DisabledRequirement = disabledRequirement;
                DisabledTitle = disabledTitle;
                DisabledRequirementText = disabledRequirementText;
            }

            public Color Main { get; }
            public Color Requirement { get; }
            public Color Title { get; }
            public Color RequirementText { get; }
            public Color DisabledMain { get; }
            public Color DisabledRequirement { get; }
            public Color DisabledTitle { get; }
            public Color DisabledRequirementText { get; }
        }

        private void CreateResultButton(string resultText, Action onEnd, bool styleAsOption)
        {
            if (_optionsRoot == null || _optionButtonTemplate == null)
            {
                onEnd?.Invoke();
                return;
            }

            Button button = Instantiate(_optionButtonTemplate, _optionsRoot);
            button.gameObject.name = "EventResult";
            button.gameObject.SetActive(true);

            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = string.IsNullOrWhiteSpace(resultText) ? "结束" : resultText;
                if (styleAsOption)
                {
                    ApplyOptionVisual(button, text, string.Empty, interactable: true);
                }
                else
                {
                    ApplyResultButtonWidth(button, text);
                }
            }

            button.onClick.RemoveAllListeners();
            button.interactable = true;
            button.onClick.AddListener(() =>
            {
                if (_resolved)
                {
                    return;
                }

                _resolved = true;
                onEnd?.Invoke();
            });
            _spawnedButtons.Add(button);
        }

        private static void ApplyResultButtonWidth(Button button, TMP_Text label)
        {
            float width = Mathf.Max(ResultButtonMinWidth, label.preferredWidth + ResultButtonHorizontalPadding);
            if (button.transform is RectTransform rectTransform)
            {
                rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            }

            LayoutElement layoutElement = button.GetComponent<LayoutElement>();
            if (layoutElement != null)
            {
                layoutElement.minWidth = width;
                layoutElement.preferredWidth = width;
                layoutElement.flexibleWidth = 0f;
            }
        }

        private void ClearButtons()
        {
            foreach (Button button in _spawnedButtons)
            {
                if (button != null)
                {
                    button.gameObject.SetActive(false);
                    if (Application.isPlaying)
                    {
                        Destroy(button.gameObject);
                    }
                    else
                    {
                        DestroyImmediate(button.gameObject);
                    }
                }
            }

            _spawnedButtons.Clear();
            if (_optionButtonTemplate != null)
            {
                _optionButtonTemplate.gameObject.SetActive(false);
            }
        }

        private void SetIllustration(string spritePath)
        {
            Image illustration = EnsureIllustrationImage();
            if (illustration == null)
            {
                return;
            }

            string resourcePath = NormalizeResourcePath(spritePath);
            Sprite sprite = string.IsNullOrWhiteSpace(resourcePath)
                ? null
                : Resources.Load<Sprite>(resourcePath);
            illustration.sprite = sprite;
            illustration.enabled = sprite != null;
            illustration.gameObject.SetActive(sprite != null);

            if (sprite == null && !string.IsNullOrWhiteSpace(resourcePath))
            {
                Debug.LogWarning($"事件插画加载失败：{resourcePath}");
            }
        }

        private Image EnsureIllustrationImage()
        {
            if (_illustrationImage != null)
            {
                return _illustrationImage;
            }

            var illustrationObject = new GameObject(
                "Illustration",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            illustrationObject.layer = gameObject.layer;
            RectTransform rect = illustrationObject.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = IllustrationAnchorMin;
            rect.anchorMax = IllustrationAnchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetSiblingIndex(Mathf.Max(0, transform.childCount - 2));

            _illustrationImage = illustrationObject.GetComponent<Image>();
            _illustrationImage.type = Image.Type.Simple;
            _illustrationImage.preserveAspect = true;
            _illustrationImage.raycastTarget = false;
            _illustrationImage.color = Color.white;
            _illustrationImage.enabled = false;
            illustrationObject.SetActive(false);
            return _illustrationImage;
        }

        private static string NormalizeResourcePath(string spritePath)
        {
            if (string.IsNullOrWhiteSpace(spritePath))
            {
                return string.Empty;
            }

            string path = spritePath.Trim().Replace('\\', '/');
            const string resourcesSegment = "/Resources/";
            int resourcesIndex = path.IndexOf(resourcesSegment, StringComparison.OrdinalIgnoreCase);
            if (resourcesIndex >= 0)
            {
                path = path.Substring(resourcesIndex + resourcesSegment.Length);
            }

            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(0, path.Length - 4);
            }

            return path.TrimStart('/');
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }

        private static void SetVisible(Behaviour component, bool visible)
        {
            if (component != null)
            {
                component.gameObject.SetActive(visible);
            }
        }
    }
}
