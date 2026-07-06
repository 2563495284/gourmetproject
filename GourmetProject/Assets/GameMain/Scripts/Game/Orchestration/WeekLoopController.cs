using System;
using System.Collections.Generic;
using System.Globalization;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Common;
using GourmetProject.Gameplay.Scoring;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Orchestration
{
    public interface IWeekLoopView
    {
        int LastBattleTotal { get; }

        void HideBattleWorld();

        void HideResultPanel();

        void OpenWeekMap();

        void OpenShop();

        /// <summary>行动轴节点卡片：先展示节点卡，玩家点击后再执行节点效果。</summary>
        void ShowTimelineNodeCard(cfg.TimelineNode node, Action onPick);

        void StartBattle(int requiredScore, string modifier, string key, ActionExecutionContext actionContext);

        void ShowNotice(string title, string message, Action onContinue);

        /// <summary>事件「n 选一」：在常驻壳中部就地铺出事件选项卡（与行动选择共用一套中部卡片 UI）。</summary>
        void ShowEventChoices(string title, string desc, IReadOnlyList<string> options, Action<int> onPick);

        void ShowRunResult(bool win, int total);
    }

    /// <summary>
    /// 局外周循环编排器：行动选择、时间轴节点、事件/商店/战斗续接都在这里推进。
    /// </summary>
    public sealed class WeekLoopController
    {
        private readonly GameRun _run;
        private readonly IWeekLoopView _view;

        private Queue<cfg.TimelineNode> _pendingNodes;
        private Action _afterNodes;
        private Action _afterBattleWin;
        private Action<ScoreResult> _afterBattleLose;
        private Action _afterShop;

        public WeekLoopController(GameRun run, IWeekLoopView view)
        {
            _run = run;
            _view = view;
        }

        public ActionExecutionContext CurrentBattleActionContext { get; private set; }

        /// <summary>进入（或继续）一周：随机/沿用行动轴后开始行动循环。</summary>
        public void BeginWeek()
        {
            _view.HideBattleWorld();
            _view.HideResultPanel();

            // 新周或上一周已走完 → 随机一条新行动轴；中途读档则沿用存档里的行动轴。
            if (string.IsNullOrEmpty(_run.CurrentTimelineId) || _run.CurrentDay >= _run.TimelineLengthDays)
            {
                _run.RequiredScoreOverride = -1;
                IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Map, $"w{_run.WeekIndex}");
                TimelineService.RollWeekTimeline(_run, rng);
            }

            RunPersistence.Save(_run);
            PromptNextAction();
        }

        /// <summary>行动轴未走完则弹「n 选一行动」；走完则进入下一周。</summary>
        public void PromptNextAction()
        {
            if (TimelineService.IsWeekFinished(_run))
            {
                EndWeek();
                return;
            }

            _view.HideBattleWorld();
            _view.HideResultPanel();
            _view.OpenWeekMap();
        }

        /// <summary>WeekMapForm 选择行动后回调（null = 无行动可选时的「休息」）。</summary>
        public void OnActionPicked(ActionChoice choice)
        {
            if (choice == null)
            {
                float restPrev = TimelineService.AdvanceDays(_run, 1f);
                _run.AdvanceActionStep();
                RunPersistence.Save(_run);
                ResolveNodes(restPrev, PromptNextAction);
                return;
            }

            ActionExecutionContext context = choice.ToExecutionContext();
            if (!context.IsValid)
            {
                float prevDay = _run.CurrentDay;
                _run.AdvanceActionStep();
                RunPersistence.Save(_run);
                ResolveNodes(prevDay, PromptNextAction);
                return;
            }

            IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Effect, $"exec_r{context.RunStepIndex}_w{_run.WeekIndex}_s{context.StepIndex}_{context.ActionGroupId}_{context.Action.Id}");
            ActionOutcome outcome = ActionExecutor.Execute(_run, context, rng);

            // 进入行动时不推进天数/步数、不存档；只有玩家明确结算（商店退出、事件选完、战斗结算、
            // 通知点继续）时才 Commit（推进天数/步数 + 标记已用）并存档。中途放弃/退出游戏则该行动不消耗。
            bool committed = false;
            float committedPrevDay = _run.CurrentDay;

            void Commit()
            {
                if (committed)
                {
                    return;
                }

                committed = true;
                committedPrevDay = ActionExecutor.Commit(_run, context);
                RunPersistence.Save(_run);
            }

            void CommitAndResolveNodes()
            {
                Commit();
                ResolveNodes(committedPrevDay, PromptNextAction);
            }

            switch (outcome.Kind)
            {
                case ActionOutcomeKind.Immediate:
                    _view.ShowNotice(context.Action.Name, outcome.Feedback, CommitAndResolveNodes);
                    break;
                case ActionOutcomeKind.Shop:
                    OpenShopThen(CommitAndResolveNodes);
                    break;
                case ActionOutcomeKind.Event:
                    ResolveEventById(outcome.EventId, CommitAndResolveNodes);
                    break;
                case ActionOutcomeKind.Battle:
                    StartBattle(outcome.RequiredScore, outcome.Modifier, outcome.BattleKey, false, null,
                        CommitAndResolveNodes,
                        context);
                    break;
            }
        }

        /// <summary>ShopForm 关闭时回调，继续编排。</summary>
        public void OnShopClosed()
        {
            Action cb = _afterShop;
            _afterShop = null;
            cb?.Invoke();
        }

        public void OnBattleSettled(ScoreResult result, bool isWin)
        {
            // 结算演出已放完 → 立即退出美食态，隐藏世界棋盘与其专属按钮（总览/吃/涂鸦）。
            // 否则战斗后到下一次 PromptNextAction 之间的发奖 / 事件 / 利息 / 通知等弹层背后，
            // 美食态按钮会一直残留（事件选择时按钮仍显示的根因就在这里）。
            _view.HideBattleWorld();

            if (isWin)
            {
                // 达标：发奖（不推进周），奖励确认后继续编排。
                GameApp.UI.OpenUIForm(UIForms.Reward, UIForms.GroupDialog);
                return;
            }

            if (_afterBattleLose != null)
            {
                Action<ScoreResult> cb = _afterBattleLose;
                _afterBattleLose = null;
                _afterBattleWin = null;
                CurrentBattleActionContext = null;
                cb.Invoke(result);
            }
            else
            {
                // 常规美食/Boss 挑战不达标即失败；事件战斗可通过 onLose 覆盖为惩罚后继续。
                _view.ShowRunResult(false, result.Total);
            }
        }

        /// <summary>RewardForm 发奖确认后回调：继续战斗后的编排续接。</summary>
        public void OnRewardConfirmed()
        {
            Action cb = _afterBattleWin;
            _afterBattleWin = null;
            _afterBattleLose = null;
            CurrentBattleActionContext = null;
            cb?.Invoke();
        }

        /// <summary>行动轴走完：推进到下一周（最终周胜利由 Boss 节点判定）。</summary>
        private void EndWeek()
        {
            _run.WeekIndex++;
            _run.RequiredScoreOverride = -1;
            RunPersistence.Save(_run);
            BeginWeek();
        }

        /// <summary>把天数游标格式化为跨语言环境稳定的随机 key 片段（一位小数）。</summary>
        private static string DayKey(float currentDay)
        {
            return currentDay.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private void ResolveNodes(float prevDay, Action onDone)
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
                    _view.ShowTimelineNodeCard(node, () => OpenShopThen(ProcessNextNode));
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
            _view.ShowNotice("收取利息", msg, ProcessNextNode);
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
                IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Event, $"node_w{_run.WeekIndex}_d{node.Day}");
                ev = EventService.RollEvent(_run, rng);
            }

            ResolveEvent(ev, ProcessNextNode);
        }

        private void HandleBossNode(cfg.TimelineNode node)
        {
            // key 含节点唯一 id：同一周若存在多个 Boss 节点，各自独立抽取，避免共用同一条 boss 流按调用顺序续掷。
            IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Boss, $"w{_run.WeekIndex}_{node.Id}");
            cfg.Boss boss = BossService.RollBoss(_run, rng, node.PayloadParam);
            if (boss == null)
            {
                ProcessNextNode();
                return;
            }

            int required = _run.ComputeBossRequiredScore(boss.ScoreProfileId);
            string key = $"boss_w{_run.WeekIndex}_{boss.Id}";
            _view.ShowNotice($"Boss：{boss.Name}", $"目标分 {required}，准备应战！", () =>
                StartBattle(required, boss.Modifier, key, true, boss.Id, () =>
                {
                    _run.MarkBossCompleted(boss.Id);
                    RunPersistence.Save(_run);
                    if (IsFinalBossVictory(boss))
                    {
                        ClearPendingNodes();
                        OnVictory();
                    }
                    else
                    {
                        ClearPendingNodes();
                        EndWeek();
                    }
                }, null));
        }

        private bool IsFinalBossVictory(cfg.Boss boss)
        {
            return boss != null && !_run.IsEndless && _run.WeekIndex >= _run.TotalWeeks && boss.Week == _run.TotalWeeks;
        }

        private void ResolveEventById(string eventId, Action onDone)
        {
            cfg.Tables tables = _run.Tables ?? GameApp.Config.Tables;
            cfg.GameEvent ev;
            if (string.IsNullOrEmpty(eventId))
            {
                IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Event, $"action_w{_run.WeekIndex}_d{DayKey(_run.CurrentDay)}_s{_run.ActionStepIndex}");
                ev = EventService.RollEvent(_run, rng);
            }
            else
            {
                ev = tables.TbEvent.GetOrDefault(eventId);
            }

            ResolveEvent(ev, onDone);
        }

        private void ResolveEvent(cfg.GameEvent ev, Action onDone)
        {
            if (ev == null)
            {
                onDone?.Invoke();
                return;
            }

            IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Event, $"resolve_w{_run.WeekIndex}_d{DayKey(_run.CurrentDay)}_{ev.Id}");
            List<cfg.EventOption> options = EventService.GetOptions(ev.Id);
            if (options.Count == 0)
            {
                EventResolveResult result = EventService.ResolveImmediate(_run, ev, rng);
                if (ShouldSaveEventResultImmediately(result))
                {
                    RunPersistence.Save(_run);
                }

                ContinueEventResult(ev.Name, ev.Id, result, onDone);
                return;
            }

            // 事件选项与「行动 n 选一」共用同一套中部卡片 UI（不再走独立 ConfirmDialog 弹层）。
            var optionTexts = new List<string>(options.Count);
            foreach (cfg.EventOption option in options)
            {
                optionTexts.Add(option.Text);
            }

            _view.ShowEventChoices(ev.Name, ev.Desc, optionTexts, index =>
            {
                if (index < 0 || index >= options.Count)
                {
                    onDone?.Invoke();
                    return;
                }

                ApplyEventOption(ev, options[index], rng, onDone);
            });
        }

        private void ApplyEventOption(cfg.GameEvent ev, cfg.EventOption option, IRandomStream rng, Action onDone)
        {
            EventResolveResult result = EventService.ResolveOption(_run, ev, option, rng);
            if (ShouldSaveEventResultImmediately(result))
            {
                RunPersistence.Save(_run);
            }

            ContinueEventResult(ev.Name, ev.Id, result, onDone);
        }

        private static bool ShouldSaveEventResultImmediately(EventResolveResult result)
        {
            return result == null || result.FollowUpKind != EventFollowUpKind.Shop;
        }

        private void ContinueEventResult(string title, string eventId, EventResolveResult result, Action onDone)
        {
            if (result == null)
            {
                onDone?.Invoke();
                return;
            }

            switch (result.FollowUpKind)
            {
                case EventFollowUpKind.Battle:
                {
                    string key = $"event_battle_w{_run.WeekIndex}_d{DayKey(_run.CurrentDay)}_{eventId}";
                    Action start = () => StartBattle(
                        result.RequiredScore,
                        result.Modifier,
                        key,
                        false,
                        null,
                        onDone,
                        null,
                        battleResult => OnEventBattleFailed(result, battleResult, onDone));
                    _view.ShowNotice(title, result.Feedback, start);
                    break;
                }

                case EventFollowUpKind.Shop:
                    OpenShopThen(onDone);
                    break;

                case EventFollowUpKind.GameOver:
                    _view.ShowNotice(title, result.Feedback, () =>
                    {
                        ClearPendingNodes();
                        _view.ShowRunResult(false, 0);
                    });
                    break;

                case EventFollowUpKind.Victory:
                    _view.ShowNotice(title, result.Feedback, () =>
                    {
                        ClearPendingNodes();
                        OnVictory();
                    });
                    break;

                default:
                    _view.ShowNotice(title, result.Feedback, onDone);
                    break;
            }
        }

        private void OnEventBattleFailed(EventResolveResult result, ScoreResult battleResult, Action onDone)
        {
            _view.HideBattleWorld();
            int score = battleResult?.Total ?? 0;
            string message = $"事件挑战未达标（{score}/{result.RequiredScore}），本次事件继续结算。";
            _view.ShowNotice("事件挑战失败", message, onDone);
        }

        private void ClearPendingNodes()
        {
            _pendingNodes = null;
            _afterNodes = null;
        }

        private void OpenShopThen(Action onClose)
        {
            _afterShop = onClose;
            _view.OpenShop();
        }

        private void StartBattle(
            int requiredScore,
            string modifier,
            string key,
            bool isBoss,
            string bossId,
            Action onWin,
            ActionExecutionContext actionContext = null,
            Action<ScoreResult> onLose = null)
        {
            _afterBattleWin = onWin;
            _afterBattleLose = onLose;
            CurrentBattleActionContext = actionContext;
            _view.HideResultPanel();
            _view.StartBattle(requiredScore, modifier, key, actionContext);
        }

        private void OnVictory()
        {
            _view.HideBattleWorld();
            _view.ShowRunResult(true, _view.LastBattleTotal);
        }
    }
}
