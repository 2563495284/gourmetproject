using System;
using System.Collections.Generic;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 经营挑战胜利领奖页中的单条奖励卡片。奖励规则由 RewardForm/RewardGranter 处理，本视图只负责展示和点击。
    /// </summary>
    public sealed class RewardChoiceRowView : MonoBehaviour
    {
        [SerializeField] private Image _background;
        [SerializeField] private Image _icon;
        [SerializeField] private DishIconRenderTexturePreview _dishPreview;
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _descriptionText;
        [SerializeField] private TMP_Text _stateText;
        [SerializeField] private Button _button;

        private static readonly Color NormalColor = new(0.97f, 0.94f, 0.86f, 1f);
        private static readonly Color SelectedColor = new(1f, 0.82f, 0.42f, 1f);
        private static readonly Color GrantedColor = new(0.88f, 0.96f, 0.82f, 1f);
        private Sprite _selectionFlySprite;

        public RectTransform TipPlacementTarget
        {
            get
            {
                EnsureRefs();
                return _dishPreview != null && _dishPreview.gameObject.activeSelf
                    ? _dishPreview.transform as RectTransform
                    : _icon != null ? _icon.rectTransform : transform as RectTransform;
            }
        }

        public RectTransform SelectionFlySource => TipPlacementTarget;

        public Sprite SelectionFlySprite
        {
            get
            {
                EnsureRefs();
                return _selectionFlySprite != null
                    ? _selectionFlySprite
                    : _icon != null && _icon.enabled ? _icon.sprite : null;
            }
        }

        public RenderTexture CaptureSelectionFlyTexture()
        {
            EnsureRefs();
            return _dishPreview != null
                ? _dishPreview.CopyCurrentTexture()
                : null;
        }

        public TipHoverTrigger EnsureTipTrigger()
        {
            TipHoverTrigger trigger = GetComponent<TipHoverTrigger>();
            if (trigger == null)
            {
                trigger = gameObject.AddComponent<TipHoverTrigger>();
            }

            trigger.enabled = true;
            return trigger;
        }

        public void DisableTipTrigger()
        {
            TipHoverTrigger trigger = GetComponent<TipHoverTrigger>();
            if (trigger == null)
            {
                return;
            }

            trigger.ClearTip();
            trigger.enabled = false;
        }

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
            IReadOnlyList<string> flavorIds = null,
            bool showDishPreview = true,
            Sprite selectionFlySprite = null)
        {
            EnsureRefs();
            _selectionFlySprite = selectionFlySprite;

            if (_background != null)
            {
                _background.color = granted ? GrantedColor : selected ? SelectedColor : NormalColor;
            }

            bool hasDishPreview = dish != null && _dishPreview != null;
            bool useDishPreview = hasDishPreview && showDishPreview;
            if (_dishPreview != null)
            {
                if (dish != null)
                {
                    _dishPreview.gameObject.SetActive(true);
                    _dishPreview.Bind(
                        DishPreviewRequest.FromDefinition(
                            dish,
                            selectionFlySprite ?? icon,
                            flavorIds,
                            DishIconPreviewMode.Warehouse));
                    _dishPreview.SetRaycastTarget(false);
                    _dishPreview.gameObject.SetActive(showDishPreview);
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
                _titleText = title != null ? title.GetComponent<TMP_Text>() : null;
            }

            if (_descriptionText == null)
            {
                Transform desc = transform.Find("Texts/Description") ?? transform.Find("Description");
                _descriptionText = desc != null ? desc.GetComponent<TMP_Text>() : null;
            }

            if (_stateText == null)
            {
                Transform state = transform.Find("State") ?? transform.Find("StateText");
                _stateText = state != null ? state.GetComponent<TMP_Text>() : null;
            }
        }
    }
}
