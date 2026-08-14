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
        /// <summary>当次计入加法区、结算后写回食物实例的永久分数。</summary>
        DishPermanentFlat = 16,
        /// <summary>咸味触发的一次独立额外结算贡献。</summary>
        ExtraSettlement = 17,
        /// <summary>主动技能在本次结算中修改食物有效份数。</summary>
        CountAs = 18,
        /// <summary>兼容旧名称；与 <see cref="ScoreLineKind.CountAs"/> 表示同一种结算行。</summary>
        DishCountAs = CountAs,
        /// <summary>当次结算中每个空格提供的有效份数发生变化。</summary>
        EmptyCountAs = 19,
        /// <summary>目标食物在当前经营挑战中获得临时分类。</summary>
        TemporaryCategory = 20,
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
