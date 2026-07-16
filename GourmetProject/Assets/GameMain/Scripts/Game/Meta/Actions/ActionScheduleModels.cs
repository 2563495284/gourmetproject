using System;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>行动选择快照：UI 展示和执行必须使用同一份耗时/序号数据。</summary>
    public sealed class ActionChoice
    {
        public ActionChoice(cfg.GameAction action, string actionGroupId, int weekStepIndex, int runStepIndex, float costDays)
        {
            Action = action;
            ActionGroupId = actionGroupId ?? string.Empty;
            WeekStepIndex = Math.Max(0, weekStepIndex);
            RunStepIndex = Math.Max(0, runStepIndex);
            CostDays = TimelineMath.Quantize(Math.Max(0f, costDays));
        }

        public cfg.GameAction Action { get; }

        public string ActionGroupId { get; }

        public int WeekStepIndex { get; }

        public int RunStepIndex { get; }

        public float CostDays { get; }

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
            : this(action, stepIndex, stepIndex, string.Empty, action?.MinCostDays ?? 0f)
        {
        }

        public ActionExecutionContext(cfg.GameAction action, int stepIndex, int runStepIndex, string actionGroupId, float costDays)
        {
            Action = action;
            StepIndex = Math.Max(0, stepIndex);
            RunStepIndex = Math.Max(0, runStepIndex);
            ActionGroupId = actionGroupId ?? string.Empty;
            CostDays = TimelineMath.Quantize(Math.Max(0f, costDays));
        }

        public cfg.GameAction Action { get; }

        public int StepIndex { get; }

        public int RunStepIndex { get; }

        public string ActionGroupId { get; }

        public float CostDays { get; }

        public bool IsValid => Action != null;

        /// <summary>
        /// 来源标识：用于生成确定性的随机流 key（如 Boss 抽取、战斗流）。
        /// 放置来源（行动轴节点）设为节点 id；随机来源可留空，由执行侧按步数/天数拼 key。
        /// </summary>
        public string SourceKey { get; set; } = string.Empty;

        /// <summary>目标分曲线使用的天数覆盖值；行动轴 Boss 节点用节点所在天数，而非玩家当前游标。</summary>
        public float? TargetScoreDayOverride { get; set; }
    }
}
