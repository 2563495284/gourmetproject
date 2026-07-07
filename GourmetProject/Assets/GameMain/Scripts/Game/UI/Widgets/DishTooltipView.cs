using System.Collections.Generic;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Widgets
{
    /// <summary>
    /// 菜品 hover Tips（对应原型图 菜品Tips.png）：
    /// 左列顶部框（名字 / 美味值 / 风味占位标签），其下逐张风味详情卡；
    /// 右列技能卡滚动列表（技能超过 <see cref="VisibleSkillCount"/> 个时显示滚动条）。
    ///
    /// 这是一个可挂在任意 Canvas 下的 MonoBehaviour View（非 UGuiForm），
    /// 通过公开的 <see cref="Bind"/> / <see cref="Show"/> / <see cref="Hide"/> 驱动，
    /// 作为"鼠标悬浮菜品显示 tips"的预留口子——真实 hover 触发后续接入。
    ///
    /// 固定结构在 DishTooltipView.prefab，卡片/标签按数据实例化。
    /// </summary>
    public sealed class DishTooltipView : MonoBehaviour
    {
        /// <summary>技能列表不出现滚动条的最大条数；超过则显示竖向滚动条。</summary>
        public const int VisibleSkillCount = 3;

        [Header("Root")]
        [SerializeField] private CanvasGroup _canvasGroup;

        [Header("左上：名称 / 美味值 / 风味占位标签")]
        [SerializeField] private Text _nameText;
        [SerializeField] private Image _deliciousIcon;
        [SerializeField] private Text _deliciousText;
        [SerializeField] private RectTransform _flavorTagContainer;
        [SerializeField] private DishFlavorTag _flavorTagPrefab;

        [Header("左下：风味详情卡（每个风味一张，竖向堆叠）")]
        [SerializeField] private RectTransform _flavorDetailContainer;
        [SerializeField] private DishInfoCard _flavorCardPrefab;

        [Header("右列：技能卡滚动列表")]
        [SerializeField] private ScrollRect _skillScroll;
        [SerializeField] private RectTransform _skillContent;
        [SerializeField] private Scrollbar _skillScrollbar;
        [SerializeField] private DishInfoCard _skillCardPrefab;

        private readonly List<GameObject> _spawned = new();

        private IReadOnlyDictionary<string, string> _skillSources;

        /// <summary>绑定单风味便捷重载。</summary>
        public void Bind(DishDef def, IReadOnlyList<string> skillIds, string flavorId, GameplayDatabase db, IReadOnlyDictionary<string, string> skillSources = null)
        {
            Bind(def, skillIds, flavorId == null ? null : new[] { flavorId }, db, skillSources);
        }

        /// <summary>
        /// 绑定并刷新 Tips 内容。<paramref name="flavorIds"/> 支持多风味
        /// （默认单槽，道具解除上限后可多个）——每个风味 = 顶部一个占位标签 + 下方一张详情卡。
        /// <paramref name="skillSources"/> 为技能来源标签（skillId → 「源名&lt;甜蜜传递&gt;」），有则替代技能名做卡标题。
        /// </summary>
        public void Bind(DishDef def, IReadOnlyList<string> skillIds, IReadOnlyList<string> flavorIds, GameplayDatabase db, IReadOnlyDictionary<string, string> skillSources = null)
        {
            ClearSpawned();
            ResetDynamicContainers();

            if (def == null)
            {
                return;
            }

            _skillSources = skillSources;

            if (_nameText != null)
            {
                _nameText.text = def.Name;
            }

            if (_deliciousText != null)
            {
                _deliciousText.text = $"：{def.Deliciousness}";
            }

            BuildFlavors(flavorIds, db);
            BuildSkills(skillIds, db);
        }

        public void Show()
        {
            gameObject.SetActive(true);
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }
        }

        public void Hide()
        {
            ClearSpawned();
            ResetDynamicContainers();

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }

            gameObject.SetActive(false);
        }

        private void BuildFlavors(IReadOnlyList<string> flavorIds, GameplayDatabase db)
        {
            if (flavorIds == null || db == null)
            {
                return;
            }

            foreach (string flavorId in flavorIds)
            {
                FlavorDef flavor = db.GetFlavor(flavorId);
                if (flavor == null)
                {
                    continue;
                }

                if (_flavorTagPrefab != null && _flavorTagContainer != null)
                {
                    DishFlavorTag tag = Instantiate(_flavorTagPrefab, _flavorTagContainer);
                    tag.Set(flavor.Name);
                    _spawned.Add(tag.gameObject);
                }

                if (_flavorCardPrefab != null && _flavorDetailContainer != null)
                {
                    DishInfoCard card = Instantiate(_flavorCardPrefab, _flavorDetailContainer);
                    card.Set(flavor.Name, flavor.Desc);
                    _spawned.Add(card.gameObject);
                }
            }
        }

        private void BuildSkills(IReadOnlyList<string> skillIds, GameplayDatabase db)
        {
            EnsureSkillScrollLayout();

            var skillCards = new List<RectTransform>();
            if (skillIds != null && db != null && _skillCardPrefab != null && _skillContent != null)
            {
                foreach (string skillId in skillIds)
                {
                    SkillDef skill = db.GetSkill(skillId);
                    if (skill == null)
                    {
                        continue;
                    }

                    string title = skill.Name;
                    if (_skillSources != null && _skillSources.TryGetValue(skillId, out string sourceLabel) && !string.IsNullOrEmpty(sourceLabel))
                    {
                        title = sourceLabel;
                    }

                    DishInfoCard card = Instantiate(_skillCardPrefab, _skillContent);
                    card.Set(title, skill.Desc);
                    _spawned.Add(card.gameObject);

                    if (card.transform is RectTransform cardRect)
                    {
                        skillCards.Add(cardRect);
                    }
                }
            }

            int shown = skillCards.Count;
            if (_skillScroll != null)
            {
                _skillScroll.gameObject.SetActive(shown > 0);
            }

            bool needsScroll = shown > VisibleSkillCount;
            if (_skillScrollbar != null)
            {
                _skillScrollbar.gameObject.SetActive(shown > 0 && needsScroll);
            }

            if (shown <= 0)
            {
                return;
            }

            ResizeSkillScroll(skillCards, Mathf.Min(shown, VisibleSkillCount));

            if (_skillScroll != null)
            {
                _skillScroll.vertical = needsScroll;
                _skillScroll.verticalNormalizedPosition = 1f;
            }
        }

        private void ResizeSkillScroll(IReadOnlyList<RectTransform> skillCards, int visibleCount)
        {
            if (_skillScroll == null || skillCards == null || visibleCount <= 0)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            if (_skillContent != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_skillContent);
            }

            float height = 0f;
            float contentHeight = _skillContent != null
                ? Mathf.Max(_skillContent.rect.height, LayoutUtility.GetPreferredHeight(_skillContent))
                : 0f;
            VerticalLayoutGroup layout = _skillContent != null ? _skillContent.GetComponent<VerticalLayoutGroup>() : null;
            float padding = 0f;
            float spacing = 0f;
            if (layout != null)
            {
                padding = layout.padding.top + layout.padding.bottom;
                spacing = layout.spacing;
            }

            float estimatedCardHeight = 0f;
            if (contentHeight > padding && skillCards.Count > 0)
            {
                estimatedCardHeight = (contentHeight - padding - Mathf.Max(0, skillCards.Count - 1) * spacing) / skillCards.Count;
            }

            for (int i = 0; i < visibleCount && i < skillCards.Count; i++)
            {
                RectTransform card = skillCards[i];
                if (card == null)
                {
                    continue;
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(card);
                float cardHeight = Mathf.Max(card.rect.height, LayoutUtility.GetPreferredHeight(card), estimatedCardHeight);
                height += cardHeight;
            }

            height += padding;
            height += Mathf.Max(0, visibleCount - 1) * spacing;

            if (height <= padding && contentHeight > 0f)
            {
                height = contentHeight;
            }

            RectTransform scrollRect = _skillScroll.transform as RectTransform;
            if (scrollRect != null)
            {
                scrollRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            }
        }

        private void ClearSpawned()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null)
                {
                    go.SetActive(false);
                    Destroy(go);
                }
            }

            _spawned.Clear();
        }

        private void ResetDynamicContainers()
        {
            if (_skillScroll != null)
            {
                _skillScroll.gameObject.SetActive(false);
                _skillScroll.verticalNormalizedPosition = 1f;
            }

            if (_skillScrollbar != null)
            {
                _skillScrollbar.gameObject.SetActive(false);
            }
        }

        private void EnsureSkillScrollLayout()
        {
            if (_skillScroll == null)
            {
                return;
            }

            if (_skillScroll.transform is RectTransform scrollRect)
            {
                float top = scrollRect.anchorMax.y;
                scrollRect.anchorMin = new Vector2(scrollRect.anchorMin.x, top);
                scrollRect.anchorMax = new Vector2(scrollRect.anchorMax.x, top);
                scrollRect.anchoredPosition = new Vector2(scrollRect.anchoredPosition.x, 0f);
                scrollRect.pivot = new Vector2(0.5f, 1f);
            }

            RectTransform viewport = _skillScroll.viewport;
            if (viewport != null)
            {
                viewport.anchorMin = Vector2.zero;
                viewport.anchorMax = Vector2.one;
                viewport.offsetMin = Vector2.zero;
                viewport.offsetMax = Vector2.zero;
                viewport.pivot = new Vector2(0f, 1f);
            }

            RectTransform content = _skillContent != null ? _skillContent : _skillScroll.content;
            if (content != null)
            {
                content.anchorMin = new Vector2(0f, 1f);
                content.anchorMax = new Vector2(1f, 1f);
                content.anchoredPosition = Vector2.zero;
                content.pivot = new Vector2(0.5f, 1f);
                content.offsetMin = new Vector2(0f, content.offsetMin.y);
                content.offsetMax = new Vector2(0f, content.offsetMax.y);
            }
        }
    }
}
