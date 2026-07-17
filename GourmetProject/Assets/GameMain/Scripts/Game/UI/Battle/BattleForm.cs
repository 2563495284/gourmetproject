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
using GourmetProject.Game.UI.Battle.States;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Game.Meta.Passives;
using UnityEngine.Serialization;

namespace GourmetProject.Game.UI.Battle
{
    /// <summary>
    /// 局外周循环编排枢纽 + 局内战斗结果壳。
    /// 常驻壳（左列信息 / 右列道具 / 顶部行动轴 / 底部扇形菜谱）进入玩法后全程常驻，只有中部内容区在五态间切换：
    /// 行动选择(含事件 n 选一) / 商店 / 编辑菜谱 / 美食战斗 / 餐桌编辑。切换只对中部内容区做 DOTween 渐隐渐显
    /// （<see cref="UITransition.FadeSwap"/>），常驻壳不参与动画；美食 / 餐桌态在同一 Battle 场景内透出世界空间表现。
    /// </summary>
    public sealed class BattleForm : UGuiForm, IWeekLoopView, IBattleViewHost, ITableViewHost
    {
        private const string Tag = "Battle";
        private const float ShopItemFlyDuration = 0.42f;

        /// <summary>当前打开的战斗界面，供各弹窗回调推进周循环。</summary>
        public static BattleForm Active { get; private set; }

        [Header("HUD Frame")]
        [SerializeField] private GameObject _hudFrame;
        [SerializeField] private GameObject _backdrop;

        [Header("Center (切换动画区)")]
        [Tooltip("中部内容区根的 CanvasGroup：五态切换时对它做渐隐渐显；常驻壳不在其下。")]
        [SerializeField] private CanvasGroup _center;
        [SerializeField] private Text _centerTitleText;

        [Header("DiningTable Area (餐桌锁定区)")]
        [Tooltip("HUD 里的空区矩形：世界餐桌将 fit 并居中锁定在该屏幕区域内；同时作为食物调整遮黑的挖洞区。")]
        [SerializeField] private RectTransform _boardArea;

        [Header("Left Column")]
        [SerializeField] private BattleInfoColumn _infoColumn;

        [Header("Action Axis")]
        [SerializeField] private ActionAxisBar _actionAxisBar;

        [Header("Hover Tips")]
        [SerializeField] private BattleTipRegistry _tips;

        [Header("Action Selection (center)")]
        [SerializeField] private GameObject _actionSelectionPanel;
        [SerializeField] private ActionCardDeck _deck;

        [Header("Shop (center)")]
        [SerializeField] private ShopForm _shopPanel;

        [Header("Recipe Workspace (center)")]
        [FormerlySerializedAs("_recipeEditPanel")]
        [SerializeField] private RecipeWorkspacePanel _recipeWorkspacePanel;

        [Header("Reward Dish Pack (center)")]
        [SerializeField] private RewardDishPackPanel _rewardDishPackPanel;
        [SerializeField] private RewardItemChoicePanel _rewardItemChoicePanelPrefab;
        private RewardItemChoicePanel _rewardItemChoicePanel;
        [SerializeField] private RandomizedItemsPanel _randomizedItemsPanel;

        [Header("Event Page (center)")]
        [SerializeField] private EventPagePanel _eventPanel;

        [Header("DiningTable Edit")]
        [SerializeField] private Button _boardEditSkipButton;

        [Header("Right Column - Items")]
        [SerializeField] private BattleItemsColumn _itemsColumn;

        [Header("Active Items")]
        [SerializeField] private ActiveItemActionPopup _activeItemPopupPrefab;
        [SerializeField] private TargetArrowView _activeItemTargetArrowPrefab;
        [SerializeField] private ActiveItemTargetOverlayView _activeItemTargetOverlayPrefab;
        [SerializeField] private GameObject _shopItemFlyFxPrefab;

        [Header("Recipe View")]
        [SerializeField] private RecipeView _recipeView;

        [Header("Food Actions")]
        [SerializeField] private BattleFoodActionBar _foodBar;
        [SerializeField] private View.FoodAdjustOverlay _foodAdjustOverlayPrefab;

        [Header("Battle Message")]
        [SerializeField] private Text _messageText;

        private bool _inBattle;
        private GameplayView _current = GameplayView.None;
        private Action<bool> _afterRewardTableEdit;

        private GameRun _run;
        private BattleSession _session;
        private BattleWorldController _world;

        private WeekLoopController _loop;

        private RecipeBooksPresenter _recipePresenter;
        private TimelineAxisBinder _axisBinder;
        private TableViewCoordinator _tableCoordinator;
        private GameplayViewStateMachine _viewStates;
        private ActiveItemUseCoordinator _activeItemUse;
        private int _shopItemFlyInFlight;
        private ItemDefinition _activeItemRecipeTargetItem;
        private GameplayView _activeItemRecipeReturnView = GameplayView.None;
        private Action _activeItemRecipeTargetCancel;
        private Action<ActiveTarget> _activeItemRecipeTargetConfirmed;
        private string _eventRecipeDeleteTitle;
        private Action _eventRecipeDeleteCancel;
        private Action<ActiveTarget> _eventRecipeDeleteConfirmed;
        private Action _eventRecipeDeleteChanged;
        private View.FoodAdjustOverlay _foodAdjustOverlay;
        private DishPieceView _hoveredDishPiece;
        private DiningTableCellView _hoveredCell;
        private SettlementRevealState _settlementReveal;
        [SerializeField] private GameObject _passiveOverlayRoot;
        [SerializeField] private Text _passiveOverlayText;
        private Sequence _passiveOverlaySeq;

