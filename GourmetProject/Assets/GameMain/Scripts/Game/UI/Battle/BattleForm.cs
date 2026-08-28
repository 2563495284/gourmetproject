using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BreakInfinity;
using DG.Tweening;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Analytics;
using GourmetProject.Game.Flow;
using GourmetProject.Game.Save;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Orchestration;
using GourmetProject.Game.Run;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using Log = GourmetProject.Core.Diagnostics.Log;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Game.UI.Battle.Pages;
using GourmetProject.Game.UI.Battle.States;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Tutorial;
using UnityEngine.Serialization;

namespace GourmetProject.Game.UI.Battle
{
    /// <summary>
    /// 局外周循环编排枢纽 + 局内经营挑战结果壳。
    /// 常驻壳（左列信息 / 右列装饰品和消耗品 / 顶部时间轴）进入玩法后全程常驻；真实业务页面只在中部内容区切换。
    /// 菜谱/餐桌查看与餐桌碎片编辑分别由独立层承载，不进入主页面状态机。主页面切换只对中部内容区做
    /// DOTween 渐隐渐显（<see cref="UITransition.FadeSwap"/>），常驻壳不参与动画。
    /// </summary>
    public sealed class BattleForm : UGuiForm,
        IWeekLoopView,
        IBattleInspectionHost,
        IBattleOverlaySuspensionHost,
        IBattleTableFragmentEditHost,
        IGameplayPageRouterHost,
        IRecipeBookHost,
        IShopPageHost,
        IRewardPageHost,
        IEventPageHost
    {
        private const string Tag = "Battle";
        private const float RandomizedItemFlyDuration = 0.42f;
        private const float RecipeCopySpriteSize = 250f;
        private const float RecipeCopyCenterMargin = 64f;
        private const float RecipeCopyHorizontalSpacing = 210f;
        private const float RecipeCopyVerticalSpacing = 180f;
        private const int RecipeCopyMaxColumns = 5;
        private const string GoldOnTransferItemId = "item_gold_on_transfer";
        private enum FoodTipsHoverOwner
        {
            None,
            TableDish,
            TableCell,
            TableFragment,
            ServingOutlet,
            Tutorial,
        }

        /// <summary>当前打开的经营挑战界面，供各弹窗回调推进周循环。</summary>
        public static BattleForm Active { get; private set; }

        [Header("HUD Frame")]
        [SerializeField] private GameObject _hudFrame;
        [SerializeField] private GameObject _backdrop;

        [Header("Center (切换动画区)")]
        [Tooltip("中部内容区根的 CanvasGroup：五态切换时对它做渐隐渐显；常驻壳不在其下。")]
        [SerializeField] private CanvasGroup _center;
        [Tooltip("独立的中部世界遮罩；范围与 Center 一致，不包含左右常驻栏。")]
        [SerializeField] private CanvasGroup _centerTransitionCover;
        [SerializeField] private GameplayTransitionSettings _pageTransitionSettings = new GameplayTransitionSettings();
        [Tooltip("遮黑期间展示最终目标美味值与星级评鉴规则的全屏输入覆盖层。")]
        [SerializeField] private BattleEntryPresentationView _battleEntryPresentation;

        [Header("DiningTable Area (餐桌锁定区)")]
        [Tooltip("HUD 里的空区矩形：世界餐桌将 fit 并居中锁定在该屏幕区域内。")]
        [SerializeField] private RectTransform _boardArea;

        [Header("Independent Inspection Layer")]
        [SerializeField] private BattleInspectionLayer _inspectionLayer;

        [Header("Independent Table Fragment Edit Layer")]
        [SerializeField] private BattleTableFragmentEditLayer _fragmentEditLayer;

        [Header("Left Column")]
        [SerializeField] private BattleInfoColumn _infoColumn;

        [Header("Action Axis")]
        [FormerlySerializedAs("_actionAxisBar")]
        [SerializeField] private TimelineAxisView _timelineAxis;

        [Header("Hover Tips")]
        [SerializeField] private BattleTipRegistry _tips;
        private CakeLayerBuffHud _cakeLayerBuffHud;

        [Header("Action Selection (center)")]
        [SerializeField] private GameObject _actionSelectionPanel;
        [SerializeField] private ActionCardDeck _deck;

        [Header("Shop (center)")]
        [SerializeField] private ShopForm _shopPanel;

        [Header("Recipe Workspace (center)")]
        [FormerlySerializedAs("_recipeEditPanel")]
        [SerializeField] private RecipeReadonlyBookView _recipeReadonlyBookView;

        [Header("Reward Dish Pack (center)")]
        [SerializeField] private RewardSubflowLayer _rewardSubflowLayer;
        [SerializeField] private RewardDishPackPanel _rewardDishPackPanel;
        [SerializeField] private RewardItemChoicePanel _rewardItemChoicePanel;
        [SerializeField] private RandomizedItemsPanel _randomizedItemsPanel;

        [Header("Event Page (center)")]
        [FormerlySerializedAs("_eventPanel")]
        [SerializeField] private EventPagePanel _eventPagePanel;

        [Header("Overlay Suspension")]
        [FormerlySerializedAs("_rewardTableEditActionAxisGroup")]
        [Tooltip("覆盖层显示期间用于精确冻结时间轴表现；组件固定在 BattleForm prefab 上。")]
        [SerializeField] private CanvasGroup _actionAxisGroup;

        [Header("Right Column - Items")]
        [SerializeField] private BattleItemsColumn _itemsColumn;

        [Header("Active Items")]
        [SerializeField] private ActiveItemActionPopup _activeItemPopupPrefab;
        [SerializeField] private TargetArrowView _activeItemTargetArrowPrefab;
        [SerializeField] private ActiveItemTargetOverlayView _activeItemTargetOverlayPrefab;
        [SerializeField] private GameObject _shopItemFlyFxPrefab;

        [Header("Food Battle (center)")]
        [Tooltip("美食战斗专属 UI 根节点：MessageText / FoodActions / BuffList / ServingOutlet / FoodDiscardBin / TemporaryArea。")]
        [SerializeField] private GameObject _foodBattlePanel;
        [SerializeField] private BattleFoodActionBar _foodBar;
        [SerializeField] private ServingOutletView _servingOutlet;
        [SerializeField] private FoodDiscardBinView _foodDiscardBin;
        [Tooltip("美食战斗 HUD 里的暂存区框：世界棋子按此矩形投影落位。")]
        [SerializeField] private RectTransform _temporaryArea;
        [SerializeField] private RectTransform _bottomUi;
        [Tooltip("BottomUI（含出餐口）下滑出屏时长（秒）。")]
        [SerializeField] private float _foodSettlementLayoutDuration = 0.32f;
        [Tooltip("TableLayoutArea 放大、餐桌跟上的时长（秒）。")]
        [SerializeField] private float _foodSettlementTableLayoutDuration = 0.64f;
        [Tooltip("布局动画结束后、开始结算演出前的额外静止停顿（秒）；0 表示不空停，由第一份食物的章节起势承接。")]
        [SerializeField] private float _foodSettlementHoldDuration = 0f;

        private DishDragGhostOverlay _dragGhostOverlay;
        private TemporaryAreaDishOverlay _temporaryAreaDishOverlay;
        private CanvasGroup _bottomUiGroup;
        private Vector2 _bottomUiRestPosition;
        private bool _bottomUiRestCaptured;
        private Tween _bottomUiTween;

        private bool _inBattle;
        private GameplayView _current = GameplayView.None;

        private GameRun _run;
        private BattleSession _session;
        private BattleSession _foodDiscardCapacitySession;
        private int _observedFoodDiscardCapacity = -1;
        private BattleWorldController _world;

        private WeekLoopController _loop;

        private TimelineAxisBinder _axisBinder;
        private TimelineAxisFocusPresenter _timelineAxisFocus;
        private BattleOverlaySuspension _overlaySuspension;
        private BattleInspectionCoordinator _inspectionCoordinator;
        private BattleTableFragmentEditCoordinator _fragmentEditCoordinator;
        private GameplayPageRouter _pageRouter;
        private ShopPageCoordinator _shopPage;
        private RecipeBookCoordinator _recipeBookPage;
        private RewardPageCoordinator _rewardPage;
        private EventPageCoordinator _eventPage;
        private ActiveItemUseCoordinator _activeItemUse;
        private int _shopItemFlyInFlight;
        private readonly HashSet<ShopPurchaseFlyView> _activeShopPurchaseFlys = new();
        private readonly TutorialAcquiredItemPresentationGate _tutorialAcquiredItemPresentation =
            new(TutorialRuntime.ObserveContentAcquired);
        private string _pendingTutorialAcquiredPassiveItemId = string.Empty;
        private string _pendingTutorialAcquiredActiveItemId = string.Empty;
        private RectTransform _tutorialAcquiredPassiveItemAnchor;
        private RectTransform _tutorialAcquiredActiveItemAnchor;
        private int? _battleMusicSerialId;
        private cfg.BossDebuff _currentBossDebuff;
        [SerializeField] private BossDebuffPresentationView _bossPresentation;
        private BossDialogueShuffleBag _bossDialogueBag;
        private CancellationTokenSource _bossPresentationCts;
        private int _bossPresentationVersion;
        private bool _bossDiscardRevealPending;
        private readonly HashSet<ShopPurchaseFlyView> _bossRecipeCopyFlys = new();
        private cfg.TimelineNode _currentTimelineNodeCard;
        private int? _currentTimelineNodeInterestMaxGain;
        private Action _currentTimelineNodePick;
        private ArchetypeVector _actionOfferArchetype;
        private float _actionOfferOpenedRealtime;
        private int _activeBattleRawRequiredScore;
        private string _activeBattleModifier = string.Empty;
        private string _activeBossDebuffId = string.Empty;
        private string _activeBattleKey = string.Empty;
        private bool _activeBattleIsBoss;
        private bool _rewardPeekOnly;
        private bool _discardSettlementCallbacks;
        private DishPieceView _hoveredDishPiece;
        private GridPos? _hoveredCell;
        private int _hoveredTableFragmentSession = -1;
        private int _hoveredTableFragmentIndex = -1;
        private FoodTipsHoverOwner _foodTipsHoverOwner;
        private SettlementRevealState _settlementReveal;
        private int _displayedCakeLayers;
        private int? _pendingSettlementCakeLayers;
        private int _pendingSettlementCakeLayerBonus;
        private bool _settlementPassivePresentationActive;
        private int _settlementPresentedGold;
        private int _settlementSuppressedPassiveGold;
        private readonly Dictionary<string, RunItemSlotView> _settlementPassivePresentationSlots =
            new(StringComparer.Ordinal);
        private readonly PassivePresentationScheduler _passivePresentations = new();
        private Sequence _passivePresentationDelay;
        private CanvasGroup _passivePresentationInputBlocker;
        private int _switchRequestId;
        private CanvasGroup _passiveFlavorMutationPage;
        private RectTransform _passiveFlavorMutationContent;
        private readonly List<RecipeEditDishView> _passiveFlavorMutationDishes = new();
        private readonly List<RecipeMutationEntry> _passiveFlavorMutationEntries = new();
        private readonly HashSet<ShopPurchaseFlyView> _passiveFlavorMutationFlys = new();
        private readonly HashSet<ShopPurchaseFlyView> _passiveRecipeCopyFlys = new();

        public GameRun Run => _run;
        public BattleSession Session => _session;
        public event Action<ShopEntryKind> ShopPurchaseAnimationStarted;
        public event Action PreparingChild;
        public event Action ChildReady;
        public event Action PreparingReturn;
        public event Action ParentRestored;
        public ActionExecutionContext CurrentBattleActionContext => _loop?.CurrentBattleActionContext;
        internal GameRun ActiveRun => _run;
        internal BattleSession ActiveSession => _session;
        internal WeekLoopController ActiveLoop => _loop;
        internal GameplayView CurrentView => _current;
        internal bool InBattle => _inBattle;
        internal bool IsActionAxisVisible =>
            _timelineAxis != null
            && _timelineAxis.gameObject.activeInHierarchy
            && (_actionAxisGroup == null || _actionAxisGroup.alpha > 0f);
        internal bool IsDailyActionSelectionActive =>
            _current == GameplayView.ActionSelect && _currentTimelineNodeCard == null;
        internal BattleInspectionView ActiveInspectionView =>
            _inspectionCoordinator?.View ?? BattleInspectionView.None;
        internal bool IsInspectionOverlayActive =>
            _inspectionCoordinator?.ManagesSourcePresentation == true;
        internal bool IsTableFragmentEditActive => _fragmentEditCoordinator?.IsActive == true;
        internal BattleWorldController ActiveWorld => _world ?? BattleWorldController.Instance;
        internal ActiveItemActionPopup ActiveItemPopupPrefab => _activeItemPopupPrefab;
        internal TargetArrowView ActiveItemTargetArrowPrefab => _activeItemTargetArrowPrefab;
        internal ActiveItemTargetOverlayView ActiveItemTargetOverlayPrefab => _activeItemTargetOverlayPrefab;
        internal Transform ActiveItemLayer
        {
            get
            {
                Canvas canvas = GetComponentInParent<Canvas>();
                return canvas != null ? canvas.transform : transform;
            }
        }

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            EnsureCenterTransitionCover();
            EnsurePassivePresentationInputBlocker();
            _passivePresentations.BindLock(SetPassivePresentationInputLocked);
            EnsureRewardSubflowLayer();
            _inspectionLayer?.Initialize();
            _fragmentEditLayer?.Initialize();

            _infoColumn?.Bind(OnSettingsClicked, OnViewTableClicked, OnViewRecipeClicked);
            _inspectionLayer?.TablePanel?.Bind(OnExitTableViewClicked);
            _cakeLayerBuffHud = GetComponent<CakeLayerBuffHud>();
            _foodBar?.Bind(
                OnEatClicked,
                OnBattleRecipeClicked,
                OnDoodleDrawClicked,
                OnDoodleEraseClicked,
                OnDoodleClearClicked,
                OnDoodleToggleClicked);
            if (_bossPresentation == null)
            {
                throw new MissingReferenceException(
                    "BattleForm requires the nested BossDebuffPresentationOverlay prefab reference.");
            }
            _bossPresentation.EnsureBuilt();
            if (_battleEntryPresentation == null)
            {
                throw new MissingReferenceException(
                    "BattleForm requires the nested BattleEntryPresentationOverlay prefab reference.");
            }
            _battleEntryPresentation.EnsureBuilt();

            _axisBinder = new TimelineAxisBinder(
                _timelineAxis,
                () => _tips != null ? _tips.Timeline : null);
            _timelineAxisFocus = new TimelineAxisFocusPresenter(
                _timelineAxis != null ? _timelineAxis.transform as RectTransform : null,
                ResolveActionAxisGroup(),
                _timelineAxis != null ? _timelineAxis.WeekTransitionTextStyle : null);
            _deck?.SetRewardTip(() => _tips != null ? _tips.Item : null);
            _pageRouter = new GameplayPageRouter(this);
            _shopPage = new ShopPageCoordinator(this);
            _recipeBookPage = new RecipeBookCoordinator(this);
            _rewardPage = new RewardPageCoordinator(this);
            _eventPage = new EventPageCoordinator(this);
            _overlaySuspension = new BattleOverlaySuspension(this);
            _inspectionCoordinator = new BattleInspectionCoordinator(this);
            _fragmentEditCoordinator = new BattleTableFragmentEditCoordinator(this);
            _fragmentEditLayer?.Bind(_fragmentEditCoordinator.ExecuteCurrentAction);
            _activeItemUse = new ActiveItemUseCoordinator(this);

            HideHud();
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            _run = GameRunContext.Current;
            if (_run == null)
            {
                Log.Error("BattleForm opened without an active run.", Tag);
                return;
            }

            Active = this;
            RegisterTutorialAnchors();
            RegisterTutorialCommands();
            _discardSettlementCallbacks = false;
            _run.GoldChanged -= OnGoldChanged;
            _run.GoldChanged += OnGoldChanged;
            _run.ContentAcquired -= OnContentAcquired;
            _run.ContentAcquired += OnContentAcquired;
            // 尽早绑定场景里的经营挑战世界单例：否则首次 StartBattle 之前 _world 为 null，
            // BeginWeek 里的 HideBattleWorld 会变成空操作，导致进场景默认态残留食物专属按钮。
            _world = BattleWorldController.Instance;
            if (_tips != null)
            {
                _tips.EnsureAll();
            }

            _loop = new WeekLoopController(_run, this);
            _loop.BeginWeek();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            TutorialRuntime.CloseForPageChange();
            UnregisterTutorialCommands();
            UnregisterTutorialAnchors();
            ClearTutorialAcquiredItemPresentationState();
            FinalizeSettlementAndEndPassivePresentation();
            if (Active == this)
            {
                Active = null;
            }

            _discardSettlementCallbacks = true;
            if (_run != null)
            {
                _run.GoldChanged -= OnGoldChanged;
                _run.ContentAcquired -= OnContentAcquired;
            }
            SetSession(null);
            StopBattleMusic(0.15f);
            CancelBossPresentation();
            ResetBossBattlePresentation();
            CancelActiveShopPurchaseAnimations();
            _inspectionCoordinator?.ForceClose();
            _rewardPage?.CloseRewardPages();
            _fragmentEditCoordinator?.ForceClose();
            _settlementReveal = null;
            _loop = null;
            _switchRequestId++;
            _pageRouter?.CancelTransition();
            _battleEntryPresentation?.ResetImmediate();
            CancelPassivePresentations();
            _activeItemUse?.Dispose();
            _rewardPeekOnly = false;
            _axisBinder?.EndAdvanceSequence();
            _timelineAxisFocus?.Cancel();
            _timelineAxis?.CancelPresentation();
            _deck?.KillAllTweens();
            ClearWorldHoverCallbacks();
            HideAllTips();
            if (_world != null)
            {
                _world.HideWorld();
            }

            UnsubscribeCakeLayerChanges();
            ResetFoodDiscardCapacityTracking();

            base.OnClose(isShutdown, userData);
        }

        private void Update()
        {
            SynchronizeFoodDiscardCapacity();
            _activeItemUse?.Update();
        }

        // —— 周循环编排（代理到 WeekLoopController）——

        public BigDouble LastBattleTotal => _session?.LastResult?.Total ?? BigDouble.Zero;

        private bool HasPendingBattleRewardLifecycle
            => _session?.IsSettled == true && _run?.HasPendingRewardBattleView == true;

        internal bool IsRewardItemContextActive => RewardForm.Active != null;

        internal bool IsActiveItemUseBlocked
            => _passivePresentations.IsBusy
                || _fragmentEditCoordinator?.IsActive == true
                || _rewardPage?.IsActive == true
                || (RewardForm.Active != null && !RewardForm.Active.AllowsPersistentInteractions)
                || (HasPendingBattleRewardLifecycle && RewardForm.Active == null);

        /// <summary>进入（或继续）一周：随机/沿用时间轴后开始行动循环。</summary>
        public void BeginWeek()
        {
            UnsubscribeCakeLayerChanges();
            SetSession(null);
            ResetFoodDiscardCapacityTracking();
            _loop?.BeginWeek();
        }

        /// <summary>时间轴未走完则弹「n 选一行动」；走完则进入下一周。</summary>
        public void PromptNextAction()
        {
            _loop?.PromptNextAction();
        }

        /// <summary> 选择行动后回调（null = 无行动可选时的「休息」）。</summary>
        public void OnActionPicked(ActionChoice choice)
        {
            _loop?.OnActionPicked(choice);
        }

        /// <summary>离开商店态时回调，继续周循环编排。</summary>
        public void OnShopClosed()
        {
            _loop?.OnShopClosed();
        }

        /// <summary>RewardForm 发奖确认后回调：继续经营挑战后的编排续接。</summary>
        public void OnRewardConfirmed()
        {
            _infoColumn?.SetBossBattlePresentation(null, false, true);
            UnsubscribeCakeLayerChanges();
            _session?.ClearHappyCakeLayers();
            _displayedCakeLayers = 0;
            _pendingSettlementCakeLayers = null;
            if (_current == GameplayView.Food)
            {
                ExitBattleBeforeTimelinePresentation(() => _loop?.OnRewardConfirmed());
                return;
            }

            _loop?.OnRewardConfirmed();
        }

        /// <summary>抽奖机奖励领取完成后，回到同一台抽奖机或结束已达上限的节点。</summary>
        public void OnSlotRewardConfirmed()
        {
            _loop?.OnSlotRewardConfirmed();
        }

        /// <summary>事件奖励领取完成后，恢复并结束仍在底层显示的事件页。</summary>
        public void OnEventRewardConfirmed()
        {
            _loop?.OnEventRewardConfirmed();
        }

        public void CloseRewardOperationPages()
        {
            _rewardPage?.CloseRewardPages();
        }

        public void SetRewardPeekOnly(bool active)
        {
            _rewardPeekOnly = active;
            if (active)
            {
                HideAllTips();
            }
        }

        public void HideBattleWorld()
        {
            CancelBossPresentation();
            RestoreBottomUiImmediate();
            _infoColumn?.SetBattleScoreOverride(null);
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            world?.HideWorld();
            world?.ClearBattleTable();
        }

        public void ResetBossBattlePresentation()
        {
            _infoColumn?.SetBossBattlePresentation(null, false, false);
        }

        public void SavePendingRewardBattleView()
        {
            if (_run == null || _session == null || !_session.IsSettled || _session.LastResult == null)
            {
                return;
            }

            var snapshot = new PendingRewardBattleViewSaveData
            {
                RequiredScore = _session.RequiredScore,
                RawRequiredScore = _activeBattleRawRequiredScore > 0 ? _activeBattleRawRequiredScore : _session.RequiredScore,
                Modifier = _activeBattleModifier ?? string.Empty,
                BossDebuffId = _activeBossDebuffId ?? string.Empty,
                BattleKey = _activeBattleKey ?? string.Empty,
                IsBoss = _activeBattleIsBoss,
                LastTotal = BigNumberSaveData.ToLegacyInt(_session.LastResult.Total),
                LastTotalBig = BigNumberSaveData.From(_session.LastResult.Total),
                FinalHappyCakeLayers = _session.HappyCakeLayers,
                HasDetailedScore = true,
                RawSum = BigNumberSaveData.ToLegacyFloat(_session.LastResult.RawSum),
                RawSumBig = BigNumberSaveData.From(_session.LastResult.RawSum),
                FinalFlat = BigNumberSaveData.ToLegacyFloat(_session.LastResult.FinalFlat),
                FinalFlatBig = BigNumberSaveData.From(_session.LastResult.FinalFlat),
                FinalMultiplier = BigNumberSaveData.ToLegacyFloat(_session.LastResult.FinalMultiplier),
                FinalMultiplierBig = BigNumberSaveData.From(_session.LastResult.FinalMultiplier),
                Dishes = new List<PendingRewardBattleDishSaveData>(),
                Cakes = new List<PendingRewardCakeVisualSaveData>(),
            };

            var scoresByDishId = new Dictionary<int, DishScore>();
            foreach (DishScore score in _session.LastResult.DishScores)
            {
                if (score != null)
                {
                    scoresByDishId[score.DishInstanceId] = score;
                }
            }

            foreach (DishInstance dish in _session.DiningTable.Dishes)
            {
                if (dish?.Def == null)
                {
                    continue;
                }

                scoresByDishId.TryGetValue(dish.Id, out DishScore dishScore);
                snapshot.Dishes.Add(new PendingRewardBattleDishSaveData
                {
                    Id = dish.Id,
                    DishId = dish.Def.Id,
                    OriginX = dish.Placement.Origin.X,
                    OriginY = dish.Placement.Origin.Y,
                    Rotation = dish.Placement.RotationIndex,
                    SourceSlotIndex = dish.SourceSlotIndex,
                    SourceDishIndex = dish.SourceDishIndex,
                    SkillIds = new List<string>(dish.SkillIds),
                    FlavorIds = new List<string>(dish.FlavorIds),
                    RuntimeCountAsBonus = dish.RuntimeCountAsBonus,
                    PermanentFlatBonus = BigNumberSaveData.ToLegacyFloat(dish.PermanentFlatBonus),
                    PermanentFlatBonusBig = BigNumberSaveData.From(dish.PermanentFlatBonus),
                    PermanentMultBonus = BigNumberSaveData.ToLegacyFloat(dish.PermanentMultBonus),
                    PermanentMultBonusBig = BigNumberSaveData.From(dish.PermanentMultBonus),
                    TemporaryBaseMultiplier = BigNumberSaveData.ToLegacyFloat(dish.TemporaryBaseMultiplier),
                    TemporaryBaseMultiplierBig = BigNumberSaveData.From(dish.TemporaryBaseMultiplier),
                    ServeMultiplier = BigNumberSaveData.ToLegacyFloat(dish.ServeMultiplier),
                    ServeMultiplierBig = BigNumberSaveData.From(dish.ServeMultiplier),
                    ServeMultiplierFlatBonus = BigNumberSaveData.ToLegacyFloat(dish.ServeMultiplierFlatBonus),
                    ServeMultiplierFlatBonusBig = BigNumberSaveData.From(dish.ServeMultiplierFlatBonus),
                    SkillsDisabled = dish.SkillsDisabled,
                    ExcludedFromScore = dish.ExcludedFromScore,
                    IsTemporary = dish.IsTemporary,
                    HasDishScore = dishScore != null,
                    ScoreBaseValue = BigNumberSaveData.ToLegacyFloat(dishScore?.BaseValue ?? BigDouble.Zero),
                    ScoreBaseValueBig = BigNumberSaveData.From(dishScore?.BaseValue ?? BigDouble.Zero),
                    ScoreFlatBonus = BigNumberSaveData.ToLegacyFloat(dishScore?.FlatBonus ?? BigDouble.Zero),
                    ScoreFlatBonusBig = BigNumberSaveData.From(dishScore?.FlatBonus ?? BigDouble.Zero),
                    ScoreMultiplier = BigNumberSaveData.ToLegacyFloat(dishScore?.Multiplier ?? BigDouble.One),
                    ScoreMultiplierBig = BigNumberSaveData.From(dishScore?.Multiplier ?? BigDouble.One),
                    ScoreEffectiveCountAs = dishScore?.EffectiveCountAs ?? Math.Max(1, dish.EffectiveCountAs),
                    ScoreExtraSettlementContribution = BigNumberSaveData.ToLegacyFloat(dishScore?.ExtraSettlementContribution ?? BigDouble.Zero),
                    ScoreExtraSettlementContributionBig = BigNumberSaveData.From(dishScore?.ExtraSettlementContribution ?? BigDouble.Zero),
                    ScoreExtraSettlementCount = dishScore?.ExtraSettlementCount ?? 0,
                });
            }

            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if (world != null)
            {
                foreach (CakeLayerVisualState cake in world.CapturePendingRewardCakeVisuals())
                {
                    snapshot.Cakes.Add(new PendingRewardCakeVisualSaveData
                    {
                        ViewportX = cake.ViewportX,
                        ViewportY = cake.ViewportY,
                        RotationZ = cake.RotationZ,
                        ScaleX = cake.ScaleX,
                        ScaleY = cake.ScaleY,
                        ScaleZ = cake.ScaleZ,
                    });
                }
            }

            _run.SetPendingRewardBattleView(snapshot);
        }

