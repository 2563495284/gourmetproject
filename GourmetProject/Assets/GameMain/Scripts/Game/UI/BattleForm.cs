using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Gameplay;
using GourmetProject.Game.Gameplay.Presentation;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Scoring;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 局外周循环编排枢纽 + 局内战斗结果壳。
    /// 周循环模型（行动轴）：每周随机一条行动轴 → 反复「三选一行动」推进天数 → 经过节点按序触发（利息/商店/Boss/事件）
    /// → 行动轴走完进入下一周；美食行动与 Boss 节点会启动局内战斗（复用 BattleWorldController / BattleSession）。
    /// 失败：任意美食挑战不达标。胜利：通关最终周 Boss。
    /// </summary>
    public sealed class BattleForm : UGuiForm
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

        // —— 编排续接 ——
        private Queue<cfg.TimelineNode> _pendingNodes;
        private Action _afterNodes;
        private Action _afterBattleWin;
        private Action _afterShop;
        private bool _victory;

        public GameRun Run => _run;
        public BattleSession Session => _session;

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
            BeginWeek();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            if (Active == this)
            {
                Active = null;
            }

            _world?.HideWorld();
            base.OnClose(isShutdown, userData);
        }

        // —— 周循环编排 ——

        /// <summary>进入（或继续）一周：随机/沿用行动轴后开始行动循环。</summary>
        public void BeginWeek()
        {
            _run.RequiredScoreOverride = -1;
            _session = null;
            _world?.HideWorld();
            HideResult();

            // 新周或上一周已走完 → 随机一条新行动轴；中途读档则沿用存档里的行动轴。
            if (string.IsNullOrEmpty(_run.CurrentTimelineId) || _run.CurrentDay >= _run.TimelineLengthDays)
            {
                IRandomStream rng = GameApp.Random.Stream($"timeline_w{_run.WeekIndex}");
                TimelineService.RollWeekTimeline(_run, rng);
            }

            RunPersistence.Save(_run);
            PromptNextAction();
        }

        /// <summary>行动轴未走完则弹「三选一行动」；走完则进入下一周。</summary>
        public void PromptNextAction()
        {
            if (TimelineService.IsWeekFinished(_run))
            {
                EndWeek();
                return;
            }

            _world?.HideWorld();
            HideResult();
            GameApp.UI.OpenUIForm(UIForms.WeekMap, UIForms.GroupDialog);
        }

        /// <summary>WeekMapForm 选择行动后回调（null = 无行动可选时的「休息」）。</summary>
        public void OnActionPicked(cfg.GameAction action)
        {
            if (action == null)
            {
                int restPrev = TimelineService.AdvanceDays(_run, 1);
                RunPersistence.Save(_run);
                ResolveNodes(restPrev, PromptNextAction);
                return;
            }

            int prevDay = _run.CurrentDay;
            IRandomStream rng = GameApp.Random.Stream($"action_exec_w{_run.WeekIndex}_d{prevDay}_{action.Id}");
            ActionOutcome outcome = ActionExecutor.Execute(_run, action, rng);
            RunPersistence.Save(_run);

            switch (outcome.Kind)
            {
                case ActionOutcomeKind.Immediate:
                    ShowNotice(action.Name, outcome.Feedback, () => ResolveNodes(prevDay, PromptNextAction));
                    break;
                case ActionOutcomeKind.Shop:
                    OpenShopThen(() => ResolveNodes(prevDay, PromptNextAction));
                    break;
                case ActionOutcomeKind.Event:
                    ResolveEventById(outcome.EventId, () => ResolveNodes(prevDay, PromptNextAction));
                    break;
                case ActionOutcomeKind.Battle:
                    StartBattle(outcome.RequiredScore, outcome.Modifier, outcome.BattleKey, false, null,
                        () => ResolveNodes(prevDay, PromptNextAction));
                    break;
            }
        }

        /// <summary>行动轴走完：推进到下一周（最终周胜利由 Boss 节点判定）。</summary>
        private void EndWeek()
        {
            _run.WeekIndex++;
            _run.RequiredScoreOverride = -1;
            RunPersistence.Save(_run);
            BeginWeek();
        }

        // —— 节点结算 ——

        private void ResolveNodes(int prevDay, Action onDone)
        {
            _pendingNodes = new Queue<cfg.TimelineNode>(TimelineService.CollectPassedNodes(_run, prevDay, _run.CurrentDay));
            _afterNodes = onDone;
            ProcessNextNode();
        }

        private void ProcessNextNode()
        {
            if (_pendingNodes == null || _pendingNodes.Count == 0)
            {
                RunPersistence.Save(_run);
                Action cb = _afterNodes;
                _afterNodes = null;
                cb?.Invoke();
                return;
            }

            cfg.TimelineNode node = _pendingNodes.Dequeue();
            _run.MarkNodeTriggered(node.Id);

            switch (node.NodeType)
            {
                case cfg.TimelineNodeType.Interest:
                    HandleInterestNode(node);
                    break;
                case cfg.TimelineNodeType.Shop:
                    OpenShopThen(ProcessNextNode);
                    break;
                case cfg.TimelineNodeType.Event:
                    HandleEventNode(node);
                    break;
                case cfg.TimelineNodeType.Boss:
                    HandleBossNode(node);
                    break;
                default:
                    ProcessNextNode();
                    break;
            }
        }

        private void HandleInterestNode(cfg.TimelineNode node)
        {
            int threshold = (int)node.PayloadValue;
            int goldPer = int.TryParse(node.PayloadParam, out int gp) ? gp : 1;
            int gold = TimelineMath.Interest(_run.Gold, threshold, goldPer);
            _run.Gold += gold;
            RunPersistence.Save(_run);

            string msg = gold > 0
                ? $"利息结算：金币 +{gold}（每满 {threshold} 金币得 {goldPer}），当前 {_run.Gold}。"
                : $"金币不足 {threshold}，本次没有利息。";
            ShowNotice("收取利息", msg, ProcessNextNode);
        }

        private void HandleEventNode(cfg.TimelineNode node)
        {
            cfg.GameEvent ev;
            if (!string.IsNullOrEmpty(node.PayloadParam))
            {
                ev = GameApp.Config.Tables.TbEvent.GetOrDefault(node.PayloadParam);
            }
            else
            {
                IRandomStream rng = GameApp.Random.Stream($"event_node_w{_run.WeekIndex}_d{node.Day}");
                ev = EventService.RollEvent(_run, rng);
            }

            ResolveEvent(ev, ProcessNextNode);
        }

        private void HandleBossNode(cfg.TimelineNode node)
        {
            IRandomStream rng = GameApp.Random.Stream($"boss_w{_run.WeekIndex}");
            cfg.Boss boss = BossService.RollBoss(_run, rng, node.PayloadParam);
            if (boss == null)
            {
                ProcessNextNode();
                return;
            }

            int required = _run.ComputeBossRequiredScore(boss.ScoreProfileId);
            string key = $"boss_w{_run.WeekIndex}_{boss.Id}";
            ShowNotice($"Boss：{boss.Name}", $"目标分 {required}，准备应战！", () =>
                StartBattle(required, boss.Modifier, key, true, boss.Id, () =>
                {
                    _run.MarkBossCompleted(boss.Id);
                    RunPersistence.Save(_run);
                    if (_run.WeekIndex >= _run.TotalWeeks && !_run.IsEndless)
                    {
                        OnVictory();
                    }
                    else
                    {
                        ProcessNextNode();
                    }
                }));
        }

        // —— 事件 ——

        private void ResolveEventById(string eventId, Action onDone)
        {
            cfg.GameEvent ev = string.IsNullOrEmpty(eventId) ? null : GameApp.Config.Tables.TbEvent.GetOrDefault(eventId);
            ResolveEvent(ev, onDone);
        }

        private void ResolveEvent(cfg.GameEvent ev, Action onDone)
        {
            if (ev == null)
            {
                onDone?.Invoke();
                return;
            }

            IRandomStream rng = GameApp.Random.Stream($"event_resolve_w{_run.WeekIndex}_d{_run.CurrentDay}_{ev.Id}");
            List<cfg.EventOption> options = EventService.GetOptions(ev.Id);
            if (options.Count == 0)
            {
                string fb = EventService.ResolveImmediate(_run, ev, rng);
                RunPersistence.Save(_run);
                ShowNotice(ev.Name, fb, onDone);
                return;
            }

            cfg.EventOption a = options[0];
            cfg.EventOption b = options.Count > 1 ? options[1] : null;
            var data = new ConfirmDialogData
            {
                Title = ev.Name,
                Message = ev.Desc,
                ConfirmText = a.Text,
                OnConfirm = () => ApplyEventOption(ev, a, rng, onDone),
                CancelText = b != null ? b.Text : string.Empty,
                OnCancel = b != null ? (Action)(() => ApplyEventOption(ev, b, rng, onDone)) : null,
            };
            GameApp.UI.OpenUIForm(UIForms.ConfirmDialog, UIForms.GroupDialog, data);
        }

        private void ApplyEventOption(cfg.GameEvent ev, cfg.EventOption option, IRandomStream rng, Action onDone)
        {
            string fb = EventService.ResolveOption(_run, ev, option, rng);
            RunPersistence.Save(_run);
            ShowNotice(ev.Name, fb, onDone);
        }

        // —— 商店 ——

        private void OpenShopThen(Action onClose)
        {
            _afterShop = onClose;
            GameApp.UI.OpenUIForm(UIForms.Shop, UIForms.GroupDialog);
        }

        /// <summary>ShopForm 关闭时回调，继续编排。</summary>
        public void OnShopClosed()
        {
            Action cb = _afterShop;
            _afterShop = null;
            cb?.Invoke();
        }

        // —— 战斗 ——

        private void StartBattle(int requiredScore, string modifier, string key, bool isBoss, string bossId, Action onWin)
        {
            _afterBattleWin = onWin;
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

            if (_session.IsWin)
            {
                // 达标：发奖（不推进周），奖励确认后继续编排。
                GameApp.UI.OpenUIForm(UIForms.Reward, UIForms.GroupDialog);
            }
            else
            {
                // 任意美食挑战不达标即失败。
                ShowResult(false, result.Total);
            }
        }

        /// <summary>RewardForm 发奖确认后回调：继续战斗后的编排续接。</summary>
        public void OnRewardConfirmed()
        {
            Action cb = _afterBattleWin;
            _afterBattleWin = null;
            cb?.Invoke();
        }

        private void OnVictory()
        {
            _victory = true;
            _world?.HideWorld();
            ShowResult(true, _session?.LastResult?.Total ?? 0);
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

            RunItemState state = _run.GetItemState(itemId);
            if (state == null || state.Count <= 0)
            {
                _world?.ShowMessage($"{item.Name}：没有可用数量。");
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

        private void RefreshAll()
        {
            _world?.RefreshAll();
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

        private void ShowNotice(string title, string message, Action onContinue)
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
