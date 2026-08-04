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
using GourmetProject.Game.UI.Tooltips;
using TMPro;

namespace GourmetProject.Game.UI.Meta
{
    public sealed class RewardFormOpenArgs
    {
        private RewardFormOpenArgs(
            bool useGenericQueue,
            PendingGenericRewardContinuationKind genericRewardContinuation,
            bool allowResultPeek)
        {
            UseGenericQueue = useGenericQueue;
            GenericRewardContinuation = genericRewardContinuation;
            AllowResultPeek = allowResultPeek;
        }

        public bool UseGenericQueue { get; }

        public PendingGenericRewardContinuationKind GenericRewardContinuation { get; }

        public bool ConfirmBattleRewardAfterDone =>
            GenericRewardContinuation == PendingGenericRewardContinuationKind.Battle;

        public bool AllowResultPeek { get; }

        public static RewardFormOpenArgs BattleReward(bool allowResultPeek = true)
        {
            return new RewardFormOpenArgs(
                false,
                PendingGenericRewardContinuationKind.None,
                allowResultPeek);
        }

        public static RewardFormOpenArgs GenericQueue(bool confirmBattleRewardAfterDone = false, bool allowResultPeek = false)
        {
            return new RewardFormOpenArgs(
                true,
                confirmBattleRewardAfterDone
                    ? PendingGenericRewardContinuationKind.Battle
                    : PendingGenericRewardContinuationKind.None,
                allowResultPeek);
        }

        public static RewardFormOpenArgs GenericQueue(
            PendingGenericRewardContinuationKind continuation,
            bool allowResultPeek = false)
        {
            return new RewardFormOpenArgs(true, continuation, allowResultPeek);
        }
    }

    /// <summary>
    /// 过关领奖界面：展示本周得分与发放的奖励，点「继续」推进到下一周（或通关返回菜单）。
    /// 结构全固定、落在 RewardForm.prefab，脚本只赋文本并按是否最终周切换按钮组。
    /// 奖励经 RewardGranter 按周命名流发放，并在推进时存档以支持「继续游戏」。
    /// </summary>
    public sealed class RewardForm : UGuiForm
    {
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private Button _continueButton;
        [SerializeField] private Button _peekHideButton;
        [SerializeField] private Button _peekReturnButton;
        [SerializeField] private RectTransform _rewardListContent;
        [SerializeField] private RewardChoiceRowView _rewardRowTemplate;
        [Header("Reward Tips")]
        [SerializeField] private FoodTipsView _foodTipsPrefab;
        [SerializeField] private ItemTipView _itemTipPrefab;
        [Header("Reward Scrollbar")]
        [SerializeField] private ScrollRect _rewardScrollRect;
        [SerializeField] private Scrollbar _rewardScrollbar;
        [Min(0f)]
        [SerializeField] private float _rewardScrollbarIdleSeconds = 0.8f;
        [Min(0f)]
        [SerializeField] private float _rewardScrollbarFadeSeconds = 0.2f;
        [Header("Form Transition")]
        [SerializeField] private CanvasGroup _transitionGroup;
        [SerializeField] private RectTransform _transitionPanel;

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
        private PendingGenericRewardContinuationKind _genericRewardContinuation;
        private bool _allowResultPeek;
        private bool _peekHidden;
        private int _expandedChoicePackGroupIndex = NoExpandedChoicePackGroup;
        private readonly List<PeekChildState> _peekChildStates = new List<PeekChildState>();
        private FoodTipsView _foodTipsView;
        private ItemTipView _itemTipView;
        private Sequence _transitionSequence;
        private bool _isClosing;

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
            EnsureTipViews();
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            PrepareOpenTransition();
            ConfigureRewardScrollbar();
            EnsureTipViews();
            HideTips();
            HideRewardScrollbar(immediate: true);

            _run = GameRunContext.Current;
            if (_run == null)
            {
                Close();
                return;
            }

