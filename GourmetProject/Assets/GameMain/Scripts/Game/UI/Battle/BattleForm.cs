using System;
using System.Collections.Generic;
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
    /// 周循环模型（行动轴）：每周随机一条行动轴 → 反复「n 选一行动（最多 3 个）」推进天数 → 经过节点按序触发（利息/商店/Boss/事件）
    /// → 行动轴走完进入下一周；美食行动与 Boss 节点会启动局内战斗（复用 BattleWorldController / BattleSession）。
    /// 失败：常规美食/Boss 挑战不达标或事件直接失败。胜利：通关最终周 Boss。
    /// </summary>
    public sealed class BattleForm : UGuiForm, IWeekLoopView
    {
        private const string Tag = "Battle";
        private const int PassiveSlotCapacity = 10;
        private const int PassiveSlotColumns = 2;

        /// <summary>当前打开的战斗界面，供各弹窗回调推进周循环。</summary>
        public static BattleForm Active { get; private set; }

        [Header("HUD Frame")]
        [SerializeField] private GameObject _hudFrame;
        [SerializeField] private GameObject _backdrop;

        [Header("Left Column")]
        [SerializeField] private Text _weekText;
        [SerializeField] private Text _goldText;
        [SerializeField] private Text _scoreReqText;
        [SerializeField] private Text _foodAdjustText;
        [SerializeField] private Button _viewStomachButton;
        [SerializeField] private Button _settingsButton;

        [Header("Action Axis")]
        [SerializeField] private ActionAxisBar _actionAxisBar;

        [Header("Action Selection (center)")]
        [SerializeField] private GameObject _actionSelectionPanel;
        [SerializeField] private RectTransform _cardsContainer;
        [SerializeField] private WeekEventCardView _cardPrefab;
        [SerializeField] private Button _skipButton;

        [Header("Right Column - Items")]
        [SerializeField] private RectTransform _passiveItemsContainer;
        [SerializeField] private RunItemSlotView _itemSlotPrefab;
        [SerializeField] private RunItemSlotView[] _activeItemSlots;

        [Header("Recipe Drawer")]
        [SerializeField] private RecipeDrawer _recipeDrawer;

        private readonly List<RunItemSlotView> _passiveSlots = new();
        private readonly List<WeekEventCardView> _cards = new();
        private bool _inBattle;

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
                _viewStomachButton.onClick.AddListener(OnViewStomachClicked);
            }

            if (_skipButton != null)
            {
                _skipButton.onClick.AddListener(() => OnActionSelectionPicked(null));
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

        /// <summary>WeekMapForm 选择行动后回调（null = 无行动可选时的「休息」）。</summary>
        public void OnActionPicked(ActionChoice choice)
        {
            _loop?.OnActionPicked(choice);
        }

        /// <summary>ShopForm 关闭时回调，继续编排。</summary>
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

        /// <summary>周循环请求「n 选一行动」：在常驻 HUD 中部就地展示行动选择（不再打开独立弹层）。</summary>
        public void OpenWeekMap()
        {
            ShowActionSelection();
        }

        public void OpenShop()
        {
            GameApp.UI.OpenUIForm(UIForms.Shop, UIForms.GroupDialog);
        }

        // —— 常驻 HUD 框 + 行动选择（中部内容区）——

        /// <summary>展示行动选择：显示常驻框、刷新左右栏与行动轴，铺出当前行动组的 n 选一卡片。</summary>
        private void ShowActionSelection()
        {
            if (_run == null)
            {
                return;
            }

            _inBattle = false;

            if (_hudFrame != null)
            {
                _hudFrame.SetActive(true);
            }

            if (_backdrop != null)
            {
                _backdrop.SetActive(true);
            }

            RefreshPersistent();
            _actionAxisBar?.Build(_run);

            if (_recipeDrawer != null)
            {
                _recipeDrawer.ConfigureCollapsible(false);
                BuildRecipeDrawer();
            }

            if (_actionSelectionPanel != null)
            {
                _actionSelectionPanel.SetActive(true);
            }

            BuildActionCards();
        }

        /// <summary>进入战斗：常驻框覆盖在世界空间棋盘之上，隐藏行动选择与白底，菜谱抽屉锁定展开并接上菜。</summary>
        private void ShowBattleHud()
        {
            _inBattle = true;

            if (_hudFrame != null)
            {
                _hudFrame.SetActive(true);
            }

            // 关闭全屏白底，让世界空间棋盘透出；隐藏行动选择卡片与行动轴。
            if (_backdrop != null)
            {
                _backdrop.SetActive(false);
            }

            if (_actionSelectionPanel != null)
            {
                _actionSelectionPanel.SetActive(false);
            }

            _actionAxisBar?.Build(_run);
            RefreshPersistent();

            if (_recipeDrawer != null)
            {
                _recipeDrawer.ConfigureLockedOpen();
                BuildBattleRecipe();
            }
        }

        /// <summary>战斗态菜谱抽屉：每本菜谱一条，点击从该菜谱上菜（触发世界空间上菜动画）。</summary>
        private void BuildBattleRecipe()
        {
            if (_recipeDrawer == null || _session == null)
            {
                return;
            }

            var entries = new List<RecipeDrawer.EntryData>();
            for (int i = 0; i < _session.Slots.Count; i++)
            {
                RecipeSlot slot = _session.Slots[i];
                int slotIndex = i;
                bool interactable = !_session.IsSettled && !slot.IsEmpty;
                entries.Add(new RecipeDrawer.EntryData(
                    $"菜谱{i + 1}",
                    $"剩 {slot.Count}",
                    interactable,
                    () => ServeFromRecipe(slotIndex)));
            }

            _recipeDrawer.SetEntries(entries);
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

        /// <summary>菜谱抽屉展示态（阶段一）：列出本局奖励菜谱池（BonusDishIds），完整菜谱在战斗中查看。</summary>
        private void BuildRecipeDrawer()
        {
            if (_recipeDrawer == null || _run == null)
            {
                return;
            }

            cfg.Tables tables = GameApp.Config.Tables;
            var entries = new List<RecipeDrawer.EntryData>();
            foreach (string dishId in _run.BonusDishIds)
            {
                entries.Add(new RecipeDrawer.EntryData(DishDisplayName(tables, dishId), string.Empty, false, null));
            }

            _recipeDrawer.SetEntries(entries);
        }

        private static string DishDisplayName(cfg.Tables tables, string dishId)
        {
            cfg.DishVariant variant = tables.TbDishVariant.GetOrDefault(dishId);
            if (variant != null)
            {
                cfg.DishBase baseDish = tables.TbDishBase.GetOrDefault(variant.BaseId);
                if (baseDish != null)
                {
                    return baseDish.Name;
                }
            }

            return dishId;
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
                SpawnCard(choices[i], minX, minX + cardW);
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

        private void SpawnCard(ActionChoice choice, float minX, float maxX)
        {
            WeekEventCardView card = Instantiate(_cardPrefab, _cardsContainer);
            var rect = (RectTransform)card.transform;
            rect.anchorMin = new Vector2(minX, 0f);
            rect.anchorMax = new Vector2(maxX, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            ActionChoice captured = choice;
            card.Bind(choice, () => OnActionSelectionPicked(captured));
            _cards.Add(card);
        }

        private void ClearCards()
        {
            foreach (WeekEventCardView card in _cards)
            {
                if (card != null)
                {
                    Destroy(card.gameObject);
                }
            }

            _cards.Clear();
        }

        /// <summary>玩家在中部选择了一个行动（null = 无行动可选时的「休息」）。</summary>
        private void OnActionSelectionPicked(ActionChoice choice)
        {
            _run?.ClearPendingActionChoices();
            ClearCards();
            if (_actionSelectionPanel != null)
            {
                _actionSelectionPanel.SetActive(false);
            }

            _loop?.OnActionPicked(choice);
        }

        /// <summary>隐藏常驻框与行动选择（用于结算/返回菜单前的清场）。</summary>
        private void HideHud()
        {
            _inBattle = false;

            if (_actionSelectionPanel != null)
            {
                _actionSelectionPanel.SetActive(false);
            }

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
            ShowNotice("查看胃", BuildStomachPreviewText(), null);
        }

        private string BuildStomachPreviewText()
        {
            if (_run == null)
            {
                return "当前没有运行数据。";
            }

            Board board = _run.BuildStomachPreviewBoard(_run.WeekModifier);
            var text = new System.Text.StringBuilder();
            text.AppendLine($"胃容量：{board.CellCapacity} 格");
            text.AppendLine($"胃部碎片：{_run.StomachFragmentIds.Count}");
            text.AppendLine();

            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    text.Append(board.Exists(new GridPos(x, y)) ? "■" : "□");
                }

                text.AppendLine();
            }

            return text.ToString();
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
            // 常驻框在战斗中持续显示并接管分数/道具/菜谱面板（棋盘/菜品仍在世界空间场景）。
            ShowBattleHud();

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
                OnEatClicked,
                OnOverviewClicked,
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

            RunPersistence.Save(_run);
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
