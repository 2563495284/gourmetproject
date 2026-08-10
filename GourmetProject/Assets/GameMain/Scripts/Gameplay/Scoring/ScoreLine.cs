using GourmetProject.Gameplay.Model;
using BreakInfinity;

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
        DishMultiplierAdd = 8,
        SilverItemRoll = 9,
        CopySkill = 10,
        TriggerSweetTransfer = 11,
        TriggeredSweetTransferSource = 12,
        SweetTransferBuffApplied = 13,
        SweetTransferBuffTriggered = 14,
        SweetTransferFailed = 15,
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
            BigDouble value,
            BigDouble before,
            BigDouble after,
            string message,
            SkillExecutionTrace trace = null,
            int executionGroupId = 0)
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
            Trace = trace;
            ExecutionGroupId = executionGroupId;
        }

        public ScorePhase Phase { get; }

        public ScoreLineKind Kind { get; }

        public ScoreSource Source { get; }

        public int DishInstanceId { get; }

        public string DishId { get; }

        public GridPos? Cell { get; }

        public BigDouble Value { get; }

        public BigDouble Before { get; }

        public BigDouble After { get; }

        public string Message { get; }

        public SkillExecutionTrace Trace { get; }

        /// <summary>
        /// 产生该明细的效果执行批次。0 表示基础分或没有效果上下文；
        /// 仅供解释与演出分组，不参与计分。
        /// </summary>
        public int ExecutionGroupId { get; }
    }
}
