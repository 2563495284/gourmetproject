using System;
using System.Collections.Generic;
using BreakInfinity;
using GourmetProject.Gameplay.Scoring;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 结算属性色板。来源聚光表达“谁触发”，本色板只表达“发生了哪类属性变化”。
    /// </summary>
    internal static class SettlementAttributePalette
    {
        public static readonly Color BaseScore = new Color32(246, 196, 83, 255);
        public static readonly Color AddMultiplier = new Color32(57, 208, 176, 255);
        public static readonly Color MultiplyMultiplier = new Color32(255, 90, 95, 255);
        public static readonly Color Special = new Color32(169, 120, 255, 255);

        public static Color For(ScoreLineKind kind)
        {
            return kind switch
            {
                ScoreLineKind.DishBase or ScoreLineKind.DishFlat or ScoreLineKind.FinalFlat => BaseScore,
                ScoreLineKind.DishMultiplierAdd => AddMultiplier,
                ScoreLineKind.DishMultiplier or ScoreLineKind.FinalMultiplier => MultiplyMultiplier,
                _ => Special,
            };
        }

        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }
    }

    internal enum SettlementSweetTransferTransition
    {
        None = 0,
        Begin = 1,
        Keep = 2,
        Switch = 3,
        Clear = 4,
    }

    /// <summary>纯逻辑判定甜蜜传递持续来源应如何切换，避免表现状态跨效果组泄漏。</summary>
    internal static class SettlementSweetTransferTransitionResolver
    {
        public static SettlementSweetTransferTransition Resolve(int currentSourceDishId, int nextSourceDishId)
        {
            if (currentSourceDishId <= 0)
            {
                return nextSourceDishId > 0
                    ? SettlementSweetTransferTransition.Begin
                    : SettlementSweetTransferTransition.None;
            }

            if (nextSourceDishId <= 0)
            {
                return SettlementSweetTransferTransition.Clear;
            }

            return currentSourceDishId == nextSourceDishId
                ? SettlementSweetTransferTransition.Keep
                : SettlementSweetTransferTransition.Switch;
        }
    }

    internal readonly struct SettlementSweetTransferPresentationKey : IEquatable<SettlementSweetTransferPresentationKey>
    {
        public SettlementSweetTransferPresentationKey(
            int sourceDishInstanceId,
            int executorDishInstanceId,
            string skillId)
        {
            SourceDishInstanceId = sourceDishInstanceId;
            ExecutorDishInstanceId = executorDishInstanceId;
            SkillId = skillId ?? string.Empty;
        }

        public int SourceDishInstanceId { get; }
        public int ExecutorDishInstanceId { get; }
        public string SkillId { get; }
        public bool IsEmpty => SourceDishInstanceId <= 0 || ExecutorDishInstanceId <= 0;

        public bool Equals(SettlementSweetTransferPresentationKey other)
        {
            return SourceDishInstanceId == other.SourceDishInstanceId
                && ExecutorDishInstanceId == other.ExecutorDishInstanceId
                && string.Equals(SkillId, other.SkillId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is SettlementSweetTransferPresentationKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = SourceDishInstanceId;
                hash = (hash * 397) ^ ExecutorDishInstanceId;
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(SkillId ?? string.Empty);
                return hash;
            }
        }
    }

    internal readonly struct SettlementSweetTransferPresentationContext
    {
        private SettlementSweetTransferPresentationContext(
            int sourceDishInstanceId,
            int executorDishInstanceId,
            string sourceName,
            string executorName,
            string skillId,
            string skillName,
            int executionGroupId)
        {
            SourceDishInstanceId = sourceDishInstanceId;
            ExecutorDishInstanceId = executorDishInstanceId;
            SourceName = sourceName ?? string.Empty;
            ExecutorName = executorName ?? string.Empty;
            SkillId = skillId ?? string.Empty;
            SkillName = skillName ?? SkillId;
            ExecutionGroupId = executionGroupId;
        }

        public int SourceDishInstanceId { get; }
        public int ExecutorDishInstanceId { get; }
        public string SourceName { get; }
        public string ExecutorName { get; }
        public string SkillId { get; }
        public string SkillName { get; }
        public int ExecutionGroupId { get; }
        public bool IsValid => SourceDishInstanceId > 0 && ExecutorDishInstanceId > 0;
        public bool IsSelfTransfer => IsValid && SourceDishInstanceId == ExecutorDishInstanceId;
        public SettlementSweetTransferPresentationKey Key => new(
            SourceDishInstanceId,
            ExecutorDishInstanceId,
            SkillId);

        public bool RequiresHandoffAfter(SettlementSweetTransferPresentationKey previous)
        {
            return IsValid && !Key.Equals(previous);
        }

        public static SettlementSweetTransferPresentationContext FromGroup(SettlementEffectGroup group)
        {
            return group != null ? FromTrace(group.Trace, group.GroupId) : default;
        }

        public static SettlementSweetTransferPresentationContext FromLine(ScoreLine line)
        {
            return line != null ? FromTrace(line.Trace, line.ExecutionGroupId) : default;
        }

        private static SettlementSweetTransferPresentationContext FromTrace(
            SkillExecutionTrace trace,
            int executionGroupId)
        {
            if (trace == null || trace.Kind != SkillExecutionKind.SweetTransfer)
            {
                return default;
            }

            return new SettlementSweetTransferPresentationContext(
                trace.OwnerDishInstanceId,
                trace.RuntimeSelfDishInstanceId,
                trace.OwnerDishName,
                trace.RuntimeSelfDishName,
                trace.SkillId,
                trace.SkillName,
                executionGroupId);
        }
    }

    public enum SettlementBeatKind
    {
        SourceStarted = 0,
        ScopeShown = 1,
        ResultApplied = 2,
        GroupCompleted = 3,
        FinaleConfirmed = 4,
    }

    /// <summary>稳定的结算节拍信号；当前只预留给音效，后续可用 Speed 映射 pitch。</summary>
    public readonly struct SettlementBeatSignal
    {
        public SettlementBeatSignal(
            SettlementBeatKind kind,
            string sourceName,
            int dishInstanceId,
            float speed,
            float normalizedProgress)
        {
            Kind = kind;
            SourceName = sourceName ?? string.Empty;
            DishInstanceId = dishInstanceId;
            Speed = speed;
            NormalizedProgress = normalizedProgress;
        }

        public SettlementBeatKind Kind { get; }
        public string SourceName { get; }
        public int DishInstanceId { get; }
        public float Speed { get; }
        public float NormalizedProgress { get; }
    }

    internal sealed class SettlementPresentationPlan
    {
        public List<SettlementBaseBeat> BaseBeats { get; } = new();
        public List<SettlementEffectGroup> Groups { get; } = new();

        public int ResultBeatCount
        {
            get
            {
                int count = BaseBeats.Count + 1;
                for (int i = 0; i < Groups.Count; i++)
                {
                    count += Groups[i].Lines.Count;
                }

                return Mathf.Max(1, count);
            }
        }

        public static SettlementPresentationPlan Build(ScoreResult result)
        {
            var plan = new SettlementPresentationPlan();
            if (result == null)
            {
                return plan;
            }

            var baseLines = new Dictionary<int, ScoreLine>();
            var remainingBaseLines = new List<ScoreLine>();
            for (int i = 0; i < result.ScoreLines.Count; i++)
            {
                ScoreLine line = result.ScoreLines[i];
                if (line == null || line.Kind != ScoreLineKind.DishBase)
                {
                    continue;
                }

                if (!baseLines.ContainsKey(line.DishInstanceId))
                {
                    baseLines[line.DishInstanceId] = line;
                }
                else
                {
                    remainingBaseLines.Add(line);
                }
            }

            var addedBaseIds = new HashSet<int>();
            for (int i = 0; i < result.DishScores.Count; i++)
            {
                DishScore score = result.DishScores[i];
                if (score == null || !addedBaseIds.Add(score.DishInstanceId))
                {
                    continue;
                }

                baseLines.TryGetValue(score.DishInstanceId, out ScoreLine line);
                plan.BaseBeats.Add(new SettlementBaseBeat(
                    score.DishInstanceId,
                    score.DishId,
                    line,
                    line != null ? line.After : score.BaseValue));
            }

            foreach (KeyValuePair<int, ScoreLine> entry in baseLines)
            {
                if (addedBaseIds.Add(entry.Key))
                {
                    plan.BaseBeats.Add(new SettlementBaseBeat(
                        entry.Key,
                        entry.Value.DishId,
                        entry.Value,
                        entry.Value.After));
                }
            }

            for (int i = 0; i < remainingBaseLines.Count; i++)
            {
                ScoreLine line = remainingBaseLines[i];
                plan.BaseBeats.Add(new SettlementBaseBeat(
                    line.DishInstanceId,
                    line.DishId,
                    line,
                    line.After));
            }

            for (int i = 0; i < result.ScoreLines.Count; i++)
            {
                ScoreLine line = result.ScoreLines[i];
                if (line == null || line.Kind == ScoreLineKind.DishBase)
                {
                    continue;
                }

                SettlementEffectGroup current = plan.Groups.Count > 0
                    ? plan.Groups[plan.Groups.Count - 1]
                    : null;
                if (current == null || !current.CanAppend(line))
                {
                    current = new SettlementEffectGroup(line);
                    plan.Groups.Add(current);
                }
                else
                {
                    current.Append(line);
                }
            }

            AddAggregateFallbacks(plan, result);
            return plan;
        }

        private static void AddAggregateFallbacks(SettlementPresentationPlan plan, ScoreResult result)
        {
            bool hasFinalFlat = false;
            bool hasFinalMultiplier = false;
            bool hasGold = false;
            bool hasLayer = false;
            bool hasSilverRoll = false;
            for (int i = 0; i < result.ScoreLines.Count; i++)
            {
                ScoreLine line = result.ScoreLines[i];
                if (line == null)
                {
                    continue;
                }

                hasFinalFlat |= line.Kind == ScoreLineKind.FinalFlat;
                hasFinalMultiplier |= line.Kind == ScoreLineKind.FinalMultiplier;
                hasGold |= line.Kind == ScoreLineKind.Gold;
                hasLayer |= line.Kind == ScoreLineKind.Layer;
                hasSilverRoll |= line.Kind == ScoreLineKind.SilverItemRoll;
            }

            var source = ScoreSource.FinalModifier("settlement_fallback", "结算");
            if (!hasFinalFlat && BigDouble.Abs(result.FinalFlat) > 0.001f)
            {
                AppendSynthetic(plan, new ScoreLine(
                    ScorePhase.Final,
                    ScoreLineKind.FinalFlat,
                    source,
                    0,
                    string.Empty,
                    null,
                    result.FinalFlat,
                    0f,
                    result.FinalFlat,
                    $"局级加法 {FormatSigned(result.FinalFlat)}"));
            }

            if (!hasFinalMultiplier && BigDouble.Abs(result.FinalMultiplier - 1f) > 0.001f)
            {
                AppendSynthetic(plan, new ScoreLine(
                    ScorePhase.Final,
                    ScoreLineKind.FinalMultiplier,
                    source,
                    0,
                    string.Empty,
                    null,
                    result.FinalMultiplier,
                    1f,
                    result.FinalMultiplier,
                    $"局级倍率 ×{FormatDecimal(result.FinalMultiplier)}"));
            }

            if (!hasGold && Mathf.Abs(result.GoldDelta) > 0.001f)
            {
                AppendSynthetic(plan, SyntheticSideEffect(
                    ScoreLineKind.Gold,
                    result.GoldDelta,
                    $"金币 {FormatSigned(result.GoldDelta)}"));
            }

            if (!hasLayer && result.HappyCakeLayerDelta != 0)
            {
                AppendSynthetic(plan, SyntheticSideEffect(
                    ScoreLineKind.Layer,
                    result.HappyCakeLayerDelta,
                    $"层数 {FormatSigned(result.HappyCakeLayerDelta)}"));
            }

            if (!hasSilverRoll && result.SilverItemRollRequests > 0)
            {
                AppendSynthetic(plan, SyntheticSideEffect(
                    ScoreLineKind.SilverItemRoll,
                    result.SilverItemRollRequests,
                    $"获得装饰品和消耗品 ×{result.SilverItemRollRequests}"));
            }
        }

        private static ScoreLine SyntheticSideEffect(ScoreLineKind kind, float value, string message)
        {
            return new ScoreLine(
                ScorePhase.Final,
                kind,
                ScoreSource.FinalModifier("settlement_fallback", "结算"),
                0,
                string.Empty,
                null,
                value,
                0f,
                value,
                message);
        }

        private static void AppendSynthetic(SettlementPresentationPlan plan, ScoreLine line)
        {
            plan.Groups.Add(new SettlementEffectGroup(line));
        }

        private static string FormatSigned(BigDouble value)
        {
            return $"{(value >= 0f ? "+" : string.Empty)}{ScoreNumberFormatter.Format(value)}";
        }

        private static string FormatDecimal(BigDouble value)
        {
            return BigDouble.Abs(value) < ScoreNumberFormatter.ScientificThreshold
                ? value.ToString("G3")
                : ScoreNumberFormatter.Format(value);
        }
    }

    internal sealed class SettlementBaseBeat
    {
        public SettlementBaseBeat(int dishInstanceId, string dishId, ScoreLine line, BigDouble baseValue)
        {
            DishInstanceId = dishInstanceId;
            DishId = dishId ?? string.Empty;
            Line = line;
            BaseValue = baseValue;
        }

        public int DishInstanceId { get; }
        public string DishId { get; }
        public ScoreLine Line { get; }
        public BigDouble BaseValue { get; }
    }

    internal sealed class SettlementEffectGroup
    {
        private readonly HashSet<int> _targetDishIds = new();

        public SettlementEffectGroup(ScoreLine first)
        {
            GroupId = first?.ExecutionGroupId ?? 0;
            Phase = first?.Phase ?? ScorePhase.Final;
            Source = first?.Source;
            Trace = first?.Trace;
            Append(first);
        }

        public int GroupId { get; }
        public ScorePhase Phase { get; }
        public ScoreSource Source { get; }
        public SkillExecutionTrace Trace { get; }
        public List<ScoreLine> Lines { get; } = new();
        public IReadOnlyCollection<int> TargetDishIds => _targetDishIds;

        public int ActorDishInstanceId
        {
            get
            {
                if (Trace?.Kind == SkillExecutionKind.SweetTransfer
                    && Trace.RuntimeSelfDishInstanceId > 0)
                {
                    return Trace.RuntimeSelfDishInstanceId;
                }

                if (Trace != null && Trace.OwnerDishInstanceId > 0)
                {
                    return Trace.OwnerDishInstanceId;
                }

                return Source?.DishInstanceId ?? 0;
            }
        }

        public string SourceName
        {
            get
            {
                if (Trace != null && !string.IsNullOrEmpty(Trace.SourceLabel))
                {
                    return Trace.SourceLabel;
                }

                if (!string.IsNullOrEmpty(Source?.Name))
                {
                    return Source.Name;
                }

                return "结算";
            }
        }

        public bool CanAppend(ScoreLine line)
        {
            if (line == null)
            {
                return false;
            }

            if (GroupId > 0 || line.ExecutionGroupId > 0)
            {
                return GroupId > 0 && GroupId == line.ExecutionGroupId;
            }

            return Phase == line.Phase
                && SameSource(Source, line.Source);
        }

        public void Append(ScoreLine line)
        {
            if (line == null)
            {
                return;
            }

            Lines.Add(line);
            if (line.DishInstanceId > 0)
            {
                _targetDishIds.Add(line.DishInstanceId);
            }

            if (line.Trace?.VisualTargetDishInstanceIds != null)
            {
                for (int i = 0; i < line.Trace.VisualTargetDishInstanceIds.Count; i++)
                {
                    int id = line.Trace.VisualTargetDishInstanceIds[i];
                    if (id > 0)
                    {
                        _targetDishIds.Add(id);
                    }
                }
            }
        }

        private static bool SameSource(ScoreSource a, ScoreSource b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            return a != null
                && b != null
                && a.Type == b.Type
                && a.DishInstanceId == b.DishInstanceId
                && string.Equals(a.Id, b.Id, StringComparison.Ordinal);
        }
    }

    internal sealed class SettlementRunningLedger
    {
        private sealed class DishState
        {
            public BigDouble Base;
            public BigDouble Flat;
            public BigDouble Multiplier = BigDouble.One;
            public bool HasBase;
        }

        private readonly Dictionary<int, DishState> _dishes = new();
        private BigDouble _finalFlat;
        private BigDouble _finalMultiplier = BigDouble.One;

        public SettlementRunningLedger(
            IReadOnlyList<DishScore> scores,
            SettlementBaselineSnapshot baselineSnapshot)
        {
            if (scores == null)
            {
                return;
            }

            for (int i = 0; i < scores.Count; i++)
            {
                DishScore score = scores[i];
                if (score == null)
                {
                    continue;
                }

                BigDouble multiplier = BigDouble.One;
                if (baselineSnapshot != null
                    && baselineSnapshot.TryGet(score.DishInstanceId, out SettlementDishBaseline baseline))
                {
                    multiplier = baseline.Multiplier;
                }

                _dishes[score.DishInstanceId] = new DishState { Multiplier = multiplier };
            }
        }

        public BigDouble CurrentTotal
        {
            get
            {
                BigDouble raw = BigDouble.Zero;
                foreach (DishState state in _dishes.Values)
                {
                    if (state.HasBase)
                    {
                        raw += DishScore.CeilContribution(state.Base + state.Flat, state.Multiplier);
                    }
                }

                return BigDouble.Round(
                    (raw + _finalFlat) * _finalMultiplier,
                    MidpointRounding.AwayFromZero);
            }
        }

        public BigDouble ApplyBase(int dishInstanceId, BigDouble baseValue)
        {
            DishState state = EnsureDish(dishInstanceId);
            state.Base = baseValue;
            state.HasBase = true;
            return ContributionFor(dishInstanceId);
        }

        public BigDouble Apply(ScoreLine line)
        {
            if (line == null)
            {
                return BigDouble.Zero;
            }

            DishState state;
            switch (line.Kind)
            {
                case ScoreLineKind.DishBase:
                    return ApplyBase(line.DishInstanceId, line.After);
                case ScoreLineKind.DishFlat:
                    state = EnsureDish(line.DishInstanceId);
                    state.Flat = line.After;
                    break;
                case ScoreLineKind.DishMultiplier:
                case ScoreLineKind.DishMultiplierAdd:
                    state = EnsureDish(line.DishInstanceId);
                    state.Multiplier = line.After;
                    break;
                case ScoreLineKind.FinalFlat:
                    _finalFlat = line.After;
                    break;
                case ScoreLineKind.FinalMultiplier:
                    _finalMultiplier = line.After;
                    break;
            }

            return ContributionFor(line.DishInstanceId);
        }

        public BigDouble ContributionFor(int dishInstanceId)
        {
            if (!_dishes.TryGetValue(dishInstanceId, out DishState state) || !state.HasBase)
            {
                return BigDouble.Zero;
            }

            return DishScore.CeilContribution(state.Base + state.Flat, state.Multiplier);
        }

        private DishState EnsureDish(int dishInstanceId)
        {
            if (!_dishes.TryGetValue(dishInstanceId, out DishState state))
            {
                state = new DishState();
                _dishes[dishInstanceId] = state;
            }

            return state;
        }
    }
}
