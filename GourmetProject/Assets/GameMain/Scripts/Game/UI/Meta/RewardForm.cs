using System;
using System.Collections.Generic;
using DG.Tweening;
using GourmetProject.Core.Rng;
using GourmetProject.Game;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Flow;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;

namespace GourmetProject.Game.UI.Meta
{
    public sealed class RewardFormOpenArgs
    {
        private RewardFormOpenArgs(bool useGenericQueue, bool confirmBattleRewardAfterDone)
        {
            UseGenericQueue = useGenericQueue;
            ConfirmBattleRewardAfterDone = confirmBattleRewardAfterDone;
        }

        public bool UseGenericQueue { get; }

        public bool ConfirmBattleRewardAfterDone { get; }

        public static RewardFormOpenArgs GenericQueue(bool confirmBattleRewardAfterDone = false)
        {
            return new RewardFormOpenArgs(true, confirmBattleRewardAfterDone);
        }
    }

    /// <summary>
    /// 过关领奖界面：展示本周得分与发放的奖励，点「继续」推进到下一周（或通关返回菜单）。
    /// 结构全固定、落在 RewardForm.prefab，脚本只赋文本并按是否最终周切换按钮组。
    /// 奖励经 RewardGranter 按周命名流发放，并在推进时存档以支持「继续游戏」。
    /// </summary>
    public sealed class RewardForm : UGuiForm
    {
        [SerializeField] private Text _titleText;
        [SerializeField] private Button _continueButton;
        [SerializeField] private RectTransform _rewardListContent;
        [SerializeField] private RewardChoiceRowView _rewardRowTemplate;
        [Header("Reward Scrollbar")]
        [SerializeField] private ScrollRect _rewardScrollRect;
        [SerializeField] private Scrollbar _rewardScrollbar;
        [Min(0f)]
        [SerializeField] private float _rewardScrollbarIdleSeconds = 0.8f;
        [Min(0f)]
        [SerializeField] private float _rewardScrollbarFadeSeconds = 0.2f;

        private GameRun _run;
        private RewardOffer _offer;
        private readonly List<RewardChoiceRowView> _spawnedRows = new List<RewardChoiceRowView>();
        private string _rewardKey;
        private string _genericRewardKey;
        private string _genericRewardTitle;
        private int _lastTotal;
        private int _lastTarget;
        private CanvasGroup _rewardScrollbarGroup;
        private float _lastRewardScrollTime;
        private bool _rewardScrollListenerAttached;
        private bool _rewardScrollbarVisible;
        private bool _genericMode;
        private bool _confirmBattleRewardAfterGeneric;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            _continueButton.onClick.AddListener(OnContinue);
            ConfigureRewardScrollbar();
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            ConfigureRewardScrollbar();
            HideRewardScrollbar(immediate: true);

            _run = GameRunContext.Current;
            if (_run == null)
            {
                Close();
                return;
            }

            RewardFormOpenArgs args = userData as RewardFormOpenArgs;
            _genericMode = args != null && args.UseGenericQueue;
            _confirmBattleRewardAfterGeneric = args != null && args.ConfirmBattleRewardAfterDone;
            _genericRewardKey = string.Empty;
            _genericRewardTitle = string.Empty;
            _rewardKey = string.Empty;

            if (_genericMode)
            {
                if (!LoadNextGenericReward())
                {
                    Close();
                    return;
                }

                _lastTotal = 0;
                _lastTarget = 0;
                RefreshOffer();
                return;
            }

            BattleSession session = BattleForm.Active?.Session;
            int total = session != null && session.IsSettled ? session.LastResult.Total : 0;
            int target = session?.RequiredScore ?? _run.RequiredScore;
            ActionExecutionContext actionContext = BattleForm.Active?.CurrentBattleActionContext;
            _rewardKey = GameRun.BuildRewardKey(_run.WeekIndex, _run.CurrentDay, actionContext);

