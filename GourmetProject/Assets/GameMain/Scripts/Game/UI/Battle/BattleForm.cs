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

namespace GourmetProject.Game.UI.Battle
{
    /// <summary>
    /// 局外周循环编排枢纽 + 局内战斗结果壳。
    /// 常驻壳（左列信息 / 右列道具 / 顶部行动轴 / 底部扇形菜谱）进入玩法后全程常驻，只有中部内容区在五态间切换：
    /// 行动选择(含事件 n 选一) / 商店 / 编辑菜谱 / 美食战斗 / 棋盘编辑。切换只对中部内容区做 DOTween 渐隐渐显
    /// （<see cref="UITransition.FadeSwap"/>），常驻壳不参与动画；美食 / 棋盘态在同一 Battle 场景内透出世界空间表现。
    /// </summary>
    public sealed class BattleForm : UGuiForm, IWeekLoopView, IBattleViewHost, IStomachViewHost
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

        [Header("Board Area (棋盘锁定区)")]
        [Tooltip("HUD 里的空区矩形：世界棋盘将 fit 并居中锁定在该屏幕区域内；同时作为食物调整遮黑的挖洞区。")]
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

        [Header("Recipe Edit (center)")]
        [SerializeField] private RecipeEditPanel _recipeEditPanel;

        [Header("Reward Dish Pack (center)")]
        [SerializeField] private RewardDishPackPanel _rewardDishPackPanel;

        [Header("Board Edit")]
        [SerializeField] private Button _boardEditSkipButton;

        [Header("Right Column - Items")]
        [SerializeField] private BattleItemsColumn _itemsColumn;

        [Header("Recipe View")]
        [SerializeField] private RecipeView _recipeView;

        [Header("Food Actions")]
        [SerializeField] private BattleFoodActionBar _foodBar;

        [Header("Battle Message")]
        [SerializeField] private Text _messageText;

        private bool _inBattle;
        private GameplayView _current = GameplayView.None;
        private Action<bool> _afterRewardBoardEdit;

        private GameRun _run;
        private BattleSession _session;
        private BattleWorldController _world;

        private WeekLoopController _loop;

        private RecipeBooksPresenter _recipePresenter;
        private TimelineAxisBinder _axisBinder;
        private StomachViewCoordinator _stomachCoordinator;
        private GameplayViewStateMachine _viewStates;
        private int _shopItemFlyInFlight;
        private View.FoodAdjustOverlay _foodAdjustOverlay;

        public GameRun Run => _run;
        public BattleSession Session => _session;
        public ActionExecutionContext CurrentBattleActionContext => _loop?.CurrentBattleActionContext;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            _infoColumn?.Bind(OnSettingsClicked, OnViewStomachClicked, OnFoodAdjustClicked);
            _foodBar?.Bind(OnOverviewClicked, OnEatClicked, OnDoodleClearClicked, OnDoodleToggleClicked);

            if (_boardEditSkipButton != null)
            {
                _boardEditSkipButton.onClick.AddListener(OnBoardEditSkipClicked);
            }

            _recipePresenter = new RecipeBooksPresenter(_recipeView);
            _axisBinder = new TimelineAxisBinder(
                _actionAxisBar,
                () => _tips != null ? _tips.Shop : null,
                () => _tips != null ? _tips.Interest : null,
                () => _tips != null ? _tips.Boss : null);
            _stomachCoordinator = new StomachViewCoordinator(this);
            _viewStates = new GameplayViewStateMachine(this);

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
            _tips?.EnsureAll();
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
            _deck?.KillAllTweens();
            HideAllTips();
            _world?.HideWorld();
            base.OnClose(isShutdown, userData);
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
            world?.ClearBattleBoard();
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
            bool worldView = view == GameplayView.Food || view == GameplayView.BoardEdit || view == GameplayView.StomachView;

            if (_actionSelectionPanel != null)
            {
                _actionSelectionPanel.SetActive(actionSel);
            }

            if (_shopPanel != null)
            {
                _shopPanel.gameObject.SetActive(shop);
            }

            if (_recipeEditPanel != null)
            {
                _recipeEditPanel.gameObject.SetActive(recipeEdit);
            }

            if (_rewardDishPackPanel != null)
            {
                _rewardDishPackPanel.gameObject.SetActive(rewardDishPack);
            }

