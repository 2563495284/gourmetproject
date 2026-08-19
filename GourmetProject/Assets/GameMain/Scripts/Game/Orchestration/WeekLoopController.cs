using System;
using System.Collections.Generic;
using System.Globalization;
using BreakInfinity;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Analytics;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Game.Save;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Gameplay.Scoring;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Orchestration
{
    public interface IWeekLoopView
    {
        BigDouble LastBattleTotal { get; }

        void HideBattleWorld();

        void ResetBossBattlePresentation();

        void SavePendingRewardBattleView();

        void RestorePendingRewardBattleView();

        void HideResultPanel();

        void OpenWeekMap();

        void OpenShop();

        /// <summary>打开通用奖励弹窗；事件奖励需要在弹窗完成后再结束事件页。</summary>
        void OpenRewardForm(RewardFormOpenArgs args);

        /// <summary>时间轴节点卡片：先展示节点卡，玩家点击后再执行节点效果。</summary>
        void ShowTimelineNodeCard(cfg.TimelineNode node, int? interestMaxGain, Action onPick);

        /// <summary>收起当前节点行动卡；隐藏完成后再回调，避免误当成日常行动重铺。</summary>
        void DismissTimelineNodeCard(Action onDone);

        void ShowTimelineNodeSkipped(
            cfg.TimelineNode node,
            TimelineMutationResult result,
            Action onDone);

        void PlayTimelineAdvance(
            float fromDay,
            float toDay,
            string arrivingNodeId,
            Action onDone);

        /// <summary>锁定本次推进演出的视觉游标，避免节点页面重建时读取错误日期。</summary>
        void BeginTimelineAdvanceSequence(float fromDay);

        /// <summary>推进与所经过节点均处理完成后解除视觉游标锁。</summary>
        void EndTimelineAdvanceSequence();

        void PlayTimelineNodeCue(
            string nodeId,
            TimelinePresentationCueKind kind,
            Action onDone);

        void StartBattle(
            int requiredScore,
            string modifier,
            string key,
            string bossDebuffId,
            ActionExecutionContext actionContext);

        void ShowNotice(string title, string message, Action onContinue);

        void ShowHeartBreak(HeartBreakFormOpenArgs args, Action onComplete);

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
            IReadOnlyList<string> optionRequirements,
            IReadOnlyList<bool> optionEnabled,
            Action<int> onPick,
            Action onEnd);

        /// <summary>自动展示事件效果反馈；表现结束后回调，不要求玩家再次确认。</summary>
        void ShowEventEffectFeedback(
            string title,
            string message,
            string bgSprite,
            Action onComplete);

        /// <summary>事件完整结束时先退出事件页；退场完成后才允许提交行动并推进时间轴。</summary>
        void ExitEventPage(Action onExited);

        /// <summary>展示事件造成的食谱食物变化；全部动画结束后回调。</summary>
        void ShowEventRecipeMutation(
            RecipeMutationResult result,
            Action onComplete);

        /// <summary>在常驻壳中直接展示一件已到账的负面装饰品，表现结束后继续事件流程。</summary>
        void ShowDirectPassiveItemAcquire(
            ItemDefinition item,
            ItemAcquireResult acquireResult,
            Action onComplete);

        void ShowRunResult(bool win, BigDouble total);
    }

    /// <summary>
    /// 局外周循环编排器：行动选择、时间轴节点、事件/商店/经营挑战续接都在这里推进。
    /// </summary>
    public sealed class WeekLoopController
    {
        private const string KitchenGodStatueEventId = "ev_kitchen_god_statue";
        private const string KitchenGodDirectNegativeOptionId = "opt_kitchen_god_statue_choice_2";

        /// <summary>日常营业、火热营业与星级评鉴失败都只损失这么多红心。</summary>
        internal const int HeartLossPerFailedBattle = 1;

        private readonly GameRun _run;
        private readonly IWeekLoopView _view;

        private Action _afterNodes;
        private Action _afterBattleWin;
        private Action _afterEventReward;
        private Action _beforeBattleReward;
        private Action _afterShop;
        private bool _currentBattleIsBoss;
        private bool _timelinePresentationActive;
        private float _timelinePresentationCursor;
        private float _timelinePresentationTarget;
        private string _presentedTimelineNodeId = string.Empty;

        public WeekLoopController(GameRun run, IWeekLoopView view)
        {
            _run = run;
            _view = view;
        }

        public ActionExecutionContext CurrentBattleActionContext { get; private set; }

        /// <summary>
        /// 立即进入指定事件的正常交互流程，不消耗天数或行动次数。
        /// 供开发者工具从空闲的行动选择页触发事件；完成后返回行动选择。
        /// </summary>
        public bool ExecuteEventImmediately(string eventId)
        {
            cfg.GameEvent ev = string.IsNullOrWhiteSpace(eventId)
                ? null
                : _run?.Tables?.TbEvent.GetOrDefault(eventId);
            if (ev == null)
            {
                return false;
            }

            ResolveEvent(ev, PromptNextAction);
            return true;
        }

        /// <summary>删除尚未开始执行的节点；到期节点每轮动态重扫，无需维护队列快照。</summary>
        public bool RemoveTimelineNode(string nodeId)
        {
            bool removed = _run.RemoveRuntimeTimelineNode(nodeId);
            if (!removed)
            {
                return false;
            }

            // 只有删掉正在展示的节点卡才续跑序列。删其它节点只改时间轴，不能白刷当前页。
            if (!string.IsNullOrEmpty(nodeId)
                && string.Equals(_presentedTimelineNodeId, nodeId, StringComparison.Ordinal))
            {
                _presentedTimelineNodeId = string.Empty;
                _view.DismissTimelineNodeCard(ProcessNextNode);
            }

            return true;
        }

        /// <summary>进入（或继续）一周：随机/沿用时间轴后开始行动循环。</summary>
        public void BeginWeek()
        {
            _view.HideResultPanel();

            if (_run.HasPendingHeartBreak)
            {
                OpenPendingHeartBreak();
                return;
            }

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
                PendingGenericRewardContinuationKind continuation =
                    _run.PendingGenericRewardContinuation;
                if (continuation == PendingGenericRewardContinuationKind.Battle
                    && _run.HasPendingRewardBattleView)
                {
                    CurrentBattleActionContext = _run.LastActionContext;
                    _afterBattleWin = ContinueAfterRecoveredBattleReward;
                    _view.RestorePendingRewardBattleView();
                }

                _view.OpenRewardForm(RewardFormOpenArgs.GenericQueue(continuation));
                return;
            }

            // RewardForm 已清空队列、但事件完成回调尚未落盘时，从事件专用续接点恢复。
            if (_run.PendingGenericRewardContinuation == PendingGenericRewardContinuationKind.Event)
            {
                ContinueAfterRecoveredEventReward();
                return;
            }

            if (_run.HasPendingRewardBattleView)
            {
                _run.ClearPendingRewardBattleView();
                _run?.RequestSave();
            }

            if (_run.HasPendingActionExecution)
            {
                RestorePendingActionExecution();
                return;
            }

            _view.HideBattleWorld();

            // 无时间轴，或换周后仍拿着上一周时间轴 → 随机一条新时间轴。
            // 同周即使 CurrentDay 已到末尾，也先交给 PromptNextAction 恢复尚未点击的时间轴节点卡。
            if (string.IsNullOrEmpty(_run.CurrentTimelineId) || _run.CurrentTimelineWeekIndex != _run.WeekIndex)
            {
                _run.RequiredScoreOverride = -1;
                IRandomStream rng = _run.Random.DomainStream(SeedDomains.Map, $"w{_run.WeekIndex}");
                TimelineService.RollWeekTimeline(_run, rng);
            }

            _run?.RequestSave();
            PromptNextAction();
        }

        private void RestorePendingActionExecution()
        {
            PendingActionExecutionSaveData data = _run.GetPendingActionExecution();
            ActionExecutionContext context = RestoreActionContext(data);
            if (context == null || !context.IsValid)
            {
                if (_run.PendingGenericRewardContinuation == PendingGenericRewardContinuationKind.Slot)
                {
                    _run.PendingGenericRewardContinuation = PendingGenericRewardContinuationKind.None;
                }

                _run.ClearPendingActionExecution();
                _run?.RequestSave();
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
                TimelineStopChance = data.TimelineStopChance,
                NodeRepeatIndex = System.Math.Max(1, data.NodeRepeatIndex),
                NodeRepeatTotal = System.Math.Max(1, data.NodeRepeatTotal),
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
                case ActionOutcomeKind.Slot:
                    return ActionOutcome.Slot(data.SlotEventId);
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
                return () => ContinueRecoveredTimelineNodePass(context);
            }

            return () =>
            {
                _run.ClearPendingActionExecution();
                float previousDay = ActionExecutor.Commit(_run, context);
                _run?.RequestSave();
                ResolveNodes(PromptNextAction, previousDay);
            };
        }

        private Action BuildRecoveredBossComplete(ActionExecutionContext context)
        {
            // 星级评鉴完成发生在领奖前。节点的完成/重复轮次统一留给领奖后的 continuation，
            // 避免把节点过早标记为已结算。
            return () => _run.ClearPendingActionExecution();
        }

        /// <summary>时间轴未走完则弹「n 选一行动」；走完则进入下一周。</summary>
        public void PromptNextAction()
        {
            RepairIncompleteTriggeredBossNodes();

            if (ResumePendingTimelineNodeRepeat())
            {
                return;
            }

            if (ResolveDueNodes(PromptNextAction))
            {
                return;
            }

            if (TimelineService.IsWeekFinished(_run))
            {
                if (IsConfiguredFinalWeek())
                {
                    CompleteFinalWeek();
                }
                else
                {
                    EndWeek();
                }

                return;
            }

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

        /// <summary>读档/回到行动选择时，优先补处理当前天数已经到达但尚未结算的时间轴节点。</summary>
        private bool ResolveDueNodes(Action onDone)
        {
            if (_run == null || _run.CurrentDay <= TimelineMath.Epsilon)
            {
                return false;
            }

            if (TimelineService.GetNextDueUntriggeredNode(_run) == null)
            {
                return false;
            }

            ResolveNodes(onDone);
            return true;
        }

        /// <summary>WeekMapForm 选择行动后回调（null = 无行动可选时的「休息」）。</summary>
        public void OnActionPicked(ActionChoice choice)
        {
            if (choice == null)
            {
                float previousDay = _run.CurrentDay;
                TimelineService.AdvanceDays(_run, 1f);
                _run.AdvanceActionStep();
                _run?.RequestSave();
                ResolveNodes(PromptNextAction, previousDay);
                return;
            }

            ActionExecutionContext context = choice.ToExecutionContext();
            if (!context.IsValid)
            {
                _run.AdvanceActionStep();
                _run?.RequestSave();
                ResolveNodes(PromptNextAction);
                return;
            }

            IRandomStream rng = _run.Random.DomainStream(SeedDomains.Effect, $"exec_r{context.RunStepIndex}_w{_run.WeekIndex}_s{context.StepIndex}_{context.ActionGroupId}_{context.Action.Id}");
            ActionOutcome outcome = ActionExecutor.Execute(_run, context, rng);

            // 进入行动时只记录可恢复的 pending 页面，不推进天数/步数；只有玩家明确结算
            // （商店退出、事件选完、经营挑战结算、通知点继续）时才 Commit（推进天数/步数 + 标记已用）。
            bool committed = false;

            void Commit()
            {
                if (committed)
                {
                    return;
                }

                committed = true;
                ActionExecutor.Commit(_run, context);
                _run?.RequestSave();
            }

            void CommitAndResolveNodes()
            {
                float previousDay = _run.CurrentDay;
                Commit();
                ResolveNodes(PromptNextAction, previousDay);
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
            ActionExecutionContext battleContext = CurrentBattleActionContext ?? _run.LastActionContext;
            bool analyticsIsBoss = _currentBattleIsBoss;
            string analyticsBossId = analyticsIsBoss
                ? FoodService.ResolveBoss(_run, battleContext?.Action)?.Id ?? string.Empty
                : string.Empty;
            cfg.Food battleFood = FoodService.Resolve(_run.Tables, battleContext?.Action);
            bool isBusiness = battleFood != null
                && (battleFood.ActionKind == cfg.FoodActionKind.Normal
                    || battleFood.ActionKind == cfg.FoodActionKind.Super);
            string settledBattleKey = ResolveSettledBattleKey(battleContext);
            if (isBusiness && !_run.TryMarkFoodBattleSettled(settledBattleKey))
            {
                return;
            }

            if (battleFood?.ActionKind == cfg.FoodActionKind.Normal)
            {
                ApplyNormalMealBonus();
            }

            ApplyCakeLayerRetain(finalHappyCakeLayers);

            if (isWin)
            {
                TrackBattleSettled(
                    _run, settledBattleKey, analyticsIsBoss, analyticsBossId,
                    result?.Total ?? BigDouble.Zero, true, true, false, _run.HeartsRemaining);
                SettleFoodPassives(battleFood, battleContext, settledBattleKey, survived: true);
                ApplyCakeLayerGold(finalHappyCakeLayers);
                Action beforeReward = _beforeBattleReward;
                _beforeBattleReward = null;
                beforeReward?.Invoke();
                EnsurePendingBattleReward(battleContext);

                // 达标：发奖（不推进周），奖励确认后继续编排。
                _view.OpenRewardForm(RewardFormOpenArgs.BattleReward());
                return;
            }

            int heartLoss = HeartLossPerFailedBattle;
            if (_run.HeartsRemaining <= heartLoss && _run.TryConsumeUndying(out int restoreToHearts))
            {
                // 最后一颗心优先由名刀挡下：不失去红心，并恢复到配置目标颗数，仍按“失败但存活”完整结算本场奖励。
                int missingHearts = restoreToHearts - _run.HeartsRemaining;
                if (missingHearts > 0)
                {
                    _run.RestoreHearts(missingHearts);
                }

                TrackBattleSettled(
                    _run, settledBattleKey, analyticsIsBoss, analyticsBossId,
                    result?.Total ?? BigDouble.Zero, false, true, false, _run.HeartsRemaining);

                SettleFoodPassives(battleFood, battleContext, settledBattleKey, survived: true);
                ApplyCakeLayerGold(finalHappyCakeLayers);
                Action beforeReward = _beforeBattleReward;
                _beforeBattleReward = null;
                beforeReward?.Invoke();
                EnsurePendingBattleReward(battleContext);
                _currentBattleIsBoss = false;
                _view.ResetBossBattlePresentation();
                _run?.RequestSave();
                string itemName = ItemDefinition.Get(_run.Tables, "item_famous_knife")?.Name ?? "名刀";
                _view.ShowNotice(
                    itemName,
                    $"[strong]分数[/strong]未达标，但{itemName}替你挡下了失败，红心恢复至{_run.HeartsRemaining}颗（[term]装饰品[/term]和[term]消耗品[/term]已消耗）。",
                    () =>
                    {
                        _view.OpenRewardForm(RewardFormOpenArgs.BattleReward());
                    });
                return;
            }

            if (!_run.TryLoseHearts(heartLoss, out int before, out int after))
            {
                TrackBattleSettled(
                    _run, settledBattleKey, analyticsIsBoss, analyticsBossId,
                    result?.Total ?? BigDouble.Zero, false, false, true, 0);
                TrackRunEnded("hearts_zero", isDeath: true);
                // 防御性兜底：开发期旧存档或中断状态可能已经为 0；仍必须先展示最后碎心页，不能直跳失败页。
                SettleFoodPassives(battleFood, battleContext, settledBattleKey, survived: false);
                _beforeBattleReward = null;
                _currentBattleIsBoss = false;
                _view.SavePendingRewardBattleView();
                _run.SetPendingHeartBreak(new PendingHeartBreakSaveData
                {
                    BeforeHeartCount = 0,
                    AfterHeartCount = 0,
                    BattleTotal = BigNumberSaveData.ToLegacyInt(result?.Total ?? BigDouble.Zero),
                    BattleTotalBig = BigNumberSaveData.From(result?.Total ?? BigDouble.Zero),
                    IsTerminal = true,
                });
                _run?.RequestSave();
                ShowPendingHeartBreak();
                return;
            }

            bool terminal = after <= 0;
            TrackBattleSettled(
                _run, settledBattleKey, analyticsIsBoss, analyticsBossId,
                result?.Total ?? BigDouble.Zero, false, !terminal, terminal, after);
            if (terminal)
            {
                TrackRunEnded("hearts_zero", isDeath: true);
            }
            SettleFoodPassives(battleFood, battleContext, settledBattleKey, survived: !terminal);
            _run.SetPendingHeartBreak(new PendingHeartBreakSaveData
            {
                BeforeHeartCount = before,
                AfterHeartCount = after,
                BattleTotal = BigNumberSaveData.ToLegacyInt(result?.Total ?? BigDouble.Zero),
                BattleTotalBig = BigNumberSaveData.From(result?.Total ?? BigDouble.Zero),
                IsTerminal = terminal,
            });
            if (!terminal)
            {
                // 非致命碎心按通关处理：星级评鉴完成、蛋糕金币及奖励包都与达标路径一致。
                ApplyCakeLayerGold(finalHappyCakeLayers);
                Action beforeReward = _beforeBattleReward;
                _beforeBattleReward = null;
                beforeReward?.Invoke();
                EnsurePendingBattleReward(battleContext);
            }
            else
            {
                _beforeBattleReward = null;
                _view.SavePendingRewardBattleView();
            }

            _currentBattleIsBoss = false;
            _run?.RequestSave();
            ShowPendingHeartBreak();
        }

        private string ResolveSettledBattleKey(ActionExecutionContext actionContext)
        {
            PendingActionExecutionSaveData pending = _run.GetPendingActionExecution();
            if (!string.IsNullOrEmpty(pending?.BattleKey))
            {
                return pending.BattleKey;
            }

            return GameRun.BuildRewardKey(_run.WeekIndex, _run.CurrentDay, actionContext);
        }

        private void ApplyNormalMealBonus()
        {
            if (_run.MealBonusRemaining <= 0)
            {
                return;
            }

            var itemRuntime = new ItemRuntime(_run);
            int bonusGold = itemRuntime.MealBonusGoldPerMeal();
            if (bonusGold != 0)
            {
                _run.Gold += bonusGold;
                itemRuntime.FlashTriggered(m => m.ItemId == "item_gold_meal_bonus");
            }

            _run.ConsumeMealBonusMeal();
            itemRuntime.RefreshIconState(m => m.ItemId == "item_gold_meal_bonus");
            itemRuntime.RefreshInfoText(m => m.ItemId == "item_gold_meal_bonus");
        }

        private void SettleFoodPassives(
            cfg.Food food,
            ActionExecutionContext actionContext,
            string settledBattleKey,
            bool survived)
        {
            if (food == null)
            {
                return;
            }

            string rewardSeedKey = $"{settledBattleKey}_food_settlement";
            IRandomStream rng = _run.Random.DomainStream(SeedDomains.Reward, rewardSeedKey);
            IReadOnlyList<FoodSettlementReward> rewards =
                new ItemRuntime(_run).OnFoodBattleSettled(actionContext, survived, rng);
            for (int i = 0; i < rewards.Count; i++)
            {
                FoodSettlementReward reward = rewards[i];
                _run.EnqueueGenericRewardOffer(
                    $"{rewardSeedKey}_{reward.SourceItemId}_{i}",
                    reward.Title,
                    reward.Offer);
            }

            if (rewards.Count > 0)
            {
                _run.PendingGenericRewardContinuation = PendingGenericRewardContinuationKind.Battle;
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

        /// <summary>RewardForm 发奖确认后回调：继续经营挑战后的编排续接。</summary>
        public void OnRewardConfirmed()
        {
            ContinueBattleWin();
        }

        /// <summary>抽奖机奖励领取完毕：恢复同一 pending 行动，不走经营挑战/事件完成逻辑。</summary>
        public void OnSlotRewardConfirmed()
        {
            PendingActionExecutionSaveData data = _run.GetPendingActionExecution();
            ActionExecutionContext context = RestoreActionContext(data);
            if (data == null
                || data.OutcomeKind != ActionOutcomeKind.Slot
                || context == null
                || !context.IsValid)
            {
                _run.PendingGenericRewardContinuation = PendingGenericRewardContinuationKind.None;
                _run.ClearPendingActionExecution();
                _run?.RequestSave();
                PromptNextAction();
                return;
            }

            _run.PendingGenericRewardContinuation = PendingGenericRewardContinuationKind.None;
            _run.SetPendingSlotExecutionState(
                data.SlotEventId,
                data.SlotSpinsUsed,
                SlotExecutionStage.Ready);
            _run?.RequestSave();

            ActionOutcome outcome = ActionOutcome.Slot(data.SlotEventId);
            ResolveSlotAction(
                context,
                BuildRecoveredPendingContinuation(context, outcome),
                outcome,
                restoringPending: true);
        }

        /// <summary>事件奖励领取完毕：先完成并退出事件，再提交行动和推进时间轴。</summary>
        public void OnEventRewardConfirmed()
        {
            Action cb = _afterEventReward;
            _afterEventReward = null;
            if (cb != null)
            {
                cb.Invoke();
                return;
            }

            ContinueAfterRecoveredEventReward();
        }

        private void ContinueAfterRecoveredEventReward()
        {
            PendingActionExecutionSaveData pending = _run?.GetPendingActionExecution();
            cfg.GameEvent ev = pending?.OutcomeKind == ActionOutcomeKind.Event
                && !string.IsNullOrEmpty(pending.EventId)
                    ? _run.Tables?.TbEvent.GetOrDefault(pending.EventId)
                    : null;
            EventService.OnEventFinished(_run, ev, EventResolveResult.Immediate(string.Empty));
            if (_run != null)
            {
                _run.PendingGenericRewardContinuation = PendingGenericRewardContinuationKind.None;
                _run?.RequestSave();
            }

            if (pending == null)
            {
                DrainPendingExtraNodes(PromptNextAction);
                return;
            }

            DrainPendingExtraNodes(ContinueAfterRecoveredBattleReward);
        }

        private void ContinueBattleWin()
        {
            Action cb = _afterBattleWin;
            _afterBattleWin = null;
            _beforeBattleReward = null;
            CurrentBattleActionContext = null;
            _currentBattleIsBoss = false;
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
            _view.OpenRewardForm(RewardFormOpenArgs.BattleReward());
        }

        private void OpenPendingHeartBreak()
        {
            CurrentBattleActionContext = _run.LastActionContext;
            _afterBattleWin = ContinueAfterRecoveredBattleReward;
            _beforeBattleReward = null;
            if (_run.HasPendingRewardBattleView)
            {
                _view.RestorePendingRewardBattleView();
            }

            ShowPendingHeartBreak();
        }

        private void ShowPendingHeartBreak()
        {
            PendingHeartBreakSaveData pending = _run.GetPendingHeartBreak();
            if (pending == null)
            {
                return;
            }

            var args = new HeartBreakFormOpenArgs(
                pending.BeforeHeartCount,
                pending.AfterHeartCount,
                _run.HeartCapacity,
                pending.IsTerminal);
            _view.ShowHeartBreak(args, () => OnHeartBreakComplete(pending));
        }

        private void OnHeartBreakComplete(PendingHeartBreakSaveData pending)
        {
            if (pending == null)
            {
                return;
            }

            if (pending.IsTerminal)
            {
                _view.ShowRunResult(false, pending.BattleTotalBig?.GetValue(pending.BattleTotal) ?? pending.BattleTotal);
                return;
            }

            _run.ClearPendingHeartBreak();
            _run?.RequestSave();
            _view.OpenRewardForm(RewardFormOpenArgs.BattleReward());
        }

        private void ContinueAfterRecoveredBattleReward()
        {
            ActionExecutionContext context = _run.LastActionContext;
            if (context == null || context.Action == null)
            {
                _run.ClearPendingActionExecution();
                _run?.RequestSave();
                PromptNextAction();
                return;
            }

            if (!string.IsNullOrEmpty(context.SourceKey))
            {
                if (FoodService.IsBossAction(_run.Tables, context.Action))
                {
                    cfg.Food boss = FoodService.ResolveBoss(_run, context.Action);
                    MarkBossCompletedAndApplyGold(boss?.Id);
                }

                ContinueRecoveredTimelineNodePass(context);
                return;
            }

            _run.ClearPendingActionExecution();
            float previousDay = ActionExecutor.Commit(_run, context);
            _run?.RequestSave();
            ResolveNodes(PromptNextAction, previousDay);
        }

        private void EnsurePendingBattleReward(ActionExecutionContext actionContext)
        {
            string rewardKey = GameRun.BuildRewardKey(_run.WeekIndex, _run.CurrentDay, actionContext);
            RewardOffer offer = _run.GetPendingRewardOffer(rewardKey);
            if (offer == null)
            {
                IRandomStream rng = _run.Random.DomainStream(SeedDomains.Reward, rewardKey);
                offer = RewardGranter.GenerateOffer(_run, _run.CurrentWeek, rng, actionContext);
                _run.SetPendingRewardOffer(rewardKey, offer);
            }

            _view.SavePendingRewardBattleView();
            _run?.RequestSave();
        }

        /// <summary>时间轴走完：完成周末兼容结算并推进到下一周。</summary>
        private void EndWeek()
        {
            ApplyEndOfWeekItemSettlement();
            _run.IncrementWeek();
            _run.Execution.Telemetry.Track(
                () => GameAnalyticsService.TrackRunCheckpoint(_run, _run.WeekIndex, 0));
            _run.RequiredScoreOverride = -1;
            _run?.RequestSave();
            BeginWeek();
        }

        /// <summary>
        /// 周末兼容结算：旧存档遗留债务仍会扣除，最后按「保底基金」补足下限。
        /// 新获得的高利贷与月光族均由周末时间轴节点结算，不再走这里。
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

        private void ResolveNodes(Action onDone, float? presentationFromDay = null)
        {
            _afterNodes = onDone;
            _timelinePresentationTarget = _run?.CurrentDay ?? 0f;
            _timelinePresentationCursor = presentationFromDay.HasValue
                ? System.Math.Max(0f, presentationFromDay.Value)
                : _timelinePresentationTarget;
            _timelinePresentationActive = presentationFromDay.HasValue
                && _timelinePresentationTarget > _timelinePresentationCursor + TimelineMath.Epsilon;
            if (_timelinePresentationActive)
            {
                _view.BeginTimelineAdvanceSequence(_timelinePresentationCursor);
                float fromDay = _timelinePresentationCursor;
                _timelinePresentationCursor = _timelinePresentationTarget;
                _view.PlayTimelineAdvance(
                    fromDay,
                    _timelinePresentationTarget,
                    null,
                    Once(ProcessNextNode));
                return;
            }

            ProcessNextNode();
        }

        private void ProcessNextNode()
        {
            // 每个节点完整结束后重新读取运行态时间轴。这样节点内容期间新增的
            // 当前日节点会按日期/创建顺序插入，且不会被旧的队列快照漏掉。
            cfg.TimelineNode node = TimelineService.GetNextDueUntriggeredNode(_run);
            if (node == null)
            {
                FinishTimelineNodeSequence();
                return;
            }

            ProcessArrivedNode(node);
        }

        private void ProcessArrivedNode(cfg.TimelineNode node)
        {
            if (node == null || TimelineService.GetNode(_run, node.Id) == null)
            {
                ProcessNextNode();
                return;
            }

            cfg.GameAction action = TimelineService.NodeAction(_run, node);
            if (action == null)
            {
                _run.MarkNodeTriggered(node.Id);
                _view.PlayTimelineNodeCue(
                    node.Id,
                    TimelinePresentationCueKind.TriggerStart,
                    Once(() => _view.PlayTimelineNodeCue(
                        node.Id,
                        TimelinePresentationCueKind.TriggerComplete,
                        Once(ProcessNextNode))));
                return;
            }

            _view.PlayTimelineNodeCue(
                node.Id,
                TimelinePresentationCueKind.TriggerStart,
                Once(() => ProcessTriggeredNode(node, action)));
        }

        private void ProcessTriggeredNode(cfg.TimelineNode node, cfg.GameAction action)
        {
            if (node == null || action == null)
            {
                ProcessNextNode();
                return;
            }

            var itemRuntime = new ItemRuntime(_run);
            if (itemRuntime.TryConsumeTimelineSkip(action.Behavior))
            {
                TimelineMutationResult mutation = PassiveTimelineMutationService.SkipNode(
                    _run,
                    "停业整顿",
                    node.Id);
                if (!mutation.Changed)
                {
                    _run.MarkNodeTriggered(node.Id);
                    _run?.RequestSave();
                    ProcessNextNode();
                    return;
                }

                _run?.RequestSave();
                _view.ShowTimelineNodeSkipped(node, mutation, ProcessNextNode);
                return;
            }

            // 节点即「放置来源的原子行动」：先展示放置行动卡，玩家点击后走与普通行动完全相同的执行路径。
            _presentedTimelineNodeId = node.Id;
            _view.ShowTimelineNodeCard(node, InterestMaxGain(), () => ExecutePlacedAction(node, action));
        }

        private void FinishTimelineNodeSequence()
        {
            _timelinePresentationActive = false;
            _view.EndTimelineAdvanceSequence();
            _run?.RequestSave();
            Action cb = _afterNodes;
            _afterNodes = null;
            cb?.Invoke();
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

            _run?.RequestSave();
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

            _view.PlayTimelineNodeCue(
                node.Id,
                TimelinePresentationCueKind.TriggerStart,
                Once(() =>
                {
                    var context = new ActionExecutionContext(action)
                    {
                        SourceKey = node.Id,
                        TargetScoreDayOverride = FoodService.IsBossAction(_run?.Tables, action)
                            ? (float?)node.Day
                            : null,
                        IsExtraTimelineExecution = true,
                    };
                    IRandomStream rng = _run.Random.DomainStream(
                        SeedDomains.Effect,
                        $"extra_node_w{_run.WeekIndex}_{node.Id}_{action.Id}");
                    ActionOutcome outcome = ActionExecutor.Execute(_run, context, rng);
                    DispatchOutcome(
                        outcome,
                        context,
                        () =>
                        {
                            _run.ClearPendingActionExecution();
                            _run?.RequestSave();
                            _view.PlayTimelineNodeCue(
                                node.Id,
                                TimelinePresentationCueKind.TriggerComplete,
                                Once(onDone));
                        },
                        () => _run.ClearPendingActionExecution());
                }));
        }

        /// <summary>放置行动执行：与普通行动共用 <see cref="ActionExecutor"/> 与 <see cref="DispatchOutcome"/>，节点不消耗天数/步数。</summary>
        private void ExecutePlacedAction(cfg.TimelineNode node, cfg.GameAction action)
        {
            bool wasPresented = node != null
                && string.Equals(_presentedTimelineNodeId, node.Id, StringComparison.Ordinal);
            if (wasPresented)
            {
                _presentedTimelineNodeId = string.Empty;
            }

            if (node == null || TimelineService.GetNode(_run, node.Id) == null)
            {
                if (wasPresented)
                {
                    ProcessNextNode();
                }

                return;
            }

            int repeatTotal = FoodService.IsBossAction(_run?.Tables, action)
                ? 1
                : new ItemRuntime(_run).TimelineNodeRepeatCount(action.Behavior);
            ExecutePlacedActionPass(node, action, 1, repeatTotal, ProcessNextNode);
        }

        private void ExecutePlacedActionPass(
            cfg.TimelineNode node,
            cfg.GameAction action,
            int repeatIndex,
            int repeatTotal,
            Action onNodeDone)
        {
            if (node == null || action == null)
            {
                onNodeDone?.Invoke();
                return;
            }

            var context = new ActionExecutionContext(action)
            {
                SourceKey = node.Id,
                NodeRepeatIndex = System.Math.Max(1, repeatIndex),
                NodeRepeatTotal = System.Math.Max(1, repeatTotal),
            };
            if (FoodService.IsBossAction(_run?.Tables, action))
            {
                context.TargetScoreDayOverride = node.Day;
            }

            IRandomStream rng = _run.Random.DomainStream(
                SeedDomains.Effect,
                $"node_w{_run.WeekIndex}_{node.Id}_{action.Id}_repeat{context.NodeRepeatIndex}");
            ActionOutcome outcome = ActionExecutor.Execute(_run, context, rng);
            DispatchOutcome(
                outcome,
                context,
                () => ContinueTimelineNodePass(node, action, context, onNodeDone),
                () => _run.ClearPendingActionExecution());
        }

        private void ContinueTimelineNodePass(
            cfg.TimelineNode node,
            cfg.GameAction action,
            ActionExecutionContext context,
            Action onNodeDone)
        {
            _run.ClearPendingActionExecution();
            int repeatIndex = System.Math.Max(1, context?.NodeRepeatIndex ?? 1);
            int repeatTotal = System.Math.Max(repeatIndex, context?.NodeRepeatTotal ?? 1);
            bool wasTriggered = _run.IsNodeTriggered(node?.Id);
            if (node != null)
            {
                // 第一轮就是节点本身的正常执行；完成后立即结算节点并切成灰色。
                _run.MarkNodeTriggered(node.Id);
            }

            _run?.RequestSave();
            if (repeatIndex < repeatTotal)
            {
                Action showRepeat = () => ActivateAndShowNextTimelineNodeRepeatCard(
                    node,
                    action,
                    repeatIndex + 1,
                    repeatTotal,
                    onNodeDone);
                if (!wasTriggered && node != null)
                {
                    _view.PlayTimelineNodeCue(
                        node.Id,
                        TimelinePresentationCueKind.TriggerComplete,
                        Once(showRepeat));
                }
                else
                {
                    showRepeat();
                }

                return;
            }

            if (!wasTriggered && node != null)
            {
                _view.PlayTimelineNodeCue(
                    node.Id,
                    TimelinePresentationCueKind.TriggerComplete,
                    Once(onNodeDone));
            }
            else
            {
                onNodeDone?.Invoke();
            }
        }

        private void ContinueRecoveredTimelineNodePass(ActionExecutionContext context)
        {
            _run.ClearPendingActionExecution();
            if (context == null || string.IsNullOrEmpty(context.SourceKey))
            {
                _run?.RequestSave();
                PromptNextAction();
                return;
            }

            if (context.IsExtraTimelineExecution)
            {
                _run?.RequestSave();
                _view.PlayTimelineNodeCue(
                    context.SourceKey,
                    TimelinePresentationCueKind.TriggerComplete,
                    Once(() => DrainPendingExtraNodes(PromptNextAction)));
                return;
            }

            cfg.TimelineNode node = TimelineService.GetNode(_run, context.SourceKey);
            cfg.GameAction action = node != null ? TimelineService.NodeAction(_run, node) : null;
            if (node != null && action != null)
            {
                ContinueTimelineNodePass(node, action, context, PromptNextAction);
                return;
            }

            if (node != null)
            {
                _run.MarkNodeTriggered(node.Id);
            }

            _run?.RequestSave();
            if (node != null)
            {
                _view.PlayTimelineNodeCue(
                    node.Id,
                    TimelinePresentationCueKind.TriggerComplete,
                    Once(() => DrainPendingExtraNodes(PromptNextAction)));
            }
            else
            {
                DrainPendingExtraNodes(PromptNextAction);
            }
        }

        /// <summary>第一轮节点已置灰后，由被动道具激活表现引出下一轮节点卡。</summary>
        private void ActivateAndShowNextTimelineNodeRepeatCard(
            cfg.TimelineNode node,
            cfg.GameAction action,
            int repeatIndex,
            int repeatTotal,
            Action onNodeDone)
        {
            _run?.RequestSave();
            new ItemRuntime(_run).FlashTriggered(
                model => model.TimelineNodeRepeatCount(action.Behavior) > 1);
            _view.ShowTimelineNodeCard(
                node,
                InterestMaxGain(),
                () => ExecutePlacedActionPass(node, action, repeatIndex, repeatTotal, onNodeDone));
        }

        /// <summary>节点已置灰、下一轮卡片尚未点击时的读档恢复。</summary>
        private bool ResumePendingTimelineNodeRepeat()
        {
            ActionExecutionContext previous = _run?.LastActionContext;
            int repeatIndex = System.Math.Max(1, previous?.NodeRepeatIndex ?? 1);
            int repeatTotal = System.Math.Max(repeatIndex, previous?.NodeRepeatTotal ?? 1);
            if (previous?.Action == null
                || previous.IsExtraTimelineExecution
                || string.IsNullOrEmpty(previous.SourceKey)
                || repeatIndex >= repeatTotal
                || !_run.IsNodeTriggered(previous.SourceKey))
            {
                return false;
            }

            cfg.TimelineNode node = TimelineService.GetNode(_run, previous.SourceKey);
            cfg.GameAction action = node != null ? TimelineService.NodeAction(_run, node) : null;
            if (action == null
                || !string.Equals(action.Id, previous.Action.Id, StringComparison.Ordinal))
            {
                return false;
            }

            ActivateAndShowNextTimelineNodeRepeatCard(
                node,
                action,
                repeatIndex + 1,
                repeatTotal,
                ProcessNextNode);
            return true;
        }

        private static Action Once(Action callback)
        {
            bool invoked = false;
            return () =>
            {
                if (invoked)
                {
                    return;
                }

                invoked = true;
                callback?.Invoke();
            };
        }

        private int InterestMaxGain()
        {
            return _run != null ? _run.InterestCap : 0;
        }

        /// <summary>行动执行结果的统一续接：普通行动与放置节点共用；Boss 战领奖后推进/通关。</summary>
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

                    _view.ShowNotice(title, outcome.Feedback, onContinue);
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
                case ActionOutcomeKind.Slot:
                    ResolveSlotAction(context, onContinue, outcome, restoringPending);
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
            _run?.RequestSave();
        }

        private void StartBossBattle(
            ActionOutcome outcome,
            ActionExecutionContext context,
            Action onContinue,
            Action onBossComplete)
        {
            cfg.Tables tables = _run.Tables;
            cfg.Food boss = tables.TbFood.GetOrDefault(outcome.BossId);
            _run.MarkBossDebuffRolled(outcome.BossDebuffId);
            StartBattle(
                outcome.RequiredScore,
                outcome.Modifier,
                outcome.BattleKey,
                true,
                outcome.BossDebuffId,
                onContinue,
                context,
                beforeReward: () => CompleteBossBeforeReward(outcome, boss, onBossComplete));
        }

        private void CompleteBossBeforeReward(ActionOutcome outcome, cfg.Food boss, Action onBossComplete)
        {
            _run.ClearPendingActionExecution();
            onBossComplete?.Invoke();

            string bossId = !string.IsNullOrEmpty(outcome?.BossId)
                ? outcome.BossId
                : boss?.Id ?? string.Empty;
            MarkBossCompletedAndApplyGold(bossId);
            _run?.RequestSave();
        }

        private void MarkBossCompletedAndApplyGold(string bossId)
        {
            if (string.IsNullOrEmpty(bossId))
            {
                return;
            }

            if (!_run.IsBossCompleted(bossId))
            {
                _run.MarkBossCompleted(bossId);
            }

            ApplyBossCompleteGold();
        }

        private void ApplyBossCompleteGold()
        {
            // Boss 赏金（GoldOnBossComplete）：通关本次 Boss 后额外获得金币。
            var itemRuntime = new ItemRuntime(_run);
            int bossGold = itemRuntime.ClaimBossCompleteGold();
            if (bossGold <= 0)
            {
                return;
            }

            _run.Gold += bossGold;
        }

        private bool IsConfiguredFinalWeek()
        {
            return _run != null
                && !_run.IsEndless
                && _run.WeekIndex == _run.TotalWeeks;
        }

        /// <summary>
        /// 最终周的所有到期节点都已结算后，再执行旧存档兼容结算并按红心决定胜负。
        /// </summary>
        private void CompleteFinalWeek()
        {
            ApplyEndOfWeekItemSettlement();
            _run?.RequestSave();
            if (_run.HeartsRemaining > 0)
            {
                OnVictory();
                return;
            }

            _view.HideBattleWorld();
            _view.ShowRunResult(false, _view.LastBattleTotal);
        }

        private void ResolveSlotAction(
            ActionExecutionContext context,
            Action onDone,
            ActionOutcome outcome,
            bool restoringPending)
        {
            if (!restoringPending)
            {
                SavePendingActionExecution(context, outcome);
            }

            PendingActionExecutionSaveData data = _run.GetPendingActionExecution();
            if (data == null || data.OutcomeKind != ActionOutcomeKind.Slot)
            {
                FinishSlotAction(onDone);
                return;
            }

            string savedSlotEventId = !string.IsNullOrWhiteSpace(data.SlotEventId)
                ? data.SlotEventId
                : outcome?.SlotEventId;
            if (!SlotService.TryGetConfig(
                    _run,
                    context?.Action,
                    savedSlotEventId,
                    out SlotMachineConfig config,
                    out string error))
            {
                _view.ShowNotice(
                    context?.Action?.Name ?? "抽奖机",
                    string.IsNullOrWhiteSpace(error) ? "抽奖机配置无效，本次节点已结束。" : error,
                    () => FinishSlotAction(onDone));
                return;
            }

            // 奖励队列比 pending action 更早恢复。若仍有奖励则继续领奖；若队列已清空，
            // 说明奖励已经领取但回调尚未执行，直接恢复 Ready，避免重抽或重复扣费。
            if (data.SlotStage == SlotExecutionStage.AwaitingReward)
            {
                if (_run.HasPendingGenericRewards)
                {
                    _run.PendingGenericRewardContinuation = PendingGenericRewardContinuationKind.Slot;
                    _run?.RequestSave();
                    _view.OpenRewardForm(
                        RewardFormOpenArgs.GenericQueue(PendingGenericRewardContinuationKind.Slot));
                    return;
                }

                _run.PendingGenericRewardContinuation = PendingGenericRewardContinuationKind.None;
                _run.SetPendingSlotExecutionState(
                    config.Event.Id,
                    data.SlotSpinsUsed,
                    SlotExecutionStage.Ready);
                _run?.RequestSave();
                data = _run.GetPendingActionExecution();
            }

            if (data.SlotStage == SlotExecutionStage.EmptyResult)
            {
                ShowSlotEmptyResult(context, config, data.SlotSpinsUsed, onDone);
                return;
            }

            if (data.SlotSpinsUsed >= config.MaxSpins)
            {
                FinishSlotAction(onDone);
                return;
            }

            ShowSlotMachine(context, config, data.SlotSpinsUsed, onDone);
        }

        private void ShowSlotMachine(
            ActionExecutionContext context,
            SlotMachineConfig config,
            int spinsUsed,
            Action onDone)
        {
            int safeSpins = System.Math.Max(0, System.Math.Min(spinsUsed, config.MaxSpins));
            int cost = SlotService.CostForNextSpin(_run, config, safeSpins);
            bool canAfford = cost <= 0 || _run.Gold >= cost;
            bool canSpin = safeSpins < config.MaxSpins && canAfford;
            var optionTexts = new List<string>(config.Options.Count);
            var optionEnabled = new List<bool>(config.Options.Count);
            for (int i = 0; i < config.Options.Count; i++)
            {
                cfg.EventOption option = config.Options[i];
                optionTexts.Add(
                    SlotService.FormatOptionText(
                        _run,
                        config,
                        option,
                        safeSpins,
                        canAfford));
                optionEnabled.Add(
                    option?.Id == SlotService.SpinOptionId
                        ? canSpin && PreconditionEvaluator.IsSatisfied(_run, option.Condition)
                        : option?.Id == SlotService.LeaveOptionId);
            }

            _view.ShowEventPage(
                config.Event.Name,
                EventService.FormatRuntimeText(_run, config.Event.Desc),
                string.Empty,
                config.Event.BgSprite,
                optionTexts,
                Array.Empty<string>(),
                optionEnabled,
                index =>
                {
                    if (index < 0 || index >= config.Options.Count)
                    {
                        return;
                    }

                    cfg.EventOption selected = config.Options[index];
                    if (selected?.Id == SlotService.SpinOptionId && optionEnabled[index])
                    {
                        SpinSlot(context, config, safeSpins, onDone);
                    }
                    else if (selected?.Id == SlotService.LeaveOptionId)
                    {
                        FinishSlotAction(onDone);
                    }
                },
                onEnd: null);
        }

        private void SpinSlot(
            ActionExecutionContext context,
            SlotMachineConfig config,
            int expectedSpinsUsed,
            Action onDone)
        {
            PendingActionExecutionSaveData data = _run.GetPendingActionExecution();
            if (data == null
                || data.OutcomeKind != ActionOutcomeKind.Slot
                || data.SlotStage != SlotExecutionStage.Ready
                || data.SlotSpinsUsed != expectedSpinsUsed
                || expectedSpinsUsed >= config.MaxSpins)
            {
                ResolveSlotAction(
                    context,
                    onDone,
                    ActionOutcome.Slot(config.Event.Id),
                    restoringPending: true);
                return;
            }

            int cost = SlotService.CostForNextSpin(_run, config, expectedSpinsUsed);
            if (cost > 0 && _run.Gold < cost)
            {
                ShowSlotMachine(context, config, expectedSpinsUsed, onDone);
                return;
            }

            int spinIndex = expectedSpinsUsed + 1;
            string spinKey = SlotService.BuildSpinKey(_run, context, spinIndex);
            IRandomStream rng = _run.Random?.DomainStream(SeedDomains.Slot, spinKey);
            SlotSpinResult result = SlotService.Roll(_run, config, rng, context);
            if (!result.Success)
            {
                _view.ShowNotice(
                    config.Event.Name,
                    string.IsNullOrWhiteSpace(result.Error) ? "抽奖失败，请检查配置。" : result.Error,
                    () => ShowSlotMachine(context, config, expectedSpinsUsed, onDone));
                return;
            }

            if (cost > 0)
            {
                _run.Gold -= cost;
            }

            if (result.IsEmpty)
            {
                _run.SetPendingSlotExecutionState(
                    config.Event.Id,
                    spinIndex,
                    SlotExecutionStage.EmptyResult);
                _run?.RequestSave();
                ShowSlotEmptyResult(context, config, spinIndex, onDone);
                return;
            }

            string rewardKey = $"slot_{spinKey}";
            _run.SetPendingSlotExecutionState(
                config.Event.Id,
                spinIndex,
                SlotExecutionStage.AwaitingReward,
                rewardKey);
            _run.EnqueueGenericRewardOffer(rewardKey, config.Event.Name, result.Offer);
            _run.PendingGenericRewardContinuation = PendingGenericRewardContinuationKind.Slot;
            _run?.RequestSave();
            _view.OpenRewardForm(
                RewardFormOpenArgs.GenericQueue(PendingGenericRewardContinuationKind.Slot));
        }

        private void ShowSlotEmptyResult(
            ActionExecutionContext context,
            SlotMachineConfig config,
            int spinsUsed,
            Action onDone)
        {
            bool finished = spinsUsed >= config.MaxSpins;
            string text = string.IsNullOrWhiteSpace(config.Event.ResultText)
                ? "这次什么也没有"
                : config.Event.ResultText;
            _view.ShowEventPage(
                config.Event.Name,
                EventService.FormatRuntimeText(_run, text),
                finished ? "结束" : "继续",
                config.Event.BgSprite,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<bool>(),
                onPick: null,
                onEnd: () =>
                {
                    PendingActionExecutionSaveData current = _run.GetPendingActionExecution();
                    if (current == null
                        || current.OutcomeKind != ActionOutcomeKind.Slot
                        || current.SlotStage != SlotExecutionStage.EmptyResult
                        || current.SlotSpinsUsed != spinsUsed)
                    {
                        return;
                    }

                    _run.SetPendingSlotExecutionState(
                        config.Event.Id,
                        spinsUsed,
                        SlotExecutionStage.Ready);
                    _run?.RequestSave();
                    if (finished)
                    {
                        FinishSlotAction(onDone);
                    }
                    else
                    {
                        ShowSlotMachine(context, config, spinsUsed, onDone);
                    }
                });
        }

        private void FinishSlotAction(Action onDone)
        {
            if (_run.PendingGenericRewardContinuation == PendingGenericRewardContinuationKind.Slot)
            {
                _run.PendingGenericRewardContinuation = PendingGenericRewardContinuationKind.None;
            }

            if (onDone != null)
            {
                onDone.Invoke();
                return;
            }

            _run.ClearPendingActionExecution();
            _run?.RequestSave();
            PromptNextAction();
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
                ? $"node_{context.SourceKey}_repeat{System.Math.Max(1, context.NodeRepeatIndex)}"
                : $"action_w{_run.WeekIndex}_d{DayKey(_run.CurrentDay)}_s{_run.ActionStepIndex}";

            IRandomStream rng = _run.Random.DomainStream(SeedDomains.Event, seedKey);
            cfg.GameEvent ev = string.IsNullOrEmpty(outcome?.EventId)
                ? null
                : _run.Tables.TbEvent.GetOrDefault(outcome.EventId);
            ev ??= behavior == cfg.ActionBehavior.Event
                ? EventService.RollActionEventWithGuarantee(_run, rng)
                : EventService.RollEvent(_run, rng, behavior);
            if (ev != null)
            {
                SavePendingActionExecution(context, outcome, ev.Id);
                GrantEventEntryGoldOnce();
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
                _run?.RequestSave();
                onDone?.Invoke();
                return;
            }

            // 厨神像直领奖励在表现前已经完成结算并存档。若期间中断，直接完成行动，
            // 避免恢复事件页后再次领取金币和负面装饰品。
            if (string.Equals(eventId, KitchenGodStatueEventId, StringComparison.Ordinal)
                && _run.HasUsedEvent(eventId))
            {
                onDone?.Invoke();
                return;
            }

            if (!restoringPending)
            {
                SavePendingActionExecution(context, outcome, ev.Id);
            }

            GrantEventEntryGoldOnce();

            ResolveEvent(ev, onDone);
        }

        /// <summary>
        /// 「事件红包」在根事件进入时发放。标记与金币一起保存，保证恢复同一事件页时不重复发放；
        /// Slot 使用独立流程，不会经过这里。
        /// </summary>
        private void GrantEventEntryGoldOnce()
        {
            if (!EventService.TryGrantEventEntryGold(_run, out _))
            {
                return;
            }

            _run?.RequestSave();
        }

        private void ResolveEvent(cfg.GameEvent ev, Action onDone)
        {
            if (ev == null)
            {
                onDone?.Invoke();
                return;
            }

            // 一个事件复用同一条随机流（Gamble 等随机效果按序派生），parentId 串起后续选项页。
            IRandomStream rng = _run.Random.DomainStream(SeedDomains.Event, $"resolve_w{_run.WeekIndex}_d{DayKey(_run.CurrentDay)}_{ev.Id}");
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

            if (pageOptions.Count == 0)
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

            if (!hasEnabledOption)
            {
                ShowEventPage(
                    ev,
                    pageDescription,
                    "离开",
                    pageOptions,
                    optionEnabled,
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

                    if (index >= shownEnabled.Count
                        || !shownEnabled[index]
                        || !PreconditionEvaluator.IsSatisfied(_run, shown[index].Condition))
                    {
                        EnterEventPage(ev, pageDescription, shown, rng, onDone);
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

            if (string.Equals(
                    option?.Id,
                    KitchenGodDirectNegativeOptionId,
                    StringComparison.Ordinal))
            {
                ResolveKitchenGodDirectNegativeOption(ev, option, rng, onDone);
                return;
            }

            EventResolveResult result = EventService.ResolveOption(_run, option, rng);
            ContinueResolvedEventOption(ev, pageDescription, option, result, rng, onDone);
        }

        private void ResolveKitchenGodDirectNegativeOption(
            cfg.GameEvent ev,
            cfg.EventOption option,
            IRandomStream rng,
            Action onDone)
        {
            EventResolveResult result = EventService.ResolveOption(
                _run,
                option,
                rng,
                cfg.EffectType.GrantRandomPassiveItems);
            RandomizedItemResult directReward = EffectResolver.GrantRandomNegativePassiveDirect(
                _run,
                rng,
                fallbackGold: 40);

            // 金币和装饰品都已到账；事件也先标记完成再落盘，避免中断恢复时重复领取。
            EventService.OnEventFinished(_run, ev, result);
            _run?.RequestSave();
            _view.ShowDirectPassiveItemAcquire(
                directReward?.Item,
                directReward?.AcquireResult ?? default,
                () => ExitEventAndContinueAfterRewards(onDone));
        }

        private void ContinueResolvedEventOption(
            cfg.GameEvent ev,
            string pageDescription,
            cfg.EventOption option,
            EventResolveResult result,
            IRandomStream rng,
            Action onDone,
            bool effectPresentationComplete = false)
        {
            result ??= EventResolveResult.Immediate(string.Empty);

            if (!effectPresentationComplete
                && HasEventEffectPresentation(option, result))
            {
                PresentEventEffects(
                    ev,
                    option,
                    result,
                    () => ContinueResolvedEventOption(
                        ev,
                        pageDescription,
                        option,
                        result,
                        rng,
                        onDone,
                        effectPresentationComplete: true));
                return;
            }

            // 经营挑战、商店和结局节点是立即终点，不再显示普通结果按钮。
            if (result.FollowUpKind != EventFollowUpKind.None)
            {
                EventService.OnEventFinished(_run, ev, result);
                ContinueEventResult(ev.Name, ev.Id, result, onDone);
                return;
            }

            if (option.AutoEnd)
            {
                FinishEventAndContinue(ev, result, onDone);
                return;
            }

            List<cfg.EventOption> children = EventService.GetChildOptions(_run, ev.Id, option.Id);
            if (children.Count > 0)
            {
                string nextDescription = ResolveEventPageText(option, result, pageDescription);
                if (EventService.TryRollWeightedChild(children, rng, out cfg.EventOption randomChild))
                {
                    if (!string.IsNullOrWhiteSpace(randomChild.BranchPageText))
                    {
                        nextDescription = randomChild.BranchPageText;
                    }

                    EnterEventPage(
                        ev,
                        nextDescription,
                        new List<cfg.EventOption> { randomChild },
                        rng,
                        onDone);
                    return;
                }

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

        private static bool HasEventEffectPresentation(
            cfg.EventOption option,
            EventResolveResult result)
        {
            return result?.RecipeMutation?.HasChanges == true
                || (option?.AutoEnd == true
                    && (!string.IsNullOrWhiteSpace(option.ResultText)
                        || !string.IsNullOrWhiteSpace(result?.Feedback)));
        }

        private void PresentEventEffects(
            cfg.GameEvent ev,
            cfg.EventOption option,
            EventResolveResult result,
            Action onComplete)
        {
            void PresentAutoEndFeedback()
            {
                if (option?.AutoEnd != true)
                {
                    onComplete?.Invoke();
                    return;
                }

                string message = ResolveEventPageText(option, result, string.Empty);
                if (string.IsNullOrWhiteSpace(message))
                {
                    onComplete?.Invoke();
                    return;
                }

                _view.ShowEventEffectFeedback(
                    ev?.Name ?? string.Empty,
                    EventService.FormatRuntimeText(_run, message),
                    ev?.BgSprite ?? string.Empty,
                    onComplete);
            }

            if (result?.RecipeMutation?.HasChanges == true)
            {
                _view.ShowEventRecipeMutation(
                    result.RecipeMutation,
                    PresentAutoEndFeedback);
                return;
            }

            PresentAutoEndFeedback();
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
                EventService.FormatRuntimeText(_run, option.Text),
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
            if (_run != null && _run.HasPendingGenericRewards)
            {
                _afterEventReward = () => CompleteEventAndExit(
                    ev,
                    result,
                    onDone,
                    persistEventRewardCompletion: true);
                _run.PendingGenericRewardContinuation = PendingGenericRewardContinuationKind.Event;
                _run?.RequestSave();
                _view.OpenRewardForm(
                    RewardFormOpenArgs.GenericQueue(PendingGenericRewardContinuationKind.Event));
                return;
            }

            CompleteEventAndExit(ev, result, onDone);
        }

        private void ExitEventAndContinueAfterRewards(Action onDone)
        {
            _view.ExitEventPage(() => DrainPendingExtraNodes(onDone));
        }

        private void CompleteEventAndExit(
            cfg.GameEvent ev,
            EventResolveResult result,
            Action onDone,
            bool persistEventRewardCompletion = false)
        {
            EventService.OnEventFinished(_run, ev, result);
            if (persistEventRewardCompletion && _run != null)
            {
                _run.PendingGenericRewardContinuation = PendingGenericRewardContinuationKind.None;
                _run?.RequestSave();
            }

            ExitEventAndContinueAfterRewards(onDone);
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
            var optionRequirements = new List<string>();
            if (options != null)
            {
                foreach (cfg.EventOption option in options)
                {
                    optionTexts.Add(EventService.FormatRuntimeText(_run, option.Text));
                    optionRequirements.Add(ResolveConditionText(_run, option));
                }
            }

            _view.ShowEventPage(
                ev != null ? ev.Name : string.Empty,
                EventService.FormatRuntimeText(
                    _run,
                    pageDescription ?? (ev != null ? ev.Desc : string.Empty)),
                EventService.FormatRuntimeText(_run, resultButtonText),
                ev != null ? ev.BgSprite : string.Empty,
                optionTexts,
                optionRequirements,
                optionEnabled,
                onPick,
                onEnd);
        }

        internal static string ResolveConditionText(GameRun run, cfg.EventOption option)
        {
            if (option == null || string.IsNullOrWhiteSpace(option.Condition))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(option.ConditionText))
            {
                UnityEngine.Debug.LogWarning(
                    $"事件选项 {option.Id} 配置了 condition，但未配置 conditionText。" +
                    "事件页将显示配置错误提示。");
                return "条件文案未配置";
            }

            return EventService.FormatRuntimeText(run, option.ConditionText);
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
                        null);
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

        private void ClearPendingNodes()
        {
            _afterNodes = null;
        }

        private void OpenShopThen(Action onClose)
        {
            OpenShopThen(onClose, null, null);
        }

        private void OpenShopThen(Action onClose, ActionExecutionContext context, ActionOutcome outcome)
        {
            _run?.BeginShopVisit();

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
            else
            {
                // 事件选项进入的商店复用原事件 pending，没有新的行动快照可写；
                // 仍需立即保存本次进入金币，避免中断恢复后丢失本次结算。
                _run?.RequestSave();
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
            string bossDebuffId,
            Action onWin,
            ActionExecutionContext actionContext = null,
            Action beforeReward = null)
        {
            _afterBattleWin = onWin;
            _beforeBattleReward = beforeReward;
            CurrentBattleActionContext = actionContext;
            _currentBattleIsBoss = isBoss;
            _view.HideResultPanel();
            _view.StartBattle(requiredScore, modifier, key, bossDebuffId, actionContext);
        }

        private void OnVictory()
        {
            _view.HideBattleWorld();
            _view.ShowRunResult(true, _view.LastBattleTotal);
        }

        private static void TrackBattleSettled(
            GameRun run,
            string battleKey,
            bool isBoss,
            string bossId,
            BigDouble total,
            bool passed,
            bool survived,
            bool terminal,
            int heartsRemaining)
        {
            run?.Execution?.Telemetry.Track(() =>
                GameAnalyticsService.TrackBattleSettled(
                    run,
                    battleKey,
                    isBoss,
                    bossId,
                    total,
                    passed,
                    survived,
                    terminal,
                    heartsRemaining));
        }

        private void TrackRunEnded(string reason, bool isDeath)
        {
            _run?.Execution?.Telemetry.Track(
                () => GameAnalyticsService.TrackRunEnded(_run, reason, isDeath));
        }
    }
}
