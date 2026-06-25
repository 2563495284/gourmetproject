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
using GourmetProject.Gameplay.Scoring;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using Log = GourmetProject.Core.Diagnostics.Log;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
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

        /// <summary>当前打开的战斗界面，供各弹窗回调推进周循环。</summary>
        public static BattleForm Active { get; private set; }

        [SerializeField] private GameObject _resultPanel;
        [SerializeField] private Text _resultText;
        [SerializeField] private Button _resultButton;

        private GameRun _run;
        private BattleSession _session;
        private BattleWorldController _world;

        private WeekLoopController _loop;
        private bool _victory;

        public GameRun Run => _run;
        public BattleSession Session => _session;
        public ActionExecutionContext CurrentBattleActionContext => _loop?.CurrentBattleActionContext;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            _resultButton.onClick.AddListener(OnResultConfirm);
            _resultPanel.SetActive(false);
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
        public void OnActionPicked(cfg.GameAction action)
        {
            _loop?.OnActionPicked(action);
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
            HideResult();
        }

        public void OpenWeekMap()
        {
            GameApp.UI.OpenUIForm(UIForms.WeekMap, UIForms.GroupDialog);
        }

        public void OpenShop()
        {
            GameApp.UI.OpenUIForm(UIForms.Shop, UIForms.GroupDialog);
        }

        public void ShowRunResult(bool win, int total)
        {
            _victory = win;
            _world?.HideWorld();
            ShowResult(win, total);
        }

        // —— 战斗 ——

        public void StartBattle(int requiredScore, string modifier, string key, ActionExecutionContext actionContext)
        {
            HideResult();
            _session = _run.BuildBattleSession(requiredScore, modifier, key);

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
            RefreshAll();
            _loop?.OnBattleSettled(result, _session != null && _session.IsWin);
        }

        private void RefreshAll()
        {
            _world?.RefreshAll();
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

            var data = new DishDetailData(inst.Def, _run.Database, inst.TagIds);
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

        // —— 结果壳（胜利/失败）——

        private void OnResultConfirm()
        {
            if (_victory)
            {
                // 通关：保留存档（可继续无尽），返回菜单。
                RunPersistence.Save(_run);
            }
            else
            {
                RunPersistence.Delete();
            }

            GameplayFlowSignal.RequestReturnToMenu();
        }

        private void ShowResult(bool win, int total)
        {
            _resultPanel.SetActive(true);
            int target = _session?.RequiredScore ?? _run.RequiredScore;
            SettlementSummary summary = SettlementService.Build(_run, win, total, target);
            _resultText.text = $"{summary.Title}\n\n{summary.Body}";

            Text resultLabel = _resultButton.GetComponentInChildren<Text>();
            if (resultLabel != null)
            {
                resultLabel.text = summary.ButtonLabel;
            }
        }

        private void HideResult()
        {
            if (_resultPanel != null)
            {
                _resultPanel.SetActive(false);
            }
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
