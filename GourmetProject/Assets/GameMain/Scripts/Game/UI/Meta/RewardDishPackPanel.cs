using System;
using System.Collections.Generic;
using GourmetProject.Game;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 菜品奖励选择页。候选菜品居中展示，点击后通过二次确认完成领取。
    /// </summary>
    public sealed class RewardDishPackPanel : MonoBehaviour
    {
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private Text _promptText;
        [SerializeField] private RectTransform _choiceContainer;
        [SerializeField] private RewardDishChoiceCardView _cardTemplate;
        [SerializeField] private Button _skipButton;

        private readonly List<RewardDishChoiceCardView> _spawnedCards = new();
        private readonly List<RewardChoice> _choices = new();
        private GameRun _run;
        private Func<int, bool> _onChoiceSelected;
        private Action _onSkip;
        private Func<FoodTipsView> _getFoodTips;
        private RewardDishChoiceCardView _hoveredCard;
        private bool _resolved;
        private bool _confirmPending;
        private bool _wired;

        private void Awake()
        {
            EnsureWired();
        }

        private void OnDisable()
        {
            // 页面切换（菜谱/餐桌）只是挂起；候选、回调和已选状态只能由显式 Close 销毁。
            HideDishTips();
        }

        public void Open(
            GameRun run,
            IReadOnlyList<RewardChoice> choices,
            Func<int, bool> onChoiceSelected,
            Action onSkip,
            Func<FoodTipsView> getFoodTips = null)
        {
            EnsureWired();
            HideDishTips();
            ClearCards();

            _run = run;
            _onChoiceSelected = onChoiceSelected;
            _onSkip = onSkip;
            _getFoodTips = getFoodTips;
            _resolved = false;
            _confirmPending = false;
            _choices.Clear();

            if (choices != null)
            {
                for (int i = 0; i < choices.Count; i++)
                {
                    RewardChoice choice = choices[i];
                    if (choice != null && choice.Kind == cfg.RewardKind.DishChoice)
                    {
                        _choices.Add(choice);
                    }
                }
            }

            if (_panelRoot != null)
            {
                _panelRoot.SetActive(true);
            }

            if (_promptText != null)
            {
                _promptText.text = "选择一个菜品";
            }

            BuildCards();
        }

        public void Close()
        {
            HideDishTips();
            ClearCards();
            _choices.Clear();
            _run = null;
            _onChoiceSelected = null;
            _onSkip = null;
            _getFoodTips = null;
            _resolved = false;
            _confirmPending = false;

            if (_panelRoot != null)
            {
                _panelRoot.SetActive(false);
            }

            gameObject.SetActive(false);
        }

        private void EnsureWired()
        {
            if (_wired)
            {
                return;
            }

            _wired = true;
            if (_skipButton != null)
            {
                _skipButton.onClick.RemoveAllListeners();
                _skipButton.onClick.AddListener(OnSkipClicked);
            }
        }

        private void BuildCards()
        {
            if (_cardTemplate == null || _choiceContainer == null)
            {
                Debug.LogError($"{nameof(RewardDishPackPanel)} 缺少 ChoiceContainer 或 RewardDishChoiceCardTemplate。", this);
                return;
            }

            _cardTemplate.gameObject.SetActive(false);
            HorizontalLayoutGroup layout = _choiceContainer.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
            {
                layout.enabled = true;
                layout.childAlignment = TextAnchor.MiddleCenter;
            }

            for (int i = 0; i < _choices.Count; i++)
            {
                int index = i;
                RewardChoice choice = _choices[index];
                DishDef dish = _run?.Database.GetDish(choice.Id);
                RewardDishChoiceCardView card = Instantiate(_cardTemplate, _choiceContainer);
                card.gameObject.name = $"RewardDishChoice_{index + 1}";
                card.gameObject.SetActive(true);
                card.Bind(
                    choice,
                    dish,
                    LoadDishIcon(dish),
                    index,
                    OnChoiceClicked,
                    ShowDishTips,
                    HideDishTips);
                _spawnedCards.Add(card);
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_choiceContainer);
        }

        private void ClearCards()
        {
            for (int i = 0; i < _spawnedCards.Count; i++)
            {
                if (_spawnedCards[i] != null)
                {
                    Destroy(_spawnedCards[i].gameObject);
                }
            }

            _spawnedCards.Clear();
        }

        private void OnChoiceClicked(RewardDishChoiceCardView card, int choiceIndex)
        {
            if (_resolved || _confirmPending || choiceIndex < 0 || choiceIndex >= _choices.Count)
            {
                return;
            }

            HideDishTips();
            _confirmPending = true;
            RewardChoice choice = _choices[choiceIndex];
            string choiceName = string.IsNullOrWhiteSpace(choice.Name) ? choice.Id : choice.Name;
            var data = new ConfirmDialogData
            {
                Title = "确认选择菜品",
                Message = $"确定选择「{choiceName}」吗？",
                ConfirmText = "选择",
                CancelText = "返回",
                OnConfirm = () => ConfirmChoice(card, choiceIndex),
                OnCancel = () => _confirmPending = false,
            };
            GameApp.UI.OpenUIForm(UIForms.ConfirmDialog, UIForms.GroupDialog, data);
        }

        private void ConfirmChoice(RewardDishChoiceCardView card, int choiceIndex)
        {
            _confirmPending = false;
            if (_resolved || choiceIndex < 0 || choiceIndex >= _choices.Count || _onChoiceSelected == null)
            {
                return;
            }

            // 先锁住当前批次；n 选 m 回调可能同步 Open 下一批剩余候选，并把 _resolved 重置为 false。
            _resolved = true;
            if (!_onChoiceSelected.Invoke(choiceIndex))
            {
                _resolved = false;
                card?.PlayTargetFailed();
                return;
            }

            if (!_resolved)
            {
                return;
            }

            for (int i = 0; i < _spawnedCards.Count; i++)
            {
                _spawnedCards[i]?.SetResolved(true);
            }
        }

        private void OnSkipClicked()
        {
            if (_resolved || _confirmPending)
            {
                return;
            }

            _resolved = true;
            _onSkip?.Invoke();
        }

        private Sprite LoadDishIcon(DishDef dish)
        {
            return ContentIconLoader.LoadDish(dish)
                ?? Resources.Load<Sprite>("Sprites/UI/ui_icon_shop_food")
                ?? Resources.Load<Sprite>("Sprites/UI/card_action_food_dish");
        }

        private void ShowDishTips(RewardDishChoiceCardView card)
        {
            if (card == null || _run?.Database == null)
            {
                return;
            }

            int index = card.ChoiceIndex;
            if (index < 0 || index >= _choices.Count)
            {
                return;
            }

            RewardChoice choice = _choices[index];
            DishDef def = _run.Database.GetDish(choice.Id);
            if (def == null)
            {
                return;
            }

            RecipeBookSlot slot = null;
            if (!string.IsNullOrEmpty(choice.FlavorId))
            {
                slot = new RecipeBookSlot(choice.Id);
                slot.AddFlavor(choice.FlavorId);
            }

            FoodTipsView tips = _getFoodTips?.Invoke();
            if (tips == null)
            {
                return;
            }

            _hoveredCard = card;
            tips.Bind(BuildDishTipsData(def, slot));
            tips.Show();
            tips.transform.SetAsLastSibling();
            tips.PlaceAroundRectTransform(card.TipPlacementTarget, GetComponentInParent<Canvas>());
        }

        private void HideDishTips(RewardDishChoiceCardView card = null)
        {
            if (card != null && _hoveredCard != null && _hoveredCard != card)
            {
                return;
            }

            _hoveredCard = null;
            FoodTipsView tips = _getFoodTips?.Invoke();
            if (tips != null)
            {
                tips.Hide();
            }
        }

        private FoodTipsData BuildDishTipsData(DishDef def, RecipeBookSlot slot)
        {
            List<string> skillIds = ComposeSkillIds(def, slot?.ExtraSkillIds);
            List<string> flavorIds = ComposeFlavorIds(def, slot?.ExtraFlavorIds);
            var skills = new List<FoodInfoEntry>();
            foreach (string skillId in skillIds)
            {
                SkillDef skill = _run.Database.GetSkill(skillId);
                if (skill != null)
                {
                    skills.Add(new FoodInfoEntry(skill.Name, skill.Desc));
                }
            }

            var flavorNames = new List<string>();
            var flavorDetails = new List<FoodInfoEntry>();
            foreach (string flavorId in flavorIds)
            {
                FlavorDef flavor = _run.Database.GetFlavor(flavorId);
                if (flavor == null)
                {
                    continue;
                }

                flavorNames.Add(flavor.Name);
                flavorDetails.Add(new FoodInfoEntry(flavor.Name, flavor.Desc));
            }

            var summary = new FoodSummaryTipsData(def.Name, skills, flavorNames);
            float multiplier = slot != null ? slot.ScoreMultiplier : 1f;
            float score = def.Deliciousness + (slot != null ? slot.ScoreFlatBonus : 0f);
            return new FoodTipsData(
                summary,
                new FoodScoreTipsData(score, multiplier),
                Array.Empty<FoodMaterialTipsEntry>(),
                flavorDetails,
                Array.Empty<FoodInfoEntry>(),
                BuildRecipeSpecialTags(skillIds));
        }

        private IReadOnlyList<FoodInfoEntry> BuildRecipeSpecialTags(IReadOnlyList<string> skillIds)
        {
            if (skillIds == null || _run?.Database == null)
            {
                return Array.Empty<FoodInfoEntry>();
            }

            var termIds = new List<string>();
            foreach (string skillId in skillIds)
            {
                SkillDef skill = _run.Database.GetSkill(skillId);
                AddUniqueRange(termIds, skill?.TermIds);
            }

            var tags = new List<FoodInfoEntry>(termIds.Count);
            foreach (string termId in termIds)
            {
                cfg.Term term = GameApp.Config?.Tables?.TbTerm?.GetOrDefault(termId);
                tags.Add(term != null
                    ? new FoodInfoEntry(term.Name, term.Desc)
                    : new FoodInfoEntry(termId, string.Empty));
            }

            return tags;
        }

        private static void AddUniqueRange(List<string> list, IReadOnlyList<string> values)
        {
            if (list == null || values == null)
            {
                return;
            }

            foreach (string value in values)
            {
                if (!string.IsNullOrEmpty(value) && !list.Contains(value))
                {
                    list.Add(value);
                }
            }
        }

        private static List<string> ComposeSkillIds(DishDef def, IReadOnlyList<string> extraSkillIds)
        {
            var ids = new List<string>();
            if (def?.SkillIds != null)
            {
                ids.AddRange(def.SkillIds);
            }

            if (extraSkillIds != null)
            {
                ids.AddRange(extraSkillIds);
            }

            return ids;
        }

        private static List<string> ComposeFlavorIds(DishDef def, IReadOnlyList<string> extraFlavorIds)
        {
            var ids = new List<string>();
            if (def != null && !string.IsNullOrEmpty(def.FlavorId))
            {
                ids.Add(def.FlavorId);
            }

            if (extraFlavorIds != null)
            {
                ids.AddRange(extraFlavorIds);
            }

            return ids;
        }
    }
}
