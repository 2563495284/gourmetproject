using System;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>行动选择快照：UI 展示和执行必须使用同一份耗时/序号数据。</summary>
    public sealed class ActionChoice
    {
        public ActionChoice(cfg.GameAction action, cfg.ActionGroup group, int weekStepIndex, int runStepIndex, int costDays)
        {
            Action = action;
            Group = group;
            ActionGroupId = group?.Id ?? string.Empty;
            WeekStepIndex = Math.Max(0, weekStepIndex);
            RunStepIndex = Math.Max(0, runStepIndex);
            CostDays = Math.Max(0, costDays);
        }

        public cfg.GameAction Action { get; }

        public cfg.ActionGroup Group { get; }

        public string ActionGroupId { get; }

        public int WeekStepIndex { get; }

        public int RunStepIndex { get; }

        public int CostDays { get; }

        public bool IsValid => Action != null;

        public ActionExecutionContext ToExecutionContext()
        {
            return new ActionExecutionContext(Action, WeekStepIndex, RunStepIndex, ActionGroupId, CostDays);
        }
    }

    /// <summary>行动执行上下文：记录本次选择的行动、耗时、本周行动序号和整局行动序号。</summary>
    public sealed class ActionExecutionContext
    {
        public ActionExecutionContext(cfg.GameAction action)
            : this(action, 0)
        {
        }

        public ActionExecutionContext(cfg.GameAction action, int stepIndex)
            : this(action, stepIndex, stepIndex, string.Empty, action?.CostDays ?? 0)
        {
        }

        public ActionExecutionContext(cfg.GameAction action, int stepIndex, int runStepIndex, string actionGroupId, int costDays)
        {
            Action = action;
            StepIndex = Math.Max(0, stepIndex);
            RunStepIndex = Math.Max(0, runStepIndex);
            ActionGroupId = actionGroupId ?? string.Empty;
            CostDays = Math.Max(0, costDays);
        }

        public cfg.GameAction Action { get; }

        public int StepIndex { get; }

        public int RunStepIndex { get; }

        public string ActionGroupId { get; }

        public int CostDays { get; }

        public bool IsValid => Action != null;
    }
}
