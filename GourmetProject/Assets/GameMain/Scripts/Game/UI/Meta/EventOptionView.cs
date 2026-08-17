using System;
using GourmetProject.Game.UI.Common;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 事件页选项 / 结束按钮的模板视图。所有视觉（底色、字号、要求条样式、禁用态底板色）都由模板 prefab 决定，
    /// 禁用态只走 Button 自身的 ColorBlock / SpriteState，这里只负责填文本、切换要求条显隐以及转发点击。
    /// </summary>
    public sealed class EventOptionView : MonoBehaviour
    {
        [SerializeField] private Button _button;
        [SerializeField] private TMP_Text _labelText;

        [Tooltip("要求条整块（含底图与文字）；选项没有条件要求时隐藏。")]
        [SerializeField] private GameObject _requirementRoot;
        [SerializeField] private TMP_Text _requirementText;

        public void Bind(string label, string requirement, bool interactable, Action onClick)
        {
            EnsureRefs();

            if (_labelText != null)
            {
                SemanticDescriptionFormatter.Set(_labelText, label);
            }

            bool hasRequirement = !string.IsNullOrWhiteSpace(requirement);
            if (_requirementRoot != null)
            {
                _requirementRoot.SetActive(hasRequirement);
            }

            if (_requirementText != null)
            {
                SemanticDescriptionFormatter.Set(_requirementText, hasRequirement ? requirement : string.Empty);
            }

            if (_button == null)
            {
                return;
            }

            _button.onClick.RemoveAllListeners();
            _button.interactable = interactable;
            if (interactable && onClick != null)
            {
                _button.onClick.AddListener(() => onClick());
            }
        }

        /// <summary>结算已触发后统一关闭点击。</summary>
        public void SetInteractable(bool interactable)
        {
            EnsureRefs();
            if (_button != null)
            {
                _button.interactable = interactable;
            }
        }

        private void EnsureRefs()
        {
            if (_button == null)
            {
                _button = GetComponent<Button>();
            }

            if (_labelText == null)
            {
                _labelText = GetComponentInChildren<TMP_Text>(true);
            }
        }
    }
}
