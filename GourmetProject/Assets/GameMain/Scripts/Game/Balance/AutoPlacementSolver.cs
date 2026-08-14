using System;
using System.Collections.Generic;
using BreakInfinity;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.Balance
{
    [Serializable]
    public sealed class AutoPlacementResult
    {
        public BigDouble Score;
        public bool Truncated;
        public bool HasLegalSolution = true;
        public int SearchNodes;
        public AutoPlacementTerminationKind Termination;
        public ServePrepareOutcome LastPrepareOutcome;
        public ServeOutcome LastServeOutcome;
        public List<AutoPlacementWarning> Warnings = new List<AutoPlacementWarning>();
        public List<PlacementDecisionTrace> Decisions = new List<PlacementDecisionTrace>();
    }

    /// <summary>
    /// 在正式 <see cref="BattleSession"/> 上做有界摆盘。出菜随机只由 Session 自身随机流负责；
    /// 普通玩家的位置选择使用独立 policy 随机流，避免玩家策略扰动实机规则随机序列。
    /// </summary>
    public static class AutoPlacementSolver
    {
        private const int NormalMaximumCandidates = 24;
        private const int ExpertMaximumCandidates = 64;
        private const float NormalRankSoftmaxTemperature = 1f;

        /// <summary>
        /// 兼容旧调用方的确定性“取最佳”入口。新代码应显式传玩家等级、策略与独立 policy 随机流。
        /// </summary>
        [Obsolete("Use Solve(BattleSession, AutoPlayerLevel, AutoPlayerPolicy, IRandomStream).")]
        public static AutoPlacementResult Solve(BattleSession session, int beamWidth, int nodeBudget)
        {
            var policy = new AutoPlayerPolicy
            {
                ExpertBeamWidth = Math.Max(1, Math.Min(ExpertMaximumCandidates, beamWidth)),
                PlacementNodeBudget = nodeBudget,
            };
            return Solve(session, AutoPlayerLevel.Expert, policy, policyRandom: null);
        }

        public static AutoPlacementResult Solve(
            BattleSession session,
            AutoPlayerLevel playerLevel,
            AutoPlayerPolicy policy,
            IRandomStream policyRandom)
        {
            var result = new AutoPlacementResult();
            if (session == null)
            {
                result.HasLegalSolution = false;
                result.Termination = AutoPlacementTerminationKind.InvalidSession;
                return result;
            }

            policy ??= new AutoPlayerPolicy();
            if (playerLevel == AutoPlayerLevel.Normal && policyRandom == null)
            {
                result.HasLegalSolution = false;
                result.Termination = AutoPlacementTerminationKind.MissingPolicyRandom;
                result.Score = session.PreviewScore().Total;
                return result;
            }

            int candidateLimit = playerLevel == AutoPlayerLevel.Expert
                ? Math.Min(ExpertMaximumCandidates, policy.PlacementCandidateLimit(playerLevel))
                : Math.Min(NormalMaximumCandidates, policy.PlacementCandidateLimit(playerLevel));
            int budget = Math.Max(1, policy.PlacementNodeBudget);

            while (session.CanServeAny())
            {
                if (result.SearchNodes >= budget)
                {
                    MarkBudgetExhausted(result, session.ServesUsed + 1, 0, 0);
                    break;
                }

                ServePrepareResult prepared = session.PrepareServeAutomatically(0);
                result.LastPrepareOutcome = prepared.Outcome;
                if (!prepared.Success)
                {
                    result.HasLegalSolution = prepared.Outcome == ServePrepareOutcome.LimitReached
                        || prepared.Outcome == ServePrepareOutcome.SlotEmpty;
                    result.Termination = AutoPlacementTerminationKind.PrepareFailed;
                    break;
                }

                IReadOnlyList<Placement> legal = prepared.PreparedDish.Placements;
                if (legal == null || legal.Count == 0)
                {
                    result.HasLegalSolution = false;
                    result.Termination = AutoPlacementTerminationKind.NoLegalPlacement;
                    break;
                }

                int serveIndex = session.ServesUsed + 1;
                var rankedCandidates = new List<CandidateGeometry>(legal.Count);
                for (int i = 0; i < legal.Count; i++)
                {
                    rankedCandidates.Add(AnalyzeGeometry(session.DiningTable, legal[i], i));
                }

                rankedCandidates.Sort(CompareGeometryForPreview);
                int boundedCount = Math.Min(rankedCandidates.Count, candidateLimit);
                int remainingBudget = budget - result.SearchNodes;
                int evaluationCount = Math.Min(boundedCount, remainingBudget);
                bool candidateLimitApplied = legal.Count > candidateLimit;
                if (candidateLimitApplied)
                {
                    result.Warnings.Add(new AutoPlacementWarning
                    {
                        Kind = AutoPlacementWarningKind.CandidateLimitApplied,
                        ServeIndex = serveIndex,
                        LegalPlacementCount = legal.Count,
                        EvaluatedPlacementCount = evaluationCount,
                        Message = $"Placement candidates were bounded to {candidateLimit} of {legal.Count}.",
                    });
                }

                if (evaluationCount <= 0)
                {
                    MarkBudgetExhausted(result, serveIndex, legal.Count, 0);
                    break;
                }

                var evaluated = new List<EvaluatedPlacement>(evaluationCount);
                for (int i = 0; i < evaluationCount; i++)
                {
                    CandidateGeometry candidate = rankedCandidates[i];
                    PreparedPlacementPreview preview = session.PreviewPreparedPlacement(candidate.Placement);
                    result.SearchNodes++;
                    if (preview.Success)
                    {
                        evaluated.Add(new EvaluatedPlacement(candidate, preview.Score));
                    }
                }

                if (evaluated.Count == 0)
                {
                    result.HasLegalSolution = false;
                    result.Termination = AutoPlacementTerminationKind.NoLegalPlacement;
                    break;
                }

                evaluated.Sort(CompareEvaluatedPlacements);
                int selectedRank = playerLevel == AutoPlayerLevel.Expert
                    ? 0
                    : SelectNormalRank(evaluated.Count, policyRandom);
                EvaluatedPlacement selected = evaluated[selectedRank];
                var decision = new PlacementDecisionTrace
                {
                    ServeIndex = serveIndex,
                    DishId = prepared.PreparedDish.Definition.Id,
                    PlayerLevel = playerLevel,
                    SelectionKind = playerLevel == AutoPlayerLevel.Expert
                        ? PlacementSelectionKind.BestScore
                        : PlacementSelectionKind.RankSoftmax,
                    RankSoftmaxTemperature = playerLevel == AutoPlayerLevel.Normal
                        ? NormalRankSoftmaxTemperature
                        : 0f,
                    LegalPlacementCount = legal.Count,
                    CandidateLimit = candidateLimit,
                    EvaluatedPlacementCount = evaluated.Count,
                    SelectedCandidateIndex = selected.CandidateIndex,
                    SelectedRank = selectedRank,
                    RotationIndex = selected.Placement.RotationIndex,
                    OriginX = selected.Placement.Origin.X,
                    OriginY = selected.Placement.Origin.Y,
                    PreviewScore = selected.Score,
                    FutureOpenSpace = selected.FutureOpenSpace,
                    EmptyRegionCount = selected.EmptyRegionCount,
                    SelectionReason = playerLevel == AutoPlayerLevel.Expert
                        ? "preview-score > future-space > fragmentation > stable-coordinate"
                        : "rank-softmax(T=1.0) over preview-score/future-space/fragmentation/stable-coordinate",
                    CandidateLimitApplied = candidateLimitApplied,
                    SearchNodesAfterDecision = result.SearchNodes,
                };
                result.Decisions.Add(decision);

                ServeResult placed = session.CommitPreparedServe(selected.Placement);
                result.LastServeOutcome = placed.Outcome;
                if (!placed.Success)
                {
                    result.HasLegalSolution = false;
                    result.Termination = AutoPlacementTerminationKind.CommitFailed;
                    break;
                }

                if (evaluationCount < boundedCount)
                {
                    MarkBudgetExhausted(result, serveIndex, legal.Count, evaluationCount);
                    break;
                }
            }

            if (result.Termination == AutoPlacementTerminationKind.None)
            {
                result.Termination = AutoPlacementTerminationKind.Completed;
            }

            result.Score = session.PreviewScore().Total;
            return result;
        }

        private static int SelectNormalRank(int count, IRandomStream policyRandom)
        {
            var weights = new List<float>(count);
            for (int rank = 0; rank < count; rank++)
            {
                weights.Add((float)Math.Exp(-rank / NormalRankSoftmaxTemperature));
            }

            int selected = policyRandom.WeightedPickIndex(weights);
            return Math.Max(0, Math.Min(count - 1, selected));
        }

        private static int CompareEvaluatedPlacements(EvaluatedPlacement left, EvaluatedPlacement right)
        {
            int score = right.Score.CompareTo(left.Score);
            if (score != 0) return score;
            int futureSpace = right.FutureOpenSpace.CompareTo(left.FutureOpenSpace);
            if (futureSpace != 0) return futureSpace;
            int fragmentation = left.EmptyRegionCount.CompareTo(right.EmptyRegionCount);
            if (fragmentation != 0) return fragmentation;
            return CompareStableCoordinate(left.Placement, right.Placement, left.CandidateIndex, right.CandidateIndex);
        }

        private static CandidateGeometry AnalyzeGeometry(
            DiningTable table,
            Placement placement,
            int candidateIndex)
        {
            var occupiedByCandidate = new HashSet<GridPos>();
            foreach (GridPos local in placement.Orientation.Cells)
            {
                occupiedByCandidate.Add(local.Offset(placement.Origin.X, placement.Origin.Y));
            }

            var remaining = new HashSet<GridPos>();
            foreach (GridPos cell in table.ExistingCells())
            {
                if (table.IsEmpty(cell) && !occupiedByCandidate.Contains(cell))
                {
                    remaining.Add(cell);
                }
            }

            int regions = 0;
            int largestRegion = 0;
            var queue = new Queue<GridPos>();
            while (remaining.Count > 0)
            {
                GridPos start = default;
                foreach (GridPos cell in remaining)
                {
                    start = cell;
                    break;
                }

                regions++;
                int regionSize = 0;
                remaining.Remove(start);
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    GridPos cell = queue.Dequeue();
                    regionSize++;
                    EnqueueIfRemaining(cell.Offset(1, 0), remaining, queue);
                    EnqueueIfRemaining(cell.Offset(-1, 0), remaining, queue);
                    EnqueueIfRemaining(cell.Offset(0, 1), remaining, queue);
                    EnqueueIfRemaining(cell.Offset(0, -1), remaining, queue);
                }

                largestRegion = Math.Max(largestRegion, regionSize);
            }

            return new CandidateGeometry(
                candidateIndex,
                placement,
                largestRegion,
                regions);
        }

        private static void EnqueueIfRemaining(
            GridPos cell,
            HashSet<GridPos> remaining,
            Queue<GridPos> queue)
        {
            if (remaining.Remove(cell))
            {
                queue.Enqueue(cell);
            }
        }

        private static int CompareGeometryForPreview(CandidateGeometry left, CandidateGeometry right)
        {
            int futureSpace = right.FutureOpenSpace.CompareTo(left.FutureOpenSpace);
            if (futureSpace != 0) return futureSpace;
            int fragmentation = left.EmptyRegionCount.CompareTo(right.EmptyRegionCount);
            if (fragmentation != 0) return fragmentation;
            return CompareStableCoordinate(left.Placement, right.Placement, left.CandidateIndex, right.CandidateIndex);
        }

        private static int CompareStableCoordinate(
            Placement left,
            Placement right,
            int leftCandidateIndex,
            int rightCandidateIndex)
        {
            int y = left.Origin.Y.CompareTo(right.Origin.Y);
            if (y != 0) return y;
            int x = left.Origin.X.CompareTo(right.Origin.X);
            if (x != 0) return x;
            int rotation = left.RotationIndex.CompareTo(right.RotationIndex);
            return rotation != 0 ? rotation : leftCandidateIndex.CompareTo(rightCandidateIndex);
        }

        private static void MarkBudgetExhausted(
            AutoPlacementResult result,
            int serveIndex,
            int legalPlacementCount,
            int evaluatedPlacementCount)
        {
            result.Truncated = true;
            result.Termination = AutoPlacementTerminationKind.NodeBudgetExhausted;
            result.Warnings.Add(new AutoPlacementWarning
            {
                Kind = AutoPlacementWarningKind.NodeBudgetExhausted,
                ServeIndex = serveIndex,
                LegalPlacementCount = legalPlacementCount,
                EvaluatedPlacementCount = evaluatedPlacementCount,
                Message = "Placement node budget was exhausted before the battle could be fully placed.",
            });
        }

        private readonly struct EvaluatedPlacement
        {
            public EvaluatedPlacement(CandidateGeometry geometry, BigDouble score)
            {
                CandidateIndex = geometry.CandidateIndex;
                Placement = geometry.Placement;
                FutureOpenSpace = geometry.FutureOpenSpace;
                EmptyRegionCount = geometry.EmptyRegionCount;
                Score = score;
            }

            public int CandidateIndex { get; }

            public Placement Placement { get; }

            public int FutureOpenSpace { get; }

            public int EmptyRegionCount { get; }

            public BigDouble Score { get; }
        }

        private readonly struct CandidateGeometry
        {
            public CandidateGeometry(
                int candidateIndex,
                Placement placement,
                int futureOpenSpace,
                int emptyRegionCount)
            {
                CandidateIndex = candidateIndex;
                Placement = placement;
                FutureOpenSpace = futureOpenSpace;
                EmptyRegionCount = emptyRegionCount;
            }

            public int CandidateIndex { get; }

            public Placement Placement { get; }

            public int FutureOpenSpace { get; }

            public int EmptyRegionCount { get; }
        }
    }
}
