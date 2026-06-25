using System;
using System.Collections.Generic;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Gameplay
{
    /// <summary>行动组序列中的一个可选行动。保存 actionId 与本次展开后的耗时，避免读档后重新随机。</summary>
    [Serializable]
    public sealed class ScheduledActionChoice
    {
        public ScheduledActionChoice()
        {
        }

        public ScheduledActionChoice(string groupId, string actionId, int costDays, int stepIndex)
        {
            GroupId = groupId ?? string.Empty;
            ActionId = actionId ?? string.Empty;
            CostDays = Math.Max(0, costDays);
            StepIndex = Math.Max(0, stepIndex);
        }

        public string GroupId;
        public string ActionId;
        public int CostDays;
        public int StepIndex;

        public cfg.GameAction Action => string.IsNullOrEmpty(ActionId)
            ? null
            : GameApp.Config.Tables.TbAction.GetOrDefault(ActionId);
    }

    /// <summary>预生成行动序列中的一步，包含一个行动组展开出的多个行动选项。</summary>
    [Serializable]
    public sealed class ActionScheduleStep
    {
        public ActionScheduleStep()
        {
        }

        public ActionScheduleStep(int index, string groupId, List<ScheduledActionChoice> choices)
        {
            Index = Math.Max(0, index);
            GroupId = groupId ?? string.Empty;
            Choices = choices ?? new List<ScheduledActionChoice>();
        }

        public int Index;
        public string GroupId;
        public List<ScheduledActionChoice> Choices = new List<ScheduledActionChoice>();
    }

    /// <summary>行动执行上下文：把配置行动与其所属行动组、本次耗时和步骤索引绑定在一起。</summary>
    public sealed class ActionExecutionContext
    {
        public ActionExecutionContext(ScheduledActionChoice choice)
        {
            Choice = choice;
            Action = choice?.Action;
            GroupId = choice?.GroupId ?? string.Empty;
            StepIndex = choice?.StepIndex ?? 0;
            CostDays = choice?.CostDays ?? Action?.CostDays ?? 0;
        }

        public ActionExecutionContext(cfg.GameAction action)
        {
            Action = action;
            GroupId = string.Empty;
            StepIndex = 0;
            CostDays = action?.CostDays ?? 0;
        }

        public ScheduledActionChoice Choice { get; }

        public cfg.GameAction Action { get; }

        public string GroupId { get; }

        public int StepIndex { get; }

        public int CostDays { get; }

        public bool IsValid => Action != null;
    }
}
