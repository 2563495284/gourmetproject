using System;
using System.Collections.Generic;
using GourmetProject.Core.Utility;
using GourmetProject.Game.Presentation.Battle;
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
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _descriptionText;
        [SerializeField] private Text _stateText;
        [SerializeField] private Button _button;

        private static readonly Color NormalColor = new Color(0.97f, 0.94f, 0.86f, 1f);
        private static readonly Color SelectedColor = new Color(1f, 0.82f, 0.42f, 1f);
        private static readonly Color GrantedColor = new Color(0.88f, 0.96f, 0.82f, 1f);
        private readonly List<string> _flavorScratch = new();
        private Material _flavorMaterial;

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

            if (_icon != null)
            {
                _icon.enabled = icon != null;
                _icon.sprite = icon;
                _icon.preserveAspect = true;
                ApplyDishIconVisual(dish, flavorIds);
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
                _stateText.text = string.Empty;
                _stateText.gameObject.SetActive(false);
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

        private void OnDestroy()
        {
            FlavorStainPalette.ReleaseMaterial(ref _flavorMaterial);
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

        private void ApplyDishIconVisual(DishDef dish, IReadOnlyList<string> flavorIds)
        {
            if (_icon == null)
            {
                return;
            }

            RectTransform rect = _icon.rectTransform;
            if (dish == null || _icon.sprite == null)
            {
                rect.localRotation = Quaternion.identity;
                _icon.material = null;
                return;
            }

            ComposeFlavorScratch(dish, flavorIds);
            int rotationIndex = FlavorStainPalette.DisplayRotationIndex(dish.RotationIndex, _flavorScratch);
            rect.localRotation = Quaternion.Euler(0f, 0f, -90f * rotationIndex);

            var settings = new FlavorStainPalette.Settings(
                8f,
                0.62f,
                0.12f,
                0.12f,
                (float)(StableHash.Fnv1a64(dish.Id) & 0xFFFFFF));
            FlavorStainPalette.ApplyToGraphic(_icon, _flavorScratch, ref _flavorMaterial, settings);
        }

        private void ComposeFlavorScratch(DishDef dish, IReadOnlyList<string> flavorIds)
        {
            _flavorScratch.Clear();
            if (!string.IsNullOrEmpty(dish.FlavorId))
            {
                _flavorScratch.Add(dish.FlavorId);
            }

            if (flavorIds != null)
            {
                _flavorScratch.AddRange(flavorIds);
            }
        }
    }
}
