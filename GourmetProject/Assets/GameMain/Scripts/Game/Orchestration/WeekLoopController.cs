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

        void SavePendingRewardBattleView();

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

        /// <summary>事件页：在常驻壳中部展示事件背景、正文、选项或结果结束按钮。</summary>
        void ShowEventPage(
            string title,
            string desc,
            string resultButtonText,
            string bgSprite,
            IReadOnlyList<string> options,
            IReadOnlyList<bool> optionEnabled,
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
        private Action _beforeBattleReward;
        private Action _afterShop;
        private bool _currentBattleIsBoss;

        public WeekLoopController(GameRun run, IWeekLoopView view)
        {
            _run = run;
            _view = view;
        }

        public ActionExecutionContext CurrentBattleActionContext { get; private set; }

        /// <summary>删除尚未开始执行的节点，并同步剔除已经收集到本轮待执行队列中的快照。</summary>
        public bool RemoveTimelineNode(string nodeId)
        {
            if (!_run.RemoveRuntimeTimelineNode(nodeId))
            {
                return false;
            }

            if (_pendingNodes != null && _pendingNodes.Count > 0)
            {
                var remaining = new Queue<cfg.TimelineNode>();
                while (_pendingNodes.Count > 0)
                {
                    cfg.TimelineNode pending = _pendingNodes.Dequeue();
                    if (pending != null && pending.Id != nodeId)
                    {
                        remaining.Enqueue(pending);
                    }
                }

                _pendingNodes = remaining;
            }

            return true;
        }

        /// <summary>进入（或继续）一周：随机/沿用行动轴后开始行动循环。</summary>
        public void BeginWeek()
        {
            _view.HideResultPanel();

            if (_run.HasPendingRewardOffer)
            {
                if (_run.HasPendingRewardBattleView)
                {
                    OpenPendingBattleReward();
                    return;
                }

                _run.ClearPendingRewardOffer();
            }

            if (_run.HasPendingGenericRewards)
            {
                GameApp.UI.OpenUIForm(
                    UIForms.Reward,
                    UIForms.GroupDialog,
                    RewardFormOpenArgs.GenericQueue(_run.PendingGenericRewardsConfirmBattleAfterDone));
                return;
            }

            if (_run.HasPendingActionExecution)
            {
                RestorePendingActionExecution();
                return;
            }

            _view.HideBattleWorld();

            // 无行动轴，或换周后仍拿着上一周行动轴 → 随机一条新行动轴。
            // 同周即使 CurrentDay 已到末尾，也先交给 PromptNextAction 恢复尚未点击的行动轴节点卡。
            if (string.IsNullOrEmpty(_run.CurrentTimelineId) || _run.CurrentTimelineWeekIndex != _run.WeekIndex)
            {
                _run.RequiredScoreOverride = -1;
                IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Map, $"w{_run.WeekIndex}");
                TimelineService.RollWeekTimeline(_run, rng);
            }

            RunPersistence.Save(_run);
            PromptNextAction();
        }

        private void RestorePendingActionExecution()
        {
            PendingActionExecutionSaveData data = _run.GetPendingActionExecution();
            ActionExecutionContext context = RestoreActionContext(data);
            if (context == null || !context.IsValid)
            {
                _run.ClearPendingActionExecution();
                RunPersistence.Save(_run);
                PromptNextAction();
                return;
            }

            ActionOutcome outcome = RestoreActionOutcome(data);
            _run.SetLastActionContext(context);

            if (outcome.Kind != ActionOutcomeKind.Battle)
            {
                _view.HideBattleWorld();
            }

            DispatchOutcome(
                outcome,
                context,
                BuildRecoveredPendingContinuation(context, outcome),
                outcome.IsBoss ? BuildRecoveredBossComplete(context) : null,
                resolvedEventId: data?.EventId,
                restoringPending: true);
        }

        private ActionExecutionContext RestoreActionContext(PendingActionExecutionSaveData data)
        {
            if (data == null || string.IsNullOrEmpty(data.ActionId))
            {
                return null;
            }

            cfg.GameAction action = _run.Tables.TbAction.GetOrDefault(data.ActionId);
            if (action == null)
            {
                return null;
            }

            float costDays = data.CostDays > 0f ? data.CostDays : action.MinCostDays;
            var context = new ActionExecutionContext(
                action,
                data.StepIndex,
                data.RunStepIndex,
                data.ActionGroupId,
                costDays)
            {
                SourceKey = data.SourceKey ?? string.Empty,
                TargetScoreDayOverride = data.HasTargetScoreDayOverride
                    ? (float?)data.TargetScoreDayOverride
                    : null,
                HalfDayBuffApplied = data.HalfDayBuffApplied,
                IsExtraTimelineExecution = data.IsExtraTimelineExecution,
            };
            return context;
        }

        private static ActionOutcome RestoreActionOutcome(PendingActionExecutionSaveData data)
        {
            if (data == null)
            {
                return ActionOutcome.Immediate(string.Empty);
            }

            switch (data.OutcomeKind)
            {
                case ActionOutcomeKind.Battle:
                    return ActionOutcome.Battle(
                        data.RequiredScore,
                        data.Modifier,
                        data.BattleKey,
                        data.IsBoss,
                        data.BossId,
                        data.BossDebuffId);
                case ActionOutcomeKind.Event:
                    return ActionOutcome.Event(data.EventId);
                case ActionOutcomeKind.Shop:
                    return ActionOutcome.Shop();
                default:
                    return ActionOutcome.Immediate(data.Feedback);
            }
        }

        private Action BuildRecoveredPendingContinuation(ActionExecutionContext context, ActionOutcome outcome)
        {
            if (context == null)
            {
                return PromptNextAction;
            }

            if (!string.IsNullOrEmpty(context.SourceKey))
            {
                return () =>
                {
                    _run.ClearPendingActionExecution();
                    if (!context.IsExtraTimelineExecution)
                    {
                        _run.MarkNodeTriggered(context.SourceKey);
                    }

                    RunPersistence.Save(_run);
                    DrainPendingExtraNodes(PromptNextAction);
                };
            }

            return () =>
            {
                _run.ClearPendingActionExecution();
                float prevDay = ActionExecutor.Commit(_run, context);
                RunPersistence.Save(_run);
                ResolveNodes(prevDay, PromptNextAction);
            };
        }

        private Action BuildRecoveredBossComplete(ActionExecutionContext context)
        {
            return () =>
            {
                _run.ClearPendingActionExecution();
                if (!string.IsNullOrEmpty(context?.SourceKey) && !context.IsExtraTimelineExecution)
                {
                    _run.MarkNodeTriggered(context.SourceKey);
                }
            };
        }

        /// <summary>行动轴未走完则弹「n 选一行动」；走完则进入下一周。</summary>
        public void PromptNextAction()
        {
            RepairIncompleteTriggeredBossNodes();

            if (ResolveDueNodes(PromptNextAction))
            {
                return;
            }

            if (TimelineService.IsWeekFinished(_run))
            {
                if (IsFinalWeekVictoryReady())
                {
                    OnVictory();
                }
                else
                {
                    EndWeek();
                }

                return;
            }

            _view.HideBattleWorld();
            _view.HideResultPanel();
            _view.OpenWeekMap();
        }

        private void RepairIncompleteTriggeredBossNodes()
        {
            if (_run == null || _run.CurrentDay <= TimelineMath.Epsilon)
            {
                return;
            }

            foreach (cfg.TimelineNode node in TimelineService.GetNodes(_run))
            {
                if (node == null
                    || node.Day > _run.CurrentDay + TimelineMath.Epsilon
                    || !_run.IsNodeTriggered(node.Id))
                {
                    continue;
                }

                cfg.GameAction action = TimelineService.NodeAction(_run, node);
                if (!FoodService.IsBossAction(_run.Tables, action))
                {
                    continue;
                }

                cfg.Food boss = FoodService.ResolveBoss(_run, action);
                if (boss != null && _run.IsBossCompleted(boss.Id))
                {
                    continue;
                }

                _run.UnmarkNodeTriggered(node.Id);
            }
        }

        /// <summary>读档/回到行动选择时，优先补处理当前天数已经到达但尚未结算的行动轴节点。</summary>
        private bool ResolveDueNodes(Action onDone)
        {
            if (_run == null || _run.CurrentDay <= TimelineMath.Epsilon)
            {
                return false;
            }

            List<cfg.TimelineNode> dueNodes = TimelineService.CollectPassedNodes(_run, 0f, _run.CurrentDay);
            if (dueNodes.Count == 0)
            {
                return false;
            }

            _pendingNodes = new Queue<cfg.TimelineNode>(dueNodes);
            _afterNodes = onDone;
            ProcessNextNode();
            return true;
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

            // 进入行动时只记录可恢复的 pending 页面，不推进天数/步数；只有玩家明确结算
            // （商店退出、事件选完、战斗结算、通知点继续）时才 Commit（推进天数/步数 + 标记已用）。
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
            DrainPendingExtraNodes(cb);
        }

        public void OnBattleSettled(ScoreResult result, bool isWin, int finalHappyCakeLayers)
        {
            ApplyCakeLayerRetain(finalHappyCakeLayers);

            if (isWin)
            {
                ApplyCakeLayerGold(finalHappyCakeLayers);
            }

            if (isWin)
            {
                Action beforeReward = _beforeBattleReward;
                _beforeBattleReward = null;
                beforeReward?.Invoke();
                EnsurePendingBattleReward(CurrentBattleActionContext);

                // 达标：发奖（不推进周），奖励确认后继续编排。
                GameApp.UI.OpenUIForm(UIForms.Reward, UIForms.GroupDialog, RewardFormOpenArgs.BattleReward());
                return;
            }

            _currentBattleIsBoss = false;
            _beforeBattleReward = null;
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

        private void ApplyCakeLayerGold(int finalHappyCakeLayers)
        {
            var itemRuntime = new ItemRuntime(_run);
            int gold = itemRuntime.GoldForCakeLayers(finalHappyCakeLayers);
            if (gold <= 0)
            {
                return;
            }

            itemRuntime.FlashTriggered(m => m.GoldForCakeLayers(finalHappyCakeLayers) > 0);
            _run.Gold += gold;
        }

        private void ApplyCakeLayerRetain(int finalHappyCakeLayers)
        {
            if (_run == null || finalHappyCakeLayers <= 0)
            {
                return;
            }

            var itemRuntime = new ItemRuntime(_run);
            float fraction = itemRuntime.CakeRetainFraction();
            if (fraction <= 0f)
            {
                return;
            }

            int retained = (int)System.Math.Floor(finalHappyCakeLayers * fraction);
            if (retained <= 0)
            {
                return;
            }

            _run.SetRetainedHappyCakeLayers(retained);
            itemRuntime.FlashTriggered(m =>
            {
                return m.TryGetCakeRetainFraction(out float value) && value > 0f;
            });
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
            _beforeBattleReward = null;
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
            _beforeBattleReward = null;
            _view.RestorePendingRewardBattleView();
            GameApp.UI.OpenUIForm(UIForms.Reward, UIForms.GroupDialog, RewardFormOpenArgs.BattleReward());
        }

        private void ContinueAfterRecoveredBattleReward()
        {
            ActionExecutionContext context = _run.LastActionContext;
            if (context == null || context.Action == null)
            {
                _run.ClearPendingActionExecution();
                RunPersistence.Save(_run);
                PromptNextAction();
                return;
            }

            if (!string.IsNullOrEmpty(context.SourceKey))
            {
                _run.ClearPendingActionExecution();
                if (!context.IsExtraTimelineExecution)
                {
                    _run.MarkNodeTriggered(context.SourceKey);
                }

                if (FoodService.IsBossAction(_run.Tables, context.Action))
                {
                    cfg.Food boss = FoodService.ResolveBoss(_run, context.Action);
                    MarkBossCompletedAndApplyGold(boss?.Id);
                }

                RunPersistence.Save(_run);
                DrainPendingExtraNodes(PromptNextAction);
                return;
            }

            _run.ClearPendingActionExecution();
            float prevDay = ActionExecutor.Commit(_run, context);
            RunPersistence.Save(_run);
            ResolveNodes(prevDay, PromptNextAction);
        }

        private void EnsurePendingBattleReward(ActionExecutionContext actionContext)
        {
            string rewardKey = GameRun.BuildRewardKey(_run.WeekIndex, _run.CurrentDay, actionContext);
            RewardOffer offer = _run.GetPendingRewardOffer(rewardKey);
            if (offer == null)
            {
                IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Reward, rewardKey);
                offer = RewardGranter.GenerateOffer(_run, _run.CurrentWeek, rng, actionContext);
                _run.SetPendingRewardOffer(rewardKey, offer);
            }

            _view.SavePendingRewardBattleView();
            RunPersistence.Save(_run);
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

        public bool ForceExecuteExtraTimelineNode(string nodeId)
        {
            cfg.TimelineNode node = TimelineService.GetNode(_run, nodeId);
            if (node == null)
            {
                return false;
            }

            ExecuteExtraTimelineNode(node, PromptNextAction);
            return true;
        }

        public bool QueueExtraTimelineNode(string nodeId)
        {
            if (TimelineService.GetNode(_run, nodeId) == null
                || !_run.EnqueueExtraTimelineNode(nodeId))
            {
                return false;
            }

            return true;
        }

        public void ExecuteQueuedExtraTimelineNodes()
        {
            DrainPendingExtraNodes(PromptNextAction);
        }

        private void DrainPendingExtraNodes(Action onDone)
        {
            if (_run == null || !_run.TryDequeueExtraTimelineNode(out string nodeId))
            {
                onDone?.Invoke();
                return;
            }

            RunPersistence.Save(_run);
            cfg.TimelineNode node = TimelineService.GetNode(_run, nodeId);
            if (node == null)
            {
                DrainPendingExtraNodes(onDone);
                return;
            }

            ExecuteExtraTimelineNode(node, () => DrainPendingExtraNodes(onDone));
        }

        private void ExecuteExtraTimelineNode(cfg.TimelineNode node, Action onDone)
        {
            cfg.GameAction action = TimelineService.NodeAction(_run, node);
            if (action == null)
            {
                onDone?.Invoke();
                return;
            }

            var context = new ActionExecutionContext(action)
            {
                SourceKey = node.Id,
                TargetScoreDayOverride = FoodService.IsBossAction(_run?.Tables, action) ? (float?)node.Day : null,
                IsExtraTimelineExecution = true,
            };
            IRandomStream rng = GameApp.Random.DomainStream(
                SeedDomains.Effect,
                $"extra_node_w{_run.WeekIndex}_{node.Id}_{action.Id}");
            ActionOutcome outcome = ActionExecutor.Execute(_run, context, rng);
            DispatchOutcome(
                outcome,
                context,
                () =>
                {
                    _run.ClearPendingActionExecution();
                    RunPersistence.Save(_run);
                    onDone?.Invoke();
                },
                () => _run.ClearPendingActionExecution());
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
                _run.ClearPendingActionExecution();
                _run.MarkNodeTriggered(node.Id);
                RunPersistence.Save(_run);
                ProcessNextNode();
            }, () =>
            {
                _run.ClearPendingActionExecution();
                _run.MarkNodeTriggered(node.Id);
            });
        }

        private int InterestMaxGain()
        {
            return _run != null ? _run.InterestCap : 0;
        }

        /// <summary>行动执行结果的统一续接：随机行动与放置节点共用；Boss 战领奖后推进/通关。</summary>
        private void DispatchOutcome(
            ActionOutcome outcome,
            ActionExecutionContext context,
            Action onContinue,
            Action onBossComplete = null,
            string resolvedEventId = null,
            bool restoringPending = false)
        {
            if (outcome == null)
            {
                onContinue?.Invoke();
                return;
            }

            string title = context?.Action?.Name ?? string.Empty;
            switch (outcome.Kind)
            {
                case ActionOutcomeKind.Immediate:
                    if (!restoringPending)
                    {
                        SavePendingActionExecution(context, outcome);
                    }

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
                    if (restoringPending)
                    {
                        OpenRecoveredShopThen(onContinue);
                    }
                    else
                    {
                        OpenShopThen(onContinue, context, outcome);
                    }
                    break;
                case ActionOutcomeKind.Event:
                    if (!string.IsNullOrEmpty(resolvedEventId))
                    {
                        ResolveSavedEventAction(context, resolvedEventId, onContinue, outcome, restoringPending);
                    }
                    else
                    {
                        ResolveEventAction(context, onContinue, outcome);
                    }
                    break;
                case ActionOutcomeKind.Battle:
                    if (!restoringPending)
                    {
                        SavePendingActionExecution(context, outcome);
                    }

                    if (outcome.IsBoss)
                    {
                        StartBossBattle(outcome, context, onContinue, onBossComplete);
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

        private void SavePendingActionExecution(ActionExecutionContext context, ActionOutcome outcome, string resolvedEventId = null)
        {
            _run.SetPendingActionExecution(context, outcome, resolvedEventId);
            RunPersistence.Save(_run);
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
                onPick: null,
                onEnd: onContinue);
        }

        private void StartBossBattle(
            ActionOutcome outcome,
            ActionExecutionContext context,
            Action onContinue,
            Action onBossComplete)
        {
            cfg.Tables tables = _run.Tables ?? GameApp.Config.Tables;
            cfg.Food boss = tables.TbFood.GetOrDefault(outcome.BossId);
            string title = boss != null ? $"Boss：{boss.Name}" : "Boss";
            _view.ShowNotice(title, $"目标分 {outcome.RequiredScore}，准备应战！", () =>
            {
                _run.MarkBossDebuffRolled(outcome.BossDebuffId);
                StartBattle(
                    outcome.RequiredScore,
                    outcome.Modifier,
                    outcome.BattleKey,
                    true,
                    outcome.BossId,
                    onContinue,
                    context,
                    beforeReward: () => CompleteBossBeforeReward(outcome, boss, onBossComplete));
            });
        }

        private void CompleteBossBeforeReward(ActionOutcome outcome, cfg.Food boss, Action onBossComplete)
        {
            _run.ClearPendingActionExecution();
            onBossComplete?.Invoke();

            string bossId = !string.IsNullOrEmpty(outcome?.BossId)
                ? outcome.BossId
                : boss?.Id ?? string.Empty;
            MarkBossCompletedAndApplyGold(bossId);
            RunPersistence.Save(_run);
        }

        private void MarkBossCompletedAndApplyGold(string bossId)
        {
            if (string.IsNullOrEmpty(bossId) || _run.IsBossCompleted(bossId))
            {
                return;
            }

            _run.MarkBossCompleted(bossId);
            ApplyBossCompleteGold();
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

        private bool IsFinalWeekVictoryReady()
        {
            if (_run == null || _run.IsEndless || _run.WeekIndex < _run.TotalWeeks)
            {
                return false;
            }

            foreach (cfg.TimelineNode node in TimelineService.GetNodes(_run))
            {
                cfg.GameAction action = TimelineService.NodeAction(_run, node);
                if (!FoodService.IsBossAction(_run.Tables, action) || !_run.IsNodeTriggered(node.Id))
                {
                    continue;
                }

                cfg.Food boss = FoodService.ResolveBoss(_run, action);
                if (boss != null && _run.IsBossCompleted(boss.Id))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 解析事件行动：act_event(Event) 从「全类型合并池」抽取（含 LuckyEventChance/MoreEvents 权重修正与
        /// LuckyEventGuarantee 保底），其余 behavior(Reward/Negative) 仍从各自分类事件池随机，再统一结算。
        /// </summary>
        private void ResolveEventAction(ActionExecutionContext context, Action onDone, ActionOutcome outcome)
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
            if (ev != null)
            {
                SavePendingActionExecution(context, outcome, ev.Id);
            }

            ResolveEvent(ev, onDone);
        }

        private void ResolveSavedEventAction(
            ActionExecutionContext context,
            string eventId,
            Action onDone,
            ActionOutcome outcome,
            bool restoringPending)
        {
            cfg.GameEvent ev = _run.Tables.TbEvent.GetOrDefault(eventId);
            if (ev == null)
            {
                _run.ClearPendingActionExecution();
                RunPersistence.Save(_run);
                onDone?.Invoke();
                return;
            }

            if (!restoringPending)
            {
                SavePendingActionExecution(context, outcome, ev.Id);
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

            // 一个事件复用同一条随机流（Gamble 等随机效果按序派生），parentId 串起后续选项页。
            IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Event, $"resolve_w{_run.WeekIndex}_d{DayKey(_run.CurrentDay)}_{ev.Id}");
            EnterEventPage(ev, ev.Desc, EventService.GetRootOptions(_run, ev.Id), rng, onDone);
        }

        /// <summary>进入流程中的一页：显示本页描述和所有可用选项。</summary>
        private void EnterEventPage(
            cfg.GameEvent ev,
            string pageDescription,
            List<cfg.EventOption> pageOptions,
            IRandomStream rng,
            Action onDone)
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

            if (pageOptions.Count == 0 || !hasEnabledOption)
            {
                ShowEventPage(
                    ev,
                    pageDescription,
                    "结束",
                    new List<cfg.EventOption>(),
                    new List<bool>(),
                    onPick: null,
                    onEnd: () =>
                    {
                        FinishEventAndContinue(ev, EventResolveResult.Immediate(string.Empty), onDone);
                    });
                return;
            }

            List<cfg.EventOption> shown = pageOptions;
            List<bool> shownEnabled = optionEnabled;
            ShowEventPage(
                ev,
                pageDescription,
                string.Empty,
                shown,
                shownEnabled,
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

                    ChooseEventOption(ev, pageDescription, shown, shown[index], rng, onDone);
                },
                onEnd: null);
        }

        /// <summary>结算所选选项，再进入子页、终止界面或结果确认。</summary>
        private void ChooseEventOption(
            cfg.GameEvent ev,
            string pageDescription,
            List<cfg.EventOption> pageOptions,
            cfg.EventOption option,
            IRandomStream rng,
            Action onDone)
        {
            if (RequiresRecipeDishDelete(option))
            {
                BeginEventRecipeDishDelete(ev, pageDescription, pageOptions, option, rng, onDone);
                return;
            }

            EventResolveResult result = EventService.ResolveOption(_run, option, rng);
            ContinueResolvedEventOption(ev, pageDescription, option, result, rng, onDone);
        }

        private void ContinueResolvedEventOption(
            cfg.GameEvent ev,
            string pageDescription,
            cfg.EventOption option,
            EventResolveResult result,
            IRandomStream rng,
            Action onDone)
        {
            result ??= EventResolveResult.Immediate(string.Empty);

            // 战斗、商店和结局节点是立即终点，不再显示普通结果按钮。
            if (result.FollowUpKind != EventFollowUpKind.None)
            {
                EventService.OnEventFinished(_run, ev, result);
                ContinueEventResult(ev.Name, ev.Id, result, onDone);
                return;
            }

            List<cfg.EventOption> children = EventService.GetChildOptions(_run, ev.Id, option.Id);
            if (children.Count > 0)
            {
                string nextDescription = ResolveEventPageText(option, result, pageDescription);
                EnterEventPage(ev, nextDescription, children, rng, onDone);
                return;
            }

            // 奖励类效果直接打开获得弹窗；弹窗关闭后退出事件并回到行动流程。
            if (_run != null && _run.HasPendingGenericRewards)
            {
                FinishEventAndContinue(ev, result, onDone);
                return;
            }

            // 普通终点沿用结果文本按钮，给玩家一次明确的结束确认。
            string resultButtonText = ResolveEventPageText(option, result, "结束");
            ShowEventPage(
                ev,
                pageDescription,
                resultButtonText,
                new List<cfg.EventOption>(),
                new List<bool>(),
                onPick: null,
                onEnd: () => FinishEventAndContinue(ev, result, onDone));
        }

        private void BeginEventRecipeDishDelete(
            cfg.GameEvent ev,
            string pageDescription,
            List<cfg.EventOption> pageOptions,
            cfg.EventOption option,
            IRandomStream rng,
            Action onDone)
        {
            _view.OpenEventRecipeDishDelete(
                _run,
                option.Text,
                onCancel: () => EnterEventPage(ev, pageDescription, pageOptions, rng, onDone),
                onTargetConfirmed: target =>
                {
                    if (_run == null || !_run.RemoveBonusDishAt(target.Y))
                    {
                        EnterEventPage(ev, pageDescription, pageOptions, rng, onDone);
                        return;
                    }

                    EventResolveResult result = EventService.ResolveOption(_run, option, rng, cfg.EffectType.SelectRemoveRecipeDish);
                    ContinueResolvedEventOption(ev, pageDescription, option, result, rng, onDone);
                },
                onChanged: null);
        }

        private static string ResolveEventPageText(
            cfg.EventOption option,
            EventResolveResult result,
            string fallback)
        {
            if (!string.IsNullOrWhiteSpace(option?.ResultText))
            {
                return option.ResultText;
            }

            return !string.IsNullOrWhiteSpace(result?.Feedback)
                ? result.Feedback
                : (fallback ?? string.Empty);
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
                _afterBattleWin = () => DrainPendingExtraNodes(onDone);
                GameApp.UI.OpenUIForm(UIForms.Reward, UIForms.GroupDialog, RewardFormOpenArgs.GenericQueue(confirmBattleRewardAfterDone: true));
                return;
            }

            DrainPendingExtraNodes(onDone);
        }

        private void ShowEventPage(
            cfg.GameEvent ev,
            string pageDescription,
            string resultButtonText,
            IReadOnlyList<cfg.EventOption> options,
            IReadOnlyList<bool> optionEnabled,
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
                pageDescription ?? (ev != null ? ev.Desc : string.Empty),
                resultButtonText,
                ev != null ? ev.BgSprite : string.Empty,
                optionTexts,
                optionEnabled,
                onPick,
                onEnd);
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
                    _run.ClearPendingActionExecution();
                    ClearPendingNodes();
                    _view.ShowRunResult(false, 0);
                    break;

                case EventFollowUpKind.Victory:
                    _run.ClearPendingActionExecution();
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
            OpenShopThen(onClose, null, null);
        }

        private void OpenShopThen(Action onClose, ActionExecutionContext context, ActionOutcome outcome)
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
            if (context != null && outcome != null)
            {
                SavePendingActionExecution(context, outcome);
            }

            _view.OpenShop();
        }

        private void OpenRecoveredShopThen(Action onClose)
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
            Action<ScoreResult> onLose = null,
            Action beforeReward = null)
        {
            _afterBattleWin = onWin;
            _afterBattleLose = onLose;
            _beforeBattleReward = beforeReward;
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
