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

        [SerializeField] private Image _backgroundImage;
        [SerializeField] private Sprite _defaultBackgroundSprite;
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
            SetBackground(bgSpritePath);

            int count = options?.Count ?? 0;
            bool showResultButton = !string.IsNullOrWhiteSpace(resultButtonText) || count == 0;
            if (_optionsRoot != null)
            {
                _optionsRoot.gameObject.SetActive(showResultButton || count > 0);
            }

            if (showResultButton)
            {
                CreateResultButton(resultButtonText, onEnd);
                return;
            }

            for (int i = 0; i < count; i++)
            {
                bool interactable = optionEnabled == null || i >= optionEnabled.Count || optionEnabled[i];
                CreateOption(i, options[i], interactable, onPick);
            }
        }

        public void Close()
        {
            ClearButtons();
            gameObject.SetActive(false);
        }

        private void CreateOption(int index, string label, bool interactable, Action<int> onPick)
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

        private void CreateResultButton(string resultText, Action onEnd)
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
                ApplyResultButtonWidth(button, text);
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

        private void SetBackground(string spritePath)
        {
            if (_backgroundImage == null)
            {
                return;
            }

            // Sprite sprite = null;
            // if (!string.IsNullOrWhiteSpace(spritePath))
            // {
            //     sprite = Resources.Load<Sprite>(spritePath);
            // }

            // if (sprite == null)
            // {
            //     sprite = _defaultBackgroundSprite != null
            //         ? _defaultBackgroundSprite
            //         : Resources.Load<Sprite>("Sprites/UI/card_action_event");
            // }

            // _backgroundImage.sprite = sprite;
            // _backgroundImage.enabled = sprite != null;
            // _backgroundImage.color = sprite != null ? Color.white : new Color(0.12f, 0.1f, 0.08f, 0.92f);
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
