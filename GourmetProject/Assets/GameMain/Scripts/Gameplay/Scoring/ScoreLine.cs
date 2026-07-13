using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>结算明细类型，供 UI 解释分数来源或单测断言排序。</summary>
    public enum ScoreLineKind
    {
        DishBase = 0,
        DishFlat = 1,
        DishMultiplier = 2,
        FinalFlat = 3,
        FinalMultiplier = 4,
        Gold = 5,
        Layer = 6,
        ExtraSettlement = 7,
        DishMultiplierAdd = 8,
        SilverItemRoll = 9,
    }

    /// <summary>一次具体分数变化的可解释记录。</summary>
    public sealed class ScoreLine
    {
        public ScoreLine(
            ScorePhase phase,
            ScoreLineKind kind,
            ScoreSource source,
            int dishInstanceId,
            string dishId,
            GridPos? cell,
            float value,
            float before,
            float after,
            string message)
        {
            Phase = phase;
            Kind = kind;
            Source = source;
            DishInstanceId = dishInstanceId;
            DishId = dishId ?? string.Empty;
            Cell = cell;
            Value = value;
            Before = before;
            After = after;
            Message = message ?? string.Empty;
        }

        public ScorePhase Phase { get; }

        public ScoreLineKind Kind { get; }

        public ScoreSource Source { get; }

        public int DishInstanceId { get; }

        public string DishId { get; }

        public GridPos? Cell { get; }

        public float Value { get; }

        public float Before { get; }

        public float After { get; }

        public string Message { get; }
    }
}
