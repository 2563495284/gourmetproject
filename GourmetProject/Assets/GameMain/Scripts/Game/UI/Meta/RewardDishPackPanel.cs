using System;
using System.Collections.Generic;
using BreakInfinity;
using DG.Tweening;
using GourmetProject.Game;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Game.UI.Common;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 食物奖励选择页。候选食物居中展示，点击后直接完成领取。
    /// </summary>
    public sealed class RewardDishPackPanel : MonoBehaviour
    {
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private TMP_Text _promptText;
        [SerializeField] private RectTransform _choiceContainer;
        [SerializeField] private RewardDishChoiceCardView _cardTemplate;
        [SerializeField] private Button _skipButton;

        private readonly List<RewardDishChoiceCardView> _spawnedCards = new();
        private readonly List<RewardChoice> _choices = new();
        private readonly HashSet<int> _claimedOnPage = new();
        private GameRun _run;
        private Func<int, bool> _onChoiceSelected;
        private Action _onFinish;
        private Func<FoodTipsView> _getFoodTips;
        private Action<RewardDishChoiceCardView> _playSelectionFly;
        private RewardDishChoiceCardView _hoveredCard;
        private bool _resolved;
        private bool _wired;
        private string _basePrompt = string.Empty;
        private int _requiredPicks;
        private int _claimedBeforeOpen;
        private Sequence _entrySequence;
        [SerializeField] private StaggerTransitionSettings _cardTransition = new StaggerTransitionSettings();

        internal IReadOnlyList<RewardChoice> CurrentChoices => _choices;

        private void Awake()
        {
            EnsureWired();
        }

        private void OnDisable()
        {
            // 页面切换（食谱/餐桌）只是挂起；候选、回调和已选状态只能由显式 Close 销毁。
            _entrySequence?.Kill();
            _entrySequence = null;
            HideDishTips();
        }

        public void Open(
            GameRun run,
            RewardChoiceGroup group,
            IReadOnlyList<RewardChoice> choices,
            Func<int, bool> onChoiceSelected,
            Action onFinish,
            Func<FoodTipsView> getFoodTips = null,
            Action<RewardDishChoiceCardView> playSelectionFly = null)
        {
            EnsureWired();
            HideDishTips();
            ClearCards();

            _run = run;
            _onChoiceSelected = onChoiceSelected;
            _onFinish = onFinish;
            _getFoodTips = getFoodTips;
            _playSelectionFly = playSelectionFly;
            _resolved = false;
            _choices.Clear();
            _claimedOnPage.Clear();

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

            _basePrompt = BuildGroupText(group, "选择一个食物加入食谱");
            _claimedBeforeOpen = group?.ClaimedIndices.Count ?? 0;
            int remainingRequired = (group?.RequiredChoiceCount ?? 1) - _claimedBeforeOpen;
            _requiredPicks = Mathf.Clamp(remainingRequired, 1, Mathf.Max(1, _choices.Count));

            BuildCards();
            RefreshPresentation();
            PlayCardsIn();
        }

        private static string BuildGroupText(RewardChoiceGroup group, string fallback)
        {
            if (group == null)
            {
                return fallback;
            }

            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(group.Title))
            {
                lines.Add(group.Title);
            }
            if (!string.IsNullOrWhiteSpace(group.Description))
            {
                lines.Add(group.Description);
            }
            if (!string.IsNullOrWhiteSpace(group.RuleText))
            {
                lines.Add(group.RuleText);
            }
            return lines.Count > 0 ? string.Join("\n", lines) : fallback;
        }

        public void Close()
        {
            _entrySequence?.Kill();
            _entrySequence = null;
            HideDishTips();
            ClearCards();
            _choices.Clear();
            _run = null;
            _onChoiceSelected = null;
            _onFinish = null;
            _getFoodTips = null;
            _playSelectionFly = null;
            _basePrompt = string.Empty;
            _requiredPicks = 0;
            _claimedBeforeOpen = 0;
            _claimedOnPage.Clear();
            _resolved = false;

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
            BalancedWrapLayoutGroup layout = _choiceContainer.GetComponent<BalancedWrapLayoutGroup>();
            if (layout != null)
            {
                layout.enabled = true;
                layout.childAlignment = TextAnchor.MiddleCenter;
            }

            var pending = new List<(RewardDishChoiceCardView card, RewardChoice choice, DishDef dish, int index)>();
            for (int i = 0; i < _choices.Count; i++)
            {
                int index = i;
                RewardChoice choice = _choices[index];
                DishDef dish = _run?.Database.GetDish(choice.Id);
                RewardDishChoiceCardView card = Instantiate(_cardTemplate, _choiceContainer);
                card.gameObject.name = $"RewardDishChoice_{index + 1}";
                card.gameObject.SetActive(true);
                pending.Add((card, choice, dish, index));
                _spawnedCards.Add(card);
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_choiceContainer);

            for (int i = 0; i < pending.Count; i++)
            {
                (RewardDishChoiceCardView card, RewardChoice choice, DishDef dish, int index) = pending[i];
                card.Bind(
                    choice,
                    dish,
                    LoadDishIcon(dish),
                    index,
                    OnChoiceClicked,
                    ShowDishTips,
                    HideDishTips);
            }
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

        private void PlayCardsIn()
        {
            _entrySequence?.Kill();
            var rects = new List<RectTransform>(_spawnedCards.Count);
            for (int i = 0; i < _spawnedCards.Count; i++)
            {
                if (_spawnedCards[i] != null && _spawnedCards[i].transform is RectTransform rect)
                {
                    rects.Add(rect);
                }
            }

            StaggerTransitionSettings settings = _cardTransition ?? new StaggerTransitionSettings();
            _entrySequence = UITransition.StaggerIn(
                rects,
                settings.Duration,
                settings.Interval,
                settings.MaxDelay);
        }

        private void OnChoiceClicked(RewardDishChoiceCardView card, int choiceIndex)
        {
            if (_resolved
                || choiceIndex < 0
                || choiceIndex >= _choices.Count
                || _claimedOnPage.Contains(choiceIndex))
            {
                return;
            }

            HideDishTips();
            SelectChoice(card, choiceIndex);
        }

        private void SelectChoice(RewardDishChoiceCardView card, int choiceIndex)
        {
            if (_resolved || _onChoiceSelected == null)
            {
                return;
            }

            if (!_onChoiceSelected.Invoke(choiceIndex))
            {
                card?.PlayTargetFailed();
                return;
            }

            _claimedOnPage.Add(choiceIndex);
            card?.SetResolved(true);
            _playSelectionFly?.Invoke(card);
            RefreshPresentation();

            if (_claimedOnPage.Count >= _requiredPicks)
            {
                Finish();
            }
        }

        private void OnSkipClicked()
        {
            if (_resolved)
            {
                return;
            }

            Finish();
        }

        private void Finish()
        {
            if (_resolved)
            {
                return;
            }

            _resolved = true;
            _onFinish?.Invoke();
        }

        private void RefreshPresentation()
        {
            if (_promptText != null)
            {
                int claimed = _claimedBeforeOpen + _claimedOnPage.Count;
                int required = _claimedBeforeOpen + _requiredPicks;
                string prompt = required > 1
                    ? $"{_basePrompt}\n已领 {claimed}/{required}"
                    : _basePrompt;
                SemanticDescriptionFormatter.Set(_promptText, prompt);
            }

            if (_skipButton != null)
            {
                _skipButton.interactable = true;
                TMP_Text label = _skipButton.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.text = _claimedBeforeOpen + _claimedOnPage.Count > 0
                        ? "结束"
                        : "离开";
                }
            }
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
            tips.Bind(BuildDishTipsData(_run, def, slot));
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

        public static FoodTipsData BuildDishTipsData(GameRun run, RewardChoice choice)
        {
            DishDef def = run?.Database?.GetDish(choice?.Id);
            if (def == null)
            {
                return null;
            }

            RecipeBookSlot slot = null;
            if (!string.IsNullOrEmpty(choice.FlavorId))
            {
                slot = new RecipeBookSlot(choice.Id);
                slot.AddFlavor(choice.FlavorId);
            }

            return BuildDishTipsData(run, def, slot);
        }

        private static FoodTipsData BuildDishTipsData(GameRun run, DishDef def, RecipeBookSlot slot)
        {
            List<string> skillIds = ComposeSkillIds(def, slot?.ExtraSkillIds);
            List<string> flavorIds = ComposeFlavorIds(def, slot?.ExtraFlavorIds);
            var skills = new List<FoodInfoEntry>();
            foreach (string skillId in skillIds)
            {
                SkillDef skill = run.Database.GetSkill(skillId);
                if (skill != null)
                {
                    FoodTipsDataFactory.AppendSkillEntries(skills, skill);
                }
            }

            var flavorNames = new List<string>();
            var flavorDetails = new List<FoodInfoEntry>();
            foreach (string flavorId in flavorIds)
            {
                FlavorDef flavor = run.Database.GetFlavor(flavorId);
                if (flavor == null)
                {
                    continue;
                }

                flavorNames.Add(flavor.Name);
                flavorDetails.Add(new FoodInfoEntry(flavor.Name, flavor.Desc));
            }

            var summary = new FoodSummaryTipsData(
                def.Name,
                skills,
                flavorNames,
                countAs: FoodTipsDataFactory.ResolveIntrinsicCountAs(
                    def,
                    skillIds,
                    flavorIds,
                    run.Database));
            BigDouble multiplier = slot != null ? slot.ScoreMultiplier : BigDouble.One;
            BigDouble score = def.Deliciousness + (slot != null ? slot.ScoreFlatBonus : 0f);
            return new FoodTipsData(
                summary,
                new FoodScoreTipsData(score, multiplier),
                flavorDetails,
                Array.Empty<FoodInfoEntry>(),
                BuildRecipeSpecialTags(run, skillIds));
        }

        private static IReadOnlyList<FoodInfoEntry> BuildRecipeSpecialTags(
            GameRun run,
            IReadOnlyList<string> skillIds)
        {
            if (skillIds == null || run?.Database == null)
            {
                return Array.Empty<FoodInfoEntry>();
            }

            var termIds = new List<string>();
            foreach (string skillId in skillIds)
            {
                SkillDef skill = run.Database.GetSkill(skillId);
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
