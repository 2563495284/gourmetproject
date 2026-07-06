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
using GourmetProject.Game.UI.Widgets;

namespace GourmetProject.Game.UI.Battle
{
    /// <summary>
    /// 局外周循环编排枢纽 + 局内战斗结果壳。
    /// 常驻壳（左列信息 / 右列道具 / 顶部行动轴 / 底部扇形菜谱）进入玩法后全程常驻，只有中部内容区在五态间切换：
    /// 行动选择(含事件 n 选一) / 商店 / 编辑菜谱 / 美食战斗 / 棋盘编辑。切换只对中部内容区做 DOTween 渐隐渐显
    /// （<see cref="UITransition.FadeSwap"/>），常驻壳不参与动画；美食 / 棋盘态在同一 Battle 场景内透出世界空间表现。
    /// </summary>
    public sealed class BattleForm : UGuiForm, IWeekLoopView
    {
        private const string Tag = "Battle";
        private const int PassiveSlotCapacity = 10;
        private const int PassiveSlotColumns = 2;
        private const string ViewStomachLabel = "查看胃";
        private const string StomachBackLabel = "返回";

        /// <summary>常驻壳中部内容区的五种状态。</summary>
        public enum GameplayView
        {
            None,
            ActionSelect,
            Shop,
            RecipeEdit,
            Food,
            BoardEdit,
            StomachView,
        }

        /// <summary>当前打开的战斗界面，供各弹窗回调推进周循环。</summary>
        public static BattleForm Active { get; private set; }

        [Header("HUD Frame")]
        [SerializeField] private GameObject _hudFrame;
        [SerializeField] private GameObject _backdrop;

        [Header("Center (切换动画区)")]
        [Tooltip("中部内容区根的 CanvasGroup：五态切换时对它做渐隐渐显；常驻壳不在其下。")]
        [SerializeField] private CanvasGroup _center;
        [SerializeField] private Text _centerTitleText;

        [Header("Left Column")]
        [SerializeField] private Text _weekText;
        [SerializeField] private Text _goldText;
        [SerializeField] private Text _scoreReqText;
        [SerializeField] private Text _foodAdjustText;
        [SerializeField] private Button _viewStomachButton;
        private Text _viewStomachButtonText;
        [SerializeField] private Button _settingsButton;

        [Header("Action Axis")]
        [SerializeField] private ActionAxisBar _actionAxisBar;

        [Header("Action Selection (center)")]
        [SerializeField] private GameObject _actionSelectionPanel;
        [SerializeField] private RectTransform _cardsContainer;
        [SerializeField] private WeekEventCardView _cardPrefab;
        [SerializeField] private Button _skipButton;

        [Header("Shop (center)")]
        [SerializeField] private ShopForm _shopPanel;

        [Header("Recipe Edit (center)")]
        [SerializeField] private RecipeEditPanel _recipeEditPanel;

        [Header("Right Column - Items")]
        [SerializeField] private RectTransform _passiveItemsContainer;
        [SerializeField] private RunItemSlotView _itemSlotPrefab;
        [SerializeField] private RunItemSlotView[] _activeItemSlots;

        [Header("Recipe View")]
        [SerializeField] private RecipeView _recipeView;

        [Header("Food Actions")]
        [SerializeField] private GameObject _foodActions;
        [SerializeField] private Button _overviewButton;
        [SerializeField] private Button _eatButton;
        [SerializeField] private Button _doodleClearButton;
        [SerializeField] private Button _doodleToggleButton;
        [SerializeField] private Text _doodleToggleText;

        private readonly List<RunItemSlotView> _passiveSlots = new();
        private readonly List<WeekEventCardView> _cards = new();
        private bool _inBattle;
        private GameplayView _current = GameplayView.None;
        private GameplayView _stomachReturnView = GameplayView.None;
        private bool _hasStomachActionReturnSnapshot;
        private string _stomachActionReturnTitle;
        private bool _stomachActionReturnCardsActive;
        private bool _stomachActionReturnSkipActive;
        private Tween _pendingCardShowTween;
        private Tween _cardsHideTween;

        private GameRun _run;
        private BattleSession _session;
        private BattleWorldController _world;

        private WeekLoopController _loop;

