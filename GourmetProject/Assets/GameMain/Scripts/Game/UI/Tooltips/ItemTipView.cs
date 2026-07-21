using System;
using System.Collections.Generic;
using GourmetProject.Game;
using GourmetProject.Game.Meta;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 被动 / 主动道具 hover Tips（原型图第 4 张）：
    /// 标题「道具名」+ 效果描述框；无底部信息行。
    /// 固定结构在 ItemTipView.prefab，内容由 <see cref="Bind"/> 数据驱动。
    /// </summary>
    public sealed class ItemTipView : ActionTipView, ITooltipPlacementAware
    {
        private const float SpecialTagsGap = 18f;
        private const float MinWidth = 260f;
        private const float MaxWidth = 480f;
        private const float DescHorizontalPadding = 76f;

        [SerializeField] private RectTransform _specialTagsRoot;
        [SerializeField] private FoodTipCardView _infoCardPrefab;

        /// <summary>用配置道具绑定：标题取道具名，描述取效果说明，图标按名称约定加载。</summary>
        public void Bind(ItemDefinition item)
        {
            if (item == null)
            {
                Bind(string.Empty, string.Empty, null);
                return;
            }

            Sprite icon = ContentIconLoader.LoadItem(item);
            Bind(item.Name, item.Desc, icon, BuildSpecialTags(item.TermIds));
        }

        /// <summary>字段级绑定。</summary>
        public void Bind(
            string itemName,
            string desc,
            Sprite icon = null,
            IReadOnlyList<FoodInfoEntry> specialTags = null)
        {
            ApplyTexts(itemName, desc);
            ApplyFooter(null);
            BuildInfoCards(_specialTagsRoot, specialTags, "SpecialTag");
            ResizeToDescText();
        }

        public void OnPlacedAroundTarget(bool placedLeftOfTarget)
        {
            PlaceSpecialTags(placedLeftOfTarget);
        }

        private static IReadOnlyList<FoodInfoEntry> BuildSpecialTags(IReadOnlyList<string> termIds)
        {
            if (termIds == null || termIds.Count == 0)
            {
                return Array.Empty<FoodInfoEntry>();
            }

            var tags = new List<FoodInfoEntry>(termIds.Count);
            foreach (string termId in termIds)
            {
                if (string.IsNullOrEmpty(termId))
                {
                    continue;
                }

                cfg.Term term = GameApp.Config?.Tables?.TbTerm?.GetOrDefault(termId);
                tags.Add(term != null
                    ? new FoodInfoEntry(term.Name, term.Desc)
                    : new FoodInfoEntry(termId, string.Empty));
            }

            return tags.Count > 0 ? tags : Array.Empty<FoodInfoEntry>();
        }

        private void BuildInfoCards(RectTransform root, IReadOnlyList<FoodInfoEntry> entries, string prefix)
        {
            if (root == null)
            {
                return;
            }

            FoodTipUiUtility.ClearChildren(root);
            int count = entries != null ? entries.Count : 0;
            bool canBuild = count > 0 && _infoCardPrefab != null;
            root.gameObject.SetActive(canBuild);
            if (!canBuild)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                FoodInfoEntry entry = entries[i];
                FoodTipCardView card = Instantiate(_infoCardPrefab, root, false);
                card.name = $"{prefix}_{i}";
                card.Bind(entry.Title, entry.Desc);
            }
        }

        private void PlaceSpecialTags(bool left)
        {
            if (_specialTagsRoot == null)
            {
                return;
            }

            _specialTagsRoot.anchorMin = new Vector2(left ? 0f : 1f, 1f);
            _specialTagsRoot.anchorMax = new Vector2(left ? 0f : 1f, 1f);
            _specialTagsRoot.pivot = new Vector2(left ? 1f : 0f, 1f);
            _specialTagsRoot.anchoredPosition = new Vector2(left ? -SpecialTagsGap : SpecialTagsGap, 0f);
        }

        private void ResizeToDescText()
        {
            if (DescText == null || transform is not RectTransform rect)
            {
                return;
            }

            float preferredWidth = Mathf.Max(
                DescText.preferredWidth,
                TitleText != null ? TitleText.preferredWidth : 0f);
            if (preferredWidth <= 0f)
            {
                return;
            }

            float width = Mathf.Clamp(preferredWidth + DescHorizontalPadding, MinWidth, MaxWidth);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        }
    }
}