            RewardFormOpenArgs args = userData as RewardFormOpenArgs;
            _genericMode = args != null && args.UseGenericQueue;
            _genericRewardContinuation = args != null
                ? args.GenericRewardContinuation
                : _run.PendingGenericRewardContinuation;
            _confirmBattleRewardAfterGeneric =
                _genericRewardContinuation == PendingGenericRewardContinuationKind.Battle;
            if (_genericMode)
            {
                _run.PendingGenericRewardContinuation = _genericRewardContinuation;
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
                PlayOpenTransition();
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
            PlayOpenTransition();
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

            HideTips();
            _transitionSequence?.Kill();
            _transitionSequence = null;
            _isClosing = false;
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
            EnsureTipViews();
            HideTips();
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

                PendingGenericRewardContinuationKind continuation = _genericRewardContinuation;
                _run.PendingGenericRewardContinuation = PendingGenericRewardContinuationKind.None;
                if (_confirmBattleRewardAfterGeneric)
                {
                    _run.ClearPendingRewardBattleView();
                }
                RunPersistence.Save(_run);

                Action continueFlow = () =>
                {
                    if (continuation == PendingGenericRewardContinuationKind.Battle)
                    {
                        BattleForm.Active?.OnRewardConfirmed();
                    }
                    else if (continuation == PendingGenericRewardContinuationKind.Slot)
                    {
                        BattleForm.Active?.OnSlotRewardConfirmed();
                    }
                    else if (!closeForm)
                    {
                        BattleForm.Active?.OpenShop();
                    }
                };

                if (closeForm)
                {
                    Close(continueFlow);
                }
                else
                {
                    continueFlow.Invoke();
                }

                return;
            }

            _run.ClearPendingRewardOffer();
            if (_run.HasPendingGenericRewards)
            {
                _genericMode = true;
                _confirmBattleRewardAfterGeneric = true;
                _genericRewardContinuation = PendingGenericRewardContinuationKind.Battle;
                _run.PendingGenericRewardContinuation = PendingGenericRewardContinuationKind.Battle;
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
            Action confirmBattle = () => BattleForm.Active?.OnRewardConfirmed();
            if (closeForm)
            {
                Close(confirmBattle);
            }
            else
            {
                confirmBattle.Invoke();
            }
        }

        private void Close()
        {
            Close(null);
        }

        private void Close(Action onClosed)
        {
            if (_isClosing)
            {
                return;
            }

            _isClosing = true;
            _continueButton.interactable = false;
            if (_transitionGroup != null)
            {
                _transitionGroup.blocksRaycasts = false;
            }

            _transitionSequence?.Kill();
            _transitionSequence = DOTween.Sequence().SetUpdate(true).SetTarget(this);
            if (_transitionGroup != null)
            {
                _transitionSequence.Append(DOTween.To(
                    () => _transitionGroup.alpha,
                    value => _transitionGroup.alpha = value,
                    0f,
                    0.16f).SetEase(Ease.InQuad));
            }

            if (_transitionPanel != null)
            {
                _transitionSequence.Join(_transitionPanel.DOScale(0.96f, 0.16f).SetEase(Ease.InQuad));
            }

            _transitionSequence.OnComplete(() =>
            {
                GameApp.UI.CloseUIForm(UIForm);
                onClosed?.Invoke();
            });
        }

        private void PrepareOpenTransition()
        {
            _transitionSequence?.Kill();
            _isClosing = false;
            _continueButton.interactable = true;
            if (_transitionGroup != null)
            {
                _transitionGroup.alpha = 0f;
                _transitionGroup.blocksRaycasts = false;
            }

            if (_transitionPanel != null)
            {
                _transitionPanel.localScale = Vector3.one * 0.94f;
            }
        }

        private void PlayOpenTransition()
        {
            _transitionSequence?.Kill();
            _transitionSequence = DOTween.Sequence().SetUpdate(true).SetTarget(this);
            if (_transitionGroup != null)
            {
                _transitionSequence.Append(DOTween.To(
                    () => _transitionGroup.alpha,
                    value => _transitionGroup.alpha = value,
                    1f,
                    0.2f).SetEase(Ease.OutQuad));
            }

            if (_transitionPanel != null)
            {
                _transitionSequence.Join(_transitionPanel.DOScale(1f, 0.24f).SetEase(Ease.OutCubic));
            }

            _transitionSequence.OnComplete(() =>
            {
                if (_transitionGroup != null)
                {
                    _transitionGroup.blocksRaycasts = true;
                }
            });
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

            CacheCurrentOffer();
            RefreshBattlePersistentHud();
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
                bool applied;
                using (RunPersistence.SuppressSave())
                {
                    applied = RewardGranter.ApplyDishChoice(_run, choice);
                }

                if (applied)
                {
                    HideTips();
                    MarkChoiceClaimed(groupIndex, index);
                    CacheCurrentOffer();
                    RefreshBattlePersistentHud();
                    RefreshOffer();
                }
                return;
            }

