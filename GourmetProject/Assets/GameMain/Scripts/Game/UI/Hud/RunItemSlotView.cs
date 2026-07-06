using System;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 常驻 HUD 里的道具槽视图（屏幕空间 UGUI 版）：图标 + 名称 + 角标（等级 Lv/份数 xN）+ 品质描边色 + 点击回调。
    /// 固定结构在 RunItemSlotView.prefab，运行时由 BattleForm 的常驻 HUD 数据驱动实例化并 <see cref="Bind"/>。
    /// </summary>
    public sealed class RunItemSlotView : MonoBehaviour
    {
        [SerializeField] private Image _background;
        [SerializeField] private Image _icon;
        [SerializeField] private Button _button;

        /// <summary>绑定一个有内容的道具槽。</summary>
        public void Bind(Sprite icon, string name, string badge, Color qualityColor, bool interactable, Action onClick)
        {
            if (_background != null)
            {
                _background.color = qualityColor;
            }

            if (_icon != null)
            {
                _icon.enabled = icon != null;
                _icon.sprite = icon;
            }

            if (_button != null)
            {
                _button.onClick.RemoveAllListeners();
                _button.interactable = interactable;
                if (onClick != null)
                {
                    _button.onClick.AddListener(() => onClick());
                }
            }
        }

        /// <summary>把该槽显示为空槽（无图标、不可点）。</summary>
        public void SetEmpty()
        {
            Bind(null, string.Empty, string.Empty, EmptySlotColor, false, null);
        }

        private static Color EmptySlotColor => new Color(0.92f, 0.90f, 0.84f, 1f);

        /// <summary>道具品质对应的槽底色（与战斗世界空间槽保持一致）。</summary>
        public static Color QualityColor(cfg.ItemQuality quality)
        {
            switch (quality)
            {
                case cfg.ItemQuality.Uncommon:
                    return new Color(0.50f, 0.86f, 0.46f, 1f);
                case cfg.ItemQuality.Rare:
                    return new Color(0.35f, 0.62f, 1f, 1f);
                case cfg.ItemQuality.Epic:
                    return new Color(0.74f, 0.42f, 1f, 1f);
                case cfg.ItemQuality.Legendary:
                    return new Color(1f, 0.72f, 0.22f, 1f);
                default:
                    return new Color(0.92f, 0.86f, 0.74f, 1f);
            }
        }

        /// <summary>取道具图标（Resources 路径，缺失返回 null）。</summary>
        public static Sprite LoadIcon(cfg.Item item)
        {
            if (item == null || string.IsNullOrEmpty(item.Icon))
            {
                return null;
            }

            return Resources.Load<Sprite>(item.Icon);
        }

        /// <summary>取道具名前两字作为槽内短名（图标缺失时的兜底展示）。</summary>
        public static string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            return name.Length <= 2 ? name : name.Substring(0, 2);
        }
    }
}
