using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BreakInfinity;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Orchestration;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;

namespace GourmetProject.Game.Balance
{
    /// <summary>
    /// WeekLoopController 的无界面适配器。所有回调进入 trampoline 队列，避免同步 view
    /// 造成递归爆栈；决策随机与 run.Random 的正式规则随机完全分离。
    /// </summary>
    internal sealed class HeadlessWeekLoopView : IWeekLoopView
    {
        private const int CallbackGuard = 20000;

        private readonly GameRun _run;
        private readonly AutoRunRequest _request;
        private readonly AutoPlayerPolicy _policy;
        private readonly GameplayDatabase _database;
        private readonly Queue<Action> _callbacks = new Queue<Action>();
        private readonly IRandomStream _decisionRandom;
        private readonly IRandomStream _placementRandom;
        private readonly int _initialRecipeCount;
        private readonly HashSet<RecipeBookSlot> _initialRecipeEntries;
        private WeekLoopController _loop;
        private int _weekActionCount;
        private int _callbackCount;
        private bool _started;
        private bool _stopped;

        public HeadlessWeekLoopView(
            GameRun run,
            AutoRunRequest request,
            GameplayDatabase database,
            AutoRunTrace trace)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _request = request ?? throw new ArgumentNullException(nameof(request));
            _policy = request.Policy ?? new AutoPlayerPolicy();
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _initialRecipeCount = run.RecipeEntries.Count;
            _initialRecipeEntries = new HashSet<RecipeBookSlot>(run.RecipeEntries);
            Trace = trace ?? throw new ArgumentNullException(nameof(trace));
            _decisionRandom = new Xoshiro256SS(
                AutoRunPolicySeed.Derive(request.Seed, request.PlayerLevel, "decisions"));
            _placementRandom = new Xoshiro256SS(
                AutoRunPolicySeed.Derive(request.Seed, request.PlayerLevel, "placements"));
        }

        public AutoRunTrace Trace { get; }

        public BigDouble LastBattleTotal { get; private set; }

        public bool IsCompleted => _stopped;

        private AutoRunStageTrace Stage
        {
            get
            {
                int week = Math.Max(1, _run.WeekIndex);
                AutoRunStageTrace stage = Trace.Stages.LastOrDefault(v => v.Week == week);
                if (stage != null)
                {
                    return stage;
                }

                ArchetypeClassification classification = BuildArchetypeClassifier.Classify(
                    _run,
                    _policy.DualArchetypeThreshold);
                stage = new AutoRunStageTrace
                {
                    Week = week,
                    Archetype = classification.Primary,
                    MetaRoute = ResolveMetaRoute(_run, _request.MetaAffinity),
                    GoldBalance = _run.Gold,
                };
                if (Trace.Stages.Count > 0)
                {
                    BuildArchetype previous = Trace.Stages[Trace.Stages.Count - 1].Archetype;
                    stage.ArchetypeChanged = previous != stage.Archetype;
                    if (stage.ArchetypeChanged)
                    {
                        Trace.ArchetypeChanges++;
                    }
                }

                Trace.Stages.Add(stage);
                _weekActionCount = 0;
                return stage;
            }
        }

        public AutoRunTrace Execute()
        {
            Begin();
            while (!Step(CallbackGuard))
            {
            }

            return Trace;
        }

        /// <summary>
        /// 初始化正式周循环但不立即跑完整局，供编辑器按帧调度。
        /// </summary>
        public void Begin()
        {
            if (_started || _stopped)
            {
                return;
            }

            _started = true;
            _loop = new WeekLoopController(_run, this);
            Enqueue(_loop.BeginWeek);
        }

        /// <summary>
        /// 最多执行指定数量的正式流程回调；返回 true 表示本局已经终止。
        /// 决策之间的回调边界稳定，因此分帧方式不会改变规则或 policy 随机序列。
        /// </summary>
        public bool Step(int callbackBudget = 1)
        {
            Begin();
            int remaining = Math.Max(1, callbackBudget);
            while (!_stopped && _callbacks.Count > 0 && remaining-- > 0)
            {
                if (++_callbackCount > CallbackGuard)
                {
                    StopInvalid(
                        AutoRunTerminationKind.InfrastructureError,
                        "CALLBACK_GUARD",
                        "无界面周循环超过安全回调上限");
                    break;
                }

                Action callback = _callbacks.Dequeue();
                callback?.Invoke();
            }

            if (!_stopped
                && _callbacks.Count == 0
                && Trace.Termination == AutoRunTerminationKind.None)
            {
                StopInvalid(
                    AutoRunTerminationKind.InfrastructureError,
                    "FLOW_STALLED",
                    "正式周循环在产生胜负结果前停止");
            }

            return _stopped;
        }

        public void Cancel()
        {
            if (_stopped)
            {
                return;
            }

            Trace.Completed = false;
            Trace.Termination = AutoRunTerminationKind.Cancelled;
            Trace.FailureReason = "用户取消";
            if (Trace.Stages.Count > 0)
            {
                Trace.Stages[Trace.Stages.Count - 1].Termination = AutoRunTerminationKind.Cancelled;
            }

            _stopped = true;
            _callbacks.Clear();
        }

        public void HideBattleWorld() { }

        public void ResetBossBattlePresentation() { }

        public void SavePendingRewardBattleView() { }

        public void RestorePendingRewardBattleView() { }

        public void HideResultPanel() { }

        public void OpenWeekMap()
        {
            AutoRunStageTrace stage = Stage;
            if (!ValidateOwnedActiveItems())
            {
                return;
            }

            if (++_weekActionCount > Math.Max(1, _policy.MaxActionsPerWeek))
            {
                StopInvalid(
                    AutoRunTerminationKind.ActionLimitReached,
                    "ACTION_LIMIT",
                    $"第 {stage.Week} 周超过最大行动次数 {_policy.MaxActionsPerWeek}");
                return;
            }

            List<ActionChoice> choices = ActionOfferService.GetOrRoll(_run);
            UseActionSelectActiveItems(stage);
            // A schedule item may replace the pending action offer. Read it again after use.
            choices = ActionOfferService.GetOrRoll(_run);
            var actionDecision = new AutoRunActionDecisionTrace
            {
                Week = stage.Week,
                Day = _run.CurrentDay,
                OfferKey = _run.PendingActionChoiceKey ?? string.Empty,
                Revision = _run.PendingActionChoiceRevision,
                Rerolled = _run.PendingActionChoiceRevision > 0,
            };
            ActionChoice choice = PickAction(
                choices,
                stage.MetaRoute,
                stage,
                out string decisionTrace,
                actionDecision);
            stage.ActionDecisions.Add(actionDecision);
            if (choice != null)
            {
                stage.Actions.Add(choice.Action?.Id ?? string.Empty);
                stage.Actions.Add("决策:" + decisionTrace);
            }
            ActionOfferService.Resolve(_run);
            Enqueue(() => _loop.OnActionPicked(choice));
        }

        public void OpenShop()
        {
            AutoRunStageTrace stage = Stage;
            MetaRoute route = RefreshMetaRoute(stage);
            var session = new ShopSession(_run);
            TryDeleteObviousAcquiredFiller(stage);
            int reserve = route == MetaRoute.Interest
                ? Math.Max(0, _policy.InterestReserve)
                : 0;
            int guard = 0;
            while (++guard <= 128)
            {
                List<ShopEntry> legal = session.Stock
                    .Where(entry => entry != null
                        && entry.IsStocked
                        && ShopService.CurrentPrice(_run, entry) <= _run.Gold - ShopReserve(entry, reserve))
                    .ToList();
                if (legal.Count == 0)
                {
                    break;
                }

                var values = legal.Select(entry => ShopPurchasePriority(
                    entry,
                    route,
                    out _)).ToList();
                int local;
                if (_request.PlayerLevel == AutoPlayerLevel.Expert)
                {
                    List<int> worthwhile = Enumerable.Range(0, legal.Count)
                        .Where(index => ShopPurchasePriority(
                            legal[index],
                            route,
                            out bool shouldBuy) > 0f && shouldBuy)
                        .ToList();
                    if (worthwhile.Count == 0)
                    {
                        break;
                    }

                    local = worthwhile[IndexOfMax(worthwhile.Select(index => values[index]).ToList())];
                }
                else
                {
                    local = _decisionRandom.WeightedPickIndex(
                        values.Select(value => Math.Max(0.01f, value)).ToList());
                }

                if (values[local] <= 0.05f)
                {
                    break;
                }

                ShopPurchaseResult purchased = session.Purchase(legal[local]);
                if (!purchased.Success)
                {
                    break;
                }

                stage.GoldSpent += Math.Max(0, purchased.GoldBefore - purchased.GoldAfter);
                stage.Purchases.Add(purchased.Id);
                ResolvePendingFragment();
                DrainGenericRewards();
                if (_stopped || !ValidateOwnedActiveItems())
                {
                    return;
                }
            }

            Enqueue(_loop.OnShopClosed);
        }

