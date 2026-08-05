using System;
using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Meta
{
    public sealed class RewardItemChoiceCardView : MonoBehaviour
    {
        [SerializeField] private Image _background;
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _descriptionText;
        [SerializeField] private Button _button;
        private CanvasGroup _canvasGroup;

        public RectTransform SelectionFlySource =>
            _icon != null ? _icon.rectTransform : transform as RectTransform;

        public Sprite SelectionFlySprite =>
            _icon != null && _icon.enabled ? _icon.sprite : null;

        public void Bind(
            RewardChoice choice,
            cfg.ItemKind kind,
            Action onClick,
            ItemTipView itemTip = null,
            bool selectable = true,
            string disabledReason = null)
        {
            ItemDefinition item = ItemDefinition.Get(GameApp.Config.Tables, choice?.Id, kind);

            if (_background != null)
            {
                _background.color = item != null
                    ? RunItemSlotView.QualityColor(item.Quality)
                    : new Color(0.88f, 0.82f, 0.70f, 1f);
            }

            if (_icon != null)
            {
                Sprite sprite = RunItemSlotView.LoadIcon(item);
                _icon.sprite = sprite;
                _icon.preserveAspect = true;
                _icon.color = sprite != null ? Color.white : new Color(0.75f, 0.68f, 0.56f, 1f);
            }

            if (_nameText != null)
            {
                _nameText.text = choice?.Name ?? string.Empty;
            }

            if (_descriptionText != null)
            {
                string description = item != null ? item.Desc : choice?.Description ?? string.Empty;
                _descriptionText.text = string.IsNullOrWhiteSpace(disabledReason)
                    ? description
                    : $"{description}\n\n{disabledReason}";
            }

            if (_button != null)
            {
                _button.onClick.RemoveAllListeners();
                _button.interactable = selectable;
                if (selectable && onClick != null)
                {
                    _button.onClick.AddListener(() => onClick());
                }
            }

            BindTip(itemTip, item);
            SetResolved(false);
        }

        public void SetResolved(bool resolved)
        {
            if (_button != null)
            {
                _button.interactable = !resolved;
            }

            if (_canvasGroup == null)
            {
                _canvasGroup = GetComponent<CanvasGroup>();
                if (_canvasGroup == null)
                {
                    _canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }

            _canvasGroup.alpha = resolved ? 0.45f : 1f;
            _canvasGroup.interactable = !resolved;
            _canvasGroup.blocksRaycasts = !resolved;
        }

        private void BindTip(ItemTipView itemTip, ItemDefinition item)
        {
            TipHoverTrigger trigger = GetComponent<TipHoverTrigger>();
            if (itemTip == null || item == null)
            {
                if (trigger != null)
                {
                    trigger.ClearTip();
                }

                return;
            }

            if (trigger == null)
            {
                trigger = gameObject.AddComponent<TipHoverTrigger>();
            }

            RectTransform target = _icon != null ? _icon.rectTransform : transform as RectTransform;
            trigger.SetTarget(target);
            trigger.SetTip(itemTip, () => itemTip.Bind(item));
        }
    }
}