        public void RestorePendingRewardBattleView()
        {
            PendingRewardBattleViewSaveData snapshot = _run?.GetPendingRewardBattleView();
            if (snapshot == null)
            {
                return;
            }

            HideAllTips();
            _infoColumn?.ScoreFire?.Hide();
            BigDouble restoredTotal = SavedValue(snapshot.LastTotalBig, snapshot.LastTotal);
            BigDouble restoredRawSum = SavedValue(snapshot.RawSumBig, snapshot.RawSum);
            BigDouble restoredFinalFlat = SavedValue(snapshot.FinalFlatBig, snapshot.FinalFlat);
            BigDouble restoredFinalMultiplier = SavedValue(snapshot.FinalMultiplierBig, snapshot.FinalMultiplier);
            _infoColumn?.SetBattleScoreOverride(restoredTotal);

            _activeBattleRawRequiredScore = snapshot.RawRequiredScore > 0 ? snapshot.RawRequiredScore : snapshot.RequiredScore;
            _activeBattleModifier = snapshot.Modifier ?? string.Empty;
            _activeBossDebuffId = snapshot.BossDebuffId ?? string.Empty;
            _activeBattleKey = string.IsNullOrEmpty(snapshot.BattleKey)
                ? (_run.PendingRewardKey ?? string.Empty)
                : snapshot.BattleKey;
            _activeBattleIsBoss = snapshot.IsBoss;
            _currentBossDebuff = _activeBattleIsBoss ? ResolveBossDebuff(_activeBossDebuffId) : null;
            _infoColumn?.SetBossBattlePresentation(
                _currentBossDebuff,
                _activeBattleIsBoss,
                animate: false);

            UnsubscribeCakeLayerChanges();
            SetSession(_run.BuildBattleSession(
                _activeBattleRawRequiredScore,
                _activeBattleModifier,
                _activeBattleKey,
                _activeBossDebuffId));
            BeginFoodDiscardCapacityTracking();
            _pendingSettlementCakeLayers = null;
            _session.DiningTable.Clear();
            RestorePendingRewardBattleDishes(_session, snapshot);
            List<DishScore> dishScores = RestorePendingRewardDishScores(snapshot);
            _session.RestoreSettledForRewardView(
                restoredTotal,
                snapshot.FinalHappyCakeLayers,
                dishScores,
                restoredRawSum,
                restoredFinalFlat,
                restoredFinalMultiplier,
                snapshot.HasDetailedScore);
            _displayedCakeLayers = _session.HappyCakeLayers;
            _session.HappyCakeLayersChanged += OnHappyCakeLayersChanged;

            SwitchTo(GameplayView.Food);

            _world = BattleWorldController.Instance;
            if (_world == null)
            {
                Log.Error("BattleForm: cannot restore pending reward battle view because battle scene controller is missing.", Tag);
                RefreshPersistent();
                return;
            }

            BindWorldHudAreas();
            _world.Initialize(
                _run,
                _session,
                SetMessage,
                SetSettlementScore,
                RefreshAll,
                null,
                OnDishClicked);
            _world.SetDishHoverCallbacks(OnDishHoverEntered, OnDishHoverExited);
            _world.SetCellHoverCallbacks(OnCellHoverEntered, OnCellHoverExited);
            _world.SetTableFragmentHoverCallbacks(OnTableFragmentHoverEntered, OnTableFragmentHoverExited);
            RestorePendingRewardPresentation(snapshot);
            SetSettlementScore(restoredTotal);
            RefreshAll();
        }

        private static List<DishScore> RestorePendingRewardDishScores(PendingRewardBattleViewSaveData snapshot)
        {
            var result = new List<DishScore>();
            if (snapshot?.Dishes == null || !snapshot.HasDetailedScore)
            {
                return result;
            }

            foreach (PendingRewardBattleDishSaveData dish in snapshot.Dishes)
            {
                if (dish == null || !dish.HasDishScore)
                {
                    continue;
                }

                result.Add(new DishScore(
                    dish.Id,
                    dish.DishId ?? string.Empty,
                    SavedValue(dish.ScoreBaseValueBig, dish.ScoreBaseValue),
                    SavedValue(dish.ScoreFlatBonusBig, dish.ScoreFlatBonus),
                    SavedValue(dish.ScoreMultiplierBig, dish.ScoreMultiplier),
                    dish.ScoreEffectiveCountAs,
                    SavedValue(dish.ScoreExtraSettlementContributionBig, dish.ScoreExtraSettlementContribution),
                    dish.ScoreExtraSettlementCount));
            }

            return result;
        }

        private static BigDouble SavedValue(BigNumberSaveData value, BigDouble legacy)
        {
            return value?.GetValue(legacy) ?? legacy;
        }

        private void RestorePendingRewardPresentation(PendingRewardBattleViewSaveData snapshot)
        {
            if (_world == null || snapshot == null)
            {
                return;
            }

            var cakes = new List<CakeLayerVisualState>();
            if (snapshot.Cakes != null)
            {
                foreach (PendingRewardCakeVisualSaveData cake in snapshot.Cakes)
                {
                    if (cake == null)
                    {
                        continue;
                    }

                    cakes.Add(new CakeLayerVisualState(
                        cake.ViewportX,
                        cake.ViewportY,
                        cake.RotationZ,
                        cake.ScaleX,
                        cake.ScaleY,
                        cake.ScaleZ));
                }
            }

            _world.RestorePendingRewardPresentation(cakes, _session?.LastResult?.DishScores);
        }

        private void RestorePendingRewardBattleDishes(BattleSession session, PendingRewardBattleViewSaveData snapshot)
        {
            if (session == null || snapshot?.Dishes == null)
            {
                return;
            }

            var usedIds = new HashSet<int>();
            int fallbackId = 1;
            foreach (PendingRewardBattleDishSaveData saved in snapshot.Dishes)
            {
                if (saved == null || string.IsNullOrEmpty(saved.DishId))
                {
                    continue;
                }

                DishDef def = session.Database.GetDish(saved.DishId);
                if (def == null)
                {
                    Log.Warning($"BattleForm: skip restored dish '{saved.DishId}' because it no longer exists.", Tag);
                    continue;
                }

                int id = saved.Id > 0 && usedIds.Add(saved.Id) ? saved.Id : NextRestoredDishId(usedIds, ref fallbackId);
                int rotation = ((saved.Rotation % 4) + 4) % 4;
                var placement = new Placement(def.Shape.RotatedBy(rotation), rotation, new GridPos(saved.OriginX, saved.OriginY));
                var dish = new DishInstance(id, def, placement, saved.SkillIds, saved.FlavorIds);
                dish.SetSourceRecipeIndex(saved.SourceSlotIndex, saved.SourceDishIndex);
                ApplyPendingRewardDishRuntimeState(dish, saved);

                if (!session.DiningTable.CanPlace(placement.Orientation, placement.Origin))
                {
                    Log.Warning($"BattleForm: skip restored dish '{saved.DishId}' at ({saved.OriginX},{saved.OriginY}) because the table changed.", Tag);
                    continue;
                }

                session.DiningTable.Place(dish);
            }
        }

        private static int NextRestoredDishId(HashSet<int> usedIds, ref int next)
        {
            while (!usedIds.Add(next))
            {
                next++;
            }

            return next++;
        }

        private static void ApplyPendingRewardDishRuntimeState(DishInstance dish, PendingRewardBattleDishSaveData saved)
        {
            if (dish == null || saved == null)
            {
                return;
            }

            if (saved.RuntimeCountAsBonus != 0)
            {
                dish.AddCountAsBonus(saved.RuntimeCountAsBonus);
            }

            BigDouble permanentFlat = SavedValue(saved.PermanentFlatBonusBig, saved.PermanentFlatBonus);
            if (BigDouble.Abs(permanentFlat) > 0.0001d)
            {
                dish.AddPermanentFlat(permanentFlat);
            }

            BigDouble permanentMult = SavedValue(saved.PermanentMultBonusBig, saved.PermanentMultBonus);
            if (permanentMult > BigDouble.Zero && BigDouble.Abs(permanentMult - 1d) > 0.0001d)
            {
                dish.MultiplyPermanentMult(permanentMult);
            }

            BigDouble temporaryBase = SavedValue(saved.TemporaryBaseMultiplierBig, saved.TemporaryBaseMultiplier);
            if (temporaryBase > BigDouble.Zero && BigDouble.Abs(temporaryBase - 1d) > 0.0001d)
            {
                dish.MultiplyTemporaryBase(temporaryBase);
            }

            BigDouble serveMult = SavedValue(saved.ServeMultiplierBig, saved.ServeMultiplier);
            if (serveMult > BigDouble.Zero && BigDouble.Abs(serveMult - 1d) > 0.0001d)
            {
                dish.MultiplyServeMultiplier(serveMult);
            }

            BigDouble serveFlat = SavedValue(saved.ServeMultiplierFlatBonusBig, saved.ServeMultiplierFlatBonus);
            if (BigDouble.Abs(serveFlat) > 0.0001d)
            {
                dish.AddServeMultiplierFlat(serveFlat);
            }

            if (saved.SkillsDisabled)
            {
                dish.DisableSkills();
            }

            if (saved.ExcludedFromScore)
            {
                dish.ExcludeFromScore();
            }

            if (saved.IsTemporary)
            {
                dish.MarkTemporary();
            }
        }

        public void HideResultPanel()
        {
            if (GameApp.UI.HasUIForm(UIForms.Result))
            {
                var form = GameApp.UI.GetUIForm(UIForms.Result);
                if (form != null)
                {
                    GameApp.UI.CloseUIForm(form);
                }
            }
        }

        /// <summary>周循环请求「n 选一行动」：在常驻壳中部就地展示行动选择。</summary>
        public void OpenWeekMap()
        {
            ShowActionSelection();
        }

        /// <summary>周循环请求商店：在常驻壳中部就地展示商店四区。</summary>
        public void OpenShop()
        {
            SwitchTo(GameplayView.Shop);
        }

        public void OpenRewardForm(RewardFormOpenArgs args)
        {
            GameApp.UI.OpenUIForm(UIForms.Reward, UIForms.GroupDialog, args);
        }

        /// <summary>时间轴节点卡片：先展示节点卡，玩家点击后再执行节点效果。</summary>
        public void ShowTimelineNodeCard(cfg.TimelineNode node, int? interestMaxGain, Action onPick)
        {
            TrackTimelineNodeCard(node, interestMaxGain, onPick);
            SwitchTo(GameplayView.ActionSelect, () =>
            {
                BuildTimelineNodeCard(node, interestMaxGain, onPick);
            }, () =>
            {
                PlayShowCardsWhenReady(() =>
                {
                    if (_run != null
                        && _run.IsTutorialRun
                        && !TutorialProgressService.IsCompleted(TutorialId.CoreComplete)
                        && TutorialProgressService.IsCompleted(TutorialId.FirstAction))
                        TutorialRuntime.Play(TutorialId.TimelineNode);
                });
            });
        }

        public void DismissTimelineNodeCard(Action onDone)
        {
            if (_deck == null)
            {
                ClearTimelineNodeCard();
                onDone?.Invoke();
                return;
            }

            _deck.HideThenDestroy(() =>
            {
                ClearTimelineNodeCard();
                onDone?.Invoke();
            });
        }

        public void ShowTimelineNodeSkipped(
            cfg.TimelineNode node,
            TimelineMutationResult result,
            Action onDone)
        {
            bool continued = false;
            void ContinueOnce()
            {
                if (continued)
                {
                    return;
                }

                continued = true;
                onDone?.Invoke();
            }

            QueueTimelineMutation(result, ContinueOnce, () => continued = true);
        }

        public void PlayTimelineAdvance(
            float fromDay,
            float toDay,
            string arrivingNodeId,
            Action onDone)
        {
            if (_axisBinder == null || _run == null)
            {
                onDone?.Invoke();
                return;
            }

            SetActionAxisVisible(true);
            void PlayAdvance()
            {
                _axisBinder.PlayAdvance(
                    _run,
                    fromDay,
                    toDay,
                    arrivingNodeId,
                    () =>
                    {
                        if (_timelineAxisFocus != null)
                        {
                            _timelineAxisFocus.Exit(onDone);
                        }
                        else
                        {
                            onDone?.Invoke();
                        }
                    });
            }

            if (_timelineAxisFocus?.CanPresent == true)
            {
                _timelineAxisFocus.Enter(PlayAdvance);
            }
            else
            {
                _axisBinder.PlayAdvance(_run, fromDay, toDay, arrivingNodeId, onDone);
            }
        }

        public void BeginTimelineAdvanceSequence(float fromDay)
        {
            _axisBinder?.BeginAdvanceSequence(fromDay);
        }

        public void EndTimelineAdvanceSequence()
        {
            _axisBinder?.EndAdvanceSequence();
        }

        public void PlayTimelineWeekTransition(Action onDone)
        {
            SetActionAxisVisible(true);
            if (_axisBinder == null || _run == null)
            {
                onDone?.Invoke();
                return;
            }

            void ReplaceHiddenTimeline()
            {
                _axisBinder.EndAdvanceSequence();
                _axisBinder.Rebuild(_run);
            }

            if (_timelineAxisFocus?.CanPresent != true)
            {
                ReplaceHiddenTimeline();
                onDone?.Invoke();
                return;
            }

            _timelineAxisFocus.Enter(() =>
                _timelineAxisFocus.SwapWeekContent(
                    _run.WeekIndex,
                    ReplaceHiddenTimeline,
                    () => _timelineAxisFocus.Exit(onDone)));
        }

        /// <summary>
        /// 经营挑战领奖完成后先退出世界态并清空中部卡片，再允许周循环推进时间轴。
        /// 这样时间轴聚焦演出不会被强制叠到仍可见的战斗画面上。
        /// </summary>
        private void ExitBattleBeforeTimelinePresentation(Action onExited)
        {
            ClearTimelineNodeCard();
            _deck?.Clear();
            _deck?.SetCardsActive(false);

            if (_pageRouter == null)
            {
                HideBattleWorld();
                onExited?.Invoke();
                return;
            }

            SwitchTo(
                GameplayView.ActionSelect,
                () =>
                {
                    ClearTimelineNodeCard();
                    _deck?.Clear();
                    _deck?.SetCardsActive(false);
                },
                onExited);
        }

        public void PlayTimelineNodeCue(
            string nodeId,
            TimelinePresentationCueKind kind,
            Action onDone)
        {
            if (_axisBinder == null || _run == null || string.IsNullOrEmpty(nodeId))
            {
                onDone?.Invoke();
                return;
            }

            SetActionAxisVisible(true);
            _axisBinder.PlayNodeCue(_run, nodeId, kind, onDone);
        }

        public void ShowPassiveRecipeMutation(RecipeMutationResult result)
        {
            ShowRecipeMutation(result, null);
        }

        public void ShowEventRecipeMutation(
            RecipeMutationResult result,
            Action onComplete)
        {
            if (result == null || !result.HasChanges)
            {
                onComplete?.Invoke();
                return;
            }

            ShowRecipeMutation(result, onComplete);
        }

        private void ShowRecipeMutation(RecipeMutationResult result, Action onComplete)
        {
            if (result == null || !result.HasChanges)
            {
                onComplete?.Invoke();
                return;
            }

            switch (result.Presentation)
            {
                case RecipeMutationPresentation.CopyFly:
                    ShowPassiveRecipeCopyMutation(result, onComplete);
                    return;
                case RecipeMutationPresentation.FlavorStage:
                    ShowFlavorMutation(result, onComplete);
                    return;
                default:
                    ShowPassiveRecipeBookMutation(result, onComplete);
                    return;
            }
        }

        private void ShowPassiveRecipeBookMutation(RecipeMutationResult result, Action onComplete)
        {
            IReadOnlyList<RecipeReadonlyDishEntry> beforeEntries = BuildRecipeMutationEntries(result, before: true);
            IReadOnlyList<RecipeReadonlyDishEntry> afterEntries = BuildRecipeMutationEntries(result, before: false);
            bool closeAfterMutation = result.OnlyRemovesDishes;
            BattleInspectionView restoreView = closeAfterMutation
                ? BattleInspectionView.None
                : ActiveInspectionView;
            DiningTable restoreTable = restoreView == BattleInspectionView.Table
                ? BuildTablePresentationSnapshot()
                : null;

            bool completed = false;
            void CompleteOnce()
            {
                if (completed)
                {
                    return;
                }

                completed = true;
                onComplete?.Invoke();
            }

            bool cancelled = false;
            EnqueuePassivePresentation(done =>
            {
                void Finish()
                {
                    done();
                    CompleteOnce();
                }

                void RestoreAfterMutation()
                {
                    void RestoreNow() => RestorePassiveInspection(
                        restoreView,
                        afterEntries,
                        restoreTable,
                        Finish);

                    if (closeAfterMutation)
                    {
                        RestoreNow();
                    }
                    else
                    {
                        WaitPassiveHold(RestoreNow);
                    }
                }

                bool opened = _inspectionCoordinator?.ShowPassiveRecipe(
                    beforeEntries,
                    () =>
                    {
                        if (cancelled)
                        {
                            Finish();
                            return;
                        }

                        RecipeReadonlyBookView recipe = _inspectionLayer?.RecipeView;
                        DelayPassivePresentation(1f, () =>
                        {
                            if (cancelled)
                            {
                                Finish();
                                return;
                            }

                            if (recipe == null)
                            {
                                RestoreAfterMutation();
                                return;
                            }

                            recipe.PlayPassiveMutation(result, afterEntries, () =>
                            {
                                if (cancelled)
                                {
                                    Finish();
                                    return;
                                }

                                RestoreAfterMutation();
                            });
                        });
                    },
                    hideExitButton: true) == true;

                if (!opened)
                {
                    RefreshPersistent();
                    Finish();
                }
            }, () =>
            {
                cancelled = true;
                _inspectionCoordinator?.ForceClose();
                CompleteOnce();
            });
        }

        private void ShowPassiveRecipeCopyMutation(RecipeMutationResult result, Action onComplete)
        {
            bool completed = false;
            void CompleteOnce()
            {
                if (completed)
                {
                    return;
                }

                completed = true;
                onComplete?.Invoke();
            }

            EnqueuePassivePresentation(done =>
            {
                Canvas canvas = GetComponentInParent<Canvas>();
                RectTransform layer = canvas != null
                    ? canvas.transform as RectTransform
                    : transform.root as RectTransform;
                RectTransform target = _infoColumn?.ViewRecipeButtonRect;
                var dishIds = new List<string>();
                int initialRecipeCount = _run?.RecipeEntries.Count ?? 0;
                foreach (RecipeMutationEntry entry in result.Entries)
                {
                    if (entry?.After == null || string.IsNullOrEmpty(entry.After.DishId))
                    {
                        continue;
                    }

                    dishIds.Add(entry.After.DishId);
                    initialRecipeCount = Mathf.Min(initialRecipeCount, entry.DishIndex);
                }

                Canvas.ForceUpdateCanvases();
                if (layer == null
                    || target == null
                    || dishIds.Count == 0
                    || _run == null
                    || GameApp.Random == null
                    || !TryGetRectInLayer(target, layer, out RectSnapshot targetRect))
                {
                    FinishPassiveRecipeCopyMutation(() =>
                    {
                        done();
                        CompleteOnce();
                    });
                    return;
                }

                _infoColumn.SetRecipeCountPresentationOverride(initialRecipeCount);
                RefreshPersistent();
                IRandomStream cosmetic = GameApp.Random.Cosmetic(
                    $"passive_copy_food_w{_run.WeekIndex}_s{_run.RunActionStepIndex}_c{initialRecipeCount}");
                bool started = PlayRecipeCopyFlys(
                    dishIds,
                    layer,
                    _center != null ? _center.transform as RectTransform : null,
                    targetRect.Center,
                    cosmetic,
                    false,
                    arrived =>
                    {
                        _infoColumn?.SetRecipeCountPresentationOverride(
                            initialRecipeCount + arrived);
                        RefreshPersistent();
                    },
                    () => FinishPassiveRecipeCopyMutation(() =>
                    {
                        done();
                        CompleteOnce();
                    }),
                    _passiveRecipeCopyFlys);
                if (!started)
                {
                    FinishPassiveRecipeCopyMutation(() =>
                    {
                        done();
                        CompleteOnce();
                    });
                }
            }, () =>
            {
                CancelPassiveRecipeCopyMutation();
                CompleteOnce();
            });
        }

        private void FinishPassiveRecipeCopyMutation(Action onComplete)
        {
            _infoColumn?.SetRecipeCountPresentationOverride(null);
            RefreshPersistent();
            onComplete?.Invoke();
        }

        private void CancelPassiveRecipeCopyMutation()
        {
            CancelRecipeCopyFlys(_passiveRecipeCopyFlys);
            _infoColumn?.SetRecipeCountPresentationOverride(null);
            RefreshPersistent();
        }

        private void ShowFlavorMutation(
            RecipeMutationResult result,
            Action onComplete)
        {
            bool completed = false;
            void CompleteOnce()
            {
                if (completed)
                {
                    return;
                }

                completed = true;
                onComplete?.Invoke();
            }

            EnqueuePassivePresentation(done =>
            {
                void Finish()
                {
                    done();
                    CompleteOnce();
                }

                if (!OpenPassiveFlavorMutationPage(result))
                {
                    ClosePassiveFlavorMutationPage();
                    RefreshPersistent();
                    Finish();
                    return;
                }

                DelayPassivePresentation(1f, () =>
                    PlayPassiveFlavorTransforms(() =>
                        WaitPassiveHold(() =>
                            FlyPassiveFlavorDishesToRecipe(Finish))));
            }, () =>
            {
                ClosePassiveFlavorMutationPage();
                CompleteOnce();
            });
        }

        public void ShowPassiveTimelineMutation(TimelineMutationResult result)
        {
            if (result == null || !result.Changed)
            {
                return;
            }

            QueueTimelineMutation(result, null, null);
        }

        // —— 中部态切换中枢（淡入淡出调度 + 派发给状态机）——

        /// <summary>
        /// 切到某一中部态：只对中部内容区 <see cref="_center"/> 做 DOTween 渐隐渐显，常驻壳（左/右/时间轴）不动。
        /// 内容交换（隐藏旧面板 + 启用新面板 + 重建）集中在淡出完成后的 <see cref="GameplayViewStateMachine.Apply"/> 里执行。
        /// </summary>
        private void SwitchTo(
            GameplayView next,
            Action buildCenter = null,
            Action onShown = null,
            Func<Tween> coveredPresentation = null,
            Action<Action> bindCoveredSkip = null)
        {
            int requestId = ++_switchRequestId;
            _passivePresentations.WhenIdle(() =>
            {
                if (requestId != _switchRequestId)
                {
                    return;
                }

                _pageRouter?.SwitchTo(
                    next,
                    buildCenter,
                    () =>
                    {
                        SyncPageStateFromRouter();
                        onShown?.Invoke();
                    },
                    coveredPresentation,
                    bindCoveredSkip);
                SyncPageStateFromRouter();
            });
        }

        private void EnsureCenterTransitionCover()
        {
            if (_centerTransitionCover == null)
            {
                Transform parent = _hudFrame != null ? _hudFrame.transform : transform;
                var coverObject = new GameObject(
                    "CenterTransitionCover",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(CanvasGroup));
                RectTransform coverRect = coverObject.GetComponent<RectTransform>();
                coverRect.SetParent(parent, false);

                RectTransform centerRect = _center != null ? _center.transform as RectTransform : null;
                if (centerRect != null && centerRect.parent == parent)
                {
                    coverRect.anchorMin = centerRect.anchorMin;
                    coverRect.anchorMax = centerRect.anchorMax;
                    coverRect.anchoredPosition = centerRect.anchoredPosition;
                    coverRect.sizeDelta = centerRect.sizeDelta;
                    coverRect.pivot = centerRect.pivot;
                }
                else
                {
                    coverRect.anchorMin = new Vector2(0.165f, 0f);
                    coverRect.anchorMax = new Vector2(0.835f, 1f);
                    coverRect.anchoredPosition = Vector2.zero;
                    coverRect.sizeDelta = Vector2.zero;
                }

                _centerTransitionCover = coverObject.GetComponent<CanvasGroup>();
            }

            Image coverImage = _centerTransitionCover.GetComponent<Image>();
            if (coverImage != null)
            {
                coverImage.color = Color.black;
                coverImage.raycastTarget = true;
            }

            Transform coverTransform = _centerTransitionCover.transform;
            Transform coverParent = coverTransform.parent;
            Transform axisRoot = DirectChildUnder(coverParent, _timelineAxis != null
                ? _timelineAxis.transform
                : null);
            coverTransform.SetAsLastSibling();
            if (axisRoot != null)
            {
                coverTransform.SetSiblingIndex(axisRoot.GetSiblingIndex() + 1);
            }
            else if (_center != null && _center.transform.parent == coverParent)
            {
                coverTransform.SetSiblingIndex(_center.transform.GetSiblingIndex() + 1);
            }

            _centerTransitionCover.alpha = 0f;
            _centerTransitionCover.interactable = false;
            _centerTransitionCover.blocksRaycasts = false;
        }