        public GameRun Run => _run;
        public BattleSession Session => _session;
        public ActionExecutionContext CurrentBattleActionContext => _loop?.CurrentBattleActionContext;
        internal GameRun ActiveRun => _run;
        internal BattleSession ActiveSession => _session;
        internal WeekLoopController ActiveLoop => _loop;
        internal GameplayView CurrentView => _current;
        internal bool InBattle => _inBattle;
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

            _infoColumn?.Bind(OnSettingsClicked, OnViewTableClicked, OnFoodAdjustClicked);
            _foodBar?.Bind(OnEatClicked, OnDoodleClearClicked, OnDoodleToggleClicked);

            if (_boardEditSkipButton != null)
            {
                _boardEditSkipButton.onClick.AddListener(OnTableEditSkipClicked);
            }

            _recipePresenter = new RecipeBooksPresenter(_recipeView);
            _axisBinder = new TimelineAxisBinder(
                _actionAxisBar,
                () => _tips != null ? _tips.Shop : null,
                () => _tips != null ? _tips.Interest : null,
                () => _tips != null ? _tips.Boss : null);
            _tableCoordinator = new TableViewCoordinator(this);
            _viewStates = new GameplayViewStateMachine(this);
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

            _loop = null;
            _activeItemUse?.Dispose();
            _deck?.KillAllTweens();
            ClearWorldHoverCallbacks();
            HideAllTips();
            if (_world != null)
            {
                _world.HideWorld();
            }

            base.OnClose(isShutdown, userData);
        }

        private void Update()
        {
            _activeItemUse?.Update();
        }

        // —— 周循环编排（代理到 WeekLoopController）——

        public int LastBattleTotal => _session?.LastResult?.Total ?? 0;

