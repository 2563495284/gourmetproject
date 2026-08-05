using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;

namespace GourmetProject.Game.Balance
{
    /// <summary>Balance Lab 专用隔离自动玩家。不会写存档，也不设置 GameRunContext。</summary>
    public sealed class HeadlessRunSimulator
    {
        private readonly cfg.Tables _tables;
        private readonly GameplayDatabase _database;

        public HeadlessRunSimulator(cfg.Tables tables, GameplayDatabase database)
        {
            _tables = tables ?? throw new ArgumentNullException(nameof(tables));
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public AutoRunTrace Run(AutoRunRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            AutoPlayerPolicy policy = request.Policy ?? new AutoPlayerPolicy();
            var rng = new Xoshiro256SS(unchecked((ulong)(uint)request.Seed));
            var run = new GameRun(_tables, _database, request.CharacterId, $"balance-auto-{request.Seed}", 1);
            var trace = new AutoRunTrace { Seed = request.Seed, CharacterId = request.CharacterId, PlayerLevel = request.PlayerLevel };
            BuildArchetype? previous = null;

            try
            {
                for (int week = 1; week <= run.TotalWeeks && run.HeartsRemaining > 0; week++)
                {
                    run.SetWeekIndex(week);
                    TimelineService.RollWeekTimeline(run, rng);
                    ArchetypeClassification classification = BuildArchetypeClassifier.Classify(run, policy.DualArchetypeThreshold);
                    var stage = new AutoRunStageTrace
                    {
                        Week = week,
                        Archetype = classification.Primary,
                        ArchetypeChanged = previous.HasValue && previous.Value != classification.Primary,
                        MetaRoute = ResolveMetaRoute(run, request.MetaAffinity),
                        GoldBalance = run.Gold,
                    };
                    if (stage.ArchetypeChanged) trace.ArchetypeChanges++;
                    previous = classification.Primary;
                    int actions = 0;
                    while (run.CurrentDay + TimelineMath.Epsilon < run.TimelineLengthDays && actions++ < policy.MaxActionsPerWeek)
                    {
                        List<ActionChoice> choices = ActionScheduleService.GenerateChoices(run, rng);
                        if (choices.Count == 0) break;
                        ActionChoice choice = PickAction(choices, stage.MetaRoute, request.PlayerLevel, rng, policy);
                        ActionExecutionContext context = choice.ToExecutionContext();
                        ActionOutcome outcome = ActionExecutor.Execute(run, context, rng);
                        stage.Actions.Add(choice.Action.Id);
                        HandleOutcome(run, outcome, context, request, classification, rng, stage);
                        ActionExecutor.Commit(run, context);
                        ResolveDueTimelineNodes(run, request, classification, rng, stage);
                        if (run.HeartsRemaining <= 0) break;
                    }
                    ResolveDueTimelineNodes(run, request, classification, rng, stage);
                    stage.GoldBalance = run.Gold;
                    trace.Stages.Add(stage);
                }
                trace.Completed = run.HeartsRemaining > 0 && trace.Stages.Count >= run.TotalWeeks;
                if (!trace.Completed && string.IsNullOrEmpty(trace.FailureReason)) trace.FailureReason = "红心耗尽";
            }
            catch (Exception e)
            {
                trace.Completed = false;
                trace.FailureReason = e.ToString();
            }
            return trace;
        }

        private void HandleOutcome(GameRun run, ActionOutcome outcome, ActionExecutionContext context,
            AutoRunRequest request, ArchetypeClassification archetype, IRandomStream rng,
            AutoRunStageTrace stage)
        {
            switch (outcome.Kind)
            {
                case ActionOutcomeKind.Battle:
                    ResolveBattle(run, outcome.RequiredScore, outcome.Modifier, outcome.BattleKey,
                        outcome.BossDebuffId, context, request, archetype, rng, stage);
                    if (outcome.IsBoss && run.HeartsRemaining > 0 && !string.IsNullOrEmpty(outcome.BossId))
                    {
                        run.MarkBossCompleted(outcome.BossId);
                        int bossGold = new ItemRuntime(run).ClaimBossCompleteGold();
                        if (bossGold > 0) { run.Gold += bossGold; stage.GoldEarned += bossGold; }
                    }
                    break;
                case ActionOutcomeKind.Shop:
                    ResolveShop(run, request, archetype, rng, stage);
                    break;
                case ActionOutcomeKind.Event:
                    ResolveEvent(run, outcome.EventId, request, archetype, rng, stage);
                    break;
                case ActionOutcomeKind.Slot:
                    ResolveEvent(run, outcome.SlotEventId, request, archetype, rng, stage);
                    break;
            }
        }

        private void ResolveDueTimelineNodes(GameRun run, AutoRunRequest request,
            ArchetypeClassification archetype, IRandomStream rng, AutoRunStageTrace stage)
        {
            cfg.TimelineNode node;
            int guard = 0;
            while (run.HeartsRemaining > 0 && guard++ < 64 &&
                   (node = TimelineService.GetNextDueUntriggeredNode(run)) != null)
            {
                cfg.GameAction action = TimelineService.NodeAction(run, node);
                if (action == null) { run.MarkNodeTriggered(node.Id); continue; }
                var context = new ActionExecutionContext(action)
                {
                    SourceKey = node.Id,
                    TargetScoreDayOverride = FoodService.IsBossAction(run.Tables, action) ? (float?)node.Day : null,
                };
                ActionOutcome outcome = ActionExecutor.Execute(run, context, rng);
                stage.Actions.Add("节点:" + action.Id);
                HandleOutcome(run, outcome, context, request, archetype, rng, stage);
                run.MarkNodeTriggered(node.Id);
            }
        }

        private void ResolveBattle(GameRun run, int required, string modifier, string key, string debuff,
            ActionExecutionContext context, AutoRunRequest request, ArchetypeClassification archetype,
            IRandomStream rng, AutoRunStageTrace stage)
        {
            BattleSession session = BattleSessionFactory.BuildHeadless(run, required, modifier, key, debuff, rng);
            AutoPlacementResult placement = AutoPlacementSolver.Solve(session,
                request.Policy.BeamWidth(request.PlayerLevel), request.Policy.PlacementNodeBudget);
            UseBattleActiveItems(run, session, stage);
            ScoreResult settled = session.Settle();
            placement.Score = settled.Total;
            ApplyBattleGrowth(run, session, rng, stage);
            stage.Score = Math.Max(stage.Score, placement.Score);
            stage.RequiredScore = Math.Max(stage.RequiredScore, required);
            stage.SolverTruncated |= placement.Truncated;
            stage.NoLegalPlacement |= !placement.HasLegalSolution;
            stage.SolverNodes += placement.SearchNodes;
            bool passed = placement.HasLegalSolution && placement.Score >= required;
            bool isBoss = context != null && FoodService.IsBossAction(run.Tables, context.Action);
            if (isBoss)
            {
                stage.BossReached = true;
                stage.BossScore = placement.Score;
                stage.BossRequiredScore = required;
                stage.BossPassed = passed;
                stage.Passed = passed;
            }
            else
            {
                stage.MealBattles++;
                if (passed) stage.MealPasses++;
            }
            if (!passed)
            {
                run.TryLoseHeart(out _, out _);
                if (run.HeartsRemaining <= 0) return;
            }
            int before = run.Gold;
            RewardOffer offer = RewardGranter.GenerateOffer(run, run.CurrentWeek, rng, context);
            RewardGranter.ApplyBaseGold(run, offer);
            ClaimGroups(run, offer, archetype, request.PlayerLevel, rng, stage);
            AutoAttachPendingFragment(run);
            stage.GoldEarned += Math.Max(0, run.Gold - before);
        }

        private static void ApplyBattleGrowth(GameRun run, BattleSession session, IRandomStream rng, AutoRunStageTrace stage)
        {
            foreach (RecipeScoreFlatDelta delta in session.LastRecipeScoreFlatDeltas)
                run.AddRecipeScoreFlat(delta.DishIndex, delta.Delta);
            foreach (RecipeScoreMultiplierDelta delta in session.LastRecipeScoreMultiplierDeltas)
                run.MultiplyRecipeScore(delta.DishIndex, delta.Multiplier);
            int gold = (int)Math.Round(session.PendingGold, MidpointRounding.AwayFromZero);
            if (gold != 0) { run.Gold = Math.Max(0, run.Gold + gold); stage.GoldEarned += Math.Max(0, gold); }
            for (int i = 0; i < session.PendingActiveItemGrants; i++)
                ItemPoolService.GrantRandom(run.Tables, run, cfg.ItemKind.Active, rng, 20);
        }

        private static void UseBattleActiveItems(GameRun run, BattleSession session, AutoRunStageTrace stage)
        {
            var ids = run.Items.Select(v => v.ItemId).ToList();
            foreach (string id in ids)
            {
                ItemDefinition item = ItemDefinition.Get(run.Tables, id, cfg.ItemKind.Active);
                if (item == null || !IsPositiveBattleEffect(item.EffectType)) continue;
                var context = new BattleUseContext(session, run);
                IReadOnlyList<ActiveTarget> candidates = context.EnumerateTargets(item);
                int count = Math.Max(1, item.TargetCount);
                var targets = candidates.Take(count).ToList();
                if (item.TargetKind != cfg.ItemTargetKind.None && targets.Count == 0) continue;
                ActiveItemUseResult result = ActiveItemEffectRegistry.Apply(context, item, targets);
                if (!result.Success) continue;
                run.RemoveItem(id);
                stage.ActiveItemsUsed.Add(id);
            }
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

        private static void AutoAttachPendingFragment(GameRun run)
        {
            if (run.PendingFragmentPack == null || run.PendingFragmentPack.Count == 0) return;
            cfg.Character character = run.Tables.TbCharacter.GetOrDefault(run.CharacterId);
            if (character == null) { run.ClearPendingFragmentPack(); return; }
            var board = run.BuildTablePreviewFromFragments();
            var existing = GourmetProject.Gameplay.Board.TableFragmentBuilder.ToExistingSet(board);
            for (int i = 0; i < run.PendingFragmentPack.Count; i++)
            {
                string id = run.PendingFragmentPack[i];
                int rotation = i < run.PendingFragmentPackRotations.Count ? run.PendingFragmentPackRotations[i] : 0;
                TableFragmentDef def = run.Database.GetFragment(id);
                if (def == null) continue;
                for (int y = -character.MaxDiningTableHeight; y <= character.MaxDiningTableHeight; y++)
                for (int x = -character.MaxDiningTableWidth; x <= character.MaxDiningTableWidth; x++)
                {
                    var origin = new GridPos(x, y);
                    if (!GourmetProject.Gameplay.Board.TableFragmentBuilder.CanPlaceFragmentAt(existing, def, rotation, origin,
                            character.MaxDiningTableWidth, character.MaxDiningTableHeight)) continue;
                    run.AddFragmentPlacement(id, rotation, origin);
                    run.ClearPendingFragmentPack();
                    return;
                }
            }
            run.ClearPendingFragmentPack();
        }

        private void ClaimGroups(GameRun run, RewardOffer offer, ArchetypeClassification archetype,
            AutoPlayerLevel level, IRandomStream rng, AutoRunStageTrace stage)
        {
            foreach (RewardChoiceGroup group in offer.FixedGroups.Concat(new[] { offer.SpecificGroup }))
            {
                var remaining = Enumerable.Range(0, group.Choices.Count).ToList();
                for (int pick = 0; pick < group.RequiredChoiceCount && remaining.Count > 0; pick++)
                {
                    int index = PickReward(group.Choices, remaining, archetype, level, rng);
                    RewardChoice choice = group.Choices[index];
                    string text = ApplyHeadlessChoice(run, choice);
                    group.MarkClaimed(index);
                    remaining.Remove(index);
                    stage.Rewards.Add(string.IsNullOrEmpty(choice.Id) ? text : choice.Id);
                }
            }
        }

        private static string ApplyHeadlessChoice(GameRun run, RewardChoice choice)
        {
            if (choice != null && (choice.Kind == cfg.RewardKind.PassiveItemChoice
                || choice.Kind == cfg.RewardKind.ActiveItemGrant
                || choice.Kind == cfg.RewardKind.ActiveItemStrengthen
                || choice.Kind == cfg.RewardKind.ActiveItemAdjust))
            {
                // 部分 OnAcquire 模型会立即存档；无界面样本禁止触碰正式持久化。
                return run.AcquireItem(choice.Id, choice.GoldAmount > 0 ? choice.GoldAmount : 40, fireOnAcquire: false)
                    .ToRewardText(string.Empty);
            }
            return RewardGranter.ApplyChoice(run, choice);
        }

        private int PickReward(IReadOnlyList<RewardChoice> choices, List<int> candidates,
            ArchetypeClassification archetype, AutoPlayerLevel level, IRandomStream rng)
        {
            var weights = new List<float>();
            foreach (int i in candidates)
            {
                RewardChoice c = choices[i];
                DishDef dish = _database.GetDish(c.Id);
                float value = 1f + (dish != null ? BuildArchetypeClassifier.DishAffinity(dish, _database, archetype.Active) : 0.5f);
                weights.Add(Math.Max(0.01f, value));
            }
            int local = level == AutoPlayerLevel.Expert ? IndexOfMax(weights) : rng.WeightedPickIndex(weights);
            return candidates[local];
        }

        private void ResolveShop(GameRun run, AutoRunRequest request, ArchetypeClassification archetype,
            IRandomStream rng, AutoRunStageTrace stage)
        {
            List<ShopEntry> stock = ShopService.RollStock(_tables, run, rng, rng);
            int reserve = stage.MetaRoute == MetaRoute.Interest ? request.Policy.InterestReserve : 0;
            while (true)
            {
                // 商店碎片包的旧接口仍依赖全局随机流；无界面模拟先不购买，奖励碎片仍会正常自动拼接。
                var legal = stock.Where(v => v.IsStocked && v.Kind != ShopEntryKind.Fragment && v.Price <= run.Gold - reserve).ToList();
                if (legal.Count == 0) break;
                var values = legal.Select(v => Math.Max(0.01f, ShopValue(v, archetype))).ToList();
                int pick = request.PlayerLevel == AutoPlayerLevel.Expert ? IndexOfMax(values) : rng.WeightedPickIndex(values);
                if (values[pick] <= 0.05f) break;
                ShopEntry entry = legal[pick];
                int before = run.Gold;
                if (!ShopService.Purchase(run, entry)) { entry.ClearStock(); continue; }
                stage.GoldSpent += before - run.Gold;
                stage.Purchases.Add(entry.Id);
                entry.ClearStock();
                AutoAttachPendingFragment(run);
            }
        }

        private float ShopValue(ShopEntry entry, ArchetypeClassification archetype)
        {
            if (entry.Kind == ShopEntryKind.Dish)
                return 1f + BuildArchetypeClassifier.DishAffinity(_database.GetDish(entry.Id), _database, archetype.Active);
            return entry.Kind == ShopEntryKind.PassiveItem ? 2f : 1f;
        }

        private void ResolveEvent(GameRun run, string eventId, AutoRunRequest request,
            ArchetypeClassification archetype, IRandomStream rng, AutoRunStageTrace stage)
        {
            cfg.GameEvent ev = string.IsNullOrEmpty(eventId) ? EventService.RollActionEvent(run, rng) : _tables.TbEvent.GetOrDefault(eventId);
            if (ev == null) return;
            string parent = string.Empty;
            EventResolveResult result = EventResolveResult.Immediate(string.Empty);
            for (int depth = 0; depth < 16; depth++)
            {
                List<cfg.EventOption> options = string.IsNullOrEmpty(parent)
                    ? EventService.GetRootOptions(run, ev.Id)
                    : EventService.GetChildOptions(run, ev.Id, parent);
                if (options.Count == 0) break;
                cfg.EventOption option;
                if (!string.IsNullOrEmpty(parent)
                    && EventService.TryRollWeightedChild(options, rng, out cfg.EventOption randomChild))
                {
                    option = randomChild;
                    if (!PreconditionEvaluator.IsSatisfied(run, option.Condition))
                    {
                        break;
                    }
                }
                else
                {
                    option = options[rng.Range(0, options.Count)];
                }
                parent = option.Id;
                result = EventService.ResolveOption(run, option, rng);
                if (result.IsBattle)
                    ResolveBattle(run, result.RequiredScore, result.Modifier, $"event_{ev.Id}", string.Empty, null, request, archetype, rng, stage);
                if (result.FollowUpKind != EventFollowUpKind.None) break;
            }
            EventService.OnEventFinished(run, ev, result);
            DrainGenericRewards(run, archetype, request.PlayerLevel, rng, stage);
        }

        private void DrainGenericRewards(GameRun run, ArchetypeClassification archetype,
            AutoPlayerLevel level, IRandomStream rng, AutoRunStageTrace stage)
        {
            int guard = 0;
            while (guard++ < 32 && run.TryPeekPendingGenericReward(out string key, out _, out RewardOffer offer))
            {
                int before = run.Gold;
                RewardGranter.ApplyBaseGold(run, offer);
                ClaimGroups(run, offer, archetype, level, rng, stage);
                AutoAttachPendingFragment(run);
                stage.GoldEarned += Math.Max(0, run.Gold - before);
                run.ClearPendingGenericRewardOffer(key);
            }
        }

        private static ActionChoice PickAction(List<ActionChoice> choices, MetaRoute route,
            AutoPlayerLevel level, IRandomStream rng, AutoPlayerPolicy policy)
        {
            var weights = choices.Select(c => Math.Max(0.01f, ActionWeight(c.Action, route))).ToList();
            int index = level == AutoPlayerLevel.Expert ? IndexOfMax(weights) : rng.WeightedPickIndex(weights);
            return choices[index];
        }

        private static float ActionWeight(cfg.GameAction action, MetaRoute route)
        {
            string token = action.Behavior.ToString();
            if (route == MetaRoute.Event && token.IndexOf("Event", StringComparison.OrdinalIgnoreCase) >= 0) return 4f;
            if (route == MetaRoute.Shop && token.IndexOf("Shop", StringComparison.OrdinalIgnoreCase) >= 0) return 4f;
            if (route == MetaRoute.Interest && token.IndexOf("Interest", StringComparison.OrdinalIgnoreCase) >= 0) return 4f;
            string id = action.Id ?? string.Empty;
            if (id.IndexOf("active_strengthen", StringComparison.OrdinalIgnoreCase) >= 0) return 6f;
            if (id.IndexOf("passive", StringComparison.OrdinalIgnoreCase) >= 0) return 5f;
            if (id.IndexOf("fragment", StringComparison.OrdinalIgnoreCase) >= 0) return 4f;
            if (id.IndexOf("active_adjust", StringComparison.OrdinalIgnoreCase) >= 0) return 2f;
            if (id.IndexOf("gold", StringComparison.OrdinalIgnoreCase) >= 0) return 1f;
            return 1f;
        }

        private static MetaRoute ResolveMetaRoute(GameRun run, MetaAffinityCatalog catalog)
        {
            if (catalog == null) return MetaRoute.Normal;
            MetaRoute best = MetaRoute.Normal;
            float bestValue = 0f;
            foreach (MetaRoute route in Enum.GetValues(typeof(MetaRoute)))
            {
                float value = run.Items.Sum(v => catalog.Get(v.ItemId, route));
                if (value > bestValue) { bestValue = value; best = route; }
            }
            return best;
        }

        private static int IndexOfMax(IReadOnlyList<float> values)
        {
            int best = 0;
            for (int i = 1; i < values.Count; i++) if (values[i] > values[best]) best = i;
            return best;
        }
    }
}
