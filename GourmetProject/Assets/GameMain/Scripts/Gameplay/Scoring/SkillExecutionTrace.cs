using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>技能结算演出归属。Owner 表示属于谁的流程，RuntimeSelf 表示以谁为中心计算规则。</summary>
    public enum SkillExecutionKind
    {
        NativeSkill = 0,
        SweetTransfer = 1,
        CopiedSkill = 2,
    }

    /// <summary>
    /// 一条技能规则在结算/演出层的可视化上下文。
    /// A 的子技能传给 B 时：Owner=A，RuntimeSelf=B，目标范围按 B 计算。
    /// </summary>
    public sealed class SkillExecutionTrace
    {
        private static readonly IReadOnlyList<int> EmptyIds = Array.Empty<int>();
        private static readonly IReadOnlyList<GridPos> EmptyCells = Array.Empty<GridPos>();

        public SkillExecutionTrace(
            SkillExecutionKind kind,
            int ownerDishInstanceId,
            string ownerDishId,
            string ownerDishName,
            int runtimeSelfDishInstanceId,
            string runtimeSelfDishId,
            string runtimeSelfDishName,
            string skillId,
            string skillName,
            string ruleId,
            int ruleOrder,
            SkillTrigger trigger,
            SkillActionType actionType,
            SkillConditionType conditionType,
            SkillScope conditionScope,
            SkillScope actionScope,
            string sourceLabel,
            IReadOnlyList<int> visualTargetDishInstanceIds = null,
            IReadOnlyList<GridPos> visualTargetCells = null,
            IReadOnlyList<GridPos> conditionCells = null,
            IReadOnlyList<GridPos> actionScopeCells = null,
            IReadOnlyList<GridPos> scopeRegionCells = null,
            SkillScopeRegionKind scopeRegionKind = SkillScopeRegionKind.None,
            int visualIndex = -1)
        {
            Kind = kind;
            OwnerDishInstanceId = ownerDishInstanceId;
            OwnerDishId = ownerDishId ?? string.Empty;
            OwnerDishName = ownerDishName ?? OwnerDishId;
            RuntimeSelfDishInstanceId = runtimeSelfDishInstanceId;
            RuntimeSelfDishId = runtimeSelfDishId ?? string.Empty;
            RuntimeSelfDishName = runtimeSelfDishName ?? RuntimeSelfDishId;
            SkillId = skillId ?? string.Empty;
            SkillName = skillName ?? SkillId;
            RuleId = ruleId ?? string.Empty;
            RuleOrder = ruleOrder;
            Trigger = trigger;
            ActionType = actionType;
            ConditionType = conditionType;
            ConditionScope = conditionScope;
            ActionScope = actionScope;
            SourceLabel = sourceLabel ?? string.Empty;
            VisualTargetDishInstanceIds = visualTargetDishInstanceIds ?? EmptyIds;
            VisualTargetCells = visualTargetCells ?? EmptyCells;
            ConditionCells = conditionCells ?? EmptyCells;
            ActionScopeCells = actionScopeCells ?? EmptyCells;
            ScopeRegionCells = scopeRegionCells ?? EmptyCells;
            ScopeRegionKind = scopeRegionKind;
            VisualIndex = visualIndex;
        }

        public SkillExecutionKind Kind { get; }

        public int OwnerDishInstanceId { get; }

        public string OwnerDishId { get; }

        public string OwnerDishName { get; }

        public int RuntimeSelfDishInstanceId { get; }

        public string RuntimeSelfDishId { get; }

        public string RuntimeSelfDishName { get; }

        public string SkillId { get; }

        public string SkillName { get; }

        public string RuleId { get; }

        public int RuleOrder { get; }

        public SkillTrigger Trigger { get; }

        public SkillActionType ActionType { get; }

        public SkillConditionType ConditionType { get; }

        public SkillScope ConditionScope { get; }

        public SkillScope ActionScope { get; }

        public string SourceLabel { get; }

        public IReadOnlyList<int> VisualTargetDishInstanceIds { get; }

        public IReadOnlyList<GridPos> VisualTargetCells { get; }

        public IReadOnlyList<GridPos> ConditionCells { get; }

        public IReadOnlyList<GridPos> ActionScopeCells { get; }

        /// <summary>该子技能最终允许表现层绘制的唯一棋盘范围。</summary>
        public IReadOnlyList<GridPos> ScopeRegionCells { get; }

        public SkillScopeRegionKind ScopeRegionKind { get; }

        public int VisualIndex { get; }

        public SkillExecutionTrace WithVisualIndex(int visualIndex)
        {
            return new SkillExecutionTrace(
                Kind,
                OwnerDishInstanceId,
                OwnerDishId,
                OwnerDishName,
                RuntimeSelfDishInstanceId,
                RuntimeSelfDishId,
                RuntimeSelfDishName,
                SkillId,
                SkillName,
                RuleId,
                RuleOrder,
                Trigger,
                ActionType,
                ConditionType,
                ConditionScope,
                ActionScope,
                SourceLabel,
                VisualTargetDishInstanceIds,
                VisualTargetCells,
                ConditionCells,
                ActionScopeCells,
                ScopeRegionCells,
                ScopeRegionKind,
                visualIndex);
        }

        public SkillExecutionTrace WithVisualTargets(
            IReadOnlyList<int> visualTargetDishInstanceIds,
            IReadOnlyList<GridPos> visualTargetCells)
        {
            return new SkillExecutionTrace(
                Kind,
                OwnerDishInstanceId,
                OwnerDishId,
                OwnerDishName,
                RuntimeSelfDishInstanceId,
                RuntimeSelfDishId,
                RuntimeSelfDishName,
                SkillId,
                SkillName,
                RuleId,
                RuleOrder,
                Trigger,
                ActionType,
                ConditionType,
                ConditionScope,
                ActionScope,
                SourceLabel,
                visualTargetDishInstanceIds,
                visualTargetCells,
                ConditionCells,
                ActionScopeCells,
                ScopeRegionCells,
                ScopeRegionKind,
                VisualIndex);
        }

        public SkillExecutionTrace WithRuntimeContext(
            DishInstance owner,
            DishInstance runtimeSelf,
            IReadOnlyList<int> visualTargetDishInstanceIds,
            IReadOnlyList<GridPos> visualTargetCells)
        {
            return new SkillExecutionTrace(
                Kind,
                owner != null ? owner.Id : OwnerDishInstanceId,
                owner?.Def?.Id ?? OwnerDishId,
                owner?.Def?.Name ?? OwnerDishName,
                runtimeSelf != null ? runtimeSelf.Id : RuntimeSelfDishInstanceId,
                runtimeSelf?.Def?.Id ?? RuntimeSelfDishId,
                runtimeSelf?.Def?.Name ?? RuntimeSelfDishName,
                SkillId,
                SkillName,
                RuleId,
                RuleOrder,
                Trigger,
                ActionType,
                ConditionType,
                ConditionScope,
                ActionScope,
                SourceLabel,
                visualTargetDishInstanceIds,
                visualTargetCells,
                ConditionCells,
                ActionScopeCells,
                ScopeRegionCells,
                ScopeRegionKind,
                VisualIndex);
        }

        public static SkillExecutionTrace Create(
            GameplayDatabase db,
            DiningTable board,
            DishInstance owner,
            DishInstance runtimeSelf,
            SkillDef skill,
            SkillRuleDef rule,
            SkillExecutionKind kind,
            string sourceLabel,
            SkillScopeVisualMode visualMode)
        {
            if (runtimeSelf == null || rule == null)
            {
                return null;
            }

            SkillScopeVisual visual = SkillScopeResolver.Resolve(db, board, runtimeSelf, rule, visualMode);
            return new SkillExecutionTrace(
                kind,
                owner != null ? owner.Id : 0,
                owner?.Def?.Id,
                owner?.Def?.Name,
                runtimeSelf.Id,
                runtimeSelf.Def?.Id,
                runtimeSelf.Def?.Name,
                skill?.Id ?? rule.SkillId,
                skill?.Name,
                rule.Id,
                rule.Order,
                rule.Trigger,
                rule.ActionType,
                rule.CondType,
                rule.CondScope,
                rule.ActionScope,
                sourceLabel,
                visual.VisualTargetDishInstanceIds,
                visual.VisualTargetCells,
                visual.ConditionCells,
                visual.ActionScopeCells,
                visual.ScopeRegionCells,
                visual.ScopeRegionKind);
        }

        public static SkillExecutionTrace CreateWithOwnerFallback(
            GameplayDatabase db,
            DiningTable board,
            int ownerDishInstanceId,
            string ownerDishName,
            DishInstance runtimeSelf,
            SkillDef skill,
            SkillRuleDef rule,
            SkillExecutionKind kind,
            string sourceLabel,
            SkillScopeVisualMode visualMode)
        {
            SkillExecutionTrace trace = Create(db, board, null, runtimeSelf, skill, rule, kind, sourceLabel, visualMode);
            if (trace == null)
            {
                return null;
            }

            return new SkillExecutionTrace(
                trace.Kind,
                ownerDishInstanceId,
                string.Empty,
                ownerDishName,
                trace.RuntimeSelfDishInstanceId,
                trace.RuntimeSelfDishId,
                trace.RuntimeSelfDishName,
                trace.SkillId,
                trace.SkillName,
                trace.RuleId,
                trace.RuleOrder,
                trace.Trigger,
                trace.ActionType,
                trace.ConditionType,
                trace.ConditionScope,
                trace.ActionScope,
                trace.SourceLabel,
                trace.VisualTargetDishInstanceIds,
                trace.VisualTargetCells,
                trace.ConditionCells,
                trace.ActionScopeCells,
                trace.ScopeRegionCells,
                trace.ScopeRegionKind,
                trace.VisualIndex);
        }
    }
}
