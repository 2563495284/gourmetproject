using System;
using System.Collections.Generic;
using System.Threading;
using DG.Tweening;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Flow;
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
using UnityEngine.Serialization;
using TMPro;

namespace GourmetProject.Game.UI.Battle
{
    /// <summary>
    /// 局外周循环编排枢纽 + 局内经营挑战结果壳。
    /// 常驻壳（左列信息 / 右列装饰品和消耗品 / 顶部时间轴）进入玩法后全程常驻，只有中部内容区在五态间切换：
    /// 行动选择(含事件 n 选一) / 商店 / 食谱查看与选择 / 经营挑战 / 餐桌编辑。切换只对中部内容区做 DOTween 渐隐渐显
    /// （<see cref="UITransition.FadeSwap"/>），常驻壳不参与动画；食物 / 餐桌态在同一 Battle 场景内透出世界空间表现。
    /// </summary>
    public sealed class BattleForm : UGuiForm,
        IWeekLoopView,
        ITableViewHost,
        IGameplayPageRouterHost,
        IRecipeBookHost,
        IShopPageHost,
        IRewardPageHost,
        IEventPageHost
    {
        private const string Tag = "Battle";
        private const float RandomizedItemFlyDuration = 0.42f;
        private static readonly Color BoardEditConfirmColor = new Color(0.08f, 0.62f, 0.12f, 1f);

        private enum FoodTipsHoverOwner
        {
            None,
            TableDish,
            TableCell,
            TableFragment,
            ServingOutlet,
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

        [Header("DiningTable Area (餐桌锁定区)")]
        [Tooltip("HUD 里的空区矩形：世界餐桌将 fit 并居中锁定在该屏幕区域内。")]
        [SerializeField] private RectTransform _boardArea;

        [Header("DiningTable View")]
        [SerializeField] private GameObject _viewTablePanel;

        [Header("Left Column")]
        [SerializeField] private BattleInfoColumn _infoColumn;

        [Header("Action Axis")]
        [SerializeField] private ActionAxisBar _actionAxisBar;

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

        [Header("DiningTable Edit")]
        [SerializeField] private GameObject _boardEditPanel;
        [FormerlySerializedAs("_boardEditSkipButton")]
        [SerializeField] private Button _boardEditActionButton;

        [Header("Reward Table Edit Shell")]
        [Tooltip("领奖餐桌格编辑期间软隐藏时间轴；组件固定在 BattleForm prefab 上，不在运行时生成。")]
        [SerializeField] private CanvasGroup _rewardTableEditActionAxisGroup;

        [Header("Right Column - Items")]
        [SerializeField] private BattleItemsColumn _itemsColumn;

        [Header("Active Items")]
        [SerializeField] private ActiveItemActionPopup _activeItemPopupPrefab;
        [SerializeField] private TargetArrowView _activeItemTargetArrowPrefab;
        [SerializeField] private ActiveItemTargetOverlayView _activeItemTargetOverlayPrefab;
        [SerializeField] private GameObject _shopItemFlyFxPrefab;

        [Header("Food Battle (center)")]
        [Tooltip("美食战斗专属 UI 根节点：MessageText / FoodActions / BuffList。")]
        [SerializeField] private GameObject _foodBattlePanel;
        [SerializeField] private BattleFoodActionBar _foodBar;
        [SerializeField] private ServingOutletView _servingOutlet;
        [SerializeField] private FoodDiscardBinView _foodDiscardBin;

        private bool _inBattle;
        private GameplayView _current = GameplayView.None;
        private Action<bool> _afterRewardTableEdit;
        private bool _rewardTableEditActive;
        private GameplayView _rewardTableEditRootView = GameplayView.None;
        private CanvasGroupSnapshot _rewardTableEditCenterSnapshot;
        private CanvasGroupSnapshot _rewardTableEditAxisSnapshot;
        private bool _rewardTableEditFoodBattlePanelActive;
        private bool _rewardTableEditFoodBarActive;
        private ServingOutletView _rewardTableEditServingOutlet;
        private bool _rewardTableEditServingOutletActive;
        private FoodDiscardBinView _rewardTableEditFoodDiscardBin;
        private bool _rewardTableEditFoodDiscardBinActive;
        private bool _rewardTableEditBackdropActive;
        private Transform _boardEditOriginalParent;
        private int _boardEditOriginalSiblingIndex;
        private Vector2 _boardEditOriginalAnchorMin;
        private Vector2 _boardEditOriginalAnchorMax;
        private Vector2 _boardEditOriginalPosition;
        private Vector2 _boardEditOriginalSize;
        private Vector2 _boardEditOriginalPivot;
        private bool _boardEditActionCanConfirm;
        private bool _boardEditActionInteractable;
        private Color _boardEditSkipColor = new Color(0.72f, 0.02f, 0.02f, 1f);

        private GameRun _run;
        private BattleSession _session;
        private BattleSession _foodDiscardCapacitySession;
        private int _observedFoodDiscardCapacity = -1;
        private BattleWorldController _world;

        private WeekLoopController _loop;

        private TimelineAxisBinder _axisBinder;
        private InspectionNavigationContext _inspectionNavigation;
        private TableViewCoordinator _tableCoordinator;
        private GameplayPageRouter _pageRouter;

        private struct CanvasGroupSnapshot
        {
            private CanvasGroup _group;
            private float _alpha;
            private bool _interactable;
            private bool _blocksRaycasts;

            public void Capture(CanvasGroup group)
            {
                _group = group;
                if (_group == null)
                {
                    return;
                }

                _alpha = _group.alpha;
                _interactable = _group.interactable;
                _blocksRaycasts = _group.blocksRaycasts;
            }

            public void Hide()
            {
                if (_group == null)
                {
                    return;
                }

                _group.alpha = 0f;
                _group.interactable = false;
                _group.blocksRaycasts = false;
            }

            public void Restore()
            {
                if (_group == null)
                {
                    return;
                }

                _group.alpha = _alpha;
                _group.interactable = _interactable;
                _group.blocksRaycasts = _blocksRaycasts;
                _group = null;
            }
        }
        private ShopPageCoordinator _shopPage;
        private RecipeBookCoordinator _recipeBookPage;
        private RewardPageCoordinator _rewardPage;
        private EventPageCoordinator _eventPage;
        private ActiveItemUseCoordinator _activeItemUse;
        private int _shopItemFlyInFlight;
        private readonly HashSet<ShopPurchaseFlyView> _activeShopPurchaseFlys = new();
        private int? _battleMusicSerialId;
        private cfg.BossDebuff _currentBossDebuff;
        private BossDebuffPresentationView _bossPresentation;
        private BossDialogueShuffleBag _bossDialogueBag;
        private CancellationTokenSource _bossPresentationCts;
        private bool _bossDiscardRevealPending;
        private cfg.TimelineNode _currentTimelineNodeCard;
        private int? _currentTimelineNodeInterestMaxGain;
        private Action _currentTimelineNodePick;
        private int _activeBattleRawRequiredScore;
        private string _activeBattleModifier = string.Empty;
        private string _activeBossDebuffId = string.Empty;
        private string _activeBattleKey = string.Empty;
        private bool _activeBattleIsBoss;
        private bool _rewardPeekOnly;
        private bool _discardSettlementCallbacks;
        private DishPieceView _hoveredDishPiece;
        private DiningTableCellView _hoveredCell;
        private int _hoveredTableFragmentSession = -1;
        private int _hoveredTableFragmentIndex = -1;
        private FoodTipsHoverOwner _foodTipsHoverOwner;
        private SettlementRevealState _settlementReveal;
        private int _displayedCakeLayers;
        private int? _pendingSettlementCakeLayers;
        private int _pendingSettlementCakeLayerBonus;
        [SerializeField] private GameObject _passiveOverlayRoot;
        [SerializeField] private TMP_Text _passiveOverlayText;
        private Sequence _passiveOverlaySeq;

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
        internal bool IsDailyActionSelectionActive =>
            _current == GameplayView.ActionSelect && _currentTimelineNodeCard == null;
        internal bool IsViewingBattleTable => _tableCoordinator != null && _tableCoordinator.IsViewingBattleTable;
        internal bool IsRewardTableEditActive => _rewardTableEditActive;
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
            EnsureRewardSubflowLayer();

            _infoColumn?.Bind(OnSettingsClicked, OnViewTableClicked, OnViewRecipeClicked);
            _viewTablePanel?.GetComponent<ViewTablePanel>()?.Bind(OnExitTableViewClicked);
            _cakeLayerBuffHud = GetComponent<CakeLayerBuffHud>();
            _foodBar?.Bind(OnEatClicked, OnDoodleClearClicked, OnDoodleToggleClicked);
            _bossPresentation = GetComponent<BossDebuffPresentationView>();
            if (_bossPresentation == null)
            {
                _bossPresentation = gameObject.AddComponent<BossDebuffPresentationView>();
            }
            _bossPresentation.EnsureBuilt();

            if (_boardEditActionButton != null)
            {
                _boardEditActionButton.onClick.AddListener(OnTableEditActionClicked);
                if (_boardEditActionButton.targetGraphic != null)
                {
                    _boardEditSkipColor = _boardEditActionButton.targetGraphic.color;
                }

                ApplyTableEditActionState(new TableFragmentEditActionState(
                    canConfirm: false,
                    interactable: false));
            }

            _axisBinder = new TimelineAxisBinder(
                _actionAxisBar,
                () => _tips != null ? _tips.Timeline : null);
            _deck?.SetRewardTip(() => _tips != null ? _tips.Item : null);
            _inspectionNavigation = new InspectionNavigationContext();
            _tableCoordinator = new TableViewCoordinator(this, _inspectionNavigation);
            _pageRouter = new GameplayPageRouter(this);
            _shopPage = new ShopPageCoordinator(this);
            _recipeBookPage = new RecipeBookCoordinator(this, _inspectionNavigation);
            _rewardPage = new RewardPageCoordinator(this);
            _eventPage = new EventPageCoordinator(this);
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
            _discardSettlementCallbacks = false;
            _run.GoldChanged -= OnGoldChanged;
            _run.GoldChanged += OnGoldChanged;
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
            if (Active == this)
            {
                Active = null;
            }

            _discardSettlementCallbacks = true;
            if (_run != null)
            {
                _run.GoldChanged -= OnGoldChanged;
            }
            StopBattleMusic(0.15f);
            CancelBossPresentation();
            ResetBossBattlePresentation();
            CancelActiveShopPurchaseAnimations();
            _rewardPage?.CloseRewardPages();
            CancelRewardTableEditSubflow();
            _settlementReveal = null;
            _loop = null;
            _activeItemUse?.Dispose();
            _rewardPeekOnly = false;
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

            if (_rewardPeekOnly || HasPendingBattleRewardLifecycle)
            {
                return;
            }

            _activeItemUse?.Update();
        }

        // —— 周循环编排（代理到 WeekLoopController）——

        public int LastBattleTotal => _session?.LastResult?.Total ?? 0;

        private bool HasPendingBattleRewardLifecycle
            => _session?.IsSettled == true && _run?.HasPendingRewardBattleView == true;

        internal bool IsActiveItemUseBlocked
            => _rewardPeekOnly || HasPendingBattleRewardLifecycle;

        /// <summary>进入（或继续）一周：随机/沿用时间轴后开始行动循环。</summary>
        public void BeginWeek()
        {
            UnsubscribeCakeLayerChanges();
            _session = null;
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
            _loop?.OnRewardConfirmed();
        }

        /// <summary>抽奖机奖励领取完成后，回到同一台抽奖机或结束已达上限的节点。</summary>
        public void OnSlotRewardConfirmed()
        {
            _loop?.OnSlotRewardConfirmed();
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
                LastTotal = _session.LastResult.Total,
                FinalHappyCakeLayers = _session.HappyCakeLayers,
                HasDetailedScore = true,
                RawSum = _session.LastResult.RawSum,
                FinalFlat = _session.LastResult.FinalFlat,
                FinalMultiplier = _session.LastResult.FinalMultiplier,
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
                    PermanentFlatBonus = dish.PermanentFlatBonus,
                    PermanentMultBonus = dish.PermanentMultBonus,
                    TemporaryBaseMultiplier = dish.TemporaryBaseMultiplier,
                    ServeMultiplier = dish.ServeMultiplier,
                    ServeMultiplierFlatBonus = dish.ServeMultiplierFlatBonus,
                    SkillsDisabled = dish.SkillsDisabled,
                    ExcludedFromScore = dish.ExcludedFromScore,
                    IsTemporary = dish.IsTemporary,
                    HasDishScore = dishScore != null,
                    ScoreBaseValue = dishScore?.BaseValue ?? 0f,
                    ScoreFlatBonus = dishScore?.FlatBonus ?? 0f,
                    ScoreMultiplier = dishScore?.Multiplier ?? 1f,
                    ScoreEffectiveCountAs = dishScore?.EffectiveCountAs ?? Math.Max(1, dish.EffectiveCountAs),
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
            _infoColumn?.SetBattleScoreOverride(snapshot.LastTotal);

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
            _session = _run.BuildBattleSession(
                _activeBattleRawRequiredScore,
                _activeBattleModifier,
                _activeBattleKey,
                _activeBossDebuffId);
            BeginFoodDiscardCapacityTracking();
            _pendingSettlementCakeLayers = null;
            _session.DiningTable.Clear();
            RestorePendingRewardBattleDishes(_session, snapshot);
            List<DishScore> dishScores = RestorePendingRewardDishScores(snapshot);
            _session.RestoreSettledForRewardView(
                snapshot.LastTotal,
                snapshot.FinalHappyCakeLayers,
                dishScores,
                snapshot.RawSum,
                snapshot.FinalFlat,
                snapshot.FinalMultiplier,
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

            _world.SetTableArea(_boardArea);
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
            SetSettlementScore(snapshot.LastTotal);
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
                    dish.ScoreBaseValue,
                    dish.ScoreFlatBonus,
                    dish.ScoreMultiplier,
                    dish.ScoreEffectiveCountAs));
            }

