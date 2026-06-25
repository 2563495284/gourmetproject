using System;

namespace GourmetProject.Game.Gameplay
{
    /// <summary>行动执行上下文：记录本次选择的行动、耗时和本周行动序号。</summary>
    public sealed class ActionExecutionContext
    {
        public ActionExecutionContext(cfg.GameAction action)
            : this(action, 0)
        {
        }

        public ActionExecutionContext(cfg.GameAction action, int stepIndex)
        {
            Action = action;
            StepIndex = Math.Max(0, stepIndex);
            CostDays = action?.CostDays ?? 0;
        }

        public cfg.GameAction Action { get; }

        public int StepIndex { get; }

        public int CostDays { get; }

        public bool IsValid => Action != null;
    }
}
