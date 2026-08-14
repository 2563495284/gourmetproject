using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Balance;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
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

        private static DishDef Dish(string id, int deliciousness)
            => new DishDef(
                id,
                id,
                deliciousness,
                DishShape.FromRows(new[] { "X" }),
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty);

        private static BattleSession Session(
            DiningTable table,
            IRandomStream gameplayRandom,
            IReadOnlyList<DishDef> dishes,
            string recipeDishId,
            IReadOnlyList<MaterialDef> materials = null)
        {
            var db = new GameplayDatabase(
                dishes,
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                materials ?? Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            return new BattleSession(
                table,
                db,
                gameplayRandom,
                new[] { new RecipeSlot("slot", new[] { recipeDishId }) },
                requiredScore: 0);
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
