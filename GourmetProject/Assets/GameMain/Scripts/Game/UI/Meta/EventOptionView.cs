using System;
using GourmetProject.Game.UI.Common;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 事件页选项 / 结束按钮的模板视图。所有视觉（底色、字号、要求条样式、置灰表现）都由模板 prefab 决定，
    /// 这里只负责填文本、切换要求条与置灰节点的显隐，以及转发点击。
    /// </summary>
    public sealed class EventOptionView : MonoBehaviour
    {
        [SerializeField] private Button _button;
        [SerializeField] private TMP_Text _labelText;

        [Tooltip("要求条整块（含底图与文字）；选项没有条件要求时隐藏。")]
        [SerializeField] private GameObject _requirementRoot;
        [SerializeField] private TMP_Text _requirementText;

        [Tooltip("条件不满足时显示的置灰表现，可留空。")]
        [SerializeField] private GameObject _lockedRoot;

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

            if (_lockedRoot != null)
            {
                _lockedRoot.SetActive(!interactable);
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

        /// <summary>结算已触发后统一关闭点击，不改变置灰表现。</summary>
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