            _offer = _run.GetPendingRewardOffer(_rewardKey);
            if (_offer == null)
            {
                IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Reward, _rewardKey);
                _offer = RewardGranter.GenerateOffer(_run, _run.CurrentWeek, rng, actionContext);
                _run.SetPendingRewardOffer(_rewardKey, _offer);
                RunPersistence.Save(_run);
            }

            _lastTotal = total;
            _lastTarget = target;

            RefreshOffer();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            if (_rewardScrollbarGroup != null)
            {
                DOTween.Kill(_rewardScrollbarGroup);
                HideRewardScrollbar(immediate: true);
            }

            base.OnClose(isShutdown, userData);
        }

        private void Update()
        {
            if (!_rewardScrollbarVisible || _rewardScrollbarGroup == null)
            {
                return;
            }

            float idleSeconds = Mathf.Max(0f, _rewardScrollbarIdleSeconds);
            if (Time.unscaledTime - _lastRewardScrollTime >= idleSeconds)
            {
                HideRewardScrollbar(immediate: false);
            }
        }

        private void RefreshOffer()
        {
            _titleText.text = _genericMode && !string.IsNullOrEmpty(_genericRewardTitle)
                ? _genericRewardTitle
                : "奖励";
            // 行动轴模型下发奖不再推进周；底部只保留「继续行动」出口。
            _continueButton.gameObject.SetActive(true);

            SetButtonLabel(_continueButton, _genericMode ? "完成" : "继续行动");

            RebuildRewardRows();
        }

        private bool LoadNextGenericReward()
        {
            if (_run == null || !_run.TryPeekPendingGenericReward(out string key, out string title, out RewardOffer offer))
            {
                _offer = null;
                _genericRewardKey = string.Empty;
                _genericRewardTitle = string.Empty;
                return false;
            }

            _offer = offer;
            _genericRewardKey = key;
            _genericRewardTitle = string.IsNullOrWhiteSpace(title) ? "奖励" : title;
            return true;
        }

        private void OnContinue()
        {
            CompleteRewards(closeForm: true);
        }

        /// <summary>结算并推进：清空 pending offer/碎片包、存档、（可选）关界面并回到行动轴，等效于点「继续」。</summary>
        private void CompleteRewards(bool closeForm)
        {
            _run.ClearPendingFragmentPack();

            if (_genericMode)
            {
                _run.ClearPendingGenericRewardOffer(_genericRewardKey);
                RunPersistence.Save(_run);
                if (LoadNextGenericReward())
                {
                    RefreshOffer();
                    return;
                }

                if (closeForm)
                {
                    Close();
                }

                if (_confirmBattleRewardAfterGeneric)
                {
                    BattleForm.Active?.OnRewardConfirmed();
                }
                else if (!closeForm)
                {
                    BattleForm.Active?.OpenShop();
                }

                return;
            }

            _run.ClearPendingRewardOffer();
            if (_run.HasPendingGenericRewards)
            {
                RunPersistence.Save(_run);
                _genericMode = true;
                _confirmBattleRewardAfterGeneric = true;
                if (LoadNextGenericReward())
                {
                    if (!closeForm)
                    {
                        ReopenReward();
                        return;
                    }

                    RefreshOffer();
                    return;
                }
            }

            RunPersistence.Save(_run);
            if (closeForm)
            {
                Close();
            }

            BattleForm.Active?.OnRewardConfirmed();
        }

        /// <summary>领取动作后：若这是最后一个奖励（offer 已全部领取），直接等效于点「继续」。</summary>
        private bool TryAutoComplete(bool closeForm)
        {
            if (_offer == null || _run == null || !_offer.IsFullyClaimed)
            {
                return false;
            }

            CompleteRewards(closeForm);
            return true;
        }

        private void Close()
        {
            GameApp.UI.CloseUIForm(UIForm);
        }

        private void ClaimBaseGold()
        {
            if (_offer == null || _offer.BaseGoldClaimed)
            {
                return;
            }

            if (_genericMode)
            {
                _run.Gold += _offer.BaseGold;
                _offer.MarkBaseGoldClaimed();
            }
            else
            {
                RewardGranter.ApplyBaseGold(_run, _offer);
            }

            SaveCurrentOffer();
            RunPersistence.Save(_run);

            // 只剩金币这一个奖励，领完直接等效于点「继续」。
            if (TryAutoComplete(closeForm: true))
            {
                return;
            }

            RefreshOffer();
        }

        private void ClaimChoice(int groupIndex, int index, IReadOnlyList<RewardChoice> groupChoices)
        {
            if (_offer == null || groupChoices == null || index < 0 || index >= groupChoices.Count)
            {
                return;
            }

            if (IsChoiceResolved(groupIndex))
            {
                return;
            }

            RewardChoice choice = groupChoices[index];
            if (choice == null)
            {
                return;
            }

            if (choice.Kind == cfg.RewardKind.DishChoice)
            {
                OpenDishPack(groupIndex, groupChoices);
                return;
            }

            if (choice.Kind == cfg.RewardKind.FragmentChoice)
            {
                RewardGranter.ApplyFragmentPack(_run, groupChoices);
                SaveCurrentOffer();
                RunPersistence.Save(_run);

                Close();
                BattleForm.Active?.OpenRewardTableEdit(placed =>
                {
                    if (placed && !IsChoiceResolved(groupIndex))
                    {
                        MarkChoiceClaimed(groupIndex, index);
                        SaveCurrentOffer();
                    }

                    RunPersistence.Save(_run);

                    // 拼完碎片（placed）且这是最后一个奖励：不再弹回 RewardForm，直接等效于点「继续」。
                    // 若在餐桌编辑里选择跳过（!placed），碎片奖励仍保留，照常弹回 RewardForm。
                    if (TryAutoComplete(closeForm: false))
                    {
                        return;
                    }

                    ReopenReward();
                });
                return;
            }

            RewardGranter.ApplyChoice(_run, choice);
            MarkChoiceClaimed(groupIndex, index);
            SaveCurrentOffer();
            RunPersistence.Save(_run);

            if (TryAutoComplete(closeForm: true))
            {
                return;
            }

            RefreshOffer();
        }

        private bool IsChoiceResolved(int groupIndex)
        {
            return GroupFor(groupIndex).IsResolved;
        }

        private bool IsChoiceClaimed(int groupIndex, int index)
        {
            return GroupFor(groupIndex).IsClaimed(index);
        }

        private void OpenDishPack(int groupIndex, IReadOnlyList<RewardChoice> groupChoices)
        {
            BattleForm battle = BattleForm.Active;
            if (battle == null || groupChoices == null || groupChoices.Count == 0)
            {
                return;
            }

            bool opened = battle.OpenRewardDishPack(
                groupChoices,
                (choiceIndex, bookIndex) => ClaimDishChoiceToBook(groupIndex, choiceIndex, bookIndex, groupChoices),
                ReopenReward);
            if (opened)
            {
                Close();
            }
        }

        private bool ClaimDishChoiceToBook(
            int groupIndex,
            int choiceIndex,
            int bookIndex,
            IReadOnlyList<RewardChoice> groupChoices)
        {
            if (_offer == null || _run == null || groupChoices == null || IsChoiceResolved(groupIndex)
                || choiceIndex < 0 || choiceIndex >= groupChoices.Count)
            {
                return false;
            }

            RewardChoice choice = groupChoices[choiceIndex];
            if (!RewardGranter.ApplyDishChoiceToBook(_run, choice, bookIndex))
            {
                return false;
            }

            MarkChoiceClaimed(groupIndex, choiceIndex);
            SaveOfferAndReopenReward();
            return true;
        }

        private void SaveOfferAndReopenReward()
        {
            SaveCurrentOffer();
            RunPersistence.Save(_run);

            // 菜品是最后一个奖励且已放入菜谱：直接等效于点「继续」，不再弹回 RewardForm。
            if (TryAutoComplete(closeForm: false))
            {
                return;
            }

            ReopenReward();
        }

        private void ReopenReward()
        {
            if (_genericMode)
            {
                GameApp.UI.OpenUIForm(
                    UIForms.Reward,
                    UIForms.GroupDialog,
                    RewardFormOpenArgs.GenericQueue(_confirmBattleRewardAfterGeneric));
                return;
            }

            GameApp.UI.OpenUIForm(UIForms.Reward, UIForms.GroupDialog);
        }

        private void SaveCurrentOffer()
        {
            if (_genericMode)
            {
                _run.SetPendingGenericRewardOffer(_genericRewardKey, _offer);
                return;
            }

            _run.SetPendingRewardOffer(_rewardKey, _offer);
        }

        private void MarkChoiceClaimed(int groupIndex, int index)
        {
            GroupFor(groupIndex).MarkClaimed(index);
        }

        private RewardChoiceGroup GroupFor(int groupIndex)
        {
            return groupIndex < 0 ? _offer.SpecificGroup : _offer.GetFixedGroup(groupIndex);
        }

        private void RebuildRewardRows()
        {
            for (int i = 0; i < _spawnedRows.Count; i++)
            {
                if (_spawnedRows[i] != null)
                {
                    Destroy(_spawnedRows[i].gameObject);
                }
            }

            _spawnedRows.Clear();

            if (_rewardRowTemplate == null || _rewardListContent == null || _offer == null)
            {
                return;
            }

            _rewardRowTemplate.gameObject.SetActive(false);
            AddFixedGoldRow();

            for (int i = 0; i < _offer.FixedGroups.Count; i++)
            {
                RewardChoiceGroup group = _offer.FixedGroups[i];
                AddChoiceRows(string.IsNullOrEmpty(group.Title) ? "固定奖励" : group.Title, group.Choices, groupIndex: i);
            }

            AddChoiceRows(_genericMode ? "随机食物" : "特定奖励", _offer.SpecificGroup.Choices, groupIndex: -1);
            HideRewardScrollbar(immediate: true);
        }

        private void ConfigureRewardScrollbar()
        {
            if (_rewardScrollRect == null && _rewardListContent != null)
            {
                _rewardScrollRect = _rewardListContent.GetComponentInParent<ScrollRect>(true);
            }

            if (_rewardScrollRect == null)
            {
                return;
            }

            if (_rewardScrollbar == null)
            {
                _rewardScrollbar = _rewardScrollRect.verticalScrollbar;
            }

            _rewardScrollRect.vertical = true;
            _rewardScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            if (!_rewardScrollListenerAttached)
            {
                _rewardScrollRect.onValueChanged.AddListener(OnRewardScrollChanged);
                _rewardScrollListenerAttached = true;
            }

            if (_rewardScrollbar == null)
            {
                return;
            }

            _rewardScrollbar.gameObject.SetActive(true);
            _rewardScrollbarGroup = _rewardScrollbar.GetComponent<CanvasGroup>();
            if (_rewardScrollbarGroup == null)
            {
                _rewardScrollbarGroup = _rewardScrollbar.gameObject.AddComponent<CanvasGroup>();
            }
        }

        private void OnRewardScrollChanged(Vector2 _)
        {
            ShowRewardScrollbar();
        }

        private void ShowRewardScrollbar()
        {
            ConfigureRewardScrollbar();
            if (_rewardScrollbarGroup == null)
            {
                return;
            }

            _lastRewardScrollTime = Time.unscaledTime;
            bool wasVisible = _rewardScrollbarVisible;
            _rewardScrollbarVisible = true;
            _rewardScrollbarGroup.interactable = true;
            _rewardScrollbarGroup.blocksRaycasts = true;
            if (!wasVisible)
            {
                UITransition.Fade(_rewardScrollbarGroup, 1f, Mathf.Max(0f, _rewardScrollbarFadeSeconds));
            }
        }

        private void HideRewardScrollbar(bool immediate)
        {
            if (_rewardScrollbarGroup == null)
            {
                return;
            }

            _rewardScrollbarVisible = false;
            _rewardScrollbarGroup.interactable = false;
            _rewardScrollbarGroup.blocksRaycasts = false;
            UITransition.Fade(_rewardScrollbarGroup, 0f, immediate ? 0f : Mathf.Max(0f, _rewardScrollbarFadeSeconds));
        }

        private void AddFixedGoldRow()
        {
            if (_offer.BaseGoldClaimed)
            {
                return;
            }

            RewardChoiceRowView row = CreateRewardRow();
            if (row == null)
            {
                return;
            }

            row.Bind(
                $"金币 +{_offer.BaseGold}",
                "点击领取固定金币。",
                LoadGoldIcon(),
                false,
                true,
                false,
                ClaimBaseGold);
        }

        private void AddChoiceRows(
            string groupName,
            IReadOnlyList<RewardChoice> choices,
            int groupIndex)
        {
            if (choices == null || choices.Count == 0)
            {
                return;
            }

            if (IsChoiceResolved(groupIndex))
            {
                return;
            }

            if (IsFragmentPack(choices))
            {
                AddFragmentPackRow(groupName, choices, groupIndex);
                return;
            }

            if (IsDishPack(choices))
            {
                AddDishPackRow(groupName, choices, groupIndex);
                return;
            }

            for (int i = 0; i < choices.Count; i++)
            {
                int index = i;
                RewardChoice choice = choices[i];
                if (IsChoiceClaimed(groupIndex, index))
                {
                    continue;
                }

                RewardChoiceRowView row = CreateRewardRow();
                if (row == null)
                {
                    return;
                }

                row.Bind(
                    BuildChoiceTitle(groupName, choice),
                    BuildChoiceDescription(choice),
                    LoadChoiceIcon(choice),
                    false,
                    true,
                    false,
                    () => ClaimChoice(groupIndex, index, choices));
            }
        }

        private void AddFragmentPackRow(
            string groupName,
            IReadOnlyList<RewardChoice> choices,
            int groupIndex)
        {
            RewardChoiceRowView row = CreateRewardRow();
            if (row == null)
            {
                return;
            }

            int index = 0;
            row.Bind(
                $"{groupName}：碎片选择包",
                BuildFragmentPackDescription(choices),
                LoadChoiceIcon(choices[index]),
                false,
                true,
                false,
                () => ClaimChoice(groupIndex, index, choices));
        }

        private void AddDishPackRow(
            string groupName,
            IReadOnlyList<RewardChoice> choices,
            int groupIndex)
        {
            RewardChoiceRowView row = CreateRewardRow();
            if (row == null)
            {
                return;
            }

            int index = 0;
            row.Bind(
                $"{groupName}：菜品选择包",
                $"点击后从 {choices.Count} 个菜品中选择 1 个放入菜谱。",
                LoadChoiceIcon(choices[index]),
                false,
                true,
                false,
                () => ClaimChoice(groupIndex, index, choices));
        }

        private RewardChoiceRowView CreateRewardRow()
        {
            if (_rewardRowTemplate == null || _rewardListContent == null)
            {
                return null;
            }

            RewardChoiceRowView row = Instantiate(_rewardRowTemplate, _rewardListContent);
            row.gameObject.SetActive(true);
            _spawnedRows.Add(row);
            return row;
        }

        private string BuildChoiceTitle(string groupName, RewardChoice choice)
        {
            if (choice == null)
            {
                return groupName;
            }

            string category;
            switch (choice.Kind)
            {
                case cfg.RewardKind.Gold:
                    category = "金币";
                    break;
                case cfg.RewardKind.DishChoice:
                    category = "菜品选择包";
                    break;
                case cfg.RewardKind.FragmentChoice:
                    category = "碎片选择包";
                    break;
                case cfg.RewardKind.PassiveItemChoice:
                    category = "被动道具";
                    break;
                case cfg.RewardKind.ActiveItemGrant:
                    category = "主动道具";
                    break;
                default:
                    category = "奖励";
                    break;
            }

            return string.IsNullOrEmpty(choice.Name)
                ? $"{groupName}：{category}"
                : $"{groupName}：{category} - {choice.Name}";
        }

        private static string BuildChoiceDescription(RewardChoice choice)
        {
            if (choice == null)
            {
                return string.Empty;
            }

            if (choice.Kind == cfg.RewardKind.Gold || choice.IsFallbackGold)
            {
                return $"领取后获得金币 +{choice.GoldAmount}。";
            }

            if (!string.IsNullOrEmpty(choice.Description))
            {
                return choice.Description;
            }

            switch (choice.Kind)
            {
                case cfg.RewardKind.DishChoice:
                    return "加入菜谱池，后续美食挑战中可能抽到。";
                case cfg.RewardKind.FragmentChoice:
                    return "获得餐桌碎片包，进入餐桌编辑后选择并拼贴一块。";
                case cfg.RewardKind.PassiveItemChoice:
                    return "获得后持续生效，重复获得时会升级或折算。";
                case cfg.RewardKind.ActiveItemGrant:
                    return "获得一个主动道具，可在战斗中使用。";
                default:
                    return "领取后加入本轮运行。";
            }
        }

        private static bool IsFragmentPack(IReadOnlyList<RewardChoice> choices)
        {
            return choices != null && choices.Count > 0 && choices[0]?.Kind == cfg.RewardKind.FragmentChoice;
        }

        private static bool IsDishPack(IReadOnlyList<RewardChoice> choices)
        {
            return choices != null && choices.Count > 0 && choices[0]?.Kind == cfg.RewardKind.DishChoice;
        }

        private static string BuildFragmentPackDescription(IReadOnlyList<RewardChoice> choices)
        {
            if (choices == null || choices.Count == 0)
            {
                return "点击后进入餐桌编辑。";
            }

            return $"点击后进入餐桌编辑，从 {choices.Count} 个餐桌碎片中选择 1 个拼贴。";
        }

        private Sprite LoadChoiceIcon(RewardChoice choice)
        {
            if (choice == null)
            {
                return null;
            }

            switch (choice.Kind)
            {
                case cfg.RewardKind.Gold:
                    return LoadGoldIcon();
                case cfg.RewardKind.DishChoice:
                    return LoadDishIcon(choice.Id);
                case cfg.RewardKind.FragmentChoice:
                    return Resources.Load<Sprite>("Sprites/UI/ui_icon_shop_fragment")
                        ?? Resources.Load<Sprite>("Sprites/UI/card_action_food_fragment");
                case cfg.RewardKind.PassiveItemChoice:
                case cfg.RewardKind.ActiveItemGrant:
                {
                    cfg.ItemKind kind = choice.Kind == cfg.RewardKind.ActiveItemGrant ? cfg.ItemKind.Active : cfg.ItemKind.Passive;
                    ItemDefinition item = ItemDefinition.Get(_run?.Tables ?? GameApp.Config.Tables, choice.Id, kind);
                    Sprite icon = RunItemSlotView.LoadIcon(item);
                    if (icon != null)
                    {
                        return icon;
                    }

                    return Resources.Load<Sprite>(choice.Kind == cfg.RewardKind.ActiveItemGrant
                        ? "Sprites/UI/card_action_food_active"
                        : "Sprites/UI/card_action_food_passive");
                }
                default:
                    return null;
            }
        }

        private Sprite LoadDishIcon(string dishId)
        {
            DishDef dish = _run?.Database.GetDish(dishId);
            Sprite icon = ContentIconLoader.LoadDish(dish);
            if (icon != null)
            {
                return icon;
            }

            return Resources.Load<Sprite>("Sprites/UI/ui_icon_shop_food")
                ?? Resources.Load<Sprite>("Sprites/UI/card_action_food_dish");
        }

        private static Sprite LoadGoldIcon()
        {
            return Resources.Load<Sprite>("Sprites/UI/icon_coin")
                ?? Resources.Load<Sprite>("Sprites/UI/card_action_food_gold");
        }

        private static void SetButtonLabel(Button button, string text)
        {
            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = text;
            }
        }
    }
}