        public GameRun Run => _run;
        public BattleSession Session => _session;
        public ActionExecutionContext CurrentBattleActionContext => _loop?.CurrentBattleActionContext;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            if (_settingsButton != null)
            {
                _settingsButton.onClick.AddListener(OnSettingsClicked);
            }

            if (_viewStomachButton != null)
            {
                _viewStomachButtonText = _viewStomachButton.GetComponentInChildren<Text>(true);
                _viewStomachButton.onClick.AddListener(OnViewStomachClicked);
            }

            if (_skipButton != null)
            {
                _skipButton.onClick.AddListener(() => OnActionSelectionPicked(null));
            }

            if (_overviewButton != null)
            {
                _overviewButton.onClick.AddListener(OnOverviewClicked);
            }

            if (_eatButton != null)
            {
                _eatButton.onClick.AddListener(OnEatClicked);
            }

            if (_doodleClearButton != null)
            {
                _doodleClearButton.onClick.AddListener(OnDoodleClearClicked);
            }

            if (_doodleToggleButton != null)
            {
                _doodleToggleButton.onClick.AddListener(OnDoodleToggleClicked);
            }

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
            KillPendingCardShow();
            KillCardsHideTween();
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
            _world?.HideWorld();
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

        // —— 中部五态切换中枢 ——

        /// <summary>
        /// 切到某一中部态：只对中部内容区 <see cref="_center"/> 做 DOTween 渐隐渐显，常驻壳（左/右/行动轴/菜谱框）不动。
        /// 内容交换（隐藏旧面板 + 启用新面板 + 重建）集中在淡出完成后的 <see cref="ApplyView"/> 里执行。
        /// </summary>
        private void SwitchTo(GameplayView next, Action buildCenter = null, Action onShown = null)
        {
            if (_run == null)
            {
                return;
            }

            KillPendingCardShow();
            _current = next;
            _inBattle = next == GameplayView.Food;
            UITransition.FadeSwap(_center, () => ApplyView(next, buildCenter), onDone: onShown);
        }

        /// <summary>在淡出完成后落地某一态：切换中部面板显隐 + 配置常驻壳元素（行动轴/菜谱框/白底/世界）+ 重建内容。</summary>
        private void ApplyView(GameplayView view, Action buildCenter)
        {
            if (_run == null)
            {
                return;
            }

            if (_hudFrame != null)
            {
                _hudFrame.SetActive(true);
            }

            bool actionSel = view == GameplayView.ActionSelect;
            bool shop = view == GameplayView.Shop;
            bool recipeEdit = view == GameplayView.RecipeEdit;
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
                    GameplayView.Food => RecipeView.RecipeState.Shown,
                    _ => RecipeView.RecipeState.Hidden,
                };
                _recipeView.SetState(recipeState);
            }

            RefreshPersistent();

