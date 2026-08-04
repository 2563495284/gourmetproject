using System;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>行动选择快照：UI 展示和执行必须使用同一份耗时/序号数据。</summary>
    public sealed class ActionChoice
    {
        public ActionChoice(
            cfg.GameAction action,
            string actionGroupId,
            int weekStepIndex,
            int runStepIndex,
            float costDays,
            bool halfDayBuffApplied = false,
            float timelineStopChance = 0f)
        {
            Action = action;
            ActionGroupId = actionGroupId ?? string.Empty;
            WeekStepIndex = Math.Max(0, weekStepIndex);
            RunStepIndex = Math.Max(0, runStepIndex);
            CostDays = TimelineMath.Quantize(Math.Max(0f, costDays));
            HalfDayBuffApplied = halfDayBuffApplied;
            TimelineStopChance = Math.Max(0f, Math.Min(1f, timelineStopChance));
        }

        public cfg.GameAction Action { get; }

        public string ActionGroupId { get; }

        public int WeekStepIndex { get; }

        public int RunStepIndex { get; }

        public float CostDays { get; }

        public bool HalfDayBuffApplied { get; }

        public float TimelineStopChance { get; }

        public bool IsValid => Action != null;

        public ActionExecutionContext ToExecutionContext()
        {
            return new ActionExecutionContext(Action, WeekStepIndex, RunStepIndex, ActionGroupId, CostDays)
            {
                HalfDayBuffApplied = HalfDayBuffApplied,
                TimelineStopChance = TimelineStopChance,
            };
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
        /// 来源标识：用于生成确定性的随机流 key（如 Boss 抽取、经营挑战流）。
        /// 放置来源（时间轴节点）设为节点 id；随机来源可留空，由执行侧按步数/天数拼 key。
        /// </summary>
        public string SourceKey { get; set; } = string.Empty;

        /// <summary>目标美味值曲线使用的天数覆盖值；时间轴 Boss 节点用节点所在天数，而非玩家当前游标。</summary>
        public float? TargetScoreDayOverride { get; set; }

        /// <summary>本次普通行动已应用半日券；提交时消费一层。</summary>
        public bool HalfDayBuffApplied { get; set; }

        /// <summary>额外节点执行：不推进天数/行动步数，也不改变原节点完成状态。</summary>
        public bool IsExtraTimelineExecution { get; set; }

        /// <summary>选择普通行动时快照的时间停摆概率；获得/失去装饰品和消耗品不追溯当前行动。</summary>
        public float TimelineStopChance { get; set; }

        /// <summary>自然节点本次执行轮次与总次数；Boss 和旧档默认 1/1。</summary>
        public int NodeRepeatIndex { get; set; } = 1;

        public int NodeRepeatTotal { get; set; } = 1;

        public bool TimelineStopTriggered { get; set; }

        public int TimelineStopDay { get; set; }

        /// <summary>
        /// 只有玩家从普通行动选项提交的上下文才消费天数、行动步数和半日券。
        /// 时间轴节点统一带 SourceKey；额外节点还会额外标记 IsExtraTimelineExecution。
        /// </summary>
        public bool IsDailyAction =>
            !IsExtraTimelineExecution && string.IsNullOrEmpty(SourceKey);
    }
}