        /// <summary>进入（或继续）一周：随机/沿用行动轴后开始行动循环。</summary>
        public void BeginWeek()
        {
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

        public void HideBattleWorld()
        {
            _run?.EndFoodActionAdjustments();
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            world?.HideWorld();
            world?.ClearBattleTable();
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
            SwitchTo(GameplayView.ActionSelect, () =>
            {
                SetCenterTitle("行动轴事件");
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
        /// 切到某一中部态：只对中部内容区 <see cref="_center"/> 做 DOTween 渐隐渐显，常驻壳（左/右/行动轴/菜谱框）不动。
        /// 内容交换（隐藏旧面板 + 启用新面板 + 重建）集中在淡出完成后的 <see cref="GameplayViewStateMachine.Apply"/> 里执行。
        /// </summary>
        private void SwitchTo(GameplayView next, Action buildCenter = null, Action onShown = null)
        {
            if (_run == null)
            {
                return;
            }

            _deck?.KillPendingShow();
            _current = next;
            _inBattle = next == GameplayView.Food;
            UITransition.FadeSwap(_center, () => _viewStates.Apply(next, buildCenter), onDone: onShown);
        }

        /// <summary>
        /// 淡出完成后落地某一态的「常驻壳通用配置」：中部面板显隐 + 行动轴/白底/食物按钮显隐 + 菜谱抽屉态 + 刷新常驻信息。
        /// 各态的特化构建（重建行动轴/菜谱条/打开子面板/中部内容）由对应 <see cref="IGameplayViewState"/> 承担。
        /// </summary>
        private void ApplyShellForView(GameplayView view)
        {
            if (_run == null)
            {
                return;
            }

            // 离开美食态时确保退出食物调整（含遮罩），避免残留到其它态。
            if (view != GameplayView.Food)
            {
                HideFoodTips();
            }

            if (view != GameplayView.Food && _foodAdjustOverlay != null)
            {
                BattleWorldController world = _world ?? BattleWorldController.Instance;
                if (world != null && world.IsFoodAdjusting)
                {
                    world.EndFoodAdjust();
                }

                ExitFoodAdjustUI();
            }

            if (_hudFrame != null)
            {
                _hudFrame.SetActive(true);
            }

            bool actionSel = view == GameplayView.ActionSelect;
            bool shop = view == GameplayView.Shop;
            bool recipeEdit = view == GameplayView.RecipeEdit;
            bool rewardDishPack = view == GameplayView.RewardDishPack;
            bool rewardItemChoice = view == GameplayView.RewardItemChoice;
            bool randomizedItems = view == GameplayView.RandomizedItems;
            bool eventPage = view == GameplayView.Event;
            bool worldView = view == GameplayView.Food || view == GameplayView.TableEdit || view == GameplayView.TableView;

            if (_actionSelectionPanel != null)
            {
                _actionSelectionPanel.SetActive(actionSel);
            }

            if (_shopPanel != null)
            {
                _shopPanel.gameObject.SetActive(shop);
            }

            if (_recipeWorkspacePanel != null)
            {
                _recipeWorkspacePanel.gameObject.SetActive(recipeEdit);
            }

            if (_rewardDishPackPanel != null)
            {
                _rewardDishPackPanel.gameObject.SetActive(rewardDishPack);
            }

            if (_rewardItemChoicePanel != null)
            {
                _rewardItemChoicePanel.gameObject.SetActive(rewardItemChoice);
            }

            if (_randomizedItemsPanel != null)
            {
                _randomizedItemsPanel.gameObject.SetActive(randomizedItems);
            }

            if (_eventPanel != null)
            {
                _eventPanel.gameObject.SetActive(eventPage);
            }

            if (_boardEditSkipButton != null)
            {
                _boardEditSkipButton.gameObject.SetActive(view == GameplayView.TableEdit);
            }

            // 行动轴：行动选择 / 商店 / 事件页常驻显示；编辑菜谱 / 美食 / 餐桌态隐藏。
            SetActionAxisVisible(actionSel || shop || eventPage);
            SetFoodActionsVisible(view == GameplayView.Food);

            // 白底：世界态（美食 / 餐桌）关闭，让 Battle 场景世界空间透出；其余态开启。
            if (_backdrop != null)
            {
                _backdrop.SetActive(!worldView);
            }

            if (_recipeView != null)
            {
                RecipeView.RecipeState recipeState = view switch
                {
                    GameplayView.ActionSelect => RecipeView.RecipeState.Collapsed,
                    GameplayView.Shop => RecipeView.RecipeState.Shown,
                    GameplayView.RewardDishPack => RecipeView.RecipeState.Hidden,
                    GameplayView.RewardItemChoice => RecipeView.RecipeState.Hidden,
                    GameplayView.RandomizedItems => RecipeView.RecipeState.Hidden,
                    GameplayView.Event => RecipeView.RecipeState.Collapsed,
                    GameplayView.Food => RecipeView.RecipeState.Shown,
                    GameplayView.TableEdit => RecipeView.RecipeState.Collapsed,
                    _ => RecipeView.RecipeState.Hidden,
                };
                _recipeView.SetState(recipeState);
            }

            RefreshPersistent();
        }

        /// <summary>商店态：打开商店四区面板并接线各回调（离开/刷新/编辑菜谱/餐桌编辑）。</summary>
        private void OpenShopPanel()
        {
            if (_shopPanel != null)
            {
                _shopPanel.Open(OnShopLeave, RefreshShopPersistent, OpenRecipeEdit, () => OpenTableEdit(), _recipeView, PlayShopItemPurchaseFly);
            }
        }

        /// <summary>菜谱工作区：打开编辑菜谱或主动道具选菜流程。</summary>
        private void OpenRecipeWorkspacePanel()
        {
            if (_recipeWorkspacePanel != null)
            {
                if (_activeItemRecipeTargetItem != null)
                {
                    SetCenterTitle(_activeItemRecipeTargetItem.Desc);
                    _recipeWorkspacePanel.OpenForActiveRecipeDish(
                        _run,
                        _activeItemRecipeTargetItem,
                        CancelActiveItemRecipeTarget,
                        ConfirmActiveItemRecipeTarget,
                        RefreshShopPersistent,
                        () => _tips != null ? _tips.Food : null);
                    return;
                }

                if (_eventRecipeDeleteConfirmed != null)
                {
                    SetCenterTitle(string.IsNullOrWhiteSpace(_eventRecipeDeleteTitle) ? "选择要删除的菜品" : _eventRecipeDeleteTitle);
                    _recipeWorkspacePanel.OpenForEventRecipeDishDelete(
                        _run,
                        _eventRecipeDeleteTitle,
                        CancelEventRecipeDelete,
                        ConfirmEventRecipeDelete,
                        _eventRecipeDeleteChanged ?? RefreshShopPersistent,
                        () => _tips != null ? _tips.Food : null);
                    return;
                }

                _recipeWorkspacePanel.Open(_run, OpenShopFromEdit, RefreshShopPersistent, () => _tips != null ? _tips.Food : null);
            }
        }

        // —— IBattleViewHost（供状态机/各态回调壳，转发到壳内私有实现）——

        GameRun IBattleViewHost.Run => _run;
        RecipeBooksPresenter IBattleViewHost.Recipe => _recipePresenter;
        void IBattleViewHost.ApplyShellForView(GameplayView view) => ApplyShellForView(view);
        void IBattleViewHost.SetCenterTitle(string text) => SetCenterTitle(text);
        void IBattleViewHost.RebuildActionAxis() => RebuildActionAxis();
        void IBattleViewHost.OpenShopPanel() => OpenShopPanel();
        void IBattleViewHost.OpenRecipeWorkspacePanel() => OpenRecipeWorkspacePanel();
        void IBattleViewHost.BuildBattleRecipe() => BuildBattleRecipe();
        void IBattleViewHost.BuyRecipeBook() => BuyRecipeBook();

        // —— ITableViewHost（供查看餐桌编排回调壳）——

        GameplayView ITableViewHost.CurrentView => _current;
        GameRun ITableViewHost.Run => _run;
        BattleWorldController ITableViewHost.World => _world ?? BattleWorldController.Instance;
        void ITableViewHost.SwitchTo(GameplayView view, Action buildCenter, Action onShown) => SwitchTo(view, buildCenter, onShown);
        void ITableViewHost.RestoreBattleWorld() => RestoreBattleWorld();
        void ITableViewHost.BindWorldHoverCallbacks() => BindWorldHoverCallbacks();
        void ITableViewHost.PlayShowCardsWhenReady() => PlayShowCardsWhenReady();
        void ITableViewHost.OpenTableEdit(Action onShown) => OpenTableEdit(onShown);
        ActionSelectSnapshot ITableViewHost.CaptureActionSelectSnapshot() => CaptureActionSelectSnapshot();
        void ITableViewHost.RestoreActionSelection(ActionSelectSnapshot snapshot) => RestoreActionSelection(snapshot);

        // —— 行动选择态（含事件 n 选一，共用中部卡片）——

        private void ShowActionSelection()
        {
            SwitchTo(GameplayView.ActionSelect, () =>
            {
                SetCenterTitle("选择行动");
                BuildActionCards();
            }, PlayShowCardsWhenReady);
        }

        /// <summary>事件页：专用中部面板，展示事件环境、正文、结果、后续选项与结束按钮。</summary>
        public void ShowEventPage(
            string title,
            string desc,
            string result,
            string bgSprite,
            IReadOnlyList<string> options,
            IReadOnlyList<bool> optionEnabled,
            bool showEndButton,
            string endButtonText,
            Action<int> onPick,
            Action onEnd)
        {
            SwitchTo(GameplayView.Event, () =>
            {
                SetCenterTitle(string.Empty);
                if (_eventPanel != null)
                {
                    _eventPanel.Open(title, desc, result, bgSprite, options, optionEnabled, showEndButton, endButtonText, onPick, onEnd);
                }
                else if (showEndButton)
                {
                    onEnd?.Invoke();
                }
                else
                {
                    onPick?.Invoke(0);
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
            _eventRecipeDeleteTitle = title;
            _eventRecipeDeleteCancel = onCancel;
            _eventRecipeDeleteConfirmed = onTargetConfirmed;
            _eventRecipeDeleteChanged = onChanged;
            SwitchTo(GameplayView.RecipeEdit);
        }

        private void SetCenterTitle(string text)
        {
            if (_centerTitleText != null)
            {
                _centerTitleText.text = text ?? string.Empty;
            }
        }

        // —— 商店 / 编辑菜谱 / 餐桌编辑 入口 ——

        private void OnShopLeave()
        {
            if (_shopPanel != null)
            {
                _shopPanel.gameObject.SetActive(false);
            }

            OnShopClosed();
        }

        private void OpenRecipeEdit()
        {
            ClearActiveItemRecipeTargetRequest();
            ClearEventRecipeDeleteRequest();
            SwitchTo(GameplayView.RecipeEdit);
        }

        private void OpenShopFromEdit()
        {
            SwitchTo(GameplayView.Shop);
        }

        internal void OpenActiveItemRecipeTarget(
            ItemDefinition item,
            Action onCancel,
            Action<ActiveTarget> onTargetConfirmed,
            Action onOpened = null)
        {
            if (item == null)
            {
                onOpened?.Invoke();
                return;
            }

            _activeItemRecipeTargetItem = item;
            _activeItemRecipeReturnView = _current;
            _activeItemRecipeTargetCancel = onCancel;
            _activeItemRecipeTargetConfirmed = onTargetConfirmed;
            SwitchTo(GameplayView.RecipeEdit, onShown: onOpened);
        }

        internal bool TryPointerActiveItemRecipeTarget(Vector2 screenPoint, out ActiveTarget target)
        {
            target = default;
            return _current == GameplayView.RecipeEdit
                && _activeItemRecipeTargetItem != null
                && _recipeWorkspacePanel != null
                && _recipeWorkspacePanel.TryPointerRecipeDishTarget(screenPoint, out target);
        }

        internal void CancelActiveItemRecipeTarget()
        {
            if (_activeItemRecipeTargetItem == null)
            {
                return;
            }

            GameplayView returnView = _activeItemRecipeReturnView;
            Action onCancel = _activeItemRecipeTargetCancel;
            ClearActiveItemRecipeTargetRequest();
            onCancel?.Invoke();
            RestoreActiveItemRecipeReturnView(returnView);
        }

        internal void ConfirmActiveItemRecipeTarget(ActiveTarget target)
        {
            if (_activeItemRecipeTargetItem == null)
            {
                return;
            }

            GameplayView returnView = _activeItemRecipeReturnView;
            Action<ActiveTarget> onConfirmed = _activeItemRecipeTargetConfirmed;
            ClearActiveItemRecipeTargetRequest();
            onConfirmed?.Invoke(target);
            RestoreActiveItemRecipeReturnView(returnView);
        }

        private void ClearActiveItemRecipeTargetRequest()
        {
            _activeItemRecipeTargetItem = null;
            _activeItemRecipeReturnView = GameplayView.None;
            _activeItemRecipeTargetCancel = null;
            _activeItemRecipeTargetConfirmed = null;
        }

        private void RestoreActiveItemRecipeReturnView(GameplayView returnView)
        {
            switch (returnView)
            {
                case GameplayView.ActionSelect:
                case GameplayView.Shop:
                case GameplayView.Food:
                case GameplayView.TableEdit:
                case GameplayView.TableView:
                    SwitchTo(returnView);
                    break;
                default:
                    SwitchTo(GameplayView.Shop);
                    break;
            }
        }

        private void CancelEventRecipeDelete()
        {
            Action onCancel = _eventRecipeDeleteCancel;
            ClearEventRecipeDeleteRequest();
            onCancel?.Invoke();
        }

        private void ConfirmEventRecipeDelete(ActiveTarget target)
        {
            Action<ActiveTarget> onConfirmed = _eventRecipeDeleteConfirmed;
            ClearEventRecipeDeleteRequest();
            onConfirmed?.Invoke(target);
        }

        private void ClearEventRecipeDeleteRequest()
        {
            _eventRecipeDeleteTitle = null;
            _eventRecipeDeleteCancel = null;
            _eventRecipeDeleteConfirmed = null;
            _eventRecipeDeleteChanged = null;
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
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if (world == null || _run == null || _run.PendingFragmentPack.Count == 0)
            {
                onShown?.Invoke();
                return;
            }

            SwitchTo(
                GameplayView.TableEdit,
                () => world.BeginTableEdit(_run, _run.PendingFragmentPack, OnTableEditDone),
                onShown);
        }

        public void OpenRewardTableEdit(Action<bool> onDone)
        {
            OpenRewardTableEdit(_run != null ? _run.PendingFragmentPack : null, onDone);
        }

        public void OpenRewardTableEdit(IReadOnlyList<string> candidateIds, Action<bool> onDone)
        {
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if (world == null || _run == null || candidateIds == null || candidateIds.Count == 0)
            {
                onDone?.Invoke(false);
                return;
            }

            _afterRewardTableEdit = onDone;
            SwitchTo(GameplayView.TableEdit, () => world.BeginTableEdit(_run, candidateIds, OnRewardTableEditDone));
        }

        public bool OpenRewardDishPack(
            IReadOnlyList<RewardChoice> choices,
            Func<int, int, bool> onChoiceDropped,
            Action onSkip)
        {
            if (_run == null || _rewardDishPackPanel == null)
            {
                Log.Error("BattleForm: reward dish pack panel is not configured.", Tag);
                return false;
            }

            SwitchTo(GameplayView.RewardDishPack, () =>
            {
                SetCenterTitle("菜品包");
                _rewardDishPackPanel.Open(_run, choices, _recipeView, onChoiceDropped, onSkip, () => _tips != null ? _tips.Food : null);
            });
            return true;
        }

        public bool OpenAcquireDishPack(string title, IReadOnlyList<RewardChoice> choices)
        {
            if (_run == null || _rewardDishPackPanel == null || choices == null || choices.Count == 0)
            {
                return false;
            }

            GameplayView previous = _current;
            SwitchTo(GameplayView.RewardDishPack, () =>
            {
                SetCenterTitle(string.IsNullOrWhiteSpace(title) ? "菜品包" : title);
                _rewardDishPackPanel.Open(
                    _run,
                    choices,
                    _recipeView,
                    (choiceIndex, bookIndex) =>
                    {
                        if (choiceIndex < 0 || choiceIndex >= choices.Count)
                        {
                            return false;
                        }

                        if (!RewardGranter.ApplyDishChoiceToBook(_run, choices[choiceIndex], bookIndex))
                        {
                            return false;
                        }

                        RunPersistence.Save(_run);
                        RestoreAfterAcquireView(previous);
                        return true;
                    },
                    () => RestoreAfterAcquireView(previous),
                    () => _tips != null ? _tips.Food : null);
            });
            return true;
        }

        public bool OpenRewardItemChoices(string title, IReadOnlyList<RewardChoice> choices, cfg.ItemKind kind)
        {
            return OpenRewardItemChoices(
                title,
                choices,
                kind,
                index =>
                {
                    if (index >= 0 && index < choices.Count)
                    {
                        RewardGranter.ApplyChoice(_run, choices[index]);
                        RunPersistence.Save(_run);
                    }
                },
                null);
        }

        public bool OpenRewardItemChoices(
            string title,
            IReadOnlyList<RewardChoice> choices,
            cfg.ItemKind kind,
            Action<int> onPick,
            Action onSkip)
        {
            if (_run == null || choices == null || choices.Count == 0)
            {
                return false;
            }

            EnsureRewardItemChoicePanel();
            if (_rewardItemChoicePanel == null)
            {
                return false;
            }

            GameplayView previous = _current;
            SwitchTo(GameplayView.RewardItemChoice, () =>
            {
                SetCenterTitle(string.Empty);
                _rewardItemChoicePanel.Open(
                    string.IsNullOrWhiteSpace(title) ? "选择一个道具" : title,
                    choices,
                    kind,
                    index =>
                    {
                        _rewardItemChoicePanel.Close();
                        RestoreAfterAcquireView(previous);
                        onPick?.Invoke(index);
                    },
                    () =>
                    {
                        _rewardItemChoicePanel.Close();
                        RestoreAfterAcquireView(previous);
                        onSkip?.Invoke();
                    },
                    _run,
                    _tips != null ? _tips.Item : null);
            });
            return true;
        }

        public bool OpenRandomizedItemsPanel(string title, IReadOnlyList<RandomizedItemResult> results)
        {
            if (_run == null || results == null)
            {
                return false;
            }

            EnsureRandomizedItemsPanel();
            if (_randomizedItemsPanel == null)
            {
                return false;
            }

            GameplayView previous = _current;
            SwitchTo(GameplayView.RandomizedItems, () =>
            {
                SetCenterTitle(string.Empty);
                _randomizedItemsPanel.Open(
                    string.IsNullOrWhiteSpace(title) ? "随机后的道具" : title,
                    results,
                    () =>
                    {
                        PlayRandomizedItemFlys(results);
                        _randomizedItemsPanel.Close();
                        RestoreAfterAcquireView(previous);
                    });
            });
            return true;
        }

        public bool TryGrantRecipeBookFromPassive(string sourceName)
        {
            if (_run == null)
            {
                return false;
            }

            if (_recipeView == null || _recipeView.State != RecipeView.RecipeState.Shown)
            {
                ShowNotice(sourceName, "当前菜谱栏没有展开，菜谱券使用失败。", null);
                return false;
            }

            if (!_run.AddRecipeBook())
            {
                ShowNotice(sourceName, "菜谱已经满了，无法再获得新菜谱。", null);
                return false;
            }

            RunPersistence.Save(_run);
            RefreshPersistent();
            if (_current == GameplayView.Food)
            {
                BuildBattleRecipe();
            }
            else
            {
                _recipePresenter?.BuildShop(_run, BuyRecipeBook);
            }

            return true;
        }

        private ActionSelectSnapshot CaptureActionSelectSnapshot()
        {
            if (_current != GameplayView.ActionSelect)
            {
                return ActionSelectSnapshot.None;
            }

            string title = _centerTitleText != null ? _centerTitleText.text : string.Empty;
            bool cardsActive = _deck != null && _deck.CardsActive;
            bool skipActive = _deck != null && _deck.SkipActive;
            return new ActionSelectSnapshot(title, cardsActive, skipActive);
        }

        private void RestoreActionSelection(ActionSelectSnapshot snapshot)
        {
            if (!snapshot.HasSnapshot)
            {
                SetCenterTitle("选择行动");
                BuildActionCards();
                return;
            }

            SetCenterTitle(string.IsNullOrWhiteSpace(snapshot.Title) ? "选择行动" : snapshot.Title);
            _deck?.SetCardsActive(snapshot.CardsActive);
            _deck?.SetSkipActive(snapshot.SkipActive);
        }

        private void EnsureRewardItemChoicePanel()
        {
            if (_rewardItemChoicePanel != null || _center == null)
            {
                return;
            }

            if (_rewardItemChoicePanelPrefab == null)
            {
                Debug.LogError($"{nameof(BattleForm)} 缺少奖励道具选择面板 prefab。", this);
                return;
            }

            _rewardItemChoicePanel = Instantiate(_rewardItemChoicePanelPrefab, (RectTransform)_center.transform);
            RectTransform rect = _rewardItemChoicePanel.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.18f, 0.03f);
                rect.anchorMax = new Vector2(0.82f, 0.9f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.one;
            }

            _rewardItemChoicePanel.gameObject.SetActive(false);
        }

        private void EnsureRandomizedItemsPanel()
        {
            if (_randomizedItemsPanel != null)
            {
                return;
            }

            Log.Error("BattleForm: randomized items panel is not configured.", Tag);
        }

        private void RestoreAfterAcquireView(GameplayView previous)
        {
            RefreshPersistent();
            switch (previous)
            {
                case GameplayView.ActionSelect:
                    ShowActionSelection();
                    break;
                case GameplayView.Shop:
                    SwitchTo(GameplayView.Shop);
                    break;
                case GameplayView.RecipeEdit:
                    SwitchTo(GameplayView.RecipeEdit);
                    break;
                case GameplayView.Food:
                    SwitchTo(GameplayView.Food);
                    break;
                default:
                    SwitchTo(GameplayView.Shop);
                    break;
            }
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

        private void OnTableEditSkipClicked()
        {
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
        }

        /// <summary>战斗态扇形菜谱条：每本菜谱一张卡，点击从该菜谱上菜（触发世界空间上菜动画）。</summary>
        private void BuildBattleRecipe()
        {
            _recipePresenter?.BuildBattle(_session, ServeFromRecipe);
        }

        private void ServeFromRecipe(int slotIndex)
        {
            if (_world == null || _session == null || _session.IsSettled)
            {
                return;
            }

            _world.TryServeDish(slotIndex);
            RefreshAll();
        }

        /// <summary>刷新常驻信息：左栏周/金币（分数、食物调整为局内占位）、右栏道具。</summary>
        private void RefreshPersistent(bool refreshItems = true)
        {
            if (_run == null)
            {
                return;
            }

            BattleWorldController world = _world ?? BattleWorldController.Instance;
            _infoColumn?.Refresh(_run, _session, _current, world);

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

        private void RefreshFoodActions()
        {
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            _foodBar?.Refresh(_current == GameplayView.Food, _session, world);
        }

        /// <summary>右栏道具：被动网格（2 列）+ 固定主动道具槽，每份主动实例占一格。</summary>
        private void RefreshItems()
        {
            _itemsColumn?.Refresh(_run, _session, _inBattle, _tips != null ? _tips.Item : null, OnActiveItemClicked, ShowItemInfo);
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
            if (_tips != null)
            {
                _tips.HideAll();
            }
        }

        /// <summary>点击扇形末尾的「购买空菜谱」卡：扣金币加一本菜谱，随后刷新商店与底部菜谱条。</summary>
        private void BuyRecipeBook()
        {
            if (_run == null)
            {
                return;
            }

            if (ShopService.PurchaseRecipeBook(_run))
            {
                _shopPanel?.RefreshShop();
            }
        }

        /// <summary>商店内数据变化回调：刷新常驻壳信息 + 底部扇形菜谱条。</summary>
        private void RefreshShopPersistent()
        {
            RefreshPersistent(_shopItemFlyInFlight <= 0);
            _recipePresenter?.BuildShop(_run, BuyRecipeBook);
        }

        private void PlayShopItemPurchaseFly(ShopEntry entry, ShopBuyItemViewBase sourceCard)
        {
            if (entry == null || sourceCard == null || _itemsColumn == null)
            {
                return;
            }

            cfg.ItemKind kind = entry.Kind == ShopEntryKind.ActiveItem ? cfg.ItemKind.Active : cfg.ItemKind.Passive;
            ItemDefinition item = ItemDefinition.Get(GameApp.Config.Tables, entry.Id, kind);
            if (item == null)
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
            if (!TryGetRectInLayer(sourceRect, layer, out RectSnapshot start) ||
                !_itemsColumn.TryGetItemFlyTarget(_run, entry.Id, item.Kind, layer, out Vector2 targetCenter, out Vector2 targetSize))
            {
                return;
            }

            _shopItemFlyInFlight++;
            Sprite sprite = sourceCard.PurchaseFlySprite ?? RunItemSlotView.LoadIcon(item) ?? LoadShopItemFallbackIcon(item.Kind);
            PlayItemFlyTween(
                start,
                new RectSnapshot(targetCenter, targetSize),
                sprite,
                RunItemSlotView.QualityColor(item.Quality),
                OnShopItemFlyComplete);
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
            sequence.Append(DOVirtual.Float(0f, 1f, ShopItemFlyDuration, t =>
            {
                if (rect == null)
                {
                    return;
                }

                rect.anchoredPosition = Vector2.LerpUnclamped(start.Center, end.Center, t);
                rect.sizeDelta = Vector2.LerpUnclamped(start.Size, end.Size, t);
            }).SetEase(Ease.InOutCubic));
            sequence.Insert(ShopItemFlyDuration * 0.8f, DOVirtual.Float(1f, 0f, ShopItemFlyDuration * 0.2f, alpha =>
            {
                if (group != null)
                {
                    group.alpha = alpha;
                }
            }).SetEase(Ease.InQuad));
            sequence.OnComplete(() => finish());
            sequence.OnKill(() => finish());
        }

        private void OnShopItemFlyComplete()
        {
            _shopItemFlyInFlight = Mathf.Max(0, _shopItemFlyInFlight - 1);
            if (_shopItemFlyInFlight == 0)
            {
                RefreshItems();
            }
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
            _deck?.ShowActionChoices(RollChoices(_run), OnActionSelectionPicked, OnActionRerollClicked, _run != null ? _run.ActionRerollCount : 0);
        }

        /// <summary>事件 n 选一卡片：每个选项一张卡，点击回调选项序号。</summary>
        private void BuildEventCards(IReadOnlyList<string> options, Action<int> onPick)
        {
            if (_deck == null)
            {
                onPick?.Invoke(0);
                return;
            }

            _deck.ShowEventOptions(options, index => OnEventOptionPicked(index, onPick), () => onPick?.Invoke(0));
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

        /// <summary>玩家在中部选择了一个事件选项。</summary>
        private void OnEventOptionPicked(int index, Action<int> onPick)
        {
            _deck?.HideThenDestroy(() =>
            {
                if (_actionSelectionPanel != null)
                {
                    _actionSelectionPanel.SetActive(false);
                }

                onPick?.Invoke(index);
            });
        }

        /// <summary>玩家点击行动轴节点卡片。</summary>
        private void OnTimelineNodePicked(Action onPick)
        {
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
            _inBattle = false;
            _current = GameplayView.None;

            if (_actionSelectionPanel != null)
            {
                _actionSelectionPanel.SetActive(false);
            }

            if (_shopPanel != null)
            {
                _shopPanel.gameObject.SetActive(false);
            }

            if (_recipeWorkspacePanel != null)
            {
                _recipeWorkspacePanel.gameObject.SetActive(false);
            }

            if (_rewardDishPackPanel != null)
            {
                _rewardDishPackPanel.gameObject.SetActive(false);
            }

            _rewardItemChoicePanel?.Close();
            _randomizedItemsPanel?.Close();

            if (_boardEditSkipButton != null)
            {
                _boardEditSkipButton.gameObject.SetActive(false);
            }

            _recipeView?.SetState(RecipeView.RecipeState.Hidden);
            SetFoodActionsVisible(false);
            _infoColumn?.ScoreFire?.Hide();
            _infoColumn?.ResetTableLabel();
            SetMessage(string.Empty);

            if (_hudFrame != null)
            {
                _hudFrame.SetActive(false);
            }
        }

        private void OnSettingsClicked()
        {
            GameApp.UI.OpenUIForm(UIForms.Settings, UIForms.GroupDialog, new SettingsFormData(inGameplay: true));
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
            if (_tableCoordinator == null)
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

            _tableCoordinator.Open();
        }

        // —— 食物调整态 ——

        private void OnFoodAdjustClicked()
        {
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if (world == null)
            {
                return;
            }

            if (world.IsFoodAdjusting)
            {
                world.EndFoodAdjust();
                ExitFoodAdjustUI();
                return;
            }

            if (_current != GameplayView.Food || _session == null || _session.IsSettled)
            {
                return;
            }

            EnterFoodAdjustUI();
            world.BeginFoodAdjust(ExitFoodAdjustUI);
        }

        private void EnterFoodAdjustUI()
        {
            EnsureFoodAdjustOverlay();
            _foodAdjustOverlay?.Show(_boardArea);
            _infoColumn?.SetFoodAdjustActive(true, _run != null ? _run.FoodAdjustCount : 0);
        }

        private void ExitFoodAdjustUI()
        {
            _foodAdjustOverlay?.Hide();
            _infoColumn?.SetFoodAdjustActive(false, _run != null ? _run.FoodAdjustCount : 0);
            RefreshPersistent();
        }

        private void EnsureFoodAdjustOverlay()
        {
            if (_foodAdjustOverlay == null)
            {
                if (_foodAdjustOverlayPrefab == null)
                {
                    Debug.LogError($"{nameof(BattleForm)} 缺少食物调整遮罩 prefab。", this);
                    return;
                }

                _foodAdjustOverlay = Instantiate(_foodAdjustOverlayPrefab, (RectTransform)transform);
                _foodAdjustOverlay.Hide();
            }
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
            SetMessage(string.Empty);
            _run.BeginFoodActionAdjustments(BossDebuffModifiers.IsPrefabFood(modifier));
            _session = _run.BuildBattleSession(requiredScore, modifier, key);
            // 常驻壳在战斗中持续显示并接管分数/道具/菜谱面板（餐桌/菜品仍在世界空间场景）。
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
            RefreshAll();
        }

        private void OnDishHoverEntered(DishPieceView piece)
        {
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
            RebindHoveredDishTips(piece);
        }

        private void RebindHoveredDishTips(DishPieceView piece)
        {
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
            if (_settlementReveal == null || signal.IsEmpty)
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

            if (_current != GameplayView.Food && _current != GameplayView.TableView)
            {
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

            _hoveredDishPiece = null;
            _hoveredCell = cell;
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
            if (_hoveredCell != null && cell != null && _hoveredCell != cell)
            {
                return;
            }

            if (_hoveredDishPiece == null)
            {
                HideFoodTips();
            }
        }

        private void HideFoodTips()
        {
            _hoveredDishPiece = null;
            _hoveredCell = null;
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
        }

        private void OnEatClicked()
        {
            if (_session == null || _session.IsSettled)
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
            foreach (DishInstance dish in _session.DiningTable.Dishes)
            {
                reveal.CaptureBaseline(dish);
            }

            _settlementReveal = reveal;

            ScoreResult result = _session.Settle();
            ApplyRecipeScoreDeltasToRun();
            SetSettlementScore(0);
            RefreshFoodActions();

            if (_world != null)
            {
                _world.PlaySettlement(result, _infoColumn != null ? _infoColumn.ScoreFire : null, OnSettlementReveal, () => OnSettlementComplete(result));
            }
            else
            {
                OnSettlementComplete(result);
            }
        }

        private void OnSettlementComplete(ScoreResult result)
        {
            // 演出走完：清空渐进揭示态，hover 恢复展示完整结算结果。
            _settlementReveal = null;
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
                _run.EndFoodActionAdjustments();
            }

            _infoColumn?.SetBattleScoreOverride(null);
            RefreshAll();
            _loop?.OnBattleSettled(result, _session != null && _session.IsWin);
        }

        private void ApplyRecipeScoreDeltasToRun()
        {
            if (_run == null || _session == null)
            {
                return;
            }

            foreach (RecipeScoreFlatDelta delta in _session.LastRecipeScoreFlatDeltas)
            {
                _run.AddRecipeScoreFlat(delta.BookIndex, delta.DishIndex, delta.Delta);
            }

            foreach (RecipeScoreMultiplierDelta delta in _session.LastRecipeScoreMultiplierDeltas)
            {
                _run.MultiplyRecipeScore(delta.BookIndex, delta.DishIndex, delta.Multiplier);
            }
        }

        private void RefreshAll()
        {
            _world?.RefreshAll();
            if (_inBattle)
            {
                RefreshPersistent();
                BuildBattleRecipe();
            }
        }

        // —— 局内交互（透传到战斗世界）——

        private void OnDoodleClearClicked()
        {
            (_world ?? BattleWorldController.Instance)?.ClearDoodle();
            RefreshFoodActions();
        }

        private void OnDoodleToggleClicked()
        {
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

        internal void RefreshAfterActiveItem(bool boardChanged, bool persist, bool actionChoicesChanged = false)
        {
            if (boardChanged)
            {
                (_world ?? BattleWorldController.Instance)?.SyncTableFromSession();
            }

            if (persist && _run != null)
            {
                RunPersistence.Save(_run);
            }

            RebuildActionAxis();
            if (_current == GameplayView.ActionSelect)
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

                RefreshPersistent();
            }
            else if (_current == GameplayView.Shop)
            {
                RefreshShopPersistent();
            }
            else if (_current == GameplayView.RecipeEdit)
            {
                _recipeWorkspacePanel?.Refresh();
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