            if (choice.Kind == cfg.RewardKind.FragmentChoice)
            {
                using (RunPersistence.SuppressSave())
                {
                    RewardGranter.ApplyFragmentPack(_run, groupChoices);
                }

                CacheCurrentOffer();

                Close();
                BattleForm.Active?.OpenRewardTableEdit(placed =>
                {
                    if (placed && !IsChoiceResolved(groupIndex))
                    {
                        MarkChoiceClaimed(groupIndex, index);
                        CacheCurrentOffer();
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
            CacheCurrentOffer();
            RefreshBattlePersistentHud();
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
                GroupFor(groupIndex),
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
            CacheCurrentOffer();
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
                    RewardFormOpenArgs.GenericQueue(_genericRewardContinuation, _allowResultPeek));
                return;
            }

            GameApp.UI.OpenUIForm(
                UIForms.Reward,
                UIForms.GroupDialog,
                _allowResultPeek ? RewardFormOpenArgs.BattleReward() : null);
        }

        private void CacheCurrentOffer()
        {
            if (_genericMode)
            {
                _run.SetPendingGenericRewardOffer(_genericRewardKey, _offer);
            }
            else
            {
                _run.SetPendingRewardOffer(_rewardKey, _offer);
            }

            // 奖励效果与“已领取”标记在同一份存档中提交，避免在二者之间读档后重复发放。
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
                AddChoiceRows(group, groupIndex: i);
            }

            AddChoiceRows(_offer.SpecificGroup, groupIndex: -1);
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
                LoadBaseGoldIcon(),
                false,
                true,
                false,
                ClaimBaseGold);
        }

        private void AddChoiceRows(RewardChoiceGroup group, int groupIndex)
        {
            IReadOnlyList<RewardChoice> choices = group?.Choices;
            if (choices == null || choices.Count == 0)
            {
                return;
            }

            if (IsChoiceResolved(groupIndex))
            {
                return;
            }

            if (ShouldShowChoicePackRow(group, groupIndex))
            {
                AddChoicePackRow(group, groupIndex);
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

                Sprite icon = LoadGroupRowIcon(group, groupIndex, choice, out bool suppressDishPreview);

                row.Bind(
                    string.IsNullOrWhiteSpace(choice?.Name) ? FallbackGroupTitle(group) : choice.Name,
                    BuildDirectChoiceDescription(choice, group),
                    icon,
                    false,
                    true,
                    false,
                    () => ClaimChoice(groupIndex, index, choices),
                    dish: suppressDishPreview ? null : DishForChoice(choice),
                    flavorIds: suppressDishPreview ? null : FlavorIdsForChoice(choice));
                BindDirectChoiceTip(row, choice);
            }
        }