        public void OpenRewardForm(RewardFormOpenArgs args)
        {
            if (args?.UseGenericQueue == true)
            {
                PendingGenericRewardContinuationKind continuation = args.GenericRewardContinuation;
                if (!DrainGenericRewards())
                {
                    return;
                }

                Enqueue(() =>
                {
                    switch (continuation)
                    {
                        case PendingGenericRewardContinuationKind.Battle:
                            _loop.OnRewardConfirmed();
                            break;
                        case PendingGenericRewardContinuationKind.Event:
                            _loop.OnEventRewardConfirmed();
                            break;
                        case PendingGenericRewardContinuationKind.Slot:
                            _loop.OnSlotRewardConfirmed();
                            break;
                    }
                });
                return;
            }

            RewardOffer offer = _run.GetPendingRewardOffer();
            if (offer == null)
            {
                StopInvalid(
                    AutoRunTerminationKind.InfrastructureError,
                    "MISSING_BATTLE_REWARD",
                    "经营挑战结束后未生成奖励");
                return;
            }

            if (!ClaimOffer(offer, Stage))
            {
                return;
            }

            _run.ClearPendingRewardOffer();
            if (_run.HasPendingGenericRewards)
            {
                _run.PendingGenericRewardContinuation = PendingGenericRewardContinuationKind.Battle;
                if (!DrainGenericRewards())
                {
                    return;
                }
            }

            _run.ClearPendingRewardBattleView();
            Enqueue(_loop.OnRewardConfirmed);
        }

        public void ShowTimelineNodeCard(cfg.TimelineNode node, int? interestMaxGain, Action onPick)
        {
            Stage.Actions.Add("节点:" + (node?.Id ?? string.Empty));
            Enqueue(onPick);
        }

        public void DismissTimelineNodeCard(Action onDone) => Enqueue(onDone);

        public void ShowTimelineNodeSkipped(
            cfg.TimelineNode node,
            TimelineMutationResult result,
            Action onDone) => Enqueue(onDone);

        public void PlayTimelineAdvance(
            float fromDay,
            float toDay,
            string arrivingNodeId,
            Action onDone) => Enqueue(onDone);

        public void BeginTimelineAdvanceSequence(float fromDay) { }

        public void EndTimelineAdvanceSequence() { }

        public void PlayTimelineNodeCue(
            string nodeId,
            TimelinePresentationCueKind kind,
            Action onDone) => Enqueue(onDone);

        public void StartBattle(
            int requiredScore,
            string modifier,
            string key,
            string bossDebuffId,
            ActionExecutionContext actionContext)
        {
            AutoRunStageTrace stage = Stage;
            int placementDecisionStart = stage.PlacementDecisions.Count;
            bool isBoss = actionContext != null
                && FoodService.IsBossAction(_run.Tables, actionContext.Action);
            BattleSession session = BattleSessionFactory.Build(
                _run,
                requiredScore,
                modifier,
                key,
                bossDebuffId);
            UseBattleActiveItems(
                session,
                stage,
                beforePlacement: true,
                requiredScore,
                isBoss);
            AutoPlacementResult placement = AutoPlacementSolver.Solve(
                session,
                _request.PlayerLevel,
                _policy,
                _placementRandom);
            UseBattleActiveItems(
                session,
                stage,
                beforePlacement: false,
                requiredScore,
                isBoss);
            ScoreResult settled = session.Settle();
            BattleRunSettlement applied = BattleSettlementApplier.ApplyAll(_run, session);
            LastBattleTotal = settled.Total;

            stage.Score = BigDouble.Max(stage.Score, settled.Total);
            stage.RequiredScore = Math.Max(stage.RequiredScore, requiredScore);
            stage.SolverTruncated |= placement.Truncated;
            stage.NoLegalPlacement |= !placement.HasLegalSolution;
            stage.SolverNodes += placement.SearchNodes;
            stage.PlacementDecisions.AddRange(placement.Decisions);
            if (applied.Applied)
            {
                stage.GoldEarned += Math.Max(0, applied.GoldDelta);
            }

            foreach (AutoPlacementWarning warning in placement.Warnings)
            {
                AddPlacementWarning(stage, warning);
            }

            bool passed = placement.HasLegalSolution
                && placement.Termination != AutoPlacementTerminationKind.NodeBudgetExhausted
                && settled.Total >= requiredScore;
            PendingActionExecutionSaveData pending = _run.GetPendingActionExecution();
            string bossId = isBoss ? pending?.BossId ?? string.Empty : string.Empty;
            if (isBoss && string.IsNullOrEmpty(bossId))
            {
                bossId = FoodService.ResolveBoss(_run, actionContext?.Action)?.Id ?? string.Empty;
            }

            var battleTrace = new AutoRunBattleTrace
            {
                BattleKey = key ?? string.Empty,
                EncounterKey = BuildEncounterKey(actionContext),
                Week = stage.Week,
                Day = actionContext?.TargetScoreDayOverride ?? _run.CurrentDay,
                SourceKey = actionContext?.SourceKey ?? string.Empty,
                ActionId = actionContext?.Action?.Id ?? string.Empty,
                FoodId = actionContext?.Action?.FoodId ?? string.Empty,
                IsBoss = isBoss,
                BossId = bossId,
                BossDebuffId = bossDebuffId ?? string.Empty,
                RequiredScore = requiredScore,
                Score = settled.Total,
                TargetHit = passed,
                HeartsBefore = _run.HeartsRemaining,
                HeartsAfter = _run.HeartsRemaining,
                Survived = _run.HeartsRemaining > 0,
                PlacementDecisionStart = placementDecisionStart,
                PlacementDecisionCount = placement.Decisions.Count,
                SolverNodes = placement.SearchNodes,
                SolverTruncated = placement.Truncated,
                NoLegalPlacement = !placement.HasLegalSolution,
            };
            stage.Battles.Add(battleTrace);
            if (isBoss)
            {
                stage.BossReached = true;
                stage.BossScore = settled.Total;
                stage.BossRequiredScore = requiredScore;
                stage.BossPassed = passed;
                stage.Passed = passed;
            }
            else
            {
                stage.MealBattles++;
                if (passed)
                {
                    stage.MealPasses++;
                }
            }

            if (!placement.HasLegalSolution)
            {
                StopInvalid(
                    AutoRunTerminationKind.NoLegalPlacement,
                    "NO_LEGAL_PLACEMENT",
                    $"第 {stage.Week} 周没有合法摆盘");
                return;
            }

            if (placement.Termination == AutoPlacementTerminationKind.NodeBudgetExhausted)
            {
                StopInvalid(
                    AutoRunTerminationKind.PlacementBudgetExhausted,
                    "PLACEMENT_BUDGET",
                    $"第 {stage.Week} 周摆盘搜索预算耗尽");
                return;
            }

            Enqueue(() => SettleBattleAndFinalizeTrace(
                battleTrace,
                settled,
                passed,
                session.HappyCakeLayers));
        }

        private void SettleBattleAndFinalizeTrace(
            AutoRunBattleTrace battleTrace,
            ScoreResult settled,
            bool passed,
            int happyCakeLayers)
        {
            int heartsBefore = _run.HeartsRemaining;
            bool hadUndying = new ItemRuntime(_run).HasUndying();
            int expectedHeartLoss = WeekLoopController.HeartLossPerFailedBattle;
            battleTrace.HeartsBefore = heartsBefore;

            // OnBattleSettled 是正式扣心唯一入口；必须等它同步返回后再读取 heartsAfter。
            _loop.OnBattleSettled(settled, passed, happyCakeLayers);

            int heartsAfter = _run.HeartsRemaining;
            battleTrace.HeartsAfter = heartsAfter;
            battleTrace.HeartsLost = Math.Max(0, heartsBefore - heartsAfter);
            battleTrace.Survived = heartsAfter > 0;
            battleTrace.TerminalDeath = !passed && heartsAfter <= 0;
            battleTrace.UndyingPrevented = !passed
                && hadUndying
                && heartsBefore <= expectedHeartLoss
                && heartsAfter >= Math.Max(1, heartsBefore);
        }

