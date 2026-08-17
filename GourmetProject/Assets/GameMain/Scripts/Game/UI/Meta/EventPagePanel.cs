using System;
using System.Collections.Generic;
using GourmetProject.Game.UI.Common;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 事件页面中部面板：先展示事件选项，选择后改为展示以结果文本为标签的结束按钮。
    /// 规则与结算由 WeekLoopController / EventService 驱动，本组件只负责数据绑定和点击回调；
    /// 版式与视觉全部来自 EventPanel.prefab 与选项模板，代码不再运行时创建节点或改样式。
    /// </summary>
    public sealed class EventPagePanel : MonoBehaviour
    {
        [SerializeField] private Image _illustrationImage;
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _descriptionText;
        [SerializeField] private RectTransform _optionsRoot;

        [Tooltip("事件选项模板；有选项时的结束按钮也复用它。")]
        [SerializeField] private EventOptionView _optionTemplate;

        [Tooltip("无选项时的独立结束按钮模板，留空则回退到选项模板。")]
        [SerializeField] private EventOptionView _resultTemplate;

        private readonly List<EventOptionView> _spawnedOptions = new();
        private bool _resolved;
        private Tween _autoContinueTween;

        public void Open(
            string title,
            string description,
            string resultButtonText,
            string bgSpritePath,
            IReadOnlyList<string> options,
            IReadOnlyList<string> optionRequirements,
            IReadOnlyList<bool> optionEnabled,
            Action<int> onPick,
            Action onEnd,
            float autoContinueDelaySeconds = 0f)
        {
            StopAutoContinue();
            ClearOptions();
            _resolved = false;
            gameObject.SetActive(true);

            if (_titleText != null)
            {
                _titleText.text = title ?? string.Empty;
            }

            if (_descriptionText != null)
            {
                SemanticDescriptionFormatter.Set(_descriptionText, description);
                _descriptionText.gameObject.SetActive(!string.IsNullOrWhiteSpace(description));
            }

            SetIllustration(bgSpritePath);

            int count = options?.Count ?? 0;
            bool autoContinue = autoContinueDelaySeconds > 0f;
            bool showResultButton = !autoContinue
                && (!string.IsNullOrWhiteSpace(resultButtonText) || count == 0);
            if (_optionsRoot != null)
            {
                _optionsRoot.gameObject.SetActive(!autoContinue && (showResultButton || count > 0));
            }

            if (autoContinue)
            {
                _resolved = true;
                _autoContinueTween = DOVirtual.DelayedCall(
                        autoContinueDelaySeconds,
                        () =>
                        {
                            _autoContinueTween = null;
                            onEnd?.Invoke();
                        })
                    .SetUpdate(true);
                return;
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
            StopAutoContinue();
            ClearOptions();
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            StopAutoContinue();
        }

        private void StopAutoContinue()
        {
            _autoContinueTween?.Kill();
            _autoContinueTween = null;
        }

        private void CreateOption(
            int index,
            string label,
            string requirement,
            bool interactable,
            Action<int> onPick)
        {
            EventOptionView option = Spawn(_optionTemplate, $"EventOption_{index + 1}");
            option?.Bind(
                string.IsNullOrWhiteSpace(label) ? "继续" : label,
                requirement,
                interactable,
                () => ResolveOnce(() => onPick?.Invoke(index)));
        }

        private void CreateResultButton(string resultText, Action onEnd, bool styleAsOption)
        {
            EventOptionView template = styleAsOption || _resultTemplate == null
                ? _optionTemplate
                : _resultTemplate;
            EventOptionView result = Spawn(template, "EventResult");
            if (result == null)
            {
                onEnd?.Invoke();
                return;
            }

            result.Bind(
                string.IsNullOrWhiteSpace(resultText) ? "结束" : resultText,
                string.Empty,
                interactable: true,
                () => ResolveOnce(onEnd));
        }

        private EventOptionView Spawn(EventOptionView template, string objectName)
        {
            if (_optionsRoot == null || template == null)
            {
                return null;
            }

            EventOptionView view = Instantiate(template, _optionsRoot);
            view.gameObject.name = objectName;
            view.gameObject.SetActive(true);
            _spawnedOptions.Add(view);
            return view;
        }

        private void ResolveOnce(Action resolve)
        {
            if (_resolved)
            {
                return;
            }

            _resolved = true;
            foreach (EventOptionView option in _spawnedOptions)
            {
                if (option != null)
                {
                    option.SetInteractable(false);
                }
            }

            resolve?.Invoke();
        }

        private void ClearOptions()
        {
            foreach (EventOptionView option in _spawnedOptions)
            {
                if (option == null)
                {
                    continue;
                }

                option.gameObject.SetActive(false);
                if (Application.isPlaying)
                {
                    Destroy(option.gameObject);
                }
                else
                {
                    DestroyImmediate(option.gameObject);
                }
            }

            _spawnedOptions.Clear();
            HideTemplate(_optionTemplate);
            HideTemplate(_resultTemplate);
        }

        private static void HideTemplate(EventOptionView template)
        {
            if (template != null)
            {
                template.gameObject.SetActive(false);
            }
        }

        private void SetIllustration(string spritePath)
        {
            if (_illustrationImage == null)
            {
                return;
            }

            string resourcePath = NormalizeResourcePath(spritePath);
            Sprite sprite = string.IsNullOrWhiteSpace(resourcePath)
                ? null
                : Resources.Load<Sprite>(resourcePath);
            _illustrationImage.sprite = sprite;
            _illustrationImage.gameObject.SetActive(sprite != null);

            if (sprite == null && !string.IsNullOrWhiteSpace(resourcePath))
            {
                Debug.LogWarning($"事件插画加载失败：{resourcePath}");
            }
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
    }
}