            if (_boardEditSkipButton != null)
            {
                _boardEditSkipButton.gameObject.SetActive(view == GameplayView.BoardEdit);
            }

            // 行动轴：仅行动选择 / 商店常驻显示；编辑菜谱 / 美食 / 棋盘态隐藏。
            SetActionAxisVisible(actionSel || shop);
            SetFoodActionsVisible(view == GameplayView.Food);

            // 白底：世界态（美食 / 棋盘）关闭，让 Battle 场景世界空间透出；其余态开启。
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
                    GameplayView.RewardDishPack => RecipeView.RecipeState.Shown,
                    GameplayView.Food => RecipeView.RecipeState.Shown,
                    GameplayView.BoardEdit => RecipeView.RecipeState.Collapsed,
                    _ => RecipeView.RecipeState.Hidden,
                };
                _recipeView.SetState(recipeState);
            }

            RefreshPersistent();
        }

        /// <summary>商店态：打开商店四区面板并接线各回调（离开/刷新/编辑菜谱/棋盘编辑）。</summary>
        private void OpenShopPanel()
        {
            if (_shopPanel != null)
            {
                _shopPanel.Open(OnShopLeave, RefreshShopPersistent, OpenRecipeEdit, OpenBoardEdit, _recipeView, PlayShopItemPurchaseFly);
            }
        }

        /// <summary>编辑菜谱态：打开菜谱编辑面板。</summary>
        private void OpenRecipeEditPanel()
        {
            if (_recipeEditPanel != null)
            {
                _recipeEditPanel.Open(_run, OpenShopFromEdit, RefreshShopPersistent);
            }
        }

        // —— IBattleViewHost（供状态机/各态回调壳，转发到壳内私有实现）——

        GameRun IBattleViewHost.Run => _run;
        RecipeBooksPresenter IBattleViewHost.Recipe => _recipePresenter;
        void IBattleViewHost.ApplyShellForView(GameplayView view) => ApplyShellForView(view);
        void IBattleViewHost.SetCenterTitle(string text) => SetCenterTitle(text);
        void IBattleViewHost.RebuildActionAxis() => RebuildActionAxis();
        void IBattleViewHost.OpenShopPanel() => OpenShopPanel();
        void IBattleViewHost.OpenRecipeEditPanel() => OpenRecipeEditPanel();
        void IBattleViewHost.BuildBattleRecipe() => BuildBattleRecipe();
        void IBattleViewHost.BuyRecipeBook() => BuyRecipeBook();

        // —— IStomachViewHost（供查看胃编排回调壳）——

        GameplayView IStomachViewHost.CurrentView => _current;
        GameRun IStomachViewHost.Run => _run;
        BattleWorldController IStomachViewHost.World => _world ?? BattleWorldController.Instance;
        void IStomachViewHost.SwitchTo(GameplayView view, Action buildCenter, Action onShown) => SwitchTo(view, buildCenter, onShown);
        void IStomachViewHost.RestoreBattleWorld() => RestoreBattleWorld();
        void IStomachViewHost.PlayShowCardsWhenReady() => PlayShowCardsWhenReady();
        void IStomachViewHost.OpenBoardEdit() => OpenBoardEdit();
        ActionSelectSnapshot IStomachViewHost.CaptureActionSelectSnapshot() => CaptureActionSelectSnapshot();
        void IStomachViewHost.RestoreActionSelection(ActionSelectSnapshot snapshot) => RestoreActionSelection(snapshot);

        // —— 行动选择态（含事件 n 选一，共用中部卡片）——

        private void ShowActionSelection()
        {
            SwitchTo(GameplayView.ActionSelect, () =>
            {
                SetCenterTitle("选择行动");
                BuildActionCards();
            }, PlayShowCardsWhenReady);
        }

        /// <summary>事件「n 选一」：与行动选择共用中部卡片 UI，事件名/描述作为中部标题，每个选项一张卡。</summary>
        public void ShowEventChoices(string title, string desc, IReadOnlyList<string> options, Action<int> onPick)
        {
            string prompt = string.IsNullOrWhiteSpace(desc) ? title : $"{title}\n{desc}";
            SwitchTo(GameplayView.ActionSelect, () =>
            {
                SetCenterTitle(prompt);
                BuildEventCards(options, onPick);
            }, PlayShowCardsWhenReady);
        }

        private void SetCenterTitle(string text)
        {
            if (_centerTitleText != null)
            {
                _centerTitleText.text = text ?? string.Empty;
            }
        }

        // —— 商店 / 编辑菜谱 / 棋盘编辑 入口 ——

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
            SwitchTo(GameplayView.RecipeEdit);
        }

        private void OpenShopFromEdit()
        {
            SwitchTo(GameplayView.Shop);
        }

        /// <summary>购买碎片包后进入棋盘编辑态（世界空间）：隐藏商店/行动轴，露出棋盘手动拼贴。</summary>
        private void OpenBoardEdit()
        {
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if (world == null || _run == null || !_run.HasPendingFragmentPack)
            {
                return;
            }

            SwitchTo(GameplayView.BoardEdit, () => world.BeginBoardEdit(_run, _run.PendingFragmentPack, OnBoardEditDone));
        }

        public void OpenRewardBoardEdit(Action<bool> onDone)
        {
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if (world == null || _run == null || !_run.HasPendingFragmentPack)
            {
                onDone?.Invoke(false);
                return;
            }

            _afterRewardBoardEdit = onDone;
            SwitchTo(GameplayView.BoardEdit, () => world.BeginBoardEdit(_run, _run.PendingFragmentPack, OnRewardBoardEditDone));
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
                _rewardDishPackPanel.Open(_run, choices, _recipeView, onChoiceDropped, onSkip);
            });
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

            _world.SetBoardArea(_boardArea);
            _world.Initialize(
                _run,
                _session,
                SetMessage,
                SetSettlementScore,
                RefreshAll,
                OnActiveItemClicked,
                OnDishClicked,
                resetDoodle: false);
            RefreshAll();
        }

        private void OnBoardEditDone(bool placed)
        {
            (_world ?? BattleWorldController.Instance)?.HideWorld();
            SwitchTo(GameplayView.Shop);
        }

        private void OnRewardBoardEditDone(bool placed)
        {
            (_world ?? BattleWorldController.Instance)?.HideWorld();
            Action<bool> cb = _afterRewardBoardEdit;
            _afterRewardBoardEdit = null;
            SwitchTo(GameplayView.None, onShown: () => cb?.Invoke(placed));
        }

        private void OnBoardEditSkipClicked()
        {
            (_world ?? BattleWorldController.Instance)?.SkipBoardEditPack();
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

        private void RefreshFoodActions()
        {
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            _foodBar?.Refresh(_current == GameplayView.Food, _session, world);
        }

        /// <summary>右栏道具：被动网格（2 列）+ 固定 2 个主动道具槽，聚合同 id 主动实例。</summary>
        private void RefreshItems()
        {
            _itemsColumn?.Refresh(_run, _session, _inBattle, _tips != null ? _tips.Item : null, OnActiveItemClicked, ShowItemInfo);
        }

        private void ShowItemInfo(cfg.Item item, RunItemState state)
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
            _tips?.HideAll();
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

        private void PlayShopItemPurchaseFly(ShopEntry entry, ShopBuyCardView sourceCard)
        {
            if (entry == null || sourceCard == null || _itemsColumn == null)
            {
                return;
            }

            cfg.Item item = GameApp.Config.Tables.TbItem.GetOrDefault(entry.Id);
            if (item == null || (item.Kind != cfg.ItemKind.Passive && item.Kind != cfg.ItemKind.Active))
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

            var go = new GameObject("ShopItemFlyFx", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            var group = go.GetComponent<CanvasGroup>();
            var image = go.GetComponent<Image>();

            rect.SetParent(layer, false);
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
            _deck?.ShowActionChoices(RollChoices(_run), OnActionSelectionPicked);
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

            if (_recipeEditPanel != null)
            {
                _recipeEditPanel.gameObject.SetActive(false);
            }

            if (_rewardDishPackPanel != null)
            {
                _rewardDishPackPanel.gameObject.SetActive(false);
            }

            if (_boardEditSkipButton != null)
            {
                _boardEditSkipButton.gameObject.SetActive(false);
            }

            _recipeView?.SetState(RecipeView.RecipeState.Hidden);
            SetFoodActionsVisible(false);
            _infoColumn?.ScoreFire?.Hide();
            _infoColumn?.ResetStomachLabel();
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

        private void OnViewStomachClicked()
        {
            if (_stomachCoordinator == null)
            {
                return;
            }

            if (_current == GameplayView.StomachView)
            {
                _stomachCoordinator.Back();
                return;
            }

            _stomachCoordinator.Open();
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
                _foodAdjustOverlay = View.FoodAdjustOverlay.Create((RectTransform)transform);
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
            _run.BeginFoodActionAdjustments();
            _session = _run.BuildBattleSession(requiredScore, modifier, key);
            // 常驻壳在战斗中持续显示并接管分数/道具/菜谱面板（棋盘/菜品仍在世界空间场景）。
            SwitchTo(GameplayView.Food);

            _world = BattleWorldController.Instance;
            if (_world == null)
            {
                Log.Error("BattleForm: battle scene controller not found (scene not loaded?).", Tag);
                return;
            }

            _world.SetBoardArea(_boardArea);
            _world.Initialize(
                _run,
                _session,
                SetMessage,
                SetSettlementScore,
                RefreshAll,
                OnActiveItemClicked,
                OnDishClicked);
            RefreshAll();
        }

        private void OnEatClicked()
        {
            if (_session == null || _session.IsSettled)
            {
                return;
            }

            if (_session.Board.DishCount == 0)
            {
                SetMessage("棋盘还是空的，先上几道菜吧。");
                return;
            }

            ScoreResult result = _session.Settle();
            SetSettlementScore(0);
            RefreshFoodActions();

            if (_world != null)
            {
                _world.PlaySettlement(result, _infoColumn != null ? _infoColumn.ScoreFire : null, () => OnSettlementComplete(result));
            }
            else
            {
                OnSettlementComplete(result);
            }
        }

        private void OnSettlementComplete(ScoreResult result)
        {
            // 结算侧效果写回局外状态：金币入账（经济运营 + 上菜 OnServe）、大局结算历史累计。
            if (_run != null && _session != null)
            {
                int gold = (int)System.Math.Round(_session.PendingGold, System.MidpointRounding.AwayFromZero);
                if (gold != 0)
                {
                    _run.Gold = System.Math.Max(0, _run.Gold + gold);
                }

                _run.AddSettledCounts(_session.LastSettledIncrements);
                _run.EndFoodActionAdjustments();
            }

            _infoColumn?.SetBattleScoreOverride(null);
            RefreshAll();
            _loop?.OnBattleSettled(result, _session != null && _session.IsWin);
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

        private void OnOverviewClicked()
        {
            GameplayFlowSignal.RequestReturnToMenu();
        }

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
            if (inst == null || _run == null)
            {
                return;
            }

            var data = new DishDetailData(inst.Def, _run.Database, inst.SkillIds, inst.FlavorId, inst.SkillSources, inst.TransferredSkills);
            GameApp.UI.OpenUIForm(UIForms.DishDetail, UIForms.GroupDialog, data);
        }

        private void OnActiveItemClicked(string itemId)
        {
            if (_session == null || _session.IsSettled)
            {
                return;
            }

            cfg.Item item = GameApp.Config.Tables.TbItem.GetOrDefault(itemId);
            if (item == null || item.Kind != cfg.ItemKind.Active)
            {
                return;
            }

            if (!_run.HasItem(itemId))
            {
                _world?.ShowMessage($"{item.Name}：没有可用道具。");
                RefreshAll();
                return;
            }

            if (item.TriggerTiming != cfg.ItemTriggerTiming.BeforeEat)
            {
                _world?.ShowMessage($"{item.Name}：现在不是使用时机。");
                RefreshAll();
                return;
            }

            ActiveItemUseResult result = ActiveItemEffectRegistry.TryUse(_session, _run, item);
            _world?.ShowMessage(result.Message);
            if (!result.Success)
            {
                RefreshAll();
                return;
            }

            _run.UseActiveItem(itemId);
            if (result.BoardChanged)
            {
                _world?.SyncBoardFromSession();
            }

            // 战斗过程中用道具只改内存，不即时存档；战斗结算（胜利领奖确认）时由编排层 Commit 统一入档。
            // 中途退出游戏则未存档，重进会重做该战斗，道具不消耗。
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