            switch (view)
            {
                case GameplayView.ActionSelect:
                    _actionAxisBar?.Build(_run);
                    BuildRecipeBooks(showAdd: false, onAdd: null);
                    buildCenter?.Invoke();
                    break;
                case GameplayView.Shop:
                    _actionAxisBar?.Build(_run);
                    BuildShopRecipeBooks();
                    if (_shopPanel != null)
                    {
                        _shopPanel.Open(OnShopLeave, RefreshShopPersistent, OpenRecipeEdit, OpenBoardEdit, _recipeView);
                    }

                    break;
                case GameplayView.RecipeEdit:
                    if (_recipeEditPanel != null)
                    {
                        _recipeEditPanel.Open(_run, OpenShopFromEdit, RefreshShopPersistent);
                    }

                    break;
                case GameplayView.Food:
                    buildCenter?.Invoke();
                    BuildBattleRecipe();
                    break;
                case GameplayView.BoardEdit:
                    SetCenterTitle(string.Empty);
                    _recipeView?.RemoveAddCard();
                    buildCenter?.Invoke();
                    break;
                case GameplayView.StomachView:
                    SetCenterTitle(string.Empty);
                    _recipeView?.RemoveAddCard();
                    buildCenter?.Invoke();
                    break;
            }
        }

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

        private void OpenStomachView()
        {
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if (world == null || _run == null || !world.CanEnterStomachView)
            {
                return;
            }

            _world = world;
            _stomachReturnView = _current;
            CaptureStomachActionReturnSnapshot();
            SwitchTo(GameplayView.StomachView, () => world.BeginStomachView(_run));
        }

        private void OnStomachViewBack()
        {
            GameplayView target = _stomachReturnView;
            if (target == GameplayView.None || target == GameplayView.StomachView)
            {
                target = GameplayView.ActionSelect;
            }

            _stomachReturnView = GameplayView.None;
            BattleWorldController world = _world ?? BattleWorldController.Instance;
            world?.EndStomachView();

            switch (target)
            {
                case GameplayView.Food:
                    SwitchTo(GameplayView.Food, RestoreBattleWorld);
                    break;
                case GameplayView.ActionSelect:
                    world?.HideWorld();
                    SwitchTo(GameplayView.ActionSelect, RestoreActionSelectionAfterStomach, PlayShowCardsWhenReady);
                    break;
                case GameplayView.BoardEdit:
                    if (_run != null && _run.HasPendingFragmentPack)
                    {
                        OpenBoardEdit();
                    }
                    else
                    {
                        world?.HideWorld();
                        SwitchTo(GameplayView.Shop);
                    }

                    break;
                default:
                    world?.HideWorld();
                    SwitchTo(target);
                    break;
            }
        }

        private void CaptureStomachActionReturnSnapshot()
        {
            _hasStomachActionReturnSnapshot = _current == GameplayView.ActionSelect;
            if (!_hasStomachActionReturnSnapshot)
            {
                _stomachActionReturnTitle = null;
                _stomachActionReturnCardsActive = false;
                _stomachActionReturnSkipActive = false;
                return;
            }

            _stomachActionReturnTitle = _centerTitleText != null ? _centerTitleText.text : string.Empty;
            _stomachActionReturnCardsActive = _cardsContainer != null && _cardsContainer.gameObject.activeSelf;
            _stomachActionReturnSkipActive = _skipButton != null && _skipButton.gameObject.activeSelf;
        }

        private void RestoreActionSelectionAfterStomach()
        {
            if (!_hasStomachActionReturnSnapshot)
            {
                SetCenterTitle("选择行动");
                BuildActionCards();
                return;
            }

            SetCenterTitle(string.IsNullOrWhiteSpace(_stomachActionReturnTitle)
                ? "选择行动"
                : _stomachActionReturnTitle);

            if (_cardsContainer != null)
            {
                _cardsContainer.gameObject.SetActive(_stomachActionReturnCardsActive);
            }

            if (_skipButton != null)
            {
                _skipButton.gameObject.SetActive(_stomachActionReturnSkipActive);
            }

            _hasStomachActionReturnSnapshot = false;
            _stomachActionReturnTitle = null;
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

            _world.Initialize(
                _run,
                _session,
                SetMessage,
                RefreshAll,
                OnActiveItemClicked,
                OnDishClicked,
                resetDoodle: false);
            RefreshAll();
        }

        private void OnBoardEditDone()
        {
            (_world ?? BattleWorldController.Instance)?.HideWorld();
            SwitchTo(GameplayView.Shop);
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
            if (_foodActions != null)
            {
                _foodActions.SetActive(visible);
            }
        }

        /// <summary>战斗态扇形菜谱条：每本菜谱一张卡，点击从该菜谱上菜（触发世界空间上菜动画）。</summary>
        private void BuildBattleRecipe()
        {
            if (_recipeView == null || _session == null)
            {
                return;
            }

            var books = new List<RecipeView.BookEntry>();
            for (int i = 0; i < _session.Slots.Count; i++)
            {
                RecipeSlot slot = _session.Slots[i];
                int slotIndex = i;
                bool interactable = !_session.IsSettled && !slot.IsEmpty;
                books.Add(new RecipeView.BookEntry(
                    $"菜谱{i + 1}",
                    $"剩 {slot.Count}",
                    interactable,
                    () => ServeFromRecipe(slotIndex)));
            }

            _recipeView.SetBooks(books);
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
        private void RefreshPersistent()
        {
            if (_run == null)
            {
                return;
            }

            BattleWorldController world = _world ?? BattleWorldController.Instance;
            if (_viewStomachButton != null)
            {
                bool stomachView = _current == GameplayView.StomachView;
                _viewStomachButton.interactable = stomachView
                    || (world != null && _current != GameplayView.None && world.CanEnterStomachView);
                SetViewStomachButtonLabel(stomachView ? StomachBackLabel : ViewStomachLabel);
            }

            if (_weekText != null)
            {
                _weekText.text = _run.IsEndless
                    ? $"无尽 第 {_run.WeekIndex - _run.TotalWeeks} 关"
                    : $"第 {_run.WeekIndex}/{_run.TotalWeeks} 周";
            }

            if (_goldText != null)
            {
                _goldText.text = _run.Gold.ToString();
            }

            // 分数要求 / 食物调整为局内数据：非战斗态显示占位，战斗态由阶段二接真值。
            if (_scoreReqText != null)
            {
                _scoreReqText.text = _session != null && !_session.IsSettled
                    ? $"{_session.PreviewScore().Total}/{_session.RequiredScore}"
                    : $"-/{_run.RequiredScore}";
            }

            if (_foodAdjustText != null)
            {
                _foodAdjustText.text = "-";
            }

            RefreshItems();
            RefreshFoodActions();
        }

        private void RefreshFoodActions()
        {
            bool food = _current == GameplayView.Food;
            BattleWorldController world = _world ?? BattleWorldController.Instance;

            if (_overviewButton != null)
            {
                _overviewButton.interactable = food;
            }

            if (_eatButton != null)
            {
                _eatButton.interactable = food && _session != null && !_session.IsSettled;
            }

            bool doodleReady = food && world != null;
            if (_doodleClearButton != null)
            {
                _doodleClearButton.interactable = doodleReady;
            }

            if (_doodleToggleButton != null)
            {
                _doodleToggleButton.interactable = doodleReady;
            }

            if (_doodleToggleText != null)
            {
                _doodleToggleText.text = world != null ? world.DoodleToggleLabel : "隐藏涂鸦";
            }
        }

        /// <summary>右栏道具：被动网格（2 列）+ 固定 2 个主动道具槽，聚合同 id 主动实例。</summary>
        private void RefreshItems()
        {
            ClearPassiveSlots();
            if (_run == null)
            {
                return;
            }

            cfg.Tables tables = GameApp.Config.Tables;

            if (_passiveItemsContainer != null && _itemSlotPrefab != null)
            {
                var passive = new List<RunItemState>();
                foreach (RunItemState state in _run.Items)
                {
                    cfg.Item item = tables.TbItem.GetOrDefault(state.ItemId);
                    if (item != null && item.Kind == cfg.ItemKind.Passive)
                    {
                        passive.Add(state);
                    }
                }

                int shown = Mathf.Min(PassiveSlotCapacity, passive.Count);
                int rows = Mathf.Max(1, Mathf.CeilToInt(PassiveSlotCapacity / (float)PassiveSlotColumns));
                float cellW = 1f / PassiveSlotColumns;
                float cellH = 1f / rows;
                for (int i = 0; i < shown; i++)
                {
                    RunItemState state = passive[i];
                    cfg.Item item = tables.TbItem.GetOrDefault(state.ItemId);
                    RunItemSlotView slot = Instantiate(_itemSlotPrefab, _passiveItemsContainer);
                    slot.gameObject.name = $"PassiveSlot_{i}";
                    int col = i % PassiveSlotColumns;
                    int row = i / PassiveSlotColumns;
                    var rect = (RectTransform)slot.transform;
                    rect.anchorMin = new Vector2(col * cellW, 1f - (row + 1) * cellH);
                    rect.anchorMax = new Vector2((col + 1) * cellW, 1f - row * cellH);
                    rect.offsetMin = new Vector2(2f, 2f);
                    rect.offsetMax = new Vector2(-2f, -2f);
                    rect.localScale = Vector3.one;

                    string badge = state.Level > 1 ? $"Lv{state.Level}" : string.Empty;
                    cfg.Item captured = item;
                    RunItemState capturedState = state;
                    slot.Bind(
                        RunItemSlotView.LoadIcon(item),
                        RunItemSlotView.ShortName(item.Name),
                        badge,
                        RunItemSlotView.QualityColor(item.Quality),
                        true,
                        () => ShowItemInfo(captured, capturedState));
                    _passiveSlots.Add(slot);
                }
            }

            RefreshActiveItems(tables);
        }

        private void RefreshActiveItems(cfg.Tables tables)
        {
            if (_activeItemSlots == null)
            {
                return;
            }

            // 主动道具多实例：按 id 聚合成一个槽，份数用角标 xN 展示。
            var activeStates = new List<RunItemState>();
            var activeCounts = new Dictionary<string, int>();
            foreach (RunItemState state in _run.Items)
            {
                cfg.Item item = tables.TbItem.GetOrDefault(state.ItemId);
                if (item == null || item.Kind != cfg.ItemKind.Active)
                {
                    continue;
                }

                if (activeCounts.TryGetValue(state.ItemId, out int held))
                {
                    activeCounts[state.ItemId] = held + 1;
                }
                else
                {
                    activeCounts[state.ItemId] = 1;
                    activeStates.Add(state);
                }
            }

            for (int i = 0; i < _activeItemSlots.Length; i++)
            {
                RunItemSlotView slot = _activeItemSlots[i];
                if (slot == null)
                {
                    continue;
                }

                if (i < activeStates.Count)
                {
                    RunItemState state = activeStates[i];
                    cfg.Item item = tables.TbItem.GetOrDefault(state.ItemId);
                    int held = activeCounts[state.ItemId];
                    string badge = held > 1 ? $"x{held}" : string.Empty;
                    cfg.Item captured = item;
                    RunItemState capturedState = state;

                    // 战斗中：满足触发时机的主动道具可点击使用；否则（含非战斗态）点击看信息。
                    bool usableNow = _inBattle && _session != null && !_session.IsSettled
                        && item.TriggerTiming == cfg.ItemTriggerTiming.BeforeEat;
                    string capturedId = state.ItemId;
                    System.Action onClick = usableNow
                        ? (System.Action)(() => OnActiveItemClicked(capturedId))
                        : () => ShowItemInfo(captured, capturedState);

                    slot.Bind(
                        RunItemSlotView.LoadIcon(item),
                        RunItemSlotView.ShortName(item.Name),
                        badge,
                        RunItemSlotView.QualityColor(item.Quality),
                        true,
                        onClick);
                }
                else
                {
                    slot.SetEmpty();
                }
            }
        }

        private void ShowItemInfo(cfg.Item item, RunItemState state)
        {
            if (item == null)
            {
                return;
            }

            string level = item.Kind == cfg.ItemKind.Passive && state != null && state.Level > 1 ? $" Lv.{state.Level}" : string.Empty;
            ShowNotice($"{item.Name}{level}", item.Desc, null);
        }

        private void ClearPassiveSlots()
        {
            foreach (RunItemSlotView slot in _passiveSlots)
            {
                if (slot != null)
                {
                    Destroy(slot.gameObject);
                }
            }

            _passiveSlots.Clear();
        }

        /// <summary>底部扇形菜谱条：按持有的菜谱本铺卡，显示 已放/容量（如 10/12）。showAdd 时末尾追加购买空菜谱卡。</summary>
        private void BuildRecipeBooks(bool showAdd, Action onAdd)
        {
            if (_recipeView == null || _run == null)
            {
                return;
            }

            var books = new List<RecipeView.BookEntry>();
            for (int i = 0; i < _run.RecipeBookCount; i++)
            {
                int count = _run.GetRecipeBookDishes(i).Count;
                books.Add(new RecipeView.BookEntry(
                    $"菜谱{i + 1}",
                    $"{count}/{GameRun.RecipeBookCapacity}",
                    false,
                    null,
                    count < GameRun.RecipeBookCapacity));
            }

            string addCost = showAdd ? $"+ {ShopService.EmptyRecipeBookPrice}" : null;
            _recipeView.SetBooks(books, showAdd, onAdd, addCost);
        }

        /// <summary>商店态菜谱条：展示持有菜谱本；未满上限时末尾追加唯一的「购买空菜谱」卡（买得起才可点）。</summary>
        private void BuildShopRecipeBooks()
        {
            if (_run == null)
            {
                return;
            }

            bool showAdd = _run.RecipeBookCount < GameRun.MaxRecipeBookCount;
            bool canBuy = showAdd && _run.Gold >= ShopService.EmptyRecipeBookPrice;
            BuildRecipeBooks(showAdd, canBuy ? BuyRecipeBook : (Action)null);
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
            RefreshPersistent();
            BuildShopRecipeBooks();
        }

        // —— 行动选择卡片（原 WeekMapForm 逻辑并入）——

        private void BuildActionCards()
        {
            ClearCards();
            if (_cardsContainer == null || _cardPrefab == null)
            {
                return;
            }

            List<ActionChoice> choices = RollChoices(_run);
            bool hasActions = choices.Count > 0;

            _cardsContainer.gameObject.SetActive(hasActions);
            if (_skipButton != null)
            {
                _skipButton.gameObject.SetActive(!hasActions);
            }

            if (!hasActions)
            {
                return;
            }

            int n = choices.Count;
            float gap = 0.03f;
            float cardW = (1f - gap * (n + 1)) / n;
            for (int i = 0; i < n; i++)
            {
                float minX = gap + i * (cardW + gap);
                ActionChoice captured = choices[i];
                SpawnCard(minX, minX + cardW, card => card.Bind(captured, () => OnActionSelectionPicked(captured)));
            }
        }

        /// <summary>事件 n 选一卡片：每个选项一张卡，点击回调选项序号。</summary>
        private void BuildEventCards(IReadOnlyList<string> options, Action<int> onPick)
        {
            ClearCards();
            if (_cardsContainer == null || _cardPrefab == null)
            {
                onPick?.Invoke(0);
                return;
            }

            int n = options?.Count ?? 0;
            _cardsContainer.gameObject.SetActive(n > 0);
            if (_skipButton != null)
            {
                _skipButton.gameObject.SetActive(false);
            }

            if (n == 0)
            {
                onPick?.Invoke(0);
                return;
            }

            float gap = 0.03f;
            float cardW = (1f - gap * (n + 1)) / n;
            for (int i = 0; i < n; i++)
            {
                float minX = gap + i * (cardW + gap);
                int index = i;
                string text = options[i];
                SpawnCard(minX, minX + cardW, card => card.BindEventOption(text, () => OnEventOptionPicked(index, onPick)));
            }
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

        /// <summary>在中部按归一化 [minX,maxX] 铺一张卡并执行绑定回调。</summary>
        private void SpawnCard(float minX, float maxX, Action<WeekEventCardView> bind)
        {
            WeekEventCardView card = Instantiate(_cardPrefab, _cardsContainer);
            var rect = (RectTransform)card.transform;
            Rect parentRect = _cardsContainer.rect;
            if (parentRect.width <= 1f || parentRect.height <= 1f)
            {
                Canvas.ForceUpdateCanvases();
                parentRect = _cardsContainer.rect;
            }

            Vector2 fallbackSize = rect.sizeDelta;
            float slotWidth = parentRect.width * (maxX - minX);
            float maxHeight = parentRect.height * 0.92f;
            float width = Mathf.Min(slotWidth, maxHeight * 0.67f);
            if (width <= 1f)
            {
                width = Mathf.Max(1f, fallbackSize.x);
            }

            float height = width / 0.67f;
            float centerX = parentRect.width * ((minX + maxX) * 0.5f - 0.5f);

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(centerX, 0f);
            rect.sizeDelta = new Vector2(width, height);

            bind?.Invoke(card);
            _cards.Add(card);
        }

        private void ClearCards()
        {
            KillPendingCardShow();
            foreach (WeekEventCardView card in _cards)
            {
                if (card != null)
                {
                    card.PlayHideThenDestroy();
                }
            }

            _cards.Clear();
        }

        /// <summary>玩家在中部选择了一个行动（null = 无行动可选时的「休息」）。</summary>
        private void OnActionSelectionPicked(ActionChoice choice)
        {
            _run?.ClearPendingActionChoices();
            HideCardsThenDestroy(() =>
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
            HideCardsThenDestroy(() =>
            {
                if (_actionSelectionPanel != null)
                {
                    _actionSelectionPanel.SetActive(false);
                }

                onPick?.Invoke(index);
            });
        }

        private void PlayShowCardsWhenReady()
        {
            KillPendingCardShow();
            if (_current != GameplayView.ActionSelect || _cards.Count == 0)
            {
                return;
            }

            if (GameApp.UI.HasUIForm(UIForms.CartoonSceneTransition))
            {
                _pendingCardShowTween = DOVirtual.DelayedCall(0.03f, PlayShowCardsWhenReady, true)
                    .SetUpdate(true);
                return;
            }

            foreach (WeekEventCardView card in _cards)
            {
                if (card != null && card.isActiveAndEnabled)
                {
                    card.PlayShow();
                }
            }
        }

        private void HideCardsThenDestroy(Action onHidden)
        {
            KillPendingCardShow();
            if (_cardsHideTween != null && _cardsHideTween.IsActive())
            {
                return;
            }

            var cards = new List<WeekEventCardView>(_cards);
            _cards.Clear();
            float hideDelay = PickEffectHold(cards);

            Sequence seq = DOTween.Sequence().SetUpdate(true);
            bool hasTween = false;
            foreach (WeekEventCardView card in cards)
            {
                if (card == null)
                {
                    continue;
                }

                Tween tween = card.PlayHideThenDestroy(hideDelay);
                if (tween == null)
                {
                    continue;
                }

                seq.Join(tween);
                hasTween = true;
            }

            if (!hasTween)
            {
                seq.Kill();
                onHidden?.Invoke();
                return;
            }

            _cardsHideTween = seq.OnComplete(() =>
            {
                _cardsHideTween = null;
                onHidden?.Invoke();
            });
        }

        private static float PickEffectHold(IReadOnlyList<WeekEventCardView> cards)
        {
            float hold = 0f;
            if (cards == null)
            {
                return hold;
            }

            for (int i = 0; i < cards.Count; i++)
            {
                WeekEventCardView card = cards[i];
                if (card != null)
                {
                    hold = Mathf.Max(hold, card.PickEffectHold);
                }
            }

            return hold;
        }

        private void KillPendingCardShow()
        {
            if (_pendingCardShowTween != null)
            {
                _pendingCardShowTween.Kill();
                _pendingCardShowTween = null;
            }
        }

        private void KillCardsHideTween()
        {
            if (_cardsHideTween != null)
            {
                _cardsHideTween.Kill();
                _cardsHideTween = null;
            }
        }

        /// <summary>隐藏常驻壳与中部内容（用于开局前 / 结算返回菜单前的清场）。</summary>
        private void HideHud()
        {
            _inBattle = false;
            _current = GameplayView.None;
            _stomachReturnView = GameplayView.None;
            _hasStomachActionReturnSnapshot = false;
            _stomachActionReturnTitle = null;

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

            _recipeView?.SetState(RecipeView.RecipeState.Hidden);
            SetFoodActionsVisible(false);
            SetViewStomachButtonLabel(ViewStomachLabel);

            if (_hudFrame != null)
            {
                _hudFrame.SetActive(false);
            }
        }

        private void OnSettingsClicked()
        {
            GameApp.UI.OpenUIForm(UIForms.Settings, UIForms.GroupDialog);
        }

        private void OnViewStomachClicked()
        {
            if (_current == GameplayView.StomachView)
            {
                OnStomachViewBack();
                return;
            }

            OpenStomachView();
        }

        private void SetViewStomachButtonLabel(string text)
        {
            if (_viewStomachButtonText == null && _viewStomachButton != null)
            {
                _viewStomachButtonText = _viewStomachButton.GetComponentInChildren<Text>(true);
            }

            if (_viewStomachButtonText != null)
            {
                _viewStomachButtonText.text = text;
            }
        }

        public void ShowRunResult(bool win, int total)
        {
            _world?.HideWorld();
            HideHud();
            GameApp.UI.OpenUIForm(UIForms.Result, UIForms.GroupDialog, new ResultFormData(win, total));
        }

        // —— 战斗 ——

        public void StartBattle(int requiredScore, string modifier, string key, ActionExecutionContext actionContext)
        {
            HideResultPanel();
            _session = _run.BuildBattleSession(requiredScore, modifier, key);
            // 常驻壳在战斗中持续显示并接管分数/道具/菜谱面板（棋盘/菜品仍在世界空间场景）。
            SwitchTo(GameplayView.Food);

            _world = BattleWorldController.Instance;
            if (_world == null)
            {
                Log.Error("BattleForm: battle scene controller not found (scene not loaded?).", Tag);
                return;
            }

            _world.Initialize(
                _run,
                _session,
                SetMessage,
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
            RefreshFoodActions();

            if (_world != null)
            {
                _world.PlaySettlement(result, () => OnSettlementComplete(result));
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
            }

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

            var data = new DishDetailData(inst.Def, _run.Database, inst.SkillIds, inst.FlavorId);
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

            ActiveItemUseResult result = ActiveItemEffectRegistry.TryUse(_session, item);
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
            // 提示展示由战斗世界负责，此回调保留以满足接口契约。
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
