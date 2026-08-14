using System;
using System.Collections.Generic;
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
            ActionChoice choice = PickAction(choices, stage.MetaRoute);
            if (choice != null)
            {
                stage.Actions.Add(choice.Action?.Id ?? string.Empty);
            }
            ActionOfferService.Resolve(_run);
            Enqueue(() => _loop.OnActionPicked(choice));
        }

        public void OpenShop()
        {
            AutoRunStageTrace stage = Stage;
            var session = new ShopSession(_run);
            int reserve = stage.MetaRoute == MetaRoute.Interest
                ? Math.Max(0, _policy.InterestReserve)
                : 0;
            int guard = 0;
            while (++guard <= 128)
            {
                List<ShopEntry> legal = session.Stock
                    .Where(entry => entry != null
                        && entry.IsStocked
                        && ShopService.CurrentPrice(_run, entry) <= _run.Gold - reserve)
                    .ToList();
                if (legal.Count == 0)
                {
                    break;
                }

                var values = legal.Select(entry => Math.Max(0.01f, ShopValue(entry))).ToList();
                int local = _request.PlayerLevel == AutoPlayerLevel.Expert
                    ? IndexOfMax(values)
                    : _decisionRandom.WeightedPickIndex(values);
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
            BattleSession session = BattleSessionFactory.Build(
                _run,
                requiredScore,
                modifier,
                key,
                bossDebuffId);
            UseBattleActiveItems(session, stage, beforePlacement: true);
            AutoPlacementResult placement = AutoPlacementSolver.Solve(
                session,
                _request.PlayerLevel,
                _policy,
                _placementRandom);
            UseBattleActiveItems(session, stage, beforePlacement: false);
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
            bool isBoss = actionContext != null
                && FoodService.IsBossAction(_run.Tables, actionContext.Action);
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

            Enqueue(() => _loop.OnBattleSettled(settled, passed, session.HappyCakeLayers));
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
                var enabled = new List<int>();
                for (int i = 0; i < options.Count; i++)
                {
                    if (optionEnabled == null || i >= optionEnabled.Count || optionEnabled[i])
                    {
                        enabled.Add(i);
                    }
                }

                if (enabled.Count > 0)
                {
                    int selected = _request.PlayerLevel == AutoPlayerLevel.Expert
                        ? enabled[0]
                        : enabled[_decisionRandom.Range(0, enabled.Count)];
                    Enqueue(() => onPick(selected));
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
            ArchetypeClassification archetype = BuildArchetypeClassifier.Classify(
                _run,
                _policy.DualArchetypeThreshold);
            RewardClaimResult claimed = RewardClaimService.ClaimOffer(
                _run,
                offer,
                (group, remaining) => PickReward(group, remaining, archetype),
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

        private int PickReward(
            RewardChoiceGroup group,
            IReadOnlyList<int> candidates,
            ArchetypeClassification archetype)
        {
            var weights = new List<float>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                RewardChoice choice = group.Choices[candidates[i]];
                DishDef dish = _database.GetDish(choice?.Id);
                float value = 1f + (dish != null
                    ? BuildArchetypeClassifier.DishAffinity(dish, _database, archetype.Active)
                    : 0.5f);
                weights.Add(Math.Max(0.01f, value));
            }

            int local = _request.PlayerLevel == AutoPlayerLevel.Expert
                ? IndexOfMax(weights)
                : _decisionRandom.WeightedPickIndex(weights);
            return candidates[local];
        }

        private ActionChoice PickAction(IReadOnlyList<ActionChoice> choices, MetaRoute route)
        {
            if (choices == null || choices.Count == 0)
            {
                return null;
            }

            var weights = choices
                .Select(choice => Math.Max(0.01f, ActionWeight(choice.Action, route)))
                .ToList();
            int index = _request.PlayerLevel == AutoPlayerLevel.Expert
                ? IndexOfMax(weights)
                : _decisionRandom.WeightedPickIndex(weights);
            return choices[index];
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
            if (id.IndexOf("active_strengthen", StringComparison.OrdinalIgnoreCase) >= 0) return 6f;
            if (id.IndexOf("passive", StringComparison.OrdinalIgnoreCase) >= 0) return 5f;
            if (id.IndexOf("fragment", StringComparison.OrdinalIgnoreCase) >= 0) return 4f;
            if (id.IndexOf("active_adjust", StringComparison.OrdinalIgnoreCase) >= 0) return 2f;
            return 1f;
        }

        private float ShopValue(ShopEntry entry)
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

            return entry.Kind == ShopEntryKind.PassiveItem ? 2f : 1f;
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
                if (item == null || !IsPositiveActionEffect(item.EffectType))
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

        private void UseBattleActiveItems(
            BattleSession session,
            AutoRunStageTrace stage,
            bool beforePlacement)
        {
            foreach (string id in _run.Items.Select(item => item.ItemId).ToList())
            {
                ItemDefinition item = ItemDefinition.Get(_run.Tables, id, cfg.ItemKind.Active);
                if (item == null
                    || !IsPositiveBattleEffect(item.EffectType)
                    || (item.EffectType == ItemEffectTypes.AddMaterial) != beforePlacement)
                {
                    continue;
                }

                var context = new BattleUseContext(session, _run);
                TryApplyAndConsumeActiveItem(context, item, stage);
                if (_stopped)
                {
                    return;
                }
            }
        }

        private bool TryApplyAndConsumeActiveItem(
            IActiveUseContext context,
            ItemDefinition item,
            AutoRunStageTrace stage)
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

        private static bool IsPositiveBattleEffect(string effectType)
        {
            return effectType == ItemEffectTypes.GoldNow
                || effectType == ItemEffectTypes.AddScore
                || effectType == ItemEffectTypes.AddCountAs
                || effectType == ItemEffectTypes.DuplicateDish
                || effectType == ItemEffectTypes.AddFlavor
                || effectType == ItemEffectTypes.EnhanceFlavor
                || effectType == ItemEffectTypes.ConvertFlavor
                || effectType == ItemEffectTypes.AddMaterial
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

        private void ResolvePendingFragment()
        {
            if (_run.PendingFragmentPack == null || _run.PendingFragmentPack.Count == 0)
            {
                return;
            }

            cfg.Character character = _run.Tables.TbCharacter.GetOrDefault(_run.CharacterId);
            if (character == null)
            {
                _run.ClearPendingFragmentPack();
                return;
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

                for (int y = -character.MaxDiningTableHeight; y <= character.MaxDiningTableHeight; y++)
                {
                    for (int x = -character.MaxDiningTableWidth; x <= character.MaxDiningTableWidth; x++)
                    {
                        var origin = new GridPos(x, y);
                        if (GourmetProject.Gameplay.Board.TableFragmentBuilder.CanPlaceFragmentAt(
                            existing,
                            definition,
                            rotation,
                            origin,
                            character.MaxDiningTableWidth,
                            character.MaxDiningTableHeight))
                        {
                            legal.Add(new FragmentCandidate(id, rotation, origin));
                        }
                    }
                }
            }

            if (legal.Count > 0)
            {
                FragmentCandidate selected = _request.PlayerLevel == AutoPlayerLevel.Expert
                    ? legal[0]
                    : legal[_decisionRandom.Range(0, legal.Count)];
                _run.AddFragmentPlacement(selected.Id, selected.Rotation, selected.Origin);
            }

            _run.ClearPendingFragmentPack();
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
                GridPos origin)
            {
                Id = id;
                Rotation = rotation;
                Origin = origin;
            }

            public string Id { get; }
            public int Rotation { get; }
            public GridPos Origin { get; }
        }
    }
}