        private string BuildEncounterKey(ActionExecutionContext actionContext)
        {
            string sourceKey = actionContext?.SourceKey ?? string.Empty;
            if (!string.IsNullOrEmpty(sourceKey))
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "w{0}:node:{1}:repeat{2}",
                    Math.Max(1, _run.WeekIndex),
                    sourceKey,
                    Math.Max(1, actionContext.NodeRepeatIndex));
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "w{0}:action:{1}:step{2}",
                Math.Max(1, _run.WeekIndex),
                actionContext?.Action?.Id ?? string.Empty,
                Math.Max(0, actionContext?.RunStepIndex ?? _run.RunActionStepIndex));
        }

        public void ShowNotice(string title, string message, Action onContinue) => Enqueue(onContinue);

        public void ShowHeartBreak(HeartBreakFormOpenArgs args, Action onComplete) => Enqueue(onComplete);

        public void OpenEventRecipeDishDelete(
            GameRun run,
            string title,
            Action onCancel,
            Action<ActiveTarget> onTargetConfirmed,
            Action onChanged)
        {
            if (run?.RecipeEntries == null || run.RecipeEntries.Count == 0)
            {
                Enqueue(onCancel);
                return;
            }

            int index = PickRecipeRemovalIndex(run);
            Enqueue(() => onTargetConfirmed?.Invoke(
                new ActiveTarget(
                    run.RecipeEntries[index].DishId,
                    0,
                    index,
                    cfg.ItemTargetKind.RecipeDish)));
        }

        public void ShowEventPage(
            string title,
            string desc,
            string resultButtonText,
            string bgSprite,
            IReadOnlyList<string> options,
            IReadOnlyList<string> optionRequirements,
            IReadOnlyList<bool> optionEnabled,
            Action<int> onPick,
            Action onEnd)
        {
            if (onPick != null && options != null && options.Count > 0)
            {
                HeadlessEventOptionDecision decision = HeadlessEventOptionPolicy.Pick(
                    _run,
                    title,
                    options,
                    optionEnabled,
                    _request.PlayerLevel,
                    _decisionRandom);
                if (decision.Index >= 0)
                {
                    Stage.Actions.Add($"事件选择:{decision.OptionId}|{decision.Reason}");
                    Enqueue(() => onPick(decision.Index));
                    return;
                }
            }

            Enqueue(onEnd);
        }

        public void ShowEventEffectFeedback(
            string title,
            string message,
            string bgSprite,
            Action onComplete) => Enqueue(onComplete);

        public void ExitEventPage(Action onExited) => Enqueue(onExited);

        public void ShowEventRecipeMutation(RecipeMutationResult result, Action onComplete) => Enqueue(onComplete);

        public void ShowDirectPassiveItemAcquire(
            ItemDefinition item,
            ItemAcquireResult acquireResult,
            Action onComplete) => Enqueue(onComplete);

        public void ShowRunResult(bool win, BigDouble total)
        {
            LastBattleTotal = total;
            foreach (AutoRunStageTrace stage in Trace.Stages)
            {
                stage.GoldBalance = _run.Gold;
            }

            Trace.Completed = win;
            Trace.Termination = win
                ? AutoRunTerminationKind.Completed
                : (_run.HeartsRemaining <= 0
                    ? AutoRunTerminationKind.HeartsDepleted
                    : AutoRunTerminationKind.GameOver);
            Trace.FailureReason = win
                ? string.Empty
                : (Trace.Termination == AutoRunTerminationKind.HeartsDepleted
                    ? "红心耗尽"
                    : "事件导致本局结束");
            _stopped = true;
            _callbacks.Clear();
        }

        private bool DrainGenericRewards()
        {
            int guard = 0;
            while (_run.TryPeekPendingGenericReward(out string key, out _, out RewardOffer offer))
            {
                if (++guard > 64)
                {
                    StopInvalid(
                        AutoRunTerminationKind.InfrastructureError,
                        "REWARD_GUARD",
                        "通用奖励队列超过安全上限");
                    return false;
                }

                if (!ClaimOffer(offer, Stage))
                {
                    return false;
                }

                _run.ClearPendingGenericRewardOffer(key);
            }

            return true;
        }

        private bool ClaimOffer(RewardOffer offer, AutoRunStageTrace stage)
        {
            int beforeGold = _run.Gold;
            MetaRoute route = RefreshMetaRoute(stage);
            ArchetypeClassification archetype = BuildArchetypeClassifier.Classify(
                _run,
                _policy.DualArchetypeThreshold);
            RewardClaimResult claimed = RewardClaimService.ClaimOffer(
                _run,
                offer,
                (group, remaining) => PickReward(group, remaining, archetype, route),
                _ => ResolvePendingFragment(),
                allowAbandon: true);
            if (!claimed.Success)
            {
                StopInvalid(
                    AutoRunTerminationKind.InfrastructureError,
                    "REWARD_CLAIM",
                    claimed.Error);
                return false;
            }

            ResolvePendingFragment();
            stage.Rewards.AddRange(claimed.ClaimedIds);
            stage.GoldEarned += Math.Max(0, _run.Gold - beforeGold);
            return ValidateOwnedActiveItems();
        }

        internal int PickReward(
            RewardChoiceGroup group,
            IReadOnlyList<int> candidates,
            ArchetypeClassification archetype,
            MetaRoute route)
        {
            var weights = new List<float>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                RewardChoice choice = group.Choices[candidates[i]];
                weights.Add(Math.Max(0.01f, RewardValue(choice, archetype, route)));
            }

            int local = _request.PlayerLevel == AutoPlayerLevel.Expert
                ? IndexOfMax(weights)
                : _decisionRandom.WeightedPickIndex(weights);
            return candidates[local];
        }

        internal float RewardValue(
            RewardChoice choice,
            ArchetypeClassification archetype,
            MetaRoute route)
        {
            if (choice == null)
            {
                return 0f;
            }

            if (choice.Kind == cfg.RewardKind.Gold || choice.IsFallbackGold)
            {
                return Math.Max(0.05f, choice.GoldAmount / 20f);
            }

            if (choice.Kind == cfg.RewardKind.DishChoice)
            {
                DishDef dish = _database.GetDish(choice.Id);
                return 1f + (dish != null
                    ? BuildArchetypeClassifier.DishAffinity(dish, _database, archetype?.Active)
                    : 0f);
            }

            ItemDefinition item = ItemDefinition.Get(_run.Tables, choice.Id);
            if (item != null)
            {
                return ItemStrategicValue(item, route);
            }

            return choice.Kind == cfg.RewardKind.FragmentChoice ? 2f : 0.75f;
        }

        /// <summary>
        /// 自动玩家的行动选择只读取正式候选快照和当前 Run 状态。这里禁止预执行行动或访问
        /// <see cref="GameRun.Random"/>；Normal 的随机性仅来自独立的 policy stream。
        /// </summary>
        internal ActionChoice PickAction(
            IReadOnlyList<ActionChoice> choices,
            MetaRoute route,
            AutoRunStageTrace stage,
            out string decisionTrace,
            AutoRunActionDecisionTrace structuredTrace = null)
        {
            route = RefreshMetaRoute(stage);
            if (choices == null || choices.Count == 0)
            {
                decisionTrace = "无可用候选";
                if (structuredTrace != null)
                {
                    structuredTrace.SelectionReason = decisionTrace;
                }
                return null;
            }

            List<ActionEvaluation> evaluations = choices
                .Where(choice => choice?.Action != null)
                .Select(choice => EvaluateAction(choice, route, stage))
                .ToList();
            if (evaluations.Count == 0)
            {
                decisionTrace = "候选均无效";
                if (structuredTrace != null)
                {
                    structuredTrace.SelectionReason = decisionTrace;
                }
                return null;
            }

            cfg.RewardKind preferredReward = PreferredRewardKind(_policy.ActionRewardPriority);
            List<ActionEvaluation> selectionPool = preferredReward == cfg.RewardKind.None
                ? evaluations
                : evaluations.Where(value => value.RewardKind == preferredReward).ToList();
            bool preferredAvailable = preferredReward != cfg.RewardKind.None && selectionPool.Count > 0;
            if (!preferredAvailable)
            {
                selectionPool = evaluations;
            }

            float temperature = Math.Max(0.35f, _policy.SoftmaxTemperature);
            float maximum = selectionPool.Max(value => value.Utility);
            var selectionWeights = selectionPool.Select(value =>
            {
                double exponent = Math.Max(-50d, Math.Min(0d, (value.Utility - maximum) / temperature));
                return Math.Max(0.000001f, (float)Math.Exp(exponent));
            }).ToList();
            float weightTotal = selectionWeights.Sum();
            if (weightTotal > 0f)
            {
                for (int i = 0; i < selectionWeights.Count; i++)
                {
                    selectionWeights[i] /= weightTotal;
                }
            }

            var weights = evaluations.Select(value =>
            {
                int poolIndex = selectionPool.IndexOf(value);
                return poolIndex >= 0 ? selectionWeights[poolIndex] : 0f;
            }).ToList();

            int index;
            string selectionMode;
            if (_request.PlayerLevel == AutoPlayerLevel.Expert)
            {
                index = IndexOfMax(selectionPool.Select(value => value.Utility).ToList());
                selectionMode = "最高风险调整效用";
            }
            else
            {
                index = _decisionRandom.WeightedPickIndex(selectionWeights);
                selectionMode = $"policy-softmax(T={temperature.ToString("0.00", CultureInfo.InvariantCulture)})";
            }

            ActionEvaluation selected = selectionPool[index];
            int selectedIndex = evaluations.IndexOf(selected);
            if (preferredAvailable)
            {
                selectionMode = $"{ActionPriorityName(_policy.ActionRewardPriority)}优先/{selectionMode}";
            }
            decisionTrace = DescribeActionDecision(selected, evaluations, selectionMode, stage);
            if (structuredTrace != null)
            {
                structuredTrace.RunStepIndex = selected.Choice.RunStepIndex;
                structuredTrace.CandidateActionIds.AddRange(
                    evaluations.Select(value => value.Choice.Action.Id ?? string.Empty));
                structuredTrace.CandidateRewardKinds.AddRange(
                    evaluations.Select(value => value.RewardKind));
                structuredTrace.CandidateCosts.AddRange(
                    evaluations.Select(value => value.Choice.CostDays));
                structuredTrace.CandidatePolicyWeights.AddRange(weights);
                structuredTrace.SelectedActionId = selected.Choice.Action.Id ?? string.Empty;
                structuredTrace.SelectedRewardKind = selected.RewardKind;
                structuredTrace.SelectedIndex = selectedIndex;
                structuredTrace.SelectionReason = decisionTrace;
            }
            return selected.Choice;
        }

        private ActionEvaluation EvaluateAction(
            ActionChoice choice,
            MetaRoute route,
            AutoRunStageTrace stage)
        {
            cfg.GameAction action = choice.Action;
            cfg.Food food = FoodService.Resolve(_run.Tables, action);
            cfg.RewardKind rewardKind = food?.RewardKind ?? cfg.RewardKind.None;
            bool isMeal = food != null
                && (food.ActionKind == cfg.FoodActionKind.Normal
                    || food.ActionKind == cfg.FoodActionKind.Super);
            bool isHardMeal = isMeal && food.ActionKind == cfg.FoodActionKind.Super;
            int estimatedRequiredScore = 0;
            float buildRatio = 0f;
            float safety;
            string actionKind;

            if (isMeal)
            {
                estimatedRequiredScore = _run.ScoreToOneRemaining > 0
                    ? 1
                    : HiddenScoreService.TargetScore(_run, choice.ToExecutionContext());
                estimatedRequiredScore = new ItemRuntime(_run).ModifyRequiredScore(
                    Math.Max(1, estimatedRequiredScore),
                    food.ActionKind);
                double buildPower = EstimateCurrentBuildPower();
                buildRatio = Clamp((float)(buildPower / Math.Max(1, estimatedRequiredScore)), 0f, 2f);

                // 当前构筑给出先验；本周营业越多，近期通过率的权重越高。
                // 旧映射把 0.91 倍构筑强度当成 87% 安全，实测却经常刚好失败。
                // 低于要求线时应快速降置信度；只有明显越过要求线才接近“稳过”。
                float buildConfidence = Clamp01((buildRatio - 0.72f) / 0.48f);
                int recentBattles = Math.Max(0, stage?.MealBattles ?? 0);
                int recentPasses = Math.Max(0, Math.Min(recentBattles, stage?.MealPasses ?? 0));
                float recentPassRate = (recentPasses + 1f) / (recentBattles + 2f);
                float evidenceWeight = Math.Min(0.65f, recentBattles * 0.18f);
                safety = buildConfidence * (1f - evidenceWeight) + recentPassRate * evidenceWeight;

                // 火热营业的隐藏要求分已经进入 buildRatio；额外折损表示高难行动的波动风险。
                if (isHardMeal)
                {
                    safety *= 0.78f;
                }

                actionKind = isHardMeal ? "火热营业" : "日常营业";
            }
            else
            {
                safety = NonBattleSafety(action?.Behavior ?? cfg.ActionBehavior.Negative);
                actionKind = action?.Behavior.ToString() ?? "未知";
            }

            float risk = Clamp01(1f - safety + (isHardMeal ? 0.12f : 0f));
            int hearts = Math.Max(0, _run.HeartsRemaining);
            float riskWeight = hearts <= 1 ? 16f : hearts == 2 ? 10f : 4.5f;
            bool hasUndying = new ItemRuntime(_run).HasUndying();
            if (hasUndying)
            {
                riskWeight *= 0.7f;
            }

            // 营业奖励只有过关才拿得到，必须按当前通过置信度折算期望收益；旧逻辑把
            // “高价值但大概率失败”的奖励按满额计价，高手会连续赌到红心耗尽。
            float rewardUtility = Math.Max(0.01f, ActionWeight(action, route)) * 0.55f
                * (isMeal ? safety : 1f);
            float effectiveProgress = Math.Max(0f, choice.CostDays)
                * (1f - Math.Max(0f, Math.Min(1f, choice.TimelineStopChance)) * 0.5f);
            float progressUtility = effectiveProgress * (hearts <= 2 ? 0.75f : 0.35f);
            float hardPenalty = isHardMeal ? (hearts <= 2 ? 1.25f : 0.65f) : 0f;
            float utility = rewardUtility + progressUtility - risk * riskWeight - hardPenalty;
            return new ActionEvaluation(
                choice,
                actionKind,
                isMeal,
                isHardMeal,
                estimatedRequiredScore,
                buildRatio,
                safety,
                risk,
                rewardUtility,
                utility,
                hasUndying,
                rewardKind);
        }

        private string DescribeActionDecision(
            ActionEvaluation selected,
            IReadOnlyList<ActionEvaluation> evaluations,
            string selectionMode,
            AutoRunStageTrace stage)
        {
            int hearts = Math.Max(0, _run.HeartsRemaining);
            float minimumRisk = evaluations.Min(value => value.Risk);
            string focus = hearts <= 2 && selected.Risk <= minimumRisk + 0.1f
                ? "生存优先"
                : selected.RewardUtility >= evaluations.Max(value => value.RewardUtility) - 0.01f
                    ? "收益优先"
                    : "风险收益平衡";
            int recentBattles = Math.Max(0, stage?.MealBattles ?? 0);
            int recentPasses = Math.Max(0, Math.Min(recentBattles, stage?.MealPasses ?? 0));
            string candidates = string.Join(",", evaluations.Select(value => string.Format(
                CultureInfo.InvariantCulture,
                "{0}={1:0.00}/风险{2:0.00}",
                value.Choice.Action.Id,
                value.Utility,
                value.Risk)));
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1}|选择={2}|心={3}/{4}|类型={5}|安全={6:0.00}|风险={7:0.00}|构筑比={8:0.00}|本周通过={9}/{10}|估算要求={11}|耗时={12:0.0}|不死={13}|效用={14:0.00}|候选=[{15}]",
                _request.PlayerLevel,
                focus + "/" + selectionMode,
                selected.Choice.Action.Id,
                hearts,
                _run.HeartCapacity,
                selected.ActionKind,
                selected.Safety,
                selected.Risk,
                selected.BuildRatio,
                recentPasses,
                recentBattles,
                selected.EstimatedRequiredScore,
                selected.Choice.CostDays,
                selected.HasUndying ? "是" : "否",
                selected.Utility,
                candidates);
        }

        private double EstimateCurrentBuildPower()
        {
            double total = 0d;
            int synergySources = 0;
            foreach (RecipeBookSlot slot in _run.RecipeEntries ?? Array.Empty<RecipeBookSlot>())
            {
                DishDef dish = _database.GetDish(slot.DishId);
                if (dish == null)
                {
                    continue;
                }

                double flat = FiniteOr(slot.ScoreFlatBonus.ToDouble(), 0d);
                double multiplier = Math.Max(0.01d, FiniteOr(slot.ScoreMultiplier.ToDouble(), 1d));
                total += Math.Max(0d, dish.Deliciousness + flat) * multiplier;
                synergySources += (dish.SkillIds?.Count ?? 0)
                    + (slot.ExtraSkillIds?.Count ?? 0)
                    + (slot.ExtraFlavorIds?.Count ?? 0);
            }

            return total * (1d + Math.Min(0.6d, synergySources * 0.015d));
        }

        private static double FiniteOr(double value, double fallback)
            => double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;

        private static float NonBattleSafety(cfg.ActionBehavior behavior)
        {
            switch (behavior)
            {
                case cfg.ActionBehavior.Shop:
                case cfg.ActionBehavior.Interest:
                case cfg.ActionBehavior.Reward:
                    return 0.97f;
                case cfg.ActionBehavior.Event:
                    return 0.97f;
                case cfg.ActionBehavior.Slot:
                    return 0.80f;
                case cfg.ActionBehavior.Effect:
                    return 0.75f;
                case cfg.ActionBehavior.Negative:
                    return 0.25f;
                default:
                    return 0.85f;
            }
        }

        private static float Clamp01(float value) => Clamp(value, 0f, 1f);

        private static float Clamp(float value, float minimum, float maximum)
            => Math.Max(minimum, Math.Min(maximum, value));

        private sealed class ActionEvaluation
        {
            public ActionEvaluation(
                ActionChoice choice,
                string actionKind,
                bool isMeal,
                bool isHardMeal,
                int estimatedRequiredScore,
                float buildRatio,
                float safety,
                float risk,
                float rewardUtility,
                float utility,
                bool hasUndying,
                cfg.RewardKind rewardKind)
            {
                Choice = choice;
                ActionKind = actionKind;
                IsMeal = isMeal;
                IsHardMeal = isHardMeal;
                EstimatedRequiredScore = estimatedRequiredScore;
                BuildRatio = buildRatio;
                Safety = safety;
                Risk = risk;
                RewardUtility = rewardUtility;
                Utility = utility;
                HasUndying = hasUndying;
                RewardKind = rewardKind;
            }

            public ActionChoice Choice { get; }
            public string ActionKind { get; }
            public bool IsMeal { get; }
            public bool IsHardMeal { get; }
            public int EstimatedRequiredScore { get; }
            public float BuildRatio { get; }
            public float Safety { get; }
            public float Risk { get; }
            public float RewardUtility { get; }
            public float Utility { get; }
            public bool HasUndying { get; }
            public cfg.RewardKind RewardKind { get; }
        }

        private static float ActionWeight(cfg.GameAction action, MetaRoute route)
        {
            if (action == null)
            {
                return 0f;
            }

            string behavior = action.Behavior.ToString();
            if (route == MetaRoute.Event
                && behavior.IndexOf("Event", StringComparison.OrdinalIgnoreCase) >= 0) return 4f;
            if (route == MetaRoute.Shop
                && behavior.IndexOf("Shop", StringComparison.OrdinalIgnoreCase) >= 0) return 4f;
            if (route == MetaRoute.Interest
                && behavior.IndexOf("Interest", StringComparison.OrdinalIgnoreCase) >= 0) return 4f;
            string id = action.Id ?? string.Empty;
            if (id.IndexOf("active_strengthen", StringComparison.OrdinalIgnoreCase) >= 0) return 3.5f;
            if (id.IndexOf("passive", StringComparison.OrdinalIgnoreCase) >= 0) return 4f;
            if (id.IndexOf("fragment", StringComparison.OrdinalIgnoreCase) >= 0) return 3f;
            if (id.IndexOf("active_adjust", StringComparison.OrdinalIgnoreCase) >= 0) return 2f;
            return 1f;
        }

        private static cfg.RewardKind PreferredRewardKind(AutoActionRewardPriority priority)
        {
            switch (priority)
            {
                case AutoActionRewardPriority.FragmentChoice: return cfg.RewardKind.FragmentChoice;
                case AutoActionRewardPriority.PassiveItemChoice: return cfg.RewardKind.PassiveItemChoice;
                case AutoActionRewardPriority.ActiveItemStrengthen: return cfg.RewardKind.ActiveItemStrengthen;
                case AutoActionRewardPriority.ActiveItemAdjust: return cfg.RewardKind.ActiveItemAdjust;
                case AutoActionRewardPriority.Gold: return cfg.RewardKind.Gold;
                default: return cfg.RewardKind.None;
            }
        }

        private static string ActionPriorityName(AutoActionRewardPriority priority)
        {
            switch (priority)
            {
                case AutoActionRewardPriority.FragmentChoice: return "格子";
                case AutoActionRewardPriority.PassiveItemChoice: return "装饰品";
                case AutoActionRewardPriority.ActiveItemStrengthen: return "强化消耗品";
                case AutoActionRewardPriority.ActiveItemAdjust: return "调整消耗品";
                case AutoActionRewardPriority.Gold: return "金币";
                default: return "综合";
            }
        }

        internal float ShopValue(ShopEntry entry, MetaRoute route)
        {
            if (entry == null)
            {
                return 0f;
            }

            if (entry.Kind == ShopEntryKind.Dish)
            {
                ArchetypeClassification archetype = BuildArchetypeClassifier.Classify(
                    _run,
                    _policy.DualArchetypeThreshold);
                return 1f + BuildArchetypeClassifier.DishAffinity(
                    _database.GetDish(entry.Id),
                    _database,
                    archetype.Active);
            }

            if (entry.Kind == ShopEntryKind.PassiveItem || entry.Kind == ShopEntryKind.ActiveItem)
            {
                ItemDefinition item = ItemDefinition.Get(_run.Tables, entry.Id);
                return ItemStrategicValue(item, route);
            }

            return entry.Kind == ShopEntryKind.Fragment ? 2f : 0.5f;
        }

        internal float ShopPurchasePriority(
            ShopEntry entry,
            MetaRoute route,
            out bool shouldBuy)
        {
            int price = Math.Max(1, ShopService.CurrentPrice(_run, entry));
            float priority = ShopValue(entry, route) * 40f / price;
            ItemDefinition item = entry == null
                ? null
                : ItemDefinition.Get(_run.Tables, entry.Id);
            bool lifeSaving = IsLifeSavingItem(item) && _run.HeartsRemaining < _run.HeartCapacity;
            float threshold;
            switch (entry?.Kind)
            {
                case ShopEntryKind.ActiveItem:
                    threshold = 1.35f;
                    break;
                case ShopEntryKind.Dish:
                    threshold = 0.9f;
                    break;
                case ShopEntryKind.Fragment:
                    threshold = 0.8f;
                    break;
                default:
                    threshold = 1f;
                    break;
            }

            shouldBuy = lifeSaving || priority >= threshold;
            return priority;
        }

        /// <summary>
        /// Expert 在每次商店流程开始时最多使用一次正式删菜服务。只考虑本局开始后追加、
        /// 尚未获得风味/技能/分数强化且不契合当前构筑的食物，避免把初始食谱或已投资食物
        /// 当作填充牌误删。价格、每店次数和禁删效果全部由 <see cref="ShopService"/> 判定。
        /// </summary>
        internal bool TryDeleteObviousAcquiredFiller(AutoRunStageTrace stage)
        {
            if (_request.PlayerLevel != AutoPlayerLevel.Expert
                || stage == null
                || !ShopService.CanDeleteDish(_run))
            {
                return false;
            }

            ArchetypeClassification archetype = BuildArchetypeClassifier.Classify(
                _run,
                _policy.DualArchetypeThreshold);
            int weakestIndex = -1;
            float weakestValue = float.MaxValue;
            for (int index = Math.Min(_initialRecipeCount, _run.RecipeEntries.Count);
                 index < _run.RecipeEntries.Count;
                 index++)
            {
                RecipeBookSlot slot = _run.RecipeEntries[index];
                if (slot == null
                    || _initialRecipeEntries.Contains(slot)
                    || slot.ExtraFlavorIds.Count > 0
                    || slot.ExtraSkillIds.Count > 0
                    || slot.ScoreFlatBonus != BigDouble.Zero
                    || slot.ScoreMultiplier != BigDouble.One)
                {
                    continue;
                }

                DishDef dish = _database.GetDish(slot.DishId);
                float value = 1f + BuildArchetypeClassifier.DishAffinity(
                    dish,
                    _database,
                    archetype.Active);
                if (value < weakestValue)
                {
                    weakestValue = value;
                    weakestIndex = index;
                }
            }

            if (weakestIndex < 0 || weakestValue >= 1f)
            {
                return false;
            }

            int cost = ShopService.DeleteCost(_run);
            string dishId = _run.RecipeEntries[weakestIndex].DishId;
            if (!ShopService.DeleteDishAt(_run, weakestIndex))
            {
                return false;
            }

            stage.GoldSpent += cost;
            stage.Purchases.Add("delete:" + dishId);
            return true;
        }

        private int ShopReserve(ShopEntry entry, int routeReserve)
        {
            if (_request.PlayerLevel != AutoPlayerLevel.Expert)
            {
                return routeReserve;
            }

            ItemDefinition item = entry == null
                ? null
                : ItemDefinition.Get(_run.Tables, entry.Id);
            if (IsLifeSavingItem(item) && _run.HeartsRemaining < _run.HeartCapacity)
            {
                return 0;
            }

            int survivalReserve = _run.HeartsRemaining <= 1 ? 20 : _run.HeartsRemaining == 2 ? 10 : 0;
            return Math.Max(routeReserve, survivalReserve);
        }

        private float ItemStrategicValue(ItemDefinition item, MetaRoute route)
        {
            if (item == null || item.IsNegative)
            {
                return 0.01f;
            }

            float value = 0.5f + Math.Max(0, item.Price) / 50f;
            value += Math.Max(0, Math.Min(4, (int)item.Quality)) * 0.45f;
            if (_request.MetaAffinity != null)
            {
                float affinity = Math.Max(0f, _request.MetaAffinity.Get(item.Id, route));
                value += Math.Min(2f, affinity * 0.02f);
            }

            string id = item.Id ?? string.Empty;
            bool missingHeart = _run.HeartsRemaining < _run.HeartCapacity;
            if (id.IndexOf("restore_heart", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                value += missingHeart ? 8f : -0.5f;
            }
            else if (id.IndexOf("heart_capacity", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                value += _run.HeartsRemaining <= 2 ? 5f : 2f;
            }
            else if (id.IndexOf("famous_knife", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                value += _run.HeartsRemaining <= 2 ? 7f : 2f;
            }

            if (item.IsActive)
            {
                value *= 0.8f;
            }

            return Math.Max(0.01f, value);
        }

        private static bool IsLifeSavingItem(ItemDefinition item)
        {
            string id = item?.Id ?? string.Empty;
            return id.IndexOf("restore_heart", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("heart_capacity", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("famous_knife", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private int PickRecipeRemovalIndex(GameRun run)
        {
            if (_request.PlayerLevel == AutoPlayerLevel.Normal)
            {
                return _decisionRandom.Range(0, run.RecipeEntries.Count);
            }

            int best = 0;
            for (int i = 1; i < run.RecipeEntries.Count; i++)
            {
                if (run.RecipeEntries[i].ScoreFlatBonus < run.RecipeEntries[best].ScoreFlatBonus)
                {
                    best = i;
                }
            }

            return best;
        }

        private static int IndexOfMax(IReadOnlyList<float> values)
        {
            int best = 0;
            for (int i = 1; i < values.Count; i++)
            {
                if (values[i] > values[best])
                {
                    best = i;
                }
            }

            return best;
        }

        private static MetaRoute ResolveMetaRoute(GameRun run, MetaAffinityCatalog catalog)
        {
            if (catalog == null)
            {
                return MetaRoute.Normal;
            }

            MetaRoute best = MetaRoute.Normal;
            float bestValue = float.MinValue;
            foreach (MetaRoute route in Enum.GetValues(typeof(MetaRoute)))
            {
                float value = run.Items.Sum(item => catalog.Get(item.ItemId, route));
                if (value > bestValue)
                {
                    bestValue = value;
                    best = route;
                }
            }

            return best;
        }

        private MetaRoute RefreshMetaRoute(AutoRunStageTrace stage)
        {
            MetaRoute current = ResolveMetaRoute(_run, _request.MetaAffinity);
            if (stage == null || stage.MetaRoute == current)
            {
                return current;
            }

            MetaRoute previous = stage.MetaRoute;
            stage.MetaRoute = current;
            stage.Actions.Add($"路线:{previous}->{current}");
            return current;
        }

        private void AddPlacementWarning(AutoRunStageTrace stage, AutoPlacementWarning warning)
        {
            AutoRunWarningKind kind = warning.Kind == AutoPlacementWarningKind.NodeBudgetExhausted
                ? AutoRunWarningKind.PlacementBudgetExhausted
                : AutoRunWarningKind.PlacementCandidateLimitApplied;
            var runWarning = new AutoRunWarning
            {
                Kind = kind,
                Code = warning.Kind.ToString(),
                Message = warning.Message,
                Week = stage.Week,
            };
            stage.Warnings.Add(runWarning);
            Trace.Warnings.Add(runWarning);
        }

        private void UseActionSelectActiveItems(AutoRunStageTrace stage)
        {
            var context = new ActionSelectUseContext(_run, _loop);
            foreach (string id in _run.Items.Select(item => item.ItemId).ToList())
            {
                ItemDefinition item = ItemDefinition.Get(_run.Tables, id, cfg.ItemKind.Active);
                if (item == null
                    || !IsPositiveActionEffect(item.EffectType)
                    || !ShouldUseActionSelectActiveItem(item, stage))
                {
                    continue;
                }

                TryApplyAndConsumeActiveItem(context, item, stage);
                if (_stopped)
                {
                    return;
                }
            }
        }

        internal bool ShouldUseActionSelectActiveItem(
            ItemDefinition item,
            AutoRunStageTrace stage)
        {
            if (item == null)
            {
                return false;
            }

            bool createsExtraExposure = item.EffectType == ItemEffectTypes.HalfNextActionCost
                || item.EffectType == ItemEffectTypes.TimelineExecuteFuture
                || item.EffectType == ItemEffectTypes.TimelineExecutePast;
            if (!createsExtraExposure)
            {
                return true;
            }

            int battles = Math.Max(0, stage?.MealBattles ?? 0);
            int passes = Math.Max(0, Math.Min(battles, stage?.MealPasses ?? 0));
            bool lowRecentPassRate = battles >= 2 && passes / (float)battles < 0.5f;
            return _run.HeartsRemaining > 2 && !lowRecentPassRate;
        }

        internal void UseBattleActiveItems(
            BattleSession session,
            AutoRunStageTrace stage,
            bool beforePlacement,
            int requiredScore,
            bool isBoss)
        {
            if (session == null
                || (!isBoss && _run.HeartsRemaining > 2)
                || session.PreviewScoreForSolver() >= requiredScore)
            {
                return;
            }

            List<ItemDefinition> candidates = _run.Items
                .Select(state => ItemDefinition.Get(_run.Tables, state.ItemId, cfg.ItemKind.Active))
                .Where(item => item != null
                    && IsPositiveBattleEffect(item.EffectType)
                    && !beforePlacement
                    && BattleActiveScoreHelp(item) > 0d)
                .OrderByDescending(BattleActiveScoreHelp)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToList();
            bool failureWouldEndRun = !isBoss
                && _run.HeartsRemaining <= 1
                && !new ItemRuntime(_run).HasUndying();
            foreach (ItemDefinition item in candidates)
            {
                // 每次正式使用后重新走无副作用预览；已经够分就立即止损。
                if (session.PreviewScoreForSolver() >= requiredScore)
                {
                    break;
                }

                // 普通营业若尚非终局，尽量为 Boss 保留至少一件真正能帮助得分的战斗道具。
                if (!isBoss
                    && !failureWouldEndRun
                    && CountBossScoreHelpingBattleItems() <= 1)
                {
                    break;
                }

                var context = new BattleUseContext(session, _run);
                bool used = TryApplyAndConsumeActiveItem(context, item, stage, session);
                if (_stopped)
                {
                    return;
                }

                // 无目标、被 Boss 禁用或 Apply 失败都不算使用；继续尝试后续候选。
                if (!used)
                {
                    continue;
                }
            }
        }

        internal double BattleActiveScoreHelp(ItemDefinition item)
        {
            if (item == null || !IsPositiveBattleEffect(item.EffectType))
            {
                return 0d;
            }

            switch (item.EffectType)
            {
                case ItemEffectTypes.AddScore:
                    return 12000d + Math.Max(0f, item.EffectValue);
                case ItemEffectTypes.AddCountAs:
                    return 11000d + Math.Max(0f, item.EffectValue) * 100d;
                case ItemEffectTypes.DuplicateDish:
                    return 10500d;
                case ItemEffectTypes.GenerateDish:
                    return 10000d + Math.Max(
                        0,
                        _database.GetDish(item.EffectParam)?.Deliciousness ?? 0);
                case ItemEffectTypes.AddFlavor:
                case ItemEffectTypes.EnhanceFlavor:
                case ItemEffectTypes.ConvertFlavor:
                    return FlavorScoreHelp(item.EffectParam);
                default:
                    // GoldNow 等效果有长期价值，但不能把当前预览推过要求线。
                    return 0d;
            }
        }

        private double FlavorScoreHelp(string flavorId)
        {
            FlavorDef flavor = _database.GetFlavor(flavorId);
            if (flavor == null)
            {
                return 0d;
            }

            switch (flavor.EffectType)
            {
                case FlavorEffectType.ExtraSettlementChance:
                    return 6000d + Math.Max(0f, flavor.EffectValue) * 100d;
                case FlavorEffectType.AddMult:
                case FlavorEffectType.PerDishOnBoard:
                    return 5800d + Math.Max(0f, flavor.EffectValue - 1f) * 100d;
                case FlavorEffectType.AddFlat:
                case FlavorEffectType.PerAdjacentDish:
                case FlavorEffectType.PerEmptyCell:
                case FlavorEffectType.PerOccupiedCell:
                    return 5500d + Math.Max(0f, flavor.EffectValue);
                case FlavorEffectType.SettlementLayer:
                    // 顺序效果依赖当前构筑，放在确定增益之后；每次使用后的正式预览负责止损。
                    return 500d + Math.Abs(flavor.EffectValue);
                default:
                    // 鲜/麻/酸不会直接抬高当前已摆盘面的预览分，不作为保命补分道具。
                    return 0d;
            }
        }

        private int CountBossScoreHelpingBattleItems()
        {
            return _run.Items.Count(state =>
            {
                ItemDefinition item = ItemDefinition.Get(
                    _run.Tables,
                    state.ItemId,
                    cfg.ItemKind.Active);
                return item != null
                    && IsPositiveBattleEffect(item.EffectType)
                    && BattleActiveScoreHelp(item) > 0d;
            });
        }

        private bool TryApplyAndConsumeActiveItem(
            IActiveUseContext context,
            ItemDefinition item,
            AutoRunStageTrace stage,
            BattleSession battleSession = null)
        {
            if (context == null
                || item == null
                || !ItemActiveUsage.CanUse(_run, item, context.ContextKind, out _))
            {
                return false;
            }

            IReadOnlyList<ActiveTarget> sourceCandidates = context.EnumerateTargets(item)
                ?? Array.Empty<ActiveTarget>();
            List<ActiveTarget> candidates = sourceCandidates.ToList();
            if (battleSession != null && candidates.Count > 1)
            {
                candidates = OrderBattleTargetsForScore(
                    battleSession,
                    item,
                    candidates);
            }
            if (_request.PlayerLevel == AutoPlayerLevel.Normal && candidates.Count > 1)
            {
                int offset = _decisionRandom.Range(0, candidates.Count);
                candidates = candidates.Skip(offset).Concat(candidates.Take(offset)).ToList();
            }

            bool requiresTarget = ItemActiveUsage.RequiresTarget(item);
            if (requiresTarget && candidates.Count == 0)
            {
                // No currently legal target is an ordinary policy hold, not an unsupported rule.
                return false;
            }

            ActiveItemUseResult used = default;
            if (!requiresTarget)
            {
                used = ActiveItemEffectRegistry.Apply(context, item, Array.Empty<ActiveTarget>());
            }
            else
            {
                int count = Math.Max(1, item.TargetCount);
                for (int start = 0; start < candidates.Count && !used.Success; start++)
                {
                    List<ActiveTarget> targets = candidates.Skip(start).Take(count).ToList();
                    used = ActiveItemEffectRegistry.Apply(context, item, targets);
                }
            }

            if (!used.Success)
            {
                return false;
            }

            // Match the live ActiveItemUseCoordinator: apply the effect first, then
            // consume through GameRun so active-use passives and telemetry gating stay shared.
            if (!_run.UseActiveItem(item.Id, "balance_lab_" + context.ContextKind.ToString().ToLowerInvariant()))
            {
                if (!string.IsNullOrEmpty(used.CreatedTimelineNodeId))
                {
                    context.DeleteTimelineNode(used.CreatedTimelineNodeId);
                }

                StopInvalid(
                    AutoRunTerminationKind.UnsupportedMechanic,
                    "ACTIVE_ITEM_CONSUME_FAILED",
                    $"主动道具 {item.Id} 生效后无法按正式流程消耗。");
                return false;
            }

            stage.ActiveItemsUsed.Add(item.Id);
            return true;
        }

        private static List<ActiveTarget> OrderBattleTargetsForScore(
            BattleSession session,
            ItemDefinition item,
            List<ActiveTarget> candidates)
        {
            if (session == null || item == null || candidates == null)
            {
                return candidates ?? new List<ActiveTarget>();
            }

            if (item.TargetKind == cfg.ItemTargetKind.DiningTableDish)
            {
                return candidates
                    .OrderByDescending(target =>
                    {
                        if (!int.TryParse(target.Id, out int dishId)) return 0;
                        return session.FindDishById(dishId)?.Def?.Deliciousness ?? 0;
                    })
                    .ThenBy(target => target.Y)
                    .ThenBy(target => target.X)
                    .ToList();
            }

            return candidates;
        }

        private static bool IsPositiveBattleEffect(string effectType)
        {
            return effectType == ItemEffectTypes.GoldNow
                || effectType == ItemEffectTypes.AddScore
                || effectType == ItemEffectTypes.AddCountAs
                || effectType == ItemEffectTypes.DuplicateDish
                || effectType == ItemEffectTypes.AddFlavor
                || effectType == ItemEffectTypes.EnhanceFlavor
                || effectType == ItemEffectTypes.ConvertFlavor
                || effectType == ItemEffectTypes.GenerateDish;
        }

        private static bool IsPositiveActionEffect(string effectType)
        {
            return effectType == ItemEffectTypes.RerollAction
                || effectType == ItemEffectTypes.DoubleNextBusinessReward
                || effectType == ItemEffectTypes.HalfNextActionCost
                || effectType == ItemEffectTypes.ResetBossDebuff
                || effectType == ItemEffectTypes.TimelineExecuteFuture
                || effectType == ItemEffectTypes.TimelineExecutePast
                || effectType == ItemEffectTypes.TimelineAddRewardNode
                || effectType == ItemEffectTypes.TimelineAddInterestNode
                || effectType == ItemEffectTypes.TimelineAddShopNode
                || effectType == ItemEffectTypes.TimelineAddLotteryNode
                || effectType == ItemEffectTypes.TimelineAddRestoreHeartNode
                || effectType == ItemEffectTypes.TimelineDeleteNode;
        }

        private bool ValidateOwnedActiveItems()
        {
            foreach (RunItemState state in _run.Items)
            {
                ItemDefinition item = ItemDefinition.Get(
                    _run.Tables,
                    state.ItemId,
                    cfg.ItemKind.Active);
                if (item == null)
                {
                    continue;
                }

                if (IsPositiveBattleEffect(item.EffectType)
                    || IsPositiveActionEffect(item.EffectType))
                {
                    continue;
                }

                StopInvalid(
                    AutoRunTerminationKind.UnsupportedMechanic,
                    "UNSUPPORTED_ACTIVE_EFFECT",
                    $"自动玩家尚未支持主动道具 {item.Id} 的效果 {item.EffectType}。");
                return false;
            }

            return true;
        }

        internal bool ResolvePendingFragment()
        {
            if (_run.PendingFragmentPack == null || _run.PendingFragmentPack.Count == 0)
            {
                return false;
            }

            cfg.Character character = _run.Tables.TbCharacter.GetOrDefault(_run.CharacterId);
            if (character == null)
            {
                _run.ClearPendingFragmentPack();
                return false;
            }

            var board = _run.BuildTablePreviewFromFragments();
            var existing = GourmetProject.Gameplay.Board.TableFragmentBuilder.ToExistingSet(board);
            var legal = new List<FragmentCandidate>();
            for (int i = 0; i < _run.PendingFragmentPack.Count; i++)
            {
                string id = _run.PendingFragmentPack[i];
                int rotation = i < _run.PendingFragmentPackRotations.Count
                    ? _run.PendingFragmentPackRotations[i]
                    : 0;
                TableFragmentDef definition = _run.Database.GetFragment(id);
                if (definition == null)
                {
                    continue;
                }

                TableFragmentDef rotated = rotation == 0
                    ? definition
                    : definition.Rotated(rotation);
                GetBounds(existing, out int minX, out int minY, out int maxX, out int maxY);
                for (int y = minY - character.MaxDiningTableHeight;
                     y <= maxY + character.MaxDiningTableHeight;
                     y++)
                {
                    for (int x = minX - character.MaxDiningTableWidth;
                         x <= maxX + character.MaxDiningTableWidth;
                         x++)
                    {
                        var origin = new GridPos(x, y);
                        if (!FragmentFitsCanvas(rotated, origin, board.Width, board.Height))
                        {
                            continue;
                        }

                        if (GourmetProject.Gameplay.Board.TableFragmentBuilder
                            .GetFragmentPlacementStatusWithinMaxBounds(
                                existing,
                                rotated,
                                origin,
                                character.MaxDiningTableWidth,
                                character.MaxDiningTableHeight)
                            == GourmetProject.Gameplay.Board.TableFragmentBuilder.FragmentPlacementStatus.Valid)
                        {
                            legal.Add(BuildFragmentCandidate(
                                id,
                                rotation,
                                origin,
                                rotated,
                                existing,
                                minX,
                                minY,
                                maxX,
                                maxY));
                        }
                    }
                }
            }

            bool added = false;
            if (legal.Count > 0)
            {
                FragmentCandidate selected = _request.PlayerLevel == AutoPlayerLevel.Expert
                    ? legal
                        .OrderByDescending(candidate => candidate.SharedEdges)
                        .ThenBy(candidate => candidate.BoundsArea)
                        .ThenBy(candidate => candidate.BoundsPerimeter)
                        .ThenBy(candidate => candidate.CenterOffset)
                        .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
                        .ThenBy(candidate => candidate.Rotation)
                        .ThenBy(candidate => candidate.Origin.Y)
                        .ThenBy(candidate => candidate.Origin.X)
                        .First()
                    : legal[_decisionRandom.Range(0, legal.Count)];
                added = _run.AddFragmentPlacement(selected.Id, selected.Rotation, selected.Origin);
            }

            _run.ClearPendingFragmentPack();
            return added;
        }

        private static bool FragmentFitsCanvas(
            TableFragmentDef fragment,
            GridPos origin,
            int canvasWidth,
            int canvasHeight)
        {
            foreach (GridPos local in GourmetProject.Gameplay.Board.TableFragmentBuilder.FilledCells(fragment))
            {
                GridPos cell = local.Offset(origin.X, origin.Y);
                if (cell.X < 0 || cell.Y < 0 || cell.X >= canvasWidth || cell.Y >= canvasHeight)
                {
                    return false;
                }
            }

            return true;
        }

        private static FragmentCandidate BuildFragmentCandidate(
            string id,
            int rotation,
            GridPos origin,
            TableFragmentDef fragment,
            HashSet<GridPos> existing,
            int existingMinX,
            int existingMinY,
            int existingMaxX,
            int existingMaxY)
        {
            int sharedEdges = 0;
            int minX = existingMinX;
            int minY = existingMinY;
            int maxX = existingMaxX;
            int maxY = existingMaxY;
            foreach (GridPos local in GourmetProject.Gameplay.Board.TableFragmentBuilder.FilledCells(fragment))
            {
                GridPos cell = local.Offset(origin.X, origin.Y);
                minX = Math.Min(minX, cell.X);
                minY = Math.Min(minY, cell.Y);
                maxX = Math.Max(maxX, cell.X);
                maxY = Math.Max(maxY, cell.Y);
                if (existing.Contains(cell.Offset(-1, 0))) sharedEdges++;
                if (existing.Contains(cell.Offset(1, 0))) sharedEdges++;
                if (existing.Contains(cell.Offset(0, -1))) sharedEdges++;
                if (existing.Contains(cell.Offset(0, 1))) sharedEdges++;
            }

            int width = maxX - minX + 1;
            int height = maxY - minY + 1;
            int centerOffset = Math.Abs((minX + maxX) - (existingMinX + existingMaxX))
                + Math.Abs((minY + maxY) - (existingMinY + existingMaxY));
            return new FragmentCandidate(
                id,
                rotation,
                origin,
                sharedEdges,
                width * height,
                2 * (width + height),
                centerOffset);
        }

        private static void GetBounds(
            HashSet<GridPos> cells,
            out int minX,
            out int minY,
            out int maxX,
            out int maxY)
        {
            minX = minY = int.MaxValue;
            maxX = maxY = int.MinValue;
            foreach (GridPos cell in cells)
            {
                minX = Math.Min(minX, cell.X);
                minY = Math.Min(minY, cell.Y);
                maxX = Math.Max(maxX, cell.X);
                maxY = Math.Max(maxY, cell.Y);
            }

            if (cells.Count == 0)
            {
                minX = minY = maxX = maxY = 0;
            }
        }

        private void StopInvalid(AutoRunTerminationKind kind, string code, string message)
        {
            Trace.Completed = false;
            Trace.Termination = kind;
            Trace.FailureReason = message ?? kind.ToString();
            var warning = new AutoRunWarning
            {
                Kind = kind == AutoRunTerminationKind.UnsupportedMechanic
                    ? AutoRunWarningKind.UnsupportedMechanic
                    : (kind == AutoRunTerminationKind.NoLegalPlacement
                        ? AutoRunWarningKind.NoLegalPlacement
                        : AutoRunWarningKind.None),
                Code = code ?? kind.ToString(),
                Message = Trace.FailureReason,
                Week = _run.WeekIndex,
            };
            Trace.Warnings.Add(warning);
            if (Trace.Stages.Count > 0)
            {
                Trace.Stages[Trace.Stages.Count - 1].Termination = kind;
                Trace.Stages[Trace.Stages.Count - 1].Warnings.Add(warning);
            }

            _stopped = true;
            _callbacks.Clear();
        }

        private void Enqueue(Action callback)
        {
            if (!_stopped && callback != null)
            {
                _callbacks.Enqueue(callback);
            }
        }

        private readonly struct FragmentCandidate
        {
            public FragmentCandidate(
                string id,
                int rotation,
                GridPos origin,
                int sharedEdges,
                int boundsArea,
                int boundsPerimeter,
                int centerOffset)
            {
                Id = id;
                Rotation = rotation;
                Origin = origin;
                SharedEdges = sharedEdges;
                BoundsArea = boundsArea;
                BoundsPerimeter = boundsPerimeter;
                CenterOffset = centerOffset;
            }

            public string Id { get; }
            public int Rotation { get; }
            public GridPos Origin { get; }
            public int SharedEdges { get; }
            public int BoundsArea { get; }
            public int BoundsPerimeter { get; }
            public int CenterOffset { get; }
        }
    }
}
