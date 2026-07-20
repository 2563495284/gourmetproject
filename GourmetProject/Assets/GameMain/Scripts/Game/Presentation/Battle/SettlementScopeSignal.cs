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
        }

        public int OwnerDishInstanceId { get; }

        public int RuntimeSelfDishInstanceId { get; }

        public SkillExecutionTrace Trace { get; }

        public bool IsEmpty => OwnerDishInstanceId == 0 && RuntimeSelfDishInstanceId == 0 && Trace == null;

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