            return result;
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

            if (Math.Abs(saved.PermanentFlatBonus) > 0.0001f)
            {
                dish.AddPermanentFlat(saved.PermanentFlatBonus);
            }

            if (saved.PermanentMultBonus > 0f && Math.Abs(saved.PermanentMultBonus - 1f) > 0.0001f)
            {
                dish.MultiplyPermanentMult(saved.PermanentMultBonus);
            }

            if (saved.TemporaryBaseMultiplier > 0f && Math.Abs(saved.TemporaryBaseMultiplier - 1f) > 0.0001f)
            {
                dish.MultiplyTemporaryBase(saved.TemporaryBaseMultiplier);
            }

            if (saved.ServeMultiplier > 0f && Math.Abs(saved.ServeMultiplier - 1f) > 0.0001f)
            {
                dish.MultiplyServeMultiplier(saved.ServeMultiplier);
            }

            if (Math.Abs(saved.ServeMultiplierFlatBonus) > 0.0001f)
            {
                dish.AddServeMultiplierFlat(saved.ServeMultiplierFlatBonus);
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

        /// <summary>时间轴节点卡片：先展示节点卡，玩家点击后再执行节点效果。</summary>
        public void ShowTimelineNodeCard(cfg.TimelineNode node, int? interestMaxGain, Action onPick)
        {
            TrackTimelineNodeCard(node, interestMaxGain, onPick);
            SwitchTo(GameplayView.ActionSelect, () =>
            {
                BuildTimelineNodeCard(node, interestMaxGain, onPick);
            }, PlayShowCardsWhenReady);
        }

        public void ShowTimelineNodeSkipped(cfg.TimelineNode node, Action onDone)
        {
            string actionName = ActionName(node?.ActionId);
            ShowPassiveOverlay("停业整顿", $"跳过节点：{actionName}", 0.9f, onDone);
        }

        public void ShowPassiveRecipeMutation(RecipeMutationResult result)
        {
            if (result == null || !result.HasChanges)
            {
                return;
            }

            ShowPassiveOverlay(result.Title, BuildRecipeMutationText(result), 1.4f, () => RefreshPersistent());
        }

        public void ShowPassiveCellMutation(CellMutationResult result)
        {
            if (result == null || !result.HasChanges)
            {
                return;
            }

            if (_rewardTableEditActive)
            {
                ShowPassiveOverlay(result.Title, BuildCellMutationText(result), 1.2f, () => RefreshPersistent());
                return;
            }

            _world = _world ?? BattleWorldController.Instance;
            _world?.SetTableArea(_boardArea);
            bool opened = false;
            if (_tableCoordinator != null && _current != GameplayView.TableView)
            {
                _tableCoordinator.Open();
                opened = true;
            }

            ShowPassiveOverlay(result.Title, BuildCellMutationText(result), 1.2f, () =>
            {
                if (opened && _tableCoordinator != null && _current == GameplayView.TableView)
                {
                    _tableCoordinator.Back();
                }

                RefreshPersistent();
            });
        }

        public void ShowPassiveTimelineMutation(TimelineMutationResult result)
        {
            if (result == null || !result.Changed)
            {
                return;
            }

            bool restoreVisible = _current == GameplayView.ActionSelect || _current == GameplayView.Shop;
            SetActionAxisVisible(true);
            RebuildActionAxis();
            ShowPassiveOverlay(result.Title, BuildTimelineMutationText(result), 1.2f, () =>
            {
                SetActionAxisVisible(restoreVisible);
                RebuildActionAxis();
                RefreshPersistent();
            });
        }

        // —— 中部态切换中枢（淡入淡出调度 + 派发给状态机）——

        /// <summary>
        /// 切到某一中部态：只对中部内容区 <see cref="_center"/> 做 DOTween 渐隐渐显，常驻壳（左/右/时间轴）不动。
        /// 内容交换（隐藏旧面板 + 启用新面板 + 重建）集中在淡出完成后的 <see cref="GameplayViewStateMachine.Apply"/> 里执行。
        /// </summary>
        private void SwitchTo(GameplayView next, Action buildCenter = null, Action onShown = null)
        {
            // 奖励餐桌格编辑是覆盖在当前页上的临时世界子流程；期间切换主页面会让
            // Center 的软隐藏快照与页面路由同时持有显示权，最终留下透明查看页。
            if (_rewardTableEditActive && next != _current)
            {
                return;
            }

            _pageRouter?.SwitchTo(next, buildCenter, () =>
            {
                SyncPageStateFromRouter();
                if (_rewardPeekOnly && !InspectionNavigationContext.IsInspectionView(_current))
                {
                    RewardForm.Active?.SetResultPeekInspectionActive(false);
                }
                onShown?.Invoke();
            });
            SyncPageStateFromRouter();
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
            Transform axisRoot = DirectChildUnder(coverParent, _actionAxisBar != null
                ? _actionAxisBar.transform
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

        private void OnPageCovered(GameplayView current, GameplayView next)
        {
            if (InspectionNavigationContext.IsInspectionView(current)
                && !InspectionNavigationContext.IsInspectionView(next))
            {
                _inspectionNavigation?.Clear();
            }

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
            return next == GameplayView.Food
                || next == GameplayView.TableEdit
                || next == GameplayView.TableView
                || next == GameplayView.RecipeInspect;
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
        GameObject IGameplayPageRouterHost.ViewTablePanel => _viewTablePanel;
        GameObject IGameplayPageRouterHost.ActionSelectionPanel => _actionSelectionPanel;
        ActionCardDeck IGameplayPageRouterHost.Deck => _deck;
        ShopForm IGameplayPageRouterHost.ShopPanel => _shopPanel;
        RecipeReadonlyBookView IGameplayPageRouterHost.RecipeReadonlyBookView => _recipeReadonlyBookView;
        EventPagePanel IGameplayPageRouterHost.EventPagePanel => _eventPagePanel;
        GameObject IGameplayPageRouterHost.BoardEditPanel => _boardEditPanel;
        Button IGameplayPageRouterHost.BoardEditActionButton => _boardEditActionButton;
        bool IGameplayPageRouterHost.RecipeInspectShowsActionAxis => _recipeBookPage?.InspectShowsActionAxis == true;
        bool IGameplayPageRouterHost.ActionAxisVisible => _actionAxisBar != null && _actionAxisBar.gameObject.activeSelf;
        void IGameplayPageRouterHost.OnLeavingPage(GameplayView current, GameplayView next)
        {
            _recipeBookPage?.OnLeavingPage(current, next);
            if (_rewardPeekOnly && InspectionNavigationContext.IsInspectionView(next))
            {
                RewardForm.Active?.SetResultPeekInspectionActive(true);
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
        BattleSession IRecipeBookHost.Session => _session;
        GameplayView IRecipeBookHost.CurrentView => _current;
        RecipeReadonlyBookView IRecipeBookHost.RecipeReadonlyBookView => _recipeReadonlyBookView;
        void IRecipeBookHost.SwitchTo(GameplayView view, Action buildCenter, Action onShown) => SwitchTo(view, buildCenter, onShown);
        void IRecipeBookHost.RefreshPersistent() => RefreshPersistent();
        void IRecipeBookHost.RefreshShopPersistent() => RefreshShopPersistent();
        ActionSelectSnapshot IRecipeBookHost.CaptureActionSelection() => CaptureActionSelectSnapshot();
        void IRecipeBookHost.RestoreActionSelection(ActionSelectSnapshot snapshot) => RestoreActionSelection(snapshot);
        void IRecipeBookHost.ShowActionSelection(Action onShown) => ShowActionSelection(onShown);
        void IRecipeBookHost.RestoreBattleWorld() => RestoreBattleWorld();
        void IRecipeBookHost.PlayShowCardsWhenReady() => PlayShowCardsWhenReady();
        FoodTipsView IRecipeBookHost.FoodTips() => _tips != null ? _tips.Food : null;

        GameRun IShopPageHost.Run => _run;
        ShopForm IShopPageHost.ShopPanel => _shopPanel;
        bool IShopPageHost.ShouldRefreshItemsAfterShopChange => _shopItemFlyInFlight <= 0;
        void IShopPageHost.OnShopClosed() => OnShopClosed();
        void IShopPageHost.RefreshPersistent(bool refreshItems) => RefreshPersistent(refreshItems);
        void IShopPageHost.OpenDeleteDish() => _recipeBookPage?.OpenShopDelete();
        void IShopPageHost.OpenTableEdit(Action onShown) => OpenTableEdit(onShown);
        void IShopPageHost.OpenRecipeInspect(int bookIndex) => OpenRecipeInspect(bookIndex);
        void IShopPageHost.PlayShopPurchaseAnimation(ShopEntry entry, ShopBuyItemViewBase sourceCard) => PlayShopPurchaseAnimation(entry, sourceCard);

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

        // —— ITableViewHost（供查看餐桌编排回调壳）——

        GameplayView ITableViewHost.CurrentView => _current;
        GameRun ITableViewHost.Run => _run;
        BattleSession ITableViewHost.Session => _session;
        BattleWorldController ITableViewHost.World => _world ?? BattleWorldController.Instance;
        void ITableViewHost.SwitchTo(GameplayView view, Action buildCenter, Action onShown) => SwitchTo(view, buildCenter, onShown);
        void ITableViewHost.RestoreBattleWorld() => RestoreBattleWorld();
        void ITableViewHost.BindWorldHoverCallbacks() => BindWorldHoverCallbacks();
        void ITableViewHost.PlayShowCardsWhenReady() => PlayShowCardsWhenReady();
        void ITableViewHost.OpenTableEdit(Action onShown) => OpenTableEdit(onShown);
        ActionSelectSnapshot ITableViewHost.CaptureActionSelectSnapshot() => CaptureActionSelectSnapshot();
        void ITableViewHost.RestoreActionSelection(ActionSelectSnapshot snapshot) => RestoreActionSelection(snapshot);

        // —— 行动选择态（含事件 n 选一，共用中部卡片）——

        private void ShowActionSelection(Action onShown = null)
        {
            ClearTimelineNodeCard();
            SwitchTo(GameplayView.ActionSelect, () =>
            {
                BuildActionCards();
            }, () =>
            {
                PlayShowCardsWhenReady();
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

        private void OpenRecipeInspect(int bookIndex, bool useBattleRecipe = false)
        {
            if (_rewardTableEditActive)
            {
                return;
            }

            _recipeBookPage?.OpenInspect(bookIndex, useBattleRecipe);
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

        internal bool PlayActiveItemRecipeFlavorApplied(ActiveTarget target, Action onComplete)
        {
            return _recipeBookPage != null
                && _recipeBookPage.PlayActiveItemRecipeFlavorApplied(target, onComplete);
        }

        internal void OpenActiveItemTableCellTarget(Action onOpened)
        {
            if (_tableCoordinator == null || _rewardTableEditActive)
            {
                onOpened?.Invoke();
                return;
            }

            _tableCoordinator.OpenForCellTargeting(onOpened);
        }

        internal void CloseActiveItemTableCellTarget()
        {
            if (_tableCoordinator != null && _current == GameplayView.TableView)
            {
                _tableCoordinator.Back();
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

            OpenTableFragmentChoice(_run.PendingFragmentPack, OnTableEditDone, onShown);
        }

        public bool OpenRewardTableEdit(Action<bool> onDone)
        {
            return OpenRewardTableEdit(_run != null ? _run.PendingFragmentPack : null, onDone);
        }

        public bool OpenRewardTableEdit(IReadOnlyList<string> candidateIds, Action<bool> onDone)
        {
            if (_run == null || candidateIds == null || candidateIds.Count == 0)
            {
                onDone?.Invoke(false);
                return false;
            }

            _afterRewardTableEdit = onDone;
            if (OpenTableFragmentChoice(candidateIds, OnRewardTableEditDone))
            {
                return true;
            }

            _afterRewardTableEdit = null;
            return false;
        }

        private bool OpenTableFragmentChoice(
            IReadOnlyList<string> candidateIds,
            Action<bool> completed,
            Action onShown = null)
        {
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if (_rewardTableEditActive
                || world == null
                || _run == null
                || candidateIds == null
                || candidateIds.Count == 0)
            {
                completed?.Invoke(false);
                onShown?.Invoke();
                return false;
            }

            var request = new TableFragmentChoiceRequest(
                _run,
                candidateIds,
                completed,
                ApplyTableEditActionState,
                _run.PendingFragmentPackRotations);
            BeginRewardTableEditSubflow(world, request);
            onShown?.Invoke();
            return true;
        }

        private void BeginRewardTableEditSubflow(
            BattleWorldController world,
            TableFragmentChoiceRequest request)
        {
            _rewardTableEditActive = true;
            _rewardTableEditRootView = _current;
            _infoColumn?.SetInspectionNavigationBlocked(true);
            PreparingChild?.Invoke();

            CaptureRewardTableEditShell();

            // 来源页仍保持完整可见时先把世界和编辑控件构建完；最后才在同一帧软隐藏来源。
            _world = world;
            world.SetTableArea(_boardArea);
            world.BeginTableFragmentChoice(request);
            MoveBoardEditPanelToTransientLayer();

            if (_backdrop != null)
            {
                _backdrop.SetActive(false);
            }

            HideRewardTableEditShell();

            ChildReady?.Invoke();
        }

        private void CaptureRewardTableEditShell()
        {
            if (_rewardTableEditActionAxisGroup == null && _actionAxisBar != null)
            {
                _rewardTableEditActionAxisGroup = _actionAxisBar.GetComponent<CanvasGroup>();
            }

            _rewardTableEditCenterSnapshot.Capture(_center);
            _rewardTableEditAxisSnapshot.Capture(_rewardTableEditActionAxisGroup);

            _rewardTableEditFoodBattlePanelActive = _foodBattlePanel != null
                && _foodBattlePanel.activeSelf;
            _rewardTableEditFoodBarActive = _foodBar != null
                && _foodBar.gameObject.activeSelf;
            _rewardTableEditServingOutlet = ResolveServingOutlet();
            _rewardTableEditServingOutletActive = _rewardTableEditServingOutlet != null
                && _rewardTableEditServingOutlet.gameObject.activeSelf;
            _rewardTableEditFoodDiscardBin = ResolveFoodDiscardBin();
            _rewardTableEditFoodDiscardBinActive = _rewardTableEditFoodDiscardBin != null
                && _rewardTableEditFoodDiscardBin.gameObject.activeSelf;
            _rewardTableEditBackdropActive = _backdrop != null && _backdrop.activeSelf;

            RectTransform boardRect = _boardEditPanel != null
                ? _boardEditPanel.transform as RectTransform
                : null;
            if (boardRect == null)
            {
                return;
            }

            _boardEditOriginalParent = boardRect.parent;
            _boardEditOriginalSiblingIndex = boardRect.GetSiblingIndex();
            _boardEditOriginalAnchorMin = boardRect.anchorMin;
            _boardEditOriginalAnchorMax = boardRect.anchorMax;
            _boardEditOriginalPosition = boardRect.anchoredPosition;
            _boardEditOriginalSize = boardRect.sizeDelta;
            _boardEditOriginalPivot = boardRect.pivot;
        }

        private void HideRewardTableEditShell()
        {
            _rewardTableEditCenterSnapshot.Hide();
            _rewardTableEditAxisSnapshot.Hide();

            // 出菜口和垃圾桶在世界 Canvas 上，不属于 Center，必须单独关闭。
            SetFoodBattlePanelVisible(false);
            HideAllTips();
        }

        private void MoveBoardEditPanelToTransientLayer()
        {
            RectTransform boardRect = _boardEditPanel != null
                ? _boardEditPanel.transform as RectTransform
                : null;
            Transform parent = _hudFrame != null ? _hudFrame.transform : transform;
            if (boardRect == null)
            {
                return;
            }

            boardRect.SetParent(parent, false);
            RectTransform centerRect = _center != null ? _center.transform as RectTransform : null;
            if (centerRect != null)
            {
                boardRect.anchorMin = centerRect.anchorMin;
                boardRect.anchorMax = centerRect.anchorMax;
                boardRect.anchoredPosition = centerRect.anchoredPosition;
                boardRect.sizeDelta = centerRect.sizeDelta;
                boardRect.pivot = centerRect.pivot;
            }

            boardRect.SetAsLastSibling();
            Transform left = DirectChildUnder(parent, _infoColumn != null ? _infoColumn.transform : null);
            Transform right = DirectChildUnder(parent, _itemsColumn != null ? _itemsColumn.transform : null);
            int beforeColumns = parent.childCount - 1;
            if (left != null)
            {
                beforeColumns = Mathf.Min(beforeColumns, left.GetSiblingIndex());
            }
            if (right != null)
            {
                beforeColumns = Mathf.Min(beforeColumns, right.GetSiblingIndex());
            }
            boardRect.SetSiblingIndex(beforeColumns);
            _boardEditPanel.SetActive(true);
        }

        private void RestoreRewardTableEditSubflow(bool invokeLifecycle)
        {
            if (invokeLifecycle)
            {
                PreparingReturn?.Invoke();
            }

            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if (_rewardTableEditRootView == GameplayView.Food)
            {
                RestoreBattleWorld();
            }
            else
            {
                world?.HideWorld();
            }

            RestoreRewardTableEditShell();
        }

        private void RestoreRewardTableEditShell()
        {
            _rewardTableEditCenterSnapshot.Restore();
            _rewardTableEditAxisSnapshot.Restore();

            if (_foodBattlePanel != null)
            {
                _foodBattlePanel.SetActive(_rewardTableEditFoodBattlePanelActive);
            }

            _foodBar?.SetVisible(_rewardTableEditFoodBarActive);
            _rewardTableEditServingOutlet?.SetVisible(_rewardTableEditServingOutletActive);
            _rewardTableEditFoodDiscardBin?.SetVisible(_rewardTableEditFoodDiscardBinActive);
            _rewardTableEditServingOutlet = null;
            _rewardTableEditFoodDiscardBin = null;

            if (_backdrop != null)
            {
                _backdrop.SetActive(_rewardTableEditBackdropActive);
            }

            _infoColumn?.SetInspectionNavigationBlocked(false);
        }

        private void FinishRewardTableEditSubflow(bool placed, Action<bool> completed)
        {
            if (!_rewardTableEditActive)
            {
                return;
            }

            _rewardTableEditActive = false;
            RestoreRewardTableEditSubflow(invokeLifecycle: true);
            RefreshPersistent();

            // RewardForm 在编辑页仍位于最上层但处于透明挂起态；先同步恢复它，再撤编辑控件。
            completed?.Invoke(placed);
            RestoreBoardEditPanelParent();
            _rewardTableEditRootView = GameplayView.None;
            ParentRestored?.Invoke();
        }

        private void RestoreBoardEditPanelParent()
        {
            RectTransform boardRect = _boardEditPanel != null
                ? _boardEditPanel.transform as RectTransform
                : null;
            if (boardRect == null)
            {
                return;
            }

            _boardEditPanel.SetActive(false);
            if (_boardEditOriginalParent != null)
            {
                boardRect.SetParent(_boardEditOriginalParent, false);
                boardRect.SetSiblingIndex(Mathf.Clamp(
                    _boardEditOriginalSiblingIndex,
                    0,
                    Mathf.Max(0, _boardEditOriginalParent.childCount - 1)));
                boardRect.anchorMin = _boardEditOriginalAnchorMin;
                boardRect.anchorMax = _boardEditOriginalAnchorMax;
                boardRect.anchoredPosition = _boardEditOriginalPosition;
                boardRect.sizeDelta = _boardEditOriginalSize;
                boardRect.pivot = _boardEditOriginalPivot;
            }
        }

        private void CancelRewardTableEditSubflow()
        {
            if (!_rewardTableEditActive)
            {
                return;
            }

            _rewardTableEditActive = false;
            (_world ?? BattleWorldController.Instance)?.HideWorld();
            RestoreRewardTableEditShell();
            RestoreBoardEditPanelParent();
            _afterRewardTableEdit = null;
            _rewardTableEditRootView = GameplayView.None;
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
            Action<int> onPick,
            Action onFinish)
        {
            return _rewardPage != null && _rewardPage.OpenRewardItemChoices(group, choices, kind, onPick, onFinish);
        }

        public bool OpenRandomizedItemsPanel(string title, IReadOnlyList<RandomizedItemResult> results)
        {
            return _rewardPage != null && _rewardPage.OpenRandomizedItemsPanel(title, results);
        }

        private ActionSelectSnapshot CaptureActionSelectSnapshot()
        {
            return _pageRouter != null ? _pageRouter.CaptureActionSelection() : ActionSelectSnapshot.None;
        }

        private void RestoreActionSelection(ActionSelectSnapshot snapshot)
        {
            _pageRouter?.RestoreActionSelection(snapshot);
        }

        private void PlayRandomizedItemFlys(IReadOnlyList<RandomizedItemResult> results)
        {
            if (results == null || _randomizedItemsPanel == null || _itemsColumn == null)
            {
                RefreshItems();
                return;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            RectTransform layer = canvas != null ? canvas.transform as RectTransform : transform.root as RectTransform;
            if (layer == null)
            {
                RefreshItems();
                return;
            }

            Canvas.ForceUpdateCanvases();
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

                PlayItemFlyTween(
                    new RectSnapshot(startCenter, startSize),
                    new RectSnapshot(targetCenter, targetSize),
                    RunItemSlotView.LoadIcon(item) ?? LoadShopItemFallbackIcon(item.Kind),
                    RunItemSlotView.QualityColor(item.Quality),
                    null);
            }

            RefreshItems();
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

            _world.SetTableArea(_boardArea);
            _world.Initialize(
                _run,
                _session,
                SetMessage,
                SetSettlementScore,
                RefreshAll,
                null,
                OnDishClicked,
                resetDoodle: false,
                serveTriggerCueSink: OnServeTriggerCue);
            _world.SetDishHoverCallbacks(OnDishHoverEntered, OnDishHoverExited);
            _world.SetCellHoverCallbacks(OnCellHoverEntered, OnCellHoverExited);
            _world.SetTableFragmentHoverCallbacks(OnTableFragmentHoverEntered, OnTableFragmentHoverExited);
            if (_session.IsSettled && _run.HasPendingRewardBattleView)
            {
                RestorePendingRewardPresentation(_run.GetPendingRewardBattleView());
            }
            RefreshAll();
        }

        /// <summary>奖励窗重新显示前，先退出查看页并恢复其原始返回页。</summary>
        public void ReturnToPendingRewardView(Action onReturned)
        {
            if (_current == GameplayView.TableView && _tableCoordinator != null)
            {
                _tableCoordinator.Back(onReturned);
                return;
            }

            if (_current == GameplayView.RecipeInspect && _recipeBookPage != null)
            {
                _recipeBookPage.CloseInspect(onReturned);
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

        private void OnTableEditDone(bool placed)
        {
            FinishRewardTableEditSubflow(placed, null);
        }

        private void OnRewardTableEditDone(bool placed)
        {
            Action<bool> cb = _afterRewardTableEdit;
            _afterRewardTableEdit = null;
            if (!_rewardTableEditActive)
            {
                cb?.Invoke(placed);
                return;
            }

            FinishRewardTableEditSubflow(placed, cb);
        }

        private void ApplyTableEditActionState(TableFragmentEditActionState state)
        {
            _boardEditActionCanConfirm = state.CanConfirm;
            _boardEditActionInteractable = state.Interactable;
            if (_boardEditActionButton == null)
            {
                return;
            }

            _boardEditActionButton.interactable = state.Interactable;
            if (_boardEditActionButton.targetGraphic != null)
            {
                _boardEditActionButton.targetGraphic.color =
                    state.CanConfirm ? BoardEditConfirmColor : _boardEditSkipColor;
            }

            TMP_Text label = _boardEditActionButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = state.CanConfirm ? "确认" : "跳过";
            }
        }

        private void OnTableEditActionClicked()
        {
            if (_rewardPeekOnly || !_boardEditActionInteractable)
            {
                return;
            }

            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if (_boardEditActionCanConfirm)
            {
                world?.ConfirmTableEditPlacement();
            }
            else
            {
                world?.SkipTableEditPack();
            }
        }

        private void SetActionAxisVisible(bool visible)
        {
            if (_actionAxisBar != null)
            {
                _actionAxisBar.gameObject.SetActive(visible);
            }
        }

        private void SetFoodBattlePanelVisible(bool visible)
        {
            if (_foodBattlePanel != null)
            {
                _foodBattlePanel.SetActive(visible);
            }

            _foodBar?.SetVisible(visible);
            ServingOutletView servingOutlet = ResolveServingOutlet();
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            servingOutlet?.ConfigureWorldSpace(world != null ? world.WorldCamera : Camera.main);
            servingOutlet?.SetVisible(visible);
            FoodDiscardBinView discardBin = ResolveFoodDiscardBin();
            discardBin?.ConfigureWorldSpace(world != null ? world.WorldCamera : Camera.main);
            discardBin?.Bind(_session);
            discardBin?.SetVisible(visible && !_bossDiscardRevealPending);
        }

        /// <summary>初始化经营挑战态的出菜口、弃置区与世界拖拽回调。</summary>
        private void BuildBattleControls()
        {
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            ServingOutletView servingOutlet = ResolveServingOutlet();
            FoodDiscardBinView discardBin = ResolveFoodDiscardBin();
            servingOutlet?.ConfigureWorldSpace(world != null ? world.WorldCamera : Camera.main);
            discardBin?.ConfigureWorldSpace(world != null ? world.WorldCamera : Camera.main);
            discardBin?.Bind(_session);
            if (world != null)
            {
                world.SetPreparedDishDiscardTarget(
                    discardBin == null ? null : discardBin.CanAcceptDropAt,
                    discardBin == null ? null : discardBin.SetDragHovered);
            }

            servingOutlet?.Bind(
                _session,
                () => OpenRecipeInspect(0, useBattleRecipe: true),
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
            _infoColumn?.Refresh(_run, _session, _current, world);
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

            _cakeLayerBuffHud.BindHalfDayCost(
                _run != null ? _run.NextDailyActionHalfCostStacks : 0,
                _tips != null ? _tips.Item : null);
        }

        private void RefreshFoodActions()
        {
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            _foodBar?.Refresh(_current == GameplayView.Food, _session, world);
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

        /// <summary>商店内数据变化回调：刷新常驻壳信息。</summary>
        private void RefreshShopPersistent()
        {
            _shopPage?.RefreshPersistent();
        }

        private void PlayShopPurchaseAnimation(ShopEntry entry, ShopBuyItemViewBase sourceCard)
        {
            if (entry == null || sourceCard == null || entry.Kind == ShopEntryKind.Fragment)
            {
                return;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            RectTransform layer = canvas != null ? canvas.transform as RectTransform : transform.root as RectTransform;
            RectTransform sourceRect = sourceCard.PurchaseFlySource;
            if (layer == null || sourceRect == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            if (!TryGetRectInLayer(sourceRect, layer, out RectSnapshot start))
            {
                return;
            }

            if (entry.Kind == ShopEntryKind.Dish)
            {
                PlayShopFoodPurchase(sourceCard, layer, start);
                return;
            }

            PlayShopItemPurchase(entry, sourceCard, layer, start);
        }

        private void PlayShopFoodPurchase(
            ShopBuyItemViewBase sourceCard,
            RectTransform layer,
            RectSnapshot start)
        {
            RectTransform target = _infoColumn?.ViewRecipeButtonRect;
            if (target == null ||
                !TryGetRectInLayer(target, layer, out RectSnapshot end))
            {
                return;
            }

            RenderTexture texture = sourceCard.CapturePurchaseFlyTexture();
            if (texture == null)
            {
                return;
            }

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
            if (choice == null || sourceCard == null)
            {
                return null;
            }

            Func<bool> play = PrepareRewardItemSelectionFly(
                choice,
                kind,
                sourceCard.SelectionFlySource,
                sourceCard.SelectionFlySprite);
            return play == null ? null : () => play.Invoke();
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
            ShopBuyItemViewBase sourceCard,
            RectTransform layer,
            RectSnapshot start)
        {
            if (_itemsColumn == null)
            {
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
                return;
            }

            ShopPurchaseFlyView fly = CreateShopPurchaseFly(layer);
            if (fly == null)
            {
                return;
            }

            Sprite sprite = sourceCard.PurchaseFlySprite ?? RunItemSlotView.LoadIcon(item) ?? LoadShopItemFallbackIcon(item.Kind);
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
            RefreshItems();
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
            _deck?.ShowActionChoices(
                BuildDisplayedActionChoices(RollChoices(_run)),
                OnActionSelectionPicked,
                OnActionRerollClicked,
                _run != null ? _run.ActionRerollCount : 0);
        }

        private List<ActionChoice> BuildDisplayedActionChoices(IReadOnlyList<ActionChoice> baseChoices)
        {
            var result = new List<ActionChoice>();
            if (baseChoices == null)
            {
                return result;
            }

            bool applyHalfDay = _run != null && _run.NextDailyActionHalfCostStacks > 0;
            foreach (ActionChoice choice in baseChoices)
            {
                if (choice == null)
                {
                    continue;
                }

                result.Add(new ActionChoice(
                    choice.Action,
                    choice.ActionGroupId,
                    choice.WeekStepIndex,
                    choice.RunStepIndex,
                    applyHalfDay ? _run.PreviewDailyActionCost(choice.CostDays) : choice.CostDays,
                    halfDayBuffApplied: applyHalfDay,
                    timelineStopChance: choice.TimelineStopChance));
            }

            return result;
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
            if (run == null)
            {
                return new List<ActionChoice>();
            }

            string key = GameRun.BuildActionChoiceKey(run.RunActionStepIndex, run.WeekIndex, run.CurrentDay, run.ActionStepIndex);
            if (run.HasPendingActionChoices(key))
            {
                return run.GetPendingActionChoices(key);
            }

            IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Action, key);
            List<ActionChoice> choices = ActionScheduleService.GenerateChoices(run, rng);
            run.SetPendingActionChoices(key, choices);
            RunPersistence.Save(run);
            return choices;
        }

        private void OnActionRerollClicked()
        {
            if (_run == null || !_run.TrySpendActionReroll())
            {
                BuildActionCards();
                PlayShowCardsWhenReady();
                return;
            }

            string key = GameRun.BuildActionChoiceKey(_run.RunActionStepIndex, _run.WeekIndex, _run.CurrentDay, _run.ActionStepIndex);
            IReadOnlyList<ActionChoice> previous = _run.HasPendingActionChoices(key)
                ? _run.GetPendingActionChoices(key)
                : RollChoices(_run);
            IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Action, key + "_player_reroll_" + _run.NextActiveUseKey());
            List<ActionChoice> rerolled = ActionScheduleService.RerollChoices(_run, rng, previous);
            _run.SetPendingActionChoices(key, rerolled);
            RunPersistence.Save(_run);
            RebuildActionAxis();
            RefreshActionCardsAnimated();
        }

        /// <summary>玩家在中部选择了一个行动（null = 无行动可选时的「休息」）。</summary>
        private void OnActionSelectionPicked(ActionChoice choice)
        {
            _run?.ClearPendingActionChoices();
            _deck?.HideThenDestroy(() =>
            {
                _loop?.OnActionPicked(choice);
            });
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

        private void PlayShowCardsWhenReady()
        {
            _deck?.PlayShowWhenReady(() => _current == GameplayView.ActionSelect);
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
            _rewardPage?.CloseRewardPages();
            CancelRewardTableEditSubflow();
            ClearTimelineNodeCard();
            _pageRouter?.HideHud();
            _tableCoordinator?.CancelPendingTransition();
            _inspectionNavigation?.Clear();
            SyncPageStateFromRouter();
            _infoColumn?.ScoreFire?.Hide();
            _infoColumn?.ResetTableLabel();
            SetMessage(string.Empty);
        }

        private void OnSettingsClicked()
        {
            if (_rewardPeekOnly || HasPendingBattleRewardLifecycle)
            {
                return;
            }

            GameApp.UI.OpenUIForm(UIForms.Settings, UIForms.GroupDialog, new SettingsFormData(inGameplay: true));
        }

        private void OnViewRecipeClicked()
        {
            if (_run == null || _current == GameplayView.None)
            {
                return;
            }

            if (_rewardTableEditActive)
            {
                return;
            }

            if (IsViewToggleTransitioning())
            {
                return;
            }

            if (_current == GameplayView.TableView && _tableCoordinator != null)
            {
                Action atSwap = _tableCoordinator.BeginPeerExit();
                if (atSwap != null)
                {
                    _recipeBookPage?.OpenInspect(0, atSwap: atSwap);
                }
                return;
            }

            OpenRecipeInspect(0);
        }

        private string BuildRecipeMutationText(RecipeMutationResult result)
        {
            var lines = new List<string>();
            foreach (RecipeMutationEntry entry in result.Entries)
            {
                string before = DescribeDishSnapshot(entry.Before);
                string after = DescribeDishSnapshot(entry.After);
                lines.Add($"食谱{entry.BookIndex + 1}-{entry.DishIndex + 1}: {before}  ->  {after}");
            }

            return string.Join("\n", lines);
        }

        private string BuildCellMutationText(CellMutationResult result)
        {
            var lines = new List<string>();
            foreach (CellMutationEntry entry in result.Entries)
            {
                string materialName = MaterialName(entry.MaterialId);
                lines.Add($"格子 ({entry.Pos.X},{entry.Pos.Y}) 获得标签：{materialName}");
            }

            return string.Join("\n", lines);
        }

        private string BuildTimelineMutationText(TimelineMutationResult result)
        {
            return $"时间轴已变化：{result.Before.Count} 个节点 -> {result.After.Count} 个节点";
        }

        private string DescribeDishSnapshot(RecipeDishSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrEmpty(snapshot.DishId))
            {
                return "空";
            }

            DishDef dish = _run?.Database.GetDish(snapshot.DishId);
            string name = dish != null ? dish.Name : snapshot.DishId;
            string flavors = snapshot.FlavorIds != null && snapshot.FlavorIds.Count > 0
                ? string.Join("+", FlavorNames(snapshot.FlavorIds))
                : "无风味";
            string skills = snapshot.SkillIds != null && snapshot.SkillIds.Count > 0
                ? $"技能{snapshot.SkillIds.Count}"
                : "无技能";
            string mult = snapshot.ScoreMultiplier > 0f && Mathf.Abs(snapshot.ScoreMultiplier - 1f) > 0.0001f
                ? $" x{snapshot.ScoreMultiplier:0.##}"
                : string.Empty;
            string score = Mathf.Abs(snapshot.ScoreFlatBonus) > 0.0001f
                ? $", 美味+{snapshot.ScoreFlatBonus:0.##}"
                : string.Empty;
            return $"{name} [{flavors}, {skills}{score}{mult}]";
        }

        private List<string> FlavorNames(IReadOnlyList<string> flavorIds)
        {
            var names = new List<string>();
            foreach (string flavorId in flavorIds)
            {
                GourmetProject.Gameplay.Model.FlavorDef flavor = _run?.Database.GetFlavor(flavorId);
                names.Add(flavor != null ? flavor.Name : flavorId);
            }

            return names;
        }

        private string MaterialName(string materialId)
        {
            GourmetProject.Gameplay.Model.MaterialDef material = _run?.Database.GetMaterial(materialId);
            return material != null ? material.Name : materialId;
        }

        private string ActionName(string actionId)
        {
            cfg.GameAction action = _run?.Tables.TbAction.GetOrDefault(actionId);
            return action != null ? action.Name : actionId;
        }

        private void ShowPassiveOverlay(string title, string body, float duration, Action onDone = null)
        {
            EnsurePassiveOverlay();
            if (_passiveOverlayRoot == null || _passiveOverlayText == null)
            {
                onDone?.Invoke();
                return;
            }

            _passiveOverlaySeq?.Kill();
            _passiveOverlayText.text = string.IsNullOrEmpty(body) ? title : $"{title}\n{body}";
            var group = _passiveOverlayRoot.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            _passiveOverlayRoot.SetActive(true);
            _passiveOverlaySeq = DOTween.Sequence()
                .Append(DOTween.To(() => group.alpha, value => group.alpha = value, 1f, 0.18f))
                .AppendInterval(Mathf.Max(0.1f, duration))
                .Append(DOTween.To(() => group.alpha, value => group.alpha = value, 0f, 0.18f))
                .OnComplete(() =>
                {
                    _passiveOverlayRoot.SetActive(false);
                    onDone?.Invoke();
                });
        }

        private void EnsurePassiveOverlay()
        {
            if (_passiveOverlayRoot != null)
            {
                return;
            }

            Debug.LogError($"{nameof(BattleForm)} 缺少 PassiveMutationOverlay 预置引用。", this);
        }

        private void OnViewTableClicked()
        {
            if (_tableCoordinator == null)
            {
                return;
            }

            if (_rewardTableEditActive)
            {
                return;
            }

            if (IsViewToggleTransitioning())
            {
                return;
            }

            _world = _world ?? BattleWorldController.Instance;
            _world?.SetTableArea(_boardArea);

            if (_current == GameplayView.TableView)
            {
                return;
            }

            if (_current == GameplayView.RecipeInspect && _recipeBookPage != null)
            {
                _tableCoordinator.Open();
                return;
            }

            _tableCoordinator.Open();
        }

        private void OnExitTableViewClicked()
        {
            if (_tableCoordinator == null
                || _current != GameplayView.TableView
                || IsViewToggleTransitioning())
            {
                return;
            }

            _tableCoordinator.Back();
        }

        private bool IsViewToggleTransitioning()
        {
            return (_pageRouter != null && _pageRouter.IsTransitioning)
                || (_center != null && DOTween.IsTweening(_center, true))
                || (_tableCoordinator != null && _tableCoordinator.IsTransitioning);
        }

        public void ShowRunResult(bool win, int total)
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

        private void ShowDefeatDialog(int total)
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

        // —— 经营挑战 ——

        public void StartBattle(
            int requiredScore,
            string modifier,
            string key,
            string bossDebuffId,
            ActionExecutionContext actionContext)
        {
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
            _infoColumn?.SetBossBattlePresentation(
                _currentBossDebuff,
                _activeBattleIsBoss,
                animate: _activeBattleIsBoss && _currentBossDebuff != null);
            SetMessage(string.Empty);
            UnsubscribeCakeLayerChanges();
            _session = _run.BuildBattleSession(requiredScore, modifier, key, _activeBossDebuffId);
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
                    _world.SetTableArea(_boardArea);
                    if (string.Equals(bossPlan?.DebuffId, "debuff_gluttony", StringComparison.Ordinal))
                    {
                        _servingOutlet?.SetRecipeCountPresentationOverride(bossPlan.InitialRecipeEntryCount);
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
                    if (runOpeningPresentation)
                    {
                        _ = PlayBossOpeningPresentationAsync(bossPlan);
                    }
                });
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

        private async Awaitable PlayBossOpeningPresentationAsync(BossDebuffPresentationPlan plan)
        {
            await PlayBossLockedAsync(async token =>
            {
                await Awaitable.NextFrameAsync(token);
                switch (plan.DebuffId)
                {
                    case "debuff_vegetarian":
                        await SweepCellsAsync(
                            plan.DisabledCells,
                            _world.RevealBossDisabledCell,
                            token);
                        await ShowBossDialogueAsync("这几块别放肉。", token);
                        break;

                    case "debuff_indulgent":
                    case "debuff_binge":
                        await ShowBossDialogueAsync("我要多吃点", token);
                        await SweepCellsAsync(plan.AddedCells, _world.RevealBossAddedCell, token);
                        break;

                    case "debuff_weight_loss":
                    case "debuff_kids_meal":
                        await ShowBossDialogueAsync("我要少吃点", token);
                        await SweepCellsAsync(plan.RemovedCells, _world.RevealBossRemovedCell, token);
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

                _world.FinishBossPresentation();
                _servingOutlet?.SetRecipeCountPresentationOverride(null);
                RefreshAll();
                _world.EnsureNextDishPrepared(0, allowDuringBossPresentation: true);
                await ShowCarbDialogueIfNeededAsync(token);
            });
        }

        private async void OnPendingDishConfirmRequested(int dishId)
        {
            if (_session == null
                || _session.IsSettled
                || _bossPresentation?.IsPlaying == true
                || (_world != null && _world.IsFoodInteractionBusy))
            {
                return;
            }

            await PlayBossLockedAsync(token =>
                ConfirmPendingDishPresentationAsync(dishId, prepareNextDish: true, token));
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
                    await _world.PlayPendingServeTriggerCuesAsync(token);

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
                _world?.FinalizePendingDishPresentation(result, prepareNextDish: false);
            }

            if (prepareNextDish)
            {
                _world.EnsureNextDishPrepared(0, allowDuringBossPresentation: true);
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
            Canvas.ForceUpdateCanvases();
            Camera layerCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            if (layer == null
                || _servingOutlet == null
                || !_servingOutlet.TryGetRecipeInfoButtonScreenPoint(out Vector2 targetScreenPoint)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    layer,
                    targetScreenPoint,
                    layerCamera,
                    out Vector2 recipeInfoButtonCenter)
                || plan.DuplicatedDishIds.Count == 0)
            {
                return;
            }

            IRandomStream cosmetic = GameApp.Random.Cosmetic(
                $"boss_gluttony_{_activeBattleKey}_{plan.DebuffId}");
            var spriteProvider = new DishSpriteProvider();
            int arrived = 0;
            int finished = 0;
            int total = plan.DuplicatedDishIds.Count;
            foreach (string dishId in plan.DuplicatedDishIds)
            {
                token.ThrowIfCancellationRequested();
                ShopPurchaseFlyView fly = CreateShopPurchaseFly(layer);
                if (fly == null)
                {
                    arrived++;
                    finished++;
                    continue;
                }

                RegisterShopPurchaseFly(fly);
                float marginX = Mathf.Min(110f, layer.rect.width * 0.15f);
                float marginY = Mathf.Min(90f, layer.rect.height * 0.15f);
                Vector2 start = new Vector2(
                    cosmetic.Range(layer.rect.xMin + marginX, layer.rect.xMax - marginX),
                    cosmetic.Range(layer.rect.yMin + marginY, layer.rect.yMax - marginY));
                Sprite sprite = spriteProvider.Get(_run.Database.GetDish(dishId));
                fly.PlayFoodSprite(
                    start,
                    new Vector2(112f, 112f),
                    recipeInfoButtonCenter,
                    sprite,
                    () =>
                    {
                        arrived++;
                        _servingOutlet?.SetRecipeCountPresentationOverride(
                            plan.InitialRecipeEntryCount + arrived);
                        RefreshFoodActions();
                    },
                    () =>
                    {
                        finished++;
                        UnregisterShopPurchaseFly(fly);
                    });
            }

            while (finished < total)
            {
                token.ThrowIfCancellationRequested();
                await Awaitable.NextFrameAsync(token);
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

            if (_bossPresentationCts == null || _bossPresentationCts.IsCancellationRequested)
            {
                _bossPresentationCts?.Dispose();
                _bossPresentationCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            }

            _world.SetBossPresentationBusy(true);
            try
            {
                await _bossPresentation.PlayLockedAsync(sequence, _bossPresentationCts.Token);
            }
            finally
            {
                _world?.SetBossPresentationBusy(false);
                RefreshAll();
            }
        }

        private void CancelBossPresentation()
        {
            _bossPresentationCts?.Cancel();
            _bossPresentationCts?.Dispose();
            _bossPresentationCts = null;
            _bossDialogueBag = null;
            _bossDiscardRevealPending = false;
            _bossPresentation?.CancelCurrent();
            (_world ?? BattleWorldController.Instance)?.SetBossPresentationBusy(false);
            _servingOutlet?.SetRecipeCountPresentationOverride(null);
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

        private void OnBattleServed(DishInstance dish, int servesUsed) => RefreshPersistent();

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
            tips.PlaceAroundWorldBounds(
                servingOutlet.DishWorldBounds,
                world != null ? world.WorldCamera : Camera.main,
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
                    _session.LastResult,
                    _session.IsSettled ? null : _session.PreviewEffectiveCountAs(piece.Instance)));
            }
            else
            {
                ScoreResult preview = _session.IsSettled ? _session.LastResult : null;
                tips.Bind(FoodTipsDataFactory.Build(
                    piece.Instance,
                    _session.DiningTable,
                    _session.Database,
                    preview,
                    _session.IsSettled ? null : _session.PreviewEffectiveCountAs(piece.Instance)));
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

        private void OnCellHoverEntered(DiningTableCellView cell)
        {
            if (cell == null || _tips == null)
            {
                return;
            }

            if (_current != GameplayView.Food
                && _current != GameplayView.TableView
                && _current != GameplayView.TableEdit
                && !_rewardTableEditActive)
            {
                return;
            }

            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if ((_current == GameplayView.TableEdit || _rewardTableEditActive)
                && world != null
                && world.IsTableEditDragging)
            {
                HideCellMaterialTips(cell);
                return;
            }

            if (!TryGetCellTipsContext(out DiningTable table, out GameplayDatabase db))
            {
                HideCellMaterialTips(cell);
                return;
            }

            if (table == null || !table.Exists(cell.Position) || table.DishAt(cell.Position) != null)
            {
                HideCellMaterialTips(cell);
                return;
            }

            IReadOnlyList<FoodMaterialTipsEntry> materials = FoodTipsDataFactory.BuildMaterialsForCells(
                new[] { cell.Position },
                table,
                db);
            if (materials == null || materials.Count == 0)
            {
                HideCellMaterialTips(cell);
                return;
            }

            FoodTipsView tips = _tips.Food;
            if (tips == null)
            {
                return;
            }

            (_world ?? BattleWorldController.Instance)?.ClearDishScopeHighlights();
            _hoveredDishPiece = null;
            _hoveredCell = cell;
            _foodTipsHoverOwner = FoodTipsHoverOwner.TableCell;
            tips.BindMaterialsOnly(materials);
            tips.Show();
            tips.transform.SetAsLastSibling();
            tips.PlaceAroundWorldBounds(cell.WorldBounds, Camera.main, GetComponentInParent<Canvas>());
        }

        private bool TryGetCellTipsContext(out DiningTable table, out GameplayDatabase db)
        {
            if (_current == GameplayView.Food && _session != null)
            {
                table = _session.DiningTable;
                db = _session.Database;
                return table != null && db != null;
            }

            if (_current == GameplayView.TableView && _run != null)
            {
                table = _run.BuildTablePreviewFromFragments();
                db = _run.Database;
                return table != null && db != null;
            }

            if ((_current == GameplayView.TableEdit || _rewardTableEditActive) && _run != null)
            {
                table = (_world ?? BattleWorldController.Instance)?.ActiveTable;
                db = _run.Database;
                return table != null && db != null;
            }

            table = null;
            db = null;
            return false;
        }

        private void OnCellHoverExited(DiningTableCellView cell)
        {
            HideCellMaterialTips(cell);
        }

        private void HideCellMaterialTips(DiningTableCellView cell)
        {
            if (_foodTipsHoverOwner != FoodTipsHoverOwner.TableCell)
            {
                return;
            }

            if (_hoveredCell != null && cell != null && _hoveredCell != cell)
            {
                return;
            }

            HideFoodTips();
        }

        private void OnTableFragmentHoverEntered(TableFragmentHoverInfo info)
        {
            TableFragmentDef fragment = info.Definition;
            if ((_current != GameplayView.TableEdit && !_rewardTableEditActive)
                || fragment == null
                || _run?.Database == null
                || _tips == null)
            {
                return;
            }

            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if (world != null && world.IsTableEditDragging)
            {
                return;
            }

            IReadOnlyList<FoodMaterialTipsEntry> materials =
                FoodTipsDataFactory.BuildMaterialsForFragment(fragment, _run.Database);
            if (materials == null || materials.Count == 0)
            {
                HideTableFragmentMaterialTips(info);
                return;
            }

            FoodTipsView tips = _tips.Food;
            if (tips == null)
            {
                return;
            }

            _hoveredDishPiece = null;
            _hoveredCell = null;
            _hoveredTableFragmentSession = info.SessionVersion;
            _hoveredTableFragmentIndex = info.CandidateIndex;
            _foodTipsHoverOwner = FoodTipsHoverOwner.TableFragment;
            tips.BindMaterialsOnly(materials);
            tips.Show();
            tips.transform.SetAsLastSibling();
            tips.PlaceAroundWorldBounds(
                info.WorldBounds,
                world != null ? world.WorldCamera : Camera.main,
                GetComponentInParent<Canvas>());
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

            // 结算前拍基线：演出用它逐 cue 揭示，hover tips 与表演同步，而非一上来就显示全部结算信息。
            var reveal = new SettlementRevealState();
            var settlementBaseline = new SettlementBaselineSnapshot();
            foreach (DishInstance dish in _session.DiningTable.Dishes)
            {
                reveal.CaptureBaseline(dish);
                settlementBaseline.Capture(dish);
            }

            _settlementReveal = reveal;
            _pendingSettlementCakeLayers = null;
            _pendingSettlementCakeLayerBonus = 0;

            ScoreResult result = _session.Settle();
            _pendingSettlementCakeLayerBonus = Mathf.Max(
                0,
                _session.HappyCakeLayers - _displayedCakeLayers - result.HappyCakeLayerDelta);
            ApplyRecipeScoreDeltasToRun();
            SetSettlementScore(0);
            RefreshFoodActions();

            if (_world != null)
            {
                _world.PlaySettlement(
                    result,
                    settlementBaseline,
                    _infoColumn != null ? _infoColumn.ScoreFire : null,
                    OnSettlementReveal,
                    OnSettlementPassiveTriggered,
                    null,
                    () => OnSettlementComplete(result));
            }
            else
            {
                OnSettlementComplete(result);
            }
        }

        private void OnSettlementComplete(ScoreResult result)
        {
            if (_discardSettlementCallbacks || Active != this || _loop == null)
            {
                return;
            }

            StopBattleMusic(0.8f);

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

            // 结算侧效果写回局外状态：金币入账（经济运营 + 上菜 OnServe）、大局结算历史累计。
            if (_run != null && _session != null)
            {
                int gold = (int)System.Math.Round(_session.PendingGold, System.MidpointRounding.AwayFromZero);
                if (gold != 0)
                {
                    _run.Gold = System.Math.Max(0, _run.Gold + gold);
                }

                // 银材质命中：发放消耗品（掷骰已在 BattleSession 正式结算时完成，这里只落地选取具体装饰品和消耗品）。
                int silverItems = _session.PendingActiveItemGrants;
                if (silverItems > 0)
                {
                    string itemKey = $"silver_{_run.WeekIndex}_{_run.RunActionStepIndex}_{_session.ServesUsed}";
                    IRandomStream itemRng = GameApp.Random.DomainStream(SeedDomains.Item, itemKey);
                    for (int i = 0; i < silverItems; i++)
                    {
                        GourmetProject.Game.Meta.ItemPoolService.GrantRandom(
                            GameApp.Config.Tables, _run, cfg.ItemKind.Active, itemRng, 20);
                    }
                }

                _run.AddSettledCounts(_session.LastSettledIncrements);
            }

            // 领奖期间允许隐藏奖励页查看本场结果，因此保留最终美味值；
            // 奖励全部领取并离开营业时，HideBattleWorld 会再将其清空。
            _infoColumn?.SetBattleScoreOverride(result.Total);
            RefreshAll();

            // 待领奖期间保留最终层数和世界表现；只有玩家明确点击“继续行动”才结束本场生命周期。
            BattleSession settledSession = _session;
            int finalHappyCakeLayers = settledSession?.HappyCakeLayers ?? 0;
            bool isWin = settledSession != null && settledSession.IsWin;
            _loop?.OnBattleSettled(result, isWin, finalHappyCakeLayers);
        }

        private void OnGoldChanged(int before, int after)
        {
            if (after > before)
            {
                GameApp.Audio.PlayRandomCoin();
            }
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

        private void ApplyRecipeScoreDeltasToRun()
        {
            if (_run == null || _session == null)
            {
                return;
            }

            foreach (RecipeScoreFlatDelta delta in _session.LastRecipeScoreFlatDeltas)
            {
                _run.AddRecipeScoreFlat(delta.DishIndex, delta.Delta);
            }

            foreach (RecipeScoreMultiplierDelta delta in _session.LastRecipeScoreMultiplierDeltas)
            {
                _run.MultiplyRecipeScore(delta.DishIndex, delta.Multiplier);
            }
        }

        private void RefreshAll()
        {
            _world?.RefreshAll();
            if (_inBattle)
            {
                RefreshPersistent();
                BuildBattleControls();
            }
        }

        // —— 局内交互（透传到经营挑战世界）——

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

        private void SetMessage(string message)
        {
            return;
        }

        private void SetSettlementScore(int score)
        {
            _infoColumn?.SetBattleScoreOverride(score);
            RefreshPersistent(refreshItems: false);
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
