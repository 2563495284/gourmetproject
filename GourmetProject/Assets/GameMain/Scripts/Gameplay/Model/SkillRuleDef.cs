using System;
using System.Collections.Generic;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 一条技能规则（前提 × 行为）。纯数据，由 Game 层从 Luban TbSubSkill(具体子技能全字段) 合成，order=技能引用列表下标。
    /// 语义见 docs/design/技能.md。
    /// </summary>
    public sealed class SkillRuleDef
    {
        private static readonly IReadOnlyList<float> EmptyValues = Array.Empty<float>();
        private static readonly IReadOnlyList<string> EmptyParams = Array.Empty<string>();

        public SkillRuleDef(
            string id,
            string skillId,
            int order,
            SkillTrigger trigger,
            SkillConditionType condType,
            SkillScope condScope,
            CountUnit condUnit,
            CountMode condMode,
            CompareOp condCompare,
            int condThreshold,
            string condParam,
            SkillActionType actionType,
            SkillScope actionScope,
            int actionCount,
            IReadOnlyList<float> actionValues,
            IReadOnlyList<string> actionParams)
        {
            Id = id ?? string.Empty;
            SkillId = skillId ?? string.Empty;
            Order = order;
            Trigger = trigger;
            CondType = condType;
            CondScope = condScope;
            CondUnit = condUnit;
            CondMode = condMode;
            CondCompare = condCompare;
            CondThreshold = condThreshold;
            CondParam = condParam ?? string.Empty;
            ActionType = actionType;
            ActionScope = actionScope;
            ActionCount = actionCount;
            ActionValues = actionValues ?? EmptyValues;
            ActionParams = actionParams ?? EmptyParams;
        }

        public string Id { get; }

        public string SkillId { get; }

        public int Order { get; }

        public SkillTrigger Trigger { get; }

        public SkillConditionType CondType { get; }

        public SkillScope CondScope { get; }

        public CountUnit CondUnit { get; }

        public CountMode CondMode { get; }

        public CompareOp CondCompare { get; }

        public int CondThreshold { get; }

        public string CondParam { get; }

        public SkillActionType ActionType { get; }

        public SkillScope ActionScope { get; }

        public int ActionCount { get; }

        public IReadOnlyList<float> ActionValues { get; }

        public IReadOnlyList<string> ActionParams { get; }

        /// <summary>首个行为数值；空时为 0。</summary>
        public float ActionValue => ActionValues.Count > 0 ? ActionValues[0] : 0f;

        /// <summary>首个行为参数；空时为空串。</summary>
        public string ActionParam => ActionParams.Count > 0 ? ActionParams[0] : string.Empty;

        public bool HasActionParam(string token)
        {
            for (int i = 0; i < ActionParams.Count; i++)
            {
                if (string.Equals(ActionParams[i], token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>比较符求值工具。</summary>
    public static class CompareOpExtensions
    {
        public static bool Evaluate(this CompareOp op, float left, float right)
        {
            switch (op)
            {
                case CompareOp.Gte: return left >= right;
                case CompareOp.Lte: return left <= right;
                case CompareOp.Eq: return Math.Abs(left - right) < 0.0001f;
                case CompareOp.Gt: return left > right;
                case CompareOp.Lt: return left < right;
                case CompareOp.None:
                default:
                    return true;
            }
        }
    }
}