        private void EnsureRewardSubflowLayer()
        {
            if (_rewardSubflowLayer == null)
            {
                Debug.LogError($"{nameof(BattleForm)} 缺少 RewardSubflowLayer prefab 引用。", this);
                return;
            }

            _rewardSubflowLayer.Initialize();
        }

        private void BeginInspectionSource()
        {
            _overlaySuspension?.Suspend(BattleOverlaySuspensionOptions.None);
            _rewardPage?.SuspendForInspection();
            if (_rewardPeekOnly)
            {
                RewardForm.Active?.SetResultPeekInspectionActive(true);
            }

            // RewardForm 位于 Dialog 组，会覆盖 BattleForm 内的查看层。装饰品自动触发的
            // 菜谱/餐桌表现也需要挂起奖励页，并由 RestoreInspectionSource 统一恢复。
            RewardForm reward = RewardForm.Active;
            if (reward != null
                && !reward.IsSuspendedForRewardSubflow
                && !reward.IsPersistentInspectionActive)
            {
                reward.TryBeginPersistentInspection();
            }
        }

        private void RestoreInspectionSource(GameplayView sourceView)
        {
            _overlaySuspension?.Restore();
            _rewardPage?.ResumeFromInspection();
            if (_rewardPeekOnly)
            {
                RewardForm.Active?.SetResultPeekInspectionActive(false);
            }

            RewardForm.Active?.CompletePersistentInspection();
        }

        private void OnPageCovered(GameplayView current, GameplayView next)
        {
            if (current == next || !GameplayPageRouter.IsWorldView(current))
            {
                return;
            }

            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if (world == null)
            {
                return;
            }

            if (current == GameplayView.Food && !PreservesFoodWorld(next))
            {
                HideBattleWorld();
                return;
            }

            world.SuspendWorld();
        }

        private static Transform DirectChildUnder(Transform parent, Transform descendant)
        {
            if (parent == null || descendant == null)
            {
                return null;
            }

            Transform current = descendant;
            while (current.parent != null && current.parent != parent)
            {
                current = current.parent;
            }

            return current.parent == parent ? current : null;
        }

        private static bool PreservesFoodWorld(GameplayView next)
        {
            return next == GameplayView.Food;
        }

        private void SyncPageStateFromRouter()
        {
            if (_pageRouter == null)
            {
                return;
            }

            _current = _pageRouter.Current;
            _inBattle = _pageRouter.InBattle;
        }

        /// <summary>商店态：打开商店四区面板并接线各回调（离开/刷新/删除食物/餐桌编辑）。</summary>
        private void OpenShopPanel()
        {
            _shopPage?.OpenPanel();
        }

        /// <summary>食谱页面：打开查看、删除或消耗品选菜流程。</summary>
        private void OpenRecipeBookPanel()
        {
            _recipeBookPage?.OpenPanel();
        }

        // —— Page router / coordinators host implementations ——

        GameRun IGameplayPageRouterHost.Run => _run;
        BattleWorldController IGameplayPageRouterHost.World => _world ?? BattleWorldController.Instance;
        CanvasGroup IGameplayPageRouterHost.Center => _center;
        CanvasGroup IGameplayPageRouterHost.CenterTransitionCover => _centerTransitionCover;
        GameplayTransitionSettings IGameplayPageRouterHost.TransitionSettings => _pageTransitionSettings;
        GameObject IGameplayPageRouterHost.HudFrame => _hudFrame;
        GameObject IGameplayPageRouterHost.Backdrop => _backdrop;
        GameObject IGameplayPageRouterHost.ActionSelectionPanel => _actionSelectionPanel;
        ActionCardDeck IGameplayPageRouterHost.Deck => _deck;
        ShopForm IGameplayPageRouterHost.ShopPanel => _shopPanel;
        RecipeReadonlyBookView IGameplayPageRouterHost.RecipeReadonlyBookView => _recipeReadonlyBookView;
        EventPagePanel IGameplayPageRouterHost.EventPagePanel => _eventPagePanel;
        bool IGameplayPageRouterHost.ActionAxisVisible => _timelineAxis != null && _timelineAxis.gameObject.activeSelf;
        void IGameplayPageRouterHost.OnLeavingPage(GameplayView current, GameplayView next)
        {
            CancelPassivePresentations();
            _inspectionCoordinator?.ForceClose();
            if (current != next)
            {
                _fragmentEditCoordinator?.ForceClose();
            }
        }
        void IGameplayPageRouterHost.OnPageCovered(GameplayView current, GameplayView next) => OnPageCovered(current, next);
        void IGameplayPageRouterHost.OnBeforeApplyPage(GameplayView view)
        {
            if (view != GameplayView.Food)
            {
                HideFoodTips();
            }
        }

        void IGameplayPageRouterHost.SetActionAxisVisible(bool visible) => SetActionAxisVisible(visible);
        void IGameplayPageRouterHost.SetFoodBattlePanelVisible(bool visible) => SetFoodBattlePanelVisible(visible);
        void IGameplayPageRouterHost.RebuildActionAxis() => RebuildActionAxis();
        void IGameplayPageRouterHost.OpenShopPanel() => _shopPage?.OpenPanel();
        void IGameplayPageRouterHost.OpenRecipeBookPanel() => _recipeBookPage?.OpenPanel();
        void IGameplayPageRouterHost.BuildBattleControls() => BuildBattleControls();
        void IGameplayPageRouterHost.BuildActionCards() => BuildActionCards();
        void IGameplayPageRouterHost.RefreshPersistent() => RefreshPersistent();

        GameRun IRecipeBookHost.Run => _run;
        GameplayView IRecipeBookHost.CurrentView => _current;
        RecipeReadonlyBookView IRecipeBookHost.RecipeReadonlyBookView => _recipeReadonlyBookView;
        void IRecipeBookHost.SwitchTo(GameplayView view, Action buildCenter, Action onShown) => SwitchTo(view, buildCenter, onShown);
        void IRecipeBookHost.RefreshShopPersistent() => RefreshShopPersistent();
        FoodTipsView IRecipeBookHost.FoodTips() => _tips != null ? _tips.Food : null;

        GameRun IShopPageHost.Run => _run;
        ShopForm IShopPageHost.ShopPanel => _shopPanel;
        bool IShopPageHost.ShouldRefreshItemsAfterShopChange => _shopItemFlyInFlight <= 0;
        void IShopPageHost.OnShopClosed() => OnShopClosed();
        /// <summary>
        /// RewardForm 盖在 Dialog 组上。Battle 里的被动演出和领奖子页都在 Default 组，
        /// 领取后必须等这些层结束，RewardForm 才能重新显示或关闭。
        /// </summary>
        internal bool HasRewardCoveredPresentation =>
            _passivePresentations.IsBusy || (_rewardPage != null && _rewardPage.IsActive);

        internal void WhenRewardCoveredPresentationsIdle(Action onIdle)
        {
            if (onIdle == null)
            {
                return;
            }

            void Continue()
            {
                if (_passivePresentations.IsBusy)
                {
                    _passivePresentations.WhenIdle(Continue);
                    return;
                }

                if (_rewardPage != null && _rewardPage.IsActive)
                {
                    _rewardPage.WhenIdle(Continue);
                    return;
                }

                onIdle();
            }

            Continue();
        }

        void IShopPageHost.WhenPassivePresentationsIdle(Action onIdle) =>
            _passivePresentations.WhenIdle(onIdle);
        void IShopPageHost.RefreshPersistent(bool refreshItems) => RefreshPersistent(refreshItems);
        void IShopPageHost.CompleteTutorialAcquiredItemPresentation() =>
            CompleteTutorialAcquiredItemPresentation();
        void IShopPageHost.OpenDeleteDish() => _recipeBookPage?.OpenShopDelete();
        void IShopPageHost.OpenTableEdit(Action onShown) => OpenTableEdit(onShown);
        void IShopPageHost.OpenRecipeInspect(int bookIndex) => OpenRecipeInspect(bookIndex);
        PreparedShopPurchaseAnimation IShopPageHost.PrepareShopPurchaseAnimation(
            ShopEntry entry,
            ShopBuyItemViewBase sourceCard) => PrepareShopPurchaseAnimation(entry, sourceCard);

        GameRun IRewardPageHost.Run => _run;
        RewardDishPackPanel IRewardPageHost.RewardDishPackPanel => _rewardDishPackPanel;
        RewardItemChoicePanel IRewardPageHost.RewardItemChoicePanel => _rewardItemChoicePanel;
        RandomizedItemsPanel IRewardPageHost.RandomizedItemsPanel => _randomizedItemsPanel;
        void IRewardPageHost.PrepareRewardSubflowLayer()
        {
            EnsureRewardSubflowLayer();
            _rewardSubflowLayer?.Prepare();
        }
        void IRewardPageHost.ShowRewardSubflowPanel(Component panel) => _rewardSubflowLayer?.Show(panel);
        void IRewardPageHost.HideRewardSubflowPanel(Component panel) => _rewardSubflowLayer?.Hide(panel);
        void IRewardPageHost.HideRewardSubflowLayer() => _rewardSubflowLayer?.HideImmediate();
        void IRewardPageHost.NotifyRewardSubflowLifecycle(RewardSubflowLifecycle lifecycle)
        {
            switch (lifecycle)
            {
                case RewardSubflowLifecycle.PreparingChild:
                    PreparingChild?.Invoke();
                    break;
                case RewardSubflowLifecycle.ChildReady:
                    ChildReady?.Invoke();
                    break;
                case RewardSubflowLifecycle.PreparingReturn:
                    PreparingReturn?.Invoke();
                    break;
                case RewardSubflowLifecycle.ParentRestored:
                    ParentRestored?.Invoke();
                    break;
            }
        }
        void IRewardPageHost.RefreshPersistent() => RefreshPersistent();
        FoodTipsView IRewardPageHost.FoodTips() => _tips != null ? _tips.Food : null;
        ItemTipView IRewardPageHost.ItemTips() => _tips != null ? _tips.Item : null;
        void IRewardPageHost.PlayRewardDishSelectionFly(RewardDishChoiceCardView sourceCard) =>
            PlayRewardDishSelectionFly(sourceCard);
        Action IRewardPageHost.PrepareRewardItemSelectionFly(
            RewardChoice choice,
            cfg.ItemKind kind,
            RewardItemChoiceCardView sourceCard) =>
            PrepareRewardItemSelectionFly(choice, kind, sourceCard);
        void IRewardPageHost.PlayRandomizedItemFlys(IReadOnlyList<RandomizedItemResult> results) => PlayRandomizedItemFlys(results);

        GameplayView IEventPageHost.CurrentView => _current;
        EventPagePanel IEventPageHost.EventPagePanel => _eventPagePanel;
        void IEventPageHost.SwitchTo(GameplayView view, Action buildCenter, Action onShown) => SwitchTo(view, buildCenter, onShown);
        void IEventPageHost.OpenEventRecipeDishDelete(
            GameRun run,
            string title,
            Action onCancel,
            Action<ActiveTarget> onTargetConfirmed,
            Action onChanged) => _recipeBookPage?.OpenEventDelete(run, title, onCancel, onTargetConfirmed, onChanged);

        // —— IBattleInspectionHost（独立只读查看层）——

        GameRun IBattleInspectionHost.Run => _run;
        BattleSession IBattleInspectionHost.Session => _session;
        GameplayView IBattleInspectionHost.CurrentView => _current;
        BattleWorldController IBattleInspectionHost.World => _world ?? BattleWorldController.Instance;
        IBattleInspectionLayer IBattleInspectionHost.InspectionLayer => _inspectionLayer;
        bool IBattleInspectionHost.ActionAxisVisible => _timelineAxis != null && _timelineAxis.gameObject.activeSelf;
        void IBattleInspectionHost.SetActionAxisVisible(bool visible) => SetActionAxisVisible(visible);
        void IBattleInspectionHost.BeginInspectionSource() => BeginInspectionSource();
        void IBattleInspectionHost.RestoreInspectionSource(GameplayView sourceView) => RestoreInspectionSource(sourceView);
        void IBattleInspectionHost.RestoreBattleWorld() => RestoreBattleWorld();
        void IBattleInspectionHost.BindWorldHoverCallbacks() => BindWorldHoverCallbacks();
        void IBattleInspectionHost.RefreshPersistent() => RefreshPersistent();
        FoodTipsView IBattleInspectionHost.FoodTips() => _tips != null ? _tips.Food : null;

        // —— 独立覆盖层共享来源冻结 ——

        CanvasGroup IBattleOverlaySuspensionHost.Center => _center;
        CanvasGroup IBattleOverlaySuspensionHost.ActionAxisGroup => ResolveActionAxisGroup();
        GameObject IBattleOverlaySuspensionHost.Backdrop => _backdrop;
        GameObject IBattleOverlaySuspensionHost.FoodBattlePanel => _foodBattlePanel;
        BattleFoodActionBar IBattleOverlaySuspensionHost.FoodActionBar => _foodBar;
        ServingOutletView IBattleOverlaySuspensionHost.ResolveServingOutlet() => ResolveServingOutlet();
        FoodDiscardBinView IBattleOverlaySuspensionHost.ResolveFoodDiscardBin() => ResolveFoodDiscardBin();
        void IBattleOverlaySuspensionHost.SetFoodBattlePanelVisible(bool visible) => SetFoodBattlePanelVisible(visible);
        void IBattleOverlaySuspensionHost.HideAllTips() => HideAllTips();

        // —— 餐桌碎片独立编辑层 ——

        GameRun IBattleTableFragmentEditHost.Run => _run;
        GameplayView IBattleTableFragmentEditHost.CurrentView => _current;
        IBattleTableFragmentEditLayer IBattleTableFragmentEditHost.FragmentEditLayer => _fragmentEditLayer;
        bool IBattleTableFragmentEditHost.CanInteract => !_rewardPeekOnly;
        bool IBattleTableFragmentEditHost.CanOpenFragmentEdit =>
            (_world ?? BattleWorldController.Instance) != null;
        IReadOnlyList<int> IBattleTableFragmentEditHost.CandidateRotations =>
            _run?.PendingFragmentPackRotations;
        void IBattleTableFragmentEditHost.ForceCloseInspection() => _inspectionCoordinator?.ForceClose();
        void IBattleTableFragmentEditHost.CancelActiveItemUse() => _activeItemUse?.Dispose();
        bool IBattleTableFragmentEditHost.SuspendFragmentEditSource() =>
            _overlaySuspension != null
            && _overlaySuspension.Suspend(
                BattleOverlaySuspensionOptions.HideActionAxis
                | BattleOverlaySuspensionOptions.HideBackdrop);
        void IBattleTableFragmentEditHost.RestoreFragmentEditSource() => _overlaySuspension?.Restore();
        void IBattleTableFragmentEditHost.RestoreBattleWorld() => RestoreBattleWorld();
        void IBattleTableFragmentEditHost.BeginTableFragmentChoice(TableFragmentChoiceRequest request)
        {
            _world = _world ?? BattleWorldController.Instance;
            BindWorldHudAreas();
            _world?.BeginTableFragmentChoice(request);
            BindWorldHoverCallbacks();
        }
        void IBattleTableFragmentEditHost.ConfirmTableEditPlacement() =>
            (_world ?? BattleWorldController.Instance)?.ConfirmTableEditPlacement();
        void IBattleTableFragmentEditHost.SkipTableEditPack() =>
            (_world ?? BattleWorldController.Instance)?.SkipTableEditPack();
        void IBattleTableFragmentEditHost.HideTableEditWorld() =>
            (_world ?? BattleWorldController.Instance)?.HideWorld();
        void IBattleTableFragmentEditHost.SetInspectionNavigationBlocked(bool blocked) =>
            _infoColumn?.SetInspectionNavigationBlocked(blocked);
        bool IBattleTableFragmentEditHost.OpenRecipeInspection(Action onClosed) =>
            _inspectionCoordinator?.OpenRecipeFromOverlay(0, onClosed) == true;
        void IBattleTableFragmentEditHost.RefreshPersistent() => RefreshPersistent();
        void IBattleTableFragmentEditHost.NotifyPreparingChild() => PreparingChild?.Invoke();
        void IBattleTableFragmentEditHost.NotifyChildReady() => ChildReady?.Invoke();
        void IBattleTableFragmentEditHost.NotifyPreparingReturn() => PreparingReturn?.Invoke();
        void IBattleTableFragmentEditHost.NotifyParentRestored() => ParentRestored?.Invoke();

        // —— 行动选择态（含事件 n 选一，共用中部卡片）——

        private void ShowActionSelection(Action onShown = null)
        {
            ClearTimelineNodeCard();
            SwitchTo(GameplayView.ActionSelect, () =>
            {
                BuildActionCards();
            }, () =>
            {
                PlayShowCardsWhenReady(PlayActionSelectionTutorialIfNeeded);
                onShown?.Invoke();
            });
        }

        /// <summary>事件页：专用中部面板，展示事件环境、正文、选项或结果结束按钮。</summary>
        public void ShowEventPage(
            string title,
            string desc,
            string resultButtonText,
            string bgSprite,
            IReadOnlyList<string> options,
            IReadOnlyList<string> optionRequirements,
            IReadOnlyList<bool> optionEnabled,
            Action<int> onPick,
            Action onEnd)
        {
            _eventPage?.Show(new EventPageRequest(
                title,
                desc,
                resultButtonText,
                bgSprite,
                options,
                optionRequirements,
                optionEnabled,
                onPick,
                onEnd));
        }

