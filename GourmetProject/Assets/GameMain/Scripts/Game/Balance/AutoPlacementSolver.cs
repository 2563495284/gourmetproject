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

                ServePrepareResult prepared = session.PreparedServe != null
                    ? new ServePrepareResult(ServePrepareOutcome.Prepared, session.PreparedServe)
                    : session.PrepareServeAutomatically(0);
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
                    if (session.TryDiscardPreparedServe())
                    {
                        continue;
                    }

                    result.HasLegalSolution = false;
                    result.Termination = AutoPlacementTerminationKind.NoLegalPlacement;
                    break;
                }

                // 真人会把有限的丢弃次数留给明显弱于剩余食谱的出菜。旧自动玩家只在
                // “放下后总分倒退”时才丢弃，实际几乎永远不会触发，导致 4x4 开局胃里
                // 常被低基础分箭头饼干占满。这里只读取已经公开的食谱快照，不窥视也不
                // 推进正式随机流；Normal 的犹豫来自独立 policy RNG。
                if (ShouldDiscardWeakPreparedDish(
                        session,
                        prepared.PreparedDish,
                        playerLevel,
                        policyRandom)
                    && session.TryDiscardPreparedServe())
                {
                    continue;
                }

                int serveIndex = session.ServesUsed + 1;
                var rankedCandidates = new List<CandidateGeometry>(legal.Count);
                for (int i = 0; i < legal.Count; i++)
                {
                    rankedCandidates.Add(AnalyzeGeometry(
                        session,
                        prepared.PreparedDish.Dish,
                        legal[i],
                        i));
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
                    PreparedPlacementPreview preview = session.PreviewPreparedPlacementForSolver(candidate.Placement);
                    // SearchNodes 是硬预算单位：一节点严格等于一次完整 ScoreCalculator。
                    // BattleSession 会显式报告实际次数，避免预览策略调整后预算悄悄失真。
                    result.SearchNodes += preview.ScoreCalculationCount;
                    if (preview.Success)
                    {
                        evaluated.Add(new EvaluatedPlacement(candidate, preview.Score));
                    }
                }

                if (evaluated.Count == 0)
                {
                    if (session.TryDiscardPreparedServe())
                    {
                        continue;
                    }

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
                    PlanningScore = selected.PlanningScore,
                    DirectionalFuturePotential = selected.DirectionalFuturePotential,
                    FutureOpenSpace = selected.FutureOpenSpace,
                    EmptyRegionCount = selected.EmptyRegionCount,
                    SelectionReason = playerLevel == AutoPlayerLevel.Expert
                        ? "expected-preview-score + directional-future-value > preview-score > future-space > fragmentation > stable-coordinate"
                        : "rank-softmax(T=1.0) over expected-preview-score + directional-future-value/future-space/fragmentation/stable-coordinate",
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

        private static bool ShouldDiscardWeakPreparedDish(
            BattleSession session,
            PreparedServeDish prepared,
            AutoPlayerLevel playerLevel,
            IRandomStream policyRandom)
        {
            if (session == null
                || prepared?.Dish?.Def == null
                || session.FoodDiscardsRemaining <= 0
                || prepared.IsBossInsertedDish)
            {
                return false;
            }

            double preparedValue = PreparedEntryValue(prepared.Dish.Def, prepared.Entry);
            double bestRemainingValue = preparedValue;
            for (int slotIndex = 0; slotIndex < session.Slots.Count; slotIndex++)
            {
                RecipeSlot slot = session.Slots[slotIndex];
                for (int entryIndex = 0; entryIndex < slot.Entries.Count; entryIndex++)
                {
                    if (!session.CanFitRecipeEntry(slotIndex, entryIndex))
                    {
                        continue;
                    }

                    RecipeSlotEntry entry = slot.Entries[entryIndex];
                    DishDef dish = session.Database.GetDish(entry.DishId);
                    bestRemainingValue = Math.Max(bestRemainingValue, PreparedEntryValue(dish, entry));
                }
            }

            // 至少高一档（开局通常是 10 -> 20/30）才值得花掉有限丢弃次数。
            bool clearlyOutclassed = bestRemainingValue >= preparedValue + 9.5d
                && bestRemainingValue >= preparedValue * 1.45d;
            if (!clearlyOutclassed)
            {
                return false;
            }

            if (playerLevel == AutoPlayerLevel.Expert)
            {
                return true;
            }

            return policyRandom != null && policyRandom.NextBool(0.65d);
        }

        private static double PreparedEntryValue(DishDef dish, RecipeSlotEntry entry)
        {
            if (dish == null)
            {
                return 0d;
            }

            double flat = entry != null ? entry.ScoreFlatBonus.ToDouble() : 0d;
            double multiplier = entry != null ? entry.ScoreMultiplier.ToDouble() : 1d;
            if (double.IsNaN(flat) || double.IsInfinity(flat)) flat = 0d;
            if (double.IsNaN(multiplier) || double.IsInfinity(multiplier) || multiplier <= 0d) multiplier = 1d;
            return Math.Max(0d, dish.Deliciousness + flat) * multiplier;
        }

        private static int CompareEvaluatedPlacements(EvaluatedPlacement left, EvaluatedPlacement right)
        {
            int planning = right.PlanningScore.CompareTo(left.PlanningScore);
            if (planning != 0) return planning;
            int score = right.Score.CompareTo(left.Score);
            if (score != 0) return score;
            int directional = right.DirectionalFuturePotential.CompareTo(left.DirectionalFuturePotential);
            if (directional != 0) return directional;
            int futureSpace = right.FutureOpenSpace.CompareTo(left.FutureOpenSpace);
            if (futureSpace != 0) return futureSpace;
            int fragmentation = left.EmptyRegionCount.CompareTo(right.EmptyRegionCount);
            if (fragmentation != 0) return fragmentation;
            return CompareStableCoordinate(left.Placement, right.Placement, left.CandidateIndex, right.CandidateIndex);
        }

        private static CandidateGeometry AnalyzeGeometry(
            BattleSession session,
            DishInstance dish,
            Placement placement,
            int candidateIndex)
        {
            DiningTable table = session.DiningTable;
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
                regions,
                DirectionalFuturePotential(session, dish, occupiedByCandidate));
        }

        private static double DirectionalFuturePotential(
            BattleSession session,
            DishInstance dish,
            HashSet<GridPos> occupiedByCandidate)
        {
            if (session?.Database == null || dish?.SkillIds == null || dish.SkillIds.Count == 0)
            {
                return 0;
            }

            int remainingDishCount = 0;
            int remainingDishCells = 0;
            foreach (RecipeSlot slot in session.Slots)
            {
                foreach (RecipeSlotEntry entry in slot.Entries)
                {
                    DishDef futureDish = session.Database.GetDish(entry.DishId);
                    if (futureDish == null)
                    {
                        continue;
                    }

                    remainingDishCount++;
                    remainingDishCells += Math.Max(1, futureDish.Shape.CellCount);
                }
            }

            // 作用域中的空格不是未来目标数：技能对一道多格菜只生效一次。
            // 没有剩余食谱时更不存在“未来价值”，不能让末菜为虚构目标牺牲当前分。
            if (remainingDishCount == 0)
            {
                return 0d;
            }

            double averageFutureDishCells = Math.Max(
                1d,
                remainingDishCells / (double)remainingDishCount);

            double potential = 0d;
            foreach (string skillId in dish.SkillIds)
            {
                SkillDef skill = session.Database.GetSkill(skillId);
                if (skill?.Rules == null)
                {
                    continue;
                }

                foreach (SkillRuleDef rule in skill.Rules)
                {
                    double ruleWeight = FutureBenefitWeight(rule);
                    if (ruleWeight <= 0d)
                    {
                        continue;
                    }

                    int futureTargetCells = 0;
                    foreach (GridPos cell in session.DiningTable.ExistingCells())
                    {
                        if (!session.DiningTable.IsEmpty(cell)
                            || occupiedByCandidate.Contains(cell))
                        {
                            continue;
                        }

                        if (IsFutureCellInScope(
                            cell,
                            occupiedByCandidate,
                            rule.ActionScope))
                        {
                            futureTargetCells++;
                        }
                    }

                    double estimatedFutureTargets = Math.Min(
                        remainingDishCount,
                        futureTargetCells / averageFutureDishCells);
                    potential += estimatedFutureTargets * ruleWeight;
                }
            }

            return potential;
        }

        private static double FutureBenefitWeight(SkillRuleDef rule)
        {
            if (rule == null)
            {
                return 0d;
            }

            switch (rule.ActionType)
            {
                case SkillActionType.AddFlat:
                    return Math.Max(0d, rule.ActionValue);
                case SkillActionType.AddMultFlat:
                    // 倍率加值需要换算成大致的单格分值，否则 +0.5 会被 +10 固定分
                    // 低估二十倍，箭头饼干会系统性摆错方向。20 是当前开局普通菜的
                    // 基础分，仅用于候选排序，不进入正式结算。
                    return Math.Max(0d, rule.ActionValue) * 20d;
                case SkillActionType.AddMult:
                    return HasActionParam(rule, "linear")
                        ? Math.Max(0d, rule.ActionValue) * 20d
                        : Math.Max(0d, rule.ActionValue - 1f) * 20d;
                case SkillActionType.TransferSkills:
                case SkillActionType.TriggerSweetTransfer:
                    return 20d;
                default:
                    return 0d;
            }
        }

        private static bool HasActionParam(SkillRuleDef rule, string token)
        {
            foreach (string value in rule.ActionParams)
            {
                if (!string.IsNullOrEmpty(value)
                    && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsFutureCellInScope(
            GridPos cell,
            HashSet<GridPos> occupied,
            SkillScope scope)
        {
            bool sameRow = false;
            bool sameColumn = false;
            bool adjacent = false;
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            int minY = int.MaxValue;
            int maxY = int.MinValue;
            foreach (GridPos source in occupied)
            {
                sameRow |= source.Y == cell.Y;
                sameColumn |= source.X == cell.X;
                adjacent |= Math.Abs(source.X - cell.X) + Math.Abs(source.Y - cell.Y) == 1;
                minX = Math.Min(minX, source.X);
                maxX = Math.Max(maxX, source.X);
                minY = Math.Min(minY, source.Y);
                maxY = Math.Max(maxY, source.Y);
            }

            switch (scope)
            {
                case SkillScope.Row:
                case SkillScope.RowAndSelf:
                    return sameRow;
                case SkillScope.Column:
                case SkillScope.ColumnAndSelf:
                    return sameColumn;
                case SkillScope.RowAndColumn:
                    return sameRow || sameColumn;
                case SkillScope.Adjacent:
                case SkillScope.Round:
                case SkillScope.RoundAndSelf:
                    return adjacent;
                case SkillScope.Left:
                case SkillScope.LeftAndSelf:
                    return sameRow && cell.X < minX;
                case SkillScope.Right:
                case SkillScope.RightAndSelf:
                    return sameRow && cell.X > maxX;
                case SkillScope.Up:
                case SkillScope.UpAndSelf:
                    return sameColumn && cell.Y < minY;
                case SkillScope.Down:
                case SkillScope.DownAndSelf:
                    return sameColumn && cell.Y > maxY;
                case SkillScope.Self:
                case SkillScope.CakeBuff:
                    return false;
                default:
                    return true;
            }
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
            int directional = right.DirectionalFuturePotential.CompareTo(left.DirectionalFuturePotential);
            if (directional != 0) return directional;
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
                DirectionalFuturePotential = geometry.DirectionalFuturePotential;
                Score = score;
                PlanningScore = score + Math.Max(0d, geometry.DirectionalFuturePotential);
            }

            public int CandidateIndex { get; }

            public Placement Placement { get; }

            public int FutureOpenSpace { get; }

            public int EmptyRegionCount { get; }

            public double DirectionalFuturePotential { get; }

            public BigDouble Score { get; }

            public BigDouble PlanningScore { get; }
        }

        private readonly struct CandidateGeometry
        {
            public CandidateGeometry(
                int candidateIndex,
                Placement placement,
                int futureOpenSpace,
                int emptyRegionCount,
                double directionalFuturePotential)
            {
                CandidateIndex = candidateIndex;
                Placement = placement;
                FutureOpenSpace = futureOpenSpace;
                EmptyRegionCount = emptyRegionCount;
                DirectionalFuturePotential = directionalFuturePotential;
            }

            public int CandidateIndex { get; }

            public Placement Placement { get; }

            public int FutureOpenSpace { get; }

            public int EmptyRegionCount { get; }

            public double DirectionalFuturePotential { get; }
        }
    }
}
