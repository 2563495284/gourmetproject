using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Balance;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class AutoPlacementSolverContractTests
    {
        [Test]
        public void PreviewPreparedPlacement_IsRepeatableAndRestoresEveryObservablePlacementState()
        {
            DishDef dish = Dish("preview", 10);
            var sessionRandom = new Xoshiro256SS(101UL);
            BattleSession session = Session(new DiningTable(2, 1), sessionRandom, new[] { dish }, dish.Id);
            ServePrepareResult prepared = session.PrepareServeAutomatically(0);
            Assert.That(prepared.Success, Is.True);
            RngState randomBefore = sessionRandom.State;
            Placement original = prepared.PreparedDish.Dish.Placement;
            Placement candidate = prepared.PreparedDish.Placements[1];

            PreparedPlacementPreview first = session.PreviewPreparedPlacement(candidate);
            PreparedPlacementPreview second = session.PreviewPreparedPlacement(candidate);

            Assert.That(first.Success, Is.True);
            Assert.That(second.Success, Is.True);
            Assert.That(first.Score, Is.EqualTo(second.Score));
            Assert.That(first.Score.ToDouble(), Is.EqualTo(10d));
            Assert.That(sessionRandom.State, Is.EqualTo(randomBefore), "preview must not consume gameplay RNG");
            Assert.That(session.PreparedServe, Is.SameAs(prepared.PreparedDish));
            Assert.That(session.DiningTable.DishCount, Is.Zero);
            Assert.That(session.HasPendingTablePlacements, Is.False);
            Assert.That(session.ServesUsed, Is.Zero);
            Assert.That(prepared.PreparedDish.Dish.Placement.Origin, Is.EqualTo(original.Origin));
            Assert.That(prepared.PreparedDish.Dish.Placement.RotationIndex, Is.EqualTo(original.RotationIndex));
            Assert.That(session.PreviewScore().Total.ToDouble(), Is.Zero);
        }

        [Test]
        public void PreviewPreparedPlacement_InvalidCandidateDoesNotMutateSession()
        {
            DishDef dish = Dish("invalid-preview", 10);
            BattleSession session = Session(
                new DiningTable(1, 1),
                new Xoshiro256SS(102UL),
                new[] { dish },
                dish.Id);
            ServePrepareResult prepared = session.PrepareServeAutomatically(0);
            Placement legal = prepared.PreparedDish.Placements[0];
            var invalid = new Placement(legal.Orientation, legal.RotationIndex, new GridPos(2, 0));

            PreparedPlacementPreview preview = session.PreviewPreparedPlacement(invalid);

            Assert.That(preview.Success, Is.False);
            Assert.That(preview.Outcome, Is.EqualTo(ServeOutcome.InvalidPlacement));
            Assert.That(session.PreparedServe, Is.SameAs(prepared.PreparedDish));
            Assert.That(session.DiningTable.DishCount, Is.Zero);
            Assert.That(session.ServesUsed, Is.Zero);
        }

        [Test]
        public void SolverPreview_UsesOneCommonSampleWithoutConsumingGameplayRandom()
        {
            var salty = new FlavorDef(
                "solver-salty",
                "solver-salty",
                string.Empty,
                FlavorEffectType.ExtraSettlementChance,
                new[] { 0.5f },
                Array.Empty<string>(),
                string.Empty);
            DishDef dish = Dish("solver-expected", 10, flavorId: salty.Id);
            var gameplayRandom = new TrackingRandomStream(1001UL, forcedWeightedIndex: 0);
            BattleSession session = Session(
                new DiningTable(1, 1),
                gameplayRandom,
                new[] { dish },
                dish.Id,
                flavors: new[] { salty });
            ServePrepareResult prepared = session.PrepareServeAutomatically(0);
            RngState randomBefore = gameplayRandom.State;

            PreparedPlacementPreview ui = session.PreviewPreparedPlacement(prepared.PreparedDish.Placements[0]);
            PreparedPlacementPreview solver = session.PreviewPreparedPlacementForSolver(
                prepared.PreparedDish.Placements[0]);
            PreparedPlacementPreview repeated = session.PreviewPreparedPlacementForSolver(
                prepared.PreparedDish.Placements[0]);

            Assert.That(ui.Score.ToDouble(), Is.EqualTo(10d), "UI preview keeps its existing no-random semantic");
            Assert.That(solver.ScoreCalculationCount, Is.EqualTo(1),
                "one solver node must equal one full ScoreCalculator execution");
            Assert.That(solver.Score.ToDouble(), Is.EqualTo(10d).Or.EqualTo(20d),
                "the stable common sample resolves the probabilistic settle exactly once");
            Assert.That(repeated.Score, Is.EqualTo(solver.Score),
                "the common sample must be stable across repeated candidate previews");
            Assert.That(gameplayRandom.State, Is.EqualTo(randomBefore));
            Assert.That(gameplayRandom.WeightedPickCalls, Is.EqualTo(1));
            Assert.That(session.PreparedServe, Is.SameAs(prepared.PreparedDish));
            Assert.That(session.DiningTable.DishCount, Is.Zero);
            Assert.That(session.ServesUsed, Is.Zero);
        }

        [Test]
        public void SolverPreview_ReportsExactlyOneRealScoreCalculation()
        {
            DishDef dish = Dish("single-score-calculation", 10);
            var counter = new CountingScoreEffectSource();
            var calculator = new ScoreCalculator(effectSources: new[] { counter });
            BattleSession session = Session(
                new DiningTable(1, 1),
                new Xoshiro256SS(1003UL),
                new[] { dish },
                dish.Id,
                calculator: calculator);
            ServePrepareResult prepared = session.PrepareServeAutomatically(0);

            PreparedPlacementPreview first = session.PreviewPreparedPlacementForSolver(
                prepared.PreparedDish.Placements[0]);
            PreparedPlacementPreview second = session.PreviewPreparedPlacementForSolver(
                prepared.PreparedDish.Placements[0]);

            Assert.That(first.ScoreCalculationCount, Is.EqualTo(1));
            Assert.That(second.ScoreCalculationCount, Is.EqualTo(1));
            Assert.That(counter.CollectCalls, Is.EqualTo(2),
                "two successful previews must run ScoreCalculator exactly twice, not four times each");
        }

        [Test]
        public void ScoreCalculator_DiagnosticsOffMatchesEveryRuleResultWithoutCapturingLinesEventsOrRandom()
        {
            var salty = new FlavorDef(
                "fast-path-salty",
                "fast-path-salty",
                string.Empty,
                FlavorEffectType.ExtraSettlementChance,
                new[] { 1f },
                Array.Empty<string>(),
                string.Empty);
            DishDef dishDef = Dish("fast-path-dish", 10, flavorId: salty.Id);
            var table = new DiningTable(1, 1);
            var dish = new DishInstance(
                1,
                dishDef,
                new Placement(dishDef.Shape, 0, new GridPos(0, 0)),
                Array.Empty<string>(),
                new[] { salty.Id });
            dish.SetSourceRecipeIndex(0, 0);
            table.Place(dish);
            var db = new GameplayDatabase(
                new[] { dishDef },
                Array.Empty<SkillDef>(),
                new[] { salty },
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var calculator = new ScoreCalculator(
                effectSources: new[] { new DiagnosticsParityEffectSource(dish) });
            int fullCopyCalls = 0;
            int fastCopyCalls = 0;
            int fullRandomCalls = 0;
            int fastRandomCalls = 0;

            ScoreResult full = calculator.Calculate(
                table,
                db,
                initialHappyCakeLayers: 3,
                copySkillSelector: (candidates, count) =>
                {
                    fullCopyCalls++;
                    return candidates.Take(count).ToArray();
                },
                randomIntegerSelector: (minimum, maximum) =>
                {
                    fullRandomCalls++;
                    return minimum;
                },
                captureDiagnostics: true);
            ScoreResult fast = calculator.Calculate(
                table,
                db,
                initialHappyCakeLayers: 3,
                copySkillSelector: (candidates, count) =>
                {
                    fastCopyCalls++;
                    return candidates.Take(count).ToArray();
                },
                randomIntegerSelector: (minimum, maximum) =>
                {
                    fastRandomCalls++;
                    return minimum;
                },
                captureDiagnostics: false);

            Assert.That(NonDiagnosticSignature(fast), Is.EqualTo(NonDiagnosticSignature(full)),
                "turning diagnostics off must not change score, rule commands, or side-effect requests");
            Assert.That(full.ScoreLines, Is.Not.Empty);
            Assert.That(full.ScoreEvents, Is.Not.Empty);
            Assert.That(fast.ScoreLines, Is.Empty);
            Assert.That(fast.ScoreEvents, Is.Empty);
            Assert.That(fastCopyCalls, Is.EqualTo(fullCopyCalls).And.EqualTo(1));
            Assert.That(fastRandomCalls, Is.EqualTo(fullRandomCalls).And.EqualTo(1));
            Assert.That(table.Dishes.Single(), Is.SameAs(dish));
        }

        [Test]
        public void SolverPreview_BypassesBuffetDisplayGateForEarlyPlacements()
        {
            DishDef dish = Dish("buffet-preview", 10);
            BattleSession session = Session(
                new DiningTable(1, 1),
                new Xoshiro256SS(1002UL),
                new[] { dish },
                dish.Id);
            session.MinimumServesForScore = 10;
            ServePrepareResult prepared = session.PrepareServeAutomatically(0);
            Placement placement = prepared.PreparedDish.Placements[0];

            Assert.That(session.PreviewPreparedPlacement(placement).Score.ToDouble(), Is.Zero);
            Assert.That(session.PreviewPreparedPlacementForSolver(placement).Score.ToDouble(), Is.EqualTo(10d));
            Assert.That(session.PreparedServe, Is.SameAs(prepared.PreparedDish));
            Assert.That(session.ServesUsed, Is.Zero);
            Assert.That(session.DiningTable.DishCount, Is.Zero);
        }

        [Test]
        public void Expert_TiePrefersFutureCellsInSweetTransferDirection()
        {
            const string skillId = "row-transfer";
            var transfer = new SkillRuleDef(
                "row-transfer-rule",
                skillId,
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.All,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.TransferSkills,
                SkillScope.Row,
                1,
                Array.Empty<float>(),
                Array.Empty<string>());
            var payload = new SkillRuleDef(
                "row-transfer-payload",
                skillId,
                1,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.All,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.AddFlat,
                SkillScope.Self,
                1,
                new[] { 5f },
                Array.Empty<string>());
            var skill = new SkillDef(
                skillId,
                skillId,
                string.Empty,
                Array.Empty<string>(),
                new[] { transfer, payload });
            DishDef dish = Dish("row-transfer-dish", 10, new[] { skill.Id });
            DishDef futureDish = Dish("row-transfer-future", 10);
            var existing = new[]
            {
                new GridPos(0, 0),
                new GridPos(1, 0),
                new GridPos(0, 1),
                new GridPos(1, 1),
                new GridPos(2, 1),
            };
            var db = new GameplayDatabase(
                new[] { dish, futureDish },
                new[] { skill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var session = new BattleSession(
                new DiningTable(3, 2, existing, materials: null),
                db,
                new TrackingRandomStream(1003UL, forcedWeightedIndex: 0),
                new[] { new RecipeSlot("slot", new[] { dish.Id, futureDish.Id, futureDish.Id }) },
                requiredScore: 0);

            AutoPlacementResult result = AutoPlacementSolver.Solve(
                session,
                AutoPlayerLevel.Expert,
                new AutoPlayerPolicy { PlacementNodeBudget = 20 },
                policyRandom: null);

            PlacementDecisionTrace decision = result.Decisions.First();
            Assert.That(decision.PreviewScore.ToDouble(), Is.EqualTo(15d));
            Assert.That(decision.OriginY, Is.EqualTo(1),
                "the longer row has two future transfer targets; stable (0,0) only has one");
        }

        [Test]
        public void Expert_ArrowCookieCountsFutureTwoCellDishOnceAndReservesItsBuffDirection()
        {
            DishDef arrowDish = LeftArrowDish(out SkillDef arrowSkill);
            var futureDish = new DishDef(
                "future-two-cell",
                "future-two-cell",
                10,
                DishShape.FromRows(new[] { "XX" }),
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty);
            var db = new GameplayDatabase(
                new[] { arrowDish, futureDish },
                new[] { arrowSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var session = new BattleSession(
                new DiningTable(4, 1),
                db,
                new TrackingRandomStream(10031UL, forcedWeightedIndex: 0),
                new[] { new RecipeSlot("slot", new[] { arrowDish.Id, futureDish.Id }) },
                requiredScore: 0);

            AutoPlacementResult result = AutoPlacementSolver.Solve(
                session,
                AutoPlayerLevel.Expert,
                new AutoPlayerPolicy { PlacementNodeBudget = 20 },
                policyRandom: null);

            PlacementDecisionTrace decision = result.Decisions.First();
            Assert.That(decision.PreviewScore.ToDouble(), Is.EqualTo(15d));
            Assert.That(decision.DirectionalFuturePotential, Is.EqualTo(10d).Within(0.000001d),
                "the two cells belong to one future dish, so the +0.5 multiplier is valued once");
            Assert.That(decision.OriginX, Is.EqualTo(2),
                "left-arrow placement must leave the two cells on its left available for future dishes");
        }

        [Test]
        public void Expert_LastRecipeEntryHasZeroDirectionalFuturePotential()
        {
            DishDef arrowDish = LeftArrowDish(out SkillDef arrowSkill);
            BattleSession session = Session(
                new DiningTable(4, 1),
                new Xoshiro256SS(10032UL),
                new[] { arrowDish },
                arrowDish.Id,
                skills: new[] { arrowSkill });

            AutoPlacementResult result = AutoPlacementSolver.Solve(
                session,
                AutoPlayerLevel.Expert,
                new AutoPlayerPolicy { PlacementNodeBudget = 20 },
                policyRandom: null);

            PlacementDecisionTrace decision = result.Decisions.Single();
            Assert.That(decision.DirectionalFuturePotential, Is.Zero);
            Assert.That(decision.PlanningScore, Is.EqualTo(decision.PreviewScore));
            Assert.That(decision.OriginX, Is.Zero,
                "without a future recipe entry the stable coordinate wins an exact score tie");
        }

        [Test]
        public void Solver_DiscardsInvalidPreparedDishThroughFormalLimit()
        {
            DishDef dish = Dish("invalid-prepared", 10);
            var table = new DiningTable(1, 1);
            BattleSession session = Session(
                table,
                new Xoshiro256SS(1004UL),
                new[] { dish },
                dish.Id);
            session.ConfigureFoodDiscardLimit(1);
            ServePrepareResult prepared = session.PrepareServeAutomatically(0);
            table.SetDisabled(prepared.PreparedDish.Placements[0].Origin, true);

            AutoPlacementResult result = AutoPlacementSolver.Solve(
                session,
                AutoPlayerLevel.Expert,
                new AutoPlayerPolicy { PlacementNodeBudget = 10 },
                policyRandom: null);

            Assert.That(result.Termination, Is.EqualTo(AutoPlacementTerminationKind.Completed));
            Assert.That(result.HasLegalSolution, Is.True);
            Assert.That(result.Decisions, Is.Empty);
            Assert.That(session.FoodDiscardsUsed, Is.EqualTo(1));
            Assert.That(session.FoodDiscardsRemaining, Is.Zero);
            Assert.That(session.PreparedServe, Is.Null);
            Assert.That(session.DiningTable.DishCount, Is.Zero);
        }

        [Test]
        public void Expert_DiscardsClearlyOutclassedPreparedDishAndNeverExceedsFormalLimit()
        {
            DishDef weak = Dish("weak-prepared", 10);
            DishDef strong = Dish("strong-remaining", 30);
            var db = new GameplayDatabase(
                new[] { weak, strong },
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var gameplayRandom = new TrackingRandomStream(1005UL, forcedWeightedIndex: 0);
            var session = new BattleSession(
                new DiningTable(1, 1),
                db,
                gameplayRandom,
                new[] { new RecipeSlot("slot", new[] { weak.Id, strong.Id }) },
                requiredScore: 0);
            session.ConfigureFoodDiscardLimit(1);
            Assert.That(session.PrepareServeAutomatically(0).PreparedDish.Definition.Id, Is.EqualTo(weak.Id));

            AutoPlacementResult result = AutoPlacementSolver.Solve(
                session,
                AutoPlayerLevel.Expert,
                new AutoPlayerPolicy { PlacementNodeBudget = 20 },
                policyRandom: null);

            Assert.That(result.Termination, Is.EqualTo(AutoPlacementTerminationKind.Completed));
            Assert.That(session.FoodDiscardsUsed, Is.EqualTo(1));
            Assert.That(session.FoodDiscardsRemaining, Is.Zero);
            Assert.That(session.Slots[0].Count, Is.Zero);
            Assert.That(session.DiningTable.DishCount, Is.EqualTo(1));
            Assert.That(session.DiningTable.Dishes.Single().Def.Id, Is.EqualTo(strong.Id));
            Assert.That(result.Decisions, Has.Count.EqualTo(1));
            Assert.That(result.Decisions[0].DishId, Is.EqualTo(strong.Id));
        }

        [Test]
        public void Solver_DoesNotDiscardEqualScoreCandidate()
        {
            DishDef good = Dish("equal-existing", 100);
            DishDef neutral = Dish("equal-neutral", 0);
            BattleSession session = Session(
                new DiningTable(2, 1),
                new Xoshiro256SS(1006UL),
                new[] { good, neutral },
                neutral.Id);
            Assert.That(session.GenerateDishAt(good.Id, new GridPos(0, 0)), Is.True);
            session.ConfigureFoodDiscardLimit(1);

            AutoPlacementResult result = AutoPlacementSolver.Solve(
                session,
                AutoPlayerLevel.Expert,
                new AutoPlayerPolicy { PlacementNodeBudget = 10 },
                policyRandom: null);

            Assert.That(result.Decisions, Has.Count.EqualTo(1));
            Assert.That(session.FoodDiscardsUsed, Is.Zero);
            Assert.That(session.FoodDiscardsRemaining, Is.EqualTo(1));
        }

        [Test]
        public void Solve_UsesAutomaticPrepareSoConfiguredInsertionsParticipate()
        {
            DishDef recipeDish = Dish("recipe", 10);
            DishDef insertedDish = Dish("inserted", 20);
            BattleSession session = Session(
                new DiningTable(1, 1),
                new Xoshiro256SS(103UL),
                new[] { recipeDish, insertedDish },
                recipeDish.Id);
            session.ConfigureInsertedDishSequence(insertedDish.Id, windowSize: 1, countPerWindow: 1);

            AutoPlacementResult result = AutoPlacementSolver.Solve(
                session,
                AutoPlayerLevel.Expert,
                new AutoPlayerPolicy { PlacementNodeBudget = 10 },
                policyRandom: null);

            Assert.That(result.Termination, Is.EqualTo(AutoPlacementTerminationKind.Completed));
            Assert.That(result.Decisions, Has.Count.EqualTo(1));
            Assert.That(result.Decisions[0].DishId, Is.EqualTo(insertedDish.Id));
            Assert.That(session.Slots[0].Count, Is.EqualTo(1), "inserted dish must not consume the recipe entry");
            Assert.That(session.DiningTable.Dishes.Single().Def.Id, Is.EqualTo(insertedDish.Id));
        }

        [Test]
        public void AutomaticPrepare_ChoosesOnlyFittingCandidatesAndReturnsEveryPlacementForChosenDish()
        {
            DishDef oneCell = Dish("fit-one", 10);
            var twoCell = new DishDef(
                "fit-two",
                "fit-two",
                20,
                DishShape.FromRows(new[] { "XX" }),
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty);
            var tooWide = new DishDef(
                "too-wide",
                "too-wide",
                40,
                DishShape.FromRows(new[] { "XXXX" }),
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty);
            var table = new DiningTable(3, 1);
            var db = new GameplayDatabase(
                new[] { oneCell, twoCell, tooWide },
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var random = new TrackingRandomStream(110UL, forcedWeightedIndex: 1);
            var session = new BattleSession(
                table,
                db,
                random,
                new[] { new RecipeSlot("slot", new[] { oneCell.Id, twoCell.Id, tooWide.Id }) },
                requiredScore: 0);

            ServePrepareResult prepared = session.PrepareServeAutomatically(0);

            Assert.That(prepared.Success, Is.True);
            Assert.That(prepared.PreparedDish.Definition.Id, Is.EqualTo(twoCell.Id));
            Assert.That(prepared.PreparedDish.Placements.Select(value => value.Origin),
                Is.EqualTo(new[] { new GridPos(0, 0), new GridPos(1, 0) }));
            Assert.That(random.WeightedPickCalls, Is.EqualTo(1));
            Assert.That(random.LastWeights, Is.EqualTo(new[] { 1f, 2f }),
                "the impossible four-cell dish must not enter the formal weighted roll");
            Assert.That(session.Slots[0].Count, Is.EqualTo(2));
        }

        [Test]
        public void Normal_BoundsAt24AndUsesIndependentRankSoftmaxAtTemperatureOne()
        {
            DishDef dish = Dish("normal", 10);
            var gameplayRandom = new TrackingRandomStream(104UL, forcedWeightedIndex: 0);
            var policyRandom = new TrackingRandomStream(204UL, forcedWeightedIndex: int.MaxValue);
            BattleSession session = Session(new DiningTable(30, 1), gameplayRandom, new[] { dish }, dish.Id);
            var policy = new AutoPlayerPolicy
            {
                NormalBeamWidth = 128,
                PlacementNodeBudget = 100,
            };

            AutoPlacementResult result = AutoPlacementSolver.Solve(
                session,
                AutoPlayerLevel.Normal,
                policy,
                policyRandom);

            PlacementDecisionTrace decision = result.Decisions.Single();
            Assert.That(decision.SelectionKind, Is.EqualTo(PlacementSelectionKind.RankSoftmax));
            Assert.That(decision.RankSoftmaxTemperature, Is.EqualTo(1f));
            Assert.That(decision.LegalPlacementCount, Is.EqualTo(30));
            Assert.That(decision.CandidateLimit, Is.EqualTo(24));
            Assert.That(decision.EvaluatedPlacementCount, Is.EqualTo(24));
            Assert.That(decision.SelectedRank, Is.EqualTo(23));
            Assert.That(decision.SelectedCandidateIndex, Is.InRange(0, 29));
            Assert.That(decision.OriginX, Is.EqualTo(decision.SelectedCandidateIndex));
            Assert.That(decision.CandidateLimitApplied, Is.True);
            Assert.That(result.SearchNodes, Is.EqualTo(24));
            Assert.That(result.Warnings.Select(v => v.Kind),
                Does.Contain(AutoPlacementWarningKind.CandidateLimitApplied));
            Assert.That(gameplayRandom.WeightedPickCalls, Is.EqualTo(1), "session owns recipe/output RNG");
            Assert.That(policyRandom.WeightedPickCalls, Is.EqualTo(1), "policy owns placement choice RNG");
            Assert.That(policyRandom.LastWeights, Has.Count.EqualTo(24));
            Assert.That(policyRandom.LastWeights[0], Is.EqualTo(1f).Within(0.000001f));
            Assert.That(policyRandom.LastWeights[1], Is.EqualTo((float)Math.Exp(-1d)).Within(0.000001f));
        }

        [Test]
        public void Expert_BoundsAt64AndSelectsBestWithoutPolicyRandom()
        {
            DishDef dish = Dish("expert", 10);
            var gameplayRandom = new TrackingRandomStream(105UL, forcedWeightedIndex: 0);
            var policyRandom = new TrackingRandomStream(205UL, forcedWeightedIndex: int.MaxValue);
            BattleSession session = Session(new DiningTable(70, 1), gameplayRandom, new[] { dish }, dish.Id);

            AutoPlacementResult result = AutoPlacementSolver.Solve(
                session,
                AutoPlayerLevel.Expert,
                new AutoPlayerPolicy { ExpertBeamWidth = 128, PlacementNodeBudget = 100 },
                policyRandom);

            PlacementDecisionTrace decision = result.Decisions.Single();
            Assert.That(decision.SelectionKind, Is.EqualTo(PlacementSelectionKind.BestScore));
            Assert.That(decision.LegalPlacementCount, Is.EqualTo(70));
            Assert.That(decision.CandidateLimit, Is.EqualTo(64));
            Assert.That(decision.EvaluatedPlacementCount, Is.EqualTo(64));
            Assert.That(decision.SelectedRank, Is.Zero);
            Assert.That(decision.SelectedCandidateIndex, Is.Zero);
            Assert.That(decision.OriginX, Is.Zero);
            Assert.That(result.SearchNodes, Is.EqualTo(64));
            Assert.That(gameplayRandom.WeightedPickCalls, Is.EqualTo(1));
            Assert.That(policyRandom.WeightedPickCalls, Is.Zero);
        }

        [Test]
        public void Expert_SelectsHighestExactPreparedPlacementPreview()
        {
            const string materialId = "best-cell";
            DishDef dish = Dish("scored", 10);
            var material = new MaterialDef(
                materialId,
                materialId,
                string.Empty,
                MaterialEffectType.AddFlat,
                new[] { 100f },
                Array.Empty<string>(),
                string.Empty);
            var cellMaterials = new Dictionary<GridPos, IReadOnlyList<string>>
            {
                [new GridPos(1, 0)] = new[] { materialId },
            };
            var table = new DiningTable(2, 1, existingCells: null, materials: cellMaterials);
            BattleSession session = Session(
                table,
                new Xoshiro256SS(106UL),
                new[] { dish },
                dish.Id,
                new[] { material });

            AutoPlacementResult result = AutoPlacementSolver.Solve(
                session,
                AutoPlayerLevel.Expert,
                new AutoPlayerPolicy { PlacementNodeBudget = 10 },
                policyRandom: null);

            PlacementDecisionTrace decision = result.Decisions.Single();
            Assert.That(decision.OriginX, Is.EqualTo(1));
            Assert.That(decision.PreviewScore.ToDouble(), Is.EqualTo(110d));
            Assert.That(result.Score.ToDouble(), Is.EqualTo(110d));
        }

        [Test]
        public void Expert_IsNotWorseThanNormalOnFixedScoredBoard()
        {
            const string materialId = "expert-advantage-cell";
            DishDef dish = Dish("expert-advantage", 10);
            var material = new MaterialDef(
                materialId,
                materialId,
                string.Empty,
                MaterialEffectType.AddFlat,
                new[] { 100f },
                Array.Empty<string>(),
                string.Empty);
            var cellMaterials = new Dictionary<GridPos, IReadOnlyList<string>>
            {
                [new GridPos(1, 0)] = new[] { materialId },
            };
            var policy = new AutoPlayerPolicy { PlacementNodeBudget = 10 };
            BattleSession normalSession = Session(
                new DiningTable(2, 1, existingCells: null, materials: cellMaterials),
                new Xoshiro256SS(206UL),
                new[] { dish },
                dish.Id,
                new[] { material });
            BattleSession expertSession = Session(
                new DiningTable(2, 1, existingCells: null, materials: cellMaterials),
                new Xoshiro256SS(206UL),
                new[] { dish },
                dish.Id,
                new[] { material });

            AutoPlacementResult normal = AutoPlacementSolver.Solve(
                normalSession,
                AutoPlayerLevel.Normal,
                policy,
                new TrackingRandomStream(306UL, forcedWeightedIndex: int.MaxValue));
            AutoPlacementResult expert = AutoPlacementSolver.Solve(
                expertSession,
                AutoPlayerLevel.Expert,
                policy,
                policyRandom: null);

            Assert.That(expert.Score, Is.GreaterThanOrEqualTo(normal.Score));
            Assert.That(expert.Decisions.Single().SelectedRank, Is.Zero);
        }

        [Test]
        public void NodeBudget_IsHardBoundAndProducesStructuredTerminationAndWarning()
        {
            DishDef dish = Dish("budget", 10);
            BattleSession session = Session(
                new DiningTable(4, 1),
                new Xoshiro256SS(107UL),
                new[] { dish },
                dish.Id);

            AutoPlacementResult result = AutoPlacementSolver.Solve(
                session,
                AutoPlayerLevel.Expert,
                new AutoPlayerPolicy { PlacementNodeBudget = 2 },
                policyRandom: null);

            Assert.That(result.SearchNodes, Is.EqualTo(2));
            Assert.That(result.SearchNodes, Is.LessThanOrEqualTo(2));
            Assert.That(result.Truncated, Is.True);
            Assert.That(result.HasLegalSolution, Is.True);
            Assert.That(result.Termination, Is.EqualTo(AutoPlacementTerminationKind.NodeBudgetExhausted));
            Assert.That(result.Warnings.Select(v => v.Kind),
                Does.Contain(AutoPlacementWarningKind.NodeBudgetExhausted));
            Assert.That(result.Decisions.Single().EvaluatedPlacementCount, Is.EqualTo(2));
        }

        [Test]
        public void NormalWithoutPolicyRandom_FailsStructurallyBeforeMutatingSession()
        {
            DishDef dish = Dish("missing-policy-rng", 10);
            BattleSession session = Session(
                new DiningTable(1, 1),
                new Xoshiro256SS(108UL),
                new[] { dish },
                dish.Id);

            AutoPlacementResult result = AutoPlacementSolver.Solve(
                session,
                AutoPlayerLevel.Normal,
                new AutoPlayerPolicy(),
                policyRandom: null);

            Assert.That(result.HasLegalSolution, Is.False);
            Assert.That(result.Termination, Is.EqualTo(AutoPlacementTerminationKind.MissingPolicyRandom));
            Assert.That(result.SearchNodes, Is.Zero);
            Assert.That(session.PreparedServe, Is.Null);
            Assert.That(session.Slots[0].Count, Is.EqualTo(1));
            Assert.That(session.DiningTable.DishCount, Is.Zero);
        }

        [Test]
        public void TraceContracts_InitializeCollectionsAndClampPlayerCandidateLimits()
        {
            var policy = new AutoPlayerPolicy { NormalBeamWidth = 1000, ExpertBeamWidth = 1000 };
            var trace = new AutoRunTrace();
            var stage = new AutoRunStageTrace();

            Assert.That(policy.PlacementCandidateLimit(AutoPlayerLevel.Normal), Is.EqualTo(24));
            Assert.That(policy.PlacementCandidateLimit(AutoPlayerLevel.Expert), Is.EqualTo(64));
            Assert.That(trace.Termination, Is.EqualTo(AutoRunTerminationKind.None));
            Assert.That(trace.Warnings, Is.Not.Null.And.Empty);
            Assert.That(trace.Stages, Is.Not.Null.And.Empty);
            Assert.That(stage.Warnings, Is.Not.Null.And.Empty);
            Assert.That(stage.PlacementDecisions, Is.Not.Null.And.Empty);
        }

        [Test]
        public void RunWritebackMarkers_AreIndependentAndIdempotent()
        {
            DishDef dish = Dish("markers", 10);
            BattleSession session = Session(
                new DiningTable(1, 1),
                new Xoshiro256SS(109UL),
                new[] { dish },
                dish.Id);

            Assert.That(session.IsRunRecipeGrowthApplied, Is.False);
            Assert.That(session.IsRunSettlementApplied, Is.False);
            Assert.That(session.TryMarkRunRecipeGrowthApplied(), Is.True);
            Assert.That(session.TryMarkRunRecipeGrowthApplied(), Is.False);
            Assert.That(session.IsRunRecipeGrowthApplied, Is.True);
            Assert.That(session.IsRunSettlementApplied, Is.False);
            Assert.That(session.TryMarkRunSettlementApplied(), Is.True);
            Assert.That(session.TryMarkRunSettlementApplied(), Is.False);
            Assert.That(session.IsRunSettlementApplied, Is.True);
        }

        private static DishDef Dish(
            string id,
            int deliciousness,
            IReadOnlyList<string> skillIds = null,
            string flavorId = "")
            => new DishDef(
                id,
                id,
                deliciousness,
                DishShape.FromRows(new[] { "X" }),
                0,
                0,
                1f,
                skillIds ?? Array.Empty<string>(),
                flavorId);

        private static DishDef LeftArrowDish(out SkillDef skill)
            => ScoringArrowDish(
                SkillActionType.AddMultFlat,
                0.5f,
                out skill,
                SkillScope.LeftAndSelf);

        private static DishDef ScoringArrowDish(
            SkillActionType actionType,
            float actionValue,
            out SkillDef skill,
            SkillScope actionScope = SkillScope.RightAndSelf)
        {
            const string skillId = "sk_arrow_cookie_fixture";
            var rule = new SkillRuleDef(
                "sk_arrow_cookie_fixture_1",
                skillId,
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                actionType,
                actionScope,
                0,
                new[] { actionValue },
                Array.Empty<string>());
            skill = new SkillDef(
                skillId,
                skillId,
                string.Empty,
                Array.Empty<string>(),
                new[] { rule });
            return new DishDef(
                "arrow_cookie_fixture",
                "arrow_cookie_fixture",
                10,
                DishShape.FromRows(new[] { "XX" }),
                0,
                0,
                1f,
                new[] { skill.Id },
                string.Empty);
        }

        private static BattleSession Session(
            DiningTable table,
            IRandomStream gameplayRandom,
            IReadOnlyList<DishDef> dishes,
            string recipeDishId,
            IReadOnlyList<MaterialDef> materials = null,
            IReadOnlyList<SkillDef> skills = null,
            IReadOnlyList<FlavorDef> flavors = null,
            ScoreCalculator calculator = null)
        {
            var db = new GameplayDatabase(
                dishes,
                skills ?? Array.Empty<SkillDef>(),
                flavors ?? Array.Empty<FlavorDef>(),
                materials ?? Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            return new BattleSession(
                table,
                db,
                gameplayRandom,
                new[] { new RecipeSlot("slot", new[] { recipeDishId }) },
                requiredScore: 0,
                calculator: calculator);
        }

        private sealed class CountingScoreEffectSource : IScoreEffectSource
        {
            public int CollectCalls { get; private set; }

            public void CollectEffects(ScoreSnapshot snapshot, ScoreEffectCollector collector)
            {
                CollectCalls++;
            }
        }

        private sealed class DiagnosticsParityEffectSource : IScoreEffectSource
        {
            private readonly DishInstance _dish;

            public DiagnosticsParityEffectSource(DishInstance dish)
            {
                _dish = dish;
            }

            public void CollectEffects(ScoreSnapshot snapshot, ScoreEffectCollector collector)
            {
                collector.Add(new ScoreEffectEntry(
                    ScorePhase.BeforeAll,
                    ScoreSource.Relic("diagnostics-parity", "diagnostics-parity"),
                    new DiagnosticsParityEffect(_dish)));
            }
        }

        private sealed class DiagnosticsParityEffect : IScoreEffect
        {
            private readonly DishInstance _dish;

            public DiagnosticsParityEffect(DishInstance dish)
            {
                _dish = dish;
            }

            public void Apply(ScoreContext context)
            {
                context.AddFlatTo(_dish, 2);
                context.MultiplyTo(_dish, 1.5);
                context.AddPermanentFlatTo(_dish, 3);
                context.AddPermanentMultTo(_dish, 1.25);
                context.GrantGold(7);
                context.RequestSilverItemRoll(0.2f);
                context.AddHappyCakeLayers(2, mult: false);
                context.AddFinalFlat(5);
                context.MultiplyFinalBy(1.1);
                context.AddTemporaryCategory(_dish, "cake", "fixture", "fixture category");
                context.RequestRecipeRemoval(_dish, 0.4f);
                context.RecordSkillTransfer(
                    _dish,
                    new[] { new SkillEffect(null, "fixture transfer") },
                    "fixture",
                    99);
                context.RecordCopySkill(_dish, new[] { "missing-fixture-skill" }, 1, "fixture");
            }
        }

        private static string NonDiagnosticSignature(ScoreResult result)
        {
            var values = new List<string>
            {
                $"score:{result.RawSum}|{result.FinalFlat}|{result.FinalMultiplier}|{result.Total}",
                $"global:{result.GoldDelta}|{result.HappyCakeLayerDelta}",
            };
            values.AddRange(result.DishScores.Select(score =>
                $"dish:{score.DishInstanceId}|{score.DishId}|{score.BaseValue}|{score.FlatBonus}|{score.Multiplier}|{score.EffectiveCountAs}|{score.ExtraSettlementContribution}|{score.ExtraSettlementCount}|{score.Contribution}"));
            values.AddRange(result.PermanentFlatDeltas.OrderBy(pair => pair.Key)
                .Select(pair => $"permanent-flat:{pair.Key}|{pair.Value}"));
            values.AddRange(result.PermanentMultDeltas.OrderBy(pair => pair.Key)
                .Select(pair => $"permanent-mult:{pair.Key}|{pair.Value}"));
            values.AddRange(result.SilverItemRolls.Select(request =>
                $"silver:{request.Probability}|{request.DishInstanceId}|{request.MaterialId}"));
            values.AddRange(result.SkillTransfers.Select(request =>
                $"transfer:{request.TargetInstanceId}|{request.SourceName}|{request.SourceInstanceId}|{request.Effects.Count}"));
            values.AddRange(result.CopySkillRequests.Select(request =>
                $"copy:{request.TargetInstanceId}|{request.Count}|{request.SourceName}|{string.Join(",", request.Candidates)}|{string.Join(",", request.SelectedSkillIds)}"));
            values.AddRange(result.TemporaryCategories.Select(request =>
                $"category:{request.DishInstanceId}|{request.Category}|{request.SourceName}|{request.EffectDescription}"));
            values.AddRange(result.RecipeRemovalRequests.Select(request =>
                $"remove:{request.DishInstanceId}|{request.SourceDishIndex}|{request.DishId}|{request.DishName}|{request.Probability}"));
            return string.Join("\n", values);
        }

        private sealed class TrackingRandomStream : IRandomStream
        {
            private readonly Xoshiro256SS _inner;
            private readonly int _forcedWeightedIndex;

            public TrackingRandomStream(ulong seed, int forcedWeightedIndex)
            {
                _inner = new Xoshiro256SS(seed);
                _forcedWeightedIndex = forcedWeightedIndex;
            }

            public int WeightedPickCalls { get; private set; }

            public List<float> LastWeights { get; private set; } = new List<float>();

            public RngState State
            {
                get => _inner.State;
                set => _inner.State = value;
            }

            public uint NextUInt() => _inner.NextUInt();

            public ulong NextULong() => _inner.NextULong();

            public int Range(int minInclusive, int maxExclusive) => _inner.Range(minInclusive, maxExclusive);

            public float Range(float minInclusive, float maxExclusive) => _inner.Range(minInclusive, maxExclusive);

            public float NextFloat() => _inner.NextFloat();

            public double NextDouble() => _inner.NextDouble();

            public bool NextBool(double probability = 0.5) => _inner.NextBool(probability);

            public void Shuffle<T>(IList<T> list) => _inner.Shuffle(list);

            public T Pick<T>(IReadOnlyList<T> list) => _inner.Pick(list);

            public int WeightedPickIndex(IReadOnlyList<float> weights)
            {
                WeightedPickCalls++;
                LastWeights = new List<float>(weights);
                return Math.Max(0, Math.Min(weights.Count - 1, _forcedWeightedIndex));
            }
        }
    }
}
