using System;
using System.Collections.Generic;
using System.Globalization;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Gameplay.Scoring;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Orchestration
{
    public interface IWeekLoopView
    {
        int LastBattleTotal { get; }

        void HideBattleWorld();

        void RestorePendingRewardBattleView();

        void HideResultPanel();

        void OpenWeekMap();

        void OpenShop();

        /// <summary>行动轴节点卡片：先展示节点卡，玩家点击后再执行节点效果。</summary>
        void ShowTimelineNodeCard(cfg.TimelineNode node, int? interestMaxGain, Action onPick);

        void ShowTimelineNodeSkipped(cfg.TimelineNode node, Action onDone);

        void StartBattle(int requiredScore, string modifier, string key, ActionExecutionContext actionContext);

        void ShowNotice(string title, string message, Action onContinue);

        void OpenEventRecipeDishDelete(
            GameRun run,
            string title,
            Action onCancel,
            Action<ActiveTarget> onTargetConfirmed,
            Action onChanged);

        /// <summary>事件页：在常驻壳中部展示事件背景、正文、结果、后续选项与结束按钮。</summary>
        void ShowEventPage(
            string title,
            string desc,
            string result,
            string bgSprite,
            IReadOnlyList<string> options,
            IReadOnlyList<bool> optionEnabled,
            bool showEndButton,
            string endButtonText,
            Action<int> onPick,
            Action onEnd);

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
        private bool _currentBattleIsBoss;

        public WeekLoopController(GameRun run, IWeekLoopView view)
        {
            _run = run;
            _view = view;
        }

        public ActionExecutionContext CurrentBattleActionContext { get; private set; }

        /// <summary>进入（或继续）一周：随机/沿用行动轴后开始行动循环。</summary>
        public void BeginWeek()
        {
            _view.HideResultPanel();

            if (_run.HasPendingRewardOffer)
            {
                OpenPendingBattleReward();
                return;
            }

            if (_run.HasPendingGenericRewards)
            {
                GameApp.UI.OpenUIForm(
                    UIForms.Reward,
                    UIForms.GroupDialog,
                    RewardFormOpenArgs.GenericQueue(_run.PendingGenericRewardsConfirmBattleAfterDone));
                return;
            }

            _view.HideBattleWorld();

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

            DispatchOutcome(outcome, context, CommitAndResolveNodes);
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
            if (isWin && _currentBattleIsBoss)
            {
                ContinueBattleWin(hideBattleWorld: true);
                return;
            }

            if (isWin)
            {
                // 达标：发奖（不推进周），奖励确认后继续编排。
                GameApp.UI.OpenUIForm(UIForms.Reward, UIForms.GroupDialog, RewardFormOpenArgs.BattleReward());
                return;
            }

            _currentBattleIsBoss = false;
            // 未达标时立即退出美食态；胜利领奖期间保留 Battle 场景，供 RewardForm 隐藏后查看结果。
            _view.HideBattleWorld();

            if (_afterBattleLose != null)
            {
                Action<ScoreResult> cb = _afterBattleLose;
                _afterBattleLose = null;
                _afterBattleWin = null;
                CurrentBattleActionContext = null;
                cb.Invoke(result);
            }
            else if (_run.TryConsumeUndying())
            {
                // 名刀·加护：常规挑战未达标时不失败，消耗该道具后照常继续编排（不发奖）。
                _view.ShowNotice("名刀·加护", "分数未达标，但名刀·加护替你挡下了失败（道具已消耗）。", () =>
                {
                    Action cb = _afterBattleWin;
                    _afterBattleWin = null;
                    _afterBattleLose = null;
                    CurrentBattleActionContext = null;
                    cb?.Invoke();
                });
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
            ContinueBattleWin(hideBattleWorld: true);
        }

        private void ContinueBattleWin(bool hideBattleWorld)
        {
            Action cb = _afterBattleWin;
            _afterBattleWin = null;
            _afterBattleLose = null;
            CurrentBattleActionContext = null;
            _currentBattleIsBoss = false;
            if (hideBattleWorld)
            {
                _view.HideBattleWorld();
            }

            if (cb != null)
            {
                cb.Invoke();
                return;
            }

            ContinueAfterRecoveredBattleReward();
        }

        private void OpenPendingBattleReward()
        {
            CurrentBattleActionContext = _run.LastActionContext;
            _afterBattleWin = ContinueAfterRecoveredBattleReward;
            _view.RestorePendingRewardBattleView();
            GameApp.UI.OpenUIForm(UIForms.Reward, UIForms.GroupDialog, RewardFormOpenArgs.BattleReward());
        }

        private void ContinueAfterRecoveredBattleReward()
        {
            ActionExecutionContext context = _run.LastActionContext;
            if (context == null || context.Action == null)
            {
                RunPersistence.Save(_run);
                PromptNextAction();
                return;
            }

            if (FoodService.IsBossAction(_run.Tables, context.Action))
            {
                CompleteRecoveredBossBattle(context);
                return;
            }

            if (!string.IsNullOrEmpty(context.SourceKey))
            {
                _run.MarkNodeTriggered(context.SourceKey);
                RunPersistence.Save(_run);
                PromptNextAction();
                return;
            }

            float prevDay = ActionExecutor.Commit(_run, context);
            RunPersistence.Save(_run);
            ResolveNodes(prevDay, PromptNextAction);
        }

        private void CompleteRecoveredBossBattle(ActionExecutionContext context)
        {
            if (!string.IsNullOrEmpty(context.SourceKey))
            {
                _run.MarkNodeTriggered(context.SourceKey);
            }

            cfg.Food boss = FoodService.ResolveBoss(_run, context.Action);
            if (boss != null)
            {
                _run.MarkBossCompleted(boss.Id);
            }

            ApplyBossCompleteGold();
            RunPersistence.Save(_run);
            ClearPendingNodes();
            if (IsFinalBossVictory(boss))
            {
                OnVictory();
            }
            else
            {
                EndWeek();
            }
        }

        /// <summary>行动轴走完：推进到下一周（最终周胜利由 Boss 节点判定）。</summary>
        private void EndWeek()
        {
            ApplyEndOfWeekItemSettlement();
            _run.IncrementWeek();
            _run.RequiredScoreOverride = -1;
            RunPersistence.Save(_run);
            BeginWeek();
        }

        /// <summary>
        /// 周末被动道具结算：先扣高利贷债务，再按「月光族」清空金币，最后按「保底基金」补足下限。
        /// 三者顺序固定，避免相互覆盖歧义。
        /// </summary>
        private void ApplyEndOfWeekItemSettlement()
        {
            if (_run == null)
            {
                return;
            }

            var itemRuntime = new ItemRuntime(_run);

            int debt = _run.ConsumeLoanDebt();
            if (debt > 0)
            {
                _run.Gold = System.Math.Max(0, _run.Gold - debt);
            }

            if (itemRuntime.ClearsGoldOnWeekEnd())
            {
                _run.Gold = 0;
            }

            int minGold = itemRuntime.MinGoldGuarantee();
            if (minGold > 0 && _run.Gold < minGold)
            {
                _run.Gold = minGold;
            }
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
            cfg.GameAction action = TimelineService.NodeAction(_run, node);
            if (action == null)
            {
                _run.MarkNodeTriggered(node.Id);
                ProcessNextNode();
                return;
            }

            if (_run.HasItem("item_skip_node") && IsSkippableBySkipNode(action))
            {
                _run.RemoveItem("item_skip_node");
                _run.MarkNodeTriggered(node.Id);
                RunPersistence.Save(_run);
                _view.ShowTimelineNodeSkipped(node, ProcessNextNode);
                return;
            }

            // 节点即「放置来源的原子行动」：先展示放置行动卡，玩家点击后走与随机行动完全相同的执行路径。
            _view.ShowTimelineNodeCard(node, InterestMaxGain(), () => ExecutePlacedAction(node, action));
        }

        private static bool IsSkippableBySkipNode(cfg.GameAction action)
        {
            return action != null
                && (action.Behavior == cfg.ActionBehavior.Shop || action.Behavior == cfg.ActionBehavior.Interest);
        }

        /// <summary>
        /// 「加急单」：立即执行行动轴上尚未结算、day 最小的下一个节点（含运行时节点），不推进天数。
        /// 复用节点卡展示 + <see cref="ExecutePlacedAction"/> 执行链。无可执行节点返回 false。
        /// </summary>
        public bool ForceExecuteNextTimelineNode()
        {
            cfg.TimelineNode node = TimelineService.GetNextUntriggeredNode(_run);
            if (node == null)
            {
                return false;
            }

            _pendingNodes = new Queue<cfg.TimelineNode>();
            _pendingNodes.Enqueue(node);
            _afterNodes = PromptNextAction;
            ProcessNextNode();
            return true;
        }

        /// <summary>放置行动执行：与随机行动共用 <see cref="ActionExecutor"/> 与 <see cref="DispatchOutcome"/>，节点不消耗天数/步数。</summary>
        private void ExecutePlacedAction(cfg.TimelineNode node, cfg.GameAction action)
        {
            var context = new ActionExecutionContext(action) { SourceKey = node.Id };
            if (FoodService.IsBossAction(_run?.Tables, action))
            {
                context.TargetScoreDayOverride = node.Day;
            }

            IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Effect, $"node_w{_run.WeekIndex}_{node.Id}_{action.Id}");
            ActionOutcome outcome = ActionExecutor.Execute(_run, context, rng);
            DispatchOutcome(outcome, context, () =>
            {
                _run.MarkNodeTriggered(node.Id);
                ProcessNextNode();
            }, () => _run.MarkNodeTriggered(node.Id));
        }

        private int InterestMaxGain()
        {
            return _run != null ? _run.InterestCap : 0;
        }

        /// <summary>行动执行结果的统一续接：随机行动与放置节点共用；Boss 战胜利走推进/通关而非发奖。</summary>
        private void DispatchOutcome(ActionOutcome outcome, ActionExecutionContext context, Action onContinue, Action onBossComplete = null)
        {
            string title = context?.Action?.Name ?? string.Empty;
            switch (outcome.Kind)
            {
                case ActionOutcomeKind.Immediate:
                    if (IsInterestAction(context))
                    {
                        ShowInterestEventPage(outcome.Feedback, onContinue);
                    }
                    else
                    {
                        _view.ShowNotice(title, outcome.Feedback, onContinue);
                    }
                    break;
                case ActionOutcomeKind.Shop:
                    OpenShopThen(onContinue);
                    break;
                case ActionOutcomeKind.Event:
                    ResolveEventAction(context, onContinue);
                    break;
                case ActionOutcomeKind.Battle:
                    if (outcome.IsBoss)
                    {
                        StartBossBattle(outcome, context, onBossComplete);
                    }
                    else
                    {
                        StartBattle(outcome.RequiredScore, outcome.Modifier, outcome.BattleKey, false, null, onContinue, context);
                    }

                    break;
                default:
                    onContinue?.Invoke();
                    break;
            }
        }

        private static bool IsInterestAction(ActionExecutionContext context)
        {
            return context?.Action?.Behavior == cfg.ActionBehavior.Interest;
        }

        private void ShowInterestEventPage(string result, Action onContinue)
        {
            _view.ShowEventPage(
                "收取利息",
                "根据当前金币结算利息。",
                result,
                string.Empty,
                new List<string>(),
                new List<bool>(),
                showEndButton: true,
                endButtonText: "继续",
                onPick: null,
                onEnd: onContinue);
        }

        private void StartBossBattle(ActionOutcome outcome, ActionExecutionContext context, Action onBossComplete)
        {
            cfg.Tables tables = _run.Tables ?? GameApp.Config.Tables;
            cfg.Food boss = tables.TbFood.GetOrDefault(outcome.BossId);
            string title = boss != null ? $"Boss：{boss.Name}" : "Boss";
            _view.ShowNotice(title, $"目标分 {outcome.RequiredScore}，准备应战！", () =>
            {
                _run.MarkBossDebuffRolled(outcome.BossDebuffId);
                StartBattle(outcome.RequiredScore, outcome.Modifier, outcome.BattleKey, true, outcome.BossId, () =>
                {
                    onBossComplete?.Invoke();
                    _run.MarkBossCompleted(outcome.BossId);
                    ApplyBossCompleteGold();
                    RunPersistence.Save(_run);
                    ClearPendingNodes();
                    if (IsFinalBossVictory(boss))
                    {
                        OnVictory();
                    }
                    else
                    {
                        EndWeek();
                    }
                }, context);
            });
        }

        private void ApplyBossCompleteGold()
        {
            // Boss 赏金（GoldOnBossComplete）：通关本次 Boss 后额外获得金币。
            var itemRuntime = new ItemRuntime(_run);
            int bossGold = itemRuntime.BossCompleteGold();
            if (bossGold <= 0)
            {
                return;
            }

            itemRuntime.FlashTriggered(m => m.BossCompleteGold() > 0);
            _run.Gold += bossGold;
        }

        private bool IsFinalBossVictory(cfg.Food boss)
        {
            return boss != null && !_run.IsEndless && _run.WeekIndex >= _run.TotalWeeks;
        }

        /// <summary>
        /// 解析事件行动：act_event(Event) 从「全类型合并池」抽取（含 LuckyEventChance/MoreEvents 权重修正与
        /// LuckyEventGuarantee 保底），其余 behavior(Reward/Negative) 仍从各自分类事件池随机，再统一结算。
        /// </summary>
        private void ResolveEventAction(ActionExecutionContext context, Action onDone)
        {
            cfg.GameAction action = context?.Action;
            cfg.ActionBehavior behavior = action?.Behavior ?? cfg.ActionBehavior.Event;
            if (action != null && action.Id == "act_event")
            {
                _run.MarkActEventActionEntered();
            }

            string seedKey = context != null && !string.IsNullOrEmpty(context.SourceKey)
                ? $"node_{context.SourceKey}"
                : $"action_w{_run.WeekIndex}_d{DayKey(_run.CurrentDay)}_s{_run.ActionStepIndex}";

            IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Event, seedKey);
            cfg.GameEvent ev = behavior == cfg.ActionBehavior.Event
                ? EventService.RollActionEventWithGuarantee(_run, rng)
                : EventService.RollEvent(_run, rng, behavior);
            ResolveEvent(ev, onDone);
        }

        private void ResolveEvent(cfg.GameEvent ev, Action onDone)
        {
            if (ev == null)
            {
                onDone?.Invoke();
                return;
            }

            // 事件是一张「页面树」：从根页(正文=event.desc)进入，选项按 parentId 逐页展开直到终止。
            // 一个事件复用同一条随机流（Gamble 等随机效果按序派生），全程内存态，仅结束时(onDone→Commit)存档。
            IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Event, $"resolve_w{_run.WeekIndex}_d{DayKey(_run.CurrentDay)}_{ev.Id}");
            EnterEventPage(ev, string.Empty, EventService.GetRootOptions(_run, ev.Id), rng, onDone);
        }

        /// <summary>进入事件某页：初始页 resultText 为空；结果/子页 resultText 显示在下半区，可随时结束。</summary>
        private void EnterEventPage(cfg.GameEvent ev, string resultText, List<cfg.EventOption> pageOptions, IRandomStream rng, Action onDone)
        {
            var optionEnabled = new List<bool>();
            bool hasEnabledOption = false;
            foreach (cfg.EventOption opt in pageOptions)
            {
                // 逐选项前置条件（空=恒可见）复用行动前置语法；事件页保留不可选项并置灰。
                bool enabled = PreconditionEvaluator.IsSatisfied(_run, opt.Condition);
                optionEnabled.Add(enabled);
                hasEnabledOption |= enabled;
            }

            if (pageOptions.Count == 0)
            {
                ShowEventPage(
                    ev,
                    resultText,
                    pageOptions,
                    optionEnabled,
                    showEndButton: true,
                    endButtonText: "结束",
                    onPick: null,
                    onEnd: () =>
                    {
                        FinishEventAndContinue(ev, EventResolveResult.Immediate(resultText), onDone);
                    });
                return;
            }

            List<cfg.EventOption> shown = pageOptions;
            List<bool> shownEnabled = optionEnabled;
            ShowEventPage(
                ev,
                resultText,
                shown,
                shownEnabled,
                showEndButton: !string.IsNullOrWhiteSpace(resultText) || !hasEnabledOption,
                endButtonText: "结束",
                onPick: index =>
                {
                    if (index < 0 || index >= shown.Count)
                    {
                        return;
                    }

                    if (index >= shownEnabled.Count || !shownEnabled[index])
                    {
                        return;
                    }

                    ChooseEventOption(ev, shown[index], rng, onDone);
                },
                onEnd: () =>
                {
                    FinishEventAndContinue(ev, EventResolveResult.Immediate(resultText), onDone);
                });
        }

        /// <summary>结算所选选项：施加效果→跟进类(战斗/商店/结局)终止 / 有子选项则进入子页 / 否则即时终止。</summary>
        private void ChooseEventOption(cfg.GameEvent ev, cfg.EventOption option, IRandomStream rng, Action onDone)
        {
            if (RequiresRecipeDishDelete(option))
            {
                BeginEventRecipeDishDelete(ev, option, rng, onDone);
                return;
            }

            EventResolveResult result = EventService.ResolveOption(_run, option, rng);
            ContinueResolvedEventOption(ev, option, result, rng, onDone);
        }

        private void ContinueResolvedEventOption(cfg.GameEvent ev, cfg.EventOption option, EventResolveResult result, IRandomStream rng, Action onDone)
        {

            if (result.FollowUpKind != EventFollowUpKind.None)
            {
                ShowEventPage(
                    ev,
                    string.IsNullOrEmpty(result.Feedback) ? option.ResultText : ComposeEventText(result.Feedback, option.ResultText),
                    new List<cfg.EventOption>(),
                    new List<bool>(),
                    showEndButton: true,
                    endButtonText: "继续",
                    onPick: null,
                    onEnd: () =>
                    {
                        EventService.OnEventFinished(_run, ev, result);
                        ContinueEventResult(ev.Name, ev.Id, result, onDone);
                    });
                return;
            }

            // 子页正文=效果反馈 + 选项 resultText（都空则回退当前页正文）。
            string body = ComposeEventText(result.Feedback, option.ResultText);
            List<cfg.EventOption> children = EventService.GetChildOptions(_run, option.Id);
            if (option.RepeatSelf)
            {
                children.Insert(0, option);
            }

            if (children.Count > 0)
            {
                EnterEventPage(ev, body, children, rng, onDone);
                return;
            }

            // 无子选项 → 显示结果页，玩家点击结束后才提交事件行动。
            ShowEventPage(
                ev,
                body,
                children,
                new List<bool>(),
                showEndButton: true,
                endButtonText: "结束",
                onPick: null,
                onEnd: () =>
                {
                    FinishEventAndContinue(ev, result, onDone);
                });
        }

        private void BeginEventRecipeDishDelete(cfg.GameEvent ev, cfg.EventOption option, IRandomStream rng, Action onDone)
        {
            _view.OpenEventRecipeDishDelete(
                _run,
                option.Text,
                onCancel: () => EnterEventPage(ev, string.Empty, EventService.GetRootOptions(_run, ev.Id), rng, onDone),
                onTargetConfirmed: target =>
                {
                    if (_run == null || !_run.RemoveBonusDishAt(target.X, target.Y))
                    {
                        EnterEventPage(ev, "目标菜品已经不存在，请重新选择。", EventService.GetRootOptions(_run, ev.Id), rng, onDone);
                        return;
                    }

                    EventResolveResult result = EventService.ResolveOption(_run, option, rng, cfg.EffectType.SelectRemoveRecipeDish);
                    string deleteFeedback = "已从菜谱中删除 1 道菜。";
                    EventResolveResult composed = EventResolveResult.Immediate(ComposeEventText(deleteFeedback, result.Feedback));
                    ContinueResolvedEventOption(ev, option, composed, rng, onDone);
                },
                onChanged: null);
        }

        private static bool RequiresRecipeDishDelete(cfg.EventOption option)
        {
            if (option?.EffectTypes == null)
            {
                return false;
            }

            for (int i = 0; i < option.EffectTypes.Count; i++)
            {
                if (option.EffectTypes[i] == cfg.EffectType.SelectRemoveRecipeDish)
                {
                    return true;
                }
            }

            return false;
        }

        private void FinishEventAndContinue(cfg.GameEvent ev, EventResolveResult result, Action onDone)
        {
            EventService.OnEventFinished(_run, ev, result);
            ContinueAfterEventRewards(onDone);
        }

        private void ContinueAfterEventRewards(Action onDone)
        {
            if (_run != null && _run.HasPendingGenericRewards)
            {
                _afterBattleWin = onDone;
                GameApp.UI.OpenUIForm(UIForms.Reward, UIForms.GroupDialog, RewardFormOpenArgs.GenericQueue(confirmBattleRewardAfterDone: true));
                return;
            }

            onDone?.Invoke();
        }

        private void ShowEventPage(
            cfg.GameEvent ev,
            string resultText,
            IReadOnlyList<cfg.EventOption> options,
            IReadOnlyList<bool> optionEnabled,
            bool showEndButton,
            string endButtonText,
            Action<int> onPick,
            Action onEnd)
        {
            var optionTexts = new List<string>();
            if (options != null)
            {
                foreach (cfg.EventOption option in options)
                {
                    optionTexts.Add(option.Text);
                }
            }

            _view.ShowEventPage(
                ev != null ? ev.Name : string.Empty,
                ev != null ? ev.Desc : string.Empty,
                resultText,
                ev != null ? ev.BgSprite : string.Empty,
                optionTexts,
                optionEnabled,
                showEndButton,
                endButtonText,
                onPick,
                onEnd);
        }

        /// <summary>拼接页文本：效果反馈在前、选项 resultText 在后；任一为空则取另一个。</summary>
        private static string ComposeEventText(string feedback, string resultText)
        {
            bool hasFeedback = !string.IsNullOrEmpty(feedback);
            bool hasResult = !string.IsNullOrEmpty(resultText);
            if (hasFeedback && hasResult)
            {
                return feedback + "\n" + resultText;
            }

            return hasResult ? resultText : (feedback ?? string.Empty);
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
                    StartBattle(
                        result.RequiredScore,
                        result.Modifier,
                        key,
                        false,
                        null,
                        onDone,
                        null,
                        battleResult => OnEventBattleFailed(result, battleResult, onDone));
                    break;
                }

                case EventFollowUpKind.Shop:
                    OpenShopThen(onDone);
                    break;

                case EventFollowUpKind.GameOver:
                    ClearPendingNodes();
                    _view.ShowRunResult(false, 0);
                    break;

                case EventFollowUpKind.Victory:
                    ClearPendingNodes();
                    OnVictory();
                    break;

                default:
                    onDone?.Invoke();
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
            // 商会返利（GoldOnShopEnter）：进入商店时额外获得金币（每次进入结算一次）。
            var itemRuntime = new ItemRuntime(_run);
            int shopGold = itemRuntime.ShopEnterGold();
            if (shopGold > 0)
            {
                itemRuntime.FlashTriggered(m => m.ShopEnterGold() > 0);
                _run.Gold += shopGold;
            }

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
            _currentBattleIsBoss = isBoss;
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
