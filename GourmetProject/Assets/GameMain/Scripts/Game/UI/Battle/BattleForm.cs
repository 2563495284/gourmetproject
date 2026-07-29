using System;
using System.Collections.Generic;
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

namespace GourmetProject.Game.UI.Battle
{
    /// <summary>
    /// 局外周循环编排枢纽 + 局内战斗结果壳。
    /// 常驻壳（左列信息 / 右列道具 / 顶部行动轴）进入玩法后全程常驻，只有中部内容区在五态间切换：
    /// 行动选择(含事件 n 选一) / 商店 / 菜谱查看与选择 / 美食战斗 / 餐桌编辑。切换只对中部内容区做 DOTween 渐隐渐显
    /// （<see cref="UITransition.FadeSwap"/>），常驻壳不参与动画；美食 / 餐桌态在同一 Battle 场景内透出世界空间表现。
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

        private enum FoodTipsHoverOwner
        {
            None,
            TableDish,
            TableCell,
            TableFragment,
            ServingOutlet,
        }

        /// <summary>当前打开的战斗界面，供各弹窗回调推进周循环。</summary>
        public static BattleForm Active { get; private set; }

        [Header("HUD Frame")]
        [SerializeField] private GameObject _hudFrame;
        [SerializeField] private GameObject _backdrop;

        [Header("Center (切换动画区)")]
        [Tooltip("中部内容区根的 CanvasGroup：五态切换时对它做渐隐渐显；常驻壳不在其下。")]
        [SerializeField] private CanvasGroup _center;

        [Header("DiningTable Area (餐桌锁定区)")]
        [Tooltip("HUD 里的空区矩形：世界餐桌将 fit 并居中锁定在该屏幕区域内。")]
        [SerializeField] private RectTransform _boardArea;

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
        [SerializeField] private RewardDishPackPanel _rewardDishPackPanel;
        [SerializeField] private RewardItemChoicePanel _rewardItemChoicePanelPrefab;
        private RewardItemChoicePanel _rewardItemChoicePanel;
        [SerializeField] private RandomizedItemsPanel _randomizedItemsPanel;

        [Header("Event Page (center)")]
        [FormerlySerializedAs("_eventPanel")]
        [SerializeField] private EventPagePanel _eventPagePanel;

        [Header("DiningTable Edit")]
        [SerializeField] private Button _boardEditSkipButton;

        [Header("Right Column - Items")]
        [SerializeField] private BattleItemsColumn _itemsColumn;

        [Header("Active Items")]
        [SerializeField] private ActiveItemActionPopup _activeItemPopupPrefab;
        [SerializeField] private TargetArrowView _activeItemTargetArrowPrefab;
        [SerializeField] private ActiveItemTargetOverlayView _activeItemTargetOverlayPrefab;
        [SerializeField] private GameObject _shopItemFlyFxPrefab;

        [Header("Food Actions")]
        [SerializeField] private BattleFoodActionBar _foodBar;
        [SerializeField] private ServingOutletView _servingOutlet;
        [SerializeField] private FoodDiscardBinView _foodDiscardBin;

        [Header("Battle Message")]
        [SerializeField] private Text _messageText;

        private bool _inBattle;
        private GameplayView _current = GameplayView.None;
        private Action<bool> _afterRewardTableEdit;

        private GameRun _run;
        private BattleSession _session;
        private BattleWorldController _world;

        private WeekLoopController _loop;

        private TimelineAxisBinder _axisBinder;
        private TableViewCoordinator _tableCoordinator;
        private GameplayPageRouter _pageRouter;
        private ShopPageCoordinator _shopPage;
        private RecipeBookCoordinator _recipeBookPage;
        private RewardPageCoordinator _rewardPage;
        private EventPageCoordinator _eventPage;
        private ActiveItemUseCoordinator _activeItemUse;
        private int _shopItemFlyInFlight;
        private readonly HashSet<ShopPurchaseFlyView> _activeShopPurchaseFlys = new();
        private cfg.BossDebuff _currentBossDebuff;
        private cfg.TimelineNode _currentTimelineNodeCard;
        private int? _currentTimelineNodeInterestMaxGain;
        private Action _currentTimelineNodePick;
        private int _activeBattleRawRequiredScore;
        private string _activeBattleModifier = string.Empty;
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
        [SerializeField] private Text _passiveOverlayText;
        private Sequence _passiveOverlaySeq;

