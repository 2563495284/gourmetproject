using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 事件页面中部面板：展示事件环境背景、标题/正文、结果文本、后续选项与结束按钮。
    /// 规则与结算由 WeekLoopController / EventService 驱动，本组件只负责数据绑定和点击回调。
    /// </summary>
    public sealed class EventPagePanel : MonoBehaviour
    {
        [SerializeField] private Image _backgroundImage;
        [SerializeField] private Sprite _defaultBackgroundSprite;
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _descriptionText;
        [SerializeField] private Text _resultText;
        [SerializeField] private RectTransform _optionsRoot;
        [SerializeField] private Button _optionButtonTemplate;
        [SerializeField] private Button _endButton;
        [SerializeField] private Text _endButtonLabel;

        private readonly List<Button> _spawnedOptions = new();
        private bool _resolved;

        public void Open(
            string title,
            string description,
            string result,
            string bgSpritePath,
            IReadOnlyList<string> options,
            IReadOnlyList<bool> optionEnabled,
            bool showEndButton,
            string endButtonText,
            Action<int> onPick,
            Action onEnd)
        {
            EnsureRefs();
            ClearOptions();
            _resolved = false;
            gameObject.SetActive(true);

            SetText(_titleText, title);
            SetText(_descriptionText, description);
            SetText(_resultText, result);
            SetVisible(_descriptionText, !string.IsNullOrWhiteSpace(description));
            SetVisible(_resultText, !string.IsNullOrWhiteSpace(result));
            SetBackground(bgSpritePath);

            int count = options?.Count ?? 0;
            if (_optionsRoot != null)
            {
                _optionsRoot.gameObject.SetActive(count > 0);
            }

            for (int i = 0; i < count; i++)
            {
                bool interactable = optionEnabled == null || i >= optionEnabled.Count || optionEnabled[i];
                CreateOption(i, options[i], interactable, onPick);
            }

            if (_endButton != null)
            {
                _endButton.gameObject.SetActive(showEndButton);
                _endButton.onClick.RemoveAllListeners();
                _endButton.onClick.AddListener(() =>
                {
                    if (_resolved)
                    {
                        return;
                    }

                    _resolved = true;
                    onEnd?.Invoke();
                });
            }

            if (_endButtonLabel != null)
            {
                _endButtonLabel.text = string.IsNullOrWhiteSpace(endButtonText) ? "结束" : endButtonText;
            }
        }

        public void Close()
        {
            ClearOptions();
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
            Text text = button.GetComponentInChildren<Text>(true);
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
                    onPick?.Invoke(index);
                });
            }
            _spawnedOptions.Add(button);
        }

        private void ClearOptions()
        {
            foreach (Button button in _spawnedOptions)
            {
                if (button != null)
                {
                    Destroy(button.gameObject);
                }
            }

            _spawnedOptions.Clear();
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

            Sprite sprite = null;
            if (!string.IsNullOrWhiteSpace(spritePath))
            {
                sprite = Resources.Load<Sprite>(spritePath);
            }

            if (sprite == null)
            {
                sprite = _defaultBackgroundSprite != null
                    ? _defaultBackgroundSprite
                    : Resources.Load<Sprite>("Sprites/UI/card_action_event");
            }

            _backgroundImage.sprite = sprite;
            _backgroundImage.enabled = sprite != null;
            _backgroundImage.color = sprite != null ? Color.white : new Color(0.12f, 0.1f, 0.08f, 0.92f);
        }

        private void EnsureRefs()
        {
            if (_backgroundImage == null)
            {
                Transform bg = transform.Find("Background");
                _backgroundImage = bg != null ? bg.GetComponent<Image>() : null;
            }

            if (_titleText == null)
            {
                Transform title = transform.Find("Content/Top/Title");
                _titleText = title != null ? title.GetComponent<Text>() : null;
            }

            if (_descriptionText == null)
            {
                Transform desc = transform.Find("Content/Top/Description");
                _descriptionText = desc != null ? desc.GetComponent<Text>() : null;
            }

            if (_resultText == null)
            {
                Transform result = transform.Find("Content/Bottom/Result");
                _resultText = result != null ? result.GetComponent<Text>() : null;
            }

            if (_optionsRoot == null)
            {
                Transform options = transform.Find("Content/Bottom/Options");
                _optionsRoot = options as RectTransform;
            }

            if (_optionButtonTemplate == null && _optionsRoot != null)
            {
                Transform template = _optionsRoot.Find("OptionTemplate");
                _optionButtonTemplate = template != null ? template.GetComponent<Button>() : null;
            }

            if (_endButton == null)
            {
                Transform end = transform.Find("Content/Bottom/EndButton");
                _endButton = end != null ? end.GetComponent<Button>() : null;
            }

            if (_endButtonLabel == null && _endButton != null)
            {
                _endButtonLabel = _endButton.GetComponentInChildren<Text>(true);
            }
        }

        private static void SetText(Text text, string value)
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
