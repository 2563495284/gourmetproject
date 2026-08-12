using System;
using BreakInfinity;
using System.Collections.Generic;
using DG.Tweening;
using GourmetProject.Core.Rng;
using GourmetProject.Game;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Analytics;
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
using GourmetProject.Game.Tutorial;

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
        private enum PersistentInspectionOrigin
        {
            None,
            VisibleReward,
            ResultPeek,
        }

        internal static RewardForm Active { get; private set; }

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
        [SerializeField] private RewardFormTransitionSettings _transitionSettings = new RewardFormTransitionSettings();

        private const int NoExpandedChoicePackGroup = int.MinValue;

        private GameRun _run;
        private RewardOffer _offer;
        private readonly List<RewardChoiceRowView> _spawnedRows = new List<RewardChoiceRowView>();
        private string _rewardKey;
        private string _genericRewardKey;
        private string _genericRewardTitle;
        private BigDouble _lastTotal;
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
        private bool _peekInspectionActive;
        private int _expandedChoicePackGroupIndex = NoExpandedChoicePackGroup;
        private readonly List<PeekChildState> _peekChildStates = new List<PeekChildState>();
        private FoodTipsView _foodTipsView;
        private ItemTipView _itemTipView;
        private Sequence _transitionSequence;
        private Sequence _rewardRowsSequence;
        private CanvasGroup _rewardListGroup;
        private Vector2 _transitionPanelRestingPosition;
        private bool _hasBuiltRewardRows;
        private bool _isClosing;
        private bool _isSuspendedForRewardSubflow;
        private bool _completingFromRewardSubflow;
        private float _suspendedScrollPosition = 1f;
        private PersistentInspectionOrigin _persistentInspectionOrigin;
        private float _persistentInspectionScrollPosition = 1f;
        private ArchetypeVector _offerArchetype;
        private float _offerOpenedRealtime;

        private RewardFormTransitionSettings TransitionSettings =>
            _transitionSettings ?? (_transitionSettings = new RewardFormTransitionSettings());

        internal bool IsSuspendedForRewardSubflow => _isSuspendedForRewardSubflow;

        internal bool IsPersistentInspectionActive =>
            _persistentInspectionOrigin != PersistentInspectionOrigin.None;

        internal bool AllowsPersistentInteractions =>
            !_isClosing && !_isSuspendedForRewardSubflow;

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

            if (_transitionPanel != null)
            {
                _transitionPanelRestingPosition = _transitionPanel.anchoredPosition;
            }

            ConfigureRewardScrollbar();
            EnsureRewardListGroup();
            EnsureTipViews();
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            Active = this;
            PrepareOpenTransition();
            ConfigureRewardScrollbar();
            EnsureRewardListGroup();
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
            _peekInspectionActive = false;
            _isSuspendedForRewardSubflow = false;
            _completingFromRewardSubflow = false;
            _persistentInspectionOrigin = PersistentInspectionOrigin.None;
            _persistentInspectionScrollPosition = 1f;
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
                ReportOfferShown();
                RefreshOffer();
                RefreshBattlePersistentHud(refreshItems: false);
                if (!_isClosing)
                {
                    PlayOpenTransition();
                }
                return;
            }

            BattleSession session = BattleForm.Active?.Session;
            BigDouble total = session != null && session.IsSettled ? session.LastResult.Total : BigDouble.Zero;
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

            ReportOfferShown();
            RefreshOffer();
            RefreshBattlePersistentHud(refreshItems: false);
            if (!_isClosing)
            {
                PlayOpenTransition();
                TutorialAnchorRegistry.Register(TutorialAnchorId.RewardList, _rewardListContent);
                TutorialAnchorRegistry.Register(TutorialAnchorId.RewardContinue, _continueButton.transform as RectTransform);
                if (_run.IsTutorialRun
                    && actionContext?.RunStepIndex == 0
                    && !TutorialProgressService.IsCompleted(TutorialId.CoreComplete))
                    TutorialRuntime.Play(TutorialId.RewardSummary);
            }
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.RewardList, _rewardListContent);
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.RewardContinue, _continueButton != null ? _continueButton.transform as RectTransform : null);
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
            _rewardRowsSequence?.Kill();
            _rewardRowsSequence = null;
            _hasBuiltRewardRows = false;
            if (_rewardListGroup != null)
            {
                _rewardListGroup.alpha = 1f;
                _rewardListGroup.interactable = true;
                _rewardListGroup.blocksRaycasts = true;
            }

            if (_transitionPanel != null)
            {
                _transitionPanel.anchoredPosition = _transitionPanelRestingPosition;
                _transitionPanel.localScale = Vector3.one;
            }

            _isClosing = false;
            _isSuspendedForRewardSubflow = false;
            _completingFromRewardSubflow = false;
            _peekInspectionActive = false;
            _persistentInspectionOrigin = PersistentInspectionOrigin.None;
            _persistentInspectionScrollPosition = 1f;
            if (_transitionGroup != null)
            {
                // UIForm 会复用同一个实例。最后一项奖励从子流程直接关闭时，挂起阶段留下的
                // interactable=false 不能泄漏到下一次打开。
                _transitionGroup.interactable = true;
                _transitionGroup.blocksRaycasts = true;
            }
            if (ReferenceEquals(Active, this))
            {
                Active = null;
            }
            if (!isShutdown)
            {
                RefreshBattlePersistentHud(refreshItems: false);
            }
            base.OnClose(isShutdown, userData);
        }

        private void Update()
        {
            if (_isSuspendedForRewardSubflow
                || !_rewardScrollbarVisible
                || _rewardScrollbarGroup == null)
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

            // 存档恢复或异步回调仍可能让已完成的 offer 进入刷新流程；此时不渲染空列表，
            // 直接复用“继续行动”的完整收尾流程。
            if (_offer?.IsFullyClaimed == true)
            {
                CompleteRewards(closeForm: true);
                return;
            }

            _titleText.text = "奖励";
            // _titleText.text = _genericMode && !string.IsNullOrEmpty(_genericRewardTitle)
            //     ? _genericRewardTitle
            //     : "奖励";
            _continueButton.gameObject.SetActive(true);

            SetButtonLabel(_continueButton, "放弃");

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
            if (_offer != null && !_offer.IsFullyClaimed)
            {
                new ItemRuntime(_run).NotifyRewardAbandoned();
            }

            CompleteRewards(closeForm: true);
        }

        /// <summary>结算并推进：清空 pending offer/碎片包、存档、（可选）关界面并回到时间轴，等效于点「继续」。</summary>
        private void CompleteRewards(bool closeForm)
        {
            ReportOfferResolved();
            BattleForm.Active?.CloseRewardOperationPages();
            _run.ClearPendingFragmentPack();

            if (_genericMode)
            {
                _run.ClearPendingGenericRewardOffer(_genericRewardKey);
                if (LoadNextGenericReward())
                {
                    RunPersistence.Save(_run);
                    ReportOfferShown();
                    RefreshOffer();
                    return;
                }

                PendingGenericRewardContinuationKind continuation = _genericRewardContinuation;
                // 事件必须在 RewardForm 完整关闭后才结束；保留续接标记到事件回调落盘，
                // 这样在“奖励队列已清空、事件尚未完成”的中断窗口也能正确恢复。
                if (continuation != PendingGenericRewardContinuationKind.Event)
                {
                    _run.PendingGenericRewardContinuation = PendingGenericRewardContinuationKind.None;
                }
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
                    else if (continuation == PendingGenericRewardContinuationKind.Event)
                    {
                        BattleForm.Active?.OnEventRewardConfirmed();
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
                    ReportOfferShown();
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
            _rewardRowsSequence?.Kill();
            _rewardRowsSequence = null;
            if (_transitionGroup != null)
            {
                _transitionGroup.blocksRaycasts = false;
            }

            _transitionSequence?.Kill();
            _transitionSequence = DOTween.Sequence().SetUpdate(true).SetTarget(this);
            RewardFormTransitionSettings settings = TransitionSettings;
            if (_transitionGroup != null)
            {
                _transitionSequence.Append(DOTween.To(
                    () => _transitionGroup.alpha,
                    value => _transitionGroup.alpha = value,
                    0f,
                    settings.CloseDuration).SetEase(Ease.InSine));
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
            _rewardRowsSequence?.Kill();
            _rewardRowsSequence = null;
            _hasBuiltRewardRows = false;
            _isClosing = false;
            _continueButton.interactable = true;
            if (_transitionGroup != null)
            {
                _transitionGroup.alpha = 0f;
                _transitionGroup.interactable = true;
                _transitionGroup.blocksRaycasts = false;
            }

            if (_transitionPanel != null)
            {
                _transitionPanel.localScale = Vector3.one;
                _transitionPanel.anchoredPosition = _transitionPanelRestingPosition;
            }

            if (_rewardListGroup != null)
            {
                _rewardListGroup.alpha = 1f;
                _rewardListGroup.interactable = true;
                _rewardListGroup.blocksRaycasts = true;
            }
        }

        private void PlayOpenTransition()
        {
            _transitionSequence?.Kill();
            _transitionSequence = DOTween.Sequence().SetUpdate(true).SetTarget(this);
            RewardFormTransitionSettings settings = TransitionSettings;
            if (_transitionGroup != null)
            {
                _transitionSequence.Append(DOTween.To(
                    () => _transitionGroup.alpha,
                    value => _transitionGroup.alpha = value,
                    1f,
                    settings.BackgroundFade).SetEase(Ease.OutSine));
            }

            _transitionSequence.OnComplete(() =>
            {
                if (_transitionGroup != null)
                {
                    _transitionGroup.interactable = true;
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
                _peekReturnButton.gameObject.SetActive(
                    _allowResultPeek && _peekHidden && !_peekInspectionActive);
            }
        }

        internal void SetResultPeekInspectionActive(bool active)
        {
            if (!_peekHidden || _peekInspectionActive == active)
            {
                return;
            }

            _peekInspectionActive = active;
            ConfigurePeekButtons();
        }

        /// <summary>
        /// 从左右常驻栏进入只读餐桌/菜谱前挂起奖励表现。正常奖励页会在查看结束后自动恢复；
        /// 手动“查看结算”只临时隐藏其返回按钮，查看结束后仍停留在结算画面。
        /// </summary>
        internal bool TryBeginPersistentInspection()
        {
            if (_isClosing
                || _isSuspendedForRewardSubflow
                || _persistentInspectionOrigin != PersistentInspectionOrigin.None)
            {
                return false;
            }

            if (_peekHidden)
            {
                _persistentInspectionOrigin = PersistentInspectionOrigin.ResultPeek;
                _peekInspectionActive = true;
                ConfigurePeekButtons();
                return true;
            }

            _persistentInspectionOrigin = PersistentInspectionOrigin.VisibleReward;
            _persistentInspectionScrollPosition = _rewardScrollRect != null
                ? _rewardScrollRect.verticalNormalizedPosition
                : 1f;
            _transitionSequence?.Kill();
            _transitionSequence = null;
            _rewardRowsSequence?.Kill();
            _rewardRowsSequence = null;
            HideTips();
            HideRewardScrollbar(immediate: true);
            if (_transitionGroup != null)
            {
                _transitionGroup.alpha = 0f;
                _transitionGroup.interactable = false;
                _transitionGroup.blocksRaycasts = false;
            }

            BattleForm.Active?.SetRewardPeekOnly(true);
            return true;
        }

        /// <summary>完成或回滚一次常驻栏查看请求；重复调用安全无副作用。</summary>
        internal void CompletePersistentInspection()
        {
            PersistentInspectionOrigin origin = _persistentInspectionOrigin;
            if (origin == PersistentInspectionOrigin.None)
            {
                return;
            }

            _persistentInspectionOrigin = PersistentInspectionOrigin.None;
            if (origin == PersistentInspectionOrigin.ResultPeek)
            {
                _peekInspectionActive = false;
                ConfigurePeekButtons();
                return;
            }

            if (_isClosing || _isSuspendedForRewardSubflow)
            {
                return;
            }

            if (_transitionGroup != null)
            {
                _transitionGroup.alpha = 1f;
                _transitionGroup.interactable = true;
                _transitionGroup.blocksRaycasts = true;
            }

            RestoreRewardScrollPosition(_persistentInspectionScrollPosition);
            BattleForm.Active?.SetRewardPeekOnly(false);
        }

        private void HideForResultPeek()
        {
            if (!_allowResultPeek
                || _peekHidden
                || _peekReturnButton == null
                || _persistentInspectionOrigin != PersistentInspectionOrigin.None)
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
            _peekInspectionActive = false;
            _persistentInspectionOrigin = PersistentInspectionOrigin.None;
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

        private void ClaimChoice(
            int groupIndex,
            int index,
            IReadOnlyList<RewardChoice> groupChoices,
            RewardChoiceRowView sourceRow)
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

            if (IsActiveItemReward(choice.Kind)
                && (_run == null || !_run.HasFreeActiveSlot))
            {
                sourceRow?.PlayTargetFailed();
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
                    BattleForm.Active?.PlayRewardDishSelectionFly(sourceRow);
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

                bool opened = BattleForm.Active?.OpenRewardTableEdit(placed =>
                {
                    if (placed && !IsChoiceResolved(groupIndex))
                    {
                        MarkChoiceClaimed(groupIndex, index);
                        CacheCurrentOffer();
                    }

                    ResumeFromRewardSubflow();
                }) == true;
                if (opened)
                {
                    SuspendForRewardSubflow();
                }
                return;
            }

            bool isItemReward = choice.Kind == cfg.RewardKind.PassiveItemChoice
                || IsActiveItemReward(choice.Kind);
            int itemCountBefore = isItemReward ? _run.GetItemCount(choice.Id) : 0;
            Func<bool> playItemFly = isItemReward
                ? BattleForm.Active?.PrepareRewardItemSelectionFly(
                    choice,
                    IsActiveItemReward(choice.Kind) ? cfg.ItemKind.Active : cfg.ItemKind.Passive,
                    sourceRow)
                : null;

            using (RunPersistence.SuppressSave())
            {
                RewardGranter.ApplyChoice(_run, choice);
            }

            bool itemFlyStarted = isItemReward
                && _run.GetItemCount(choice.Id) > itemCountBefore
                && playItemFly?.Invoke() == true;

            MarkChoiceClaimed(groupIndex, index);
            CacheCurrentOffer();
            RefreshBattlePersistentHud(refreshItems: !itemFlyStarted);
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
                ResumeFromRewardSubflow);
            if (opened)
            {
                if (closeRewardFormOnOpen)
                {
                    SuspendForRewardSubflow();
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

            bool applied;
            using (RunPersistence.SuppressSave())
            {
                applied = RewardGranter.ApplyDishChoice(_run, groupChoices[choiceIndex]);
            }

            if (!applied)
            {
                return false;
            }

            MarkChoiceClaimed(groupIndex, choiceIndex);
            CacheCurrentOffer();
            RefreshBattlePersistentHud();
            return true;
        }

        internal void SuspendForRewardSubflow()
        {
            if (_isSuspendedForRewardSubflow || _isClosing)
            {
                return;
            }

            _isSuspendedForRewardSubflow = true;
            _suspendedScrollPosition = _rewardScrollRect != null
                ? _rewardScrollRect.verticalNormalizedPosition
                : 1f;
            _transitionSequence?.Kill();
            _transitionSequence = null;
            _rewardRowsSequence?.Kill();
            _rewardRowsSequence = null;
            HideTips();
            HideRewardScrollbar(immediate: true);
            if (_transitionGroup != null)
            {
                _transitionGroup.alpha = 0f;
                _transitionGroup.interactable = false;
                _transitionGroup.blocksRaycasts = false;
            }
        }

        internal void ResumeFromRewardSubflow()
        {
            if (!_isSuspendedForRewardSubflow || _isClosing)
            {
                return;
            }

            _isSuspendedForRewardSubflow = false;
            if (!RestoreRewardContextForCallback())
            {
                CompleteFromRewardSubflow();
                return;
            }

            if (_offer.IsFullyClaimed)
            {
                CompleteFromRewardSubflow();
                return;
            }

            RefreshOfferPreservingScroll(_suspendedScrollPosition);
            RestoreAfterRewardSubflow();
        }

        internal void CompleteFromRewardSubflow()
        {
            if (_completingFromRewardSubflow || _isClosing)
            {
                return;
            }

            _isSuspendedForRewardSubflow = false;
            _completingFromRewardSubflow = true;
            CompleteRewards(closeForm: true);

            // 通用奖励队列可能在同一个 RewardForm 实例中继续下一份奖励。
            if (!_isClosing && _offer != null && !_offer.IsFullyClaimed)
            {
                _completingFromRewardSubflow = false;
                RestoreAfterRewardSubflow();
            }
        }

        private void RefreshOfferPreservingScroll(float scrollPosition)
        {
            RefreshOffer();
            if (_isClosing || _rewardScrollRect == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rewardListContent);
            RestoreRewardScrollPosition(scrollPosition);
        }

        private void RestoreRewardScrollPosition(float scrollPosition)
        {
            if (_rewardScrollRect == null)
            {
                return;
            }

            _rewardScrollRect.StopMovement();
            _rewardScrollRect.verticalNormalizedPosition = Mathf.Clamp01(scrollPosition);
        }

        /// <summary>
        /// 右栏在奖励态使用或丢弃消耗品后刷新奖励可领状态；重建行时保留当前或挂起前滚动位置。
        /// </summary>
        internal void RefreshAfterExternalInventoryMutation()
        {
            if (_isClosing || _offer == null)
            {
                return;
            }

            float scrollPosition = _persistentInspectionOrigin == PersistentInspectionOrigin.VisibleReward
                ? _persistentInspectionScrollPosition
                : _rewardScrollRect != null
                    ? _rewardScrollRect.verticalNormalizedPosition
                    : 1f;
            RefreshOfferPreservingScroll(scrollPosition);
            if (_persistentInspectionOrigin == PersistentInspectionOrigin.VisibleReward)
            {
                _persistentInspectionScrollPosition = scrollPosition;
                if (_transitionGroup != null)
                {
                    _transitionGroup.alpha = 0f;
                    _transitionGroup.interactable = false;
                    _transitionGroup.blocksRaycasts = false;
                }
            }
        }

        private void RestoreAfterRewardSubflow()
        {
            if (_transitionGroup != null)
            {
                _transitionGroup.alpha = 1f;
                _transitionGroup.interactable = true;
                _transitionGroup.blocksRaycasts = true;
            }

            _continueButton.interactable = true;
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

            // 领取阶段只更新运行时状态；点“完成/继续行动”后由 CompleteRewards 统一落盘。
            // 这样中途退出会整体回到领奖前，不会留下半完成的奖励存档。
        }

        private void MarkChoiceClaimed(int groupIndex, int index)
        {
            GroupFor(groupIndex).MarkClaimed(index);
            ReportChoiceSelected(groupIndex, index);
            if (IsChoiceResolved(groupIndex))
            {
                _expandedChoicePackGroupIndex = NoExpandedChoicePackGroup;
            }
        }

        private void ReportOfferShown()
        {
            if (_run == null || _offer == null)
            {
                return;
            }

            _offerArchetype = ArchetypeService.Capture(_run);
            _offerOpenedRealtime = Time.realtimeSinceStartup;
            for (int groupIndex = 0; groupIndex < _offer.FixedGroups.Count; groupIndex++)
            {
                ReportGroupShown(_offer.FixedGroups[groupIndex], groupIndex);
            }

            ReportGroupShown(_offer.SpecificGroup, -1);
        }

        private void ReportGroupShown(RewardChoiceGroup group, int groupIndex)
        {
            if (group?.Choices == null)
            {
                return;
            }

            AnalyticsSelectionMode mode = ResolveSelectionMode(group);
            string offerId = $"{CurrentAnalyticsOfferId()}:g{groupIndex}";
            for (int index = 0; index < group.Choices.Count; index++)
            {
                RewardChoice choice = group.Choices[index];
                if (choice == null)
                {
                    continue;
                }

                GameAnalyticsService.TrackChoiceCandidate(
                    _run,
                    selected: false,
                    offerId,
                    CurrentAnalyticsContext(),
                    AnalyticsContentType(choice),
                    choice.Id,
                    AnalyticsBaseId(choice),
                    index,
                    group.Choices.Count,
                    group.RequiredChoiceCount,
                    mode,
                    archetype: _offerArchetype);
            }
        }

        private void ReportChoiceSelected(int groupIndex, int index)
        {
            RewardChoiceGroup group = GroupFor(groupIndex);
            if (group?.Choices == null || index < 0 || index >= group.Choices.Count)
            {
                return;
            }

            RewardChoice choice = group.Choices[index];
            if (choice == null)
            {
                return;
            }

            GameAnalyticsService.TrackChoiceCandidate(
                _run,
                selected: true,
                $"{CurrentAnalyticsOfferId()}:g{groupIndex}",
                CurrentAnalyticsContext(),
                AnalyticsContentType(choice),
                choice.Id,
                AnalyticsBaseId(choice),
                index,
                group.Choices.Count,
                group.RequiredChoiceCount,
                ResolveSelectionMode(group),
                archetype: _offerArchetype);
        }

        private void ReportOfferResolved()
        {
            if (_run == null || _offer == null)
            {
                return;
            }

            int selectedCount = _offer.SpecificGroup?.ClaimedIndices.Count ?? 0;
            bool skipped = _offer.SpecificGroup?.Skipped == true;
            foreach (RewardChoiceGroup group in _offer.FixedGroups)
            {
                selectedCount += group?.ClaimedIndices.Count ?? 0;
                skipped |= group?.Skipped == true || (group?.HasChoices == true && !group.IsResolved);
            }

            skipped |= _offer.SpecificGroup?.HasChoices == true && !_offer.SpecificGroup.IsResolved;
            long duration = (long)Math.Max(0d, (Time.realtimeSinceStartup - _offerOpenedRealtime) * 1000d);
            GameAnalyticsService.TrackChoiceOfferResolved(
                _run,
                CurrentAnalyticsOfferId(),
                CurrentAnalyticsContext(),
                selectedCount,
                skipped,
                0,
                duration,
                _offerArchetype);
        }

        private string CurrentAnalyticsOfferId()
        {
            return _genericMode ? _genericRewardKey : _rewardKey;
        }

        private string CurrentAnalyticsContext()
        {
            if (!_genericMode)
            {
                ActionExecutionContext context = BattleForm.Active?.CurrentBattleActionContext;
                return FoodService.IsBossAction(_run.Tables, context?.Action)
                    ? "boss_reward"
                    : "battle_reward";
            }

            if (_genericRewardContinuation == PendingGenericRewardContinuationKind.Slot)
            {
                return "slot_reward";
            }

            string key = _genericRewardKey ?? string.Empty;
            return key.IndexOf("shop", StringComparison.OrdinalIgnoreCase) >= 0
                && key.IndexOf("fragment", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "shop_fragment"
                    : "event_reward";
        }

        private static AnalyticsSelectionMode ResolveSelectionMode(RewardChoiceGroup group)
        {
            if (group == null || group.Choices.Count == 0)
            {
                return AnalyticsSelectionMode.Auto;
            }

            return group.Choices.Count > group.RequiredChoiceCount
                ? AnalyticsSelectionMode.Optional
                : AnalyticsSelectionMode.Forced;
        }

        private string AnalyticsBaseId(RewardChoice choice)
        {
            return choice?.Kind == cfg.RewardKind.DishChoice
                ? _run.Database?.GetDish(choice.Id)?.BaseId ?? choice.Id
                : string.Empty;
        }

        private static string AnalyticsContentType(RewardChoice choice)
        {
            return choice?.Kind switch
            {
                cfg.RewardKind.DishChoice => "dish",
                cfg.RewardKind.FragmentChoice => "fragment",
                cfg.RewardKind.PassiveItemChoice => "passive_item",
                cfg.RewardKind.ActiveItemGrant => "active_item",
                cfg.RewardKind.ActiveItemStrengthen => "active_item",
                cfg.RewardKind.ActiveItemAdjust => "active_item",
                cfg.RewardKind.Gold => "gold",
                _ => "unknown",
            };
        }

        private static void RefreshBattlePersistentHud(bool refreshItems = true)
        {
            BattleForm.Active?.RefreshPersistentHud(refreshItems);
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
            EnsureRewardListGroup();
            _rewardRowsSequence?.Kill();
            _rewardRowsSequence = null;
            bool playInitialRowsIn = !_hasBuiltRewardRows;

            RebuildRewardRowsImmediate();
            if (_rewardListGroup != null)
            {
                RestoreRewardListGroup();
            }

            if (playInitialRowsIn)
            {
                PlayRewardRowsIn(startDelay: TransitionSettings.InitialRowsDelay);
            }
        }

        private void RebuildRewardRowsImmediate()
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
            _hasBuiltRewardRows = true;
        }

        private void PlayRewardRowsIn(float startDelay = 0f)
        {
            _rewardRowsSequence?.Kill();
            var rects = new List<RectTransform>(_spawnedRows.Count);
            for (int i = 0; i < _spawnedRows.Count; i++)
            {
                if (_spawnedRows[i] != null && _spawnedRows[i].transform is RectTransform rect)
                {
                    rects.Add(rect);
                }
            }

            StaggerTransitionSettings settings = TransitionSettings.Rows ?? new StaggerTransitionSettings();
            _rewardRowsSequence = UITransition.StaggerIn(
                rects,
                settings.Duration,
                settings.Interval,
                settings.MaxDelay,
                startDelay);
        }

        private void EnsureRewardListGroup()
        {
            if (_rewardListGroup != null || _rewardListContent == null)
            {
                return;
            }

            _rewardListGroup = _rewardListContent.GetComponent<CanvasGroup>();
            if (_rewardListGroup == null)
            {
                _rewardListGroup = _rewardListContent.gameObject.AddComponent<CanvasGroup>();
            }
        }

        private void RestoreRewardListGroup()
        {
            if (_rewardListGroup == null)
            {
                return;
            }

            _rewardListGroup.alpha = 1f;
            _rewardListGroup.interactable = true;
            _rewardListGroup.blocksRaycasts = true;
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
                "点击领取",
                LoadBaseGoldIcon(),
                false,
                true,
                false,
                ClaimBaseGold);
            row.DisableTipTrigger();
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
                    () => ClaimChoice(groupIndex, index, choices, row),
                    dish: DishForChoice(choice),
                    flavorIds: FlavorIdsForChoice(choice),
                    showDishPreview: !suppressDishPreview,
                    selectionFlySprite: LoadChoiceIcon(choice));
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
                BuildGroupRuleText(group),
                icon,
                false,
                true,
                false,
                () => OpenChoicePack(groupIndex, choices, row),
                dish: suppressDishPreview ? null : DishForChoice(firstChoice),
                flavorIds: suppressDishPreview ? null : FlavorIdsForChoice(firstChoice));
        }

        private void OpenChoicePack(
            int groupIndex,
            IReadOnlyList<RewardChoice> choices,
            RewardChoiceRowView sourceRow)
        {
            RewardChoiceGroup group = GroupFor(groupIndex);
            if (group.RequiredChoiceCount >= group.Choices.Count)
            {
                ClaimAllRemainingChoices(groupIndex, choices, sourceRow);
                return;
            }

            if (IsDishPack(choices))
            {
                OpenDishPack(groupIndex, choices);
                return;
            }

            if (IsFragmentPack(choices))
            {
                ClaimChoice(groupIndex, 0, choices, null);
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
                        return false;
                    }

                    return ClaimItemChoiceFromPopup(groupIndex, sourceIndices[pickedIndex]);
                },
                ResumeFromRewardSubflow);
            if (opened)
            {
                if (closeRewardFormOnOpen)
                {
                    SuspendForRewardSubflow();
                }

                return true;
            }

            _expandedChoicePackGroupIndex = groupIndex;
            RefreshOffer();
            return false;
        }

        private bool ClaimItemChoiceFromPopup(int groupIndex, int index)
        {
            if (!RestoreRewardContextForCallback())
            {
                return false;
            }

            RewardChoiceGroup group = GroupFor(groupIndex);
            IReadOnlyList<RewardChoice> currentChoices = group.Choices;
            if (currentChoices == null
                || IsChoiceResolved(groupIndex)
                || index < 0
                || index >= currentChoices.Count
                || currentChoices[index] == null
                || IsChoiceClaimed(groupIndex, index))
            {
                return false;
            }

            using (RunPersistence.SuppressSave())
            {
                if (!RewardGranter.TryClaimChoice(_run, currentChoices[index], out _))
                {
                    return false;
                }
            }

            MarkChoiceClaimed(groupIndex, index);
            CacheCurrentOffer();
            RefreshBattlePersistentHud();
            return true;
        }

        private void ClaimAllRemainingChoices(
            int groupIndex,
            IReadOnlyList<RewardChoice> choices,
            RewardChoiceRowView sourceRow)
        {
            if (_offer == null || choices == null)
            {
                return;
            }

            int activeItemsNeeded = 0;
            for (int i = 0; i < choices.Count; i++)
            {
                if (!IsChoiceClaimed(groupIndex, i)
                    && choices[i] != null
                    && IsActiveItemReward(choices[i].Kind))
                {
                    activeItemsNeeded++;
                }
            }

            int freeActiveSlots = _run != null
                ? System.Math.Max(0, _run.ActiveSlotCapacity - _run.ActiveItemCount)
                : 0;
            if (activeItemsNeeded > freeActiveSlots)
            {
                sourceRow?.PlayTargetFailed();
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
                    if (!RewardGranter.TryClaimChoice(_run, choice, out _))
                    {
                        return;
                    }
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
            UIButtonSoundFeedback.Install(row.transform);
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
                    ? "消耗品槽已满，暂时无法领取。"
                    : $"{description}\n消耗品槽已满，暂时无法领取。";
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
                    return "加入食谱，后续经营挑战中可能抽到。";
                case cfg.RewardKind.FragmentChoice:
                    return "获得餐桌格包，进入餐桌编辑后选择并拼贴一块。";
                case cfg.RewardKind.PassiveItemChoice:
                    return "获得后持续生效，重复获得时会升级或折算。";
                case cfg.RewardKind.ActiveItemGrant:
                case cfg.RewardKind.ActiveItemStrengthen:
                case cfg.RewardKind.ActiveItemAdjust:
                    return "获得一个消耗品，可在经营挑战中使用。";
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

            // 候选全部必领时已经没有选择行为，直接把每个 roll 结果展示在 RewardForm。
            // 餐桌格仍需作为整包进入编辑页，不能拆成普通奖励行。
            bool direct = group.RequiredChoiceCount >= choices.Count
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

        private static string BuildGroupRuleText(RewardChoiceGroup group)
        {
            if (group == null)
            {
                return string.Empty;
            }

            return !string.IsNullOrWhiteSpace(group.RuleText)
                ? group.RuleText
                : BuildLegacyRuleText(group);
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
                    return "食物";
                case cfg.RewardKind.FragmentChoice:
                    return "碎片";
                case cfg.RewardKind.ActiveItemGrant:
                case cfg.RewardKind.ActiveItemStrengthen:
                case cfg.RewardKind.ActiveItemAdjust:
                    return "消耗品";
                case cfg.RewardKind.PassiveItemChoice:
                    return "装饰品";
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

            // 旧档没有 SourceSlotId；普通/困难食物奖励的第一个固定食物组就是基础食物。
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

            if (choice.Kind == cfg.RewardKind.Gold || choice.IsFallbackGold)
            {
                row.DisableTipTrigger();
                return;
            }

            TipHoverTrigger trigger = row.EnsureTipTrigger();
            trigger.SetTarget(row.TipPlacementTarget);
            trigger.SetFollowPointer(true);
            trigger.SetPreferVerticalPlacement(true);

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
