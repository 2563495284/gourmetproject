using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Scoring;

namespace GourmetProject.Game.Presentation.Battle
{
    public readonly struct SettlementScopeSignal
    {
        public SettlementScopeSignal(int ownerDishInstanceId, int runtimeSelfDishInstanceId, SkillExecutionTrace trace)
        {
            OwnerDishInstanceId = ownerDishInstanceId;
            RuntimeSelfDishInstanceId = runtimeSelfDishInstanceId;
            Trace = trace;
            Traces = trace != null
                ? new[] { trace }
                : Array.Empty<SkillExecutionTrace>();
        }

        public int OwnerDishInstanceId { get; }

        public int RuntimeSelfDishInstanceId { get; }

        public SkillExecutionTrace Trace { get; }

        public IReadOnlyList<SkillExecutionTrace> Traces { get; }

        public bool IsEmpty => OwnerDishInstanceId == 0
            && RuntimeSelfDishInstanceId == 0
            && (Traces == null || Traces.Count == 0);

        public static SettlementScopeSignal FromTraces(
            int ownerDishInstanceId,
            int runtimeSelfDishInstanceId,
            IReadOnlyList<SkillExecutionTrace> traces)
        {
            IReadOnlyList<SkillExecutionTrace> safeTraces = traces ?? Array.Empty<SkillExecutionTrace>();
            SkillExecutionTrace first = safeTraces.Count > 0 ? safeTraces[0] : null;
            return new SettlementScopeSignal(
                ownerDishInstanceId,
                runtimeSelfDishInstanceId,
                first,
                safeTraces);
        }

        private SettlementScopeSignal(
            int ownerDishInstanceId,
            int runtimeSelfDishInstanceId,
            SkillExecutionTrace trace,
            IReadOnlyList<SkillExecutionTrace> traces)
        {
            OwnerDishInstanceId = ownerDishInstanceId;
            RuntimeSelfDishInstanceId = runtimeSelfDishInstanceId;
            Trace = trace;
            Traces = traces ?? Array.Empty<SkillExecutionTrace>();
        }

        public static SettlementScopeSignal FromScoreLine(ScoreLine line)
        {
            if (line == null)
            {
                return default;
            }

            SkillExecutionTrace trace = line.Trace;
            int ownerId = trace != null
                ? trace.OwnerDishInstanceId
                : line.Source != null && line.Source.DishInstanceId > 0
                    ? line.Source.DishInstanceId
                    : line.DishInstanceId;
            int runtimeSelfId = trace != null ? trace.RuntimeSelfDishInstanceId : line.DishInstanceId;
            return new SettlementScopeSignal(ownerId, runtimeSelfId, trace);
        }
    }
}
