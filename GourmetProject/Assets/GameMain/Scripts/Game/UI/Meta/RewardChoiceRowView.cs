using System;
using System.Collections.Generic;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 战斗胜利领奖页中的单条奖励卡片。奖励规则由 RewardForm/RewardGranter 处理，本视图只负责展示和点击。
    /// </summary>
    public sealed class RewardChoiceRowView : MonoBehaviour
    {
        [SerializeField] private Image _background;
        [SerializeField] private Image _icon;
        [SerializeField] private DishIconRenderTexturePreview _dishPreview;
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _descriptionText;
        [SerializeField] private Text _stateText;
        [SerializeField] private Button _button;

        private static readonly Color NormalColor = new(0.97f, 0.94f, 0.86f, 1f);
        private static readonly Color SelectedColor = new(1f, 0.82f, 0.42f, 1f);
        private static readonly Color GrantedColor = new(0.88f, 0.96f, 0.82f, 1f);

        public void Bind(
            string title,
            string description,
            Sprite icon,
            bool selected,
            bool interactable,
            bool granted,
            Action onClick,
            string stateOverride = null,
            DishDef dish = null,
            IReadOnlyList<string> flavorIds = null)
        {
            EnsureRefs();

            if (_background != null)
            {
                _background.color = granted ? GrantedColor : selected ? SelectedColor : NormalColor;
            }

            bool useDishPreview = dish != null && _dishPreview != null;
            if (_dishPreview != null)
            {
                if (dish != null)
                {
                    _dishPreview.gameObject.SetActive(true);
                    _dishPreview.Bind(
                        DishPreviewRequest.FromDefinition(
                            dish,
                            icon,
                            flavorIds,
                            DishIconPreviewMode.Warehouse));
                    _dishPreview.SetRaycastTarget(false);
                }
                else
                {
                    _dishPreview.Hide();
                    _dishPreview.gameObject.SetActive(false);
                }
            }

            if (_icon != null)
            {
                _icon.enabled = !useDishPreview && icon != null;
                _icon.sprite = icon;
                _icon.preserveAspect = true;
                _icon.rectTransform.localRotation = Quaternion.identity;
                _icon.material = null;
            }

            if (_titleText != null)
            {
                _titleText.text = title ?? string.Empty;
            }

            if (_descriptionText != null)
            {
                _descriptionText.text = description ?? string.Empty;
            }

            if (_stateText != null)
            {
                string state = string.IsNullOrWhiteSpace(stateOverride)
                    ? (granted ? "已领取" : string.Empty)
                    : stateOverride;
                _stateText.text = state;
                _stateText.gameObject.SetActive(!string.IsNullOrEmpty(state));
            }

            if (_button != null)
            {
                _button.interactable = interactable;
                _button.onClick.RemoveAllListeners();
                if (interactable && onClick != null)
                {
                    _button.onClick.AddListener(() => onClick());
                }
            }
        }

        private void EnsureRefs()
        {
            if (_background == null)
            {
                _background = GetComponent<Image>();
            }

            if (_button == null)
            {
                _button = GetComponent<Button>();
            }

            if (_icon == null)
            {
                Transform icon = transform.Find("IconFrame/Icon") ?? transform.Find("Icon");
                _icon = icon != null ? icon.GetComponent<Image>() : null;
            }

            if (_dishPreview == null)
            {
                Transform preview = transform.Find("IconFrame/DishRenderTexture/Output")
                    ?? transform.Find("DishRenderTexture/Output");
                _dishPreview = preview != null
                    ? preview.GetComponent<DishIconRenderTexturePreview>()
                    : null;
            }

            if (_titleText == null)
            {
                Transform title = transform.Find("Texts/Title") ?? transform.Find("Title");
                _titleText = title != null ? title.GetComponent<Text>() : null;
            }

            if (_descriptionText == null)
            {
                Transform desc = transform.Find("Texts/Description") ?? transform.Find("Description");
                _descriptionText = desc != null ? desc.GetComponent<Text>() : null;
            }

            if (_stateText == null)
            {
                Transform state = transform.Find("State") ?? transform.Find("StateText");
                _stateText = state != null ? state.GetComponent<Text>() : null;
            }
        }
    }
}