        public GameRun Run => _run;
        public BattleSession Session => _session;
        public event Action<ShopEntryKind> ShopPurchaseAnimationStarted;
        public ActionExecutionContext CurrentBattleActionContext => _loop?.CurrentBattleActionContext;
        internal GameRun ActiveRun => _run;
        internal BattleSession ActiveSession => _session;
        internal WeekLoopController ActiveLoop => _loop;
        internal GameplayView CurrentView => _current;
        internal bool InBattle => _inBattle;
        internal bool IsDailyActionSelectionActive =>
            _current == GameplayView.ActionSelect && _currentTimelineNodeCard == null;
        internal bool IsViewingBattleTable => _tableCoordinator != null && _tableCoordinator.IsViewingBattleTable;
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

            _infoColumn?.Bind(OnSettingsClicked, OnViewTableClicked, OnViewRecipeClicked);
            _cakeLayerBuffHud = GetComponent<CakeLayerBuffHud>();
            _foodBar?.Bind(OnEatClicked, OnDoodleClearClicked, OnDoodleToggleClicked);

            if (_boardEditSkipButton != null)
            {
                _boardEditSkipButton.onClick.AddListener(OnTableEditSkipClicked);
            }

            _axisBinder = new TimelineAxisBinder(
                _actionAxisBar,
                () => _tips != null ? _tips.Shop : null,
                () => _tips != null ? _tips.Interest : null,
                () => _tips != null ? _tips.Boss : null);
            _tableCoordinator = new TableViewCoordinator(this);
            _pageRouter = new GameplayPageRouter(this);
            _shopPage = new ShopPageCoordinator(this);
            _recipeBookPage = new RecipeBookCoordinator(this);
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
            // 尽早绑定场景里的战斗世界单例：否则首次 StartBattle 之前 _world 为 null，
            // BeginWeek 里的 HideBattleWorld 会变成空操作，导致进场景默认态残留美食专属按钮。
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
            CancelActiveShopPurchaseAnimations();
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

