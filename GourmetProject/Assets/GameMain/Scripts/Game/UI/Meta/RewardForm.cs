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
        private RewardFormOpenArgs(bool useGenericQueue, bool confirmBattleRewardAfterDone, bool allowResultPeek)
        {
            UseGenericQueue = useGenericQueue;
            ConfirmBattleRewardAfterDone = confirmBattleRewardAfterDone;
            AllowResultPeek = allowResultPeek;
        }

        public bool UseGenericQueue { get; }

        public bool ConfirmBattleRewardAfterDone { get; }

        public bool AllowResultPeek { get; }

        public static RewardFormOpenArgs BattleReward(bool allowResultPeek = true)
        {
            return new RewardFormOpenArgs(false, false, allowResultPeek);
        }

        public static RewardFormOpenArgs GenericQueue(bool confirmBattleRewardAfterDone = false, bool allowResultPeek = false)
        {
            return new RewardFormOpenArgs(true, confirmBattleRewardAfterDone, allowResultPeek);
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
        [SerializeField] private Button _peekHideButton;
        [SerializeField] private Button _peekReturnButton;
        [SerializeField] private RectTransform _rewardListContent;
        [SerializeField] private RewardChoiceRowView _rewardRowTemplate;
        [Header("Reward Scrollbar")]
        [SerializeField] private ScrollRect _rewardScrollRect;
        [SerializeField] private Scrollbar _rewardScrollbar;
        [Min(0f)]
        [SerializeField] private float _rewardScrollbarIdleSeconds = 0.8f;
        [Min(0f)]
        [SerializeField] private float _rewardScrollbarFadeSeconds = 0.2f;

        private const int NoExpandedChoicePackGroup = int.MinValue;

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
        private bool _allowResultPeek;
        private bool _peekHidden;
        private int _expandedChoicePackGroupIndex = NoExpandedChoicePackGroup;
        private readonly List<PeekChildState> _peekChildStates = new List<PeekChildState>();

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            _continueButton.onClick.AddListener(OnContinue);
            if (_peekHideButton != null)
            {
                _peekHideButton.onClick.AddListener(HideForResultPeek);
            }

            if (_peekReturnButton != null)
            {
                _peekReturnButton.onClick.AddListener(ShowFromResultPeek);
            }

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
            _confirmBattleRewardAfterGeneric = args != null
                ? args.ConfirmBattleRewardAfterDone
                : _run.PendingGenericRewardsConfirmBattleAfterDone;
            if (_genericMode)
            {
                _run.PendingGenericRewardsConfirmBattleAfterDone = _confirmBattleRewardAfterGeneric;
            }

            _allowResultPeek = args != null && args.AllowResultPeek;
            _peekHidden = false;
            _peekChildStates.Clear();
            ConfigurePeekButtons();
            _genericRewardKey = string.Empty;
            _genericRewardTitle = string.Empty;
            _rewardKey = string.Empty;
            _expandedChoicePackGroupIndex = NoExpandedChoicePackGroup;

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
            if (_offer == null && _run.HasPendingRewardOffer)
            {
                _rewardKey = _run.PendingRewardKey;
                _offer = _run.GetPendingRewardOffer();
            }

            if (_offer == null)
            {
                IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Reward, _rewardKey);
                _offer = RewardGranter.GenerateOffer(_run, _run.CurrentWeek, rng, actionContext);
                _run.SetPendingRewardOffer(_rewardKey, _offer);
                BattleForm.Active?.SavePendingRewardBattleView();
                RunPersistence.Save(_run);
            }

            _lastTotal = total;
            _lastTarget = target;

            RefreshOffer();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            if (_peekHidden)
            {
                RestorePeekChildren();
            }

            BattleForm.Active?.SetRewardPeekOnly(false);
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
            _expandedChoicePackGroupIndex = NoExpandedChoicePackGroup;
            return true;
        }

        private void OnContinue()
        {
            CompleteRewards(closeForm: true);
        }

        /// <summary>结算并推进：清空 pending offer/碎片包、存档、（可选）关界面并回到行动轴，等效于点「继续」。</summary>
        private void CompleteRewards(bool closeForm)
        {
            BattleForm.Active?.CloseRewardOperationPages();
            _run.ClearPendingFragmentPack();

            if (_genericMode)
            {
                _run.ClearPendingGenericRewardOffer(_genericRewardKey);
                if (LoadNextGenericReward())
                {
                    RunPersistence.Save(_run);
                    RefreshOffer();
                    return;
                }

                _run.PendingGenericRewardsConfirmBattleAfterDone = false;
                if (_confirmBattleRewardAfterGeneric)
                {
                    _run.ClearPendingRewardBattleView();
                }
                RunPersistence.Save(_run);

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
                _genericMode = true;
                _confirmBattleRewardAfterGeneric = true;
                _run.PendingGenericRewardsConfirmBattleAfterDone = true;
                if (LoadNextGenericReward())
                {
                    RunPersistence.Save(_run);
                    if (!closeForm)
                    {
                        ReopenReward();
                        return;
                    }

                    RefreshOffer();
                    return;
                }
            }

            _run.ClearPendingRewardBattleView();
            RunPersistence.Save(_run);
            if (closeForm)
            {
                Close();
            }

            BattleForm.Active?.OnRewardConfirmed();
        }

        /// <summary>通用奖励保持自动完成；Food 战斗奖励必须等待玩家明确点击“继续行动”。</summary>
        private bool TryAutoComplete(bool closeForm)
        {
            if (!_genericMode || _offer == null || _run == null || !_offer.IsFullyClaimed)
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

        private void ConfigurePeekButtons()
        {
            if (_peekHideButton != null)
            {
                _peekHideButton.gameObject.SetActive(_allowResultPeek && !_peekHidden);
            }

            if (_peekReturnButton != null)
            {
                _peekReturnButton.gameObject.SetActive(_allowResultPeek && _peekHidden);
            }
        }

        private void HideForResultPeek()
        {
            if (!_allowResultPeek || _peekHidden || _peekReturnButton == null)
            {
                return;
            }

            _peekHidden = true;
            _peekChildStates.Clear();
            Transform returnTransform = _peekReturnButton.transform;
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child == null || child == returnTransform)
                {
                    continue;
                }

                _peekChildStates.Add(new PeekChildState(child, child.gameObject.activeSelf));
                child.gameObject.SetActive(false);
            }

            ConfigurePeekButtons();
            BattleForm.Active?.SetRewardPeekOnly(true);
        }

        private void ShowFromResultPeek()
        {
            if (!_peekHidden)
            {
                ConfigurePeekButtons();
                BattleForm.Active?.SetRewardPeekOnly(false);
                return;
            }

            BattleForm battle = BattleForm.Active;
            if (battle != null)
            {
                battle.ReturnToPendingRewardView(RestorePeekChildren);
                return;
            }

            RestorePeekChildren();
        }

        private void RestorePeekChildren()
        {
            for (int i = 0; i < _peekChildStates.Count; i++)
            {
                PeekChildState state = _peekChildStates[i];
                if (state.Transform != null)
                {
                    state.Transform.gameObject.SetActive(state.ActiveSelf);
                }
            }

            _peekChildStates.Clear();
            _peekHidden = false;
            ConfigurePeekButtons();
            BattleForm.Active?.SetRewardPeekOnly(false);
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
                using (RunPersistence.SuppressSave())
                {
                    RewardGranter.ApplyBaseGold(_run, _offer);
                }
            }

            SaveCurrentOffer();
            RefreshBattlePersistentHud();

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
                using (RunPersistence.SuppressSave())
                {
                    RewardGranter.ApplyFragmentPack(_run, groupChoices);
                }

                SaveCurrentOffer();

                Close();
                BattleForm.Active?.OpenRewardTableEdit(placed =>
                {
                    if (placed && !IsChoiceResolved(groupIndex))
                    {
                        MarkChoiceClaimed(groupIndex, index);
                        SaveCurrentOffer();
                    }

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

            using (RunPersistence.SuppressSave())
            {
                RewardGranter.ApplyChoice(_run, choice);
            }

            MarkChoiceClaimed(groupIndex, index);
            SaveCurrentOffer();
            RefreshBattlePersistentHud();

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

        private void OpenDishPack(
            int groupIndex,
            IReadOnlyList<RewardChoice> groupChoices,
            bool closeRewardFormOnOpen = true)
        {
            BattleForm battle = BattleForm.Active;
            if (battle == null || groupChoices == null || groupChoices.Count == 0)
            {
                return;
            }

            var remainingChoices = new List<RewardChoice>();
            var sourceIndices = new List<int>();
            for (int i = 0; i < groupChoices.Count; i++)
            {
                if (IsChoiceClaimed(groupIndex, i) || groupChoices[i] == null)
                {
                    continue;
                }

                remainingChoices.Add(groupChoices[i]);
                sourceIndices.Add(i);
            }

            if (remainingChoices.Count == 0)
            {
                return;
            }

            bool opened = battle.OpenRewardDishPack(
                remainingChoices,
                displayIndex =>
                {
                    if (displayIndex < 0 || displayIndex >= sourceIndices.Count)
                    {
                        return false;
                    }

                    return ClaimDishChoice(groupIndex, sourceIndices[displayIndex], groupChoices);
                },
                ReopenReward);
            if (opened)
            {
                if (closeRewardFormOnOpen)
                {
                    Close();
                }
            }
        }

        private bool ClaimDishChoice(
            int groupIndex,
            int choiceIndex,
            IReadOnlyList<RewardChoice> groupChoices)
        {
            if (!RestoreRewardContextForCallback()
                || groupChoices == null
                || IsChoiceResolved(groupIndex)
                || choiceIndex < 0
                || choiceIndex >= groupChoices.Count
                || IsChoiceClaimed(groupIndex, choiceIndex))
            {
                return false;
            }

            RewardChoice choice = groupChoices[choiceIndex];
            bool applied;
            using (RunPersistence.SuppressSave())
            {
                applied = RewardGranter.ApplyDishChoice(_run, choice);
            }

            if (!applied)
            {
                return false;
            }

            MarkChoiceClaimed(groupIndex, choiceIndex);
            SaveCurrentOffer();
            RefreshBattlePersistentHud();
            if (!IsChoiceResolved(groupIndex))
            {
                OpenDishPack(
                    groupIndex,
                    GroupFor(groupIndex).Choices,
                    closeRewardFormOnOpen: false);
            }
            else
            {
                ReopenReward();
            }
            return true;
        }

        private void ReopenReward()
        {
            BattleForm.Active?.CloseRewardOperationPages();
            if (_genericMode)
            {
                GameApp.UI.OpenUIForm(
                    UIForms.Reward,
                    UIForms.GroupDialog,
                    RewardFormOpenArgs.GenericQueue(_confirmBattleRewardAfterGeneric, _allowResultPeek));
                return;
            }

            GameApp.UI.OpenUIForm(
                UIForms.Reward,
                UIForms.GroupDialog,
                _allowResultPeek ? RewardFormOpenArgs.BattleReward() : null);
        }

        private void SaveCurrentOffer()
        {
            if (_genericMode)
            {
                _run.SetPendingGenericRewardOffer(_genericRewardKey, _offer);
            }
            else
            {
                _run.SetPendingRewardOffer(_rewardKey, _offer);
            }

            // 奖励应用与 claimed index 更新都在 SuppressSave 区间内完成，到这里一次性持久化。
            RunPersistence.Save(_run);
        }

        private void MarkChoiceClaimed(int groupIndex, int index)
        {
            GroupFor(groupIndex).MarkClaimed(index);
            if (IsChoiceResolved(groupIndex))
            {
                _expandedChoicePackGroupIndex = NoExpandedChoicePackGroup;
            }
        }

        private static void RefreshBattlePersistentHud()
        {
            BattleForm.Active?.RefreshPersistentHud();
        }

        private RewardChoiceGroup GroupFor(int groupIndex)
        {
            if (_offer == null)
            {
                return new RewardChoiceGroup(string.Empty, null, 0);
            }

            return groupIndex < 0 ? _offer.SpecificGroup : _offer.GetFixedGroup(groupIndex);
        }

        private bool RestoreRewardContextForCallback()
        {
            if (_run == null)
            {
                _run = GameRunContext.Current;
            }

            if (_run == null)
            {
                _offer = null;
                return false;
            }

            if (_genericMode)
            {
                if (!_run.TryPeekPendingGenericReward(out string key, out string title, out RewardOffer offer))
                {
                    _offer = null;
                    return false;
                }

                if (!string.IsNullOrEmpty(_genericRewardKey) && !string.Equals(_genericRewardKey, key, StringComparison.Ordinal))
                {
                    _offer = null;
                    return false;
                }

                _genericRewardKey = key;
                _genericRewardTitle = string.IsNullOrWhiteSpace(title) ? "奖励" : title;
                _offer = offer;
                return _offer != null;
            }

            if (!string.IsNullOrEmpty(_rewardKey))
            {
                RewardOffer offer = _run.GetPendingRewardOffer(_rewardKey);
                if (offer != null)
                {
                    _offer = offer;
                }
            }

            return _offer != null;
        }

        private void RebuildRewardRows()
        {
            for (int i = 0; i < _spawnedRows.Count; i++)
            {
                if (_spawnedRows[i] != null)
                {
                    _spawnedRows[i].gameObject.SetActive(false);
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
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rewardListContent);
            if (_rewardScrollRect != null)
            {
                _rewardScrollRect.StopMovement();
                _rewardScrollRect.verticalNormalizedPosition = 1f;
            }

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
            RewardChoiceRowView row = CreateRewardRow();
            if (row == null)
            {
                return;
            }

            bool claimed = _offer.BaseGoldClaimed;
            row.Bind(
                $"金币 +{_offer.BaseGold}",
                claimed ? "固定金币已发放。" : "点击领取固定金币。",
                LoadGoldIcon(),
                false,
                !claimed,
                claimed,
                claimed ? null : ClaimBaseGold,
                stateOverride: claimed ? "已领取" : null);
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

            AddClaimedChoiceRows(groupName, choices, groupIndex);
            if (IsChoiceResolved(groupIndex))
            {
                return;
            }

            if (ShouldShowChoicePackRow(choices, groupIndex))
            {
                AddChoicePackRow(groupName, choices, groupIndex);
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
                    () => ClaimChoice(groupIndex, index, choices),
                    dish: DishForChoice(choice),
                    flavorIds: FlavorIdsForChoice(choice));
            }
        }

        private void AddClaimedChoiceRows(
            string groupName,
            IReadOnlyList<RewardChoice> choices,
            int groupIndex)
        {
            for (int i = 0; i < choices.Count; i++)
            {
                RewardChoice choice = choices[i];
                if (choice == null || !IsChoiceClaimed(groupIndex, i))
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
                    false,
                    true,
                    null,
                    stateOverride: "已领取",
                    dish: DishForChoice(choice),
                    flavorIds: FlavorIdsForChoice(choice));
            }
        }

        private void AddChoicePackRow(
            string groupName,
            IReadOnlyList<RewardChoice> choices,
            int groupIndex)
        {
            RewardChoiceRowView row = CreateRewardRow();
            if (row == null)
            {
                return;
            }

            RewardChoice firstChoice = FirstUnclaimedChoice(choices, groupIndex) ?? choices[0];
            string packName = ChoicePackName(choices);
            row.Bind(
                $"{groupName}：{packName}选择包",
                BuildChoicePackDescription(choices, groupIndex, packName),
                LoadChoiceIcon(firstChoice),
                false,
                true,
                false,
                () => OpenChoicePack(groupIndex, choices),
                dish: DishForChoice(firstChoice),
                flavorIds: FlavorIdsForChoice(firstChoice));
        }

        private void OpenChoicePack(int groupIndex, IReadOnlyList<RewardChoice> choices)
        {
            RewardChoiceGroup group = GroupFor(groupIndex);
            if (group.RequiredChoiceCount >= group.Choices.Count)
            {
                ClaimAllRemainingChoices(groupIndex, choices);
                return;
            }

            if (IsDishPack(choices))
            {
                OpenDishPack(groupIndex, choices);
                return;
            }

            if (IsFragmentPack(choices))
            {
                ClaimChoice(groupIndex, 0, choices);
                return;
            }

            if (IsItemPack(choices))
            {
                OpenItemChoicePopup(groupIndex, choices);
                return;
            }

            _expandedChoicePackGroupIndex = groupIndex;
            RefreshOffer();
        }

        private bool OpenItemChoicePopup(int groupIndex, IReadOnlyList<RewardChoice> choices, bool closeRewardFormOnOpen = true)
        {
            BattleForm battle = BattleForm.Active;
            if (battle == null || choices == null)
            {
                _expandedChoicePackGroupIndex = groupIndex;
                RefreshOffer();
                return false;
            }

            var remainingChoices = new List<RewardChoice>();
            var sourceIndices = new List<int>();
            for (int i = 0; i < choices.Count; i++)
            {
                if (IsChoiceClaimed(groupIndex, i))
                {
                    continue;
                }

                RewardChoice choice = choices[i];
                if (choice == null)
                {
                    continue;
                }

                remainingChoices.Add(choice);
                sourceIndices.Add(i);
            }

            if (remainingChoices.Count == 0)
            {
                return false;
            }

            cfg.ItemKind itemKind = IsActiveItemReward(remainingChoices[0].Kind)
                ? cfg.ItemKind.Active
                : cfg.ItemKind.Passive;
            string title = BuildItemChoicePopupTitle(choices, groupIndex);
            bool opened = battle.OpenRewardItemChoices(
                title,
                remainingChoices,
                itemKind,
                pickedIndex =>
                {
                    if (pickedIndex < 0 || pickedIndex >= sourceIndices.Count)
                    {
                        ReopenReward();
                        return;
                    }

                    ClaimItemChoiceFromPopup(groupIndex, sourceIndices[pickedIndex]);
                },
                ReopenReward);
            if (opened)
            {
                if (closeRewardFormOnOpen)
                {
                    Close();
                }

                return true;
            }

            _expandedChoicePackGroupIndex = groupIndex;
            RefreshOffer();
            return false;
        }

        private void ClaimItemChoiceFromPopup(int groupIndex, int index)
        {
            if (!RestoreRewardContextForCallback())
            {
                ReopenReward();
                return;
            }

            RewardChoiceGroup group = GroupFor(groupIndex);
            IReadOnlyList<RewardChoice> currentChoices = group.Choices;
            if (currentChoices == null || IsChoiceResolved(groupIndex)
                || index < 0 || index >= currentChoices.Count || IsChoiceClaimed(groupIndex, index))
            {
                ReopenReward();
                return;
            }

            RewardChoice choice = currentChoices[index];
            if (choice == null)
            {
                ReopenReward();
                return;
            }

            using (RunPersistence.SuppressSave())
            {
                RewardGranter.ApplyChoice(_run, choice);
            }

            MarkChoiceClaimed(groupIndex, index);
            SaveCurrentOffer();
            RefreshBattlePersistentHud();

            if (TryAutoComplete(closeForm: false))
            {
                return;
            }

            if (!IsChoiceResolved(groupIndex) && IsItemPack(currentChoices)
                && OpenItemChoicePopup(groupIndex, currentChoices, closeRewardFormOnOpen: false))
            {
                return;
            }

            ReopenReward();
        }

        private void ClaimAllRemainingChoices(int groupIndex, IReadOnlyList<RewardChoice> choices)
        {
            if (_offer == null || choices == null)
            {
                return;
            }

            for (int i = 0; i < choices.Count && !IsChoiceResolved(groupIndex); i++)
            {
                if (IsChoiceClaimed(groupIndex, i))
                {
                    continue;
                }

                RewardChoice choice = choices[i];
                if (choice == null)
                {
                    continue;
                }

                using (RunPersistence.SuppressSave())
                {
                    RewardGranter.ApplyChoice(_run, choice);
                }

                MarkChoiceClaimed(groupIndex, i);
            }

            SaveCurrentOffer();
            RefreshBattlePersistentHud();

            if (TryAutoComplete(closeForm: true))
            {
                return;
            }

            RefreshOffer();
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
                case cfg.RewardKind.ActiveItemStrengthen:
                case cfg.RewardKind.ActiveItemAdjust:
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
                case cfg.RewardKind.ActiveItemStrengthen:
                case cfg.RewardKind.ActiveItemAdjust:
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

        private static bool IsItemPack(IReadOnlyList<RewardChoice> choices)
        {
            if (choices == null || choices.Count == 0 || choices[0] == null)
            {
                return false;
            }

            cfg.RewardKind kind = choices[0].Kind;
            if (!IsActiveItemReward(kind) && kind != cfg.RewardKind.PassiveItemChoice)
            {
                return false;
            }

            for (int i = 1; i < choices.Count; i++)
            {
                if (choices[i]?.Kind != kind)
                {
                    return false;
                }
            }

            return true;
        }

        private bool ShouldShowChoicePackRow(IReadOnlyList<RewardChoice> choices, int groupIndex)
        {
            if (choices == null || choices.Count <= 1)
            {
                return false;
            }

            if (IsDishPack(choices) || IsFragmentPack(choices))
            {
                return true;
            }

            return _expandedChoicePackGroupIndex != groupIndex;
        }

        private string BuildItemChoicePopupTitle(IReadOnlyList<RewardChoice> choices, int groupIndex)
        {
            RewardChoiceGroup group = GroupFor(groupIndex);
            string itemName = ChoicePackName(choices);
            int currentPick = group.ClaimedIndices.Count + 1;
            int required = group.RequiredChoiceCount;
            return required <= 1
                ? $"选择一个{itemName}"
                : $"选择{itemName}（{currentPick}/{required}）";
        }

        private string BuildChoicePackDescription(IReadOnlyList<RewardChoice> choices, int groupIndex, string packName)
        {
            RewardChoiceGroup group = GroupFor(groupIndex);
            int required = group.RequiredChoiceCount;
            int claimed = group.ClaimedIndices.Count;
            if (required >= choices.Count)
            {
                return $"点击后获得这 {choices.Count} 个{packName}。";
            }

            if (IsDishPack(choices))
            {
                return $"点击后从 {choices.Count} 个菜品中选择 {required} 个放入菜谱。（已选 {claimed}/{required}）";
            }

            if (IsFragmentPack(choices))
            {
                return $"点击后从 {choices.Count} 个餐桌碎片中选择 {required} 个拼贴。（已选 {claimed}/{required}）";
            }

            return $"点击后从 {choices.Count} 个{packName}中选择 {required} 个。（已选 {claimed}/{required}）";
        }

        private RewardChoice FirstUnclaimedChoice(IReadOnlyList<RewardChoice> choices, int groupIndex)
        {
            if (choices == null)
            {
                return null;
            }

            for (int i = 0; i < choices.Count; i++)
            {
                if (!IsChoiceClaimed(groupIndex, i))
                {
                    return choices[i];
                }
            }

            return null;
        }

        private static string ChoicePackName(IReadOnlyList<RewardChoice> choices)
        {
            if (choices == null || choices.Count == 0)
            {
                return "奖励";
            }

            if (choices[0] == null)
            {
                return "奖励";
            }

            switch (choices[0].Kind)
            {
                case cfg.RewardKind.Gold:
                    return "金币";
                case cfg.RewardKind.DishChoice:
                    return "菜品";
                case cfg.RewardKind.FragmentChoice:
                    return "碎片";
                case cfg.RewardKind.ActiveItemGrant:
                case cfg.RewardKind.ActiveItemStrengthen:
                case cfg.RewardKind.ActiveItemAdjust:
                    return "主动道具";
                case cfg.RewardKind.PassiveItemChoice:
                    return "被动道具";
                default:
                    return "奖励";
            }
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
                case cfg.RewardKind.ActiveItemStrengthen:
                case cfg.RewardKind.ActiveItemAdjust:
                {
                    cfg.ItemKind kind = IsActiveItemReward(choice.Kind) ? cfg.ItemKind.Active : cfg.ItemKind.Passive;
                    ItemDefinition item = ItemDefinition.Get(_run?.Tables ?? GameApp.Config.Tables, choice.Id, kind);
                    Sprite icon = RunItemSlotView.LoadIcon(item);
                    if (icon != null)
                    {
                        return icon;
                    }

                    return Resources.Load<Sprite>(IsActiveItemReward(choice.Kind)
                        ? "Sprites/UI/card_action_food_active"
                        : "Sprites/UI/card_action_food_passive");
                }
                default:
                    return null;
            }
        }

        private DishDef DishForChoice(RewardChoice choice)
        {
            return choice != null && choice.Kind == cfg.RewardKind.DishChoice
                ? _run?.Database.GetDish(choice.Id)
                : null;
        }

        private static IReadOnlyList<string> FlavorIdsForChoice(RewardChoice choice)
        {
            if (choice == null || choice.Kind != cfg.RewardKind.DishChoice || string.IsNullOrEmpty(choice.FlavorId))
            {
                return null;
            }

            return new[] { choice.FlavorId };
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

        private static bool IsActiveItemReward(cfg.RewardKind kind)
        {
            return kind == cfg.RewardKind.ActiveItemGrant ||
                   kind == cfg.RewardKind.ActiveItemStrengthen ||
                   kind == cfg.RewardKind.ActiveItemAdjust;
        }

        private static void SetButtonLabel(Button button, string text)
        {
            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = text;
            }
        }

        private readonly struct PeekChildState
        {
            public PeekChildState(Transform transform, bool activeSelf)
            {
                Transform = transform;
                ActiveSelf = activeSelf;
            }

            public Transform Transform { get; }

            public bool ActiveSelf { get; }
        }
    }
}
