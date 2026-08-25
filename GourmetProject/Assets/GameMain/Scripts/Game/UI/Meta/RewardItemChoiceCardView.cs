using System;
using System.Collections.Generic;
using DG.Tweening;
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
        private const string QualityBackingResourcesRoot =
            "Sprites/UI/FengKuangCanTing/QualityBackings/quality_backing_";
        private static readonly Dictionary<cfg.ItemQuality, Sprite> QualityBackingCache = new();

        [SerializeField] private Image _background;
        [SerializeField] private Image _icon;
        [SerializeField] private Button _button;
        private CanvasGroup _canvasGroup;
        private Tween _failureTween;

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

            RefreshBackground(item);

            if (_icon != null)
            {
                Sprite sprite = RunItemSlotView.LoadIcon(item);
                _icon.sprite = sprite;
                _icon.preserveAspect = true;
                _icon.color = sprite != null ? Color.white : new Color(0.75f, 0.68f, 0.56f, 1f);
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

        /// <summary>播放与商店购买失败一致的横向衰减晃动。</summary>
        public void PlayTargetFailed()
        {
            RectTransform target = _icon != null
                ? _icon.rectTransform
                : transform as RectTransform;
            if (target == null)
            {
                return;
            }

            _failureTween?.Kill();
            Vector2 origin = target.anchoredPosition;
            _failureTween = DOVirtual.Float(0f, 1f, 0.25f, t =>
                {
                    if (target == null)
                    {
                        return;
                    }

                    float offset = Mathf.Sin(t * Mathf.PI * 12f) * 9f * (1f - t);
                    target.anchoredPosition = origin + new Vector2(offset, 0f);
                })
                .SetEase(Ease.Linear)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    if (target != null)
                    {
                        target.anchoredPosition = origin;
                    }
                });
        }

        private void OnDestroy()
        {
            _failureTween?.Kill();
        }

        private void RefreshBackground(ItemDefinition item)
        {
            if (_background == null)
            {
                return;
            }

            bool showQualityBacking = item != null && item.IsPassive;
            // Keep the button's target graphic enabled even when active items have no
            // quality backing; a disabled Graphic is removed from UI raycasting.
            _background.enabled = true;
            _background.type = Image.Type.Sliced;
            _background.preserveAspect = false;
            if (!showQualityBacking)
            {
                _background.sprite = null;
                _background.color = Color.clear;
                return;
            }

            Sprite backing = LoadQualityBacking(item.Quality);
            _background.sprite = backing;
            _background.color = backing != null
                ? Color.white
                : RunItemSlotView.QualityColor(item.Quality);
        }

        private static Sprite LoadQualityBacking(cfg.ItemQuality quality)
        {
            if (QualityBackingCache.TryGetValue(quality, out Sprite backing))
            {
                return backing;
            }

            string assetName;
            switch (quality)
            {
                case cfg.ItemQuality.Uncommon:
                    assetName = "uncommon";
                    break;
                case cfg.ItemQuality.Rare:
                    assetName = "rare";
                    break;
                case cfg.ItemQuality.Epic:
                    assetName = "epic";
                    break;
                case cfg.ItemQuality.Legendary:
                    assetName = "legendary";
                    break;
                case cfg.ItemQuality.Negative:
                    assetName = "negative";
                    break;
                default:
                    assetName = "common";
                    break;
            }

            backing = Resources.Load<Sprite>(QualityBackingResourcesRoot + assetName);
            QualityBackingCache[quality] = backing;
            return backing;
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