        public void ShowEventEffectFeedback(
            string title,
            string message,
            string bgSprite,
            Action onComplete)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                onComplete?.Invoke();
                return;
            }

            if (_eventPage == null)
            {
                onComplete?.Invoke();
                return;
            }

            _eventPage.Show(new EventPageRequest(
                title,
                message,
                string.Empty,
                bgSprite,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<bool>(),
                onPick: null,
                onEnd: onComplete,
                autoContinueDelaySeconds: 0.5f));
        }

        public void ExitEventPage(Action onExited)
        {
            if (_current != GameplayView.Event)
            {
                onExited?.Invoke();
                return;
            }

            ClearTimelineNodeCard();
            _deck?.Clear();
            _deck?.SetCardsActive(false);
            SwitchTo(
                GameplayView.ActionSelect,
                () =>
                {
                    ClearTimelineNodeCard();
                    _deck?.Clear();
                    _deck?.SetCardsActive(false);
                },
                onExited);
        }

        public void ShowDirectPassiveItemAcquire(
            ItemDefinition item,
            ItemAcquireResult acquireResult,
            Action onComplete)
        {
            // 金币在事件结算阶段已经到账；这里只刷新常驻数值，不重建物品栏。
            RefreshPersistent(refreshItems: false);

            bool requestCompleted = false;
            void CompleteRequest()
            {
                if (requestCompleted)
                {
                    return;
                }

                requestCompleted = true;
                onComplete?.Invoke();
            }

            if (item == null || acquireResult.Outcome != ItemAcquireOutcome.Added)
            {
                CompleteTutorialAcquiredItemPresentation();
                CompleteRequest();
                return;
            }

            ShopPurchaseFlyView activeFly = null;
            BattleItemsColumn.PassiveAcquirePlan acquirePlan = null;
            EnqueuePassivePresentation(done =>
            {
                Canvas canvas = GetComponentInParent<Canvas>();
                RectTransform layer = canvas != null
                    ? canvas.transform as RectTransform
                    : transform.root as RectTransform;
                if (layer == null
                    || _itemsColumn == null
                    || !_itemsColumn.TryPreparePassiveAcquire(
                        _run,
                        item,
                        layer,
                        _tips != null ? _tips.Item : null,
                        ShowItemInfo,
                        out acquirePlan))
                {
                    CompleteTutorialAcquiredItemPresentation();
                    done();
                    CompleteRequest();
                    return;
                }

                TryRegisterTutorialAcquiredItemAnchor();
                activeFly = CreateShopPurchaseFly(layer);
                if (activeFly == null)
                {
                    acquirePlan.Complete();
                    CompleteTutorialAcquiredItemPresentation();
                    done();
                    CompleteRequest();
                    return;
                }

                RegisterShopPurchaseFly(activeFly);
                ShopPurchaseFlyView capturedFly = activeFly;
                bool presentationFinished = false;
                void FinishPresentation()
                {
                    if (presentationFinished)
                    {
                        return;
                    }

                    presentationFinished = true;
                    acquirePlan.Complete();
                    CompleteTutorialAcquiredItemPresentation();
                    UnregisterShopPurchaseFly(capturedFly);
                    if (ReferenceEquals(activeFly, capturedFly))
                    {
                        activeFly = null;
                    }

                    done();
                    CompleteRequest();
                }

                Sprite sprite = RunItemSlotView.LoadIcon(item) ?? LoadShopItemFallbackIcon(item.Kind);
                Vector2 startSize = acquirePlan.TargetSize;
                try
                {
                    capturedFly.PlayPassiveAcquire(
                        layer.rect.center,
                        startSize,
                        acquirePlan.TargetCenter,
                        acquirePlan.TargetSize,
                        sprite,
                        RunItemSlotView.QualityColor(item.Quality),
                        acquirePlan.ApplyScroll,
                        () =>
                        {
                            acquirePlan.Complete();
                            CompleteTutorialAcquiredItemPresentation();
                        },
                        FinishPresentation);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, capturedFly);
                    capturedFly.Cancel();
                    FinishPresentation();
                }
            }, cancel: () =>
            {
                acquirePlan?.Complete();
                if (activeFly != null)
                {
                    activeFly.Cancel();
                }
                else
                {
                    CompleteTutorialAcquiredItemPresentation();
                    CompleteRequest();
                }
            });
        }

        public void OpenEventRecipeDishDelete(
            GameRun run,
            string title,
            Action onCancel,
            Action<ActiveTarget> onTargetConfirmed,
            Action onChanged)
        {
            _eventPage?.OpenRecipeDishDelete(run, title, onCancel, onTargetConfirmed, onChanged);
        }

        // —— 商店 / 食谱工作区 / 餐桌编辑入口 ——

        internal void OpenActiveItemRecipeTarget(
            ItemDefinition item,
            Action onCancel,
            Action<ActiveTarget, Action> onTargetConfirmed,
            Action onOpened = null)
        {
            _recipeBookPage?.OpenActiveItemTarget(item, onCancel, onTargetConfirmed, onOpened);
        }

        private bool OpenRecipeInspect(int bookIndex, bool useBattleRecipe = false)
        {
            if (_fragmentEditCoordinator?.IsActive == true)
            {
                _fragmentEditCoordinator.OpenRecipe();
                return true;
            }

            return _inspectionCoordinator?.OpenRecipe(bookIndex, useBattleRecipe) == true;
        }

        internal void CancelActiveItemRecipeTarget()
        {
            _recipeBookPage?.CancelActiveItemTarget();
        }

        internal bool BeginActiveItemTimelineAxisTarget(
            ItemDefinition item,
            IReadOnlyList<ActiveTarget> targets,
            Action<ActiveTarget> onConfirm,
            Action onCancel)
        {
            SetActionAxisVisible(true);
            bool opened = _axisBinder != null
                && _axisBinder.BeginActiveItemTargeting(
                    _run,
                    _currentTimelineNodeCard?.Id,
                    item,
                    targets,
                    onConfirm,
                    onCancel);
            if (opened)
            {
                HideAllTips();
            }

            return opened;
        }

        internal void EndActiveItemTimelineAxisTarget()
        {
            _axisBinder?.EndActiveItemTargeting();
        }

        internal bool CommitActiveItemTimelineAxisPreview(string nodeId)
        {
            return _axisBinder != null && _axisBinder.CommitActiveItemAddPreview(nodeId);
        }

        internal bool PlayActiveItemRecipeFlavorApplied(ActiveTarget target, Action onComplete)
        {
            return _recipeBookPage != null
                && _recipeBookPage.PlayActiveItemRecipeFlavorApplied(target, onComplete);
        }

        internal bool TryServingOutletDishTarget(Vector2 screenPoint, out ActiveTarget target)
        {
            target = default;
            DishInstance dish = _session?.PreparedServe?.Dish;
            ServingOutletView outlet = ResolveServingOutlet();
            if (dish == null || outlet?.IsPreparedDishAtScreenPoint(screenPoint) != true)
            {
                return false;
            }

            GridPos origin = dish.Placement.Origin;
            target = new ActiveTarget(
                dish.Id.ToString(),
                origin.X,
                origin.Y,
                cfg.ItemTargetKind.DiningTableDish);
            return true;
        }

        internal bool IsServingOutletDishTarget(ActiveTarget target)
        {
            return target.TargetKind == cfg.ItemTargetKind.DiningTableDish
                && int.TryParse(target.Id, out int dishId)
                && _session?.PreparedServe?.Dish?.Id == dishId;
        }

        internal void SetServingOutletActiveItemTargeting(bool active)
        {
            ResolveServingOutlet()?.SetActiveItemTargeting(active);
        }

        internal void SetServingOutletActiveItemTargetHighlighted(bool highlighted)
        {
            ResolveServingOutlet()?.SetActiveItemTargetHighlighted(highlighted);
        }

        internal bool PlayActiveItemServingOutletFlavorApplied(
            ActiveTarget target,
            Action onComplete)
        {
            if (!IsServingOutletDishTarget(target))
            {
                return false;
            }

            return ResolveServingOutlet()?.PlayActiveItemFlavorApplied(
                _session.PreparedServe.Dish,
                onComplete) == true;
        }

        internal bool OpenActiveItemTableCellTarget(Action onOpened)
        {
            if (_inspectionCoordinator == null || _fragmentEditCoordinator?.IsActive == true)
            {
                return false;
            }

            return _inspectionCoordinator.OpenTableTargeting(onOpened);
        }

        internal void CloseActiveItemTableCellTarget()
        {
            if (_inspectionCoordinator != null && _inspectionCoordinator.IsTableTargeting)
            {
                _inspectionCoordinator.Close();
            }
        }

        /// <summary>购买碎片包后进入餐桌编辑态（世界空间）：隐藏商店/时间轴，露出菜桌手动拼贴。</summary>
        private void OpenTableEdit(Action onShown = null)
        {
            if (_run == null || _run.PendingFragmentPack.Count == 0)
            {
                onShown?.Invoke();
                return;
            }

            OpenTableFragmentChoice(_run.PendingFragmentPack, null, onShown);
        }

        public bool OpenRewardTableEdit(Action<bool> onDone)
        {
            return OpenRewardTableEdit(_run != null ? _run.PendingFragmentPack : null, onDone);
        }

        public bool OpenRewardTableEdit(IReadOnlyList<string> candidateIds, Action<bool> onDone)
        {
            return OpenTableFragmentChoice(candidateIds, onDone);
        }

        private bool OpenTableFragmentChoice(
            IReadOnlyList<string> candidateIds,
            Action<bool> completed,
            Action onShown = null)
        {
            if (_world == null)
            {
                _world = BattleWorldController.Instance;
            }

            BindWorldHudAreas();
            return _fragmentEditCoordinator != null
                && _fragmentEditCoordinator.Open(candidateIds, completed, onShown);
        }

        public bool OpenRewardDishPack(
            RewardChoiceGroup group,
            IReadOnlyList<RewardChoice> choices,
            Func<int, bool> onChoiceSelected,
            Action onFinish)
        {
            return _rewardPage != null && _rewardPage.OpenRewardDishPack(group, choices, onChoiceSelected, onFinish);
        }

        public bool OpenAcquireDishPack(string title, IReadOnlyList<RewardChoice> choices)
        {
            return _rewardPage != null && _rewardPage.OpenAcquireDishPack(title, choices);
        }

        public bool OpenRewardItemChoices(string title, IReadOnlyList<RewardChoice> choices, cfg.ItemKind kind)
        {
            return _rewardPage != null && _rewardPage.OpenRewardItemChoices(title, choices, kind);
        }

        public bool OpenRewardItemChoices(
            RewardChoiceGroup group,
            IReadOnlyList<RewardChoice> choices,
            cfg.ItemKind kind,
            Func<int, bool> onPick,
            Action onFinish)
        {
            return _rewardPage != null && _rewardPage.OpenRewardItemChoices(group, choices, kind, onPick, onFinish);
        }

        public bool OpenRandomizedItemsPanel(string title, IReadOnlyList<RandomizedItemResult> results)
        {
            return _rewardPage != null && _rewardPage.OpenRandomizedItemsPanel(title, results);
        }

        private void PlayRandomizedItemFlys(IReadOnlyList<RandomizedItemResult> results)
        {
            bool completed = false;
            void CompletePresentation()
            {
                if (completed)
                {
                    return;
                }

                completed = true;
                RefreshItems();
                CompleteTutorialAcquiredItemPresentation();
            }

            if (results == null || _randomizedItemsPanel == null || _itemsColumn == null)
            {
                CompletePresentation();
                return;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            RectTransform layer = canvas != null ? canvas.transform as RectTransform : transform.root as RectTransform;
            if (layer == null)
            {
                CompletePresentation();
                return;
            }

            Canvas.ForceUpdateCanvases();
            // 哨兵保证动画创建失败并同步回调时，也只会在整个批次遍历结束后完成表现。
            int pendingFlys = 1;
            void CompleteOneFly()
            {
                pendingFlys--;
                if (pendingFlys <= 0)
                {
                    CompletePresentation();
                }
            }

            for (int i = 0; i < results.Count; i++)
            {
                RandomizedItemResult result = results[i];
                ItemDefinition item = result?.Item;
                if (item == null || !result.Acquired)
                {
                    continue;
                }

                if (!_randomizedItemsPanel.TryGetCardRect(i, layer, out Vector2 startCenter, out Vector2 startSize))
                {
                    continue;
                }

                if (!_itemsColumn.TryGetItemFlyTarget(_run, item.Id, item.Kind, layer, out Vector2 targetCenter, out Vector2 targetSize))
                {
                    continue;
                }

                pendingFlys++;
                PlayItemFlyTween(
                    new RectSnapshot(startCenter, startSize),
                    new RectSnapshot(targetCenter, targetSize),
                    RunItemSlotView.LoadIcon(item) ?? LoadShopItemFallbackIcon(item.Kind),
                    RunItemSlotView.QualityColor(item.Quality),
                    CompleteOneFly);
            }

            CompleteOneFly();
        }

        private void RestoreBattleWorld()
        {
            if (_run == null || _session == null)
            {
                return;
            }

            _world = _world ?? BattleWorldController.Instance;
            if (_world == null)
            {
                Log.Error("BattleForm: battle scene controller not found while restoring stomach view.", Tag);
                return;
            }

            BindWorldHudAreas();
            _world.Initialize(
                _run,
                _session,
                SetMessage,
                SetSettlementScore,
                RefreshAll,
                null,
                OnDishClicked,
                resetDoodle: false,
                serveTriggerCueSink: OnServeTriggerCue,
                pendingDishConfirmRequested: OnPendingDishConfirmRequested);
            _world.SetDishHoverCallbacks(OnDishHoverEntered, OnDishHoverExited);
            _world.SetCellHoverCallbacks(OnCellHoverEntered, OnCellHoverExited);
            _world.SetTableFragmentHoverCallbacks(OnTableFragmentHoverEntered, OnTableFragmentHoverExited);
            if (_session.IsSettled && _run.HasPendingRewardBattleView)
            {
                RestorePendingRewardPresentation(_run.GetPendingRewardBattleView());
            }
            RefreshAll();
        }

        /// <summary>奖励窗重新显示前，关闭独立查看层并恢复底下未重建的奖励流程。</summary>
        public void ReturnToPendingRewardView(Action onReturned)
        {
            if (_inspectionCoordinator != null && _inspectionCoordinator.IsActive)
            {
                _inspectionCoordinator.Close(onReturned);
                return;
            }

            onReturned?.Invoke();
        }

        private void BindWorldHoverCallbacks()
        {
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if (world == null)
            {
                return;
            }

            _world = world;
            _world.SetDishHoverCallbacks(OnDishHoverEntered, OnDishHoverExited);
            _world.SetCellHoverCallbacks(OnCellHoverEntered, OnCellHoverExited);
            _world.SetTableFragmentHoverCallbacks(OnTableFragmentHoverEntered, OnTableFragmentHoverExited);
        }

        private void SetActionAxisVisible(bool visible)
        {
            if (_timelineAxis != null)
            {
                _timelineAxis.gameObject.SetActive(visible);
            }
        }

        private CanvasGroup ResolveActionAxisGroup()
        {
            if (_actionAxisGroup == null && _timelineAxis != null)
            {
                _actionAxisGroup = _timelineAxis.GetComponent<CanvasGroup>();
            }

            return _actionAxisGroup;
        }

        private void SetFoodBattlePanelVisible(bool visible)
        {
            if (_foodBattlePanel != null)
            {
                _foodBattlePanel.SetActive(visible);
            }

            if (visible)
            {
                SyncBottomUiToSession();
            }

            _foodBar?.SetVisible(visible);
            ServingOutletView servingOutlet = ResolveServingOutlet();
            servingOutlet?.SetVisible(visible);
            FoodDiscardBinView discardBin = ResolveFoodDiscardBin();
            discardBin?.Bind(_session);
            discardBin?.SetVisible(visible && !_bossDiscardRevealPending);
            (_world ?? BattleWorldController.Instance)?.RefreshTemporaryAreaPresentation();
        }

        private async Awaitable PlayFoodSettlementLayoutAsync()
        {
            EnsureBottomUi();
            KillBottomUiTween();
            float bottomDuration = Mathf.Max(0.01f, _foodSettlementLayoutDuration > 0.01f ? _foodSettlementLayoutDuration : 0.32f);
            float tableDuration = Mathf.Max(0.01f, _foodSettlementTableLayoutDuration > 0.01f ? _foodSettlementTableLayoutDuration : 0.64f);
            Sequence sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            sequence.Pause();
            Tween bottomTween = CreateBottomUiHideTween(bottomDuration);
            if (bottomTween != null)
            {
                sequence.Append(bottomTween);
            }

            _world?.AppendFoodSettlementLayoutTween(sequence, tableDuration);
            sequence.OnUpdate(RefreshTemporaryAreaWorldLayout);

            if (sequence.Duration() <= 0f)
            {
                ApplyBottomUiHiddenImmediate();
                _world?.ApplySettlementLayoutImmediate();
            }

            float holdDuration = Mathf.Max(0f, _foodSettlementHoldDuration);
            if (holdDuration > 0.0001f)
            {
                sequence.AppendInterval(holdDuration);
            }

            if (sequence.Duration() <= 0f)
            {
                sequence.Kill();
                return;
            }

            _bottomUiTween = sequence;
            sequence.Play();
            try
            {
                await sequence.AsyncWaitForCompletion();
            }
            catch (System.OperationCanceledException)
            {
            }

            _bottomUiTween = null;
            ApplyBottomUiHiddenImmediate();
        }

        private void SyncBottomUiToSession()
        {
            if (_session != null && _session.IsSettled)
            {
                ApplyBottomUiHiddenImmediate();
                return;
            }

            RestoreBottomUiImmediate();
        }

        private void RestoreBottomUiImmediate()
        {
            EnsureBottomUi();
            KillBottomUiTween();
            if (_bottomUi == null)
            {
                return;
            }

            _bottomUi.anchoredPosition = _bottomUiRestPosition;
            RefreshTemporaryAreaWorldLayout();
            SetBottomUiInteractable(true);
        }

        private void ApplyBottomUiHiddenImmediate()
        {
            EnsureBottomUi();
            KillBottomUiTween();
            if (_bottomUi == null)
            {
                return;
            }

            _bottomUi.anchoredPosition = HiddenBottomUiPosition();
            RefreshTemporaryAreaWorldLayout();
            SetBottomUiInteractable(false);
        }

        private void RefreshTemporaryAreaWorldLayout()
        {
            (_world ?? BattleWorldController.Instance)?.RefreshTemporaryAreaLayout();
        }

        private Tween CreateBottomUiHideTween(float duration)
        {
            EnsureBottomUi();
            if (_bottomUi == null)
            {
                return null;
            }

            SetBottomUiInteractable(false);
            return _bottomUi
                .DOAnchorPos(HiddenBottomUiPosition(), duration)
                .SetEase(Ease.InCubic);
        }

        private Vector2 HiddenBottomUiPosition()
        {
            float height = _bottomUi != null ? Mathf.Max(_bottomUi.rect.height, _bottomUi.sizeDelta.y) : 410f;
            return new Vector2(_bottomUiRestPosition.x, _bottomUiRestPosition.y - height);
        }

        private void EnsureBottomUi()
        {
            if (_bottomUi == null && _foodBattlePanel != null)
            {
                Transform found = _foodBattlePanel.transform.Find("BottomUI");
                _bottomUi = found as RectTransform;
            }

            if (_bottomUi == null)
            {
                return;
            }

            if (_bottomUiGroup == null)
            {
                _bottomUiGroup = _bottomUi.GetComponent<CanvasGroup>();
                if (_bottomUiGroup == null)
                {
                    _bottomUiGroup = _bottomUi.gameObject.AddComponent<CanvasGroup>();
                }
            }

            if (!_bottomUiRestCaptured)
            {
                _bottomUiRestPosition = _bottomUi.anchoredPosition;
                _bottomUiRestCaptured = true;
            }
        }

        private void SetBottomUiInteractable(bool interactable)
        {
            if (_bottomUiGroup == null)
            {
                return;
            }

            _bottomUiGroup.interactable = interactable;
            _bottomUiGroup.blocksRaycasts = interactable;
        }

        private void KillBottomUiTween()
        {
            if (_bottomUiTween != null && _bottomUiTween.IsActive())
            {
                _bottomUiTween.Kill(false);
            }

            _bottomUiTween = null;
        }

        private void BindWorldHudAreas()
        {
            _world ??= BattleWorldController.Instance;
            _world?.SetTableArea(_boardArea);
            EnsureDragGhostOverlay();
            ConfigureTemporaryAreaOverlayFrame();
            EnsureTemporaryAreaDishOverlay();
            _world?.SetTemporaryArea(_temporaryArea);
        }

        /// <summary>
        /// 临时桌跟出餐口一样用实心底。菜在世界层会被 Overlay 盖住，
        /// 所以另外在框上叠一层 Overlay 菜图。
        /// </summary>
        private void ConfigureTemporaryAreaOverlayFrame()
        {
            if (_temporaryArea == null)
            {
                return;
            }

            Image image = _temporaryArea.GetComponent<Image>();
            if (image != null)
            {
                image.fillCenter = true;
                image.raycastTarget = false;
                CreamSageHudPanelStyle.ApplyTemporaryTable(image);
            }

            CanvasGroup group = _temporaryArea.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.blocksRaycasts = false;
                group.interactable = false;
            }
        }

        private void EnsureTemporaryAreaDishOverlay()
        {
            if (_temporaryArea == null)
            {
                return;
            }

            if (_temporaryAreaDishOverlay == null)
            {
                _temporaryAreaDishOverlay = _temporaryArea.GetComponentInChildren<TemporaryAreaDishOverlay>(true);
            }

            if (_temporaryAreaDishOverlay == null)
            {
                var go = new GameObject(
                    "TemporaryAreaDishOverlay",
                    typeof(RectTransform),
                    typeof(TemporaryAreaDishOverlay));
                go.layer = _temporaryArea.gameObject.layer;
                var rect = (RectTransform)go.transform;
                rect.SetParent(_temporaryArea, false);
                rect.SetAsFirstSibling();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;
                var group = go.AddComponent<CanvasGroup>();
                group.blocksRaycasts = false;
                group.interactable = false;
                _temporaryAreaDishOverlay = go.GetComponent<TemporaryAreaDishOverlay>();
            }

            _temporaryAreaDishOverlay.transform.SetSiblingIndex(
                Mathf.Min(
                    CreamSageHudPanelStyle.TemporaryTableContentSiblingIndex,
                    _temporaryArea.childCount - 1));

            _temporaryAreaDishOverlay.Bind(_world ?? BattleWorldController.Instance);
        }

        /// <summary>
        /// Overlay 永远盖住世界棋子。拖拽残影单独放在更高 sortingOrder 的 Overlay 层，
        /// 才能画在出餐口上面，又不必把出餐口搬进场景。
        /// </summary>
        private void EnsureDragGhostOverlay()
        {
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if (_dragGhostOverlay == null)
            {
                var go = new GameObject(
                    "DragGhostOverlay",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasGroup),
                    typeof(DishDragGhostOverlay));
                go.layer = gameObject.layer;
                var rect = (RectTransform)go.transform;
                rect.SetParent(transform, false);
                rect.SetAsLastSibling();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;

                Canvas canvas = go.GetComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = 250;

                CanvasGroup group = go.GetComponent<CanvasGroup>();
                group.blocksRaycasts = false;
                group.interactable = false;
                group.ignoreParentGroups = true;

                _dragGhostOverlay = go.GetComponent<DishDragGhostOverlay>();
            }

            _dragGhostOverlay.Bind(world);
        }

        /// <summary>初始化经营挑战态的出菜口、弃置区与世界拖拽回调。</summary>
        private void BuildBattleControls()
        {
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            ServingOutletView servingOutlet = ResolveServingOutlet();
            FoodDiscardBinView discardBin = ResolveFoodDiscardBin();
            discardBin?.Bind(_session);
            if (world != null)
            {
                world.SetPreparedDishDiscardTarget(
                    discardBin == null ? null : discardBin.CanAcceptDropAt,
                    discardBin == null ? null : discardBin.SetDragHovered,
                    discardBin == null ? null : discardBin.GetDiscardAnimationTargetScreenPosition,
                    discardBin == null ? null : discardBin.PlayAcceptedAnimation);
            }

            servingOutlet?.Bind(
                _session,
                () => OnServingOutletDishHoverEntered(servingOutlet),
                OnServingOutletDishHoverExited,
                world == null ? null : screen => world.BeginServingOutletDrag(screen),
                world == null ? null : screen => world.UpdateServingOutletDrag(screen),
                world == null ? null : screen => world.EndServingOutletDrag(screen));
        }

        private ServingOutletView ResolveServingOutlet()
        {
            if (_servingOutlet == null)
            {
                _servingOutlet = FindFirstObjectByType<ServingOutletView>(FindObjectsInactive.Include);
            }

            return _servingOutlet;
        }

        private FoodDiscardBinView ResolveFoodDiscardBin()
        {
            if (_foodDiscardBin == null)
            {
                _foodDiscardBin = FindFirstObjectByType<FoodDiscardBinView>(FindObjectsInactive.Include);
            }

            return _foodDiscardBin;
        }

        private void BeginFoodDiscardCapacityTracking()
        {
            _foodDiscardCapacitySession = _session;
            _observedFoodDiscardCapacity = _run != null
                ? new ItemRuntime(_run).FoodDiscardCapacity()
                : -1;
        }

        private void ResetFoodDiscardCapacityTracking()
        {
            _foodDiscardCapacitySession = null;
            _observedFoodDiscardCapacity = -1;
        }

        /// <summary>
        /// 垃圾桶类装饰可能在经营挑战页存活期间由奖励 / GM 加入。
        /// 新建会话时已把当时上限配满；此处只应用之后的持有量差值，
        /// 因此 Boss 的“初始丢弃次数为 0”仍然保留，战中新获得的加成则会实时补到上限和剩余。
        /// </summary>
        private void SynchronizeFoodDiscardCapacity()
        {
            if (_run == null)
            {
                return;
            }

            int currentCapacity = new ItemRuntime(_run).FoodDiscardCapacity();
            if (_session == null || _session.IsSettled)
            {
                bool changed = _observedFoodDiscardCapacity != currentCapacity;
                _foodDiscardCapacitySession = _session;
                _observedFoodDiscardCapacity = currentCapacity;
                if (changed && _inBattle)
                {
                    RefreshPersistent(refreshItems: false);
                }

                return;
            }

            if (!ReferenceEquals(_foodDiscardCapacitySession, _session))
            {
                BeginFoodDiscardCapacityTracking();
                return;
            }

            int delta = currentCapacity - _observedFoodDiscardCapacity;
            if (delta == 0)
            {
                return;
            }

            _observedFoodDiscardCapacity = currentCapacity;
            _session.AdjustFoodDiscardLimit(delta);
            RefreshAll();
        }

        /// <summary>刷新常驻信息：左栏周/金币/分数、右栏装饰品和消耗品。</summary>
        private void RefreshPersistent(bool refreshItems = true)
        {
            if (_run == null)
            {
                return;
            }

            BattleWorldController world = _world ?? BattleWorldController.Instance;
            _infoColumn?.Refresh(
                _run,
                _session,
                _current,
                ActiveInspectionView,
                _fragmentEditCoordinator?.IsActive == true,
                world,
                RewardForm.Active?.AllowsPersistentInteractions == true);
            RefreshCakeLayerBuff();

            if (refreshItems)
            {
                RefreshItems();
            }

            RefreshFoodActions();
        }

        internal void RefreshPersistentHud(bool refreshItems = true)
        {
            RefreshPersistent(refreshItems);
        }

        internal void ShowPersistentHudForReward(bool refreshItems = true)
        {
            EnsurePersistentRewardHudVisible(
                _hudFrame,
                _infoColumn != null ? _infoColumn.gameObject : null,
                _itemsColumn != null ? _itemsColumn.gameObject : null);
            RefreshPersistent(refreshItems);
        }

        internal static void EnsurePersistentRewardHudVisible(
            GameObject hudFrame,
            GameObject leftColumn,
            GameObject rightColumn)
        {
            if (hudFrame != null)
            {
                hudFrame.SetActive(true);
            }

            if (leftColumn != null)
            {
                leftColumn.SetActive(true);
            }

            if (rightColumn != null)
            {
                rightColumn.SetActive(true);
            }
        }

        internal void CommitRewardInventoryMutation()
        {
            if (_run == null || RewardForm.Active == null)
            {
                return;
            }

            RunPersistence.Save(_run);
            RewardForm.Active.RefreshAfterExternalInventoryMutation();
        }

        private void RefreshCakeLayerBuff()
        {
            if (_cakeLayerBuffHud == null)
            {
                return;
            }

            bool shouldShow = HappyCakeHudVisibility.ShouldShow(
                _run,
                _current,
                _rewardDishPackPanel != null && _rewardDishPackPanel.gameObject.activeInHierarchy,
                _rewardDishPackPanel != null ? _rewardDishPackPanel.CurrentChoices : null,
                _shopPanel != null ? _shopPanel.CurrentStock : null);
            if (!shouldShow)
            {
                _cakeLayerBuffHud.Hide();
            }
            else
            {
                _cakeLayerBuffHud.Bind(
                    _session != null ? _displayedCakeLayers : 0,
                    _session?.Database?.CakeLayerBuffs ?? _run?.Database?.CakeLayerBuffs,
                    _tips != null ? _tips.Item : null);
            }
        }

        private void RefreshFoodActions()
        {
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            _foodBar?.Refresh(
                _current == GameplayView.Food && ActiveInspectionView == BattleInspectionView.None,
                _session,
                world);
        }

        /// <summary>右栏装饰品和消耗品：被动网格（2 列）+ 固定消耗品槽，每份主动实例占一格。</summary>
        private void RefreshItems()
        {
            _itemsColumn?.Refresh(
                _run,
                _session,
                _inBattle,
                true,
                _tips != null ? _tips.Item : null,
                OnActiveItemClicked,
                ShowItemInfo);
            TryRegisterTutorialAcquiredItemAnchor();
        }

        private void ShowItemInfo(ItemDefinition item, RunItemState state)
        {
            if (item == null)
            {
                return;
            }

            ShowNotice(item.Name, item.Desc, null);
        }

        private void RebuildActionAxis()
        {
            _axisBinder?.Rebuild(_run, _currentTimelineNodeCard?.Id);
        }

        private void HideAllTips()
        {
            _hoveredDishPiece = null;
            _hoveredCell = null;
            _foodTipsHoverOwner = FoodTipsHoverOwner.None;
            if (_tips != null)
            {
                _tips.HideAll();
            }
        }

        internal void HideBattleFoodTipsForRewardOverlay()
        {
            HideFoodTips();
        }

        /// <summary>商店内数据变化回调：刷新常驻壳信息。</summary>
        private void RefreshShopPersistent()
        {
            _shopPage?.RefreshPersistent();
        }

        private PreparedShopPurchaseAnimation PrepareShopPurchaseAnimation(
            ShopEntry entry,
            ShopBuyItemViewBase sourceCard)
        {
            if (entry == null || sourceCard == null || entry.Kind == ShopEntryKind.Fragment)
            {
                return null;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            RectTransform layer = canvas != null ? canvas.transform as RectTransform : transform.root as RectTransform;
            RectTransform sourceRect = sourceCard.PurchaseFlySource;
            if (layer == null || sourceRect == null)
            {
                return null;
            }

            Canvas.ForceUpdateCanvases();
            if (!TryGetRectInLayer(sourceRect, layer, out RectSnapshot start))
            {
                return null;
            }

            if (entry.Kind == ShopEntryKind.Dish)
            {
                RectTransform target = _infoColumn?.ViewRecipeButtonRect;
                if (target == null ||
                    !TryGetRectInLayer(target, layer, out RectSnapshot end))
                {
                    return null;
                }

                RenderTexture texture = sourceCard.CapturePurchaseFlyTexture();
                if (texture == null)
                {
                    return null;
                }

                return new PreparedShopPurchaseAnimation(
                    () => PlayShopFoodPurchase(layer, start, end, texture),
                    () => ReleasePurchaseTexture(texture));
            }

            Sprite sprite = sourceCard.PurchaseFlySprite;
            return new PreparedShopPurchaseAnimation(
                () => PlayShopItemPurchase(entry, sprite, layer, start));
        }

        private void PlayShopFoodPurchase(
            RectTransform layer,
            RectSnapshot start,
            RectSnapshot end,
            RenderTexture texture)
        {
            ShopPurchaseFlyView fly = CreateShopPurchaseFly(layer);
            if (fly == null)
            {
                ReleasePurchaseTexture(texture);
                return;
            }

            RegisterShopPurchaseFly(fly);
            try
            {
                fly.PlayFood(
                    start.Center,
                    start.Size,
                    end.Center,
                    texture,
                    null,
                    () => UnregisterShopPurchaseFly(fly));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, fly);
                fly.Cancel();
                return;
            }

            ShopPurchaseAnimationStarted?.Invoke(ShopEntryKind.Dish);
        }

        private void PlayRewardDishSelectionFly(RewardDishChoiceCardView sourceCard)
        {
            if (sourceCard == null)
            {
                return;
            }

            PlayRewardDishSelectionFly(
                sourceCard.SelectionFlySource,
                sourceCard.CaptureSelectionFlyTexture);
        }

        internal bool PlayRewardDishSelectionFly(RewardChoiceRowView sourceRow)
        {
            return sourceRow != null && PlayRewardDishSelectionFly(
                sourceRow.SelectionFlySource,
                sourceRow.CaptureSelectionFlyTexture);
        }

        private bool PlayRewardDishSelectionFly(
            RectTransform sourceRect,
            Func<RenderTexture> captureTexture)
        {
            if (sourceRect == null || captureTexture == null)
            {
                return false;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            RectTransform layer = canvas != null ? canvas.transform as RectTransform : transform.root as RectTransform;
            RectTransform target = _infoColumn?.ViewRecipeButtonRect;
            if (layer == null || sourceRect == null || target == null)
            {
                return false;
            }

            Canvas.ForceUpdateCanvases();
            if (!TryGetRectInLayer(sourceRect, layer, out RectSnapshot start) ||
                !TryGetRectInLayer(target, layer, out RectSnapshot end))
            {
                return false;
            }

            RenderTexture texture = captureTexture.Invoke();
            if (texture == null)
            {
                return false;
            }

            ShopPurchaseFlyView fly = CreateShopPurchaseFly(layer);
            if (fly == null)
            {
                ReleasePurchaseTexture(texture);
                return false;
            }

            RegisterShopPurchaseFly(fly);
            try
            {
                fly.PlayFood(
                    start.Center,
                    start.Size,
                    end.Center,
                    texture,
                    null,
                    () => UnregisterShopPurchaseFly(fly));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, fly);
                fly.Cancel();
                return false;
            }

            return true;
        }

        private Action PrepareRewardItemSelectionFly(
            RewardChoice choice,
            cfg.ItemKind kind,
            RewardItemChoiceCardView sourceCard)
        {
            if (choice == null)
            {
                return null;
            }

            Func<bool> play = sourceCard == null
                ? null
                : PrepareRewardItemSelectionFly(
                    choice,
                    kind,
                    sourceCard.SelectionFlySource,
                    sourceCard.SelectionFlySprite);
            return () =>
            {
                if (play?.Invoke() != true)
                {
                    CompleteTutorialAcquiredItemPresentation();
                }
            };
        }

        internal Func<bool> PrepareRewardItemSelectionFly(
            RewardChoice choice,
            cfg.ItemKind kind,
            RewardChoiceRowView sourceRow)
        {
            return sourceRow == null
                ? null
                : PrepareRewardItemSelectionFly(
                    choice,
                    kind,
                    sourceRow.SelectionFlySource,
                    sourceRow.SelectionFlySprite);
        }

        private Func<bool> PrepareRewardItemSelectionFly(
            RewardChoice choice,
            cfg.ItemKind kind,
            RectTransform sourceRect,
            Sprite sourceSprite)
        {
            if (choice == null || sourceRect == null)
            {
                return null;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            RectTransform layer = canvas != null ? canvas.transform as RectTransform : transform.root as RectTransform;
            if (layer == null)
            {
                return null;
            }

            Canvas.ForceUpdateCanvases();
            if (!TryGetRectInLayer(sourceRect, layer, out RectSnapshot start))
            {
                return null;
            }

            ItemDefinition item = ItemDefinition.Get(GameApp.Config.Tables, choice.Id, kind);
            if (item == null)
            {
                return null;
            }

            string itemId = item.Id;
            cfg.ItemKind itemKind = item.Kind;
            Sprite sprite =
                RunItemSlotView.LoadIcon(item) ??
                sourceSprite ??
                LoadShopItemFallbackIcon(itemKind);
            Color fallbackColor = RunItemSlotView.QualityColor(item.Quality);
            return () => PlayRewardItemSelectionFly(
                itemId,
                itemKind,
                layer,
                start,
                sprite,
                fallbackColor);
        }

        private bool PlayRewardItemSelectionFly(
            string itemId,
            cfg.ItemKind kind,
            RectTransform layer,
            RectSnapshot start,
            Sprite sprite,
            Color fallbackColor)
        {
            if (_run == null ||
                _itemsColumn == null ||
                layer == null ||
                !_itemsColumn.TryGetItemFlyTarget(
                    _run,
                    itemId,
                    kind,
                    layer,
                    out Vector2 targetCenter,
                    out Vector2 targetSize))
            {
                return false;
            }

            ShopPurchaseFlyView fly = CreateShopPurchaseFly(layer);
            if (fly == null)
            {
                return false;
            }

            _shopItemFlyInFlight++;
            RegisterShopPurchaseFly(fly);
            try
            {
                if (kind == cfg.ItemKind.Active)
                {
                    fly.PlayActive(
                        start.Center,
                        targetCenter,
                        targetSize,
                        sprite,
                        fallbackColor,
                        OnShopItemFlyArrived,
                        () => UnregisterShopPurchaseFly(fly));
                }
                else
                {
                    fly.PlayPassive(
                        start.Center,
                        start.Size,
                        targetCenter,
                        targetSize,
                        sprite,
                        fallbackColor,
                        OnShopItemFlyArrived,
                        () => UnregisterShopPurchaseFly(fly));
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, fly);
                fly.Cancel();
                OnShopItemFlyArrived();
                return false;
            }

            return true;
        }

        private void PlayShopItemPurchase(
            ShopEntry entry,
            Sprite sourceSprite,
            RectTransform layer,
            RectSnapshot start)
        {
            if (_itemsColumn == null)
            {
                CompleteTutorialAcquiredItemPresentation();
                return;
            }

            cfg.ItemKind kind = entry.Kind == ShopEntryKind.ActiveItem
                ? cfg.ItemKind.Active
                : cfg.ItemKind.Passive;
            ItemDefinition item = ItemDefinition.Get(GameApp.Config.Tables, entry.Id, kind);
            if (item == null ||
                !_itemsColumn.TryGetItemFlyTarget(
                    _run,
                    entry.Id,
                    item.Kind,
                    layer,
                    out Vector2 targetCenter,
                    out Vector2 targetSize))
            {
                CompleteTutorialAcquiredItemPresentation();
                return;
            }

            ShopPurchaseFlyView fly = CreateShopPurchaseFly(layer);
            if (fly == null)
            {
                CompleteTutorialAcquiredItemPresentation();
                return;
            }

            Sprite sprite = sourceSprite ?? RunItemSlotView.LoadIcon(item) ?? LoadShopItemFallbackIcon(item.Kind);
            Color fallbackColor = RunItemSlotView.QualityColor(item.Quality);
            _shopItemFlyInFlight++;
            RegisterShopPurchaseFly(fly);
            try
            {
                if (entry.Kind == ShopEntryKind.ActiveItem)
                {
                    fly.PlayActive(
                        start.Center,
                        targetCenter,
                        targetSize,
                        sprite,
                        fallbackColor,
                        OnShopItemFlyArrived,
                        () => UnregisterShopPurchaseFly(fly));
                }
                else
                {
                    fly.PlayPassive(
                        start.Center,
                        start.Size,
                        targetCenter,
                        targetSize,
                        sprite,
                        fallbackColor,
                        OnShopItemFlyArrived,
                        () => UnregisterShopPurchaseFly(fly));
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, fly);
                fly.Cancel();
                return;
            }

            ShopPurchaseAnimationStarted?.Invoke(entry.Kind);
        }

        private ShopPurchaseFlyView CreateShopPurchaseFly(RectTransform layer)
        {
            if (_shopItemFlyFxPrefab == null)
            {
                Debug.LogError($"{nameof(BattleForm)} 缺少商店购买飞行动画 prefab。", this);
                return null;
            }

            GameObject go = Instantiate(_shopItemFlyFxPrefab, layer);
            go.name = "ShopPurchaseFlyFx";
            ShopPurchaseFlyView fly =
                go.GetComponent<ShopPurchaseFlyView>() ??
                go.AddComponent<ShopPurchaseFlyView>();
            if (!fly.Initialize(layer))
            {
                Debug.LogError(
                    "ShopItemFlyFx prefab 必须包含 RectTransform、CanvasGroup、Image。",
                    go);
                Destroy(go);
                return null;
            }

            return fly;
        }

        private void RegisterShopPurchaseFly(ShopPurchaseFlyView fly)
        {
            if (fly != null)
            {
                _activeShopPurchaseFlys.Add(fly);
            }
        }

        private void UnregisterShopPurchaseFly(ShopPurchaseFlyView fly)
        {
            if (fly != null)
            {
                _activeShopPurchaseFlys.Remove(fly);
            }
        }

        private void OnShopItemFlyArrived()
        {
            _shopItemFlyInFlight = Mathf.Max(0, _shopItemFlyInFlight - 1);
            if (_shopItemFlyInFlight <= 0)
            {
                RefreshItems();
                CompleteTutorialAcquiredItemPresentation();
            }
        }

        private void CancelActiveShopPurchaseAnimations()
        {
            if (_activeShopPurchaseFlys.Count > 0)
            {
                var active = new List<ShopPurchaseFlyView>(_activeShopPurchaseFlys);
                for (int i = 0; i < active.Count; i++)
                {
                    active[i]?.Cancel();
                }
            }

            _activeShopPurchaseFlys.Clear();
            _shopItemFlyInFlight = 0;
        }

        private static void ReleasePurchaseTexture(RenderTexture texture)
        {
            if (texture == null)
            {
                return;
            }

            texture.Release();
            if (Application.isPlaying)
            {
                Destroy(texture);
            }
            else
            {
                DestroyImmediate(texture);
            }
        }

        private void PlayItemFlyTween(RectSnapshot start, RectSnapshot end, Sprite sprite, Color fallbackColor, Action onComplete)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            RectTransform layer = canvas != null ? canvas.transform as RectTransform : transform.root as RectTransform;
            if (layer == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (_shopItemFlyFxPrefab == null)
            {
                Debug.LogError($"{nameof(BattleForm)} 缺少商店装饰品和消耗品飞行动画 prefab。", this);
                onComplete?.Invoke();
                return;
            }

            GameObject go = Instantiate(_shopItemFlyFxPrefab, layer);
            go.name = "ShopItemFlyFx";
            var rect = go.GetComponent<RectTransform>();
            var group = go.GetComponent<CanvasGroup>();
            var image = go.GetComponent<Image>();
            if (rect == null || group == null || image == null)
            {
                Debug.LogError("ShopItemFlyFx prefab 必须包含 RectTransform、CanvasGroup、Image。", go);
                Destroy(go);
                onComplete?.Invoke();
                return;
            }

            rect.SetAsLastSibling();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = start.Center;
            rect.sizeDelta = start.Size;
            rect.localScale = Vector3.one;

            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = sprite != null ? Color.white : fallbackColor;
            group.blocksRaycasts = false;

            bool finished = false;
            Action finish = () =>
            {
                if (finished)
                {
                    return;
                }

                finished = true;
                if (go != null)
                {
                    Destroy(go);
                }

                onComplete?.Invoke();
            };

            Sequence sequence = DOTween.Sequence().SetUpdate(true).SetLink(go);
            sequence.Append(DOVirtual.Float(0f, 1f, RandomizedItemFlyDuration, t =>
            {
                if (rect == null)
                {
                    return;
                }

                rect.anchoredPosition = Vector2.LerpUnclamped(start.Center, end.Center, t);
                rect.sizeDelta = Vector2.LerpUnclamped(start.Size, end.Size, t);
            }).SetEase(Ease.InOutCubic));
            sequence.Insert(RandomizedItemFlyDuration * 0.8f, DOVirtual.Float(1f, 0f, RandomizedItemFlyDuration * 0.2f, alpha =>
            {
                if (group != null)
                {
                    group.alpha = alpha;
                }
            }).SetEase(Ease.InQuad));
            sequence.OnComplete(() => finish());
            sequence.OnKill(() => finish());
        }

        private static bool TryGetRectInLayer(RectTransform rect, RectTransform layer, out RectSnapshot snapshot)
        {
            snapshot = default;
            if (rect == null || layer == null)
            {
                return false;
            }

            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector3 first = layer.InverseTransformPoint(corners[0]);
            float minX = first.x;
            float maxX = first.x;
            float minY = first.y;
            float maxY = first.y;

            for (int i = 1; i < corners.Length; i++)
            {
                Vector3 local = layer.InverseTransformPoint(corners[i]);
                minX = Mathf.Min(minX, local.x);
                maxX = Mathf.Max(maxX, local.x);
                minY = Mathf.Min(minY, local.y);
                maxY = Mathf.Max(maxY, local.y);
            }

            Vector2 size = new Vector2(Mathf.Max(1f, maxX - minX), Mathf.Max(1f, maxY - minY));
            snapshot = new RectSnapshot(new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f), size);
            return true;
        }

        private static Sprite LoadShopItemFallbackIcon(cfg.ItemKind kind)
        {
            return Resources.Load<Sprite>(kind == cfg.ItemKind.Active
                ? "Sprites/UI/ui_icon_shop_active"
                : "Sprites/UI/ui_icon_shop_passive");
        }

        private readonly struct RectSnapshot
        {
            public RectSnapshot(Vector2 center, Vector2 size)
            {
                Center = center;
                Size = size;
            }

            public Vector2 Center { get; }
            public Vector2 Size { get; }
        }

        // —— 行动选择卡片（原 WeekMapForm 逻辑并入）——

        private void BuildActionCards()
        {
            ClearTimelineNodeCard();
            List<ActionChoice> choices = ActionOfferService.GetOrRoll(_run);
            ReportActionChoicesShown(choices);
            _deck?.ShowActionChoices(
                choices,
                OnActionSelectionPicked,
                OnActionRerollClicked,
                _run != null ? _run.ActionRerollCount : 0,
                nextBusinessRewardDoubleActive:
                    _run != null && _run.NextBusinessRewardDoubleStacks > 0);
        }

        private void TrackTimelineNodeCard(cfg.TimelineNode node, int? interestMaxGain, Action onPick)
        {
            _currentTimelineNodeCard = node;
            _currentTimelineNodeInterestMaxGain = interestMaxGain;
            _currentTimelineNodePick = onPick;
        }

        private void ClearTimelineNodeCard()
        {
            _currentTimelineNodeCard = null;
            _currentTimelineNodeInterestMaxGain = null;
            _currentTimelineNodePick = null;
        }

        /// <summary>时间轴节点单卡：用于商店等节点，点击卡片后才执行节点效果。</summary>
        private void BuildTimelineNodeCard(cfg.TimelineNode node, int? interestMaxGain, Action onPick)
        {
            if (_deck == null)
            {
                onPick?.Invoke();
                return;
            }

            int? interestThreshold = _run != null ? _run.InterestThreshold : null;
            int? interestGoldPer = _run != null ? _run.InterestGoldPer : null;
            _deck.ShowTimelineNode(
                node,
                interestThreshold,
                interestGoldPer,
                interestMaxGain,
                () => OnTimelineNodePicked(onPick),
                () => onPick?.Invoke());
        }

        private static List<ActionChoice> RollChoices(GameRun run)
        {
            return ActionOfferService.GetOrRoll(run);
        }

        private void OnActionRerollClicked()
        {
            if (_run == null || !_run.TrySpendActionReroll())
            {
                BuildActionCards();
                PlayShowCardsWhenReady();
                return;
            }

            GameAnalyticsService.TrackChoiceOfferResolved(
                _run,
                CurrentActionOfferId(),
                "daily_action",
                0,
                skipped: true,
                _run.PendingActionChoiceRevision + 1,
                (long)Math.Max(0d, (Time.realtimeSinceStartup - _actionOfferOpenedRealtime) * 1000d),
                _actionOfferArchetype);
            ActionOfferService.RerollAfterSpend(_run);
            RebuildActionAxis();
            RefreshActionCardsAnimated();
        }

        /// <summary>玩家在中部选择了一个行动（null = 无行动可选时的「休息」）。</summary>
        private void OnActionSelectionPicked(ActionChoice choice)
        {
            TutorialRuntime.Publish(TutorialSignal.ActionPicked);
            ReportActionChoiceSelected(choice);
            ActionOfferService.Resolve(_run);
            _deck?.HideThenDestroy(() =>
            {
                _loop?.OnActionPicked(choice);
            });
        }

        private void ReportActionChoicesShown(IReadOnlyList<ActionChoice> choices)
        {
            if (_run == null || choices == null)
            {
                return;
            }

            AnalyticsSelectionMode mode = choices.Count > 1
                ? AnalyticsSelectionMode.Optional
                : AnalyticsSelectionMode.Forced;
            _actionOfferArchetype = ArchetypeService.Capture(_run);
            _actionOfferOpenedRealtime = Time.realtimeSinceStartup;
            string offerId = CurrentActionOfferId();
            for (int index = 0; index < choices.Count; index++)
            {
                ActionChoice choice = choices[index];
                if (choice?.Action == null)
                {
                    continue;
                }

                GameAnalyticsService.TrackChoiceCandidate(
                    _run,
                    selected: false,
                    offerId,
                    "daily_action",
                    "action",
                    choice.Action.Id,
                    string.Empty,
                    index,
                    choices.Count,
                    1,
                    mode,
                    _run.PendingActionChoiceRevision,
                    _actionOfferArchetype);
            }
        }

        private void ReportActionChoiceSelected(ActionChoice selected)
        {
            if (_run == null || selected?.Action == null)
            {
                return;
            }

            List<ActionChoice> choices = _run.GetPendingActionChoices(_run.PendingActionChoiceKey);
            int index = choices.FindIndex(candidate =>
                candidate?.Action != null
                && string.Equals(candidate.Action.Id, selected.Action.Id, StringComparison.Ordinal)
                && string.Equals(candidate.ActionGroupId, selected.ActionGroupId, StringComparison.Ordinal));
            AnalyticsSelectionMode mode = choices.Count > 1
                ? AnalyticsSelectionMode.Optional
                : AnalyticsSelectionMode.Forced;
            string offerId = CurrentActionOfferId();
            GameAnalyticsService.TrackChoiceCandidate(
                _run,
                selected: true,
                offerId,
                "daily_action",
                "action",
                selected.Action.Id,
                string.Empty,
                Math.Max(0, index),
                choices.Count,
                1,
                mode,
                _run.PendingActionChoiceRevision,
                _actionOfferArchetype);
            GameAnalyticsService.TrackChoiceOfferResolved(
                _run,
                offerId,
                "daily_action",
                1,
                skipped: false,
                _run.PendingActionChoiceRevision,
                (long)Math.Max(0d, (Time.realtimeSinceStartup - _actionOfferOpenedRealtime) * 1000d),
                _actionOfferArchetype);
        }

        private string CurrentActionOfferId()
        {
            return _run == null
                ? string.Empty
                : $"{_run.RunId}:{_run.PendingActionChoiceKey}:r{_run.PendingActionChoiceRevision}";
        }

        /// <summary>玩家点击时间轴节点卡片。</summary>
        private void OnTimelineNodePicked(Action onPick)
        {
            ClearTimelineNodeCard();
            _deck?.HideThenDestroy(() =>
            {
                onPick?.Invoke();
            });
        }

        private void PlayShowCardsWhenReady(Action onShown = null)
        {
            _deck?.PlayShowWhenReady(
                () => _current == GameplayView.ActionSelect,
                onShown);
        }

        /// <summary>行动选项变更：先等旧卡 hide 播完，再构建并 show 新卡。</summary>
        private void RefreshActionCardsAnimated()
        {
            if (_deck == null)
            {
                BuildActionCards();
                PlayShowCardsWhenReady();
                return;
            }

            _deck.HideThenDestroy(() =>
            {
                BuildActionCards();
                PlayShowCardsWhenReady();
            });
        }

        /// <summary>隐藏常驻壳与中部内容（用于开局前 / 结算返回菜单前的清场）。</summary>
        private void HideHud()
        {
            _rewardPeekOnly = false;
            _switchRequestId++;
            CancelPassivePresentations();
            _inspectionCoordinator?.ForceClose();
            _rewardPage?.CloseRewardPages();
            _fragmentEditCoordinator?.ForceClose();
            ClearTimelineNodeCard();
            _pageRouter?.HideHud();
            SyncPageStateFromRouter();
            _infoColumn?.ScoreFire?.Hide();
            _infoColumn?.ResetTableLabel();
            SetMessage(string.Empty);
        }

        private void OnSettingsClicked()
        {
            if (GameApp.UI.HasUIForm(UIForms.Settings)
                || GameApp.UI.IsLoadingUIForm(UIForms.Settings))
            {
                return;
            }

            _world = _world ?? BattleWorldController.Instance;
            GameApp.UI.OpenUIForm(
                UIForms.Settings,
                UIForms.GroupDialog,
                new SettingsFormData(
                    inGameplay: true,
                    pauseSettlementPlayback: () => _world?.PauseSettlementPlayback() == true,
                    resumeSettlementPlayback: () => _world?.ResumeSettlementPlayback()));
        }

        private void OnViewRecipeClicked()
        {
            if (_run == null || _current == GameplayView.None)
            {
                return;
            }

            if (IsViewToggleTransitioning())
            {
                return;
            }

            if (_fragmentEditCoordinator?.IsActive == true)
            {
                OpenRecipeInspect(0);
                return;
            }

            RewardForm reward = RewardForm.Active;
            bool managesReward = reward != null && !reward.IsSuspendedForRewardSubflow;
            bool beginsRewardInspection = managesReward && !reward.IsPersistentInspectionActive;
            if (beginsRewardInspection && !reward.TryBeginPersistentInspection())
            {
                return;
            }

            if (!OpenRecipeInspect(0) && beginsRewardInspection)
            {
                reward.CompletePersistentInspection();
            }
        }

        private void OnBattleRecipeClicked()
        {
            if (_rewardPeekOnly
                || HasPendingBattleRewardLifecycle
                || _current != GameplayView.Food
                || IsViewToggleTransitioning())
            {
                return;
            }

            OpenRecipeInspect(0, useBattleRecipe: true);
        }

        private bool OpenPassiveFlavorMutationPage(RecipeMutationResult result)
        {
            EnsurePassiveFlavorMutationPage();
            ClosePassiveFlavorMutationDishes();
            if (_passiveFlavorMutationPage == null
                || _passiveFlavorMutationContent == null
                || result == null)
            {
                return false;
            }

            RecipeReadonlyBookView previewFactory =
                _inspectionLayer?.RecipeView ?? _recipeReadonlyBookView;
            if (previewFactory == null || _run == null)
            {
                return false;
            }

            for (int i = 0; i < result.Entries.Count; i++)
            {
                RecipeMutationEntry entry = result.Entries[i];
                if (entry?.Before == null
                    || entry.After == null
                    || string.IsNullOrEmpty(entry.Before.DishId)
                    || string.IsNullOrEmpty(entry.After.DishId))
                {
                    continue;
                }

                RecipeEditDishView dish = previewFactory.CreatePassiveMutationPreview(
                    _passiveFlavorMutationContent,
                    _run,
                    entry.Before,
                    _passiveFlavorMutationDishes.Count);
                if (dish == null)
                {
                    continue;
                }

                _passiveFlavorMutationDishes.Add(dish);
                _passiveFlavorMutationEntries.Add(entry);
            }

            if (_passiveFlavorMutationDishes.Count == 0)
            {
                return false;
            }

            const float cardSize = 140f;
            const float cardSpacing = 44f;
            float totalWidth = cardSize * _passiveFlavorMutationDishes.Count
                + cardSpacing * Mathf.Max(0, _passiveFlavorMutationDishes.Count - 1);
            RectTransform pageRect = _passiveFlavorMutationPage.transform as RectTransform;
            float availableWidth = Mathf.Max(1f, (pageRect?.rect.width ?? totalWidth) - 80f);
            float scale = Mathf.Min(1.35f, availableWidth / totalWidth);
            float stride = (cardSize + cardSpacing) * scale;
            float firstX = -stride * (_passiveFlavorMutationDishes.Count - 1) * 0.5f;
            for (int i = 0; i < _passiveFlavorMutationDishes.Count; i++)
            {
                RectTransform rect = _passiveFlavorMutationDishes[i].transform as RectTransform;
                if (rect == null)
                {
                    continue;
                }

                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(firstX + stride * i, 0f);
                rect.localScale = Vector3.one * scale;
                rect.localRotation = Quaternion.identity;
            }

            _passiveFlavorMutationPage.alpha = 1f;
            _passiveFlavorMutationPage.interactable = false;
            _passiveFlavorMutationPage.blocksRaycasts = false;
            _passiveFlavorMutationPage.gameObject.SetActive(true);
            _passiveFlavorMutationPage.transform.SetAsLastSibling();
            SetPassivePresentationInputLocked(true);
            Canvas.ForceUpdateCanvases();
            return true;
        }

        private void PlayPassiveFlavorTransforms(Action onComplete)
        {
            int count = Mathf.Min(
                _passiveFlavorMutationDishes.Count,
                _passiveFlavorMutationEntries.Count);
            if (count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            int remaining = count;
            void CompleteOne()
            {
                remaining--;
                if (remaining == 0)
                {
                    onComplete?.Invoke();
                }
            }

            for (int i = 0; i < count; i++)
            {
                RecipeEditDishView dish = _passiveFlavorMutationDishes[i];
                RecipeDishSnapshot after = _passiveFlavorMutationEntries[i].After;
                DishDef def = _run?.Database.GetDish(after?.DishId);
                if (dish == null || after == null || def == null)
                {
                    CompleteOne();
                    continue;
                }

                int displayValue = BigNumberSaveData.ToLegacyInt(DishScore.CeilContribution(
                    def.Deliciousness + after.ScoreFlatBonus,
                    after.ScoreMultiplier));
                dish.PlayPassiveFlavorTransform(
                    def,
                    after.FlavorIds,
                    displayValue,
                    CompleteOne);
            }
        }

        private void FlyPassiveFlavorDishesToRecipe(Action onComplete)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            RectTransform layer = canvas != null
                ? canvas.transform as RectTransform
                : transform.root as RectTransform;
            RectTransform target = _infoColumn?.ViewRecipeButtonRect;
            Canvas.ForceUpdateCanvases();
            if (layer == null
                || target == null
                || !TryGetRectInLayer(target, layer, out RectSnapshot end))
            {
                FinishPassiveFlavorMutationPage(onComplete);
                return;
            }

            int remaining = 0;
            bool scheduling = true;
            void CompleteOne(ShopPurchaseFlyView completedFly)
            {
                _passiveFlavorMutationFlys.Remove(completedFly);
                UnregisterShopPurchaseFly(completedFly);
                remaining--;
                if (!scheduling && remaining == 0)
                {
                    FinishPassiveFlavorMutationPage(onComplete);
                }
            }

            foreach (RecipeEditDishView dish in _passiveFlavorMutationDishes)
            {
                RectTransform source = dish?.PassiveMutationFlySource;
                if (source == null
                    || !TryGetRectInLayer(source, layer, out RectSnapshot start))
                {
                    continue;
                }

                RenderTexture texture = dish.CapturePassiveMutationFlyTexture();
                if (texture == null)
                {
                    continue;
                }

                ShopPurchaseFlyView fly = CreateShopPurchaseFly(layer);
                if (fly == null)
                {
                    ReleasePurchaseTexture(texture);
                    continue;
                }

                dish.gameObject.SetActive(false);
                remaining++;
                _passiveFlavorMutationFlys.Add(fly);
                RegisterShopPurchaseFly(fly);
                ShopPurchaseFlyView capturedFly = fly;
                bool flightCompleted = false;
                void CompleteFlight()
                {
                    if (flightCompleted)
                    {
                        return;
                    }

                    flightCompleted = true;
                    CompleteOne(capturedFly);
                }

                try
                {
                    fly.PlayFood(
                        start.Center,
                        start.Size,
                        end.Center,
                        texture,
                        null,
                        CompleteFlight);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, fly);
                    fly.Cancel();
                    CompleteFlight();
                }
            }

            scheduling = false;
            if (remaining == 0)
            {
                FinishPassiveFlavorMutationPage(onComplete);
            }
        }

        private void FinishPassiveFlavorMutationPage(Action onComplete)
        {
            ClosePassiveFlavorMutationPage();
            RefreshPersistent();
            onComplete?.Invoke();
        }

        private void EnsurePassiveFlavorMutationPage()
        {
            if (_passiveFlavorMutationPage != null || _center == null)
            {
                return;
            }

            var page = new GameObject(
                "PassiveFlavorMutationPage",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            RectTransform pageRect = page.GetComponent<RectTransform>();
            pageRect.SetParent(_center.transform, false);
            pageRect.anchorMin = Vector2.zero;
            pageRect.anchorMax = Vector2.one;
            pageRect.offsetMin = Vector2.zero;
            pageRect.offsetMax = Vector2.zero;
            Image background = page.GetComponent<Image>();
            background.color = new Color(0.075f, 0.05f, 0.03f, 0.96f);
            background.raycastTarget = false;
            _passiveFlavorMutationPage = page.GetComponent<CanvasGroup>();

            var content = new GameObject(
                "Dishes",
                typeof(RectTransform));
            _passiveFlavorMutationContent = content.GetComponent<RectTransform>();
            _passiveFlavorMutationContent.SetParent(pageRect, false);
            _passiveFlavorMutationContent.anchorMin = Vector2.zero;
            _passiveFlavorMutationContent.anchorMax = Vector2.one;
            _passiveFlavorMutationContent.offsetMin = Vector2.zero;
            _passiveFlavorMutationContent.offsetMax = Vector2.zero;
            page.SetActive(false);
        }

        private void ClosePassiveFlavorMutationPage()
        {
            if (_passiveFlavorMutationFlys.Count > 0)
            {
                var flys = new List<ShopPurchaseFlyView>(_passiveFlavorMutationFlys);
                _passiveFlavorMutationFlys.Clear();
                foreach (ShopPurchaseFlyView fly in flys)
                {
                    UnregisterShopPurchaseFly(fly);
                    fly?.Cancel();
                }
            }

            ClosePassiveFlavorMutationDishes();
            if (_passiveFlavorMutationPage != null)
            {
                _passiveFlavorMutationPage.gameObject.SetActive(false);
            }
        }

        private void ClosePassiveFlavorMutationDishes()
        {
            foreach (RecipeEditDishView dish in _passiveFlavorMutationDishes)
            {
                if (dish != null)
                {
                    dish.gameObject.SetActive(false);
                    Destroy(dish.gameObject);
                }
            }

            _passiveFlavorMutationDishes.Clear();
            _passiveFlavorMutationEntries.Clear();
        }

        private void EnqueuePassivePresentation(Action<Action> start, Action cancel = null)
        {
            _passivePresentations.Enqueue(start, cancel);
        }

        private void CancelPassivePresentations()
        {
            _passivePresentationDelay?.Kill();
            _passivePresentationDelay = null;
            _infoColumn?.CancelWeekIndexChange(complete: false);
            _timelineAxis?.CancelPresentation();
            _passivePresentations.CancelAll();
        }

        private void DelayPassivePresentation(float seconds, Action onComplete)
        {
            _passivePresentationDelay?.Kill();
            _passivePresentationDelay = DOTween.Sequence()
                .SetUpdate(true)
                .AppendInterval(Mathf.Max(0f, seconds))
                .OnComplete(() =>
                {
                    _passivePresentationDelay = null;
                    onComplete?.Invoke();
                });
        }

        private void WaitPassiveHold(Action onComplete)
        {
            DelayPassivePresentation(2f, onComplete);
        }

        private void EnsurePassivePresentationInputBlocker()
        {
            if (_passivePresentationInputBlocker != null)
            {
                return;
            }

            Transform parent = _hudFrame != null ? _hudFrame.transform : transform;
            var blocker = new GameObject(
                "PassivePresentationInputBlocker",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            RectTransform rect = blocker.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = blocker.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;
            _passivePresentationInputBlocker = blocker.GetComponent<CanvasGroup>();
            blocker.SetActive(false);
        }

        private void SetPassivePresentationInputLocked(bool locked)
        {
            EnsurePassivePresentationInputBlocker();
            if (_passivePresentationInputBlocker == null)
            {
                return;
            }

            GameObject blocker = _passivePresentationInputBlocker.gameObject;
            blocker.SetActive(locked);
            _passivePresentationInputBlocker.alpha = 1f;
            _passivePresentationInputBlocker.interactable = locked;
            _passivePresentationInputBlocker.blocksRaycasts = locked;
            if (locked)
            {
                blocker.transform.SetAsLastSibling();
                HideAllTips();
            }
        }

        private void QueueTimelineMutation(
            TimelineMutationResult result,
            Action onFinished,
            Action onCancelled)
        {
            if (result == null || !result.Changed)
            {
                onFinished?.Invoke();
                return;
            }

            bool restoreAxisVisible = _timelineAxis != null && _timelineAxis.gameObject.activeSelf;
            EnqueuePassivePresentation(done =>
            {
                if (result.BeforeWeekIndex != result.AfterWeekIndex)
                {
                    _infoColumn?.PlayWeekIndexChange(
                        _run,
                        result.BeforeWeekIndex,
                        result.AfterWeekIndex,
                        () => WaitPassiveHold(() =>
                        {
                            RefreshPersistent();
                            done();
                            onFinished?.Invoke();
                        }));
                    if (_infoColumn == null)
                    {
                        WaitPassiveHold(() =>
                        {
                            done();
                            onFinished?.Invoke();
                        });
                    }

                    return;
                }

                SetActionAxisVisible(true);
                void FinishTimelineMutation()
                {
                    WaitPassiveHold(() =>
                    {
                        SetActionAxisVisible(restoreAxisVisible);
                        RebuildActionAxis();
                        RefreshPersistent();
                        done();
                        onFinished?.Invoke();
                    });
                }

                if (_axisBinder != null)
                {
                    _axisBinder.PlayMutation(
                        _run,
                        result,
                        _currentTimelineNodeCard?.Id,
                        FinishTimelineMutation);
                }
                else
                {
                    FinishTimelineMutation();
                }
            }, () =>
            {
                _timelineAxis?.CancelPresentation();
                _infoColumn?.CancelWeekIndexChange(complete: false);
                onCancelled?.Invoke();
            });
        }

        private void RestorePassiveInspection(
            BattleInspectionView restoreView,
            IReadOnlyList<RecipeReadonlyDishEntry> recipeEntries,
            DiningTable table,
            Action onRestored)
        {
            if (_inspectionCoordinator == null)
            {
                RefreshPersistent();
                onRestored?.Invoke();
                return;
            }

            switch (restoreView)
            {
                case BattleInspectionView.Recipe:
                    if (_inspectionCoordinator.ShowPassiveRecipe(recipeEntries, onRestored))
                    {
                        return;
                    }
                    break;
                case BattleInspectionView.Table:
                    if (_inspectionCoordinator.ShowPassiveTable(table, onRestored))
                    {
                        return;
                    }
                    break;
                default:
                    _inspectionCoordinator.Close(onRestored);
                    return;
            }

            RefreshPersistent();
            onRestored?.Invoke();
        }

        private IReadOnlyList<RecipeReadonlyDishEntry> BuildCurrentRecipeEntries()
        {
            var entries = new List<RecipeReadonlyDishEntry>(_run?.RecipeEntries.Count ?? 0);
            if (_run?.RecipeEntries == null)
            {
                return entries;
            }

            foreach (RecipeBookSlot slot in _run.RecipeEntries)
            {
                entries.Add(new RecipeReadonlyDishEntry(slot.Clone()));
            }

            return entries;
        }

        private IReadOnlyList<RecipeReadonlyDishEntry> BuildRecipeMutationEntries(
            RecipeMutationResult result,
            bool before)
        {
            IReadOnlyList<RecipeDishSnapshot> fullRecipe = before
                ? result.BeforeRecipe
                : result.AfterRecipe;
            if (fullRecipe != null && fullRecipe.Count > 0)
            {
                var fullEntries = new List<RecipeReadonlyDishEntry>(fullRecipe.Count);
                foreach (RecipeDishSnapshot snapshot in fullRecipe)
                {
                    fullEntries.Add(new RecipeReadonlyDishEntry(RecipeSlotFromSnapshot(snapshot)));
                }

                return fullEntries;
            }

            var mutations = new Dictionary<int, RecipeMutationEntry>();
            foreach (RecipeMutationEntry entry in result.Entries)
            {
                if (entry.BookIndex == 0)
                {
                    mutations[entry.DishIndex] = entry;
                }
            }

            var entries = new List<RecipeReadonlyDishEntry>(_run?.RecipeEntries.Count ?? 0);
            for (int i = 0; i < (_run?.RecipeEntries.Count ?? 0); i++)
            {
                if (!mutations.TryGetValue(i, out RecipeMutationEntry mutation))
                {
                    entries.Add(new RecipeReadonlyDishEntry(_run.RecipeEntries[i].Clone()));
                    continue;
                }

                RecipeDishSnapshot snapshot = before ? mutation.Before : mutation.After;
                bool hidden = before && (snapshot == null || string.IsNullOrEmpty(snapshot.DishId));
                if (hidden)
                {
                    snapshot = mutation.After;
                }

                entries.Add(new RecipeReadonlyDishEntry(
                    RecipeSlotFromSnapshot(snapshot),
                    initiallyHidden: hidden));
            }

            return entries;
        }

        private RecipeBookSlot RecipeSlotFromSnapshot(RecipeDishSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return new RecipeBookSlot(string.Empty);
            }

            var slot = new RecipeBookSlot(snapshot.DishId);
            DishDef dish = _run?.Database.GetDish(snapshot.DishId);
            bool skippedBaseFlavor = false;
            if (snapshot.FlavorIds != null)
            {
                foreach (string flavorId in snapshot.FlavorIds)
                {
                    if (!skippedBaseFlavor
                        && dish != null
                        && !string.IsNullOrEmpty(dish.FlavorId)
                        && flavorId == dish.FlavorId)
                    {
                        skippedBaseFlavor = true;
                        continue;
                    }

                    slot.AddFlavor(flavorId);
                }
            }

            var baseSkills = new HashSet<string>(dish?.SkillIds ?? Array.Empty<string>());
            if (snapshot.SkillIds != null)
            {
                foreach (string skillId in snapshot.SkillIds)
                {
                    if (!baseSkills.Contains(skillId))
                    {
                        slot.AddExtraSkill(skillId);
                    }
                }
            }

            slot.RestoreScoreFlatBonus(snapshot.ScoreFlatBonus);
            slot.RestoreScoreMultiplier(snapshot.ScoreMultiplier);
            return slot;
        }

        private DiningTable BuildTablePresentationSnapshot()
        {
            DiningTable source = _run?.BuildTablePreviewFromFragments();
            if (source == null)
            {
                return null;
            }

            List<GridPos> cells = source.ExistingCells();
            var snapshot = new DiningTable(source.Width, source.Height, cells);
            foreach (GridPos cell in cells)
            {
                if (source.IsDisabled(cell))
                {
                    snapshot.SetDisabled(cell, true);
                }
            }

            return snapshot;
        }

        private void OnViewTableClicked()
        {
            if (_inspectionCoordinator == null)
            {
                return;
            }

            if (_fragmentEditCoordinator?.IsActive == true)
            {
                return;
            }

            if (IsViewToggleTransitioning())
            {
                return;
            }

            _world = _world ?? BattleWorldController.Instance;
            BindWorldHudAreas();

            RewardForm reward = RewardForm.Active;
            bool managesReward = reward != null && !reward.IsSuspendedForRewardSubflow;
            bool beginsRewardInspection = managesReward && !reward.IsPersistentInspectionActive;
            if (beginsRewardInspection && !reward.TryBeginPersistentInspection())
            {
                return;
            }

            if (!_inspectionCoordinator.OpenTable() && beginsRewardInspection)
            {
                reward.CompletePersistentInspection();
            }
        }

        private void OnExitTableViewClicked()
        {
            if (_inspectionCoordinator == null
                || !_inspectionCoordinator.IsTableVisible
                || IsViewToggleTransitioning())
            {
                return;
            }

            _inspectionCoordinator.Close();
        }

        private bool IsViewToggleTransitioning()
        {
            return (_pageRouter != null && _pageRouter.IsTransitioning)
                || (_center != null && DOTween.IsTweening(_center, true))
                || (_inspectionCoordinator != null && _inspectionCoordinator.IsTransitioning);
        }

        public void ShowRunResult(bool win, BigDouble total)
        {
            ResetBossBattlePresentation();
            _world?.HideWorld();
            HideHud();
            if (win)
            {
                GameApp.UI.OpenUIForm(UIForms.Result, UIForms.GroupDialog, new ResultFormData(true, total));
            }
            else
            {
                ShowDefeatDialog(total);
            }
        }

        private void ShowDefeatDialog(BigDouble total)
        {
            GameRun run = GameRunContext.Current;
            if (run == null)
            {
                return;
            }

            int target = _session?.RequiredScore ?? run.RequiredScore;
            MetaProgressSaveData progress = MetaProgressPersistence.Load();
            MetaProgressUpdate progressUpdate =
                MetaProgressService.EvaluateRunEnd(run, false, total, target, progress);
            SettlementSummary summary =
                SettlementService.Build(run, false, total, target, progressUpdate);

            var data = new ConfirmDialogData
            {
                Title = summary.Title,
                Message = summary.Body,
                ConfirmText = summary.ButtonLabel,
                CancelText = string.Empty,
                OnConfirm = () => CompleteDefeatedRun(progressUpdate),
            };
            GameApp.UI.OpenUIForm(UIForms.ConfirmDialog, UIForms.GroupDialog, data);
        }

        private static void CompleteDefeatedRun(MetaProgressUpdate progressUpdate)
        {
            if (progressUpdate?.Progress != null)
            {
                MetaProgressPersistence.Save(progressUpdate.Progress);
            }

            RunPersistence.Delete();
            GameplayFlowSignal.RequestReturnToMenu();
        }

        public void ShowHeartBreak(HeartBreakFormOpenArgs args, Action onComplete)
        {
            if (args == null)
            {
                onComplete?.Invoke();
                return;
            }

            RefreshAll();
            var openArgs = new HeartBreakFormOpenArgs(
                args.BeforeHeartCount,
                args.AfterHeartCount,
                args.HeartCapacity,
                args.IsTerminal,
                onComplete);
            GameApp.UI.OpenUIForm(UIForms.HeartBreak, UIForms.GroupDialog, openArgs);
        }

        public void ShowStarAward(StarAwardFormOpenArgs args)
        {
            if (args == null)
            {
                return;
            }

            RefreshAll();
            GameApp.UI.OpenUIForm(UIForms.StarAward, UIForms.GroupDialog, args);
        }

        // —— 经营挑战 ——

        public void StartBattle(
            int requiredScore,
            string modifier,
            string key,
            string bossDebuffId,
            ActionExecutionContext actionContext)
        {
            _pageRouter?.CancelTransition();
            _battleEntryPresentation?.ResetImmediate();
            CancelBossPresentation();
            HideResultPanel();
            _infoColumn?.SetBattleScoreOverride(null);
            _infoColumn?.ScoreFire?.Hide();
            _activeBattleRawRequiredScore = requiredScore;
            _activeBattleModifier = modifier ?? string.Empty;
            _activeBossDebuffId = bossDebuffId ?? string.Empty;
            _activeBattleKey = key ?? string.Empty;
            _activeBattleIsBoss = IsBossFoodAction(actionContext);
            _currentBossDebuff = _activeBattleIsBoss ? ResolveBossDebuff(_activeBossDebuffId) : null;
            _infoColumn?.SetBossBattlePresentation(null, false, animate: false);
            SetMessage(string.Empty);
            UnsubscribeCakeLayerChanges();
            SetSession(_run.BuildBattleSession(requiredScore, modifier, key, _activeBossDebuffId));
            _battleEntryPresentation.Prepare(
                _session.RequiredScore,
                _activeBattleIsBoss ? _currentBossDebuff : null);
            string analyticsBossId = _activeBattleIsBoss
                ? FoodService.ResolveBoss(_run, actionContext?.Action)?.Id ?? string.Empty
                : string.Empty;
            GameAnalyticsService.TrackBattleStarted(
                _run,
                _activeBattleKey,
                _activeBattleIsBoss,
                analyticsBossId,
                _session.RequiredScore,
                _run.HeartsRemaining);
            BossDebuffPresentationPlan bossPlan = _session.BossDebuffPresentation;
            _bossDiscardRevealPending = _activeBattleIsBoss
                && string.Equals(bossPlan?.DebuffId, "debuff_omakase", StringComparison.Ordinal);
            if (_activeBattleIsBoss && bossPlan != null && GameApp.Random != null)
            {
                _bossDialogueBag = new BossDialogueShuffleBag(
                    bossPlan.Dialogues,
                    GameApp.Random.Cosmetic($"boss_dialogue_{key}_{bossPlan.DebuffId}"));
                _bossPresentationCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            }
            BeginFoodDiscardCapacityTracking();
            _displayedCakeLayers = _session.HappyCakeLayers;
            _pendingSettlementCakeLayers = null;
            _session.Served += OnBattleServed;
            _session.HappyCakeLayersChanged += OnHappyCakeLayersChanged;
            _world = BattleWorldController.Instance;
            if (_world == null)
            {
                _battleEntryPresentation.ResetImmediate();
                Log.Error("BattleForm: battle scene controller not found (scene not loaded?).", Tag);
                return;
            }

            bool runOpeningPresentation = _activeBattleIsBoss
                && bossPlan != null
                && (bossPlan.HasIntroPresentation
                    || string.Equals(bossPlan.DebuffId, "debuff_carb_meal", StringComparison.Ordinal));

            // 世界空间对象不受 Center CanvasGroup 控制，因此必须在中心遮罩完全覆盖后初始化；
            // Boss 开场演出则等遮罩揭开，避免首个手势或对白被过渡吞掉。
            SwitchTo(
                GameplayView.Food,
                () =>
                {
                    StartBattleMusic();
                    BindWorldHudAreas();
                    if (string.Equals(bossPlan?.DebuffId, "debuff_gluttony", StringComparison.Ordinal))
                    {
                        _foodBar?.SetRecipeCountPresentationOverride(bossPlan.InitialRecipeEntryCount);
                    }

                    _world.Initialize(
                        _run,
                        _session,
                        SetMessage,
                        SetSettlementScore,
                        RefreshAll,
                        null,
                        OnDishClicked,
                        serveTriggerCueSink: OnServeTriggerCue,
                        pendingDishConfirmRequested: OnPendingDishConfirmRequested,
                        prepareNextDish: !runOpeningPresentation);
                    if (_activeBattleIsBoss && bossPlan != null)
                    {
                        _world.StageBossPresentation(bossPlan);
                    }

                    _world.SetDishHoverCallbacks(OnDishHoverEntered, OnDishHoverExited);
                    _world.SetCellHoverCallbacks(OnCellHoverEntered, OnCellHoverExited);
                    _world.SetTableFragmentHoverCallbacks(OnTableFragmentHoverEntered, OnTableFragmentHoverExited);
                    RefreshAll();
                },
                () =>
                {
                    _infoColumn?.SetBossBattlePresentation(
                        _currentBossDebuff,
                        _activeBattleIsBoss,
                        animate: _activeBattleIsBoss && _currentBossDebuff != null);
                    _ = PlayBattleOpeningPresentationAsync(bossPlan, runOpeningPresentation);
                },
                _battleEntryPresentation.BuildPresentationTween,
                _battleEntryPresentation.BindSkipHandler);
        }

        private void OnHappyCakeLayersChanged(int before, int after)
        {
            if (_settlementReveal != null)
            {
                _pendingSettlementCakeLayers = after;
                return;
            }

            ApplyCakeLayerPresentation(after);
        }

        private async Awaitable PlayBattleOpeningPresentationAsync(
            BossDebuffPresentationPlan plan,
            bool playBossIntro)
        {
            BattleSession openingSession = _session;
            BattleWorldController openingWorld = _world;
            bool stagedBossPresentation = _activeBattleIsBoss && plan != null;
            bool readyForOrderHint = false;
            CancellationToken openingToken = EnsureBossPresentationCancellationToken();

            void FinishStagedTablePresentation()
            {
                bool stillCurrent = ReferenceEquals(_session, openingSession)
                    && ReferenceEquals(_world, openingWorld);
                if (stagedBossPresentation && stillCurrent)
                {
                    openingWorld?.FinishBossPresentation();
                }

                if (stillCurrent)
                {
                    _foodBar?.SetRecipeCountPresentationOverride(null);
                    RefreshAll();
                }
            }

            if (playBossIntro)
            {
                await PlayBossLockedAsync(async token =>
                {
                    openingToken = token;
                    await Awaitable.NextFrameAsync(token);
                    try
                    {
                        switch (plan.DebuffId)
                        {
                            case "debuff_vegetarian":
                                await SweepCellsAsync(
                                    plan.DisabledCells,
                                    openingWorld.RevealBossDisabledCell,
                                    token);
                                await ShowBossDialogueAsync("这几块别放肉。", token);
                                break;

                            case "debuff_kids_meal":
                                await ShowBossDialogueAsync("我要少吃点", token);
                                await SweepCellsAsync(
                                    plan.DisabledCells,
                                    openingWorld.RevealBossDisabledCell,
                                    token);
                                break;

                            case "debuff_indulgent":
                            case "debuff_binge":
                                await ShowBossDialogueAsync("我要多吃点", token);
                                await SweepCellsAsync(plan.AddedCells, openingWorld.RevealBossAddedCell, token);
                                break;

                            case "debuff_weight_loss":
                                await ShowBossDialogueAsync("我要少吃点", token);
                                await SweepCellsAsync(plan.RemovedCells, openingWorld.RevealBossRemovedCell, token);
                                break;

                            case "debuff_gluttony":
                                await ShowBossDialogueAsync("都给我再来一份", token);
                                await PlayGluttonyRecipeFlyAsync(plan, token);
                                break;

                            case "debuff_omakase":
                                await ShowBossDialogueAsync("我不喜欢浪费", token);
                                _bossDiscardRevealPending = false;
                                ResolveFoodDiscardBin()?.SetVisible(true);
                                break;
                        }
                    }
                    finally
                    {
                        FinishStagedTablePresentation();
                    }

                    token.ThrowIfCancellationRequested();
                    readyForOrderHint = true;
                });
            }
            else
            {
                try
                {
                    await Awaitable.NextFrameAsync(openingToken);
                    FinishStagedTablePresentation();
                    openingToken.ThrowIfCancellationRequested();
                    readyForOrderHint = true;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            bool stillCurrent = ReferenceEquals(_session, openingSession)
                && ReferenceEquals(_world, openingWorld);
            if (!readyForOrderHint || !stillCurrent || openingToken.IsCancellationRequested)
            {
                return;
            }

            // 出菜和顺序提示都在 Boss 全屏输入锁释放后进行；提示只是视觉引导，玩家可立即操作。
            openingWorld.EnsureNextDishPrepared(0, allowDuringBossPresentation: true);
            // EnsureNextDishPrepared 只刷新世界表现；延迟出菜时必须同步重绑 HUD 出菜口。
            // 即使数据层已先一步准备好食物，也要执行，避免出菜口残留 NoDishCanServe。
            RefreshAll();
            try
            {
                await openingWorld.PlaySettlementOrderHintAsync(
                    openingSession?.ReverseSettlementOrder == true,
                    openingToken);
                await ShowCarbDialogueIfNeededAsync(openingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            PlayBattleTutorialIfNeeded();
        }

        private bool _pendingDishConfirmationInProgress;

        private async void OnPendingDishConfirmRequested(int dishId)
        {
            if (_session == null
                || _session.IsSettled
                || _pendingDishConfirmationInProgress
                || _bossPresentation?.IsPlaying == true
                || (_world != null && _world.IsFoodInteractionBusy))
            {
                return;
            }

            _pendingDishConfirmationInProgress = true;
            try
            {
                await PlayBossLockedAsync(token =>
                    ConfirmPendingDishPresentationAsync(dishId, prepareNextDish: true, token));
            }
            finally
            {
                _pendingDishConfirmationInProgress = false;
            }
        }

        private async Awaitable ConfirmPendingDishPresentationAsync(
            int dishId,
            bool prepareNextDish,
            CancellationToken token)
        {
            if (_world == null)
            {
                return;
            }

            PendingDishConfirmResult result = _world.ConfirmPendingDishForPresentation(dishId);
            if (!result.Success)
            {
                _world.FinalizePendingDishPresentation(result, prepareNextDish: false);
                return;
            }

            try
            {
                // Commit the dish's served appearance before any OnServe cue or boss reveal.
                // Data-side OnServe hooks have already resolved inside ConfirmPendingDishForPresentation.
                await _world.CommitPendingDishVisualStateAsync(result, token);

                string debuffId = _session?.BossDebuffPresentation?.DebuffId ?? string.Empty;
                if (result.ActionKind == PendingDishActionKind.Serve)
                {
                    // 数值提示只是视觉反馈，不应占用 Boss 全屏输入锁；队列会在世界层自行串行播放。
                    _world.PlayPendingServeTriggerCues();

                    if (string.Equals(debuffId, "debuff_appetizer", StringComparison.Ordinal)
                        && result.RemovedAfterServe)
                    {
                        if (_world.TryGetDishGrabVisual(result.Dish.Id, out DishGrabVisualSnapshot dishVisual))
                        {
                            await _bossPresentation.GrabDishAsync(
                                dishVisual,
                                () => _world.SetDishPresentationVisible(result.Dish.Id, false),
                                token);
                        }
                        else
                        {
                            _world.SetDishPresentationVisible(result.Dish.Id, false);
                        }

                        await ShowBossDialogueAsync("我先吃一点", token);
                    }
                }
            }
            finally
            {
                // Confirmation already changed gameplay state. Always reconcile the world view,
                // even when page closure cancels a hand/cue tween midway through the sequence.
                // Prepare the next dish before the same final UI refresh so the outlet never
                // renders the legacy transient "no dish can serve" state between dishes.
                _world?.FinalizePendingDishPresentation(
                    result,
                    prepareNextDish,
                    allowPrepareDuringBossPresentation: true);
            }

            if (prepareNextDish)
            {
                await ShowCarbDialogueIfNeededAsync(token);
            }
        }

        private async Awaitable ShowCarbDialogueIfNeededAsync(CancellationToken token)
        {
            if (string.Equals(
                    _session?.BossDebuffPresentation?.DebuffId,
                    "debuff_carb_meal",
                    StringComparison.Ordinal)
                && _session?.PreparedServe?.IsBossInsertedDish == true)
            {
                await ShowBossDialogueAsync("先给我来点这个", token);
            }
        }

        private async Awaitable SweepCellsAsync(
            IReadOnlyList<GridPos> cells,
            Action<GridPos> reveal,
            CancellationToken token)
        {
            var points = new List<Vector2>();
            var visibleCells = new List<GridPos>();
            foreach (GridPos cell in OrderCellsForBossPointing(cells))
            {
                if (_world.TryGetCellScreenPoint(cell, out Vector2 point))
                {
                    points.Add(point);
                    visibleCells.Add(cell);
                }
                else
                {
                    reveal?.Invoke(cell);
                }
            }

            await _bossPresentation.SweepAsync(
                points,
                index => reveal?.Invoke(visibleCells[index]),
                token);
        }

        /// <summary>Boss 逐格指向统一从最下行开始，每行由右向左，再逐行向上。</summary>
        private static List<GridPos> OrderCellsForBossPointing(IReadOnlyList<GridPos> cells)
        {
            var ordered = cells != null
                ? new List<GridPos>(cells)
                : new List<GridPos>();
            ordered.Sort((left, right) =>
            {
                int rowOrder = right.Y.CompareTo(left.Y);
                return rowOrder != 0
                    ? rowOrder
                    : right.X.CompareTo(left.X);
            });
            return ordered;
        }

        private async Awaitable PlayGluttonyRecipeFlyAsync(
            BossDebuffPresentationPlan plan,
            CancellationToken token)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            RectTransform layer = canvas != null
                ? canvas.transform as RectTransform
                : transform.root as RectTransform;
            RectTransform target = _foodBar?.RecipeInfoButtonRect;
            Canvas.ForceUpdateCanvases();
            if (layer == null
                || _run == null
                || target == null
                || !TryGetRectInLayer(target, layer, out RectSnapshot recipeInfoButtonRect)
                || plan.DuplicatedDishIds.Count == 0)
            {
                return;
            }

            bool finished = false;
            bool started = PlayRecipeCopyFlys(
                plan.DuplicatedDishIds,
                layer,
                _boardArea,
                recipeInfoButtonRect.Center,
                null,
                true,
                arrived =>
                {
                    _foodBar?.SetRecipeCountPresentationOverride(
                        plan.InitialRecipeEntryCount + arrived);
                    RefreshFoodActions();
                },
                () => finished = true,
                _bossRecipeCopyFlys);
            if (!started)
            {
                return;
            }

            while (!finished)
            {
                token.ThrowIfCancellationRequested();
                await Awaitable.NextFrameAsync(token);
            }
        }

        private bool PlayRecipeCopyFlys(
            IReadOnlyList<string> dishIds,
            RectTransform layer,
            RectTransform sourceArea,
            Vector2 targetCenter,
            IRandomStream cosmetic,
            bool arrangeStartPoints,
            Action<int> onArrived,
            Action onFinished,
            HashSet<ShopPurchaseFlyView> ownedFlys)
        {
            RectTransform centerRect = sourceArea != null
                ? sourceArea
                : _center != null
                    ? _center.transform as RectTransform
                    : null;
            if (dishIds == null
                || dishIds.Count == 0
                || layer == null
                || (!arrangeStartPoints && cosmetic == null)
                || _run == null
                || centerRect == null
                || !TryGetRectInLayer(centerRect, layer, out RectSnapshot centerArea))
            {
                return false;
            }

            var spriteProvider = new DishSpriteProvider();
            int arrived = 0;
            int finished = 0;
            int total = dishIds.Count;
            bool scheduling = true;
            bool allFinished = false;

            void CompleteArrival()
            {
                arrived++;
                onArrived?.Invoke(arrived);
            }

            void CompleteFlight(ShopPurchaseFlyView completedFly)
            {
                if (completedFly != null)
                {
                    ownedFlys?.Remove(completedFly);
                    UnregisterShopPurchaseFly(completedFly);
                }

                finished++;
                if (!scheduling && finished >= total && !allFinished)
                {
                    allFinished = true;
                    onFinished?.Invoke();
                }
            }

            for (int dishIndex = 0; dishIndex < dishIds.Count; dishIndex++)
            {
                string dishId = dishIds[dishIndex];
                ShopPurchaseFlyView fly = CreateShopPurchaseFly(layer);
                if (fly == null)
                {
                    CompleteArrival();
                    CompleteFlight(null);
                    continue;
                }

                RegisterShopPurchaseFly(fly);
                ownedFlys?.Add(fly);
                ShopPurchaseFlyView capturedFly = fly;
                bool flightArrived = false;
                bool flightFinished = false;
                void ArriveOnce()
                {
                    if (flightArrived)
                    {
                        return;
                    }

                    flightArrived = true;
                    CompleteArrival();
                }

                void FinishOnce()
                {
                    if (flightFinished)
                    {
                        return;
                    }

                    flightFinished = true;
                    CompleteFlight(capturedFly);
                }

                Vector2 start = arrangeStartPoints
                    ? RecipeCopyStartPoint(centerArea, dishIndex, dishIds.Count)
                    : RandomPointInRecipeCopyArea(centerArea, cosmetic);
                Sprite sprite = spriteProvider.Get(_run.Database.GetDish(dishId));
                try
                {
                    fly.PlayFoodSprite(
                        start,
                        Vector2.one * RecipeCopySpriteSize,
                        targetCenter,
                        sprite,
                        ArriveOnce,
                        FinishOnce);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, fly);
                    fly.Cancel();
                    ArriveOnce();
                    FinishOnce();
                }
            }

            scheduling = false;
            if (finished >= total && !allFinished)
            {
                allFinished = true;
                onFinished?.Invoke();
            }

            return true;
        }

        private static Vector2 RecipeCopyStartPoint(
            RectSnapshot centerArea,
            int index,
            int total)
        {
            int safeTotal = Mathf.Max(1, total);
            int columns = Mathf.Min(RecipeCopyMaxColumns, safeTotal);
            int rows = Mathf.CeilToInt(safeTotal / (float)columns);
            int row = Mathf.Clamp(index / columns, 0, rows - 1);
            int column = Mathf.Max(0, index % columns);
            int itemsInRow = Mathf.Min(columns, safeTotal - row * columns);

            float availableHalfWidth = Mathf.Max(
                0f,
                centerArea.Size.x * 0.5f - RecipeCopyCenterMargin - RecipeCopySpriteSize * 0.5f);
            float availableHalfHeight = Mathf.Max(
                0f,
                centerArea.Size.y * 0.5f - RecipeCopyCenterMargin - RecipeCopySpriteSize * 0.5f);
            float horizontalSpacing = itemsInRow > 1
                ? Mathf.Min(RecipeCopyHorizontalSpacing, availableHalfWidth * 2f / (itemsInRow - 1))
                : 0f;
            float verticalSpacing = rows > 1
                ? Mathf.Min(RecipeCopyVerticalSpacing, availableHalfHeight * 2f / (rows - 1))
                : 0f;

            float x = centerArea.Center.x
                + (column - (itemsInRow - 1) * 0.5f) * horizontalSpacing;
            float y = centerArea.Center.y
                + ((rows - 1) * 0.5f - row) * verticalSpacing;
            return new Vector2(x, y);
        }

        private static Vector2 RandomPointInRecipeCopyArea(
            RectSnapshot area,
            IRandomStream cosmetic)
        {
            float halfWidth = Mathf.Max(
                0f,
                area.Size.x * 0.5f - RecipeCopyCenterMargin);
            float halfHeight = Mathf.Max(
                0f,
                area.Size.y * 0.5f - RecipeCopyCenterMargin);
            float x = halfWidth > 0f
                ? cosmetic.Range(area.Center.x - halfWidth, area.Center.x + halfWidth)
                : area.Center.x;
            float y = halfHeight > 0f
                ? cosmetic.Range(area.Center.y - halfHeight, area.Center.y + halfHeight)
                : area.Center.y;
            return new Vector2(x, y);
        }

        private void CancelRecipeCopyFlys(HashSet<ShopPurchaseFlyView> ownedFlys)
        {
            if (ownedFlys == null || ownedFlys.Count == 0)
            {
                return;
            }

            var flys = new List<ShopPurchaseFlyView>(ownedFlys);
            ownedFlys.Clear();
            foreach (ShopPurchaseFlyView fly in flys)
            {
                UnregisterShopPurchaseFly(fly);
                fly?.Cancel();
            }
        }

        private async Awaitable ShowBossDialogueAsync(
            string fallback,
            CancellationToken token,
            float extraHoldSeconds = 0f)
        {
            string dialogue = _bossDialogueBag?.Draw();
            await _bossPresentation.ShowDialogueAsync(
                string.IsNullOrEmpty(dialogue) ? fallback : dialogue,
                token,
                extraHoldSeconds);
        }

        private async Awaitable PlayBossLockedAsync(Func<CancellationToken, Awaitable> sequence)
        {
            if (_bossPresentation == null || _world == null)
            {
                return;
            }

            int presentationVersion = ++_bossPresentationVersion;
            BattleWorldController presentationWorld = _world;
            CancellationToken presentationToken = EnsureBossPresentationCancellationToken();
            presentationWorld.SetBossPresentationBusy(true);
            try
            {
                await _bossPresentation.PlayLockedAsync(sequence, presentationToken);
            }
            finally
            {
                if (_bossPresentationVersion == presentationVersion)
                {
                    presentationWorld?.SetBossPresentationBusy(false);
                    RefreshAll();
                }
            }
        }

        private CancellationToken EnsureBossPresentationCancellationToken()
        {
            if (_bossPresentationCts == null || _bossPresentationCts.IsCancellationRequested)
            {
                _bossPresentationCts?.Dispose();
                _bossPresentationCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            }

            return _bossPresentationCts.Token;
        }

        private void CancelBossPresentation()
        {
            _bossPresentationVersion++;
            _bossPresentationCts?.Cancel();
            _bossPresentationCts?.Dispose();
            _bossPresentationCts = null;
            _bossDialogueBag = null;
            _bossDiscardRevealPending = false;
            _bossPresentation?.CancelCurrent();
            CancelRecipeCopyFlys(_bossRecipeCopyFlys);
            (_world ?? BattleWorldController.Instance)?.SetBossPresentationBusy(false);
            _foodBar?.SetRecipeCountPresentationOverride(null);
        }

        private void ApplyCakeLayerPresentation(int targetLayers)
        {
            int before = _displayedCakeLayers;
            _displayedCakeLayers = Mathf.Max(0, targetLayers);
            RefreshCakeLayerBuff();
            (_world ?? BattleWorldController.Instance)?.PlayCakeLayerChange(before, _displayedCakeLayers);
        }

        private void UnsubscribeCakeLayerChanges()
        {
            if (_session != null)
            {
                _session.HappyCakeLayersChanged -= OnHappyCakeLayersChanged;
            }
        }

        private void OnBattleServed(DishInstance dish, int servesUsed)
        {
            RefreshPersistent();
        }

        private void OnServeTriggerCue(ServeTriggerCue cue)
        {
            if (cue == null)
            {
                return;
            }

            if (cue.PulseSource)
            {
                if (cue.SourceKind == ServeCueSourceKind.PassiveItem && _run != null)
                {
                    new ItemRuntime(_run).FlashTriggered(model =>
                        string.Equals(model.ItemId, cue.SourceId, StringComparison.Ordinal));
                }
                else if (cue.SourceKind == ServeCueSourceKind.BossDebuff)
                {
                    _infoColumn?.PlayBossDebuffTrigger(cue.SourceId);
                }
            }

            RefreshPersistent(refreshItems: false);
            if (_hoveredDishPiece?.Instance != null
                && _hoveredDishPiece.Instance.Id == cue.DishId)
            {
                RebindHoveredDishTips(_hoveredDishPiece);
            }
        }

        private cfg.BossDebuff ResolveBossDebuff(string bossDebuffId)
        {
            if (string.IsNullOrEmpty(bossDebuffId))
            {
                return null;
            }

            cfg.Tables tables = _run?.Tables ?? GameApp.Config?.Tables;
            if (tables?.TbBossDebuff == null)
            {
                return null;
            }

            return tables.TbBossDebuff.GetOrDefault(bossDebuffId);
        }

        private bool IsBossFoodAction(ActionExecutionContext actionContext)
        {
            return actionContext != null && FoodService.IsBossAction(_run?.Tables, actionContext.Action);
        }

        private void OnDishHoverEntered(DishPieceView piece)
        {
            if (_foodTipsHoverOwner == FoodTipsHoverOwner.Tutorial)
            {
                return;
            }

            if (RewardForm.Active?.BlocksBattleWorldHover == true)
            {
                HideFoodTips();
                return;
            }

            if (_current != GameplayView.Food)
            {
                return;
            }

            if (piece == null || piece.Instance == null || _session == null || _tips == null)
            {
                return;
            }

            FoodTipsView tips = _tips.Food;
            if (tips == null)
            {
                return;
            }

            _hoveredDishPiece = piece;
            _hoveredCell = null;
            _foodTipsHoverOwner = FoodTipsHoverOwner.TableDish;
            RebindHoveredDishTips(piece);
            (_world ?? BattleWorldController.Instance)?.ShowDishScopeHighlights(piece.Instance);
        }

        private bool OnServingOutletDishHoverEntered(ServingOutletView servingOutlet)
        {
            if (_foodTipsHoverOwner == FoodTipsHoverOwner.Tutorial)
            {
                return true;
            }

            if (_current != GameplayView.Food
                || servingOutlet == null
                || _session?.PreparedServe?.Dish == null
                || _tips == null)
            {
                return false;
            }

            FoodTipsView tips = _tips.Food;
            if (tips == null)
            {
                return false;
            }

            _hoveredDishPiece = null;
            _hoveredCell = null;
            _foodTipsHoverOwner = FoodTipsHoverOwner.ServingOutlet;
            tips.Bind(_session.PreparedServe.Dish, null, _session.Database);
            tips.Show();
            tips.transform.SetAsLastSibling();

            BattleWorldController world = _world ?? BattleWorldController.Instance;
            world?.ClearDishScopeHighlights();
            tips.PlaceAroundRectTransform(
                servingOutlet.TipPlacementTarget,
                GetComponentInParent<Canvas>());
            return true;
        }

        private void OnServingOutletDishHoverExited()
        {
            if (_foodTipsHoverOwner != FoodTipsHoverOwner.ServingOutlet)
            {
                return;
            }

            HideFoodTips();
        }

        private void RebindHoveredDishTips(DishPieceView piece)
        {
            if (_current != GameplayView.Food)
            {
                HideFoodTips();
                return;
            }

            if (piece == null || piece.Instance == null || _session == null || _tips == null)
            {
                return;
            }

            FoodTipsView tips = _tips.Food;
            if (tips == null)
            {
                return;
            }

            // 结算演出进行中：只显示已被演出揭示到的分数/倍率/技能；演出走完后（_settlementReveal 清空）恢复完整结果。
            if (_settlementReveal != null && _settlementReveal.TryBuildReveal(piece.Instance, out FoodTipsReveal reveal))
            {
                tips.Bind(FoodTipsDataFactory.BuildRevealed(
                    piece.Instance,
                    _session.DiningTable,
                    _session.Database,
                    reveal,
                    _session.LastResult));
            }
            else
            {
                ScoreResult preview = _session.IsSettled ? _session.LastResult : null;
                tips.Bind(FoodTipsDataFactory.Build(
                    piece.Instance,
                    _session.DiningTable,
                    _session.Database,
                    preview,
                    _session.IsSettled
                        ? null
                        : Math.Max(1, piece.Instance.EffectiveCountAs + _session.ExtraCountAsPerDish)));
            }

            tips.Show();
            tips.transform.SetAsLastSibling();
            tips.PlaceAroundWorldBounds(piece.WorldBounds, Camera.main, GetComponentInParent<Canvas>());
        }

        private void OnSettlementReveal(SettlementRevealSignal signal)
        {
            if (_discardSettlementCallbacks || _settlementReveal == null || signal.IsEmpty)
            {
                return;
            }

            if (signal.HasFlat)
            {
                _settlementReveal.RevealFlat(signal.DishInstanceId, signal.Flat);
            }

            if (signal.HasMultiplier)
            {
                _settlementReveal.RevealMultiplier(signal.DishInstanceId, signal.Multiplier);
            }

            if (signal.CopySkillDelta > 0)
            {
                _settlementReveal.RevealCopiedSkills(signal.DishInstanceId, signal.CopySkillDelta);
            }

            if (signal.TransferredDelta > 0)
            {
                _settlementReveal.RevealTransferred(signal.DishInstanceId, signal.TransferredDelta);
            }

            if (signal.HasCountAs)
            {
                _settlementReveal.RevealCountAs(signal.DishInstanceId, signal.CountAs);
            }

            if (signal.TemporaryEffectDelta > 0)
            {
                _settlementReveal.RevealTemporaryEffects(
                    signal.DishInstanceId,
                    signal.TemporaryEffectDelta);
            }

            if (signal.HasCakeLayer)
            {
                int delta = signal.CakeLayerDelta;
                if (delta > 0 && _pendingSettlementCakeLayerBonus > 0)
                {
                    delta += _pendingSettlementCakeLayerBonus;
                    _pendingSettlementCakeLayerBonus = 0;
                }

                ApplyCakeLayerPresentation(_displayedCakeLayers + delta);
            }

            // 若正 hover 这道菜，立即把刚揭示的信息刷到 tips 上。
            if (_hoveredDishPiece != null
                && _hoveredDishPiece.Instance != null
                && _hoveredDishPiece.Instance.Id == signal.DishInstanceId)
            {
                RebindHoveredDishTips(_hoveredDishPiece);
            }
        }

        private void OnDishHoverExited(DishPieceView piece)
        {
            if (_foodTipsHoverOwner != FoodTipsHoverOwner.TableDish)
            {
                return;
            }

            if (_hoveredDishPiece != null && piece != null && _hoveredDishPiece != piece)
            {
                return;
            }

            HideFoodTips();
        }

        private void OnCellHoverEntered(GridPos cell)
        {
            HideCellMaterialTips(cell);
        }

        private void OnCellHoverExited(GridPos cell)
        {
            HideCellMaterialTips(cell);
        }

        private void HideCellMaterialTips(GridPos cell)
        {
            if (_foodTipsHoverOwner != FoodTipsHoverOwner.TableCell)
            {
                return;
            }

            if (_hoveredCell.HasValue && !_hoveredCell.Value.Equals(cell))
            {
                return;
            }

            HideFoodTips();
        }

        private void OnTableFragmentHoverEntered(TableFragmentHoverInfo info)
        {
            HideTableFragmentMaterialTips(info);
        }

        private void OnTableFragmentHoverExited(TableFragmentHoverInfo info)
        {
            HideTableFragmentMaterialTips(info);
        }

        private void HideTableFragmentMaterialTips(TableFragmentHoverInfo info)
        {
            if (_foodTipsHoverOwner != FoodTipsHoverOwner.TableFragment)
            {
                return;
            }

            if (_hoveredTableFragmentSession != info.SessionVersion
                || _hoveredTableFragmentIndex != info.CandidateIndex)
            {
                return;
            }

            HideFoodTips();
        }

        private void HideFoodTips()
        {
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.FoodTips);
            _hoveredDishPiece = null;
            _hoveredCell = null;
            _hoveredTableFragmentSession = -1;
            _hoveredTableFragmentIndex = -1;
            _foodTipsHoverOwner = FoodTipsHoverOwner.None;
            (_world ?? BattleWorldController.Instance)?.ClearDishScopeHighlights();
            if (_tips != null)
            {
                FoodTipsView foodTips = _tips.Food;
                if (foodTips != null)
                {
                    foodTips.Hide();
                }
            }
        }

        private void ClearWorldHoverCallbacks()
        {
            BattleWorldController world = _world != null ? _world : BattleWorldController.Instance;
            if (world == null)
            {
                return;
            }

            world.SetDishHoverCallbacks(null, null);
            world.SetCellHoverCallbacks(null, null);
            world.SetTableFragmentHoverCallbacks(null, null);
        }

        private async void OnEatClicked()
        {
            if (_rewardPeekOnly || HasPendingBattleRewardLifecycle)
            {
                return;
            }

            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if (_session == null
                || _session.IsSettled
                || (world != null && world.IsFoodInteractionBusy))
            {
                return;
            }

            world?.ClearDoodle();
            TutorialRuntime.Publish(TutorialSignal.SettleClicked);

            // if (_session.DiningTable.DishCount == 0)
            // {
            //     SetMessage("餐桌还是空的，先上几个食物吧。");
            //     return;
            // }

            await PlayBossLockedAsync(async token =>
            {
                var pendingIds = new List<int>();
                foreach (PendingDishPlacement pending in _session.PendingDishPlacements)
                {
                    if (pending.IsOnDiningTable)
                    {
                        pendingIds.Add(pending.Dish.Id);
                    }
                }

                foreach (int dishId in pendingIds)
                {
                    await ConfirmPendingDishPresentationAsync(
                        dishId,
                        prepareNextDish: false,
                        token);
                }

                string debuffId = _session.BossDebuffPresentation?.DebuffId ?? string.Empty;
                if (string.Equals(debuffId, "debuff_late_night", StringComparison.Ordinal))
                {
                    await ShowBossDialogueAsync("最后上的，先吃。", token);
                }
                else if (string.Equals(debuffId, "debuff_buffet", StringComparison.Ordinal)
                    && _session.ServesUsed < _session.MinimumServesForScore)
                {
                    await ShowBossDialogueAsync("就这点够谁吃", token, extraHoldSeconds: 2f);
                }
            });

            if (_discardSettlementCallbacks || Active != this || _session == null || _session.IsSettled)
            {
                return;
            }

            _world ??= BattleWorldController.Instance;
            _world?.SetFoodSettlementLayoutBusy(true);
            RefreshFoodActions();
            await PlayFoodSettlementLayoutAsync();

            if (_discardSettlementCallbacks || Active != this || _session == null || _session.IsSettled)
            {
                _world?.SetFoodSettlementLayoutBusy(false);
                RefreshFoodActions();
                return;
            }

            if (TryPlayFirstBattleSettlementOrderHint())
            {
                return;
            }

            ContinueFoodSettlementAfterLayout();
        }

        private void ContinueFoodSettlementAfterLayout()
        {
            if (_discardSettlementCallbacks
                || Active != this
                || _run == null
                || _session == null
                || _session.IsSettled)
            {
                if (Active == this)
                {
                    _world?.SetFoodSettlementLayoutBusy(false);
                    RefreshFoodActions();
                }
                return;
            }

            // 结算前拍基线：演出用它逐 cue 揭示，hover tips 与表演同步，而非一上来就显示全部结算信息。
            var reveal = new SettlementRevealState();
            var settlementBaseline = new SettlementBaselineSnapshot();
            foreach (DishInstance dish in _session.DiningTable.Dishes)
            {
                reveal.CaptureBaseline(
                    dish,
                    Math.Max(1, dish.EffectiveCountAs + _session.ExtraCountAsPerDish));
                settlementBaseline.Capture(dish);
            }

            _settlementReveal = reveal;
            _pendingSettlementCakeLayers = null;
            _pendingSettlementCakeLayerBonus = 0;

            BeginSettlementPassivePresentation();
            ScoreResult result = _session.Settle();
            _pendingSettlementCakeLayerBonus = Mathf.Max(
                0,
                _session.HappyCakeLayers - _displayedCakeLayers - result.HappyCakeLayerDelta);
            BattleSettlementApplier.ApplyRecipeGrowth(_run, _session);
            SetSettlementScore(0);
            _infoColumn?.BeginSettlementScorePresentation();
            RefreshFoodActions();

            if (_world != null)
            {
                _world.PlaySettlement(
                    result,
                    settlementBaseline,
                    _infoColumn != null ? _infoColumn.ScoreFire : null,
                    OnSettlementReveal,
                    OnSettlementPassiveTriggered,
                    OnSettlementPassivePresentation,
                    OnSettlementBeat,
                    () => OnSettlementComplete(result));
                _world.SetFoodSettlementLayoutBusy(false);
            }
            else
            {
                OnSettlementComplete(result);
            }
        }

        private async void OnSettlementComplete(ScoreResult result)
        {
            if (_discardSettlementCallbacks || Active != this || _loop == null)
            {
                return;
            }

            StopBattleMusic(0.8f);
            _infoColumn?.EndSettlementScorePresentation();

            // 演出走完：清空渐进揭示态，hover 恢复展示完整结算结果。
            _settlementReveal = null;
            if (_pendingSettlementCakeLayers.HasValue)
            {
                int targetLayers = _pendingSettlementCakeLayers.Value;
                _pendingSettlementCakeLayers = null;
                ApplyCakeLayerPresentation(targetLayers);
            }
            _pendingSettlementCakeLayerBonus = 0;
            if (_hoveredDishPiece != null)
            {
                RebindHoveredDishTips(_hoveredDishPiece);
            }

            // 美味值已汇总且营业成败已经可以判定后，逐条展示移除判定，再统一按倒序修改食谱。
            if (_session != null && _session.LastRecipeRemovalOutcomes.Count > 0)
            {
                foreach (RecipeRemovalOutcome outcome in _session.LastRecipeRemovalOutcomes)
                {
                    try
                    {
                        if (_world != null)
                        {
                            await _world.PlayRecipeRemovalOutcomeAsync(
                                outcome,
                                destroyCancellationToken);
                        }
                        else
                        {
                            ShowActiveItemMessage(
                                $"{outcome.Request.DishName}：{(outcome.Removed ? "移除" : "保留")}");
                            await Awaitable.WaitForSecondsAsync(0.75f, destroyCancellationToken);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    if (_discardSettlementCallbacks || Active != this)
                    {
                        return;
                    }
                }

            }

            FinalizeSettlementAndEndPassivePresentation();

            // 领奖期间允许隐藏奖励页查看本场结果，因此保留最终美味值；
            // 奖励全部领取并离开营业时，HideBattleWorld 会再将其清空。
            _infoColumn?.SetBattleScoreOverride(result.Total);
            RefreshAll();

            // 待领奖期间保留最终层数和世界表现；只有玩家明确点击“继续行动”才结束本场生命周期。
            BattleSession settledSession = _session;
            int finalHappyCakeLayers = settledSession?.HappyCakeLayers ?? 0;
            bool isWin = settledSession != null && settledSession.IsWin;
            void ContinueSettlement() => _loop?.OnBattleSettled(result, isWin, finalHappyCakeLayers);
            TutorialRuntime.PlayResultHeart(isWin, ContinueSettlement);
        }

        private void OnSettlementBeat(SettlementBeatSignal signal)
        {
            if (_discardSettlementCallbacks || Active != this)
            {
                return;
            }

            _infoColumn?.QueueSettlementScoreBeat(signal);
        }

        private void RegisterTutorialAnchors()
        {
            TutorialAnchorRegistry.Register(TutorialAnchorId.Score, _infoColumn?.ScoreRect);
            TutorialAnchorRegistry.Register(TutorialAnchorId.ScoreSection, _infoColumn?.ScoreSectionRect);
            TutorialAnchorRegistry.Register(TutorialAnchorId.ScoreTitle, _infoColumn?.ScoreTitleRect);
            TutorialAnchorRegistry.Register(TutorialAnchorId.ScoreMeter, _infoColumn?.ScoreMeterRect);
            TutorialAnchorRegistry.Register(TutorialAnchorId.Hearts, _infoColumn?.HeartsRect);
            TutorialAnchorRegistry.Register(TutorialAnchorId.Recipe, _infoColumn?.ViewRecipeButtonRect);
            TutorialAnchorRegistry.Register(
                TutorialAnchorId.RecipePanel,
                _inspectionLayer?.RecipeView != null ? _inspectionLayer.RecipeView.transform as RectTransform : null);
            TutorialAnchorRegistry.Register(TutorialAnchorId.FoodInfo, _infoColumn != null ? _infoColumn.transform as RectTransform : null);
            TutorialAnchorRegistry.Register(TutorialAnchorId.BossRule, _infoColumn?.BossRuleRect);
            TutorialAnchorRegistry.Register(TutorialAnchorId.ActionAxis, _timelineAxis != null ? _timelineAxis.transform as RectTransform : null);
            TutorialAnchorRegistry.Register(TutorialAnchorId.Table, _boardArea);
            TutorialAnchorRegistry.Register(TutorialAnchorId.ServingOutlet, _servingOutlet != null ? _servingOutlet.transform as RectTransform : null);
            TutorialAnchorRegistry.Register(TutorialAnchorId.Discard, _foodDiscardBin != null ? _foodDiscardBin.transform as RectTransform : null);
            TutorialAnchorRegistry.Register(TutorialAnchorId.Settle, _foodBar?.SettleRect);
        }

        private void UnregisterTutorialAnchors()
        {
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.Score);
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.ScoreSection);
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.ScoreTitle);
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.ScoreMeter);
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.Hearts);
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.Recipe);
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.RecipePanel);
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.FoodTips);
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.FoodInfo);
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.BossRule);
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.ActionAxis);
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.Table);
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.ServingOutlet);
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.Discard);
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.Settle);
        }

        private void RegisterTutorialCommands()
        {
            TutorialCommandRegistry.Register(TutorialCommand.OpenInitialRecipe, TutorialOpenInitialRecipe);
            TutorialCommandRegistry.Register(TutorialCommand.CloseInitialRecipe, TutorialCloseInitialRecipe);
            TutorialCommandRegistry.Register(TutorialCommand.ShowPreparedFoodTips, TutorialShowPreparedFoodTips);
            TutorialCommandRegistry.Register(TutorialCommand.HidePreparedFoodTips, TutorialHidePreparedFoodTips);
        }

        private void UnregisterTutorialCommands()
        {
            TutorialCommandRegistry.Unregister(TutorialCommand.OpenInitialRecipe, TutorialOpenInitialRecipe);
            TutorialCommandRegistry.Unregister(TutorialCommand.CloseInitialRecipe, TutorialCloseInitialRecipe);
            TutorialCommandRegistry.Unregister(TutorialCommand.ShowPreparedFoodTips, TutorialShowPreparedFoodTips);
            TutorialCommandRegistry.Unregister(TutorialCommand.HidePreparedFoodTips, TutorialHidePreparedFoodTips);
        }

        private void TutorialOpenInitialRecipe(Action done)
        {
            if (_inspectionCoordinator?.View == BattleInspectionView.Recipe)
            {
                done?.Invoke();
                return;
            }

            if (_inspectionCoordinator?.OpenRecipe(0, useBattleRecipe: true, onShown: done) != true)
                done?.Invoke();
        }

        private void TutorialCloseInitialRecipe(Action done)
        {
            if (_inspectionCoordinator?.IsActive == true)
                _inspectionCoordinator.Close(done);
            else
                done?.Invoke();
        }

        private void TutorialShowPreparedFoodTips(Action done)
        {
            bool shown = OnServingOutletDishHoverEntered(ResolveServingOutlet());
            if (shown && _tips?.Food != null)
            {
                _foodTipsHoverOwner = FoodTipsHoverOwner.Tutorial;
                Canvas.ForceUpdateCanvases();
                TutorialAnchorRegistry.Register(
                    TutorialAnchorId.FoodTips,
                    _tips.Food.SummaryView != null
                        ? _tips.Food.SummaryView.transform as RectTransform
                        : null);
            }
            done?.Invoke();
        }

        private void TutorialHidePreparedFoodTips(Action done)
        {
            HideFoodTips();
            done?.Invoke();
        }

        private bool IsFirstTutorialBattle() =>
            _run != null
            && _run.IsTutorialRun
            && _run.WeekIndex == 1
            && CurrentBattleActionContext?.RunStepIndex == 0
            && string.Equals(CurrentBattleActionContext?.Action?.Id, "act_food_gold", StringComparison.Ordinal);

        private void PlayActionSelectionTutorialIfNeeded()
        {
            if (_run == null)
            {
                return;
            }

            if (_run.IsTutorialRun
                && !TutorialProgressService.IsCompleted(TutorialId.CoreComplete))
            {
                if (_run.WeekIndex == 1
                    && _run.RunActionStepIndex == 0
                    && _run.CurrentDay <= TimelineMath.Epsilon)
                    TutorialRuntime.Play(TutorialId.FirstAction);
                else if (_run.WeekIndex == 1
                    && _run.RunActionStepIndex == 1
                    && TutorialProgressService.IsCompleted(TutorialId.FirstAction))
                    TutorialRuntime.Play(TutorialId.SecondAction);
            }

            // 页面与核心教程均已就绪后，恢复旧存档或中断留下的非核心教程。
            TutorialRuntime.DrainPending();
        }

        private void PlayBattleTutorialIfNeeded()
        {
            if (IsFirstTutorialBattle())
                TutorialRuntime.Play(TutorialId.FirstBattle, TryPlayFirstBattleSettleHint);
            if (_activeBattleIsBoss) TutorialRuntime.EnqueueHook(TutorialId.Boss);
            TutorialRuntime.DrainPending();
        }

        private void TryPlayFirstBattleSettleHint()
        {
            bool isFirstTutorialBattle = IsFirstTutorialBattle();
            BattleSession session = _session;
            if (!isFirstTutorialBattle || session == null)
            {
                return;
            }

            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if (!ShouldPlayFirstBattleSettleHint(
                    isFirstTutorialBattle: isFirstTutorialBattle,
                    hasSession: true,
                    isSettled: session.IsSettled,
                    firstBattleCompleted: TutorialProgressService.IsCompleted(TutorialId.FirstBattle),
                    settleHintCompleted: TutorialProgressService.IsCompleted(TutorialId.FirstBattleSettleHint),
                    tutorialPlaying: TutorialRuntime.IsPlaying,
                    foodInteractionBusy: world != null && world.IsFoodInteractionBusy,
                    hasTemporaryAreaDishes: session.TemporaryAreaDishes.Count > 0,
                    canServeAny: session.CanServeAny()))
            {
                return;
            }

            TutorialRuntime.Play(TutorialId.FirstBattleSettleHint);
        }

        internal static bool ShouldPlayFirstBattleSettleHint(
            bool isFirstTutorialBattle,
            bool hasSession,
            bool isSettled,
            bool firstBattleCompleted,
            bool settleHintCompleted,
            bool tutorialPlaying,
            bool foodInteractionBusy,
            bool hasTemporaryAreaDishes,
            bool canServeAny)
        {
            return isFirstTutorialBattle
                && hasSession
                && !isSettled
                && firstBattleCompleted
                && !settleHintCompleted
                && !tutorialPlaying
                && !foodInteractionBusy
                && !hasTemporaryAreaDishes
                && !canServeAny;
        }

        private bool TryPlayFirstBattleSettlementOrderHint()
        {
            BattleSession session = _session;
            if (!ShouldPlayFirstBattleSettlementOrderHint(
                    isFirstTutorialBattle: IsFirstTutorialBattle(),
                    hasSession: session != null,
                    isSettled: session?.IsSettled == true,
                    orderHintCompleted: TutorialProgressService.IsCompleted(
                        TutorialId.FirstBattleSettlementOrderHint),
                    tutorialPlaying: TutorialRuntime.IsPlaying))
            {
                return false;
            }

            return TutorialRuntime.Play(
                TutorialId.FirstBattleSettlementOrderHint,
                ContinueFoodSettlementAfterLayout);
        }

        internal static bool ShouldPlayFirstBattleSettlementOrderHint(
            bool isFirstTutorialBattle,
            bool hasSession,
            bool isSettled,
            bool orderHintCompleted,
            bool tutorialPlaying)
        {
            return isFirstTutorialBattle
                && hasSession
                && !isSettled
                && !orderHintCompleted
                && !tutorialPlaying;
        }

        private void OnGoldChanged(int before, int after)
        {
            if (_settlementPassivePresentationActive)
            {
                // ApplyFinal 会先标记 session，再把待入账金币搬进 Run.Gold；金币栏已经包含这部分目标。
                if (_session?.IsRunSettlementApplied == true)
                {
                    return;
                }

                int delta = after - before;
                int recordedPassiveGold = 0;
                IReadOnlyList<PassiveSettlementPresentationOccurrence> occurrences =
                    _session?.PassiveSettlementPresentationOccurrences;
                if (occurrences != null)
                {
                    for (int i = 0; i < occurrences.Count; i++)
                    {
                        recordedPassiveGold += occurrences[i].GoldDelta;
                    }
                }

                if (delta > 0
                    && recordedPassiveGold - _settlementSuppressedPassiveGold >= delta)
                {
                    _settlementSuppressedPassiveGold += delta;
                    return;
                }

                // 其它来源的直接金币仍按原节奏展示，但基于表现目标推进，不能带出尚未揭示的传菜铃金币。
                _settlementPresentedGold = Mathf.Max(0, _settlementPresentedGold + delta);
                if (delta > 0)
                {
                    GameApp.Audio.PlayRandomCoin();
                }

                _infoColumn?.PresentGoldTarget(_settlementPresentedGold);
                return;
            }

            if (after > before)
            {
                GameApp.Audio.PlayRandomCoin();
            }

            float pendingGold = _session != null ? _session.PendingGold : 0f;
            bool includePending = IsPendingGoldVisible();
            int displayedAfter = BattleInfoColumn.ResolveDisplayedGold(after, pendingGold, includePending);
            _infoColumn?.PresentGoldTarget(displayedAfter);
            RefreshPersistent(refreshItems: false);
        }

        private void OnPendingGoldChanged(float before, float after)
        {
            if (_run == null || !IsPendingGoldVisible())
            {
                return;
            }

            if (_settlementPassivePresentationActive)
            {
                int displayedDelta = (int)Math.Round(after, MidpointRounding.AwayFromZero)
                    - (int)Math.Round(before, MidpointRounding.AwayFromZero);
                _settlementPresentedGold = Mathf.Max(0, _settlementPresentedGold + displayedDelta);
                _infoColumn?.PresentGoldTarget(_settlementPresentedGold);
                return;
            }

            int displayedAfter = BattleInfoColumn.ResolveDisplayedGold(_run.Gold, after, includePending: true);
            _infoColumn?.PresentGoldTarget(displayedAfter);
            RefreshPersistent(refreshItems: false);
        }

        private bool IsPendingGoldVisible()
        {
            return ActiveInspectionView == BattleInspectionView.None
                && _current == GameplayView.Food
                && _session != null
                && !_session.IsSettled;
        }

        private void SetSession(BattleSession session)
        {
            if (ReferenceEquals(_session, session))
            {
                return;
            }

            if (_settlementPassivePresentationActive)
            {
                FinalizeSettlementAndEndPassivePresentation();
            }

            if (_session != null)
            {
                _session.PendingGoldChanged -= OnPendingGoldChanged;
            }

            _session = session;
            if (_session != null)
            {
                _session.PendingGoldChanged += OnPendingGoldChanged;
            }
        }

        private void OnContentAcquired(RunContentAcquisition acquisition)
        {
            string hook = TutorialRuntime.ContentHookFor(acquisition);
            if (string.IsNullOrEmpty(hook) || TutorialProgressService.IsCompleted(hook))
            {
                return;
            }

            // 获得事实先入存档，表现抵达后才允许播放；即使中途切页也不会丢教程。
            TutorialRuntime.PersistHook(hook);

            if (acquisition.Kind != RunContentAcquisitionKind.Item
                || string.IsNullOrEmpty(acquisition.ItemId))
            {
                TutorialRuntime.ObserveContentAcquired(acquisition);
                return;
            }

            _tutorialAcquiredItemPresentation.Stage(acquisition);
            SetPendingTutorialAcquiredItemAnchor(acquisition.ItemId, acquisition.ItemKind);
        }

        internal void CompleteTutorialAcquiredItemPresentation()
        {
            if (!_tutorialAcquiredItemPresentation.HasPending)
            {
                return;
            }

            RefreshItems();
            _tutorialAcquiredItemPresentation.Complete();
        }

        private void TryRegisterTutorialAcquiredItemAnchor()
        {
            if (_itemsColumn == null)
            {
                return;
            }

            TryRegisterTutorialAcquiredItemAnchor(
                _pendingTutorialAcquiredPassiveItemId,
                cfg.ItemKind.Passive,
                TutorialAnchorId.AcquiredPassiveItem,
                ref _tutorialAcquiredPassiveItemAnchor);
            TryRegisterTutorialAcquiredItemAnchor(
                _pendingTutorialAcquiredActiveItemId,
                cfg.ItemKind.Active,
                TutorialAnchorId.AcquiredActiveItem,
                ref _tutorialAcquiredActiveItemAnchor);
        }

        private void TryRegisterTutorialAcquiredItemAnchor(
            string itemId,
            cfg.ItemKind kind,
            string anchorId,
            ref RectTransform registeredAnchor)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return;
            }

            RunItemSlotView slot = _itemsColumn.GetItemSlot(itemId, kind);
            RectTransform target = slot?.VisualRectTransform;
            if (target == null)
            {
                return;
            }

            if (registeredAnchor != null && registeredAnchor != target)
            {
                TutorialAnchorRegistry.Unregister(anchorId, registeredAnchor);
            }

            TutorialAnchorRegistry.Register(anchorId, target);
            registeredAnchor = target;
        }

        private void SetPendingTutorialAcquiredItemAnchor(string itemId, cfg.ItemKind kind)
        {
            if (kind == cfg.ItemKind.Passive)
            {
                TutorialAnchorRegistry.Unregister(
                    TutorialAnchorId.AcquiredPassiveItem,
                    _tutorialAcquiredPassiveItemAnchor);
                _tutorialAcquiredPassiveItemAnchor = null;
                _pendingTutorialAcquiredPassiveItemId = itemId ?? string.Empty;
                return;
            }

            TutorialAnchorRegistry.Unregister(
                TutorialAnchorId.AcquiredActiveItem,
                _tutorialAcquiredActiveItemAnchor);
            _tutorialAcquiredActiveItemAnchor = null;
            _pendingTutorialAcquiredActiveItemId = itemId ?? string.Empty;
        }

        private void ClearTutorialAcquiredItemPresentationState()
        {
            TutorialAnchorRegistry.Unregister(
                TutorialAnchorId.AcquiredPassiveItem,
                _tutorialAcquiredPassiveItemAnchor);
            TutorialAnchorRegistry.Unregister(
                TutorialAnchorId.AcquiredActiveItem,
                _tutorialAcquiredActiveItemAnchor);
            _tutorialAcquiredPassiveItemAnchor = null;
            _tutorialAcquiredActiveItemAnchor = null;
            _tutorialAcquiredItemPresentation.Clear();
            _pendingTutorialAcquiredPassiveItemId = string.Empty;
            _pendingTutorialAcquiredActiveItemId = string.Empty;
        }

        private void StartBattleMusic()
        {
            StopBattleMusic(0.1f);
            _battleMusicSerialId = GameApp.Audio.PlayRandomBattleMusic();
        }

        private void StopBattleMusic(float fadeOutSeconds)
        {
            if (!_battleMusicSerialId.HasValue)
            {
                return;
            }

            GameApp.Audio.Stop(_battleMusicSerialId.Value, fadeOutSeconds);
            _battleMusicSerialId = null;
        }

        private void OnSettlementPassiveTriggered(string itemId)
        {
            if (_run == null || string.IsNullOrEmpty(itemId))
            {
                return;
            }

            new ItemRuntime(_run).FlashTriggered(model =>
                string.Equals(model.ItemId, itemId, StringComparison.Ordinal));
        }

        private void BeginSettlementPassivePresentation()
        {
            EndSettlementPassivePresentation();
            RunItemState state = _run?.GetItemState(GoldOnTransferItemId);
            if (state == null)
            {
                return;
            }

            _settlementPassivePresentationActive = true;
            int fallbackGold = BattleInfoColumn.ResolveDisplayedGold(
                _run.Gold,
                _session != null ? _session.PendingGold : 0f,
                IsPendingGoldVisible());
            _settlementPresentedGold = _infoColumn != null
                ? _infoColumn.ResolveGoldPresentationTarget(fallbackGold)
                : fallbackGold;

            RunItemSlotView slot = _itemsColumn?.GetItemSlot(
                GoldOnTransferItemId,
                cfg.ItemKind.Passive,
                revealPassive: false);
            if (slot == null)
            {
                return;
            }

            slot.BeginPassivePresentationOverride(state.Model?.InfoText ?? string.Empty);
            _settlementPassivePresentationSlots[GoldOnTransferItemId] = slot;
        }

        private void OnSettlementPassivePresentation(PassiveSettlementPresentationBatch batch)
        {
            if (_discardSettlementCallbacks
                || Active != this
                || !_settlementPassivePresentationActive
                || string.IsNullOrEmpty(batch.ItemId))
            {
                return;
            }

            if (!_settlementPassivePresentationSlots.TryGetValue(
                    batch.ItemId,
                    out RunItemSlotView slot)
                || slot == null)
            {
                slot = _itemsColumn?.GetItemSlot(
                    batch.ItemId,
                    cfg.ItemKind.Passive,
                    revealPassive: false);
                if (slot != null)
                {
                    slot.BeginPassivePresentationOverride(batch.InfoTextBefore);
                    _settlementPassivePresentationSlots[batch.ItemId] = slot;
                }
            }

            slot?.SetPassivePresentationInfoText(batch.InfoTextAfter);
            if (batch.ShouldPulse)
            {
                slot?.PlayPassivePulse();
            }

            if (batch.GoldDelta == 0)
            {
                return;
            }

            _settlementPresentedGold = Mathf.Max(0, _settlementPresentedGold + batch.GoldDelta);
            if (batch.GoldDelta > 0)
            {
                GameApp.Audio.PlayRandomCoin();
            }

            _infoColumn?.PresentGoldTarget(_settlementPresentedGold);
        }

        private void EndSettlementPassivePresentation()
        {
            foreach (RunItemSlotView slot in _settlementPassivePresentationSlots.Values)
            {
                if (slot != null)
                {
                    slot.EndPassivePresentationOverride();
                }
            }

            _settlementPassivePresentationSlots.Clear();
            _settlementPassivePresentationActive = false;
            _settlementPresentedGold = 0;
            _settlementSuppressedPassiveGold = 0;
        }

        private void FinalizeSettlementAndEndPassivePresentation()
        {
            if (_run != null && _session?.IsSettled == true)
            {
                BattleSettlementApplier.ApplyFinal(_run, _session);
            }

            EndSettlementPassivePresentation();
        }

        private void RefreshAll()
        {
            _world?.RefreshAll();
            if (_inBattle)
            {
                RefreshPersistent();
                BuildBattleControls();
            }

            TryPlayFirstBattleSettleHint();
        }

        // —— 局内交互（透传到经营挑战世界）——

        private void OnDoodleDrawClicked()
        {
            if (_rewardPeekOnly || HasPendingBattleRewardLifecycle)
            {
                return;
            }

            (_world ?? BattleWorldController.Instance)?.ToggleDoodleTool(BattleDoodleTool.Draw);
            RefreshFoodActions();
        }

        private void OnDoodleEraseClicked()
        {
            if (_rewardPeekOnly || HasPendingBattleRewardLifecycle)
            {
                return;
            }

            (_world ?? BattleWorldController.Instance)?.ToggleDoodleTool(BattleDoodleTool.Erase);
            RefreshFoodActions();
        }

        private void OnDoodleClearClicked()
        {
            if (_rewardPeekOnly || HasPendingBattleRewardLifecycle)
            {
                return;
            }

            (_world ?? BattleWorldController.Instance)?.ClearDoodle();
            RefreshFoodActions();
        }

        private void OnDoodleToggleClicked()
        {
            if (_rewardPeekOnly || HasPendingBattleRewardLifecycle)
            {
                return;
            }

            (_world ?? BattleWorldController.Instance)?.ToggleDoodleVisible();
            RefreshFoodActions();
        }

        private void OnDishClicked(DishInstance inst)
        {
            // if (inst == null || _run == null)
            // {
            //     return;
            // }

            // var data = new DishDetailData(inst.Def, _run.Database, inst.SkillIds, inst.FlavorIds, inst.SkillSources, inst.TransferredSkills);
            // GameApp.UI.OpenUIForm(UIForms.DishDetail, UIForms.GroupDialog, data);
        }

        private void OnActiveItemClicked(string itemId, RunItemSlotView slot)
        {
            _activeItemUse?.OpenActionPopup(itemId, slot);
        }

        internal void ShowActiveItemMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if (world != null)
            {
                world.ShowMessage(message);
                return;
            }

            SetMessage(message);
        }

        internal void RefreshAfterActiveItem(
            bool boardChanged,
            bool actionChoicesChanged = false,
            bool refreshActionContent = true)
        {
            if (boardChanged)
            {
                (_world ?? BattleWorldController.Instance)?.SyncTableFromSession();
            }

            RebuildActionAxis();
            if (_current == GameplayView.ActionSelect)
            {
                if (refreshActionContent && IsDailyActionSelectionActive)
                {
                    if (actionChoicesChanged)
                    {
                        RefreshActionCardsAnimated();
                    }
                    else
                    {
                        BuildActionCards();
                        PlayShowCardsWhenReady();
                    }
                }

                RefreshPersistent();
            }
            else if (_current == GameplayView.Shop)
            {
                RefreshShopPersistent();
            }
            else if (_current == GameplayView.RecipeSelection)
            {
                _recipeBookPage?.RefreshPanel();
                RefreshShopPersistent();
            }
            else if (!_inBattle)
            {
                RefreshPersistent();
            }

            RefreshAll();
        }

        internal void PlayActiveItemBossDebuffReroll(ActiveItemUseResult result)
        {
            if (string.IsNullOrEmpty(result.PresentationNodeId))
            {
                return;
            }

            if (_currentTimelineNodeCard != null
                && string.Equals(_currentTimelineNodeCard.Id, result.PresentationNodeId, StringComparison.Ordinal))
            {
                RebindCurrentTimelineNodeCard();
            }

            _axisBinder?.PlayBossDebuffRerollTip(
                result.PresentationNodeId,
                result.OldTipTitle,
                result.OldTipDesc,
                result.NewTipTitle,
                result.NewTipDesc);
        }

        private void RebindCurrentTimelineNodeCard()
        {
            if (_currentTimelineNodeCard == null || _deck == null)
            {
                return;
            }

            int? interestThreshold = _run != null ? _run.InterestThreshold : null;
            int? interestGoldPer = _run != null ? _run.InterestGoldPer : null;
            Action onPick = _currentTimelineNodePick;
            _deck.TryRebindTimelineNode(
                _currentTimelineNodeCard,
                interestThreshold,
                interestGoldPer,
                _currentTimelineNodeInterestMaxGain,
                () => OnTimelineNodePicked(onPick));
        }

        private void SetMessage(string message)
        {
            return;
        }

        private void SetSettlementScore(BigDouble score)
        {
            _infoColumn?.SetSettlementScorePresentation(score);
        }

        // —— 通知弹窗 ——

        public void ShowNotice(string title, string message, Action onContinue)
        {
            if (string.IsNullOrEmpty(message))
            {
                onContinue?.Invoke();
                return;
            }

            var data = new ConfirmDialogData
            {
                Title = title,
                Message = message,
                ConfirmText = "继续",
                CancelText = string.Empty,
                OnConfirm = onContinue,
            };
            GameApp.UI.OpenUIForm(UIForms.ConfirmDialog, UIForms.GroupDialog, data);
        }
    }
}