            base.OnClose(isShutdown, userData);
        }

        private void Update()
        {
            if (_rewardPeekOnly)
            {
                return;
            }

            _activeItemUse?.Update();
        }

        // —— 周循环编排（代理到 WeekLoopController）——

        public int LastBattleTotal => _session?.LastResult?.Total ?? 0;

        /// <summary>进入（或继续）一周：随机/沿用行动轴后开始行动循环。</summary>
        public void BeginWeek()
        {
            UnsubscribeCakeLayerChanges();
            _session = null;
            _loop?.BeginWeek();
        }

        /// <summary>行动轴未走完则弹「n 选一行动」；走完则进入下一周。</summary>
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

        /// <summary>RewardForm 发奖确认后回调：继续战斗后的编排续接。</summary>
        public void OnRewardConfirmed()
        {
            _loop?.OnRewardConfirmed();
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
            _infoColumn?.SetBattleScoreOverride(null);
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            world?.HideWorld();
            world?.ClearBattleTable();
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
                BattleKey = _activeBattleKey ?? string.Empty,
                IsBoss = _activeBattleIsBoss,
                LastTotal = _session.LastResult.Total,
                Dishes = new List<PendingRewardBattleDishSaveData>(),
            };

            foreach (DishInstance dish in _session.DiningTable.Dishes)
            {
                if (dish?.Def == null)
                {
                    continue;
                }

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
                });
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
            _activeBattleKey = string.IsNullOrEmpty(snapshot.BattleKey)
                ? (_run.PendingRewardKey ?? string.Empty)
                : snapshot.BattleKey;
            _activeBattleIsBoss = snapshot.IsBoss;
            _currentBossDebuff = _activeBattleIsBoss ? ResolveBossDebuff(_activeBattleModifier) : null;

            UnsubscribeCakeLayerChanges();
            _session = _run.BuildBattleSession(_activeBattleRawRequiredScore, _activeBattleModifier, _activeBattleKey);
            _displayedCakeLayers = _session.HappyCakeLayers;
            _pendingSettlementCakeLayers = null;
            _session.HappyCakeLayersChanged += OnHappyCakeLayersChanged;
            _session.DiningTable.Clear();
            RestorePendingRewardBattleDishes(_session, snapshot);
            _session.RestoreSettledForRewardView(snapshot.LastTotal);

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
            SetSettlementScore(snapshot.LastTotal);
            RefreshAll();
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

            if (GameApp.UI.HasUIForm(UIForms.Defeat))
            {
                var form = GameApp.UI.GetUIForm(UIForms.Defeat);
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

        /// <summary>行动轴节点卡片：先展示节点卡，玩家点击后再执行节点效果。</summary>
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
        /// 切到某一中部态：只对中部内容区 <see cref="_center"/> 做 DOTween 渐隐渐显，常驻壳（左/右/行动轴）不动。
        /// 内容交换（隐藏旧面板 + 启用新面板 + 重建）集中在淡出完成后的 <see cref="GameplayViewStateMachine.Apply"/> 里执行。
        /// </summary>
        private void SwitchTo(GameplayView next, Action buildCenter = null, Action onShown = null)
        {
            _pageRouter?.SwitchTo(next, buildCenter, () =>
            {
                SyncPageStateFromRouter();
                onShown?.Invoke();
            });
            SyncPageStateFromRouter();
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

        /// <summary>菜谱页面：打开查看、删除或主动道具选菜流程。</summary>
        private void OpenRecipeBookPanel()
        {
            _recipeBookPage?.OpenPanel();
        }

        // —— Page router / coordinators host implementations ——

        GameRun IGameplayPageRouterHost.Run => _run;
        BattleWorldController IGameplayPageRouterHost.World => _world ?? BattleWorldController.Instance;
        CanvasGroup IGameplayPageRouterHost.Center => _center;
        GameObject IGameplayPageRouterHost.HudFrame => _hudFrame;
        GameObject IGameplayPageRouterHost.Backdrop => _backdrop;
        GameObject IGameplayPageRouterHost.ActionSelectionPanel => _actionSelectionPanel;
        ActionCardDeck IGameplayPageRouterHost.Deck => _deck;
        ShopForm IGameplayPageRouterHost.ShopPanel => _shopPanel;
        RecipeReadonlyBookView IGameplayPageRouterHost.RecipeReadonlyBookView => _recipeReadonlyBookView;
        RewardDishPackPanel IGameplayPageRouterHost.RewardDishPackPanel => _rewardDishPackPanel;
        RewardItemChoicePanel IGameplayPageRouterHost.RewardItemChoicePanel => _rewardItemChoicePanel;
        RandomizedItemsPanel IGameplayPageRouterHost.RandomizedItemsPanel => _randomizedItemsPanel;
        EventPagePanel IGameplayPageRouterHost.EventPagePanel => _eventPagePanel;
        Button IGameplayPageRouterHost.BoardEditSkipButton => _boardEditSkipButton;
        bool IGameplayPageRouterHost.RecipeInspectShowsActionAxis => _recipeBookPage?.InspectShowsActionAxis == true;
        void IGameplayPageRouterHost.OnLeavingPage(GameplayView current, GameplayView next) => _recipeBookPage?.OnLeavingPage(current, next);
        void IGameplayPageRouterHost.OnBeforeApplyPage(GameplayView view)
        {
            if (view != GameplayView.Food)
            {
                HideFoodTips();
            }
        }

        void IGameplayPageRouterHost.SetActionAxisVisible(bool visible) => SetActionAxisVisible(visible);
        void IGameplayPageRouterHost.SetFoodActionsVisible(bool visible) => SetFoodActionsVisible(visible);
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
        void IRecipeBookHost.PlayShowCardsWhenReady() => PlayShowCardsWhenReady();
        FoodTipsView IRecipeBookHost.FoodTips() => _tips != null ? _tips.Food : null;

        GameRun IShopPageHost.Run => _run;
        ShopForm IShopPageHost.ShopPanel => _shopPanel;
        bool IShopPageHost.ShouldRefreshItemsAfterShopChange => _shopItemFlyInFlight <= 0;
        void IShopPageHost.OnShopClosed() => OnShopClosed();
        void IShopPageHost.RefreshPersistent(bool refreshItems) => RefreshPersistent(refreshItems);
        void IShopPageHost.OpenDeleteDish() => _recipeBookPage?.OpenShopDelete();
        void IShopPageHost.OpenTableEdit(Action onShown) => OpenTableEdit(onShown);
        void IShopPageHost.OpenRecipeInspect(int bookIndex) => _recipeBookPage?.OpenInspect(bookIndex);
        void IShopPageHost.PlayShopPurchaseAnimation(ShopEntry entry, ShopBuyItemViewBase sourceCard) => PlayShopPurchaseAnimation(entry, sourceCard);

        GameRun IRewardPageHost.Run => _run;
        GameplayView IRewardPageHost.CurrentView => _current;
        RectTransform IRewardPageHost.CenterTransform => _center != null ? _center.transform as RectTransform : null;
        RewardDishPackPanel IRewardPageHost.RewardDishPackPanel => _rewardDishPackPanel;
        RewardItemChoicePanel IRewardPageHost.RewardItemChoicePanel
        {
            get => _rewardItemChoicePanel;
            set => _rewardItemChoicePanel = value;
        }
        RewardItemChoicePanel IRewardPageHost.RewardItemChoicePanelPrefab => _rewardItemChoicePanelPrefab;
        RandomizedItemsPanel IRewardPageHost.RandomizedItemsPanel => _randomizedItemsPanel;
        void IRewardPageHost.SwitchTo(GameplayView view, Action buildCenter, Action onShown) => SwitchTo(view, buildCenter, onShown);
        void IRewardPageHost.RefreshPersistent() => RefreshPersistent();
        void IRewardPageHost.ShowActionSelection() => ShowActionSelection();
        FoodTipsView IRewardPageHost.FoodTips() => _tips != null ? _tips.Food : null;
        ItemTipView IRewardPageHost.ItemTips() => _tips != null ? _tips.Item : null;
        void IRewardPageHost.PlayRandomizedItemFlys(IReadOnlyList<RandomizedItemResult> results) => PlayRandomizedItemFlys(results);

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

        // —— 商店 / 菜谱工作区 / 餐桌编辑入口 ——

        internal void OpenActiveItemRecipeTarget(
            ItemDefinition item,
            Action onCancel,
            Action<ActiveTarget, Action> onTargetConfirmed,
            Action onOpened = null)
        {
            _recipeBookPage?.OpenActiveItemTarget(item, onCancel, onTargetConfirmed, onOpened);
        }

        private void OpenRecipeInspect(int bookIndex)
        {
            _recipeBookPage?.OpenInspect(bookIndex);
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
                && _axisBinder.BeginActiveItemTargeting(_run, item, targets, onConfirm, onCancel);
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
            if (_tableCoordinator == null)
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

        /// <summary>购买碎片包后进入餐桌编辑态（世界空间）：隐藏商店/行动轴，露出餐桌手动拼贴。</summary>
        private void OpenTableEdit(Action onShown = null)
        {
            if (_run == null || _run.PendingFragmentPack.Count == 0)
            {
                onShown?.Invoke();
                return;
            }

            OpenTableFragmentChoice(_run.PendingFragmentPack, OnTableEditDone, onShown);
        }

        public void OpenRewardTableEdit(Action<bool> onDone)
        {
            OpenRewardTableEdit(_run != null ? _run.PendingFragmentPack : null, onDone);
        }

        public void OpenRewardTableEdit(IReadOnlyList<string> candidateIds, Action<bool> onDone)
        {
            if (_run == null || candidateIds == null || candidateIds.Count == 0)
            {
                onDone?.Invoke(false);
                return;
            }

            _afterRewardTableEdit = onDone;
            OpenTableFragmentChoice(candidateIds, OnRewardTableEditDone);
        }

        private void OpenTableFragmentChoice(
            IReadOnlyList<string> candidateIds,
            Action<bool> completed,
            Action onShown = null)
        {
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if (world == null || _run == null || candidateIds == null || candidateIds.Count == 0)
            {
                completed?.Invoke(false);
                onShown?.Invoke();
                return;
            }

            var request = new TableFragmentChoiceRequest(
                _run,
                candidateIds,
                completed,
                OpenTableFragmentPlacementConfirmation);
            SwitchTo(
                GameplayView.TableEdit,
                () => world.BeginTableFragmentChoice(request),
                onShown);
        }

        public bool OpenRewardDishPack(
            IReadOnlyList<RewardChoice> choices,
            Func<int, bool> onChoiceSelected,
            Action onSkip)
        {
            return _rewardPage != null && _rewardPage.OpenRewardDishPack(choices, onChoiceSelected, onSkip);
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
            string title,
            IReadOnlyList<RewardChoice> choices,
            cfg.ItemKind kind,
            Action<int> onPick,
            Action onSkip)
        {
            return _rewardPage != null && _rewardPage.OpenRewardItemChoices(title, choices, kind, onPick, onSkip);
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
                resetDoodle: false);
            _world.SetDishHoverCallbacks(OnDishHoverEntered, OnDishHoverExited);
            _world.SetCellHoverCallbacks(OnCellHoverEntered, OnCellHoverExited);
            _world.SetTableFragmentHoverCallbacks(OnTableFragmentHoverEntered, OnTableFragmentHoverExited);
            RefreshAll();
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
            (_world ?? BattleWorldController.Instance)?.HideWorld();
            SwitchTo(GameplayView.Shop);
        }

        private void OnRewardTableEditDone(bool placed)
        {
            (_world ?? BattleWorldController.Instance)?.HideWorld();
            Action<bool> cb = _afterRewardTableEdit;
            _afterRewardTableEdit = null;
            SwitchTo(GameplayView.None, onShown: () => cb?.Invoke(placed));
        }

        private void OpenTableFragmentPlacementConfirmation(TableFragmentPlacementConfirmationRequest request)
        {
            if (request == null)
            {
                return;
            }

            HideFoodTips();
            var data = new ConfirmDialogData
            {
                Title = "确认拼接",
                Message = "确定要将这块餐桌碎片拼到当前餐桌上吗？",
                ConfirmText = "确认拼接",
                CancelText = "取消",
                OnConfirm = request.Confirm,
                OnCancel = request.Cancel,
            };
            GameApp.UI.OpenUIForm(UIForms.ConfirmDialog, UIForms.GroupDialog, data);
        }

        private void OnTableEditSkipClicked()
        {
            if (_rewardPeekOnly)
            {
                return;
            }

            (_world ?? BattleWorldController.Instance)?.SkipTableEditPack();
        }

        private void SetActionAxisVisible(bool visible)
        {
            if (_actionAxisBar != null)
            {
                _actionAxisBar.gameObject.SetActive(visible);
            }
        }

        private void SetFoodActionsVisible(bool visible)
        {
            _foodBar?.SetVisible(visible);
            ServingOutletView servingOutlet = ResolveServingOutlet();
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            servingOutlet?.ConfigureWorldSpace(world != null ? world.WorldCamera : Camera.main);
            servingOutlet?.SetVisible(visible);
            FoodDiscardBinView discardBin = ResolveFoodDiscardBin();
            discardBin?.ConfigureWorldSpace(world != null ? world.WorldCamera : Camera.main);
            discardBin?.Bind(_session);
            discardBin?.SetVisible(visible);
        }

        /// <summary>初始化战斗态的出餐口、弃置区与世界拖拽回调。</summary>
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
                ServeFromOutlet,
                () => OpenRecipeInspect(0),
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

        private void ServeFromOutlet()
        {
            if (_rewardPeekOnly)
            {
                return;
            }

            if (_world == null || _session == null || _session.IsSettled)
            {
                return;
            }

            _world.TryPrepareServeDish(0);
            RefreshAll();
        }

        /// <summary>刷新常驻信息：左栏周/金币/分数、右栏道具。</summary>
        private void RefreshPersistent(bool refreshItems = true)
        {
            if (_run == null)
            {
                return;
            }

            BattleWorldController world = _world ?? BattleWorldController.Instance;
            _infoColumn?.Refresh(_run, _session, _current, world, _currentBossDebuff);
            RefreshCakeLayerBuff();

            if (refreshItems)
            {
                RefreshItems();
            }

            RefreshFoodActions();
        }

        internal void RefreshPersistentHud()
        {
            RefreshPersistent();
        }

        private void RefreshCakeLayerBuff()
        {
            if (_cakeLayerBuffHud == null)
            {
                return;
            }

            if (_session == null)
            {
                _cakeLayerBuffHud.Hide();
            }
            else
            {
                _cakeLayerBuffHud.Bind(
                    _displayedCakeLayers,
                    _session.Database?.CakeLayerBuffs,
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

        /// <summary>右栏道具：被动网格（2 列）+ 固定主动道具槽，每份主动实例占一格。</summary>
        private void RefreshItems()
        {
            bool activeItemsInteractable =
                _activeItemUse == null
                || !_activeItemUse.IsRecipePanelTargeting;
            _itemsColumn?.Refresh(
                _run,
                _session,
                _inBattle,
                activeItemsInteractable,
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
            _axisBinder?.Rebuild(_run);
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
                Debug.LogError($"{nameof(BattleForm)} 缺少商店道具飞行动画 prefab。", this);
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

        /// <summary>行动轴节点单卡：用于商店等节点，点击卡片后才执行节点效果。</summary>
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
                if (_actionSelectionPanel != null)
                {
                    _actionSelectionPanel.SetActive(false);
                }

                _loop?.OnActionPicked(choice);
            });
        }

        /// <summary>玩家点击行动轴节点卡片。</summary>
        private void OnTimelineNodePicked(Action onPick)
        {
            ClearTimelineNodeCard();
            _deck?.HideThenDestroy(() =>
            {
                if (_actionSelectionPanel != null)
                {
                    _actionSelectionPanel.SetActive(false);
                }

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
            ClearTimelineNodeCard();
            _pageRouter?.HideHud();
            SyncPageStateFromRouter();
            _infoColumn?.ScoreFire?.Hide();
            _infoColumn?.ResetTableLabel();
            SetMessage(string.Empty);
        }

        private void OnSettingsClicked()
        {
            if (_rewardPeekOnly)
            {
                return;
            }

            GameApp.UI.OpenUIForm(UIForms.Settings, UIForms.GroupDialog, new SettingsFormData(inGameplay: true));
        }

        private void OnViewRecipeClicked()
        {
            if (_rewardPeekOnly || _run == null || _current == GameplayView.None)
            {
                return;
            }

            if (IsViewToggleTransitioning())
            {
                return;
            }

            if (_current == GameplayView.TableView && _tableCoordinator != null)
            {
                _tableCoordinator.Back(() => OpenRecipeInspect(0));
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
                lines.Add($"菜谱{entry.BookIndex + 1}-{entry.DishIndex + 1}: {before}  ->  {after}");
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
            return $"行动轴已变化：{result.Before.Count} 个节点 -> {result.After.Count} 个节点";
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
            if (_rewardPeekOnly)
            {
                return;
            }

            if (_tableCoordinator == null)
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
                _tableCoordinator.Back();
                return;
            }

            if (_current == GameplayView.RecipeInspect && _recipeBookPage != null)
            {
                _recipeBookPage.CloseInspect(_tableCoordinator.Open);
                return;
            }

            _tableCoordinator.Open();
        }

        private bool IsViewToggleTransitioning()
        {
            return (_center != null && DOTween.IsTweening(_center, true))
                || (_tableCoordinator != null && _tableCoordinator.IsTransitioning);
        }

        public void ShowRunResult(bool win, int total)
        {
            _world?.HideWorld();
            HideHud();
            if (win)
            {
                GameApp.UI.OpenUIForm(UIForms.Result, UIForms.GroupDialog, new ResultFormData(true, total));
            }
            else
            {
                GameApp.UI.OpenUIForm(UIForms.Defeat, UIForms.GroupDialog, new DefeatFormData(total));
            }
        }

        // —— 战斗 ——

        public void StartBattle(int requiredScore, string modifier, string key, ActionExecutionContext actionContext)
        {
            HideResultPanel();
            _infoColumn?.SetBattleScoreOverride(null);
            _infoColumn?.ScoreFire?.Hide();
            _activeBattleRawRequiredScore = requiredScore;
            _activeBattleModifier = modifier ?? string.Empty;
            _activeBattleKey = key ?? string.Empty;
            _activeBattleIsBoss = IsBossFoodAction(actionContext);
            _currentBossDebuff = _activeBattleIsBoss ? ResolveBossDebuff(modifier) : null;
            SetMessage(string.Empty);
            UnsubscribeCakeLayerChanges();
            _session = _run.BuildBattleSession(requiredScore, modifier, key);
            _displayedCakeLayers = _session.HappyCakeLayers;
            _pendingSettlementCakeLayers = null;
            _session.Served += OnBattleServed;
            _session.HappyCakeLayersChanged += OnHappyCakeLayersChanged;
            // 常驻壳在战斗中持续显示并接管分数/道具入口（餐桌/菜品仍在世界空间场景）。
            SwitchTo(GameplayView.Food);

            _world = BattleWorldController.Instance;
            if (_world == null)
            {
                Log.Error("BattleForm: battle scene controller not found (scene not loaded?).", Tag);
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
            RefreshAll();
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

        private cfg.BossDebuff ResolveBossDebuff(string modifier)
        {
            if (string.IsNullOrEmpty(modifier))
            {
                return null;
            }

            cfg.Tables tables = _run?.Tables ?? GameApp.Config?.Tables;
            if (tables?.TbBossDebuff == null)
            {
                return null;
            }

            foreach (cfg.BossDebuff debuff in tables.TbBossDebuff.DataList)
            {
                if (debuff != null && debuff.Modifier == modifier)
                {
                    return debuff;
                }
            }

            return null;
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
                tips.Bind(FoodTipsDataFactory.BuildRevealed(piece.Instance, _session.DiningTable, _session.Database, reveal));
            }
            else
            {
                ScoreResult preview = _session.IsSettled ? _session.LastResult : null;
                tips.Bind(piece.Instance, _session.DiningTable, _session.Database, preview);
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
                && _current != GameplayView.TableEdit)
            {
                return;
            }

            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if (_current == GameplayView.TableEdit && world != null && world.IsTableEditDragging)
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
                table = _run.BuildTablePreviewFromFragments(_run.WeekModifier);
                db = _run.Database;
                return table != null && db != null;
            }

            if (_current == GameplayView.TableEdit && _run != null)
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
            if (_current != GameplayView.TableEdit
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

        private void OnEatClicked()
        {
            if (_rewardPeekOnly)
            {
                return;
            }

            if (_session == null || _session.IsSettled || _session.PreparedServe != null)
            {
                return;
            }

            if (_session.DiningTable.DishCount == 0)
            {
                SetMessage("餐桌还是空的，先上几道菜吧。");
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

                // 银材质命中：发放主动道具（掷骰已在 BattleSession 正式结算时完成，这里只落地选取具体道具）。
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

            _infoColumn?.SetBattleScoreOverride(null);
            RefreshAll();

            // Food 结算完成后本局层数必须归零；先保留最终值，供层数金币、跨局保留等结算读取。
            BattleSession settledSession = _session;
            int finalHappyCakeLayers = settledSession?.HappyCakeLayers ?? 0;
            bool isWin = settledSession != null && settledSession.IsWin;
            settledSession?.ClearHappyCakeLayers();
            _loop?.OnBattleSettled(result, isWin, finalHappyCakeLayers);
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

        // —— 局内交互（透传到战斗世界）——

        private void OnDoodleClearClicked()
        {
            if (_rewardPeekOnly)
            {
                return;
            }

            (_world ?? BattleWorldController.Instance)?.ClearDoodle();
            RefreshFoodActions();
        }

        private void OnDoodleToggleClicked()
        {
            if (_rewardPeekOnly)
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
            if (_rewardPeekOnly)
            {
                return;
            }

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
            if (_messageText == null)
            {
                return;
            }

            string text = message ?? string.Empty;
            _messageText.text = text;
            _messageText.gameObject.SetActive(!string.IsNullOrEmpty(text));
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