        private void AddChoicePackRow(RewardChoiceGroup group, int groupIndex)
        {
            IReadOnlyList<RewardChoice> choices = group.Choices;
            RewardChoiceRowView row = CreateRewardRow();
            if (row == null)
            {
                return;
            }

            RewardChoice firstChoice = FirstUnclaimedChoice(choices, groupIndex) ?? choices[0];
            Sprite icon = LoadGroupRowIcon(group, groupIndex, firstChoice, out bool suppressDishPreview);
            row.Bind(
                FallbackGroupTitle(group),
                BuildGroupDescription(group),
                icon,
                false,
                true,
                false,
                () => OpenChoicePack(groupIndex, choices),
                dish: suppressDishPreview ? null : DishForChoice(firstChoice),
                flavorIds: suppressDishPreview ? null : FlavorIdsForChoice(firstChoice));
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
            bool opened = battle.OpenRewardItemChoices(
                GroupFor(groupIndex),
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
            CacheCurrentOffer();
            RefreshBattlePersistentHud();

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

            CacheCurrentOffer();
            RefreshBattlePersistentHud();
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

        private string BuildDirectChoiceDescription(RewardChoice choice, RewardChoiceGroup group)
        {
            if (choice == null)
            {
                return group?.Description ?? string.Empty;
            }

            string description = BuildChoiceDescription(choice);
            if (choice.Kind == cfg.RewardKind.DishChoice && string.IsNullOrWhiteSpace(choice.Description))
            {
                description = !string.IsNullOrWhiteSpace(group?.Description)
                    ? group.Description
                    : description;
            }

            if (IsActiveItemReward(choice.Kind) && _run != null && !_run.HasFreeActiveSlot)
            {
                description = string.IsNullOrWhiteSpace(description)
                    ? "主动道具槽已满，领取后会折算金币。"
                    : $"{description}\n主动道具槽已满，领取后会折算金币。";
            }

            return description;
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

        private bool ShouldShowChoicePackRow(RewardChoiceGroup group, int groupIndex)
        {
            IReadOnlyList<RewardChoice> choices = group?.Choices;
            if (choices == null || choices.Count == 0)
            {
                return false;
            }

            bool direct = choices.Count == 1
                && group.RequiredChoiceCount == 1
                && choices[0]?.Kind != cfg.RewardKind.FragmentChoice;
            if (direct)
            {
                return false;
            }

            if (IsDishPack(choices) || IsFragmentPack(choices))
            {
                return true;
            }

            return _expandedChoicePackGroupIndex != groupIndex;
        }

        private static string FallbackGroupTitle(RewardChoiceGroup group)
        {
            return !string.IsNullOrWhiteSpace(group?.Title)
                ? group.Title
                : ChoicePackName(group?.Choices);
        }

        private static string BuildGroupDescription(RewardChoiceGroup group)
        {
            if (group == null)
            {
                return string.Empty;
            }

            string rule = !string.IsNullOrWhiteSpace(group.RuleText)
                ? group.RuleText
                : BuildLegacyRuleText(group);
            return string.IsNullOrWhiteSpace(group.Description)
                ? rule
                : string.IsNullOrWhiteSpace(rule) ? group.Description : $"{group.Description}\n{rule}";
        }

        private static string BuildLegacyRuleText(RewardChoiceGroup group)
        {
            int count = group?.Choices?.Count ?? 0;
            int required = group?.RequiredChoiceCount ?? 0;
            string kind = ChoicePackName(group?.Choices);
            if (count == 1 && required == 1)
            {
                return $"随机获得 1 个{kind}。";
            }

            return required >= count
                ? $"获得全部 {count} 个{kind}。"
                : $"从 {count} 个{kind}中选择 {required} 个。";
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

        private Sprite LoadGroupRowIcon(
            RewardChoiceGroup group,
            int groupIndex,
            RewardChoice fallbackChoice,
            out bool suppressDishPreview)
        {
            suppressDishPreview = false;
            cfg.Food sourceFood = ResolveFoodRewardSource();
            if (sourceFood == null)
            {
                return LoadChoiceIcon(fallbackChoice);
            }

            string spriteName = string.Empty;
            if (groupIndex < 0)
            {
                spriteName = RewardBadgeResolver.SpriteNameFor(
                    sourceFood.ActionKind,
                    sourceFood.RewardKind);
            }
            else if (IsBaseDishGroup(group, groupIndex))
            {
                spriteName = RewardBadgeResolver.BaseDishSpriteName;
            }

            Sprite icon = string.IsNullOrEmpty(spriteName)
                ? null
                : Resources.Load<Sprite>($"Sprites/UI/{spriteName}");
            if (icon == null)
            {
                return LoadChoiceIcon(fallbackChoice);
            }

            suppressDishPreview = true;
            return icon;
        }

        private cfg.Food ResolveFoodRewardSource()
        {
            if (_genericMode || _run == null)
            {
                return null;
            }

            ActionExecutionContext context = BattleForm.Active?.CurrentBattleActionContext;
            if (context == null || context.Action == null)
            {
                context = _run.LastActionContext;
            }

            cfg.GameAction action = context?.Action;
            if (action == null || action.Behavior != cfg.ActionBehavior.Food)
            {
                return null;
            }

            cfg.Food food = FoodService.Resolve(_run.Tables, action);
            return food != null
                && (food.ActionKind == cfg.FoodActionKind.Normal
                    || food.ActionKind == cfg.FoodActionKind.Super)
                ? food
                : null;
        }

        private bool IsBaseDishGroup(RewardChoiceGroup group, int groupIndex)
        {
            if (group == null || groupIndex < 0 || !IsDishPack(group.Choices))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(group.SourceSlotId))
            {
                foreach (cfg.RewardSlot slot in _run.Tables.TbRewardSlot.DataList)
                {
                    if (string.Equals(slot.Id, group.SourceSlotId, StringComparison.Ordinal))
                    {
                        return string.Equals(slot.GroupId, "base_dish", StringComparison.Ordinal);
                    }
                }

                return false;
            }

            // 旧档没有 SourceSlotId；普通/困难美食奖励的第一个固定菜品组就是基础菜品。
            return groupIndex == 0;
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

        private static Sprite LoadBaseGoldIcon()
        {
            return Resources.Load<Sprite>("Sprites/UI/reward_badge_gold")
                ?? LoadGoldIcon();
        }

        private static bool IsActiveItemReward(cfg.RewardKind kind)
        {
            return kind == cfg.RewardKind.ActiveItemGrant ||
                   kind == cfg.RewardKind.ActiveItemStrengthen ||
                   kind == cfg.RewardKind.ActiveItemAdjust;
        }

        private void EnsureTipViews()
        {
            if (_foodTipsView == null && _foodTipsPrefab != null)
            {
                _foodTipsView = Instantiate(_foodTipsPrefab, TipLayerParent(), false);
                _foodTipsView.gameObject.name = "RewardFoodTipsView_Runtime";
            }

            if (_itemTipView == null && _itemTipPrefab != null)
            {
                _itemTipView = Instantiate(_itemTipPrefab, TipLayerParent(), false);
                _itemTipView.gameObject.name = "RewardItemTipView_Runtime";
            }

            HideTips();
            MoveTipToTop(_foodTipsView);
            MoveTipToTop(_itemTipView);
        }

        private Transform TipLayerParent()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            return canvas != null ? canvas.transform : transform;
        }

        private void MoveTipToTop(MonoBehaviour tip)
        {
            if (tip == null)
            {
                return;
            }

            Transform parent = TipLayerParent();
            if (tip.transform.parent != parent)
            {
                tip.transform.SetParent(parent, false);
            }

            tip.transform.SetAsLastSibling();
        }

        private void HideTips()
        {
            _foodTipsView?.Hide();
            _itemTipView?.Hide();
        }

        private void BindDirectChoiceTip(RewardChoiceRowView row, RewardChoice choice)
        {
            if (row == null || choice == null)
            {
                return;
            }

            TipHoverTrigger trigger = row.EnsureTipTrigger();
            trigger.SetTarget(row.TipPlacementTarget);
            trigger.SetFollowPointer(true);

            if (choice.Kind == cfg.RewardKind.DishChoice && _foodTipsView != null)
            {
                FoodTipsData data = RewardDishPackPanel.BuildDishTipsData(_run, choice);
                if (data != null)
                {
                    trigger.SetTip(
                        _foodTipsView,
                        _foodTipsView.Show,
                        _foodTipsView.Hide,
                        () =>
                        {
                            _foodTipsView.Bind(data);
                            MoveTipToTop(_foodTipsView);
                        });
                    return;
                }
            }

            if ((choice.Kind == cfg.RewardKind.PassiveItemChoice || IsActiveItemReward(choice.Kind))
                && _itemTipView != null)
            {
                cfg.ItemKind kind = IsActiveItemReward(choice.Kind) ? cfg.ItemKind.Active : cfg.ItemKind.Passive;
                ItemDefinition item = ItemDefinition.Get(_run?.Tables ?? GameApp.Config.Tables, choice.Id, kind);
                if (item != null)
                {
                    trigger.SetTip(_itemTipView, () =>
                    {
                        _itemTipView.Bind(item);
                        MoveTipToTop(_itemTipView);
                    });
                    return;
                }
            }

            trigger.ClearTip();
        }

        private static void SetButtonLabel(Button button, string text)
        {
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
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
