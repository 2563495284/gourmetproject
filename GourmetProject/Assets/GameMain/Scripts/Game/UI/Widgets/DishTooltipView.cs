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

        /// <summary>绑定单风味便捷重载。</summary>
        public void Bind(DishDef def, IReadOnlyList<string> skillIds, string flavorId, GameplayDatabase db)
        {
            Bind(def, skillIds, flavorId == null ? null : new[] { flavorId }, db);
        }

        /// <summary>
        /// 绑定并刷新 Tips 内容。<paramref name="flavorIds"/> 支持多风味
        /// （默认单槽，道具解除上限后可多个）——每个风味 = 顶部一个占位标签 + 下方一张详情卡。
        /// </summary>
        public void Bind(DishDef def, IReadOnlyList<string> skillIds, IReadOnlyList<string> flavorIds, GameplayDatabase db)
        {
            ClearSpawned();

            if (def == null)
            {
                return;
            }

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
            int shown = 0;
            if (skillIds != null && db != null && _skillCardPrefab != null && _skillContent != null)
            {
                foreach (string skillId in skillIds)
                {
                    SkillDef skill = db.GetSkill(skillId);
                    if (skill == null)
                    {
                        continue;
                    }

                    DishInfoCard card = Instantiate(_skillCardPrefab, _skillContent);
                    card.Set(skill.Name, skill.Desc);
                    _spawned.Add(card.gameObject);
                    shown++;
                }
            }

            bool needsScroll = shown > VisibleSkillCount;
            if (_skillScrollbar != null)
            {
                _skillScrollbar.gameObject.SetActive(needsScroll);
            }

            if (_skillScroll != null)
            {
                _skillScroll.verticalNormalizedPosition = 1f;
            }
        }

        private void ClearSpawned()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }

            _spawned.Clear();
        }
    }
}
