using System;
using System.Collections.Generic;
using System.Globalization;

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
            string condParam,
            SkillActionType actionType,
            SkillScope actionScope,
            int actionCount,
            IReadOnlyList<float> actionValues,
            IReadOnlyList<string> actionParams,
            IReadOnlyList<string> termIds = null)
        {
            Id = id ?? string.Empty;
            SkillId = skillId ?? string.Empty;
            Order = order;
            Trigger = trigger;
            CondType = condType;
            CondScope = condScope;
            CondUnit = condUnit;
            CondMode = condMode;
            CondParam = condParam ?? string.Empty;
            ActionType = actionType;
            ActionScope = actionScope;
            ActionCount = actionCount;
            ActionValues = actionValues ?? EmptyValues;
            ActionParams = actionParams ?? EmptyParams;
            TermIds = termIds ?? EmptyParams;
        }

        public string Id { get; }

        public string SkillId { get; }

        public int Order { get; }

        public SkillTrigger Trigger { get; }

        public SkillConditionType CondType { get; }

        public SkillScope CondScope { get; }

        public CountUnit CondUnit { get; }

        public CountMode CondMode { get; }

        public string CondParam { get; }

        public SkillActionType ActionType { get; }

        public SkillScope ActionScope { get; }

        public int ActionCount { get; }

        public IReadOnlyList<float> ActionValues { get; }

        public IReadOnlyList<string> ActionParams { get; }

        /// <summary>该子技能自身关联的专有名词 id 列表（去重前的原始声明；空表示无）。供甜蜜传递携带时展示其术语。</summary>
        public IReadOnlyList<string> TermIds { get; }

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

    /// <summary>前提附加参数解析工具。比较条件用 condParam 表达，如 "gte:2"、">=2" 或 "op:gte;threshold:2"。</summary>
    public static class SkillConditionParamParser
    {
        public static bool TryGetComparison(string condParam, out CompareOp op, out int threshold)
        {
            op = CompareOp.Gte;
            threshold = 0;
            bool hasOp = false;
            bool hasThreshold = false;

            if (string.IsNullOrEmpty(condParam))
            {
                return false;
            }

            foreach (string rawSegment in condParam.Split(';'))
            {
                string segment = rawSegment.Trim();
                if (segment.Length == 0)
                {
                    continue;
                }

                if (TryParseSymbolComparison(segment, out op, out threshold))
                {
                    return true;
                }

                if (!TryParseKeyValue(segment, out string key, out string value))
                {
                    if (int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out threshold))
                    {
                        op = CompareOp.Gte;
                        return true;
                    }

                    continue;
                }

                if (TryParseCompareName(value, out CompareOp parsedOp) && IsCompareKey(key))
                {
                    op = parsedOp;
                    hasOp = true;
                    continue;
                }

                if (TryParseThresholdKey(key, value, out CompareOp keyOp, out int parsedThreshold))
                {
                    if (!hasOp)
                    {
                        op = keyOp;
                    }

                    threshold = parsedThreshold;
                    hasThreshold = true;
                }
            }

            return hasThreshold;
        }

        public static int ThresholdOrDefault(string condParam, int defaultValue = 0)
        {
            return TryGetComparison(condParam, out _, out int threshold) ? threshold : defaultValue;
        }

        public static bool EvaluateComparison(string condParam, float left, bool defaultValue)
        {
            return TryGetComparison(condParam, out CompareOp op, out int threshold)
                ? op.Evaluate(left, threshold)
                : defaultValue;
        }

        private static bool TryParseSymbolComparison(string segment, out CompareOp op, out int threshold)
        {
            op = CompareOp.Gte;
            threshold = 0;

            if (segment.StartsWith(">=", StringComparison.Ordinal))
            {
                op = CompareOp.Gte;
                return TryParseInt(segment.Substring(2), out threshold);
            }

            if (segment.StartsWith("<=", StringComparison.Ordinal))
            {
                op = CompareOp.Lte;
                return TryParseInt(segment.Substring(2), out threshold);
            }

            if (segment.StartsWith("==", StringComparison.Ordinal))
            {
                op = CompareOp.Eq;
                return TryParseInt(segment.Substring(2), out threshold);
            }

            if (segment.StartsWith(">", StringComparison.Ordinal))
            {
                op = CompareOp.Gt;
                return TryParseInt(segment.Substring(1), out threshold);
            }

            if (segment.StartsWith("<", StringComparison.Ordinal))
            {
                op = CompareOp.Lt;
                return TryParseInt(segment.Substring(1), out threshold);
            }

            if (segment.StartsWith("=", StringComparison.Ordinal))
            {
                op = CompareOp.Eq;
                return TryParseInt(segment.Substring(1), out threshold);
            }

            return false;
        }

        private static bool TryParseKeyValue(string segment, out string key, out string value)
        {
            int split = segment.IndexOf(':');
            if (split < 0)
            {
                split = segment.IndexOf('=');
            }

            if (split < 0)
            {
                key = string.Empty;
                value = string.Empty;
                return false;
            }

            key = segment.Substring(0, split).Trim().ToLowerInvariant();
            value = segment.Substring(split + 1).Trim();
            return key.Length > 0 && value.Length > 0;
        }

        private static bool IsCompareKey(string key)
            => key == "op" || key == "cmp" || key == "compare";

        private static bool TryParseThresholdKey(string key, string value, out CompareOp op, out int threshold)
        {
            op = CompareOp.Gte;
            threshold = 0;

            switch (key)
            {
                case "gte":
                case "ge":
                case "min":
                    op = CompareOp.Gte;
                    break;
                case "lte":
                case "le":
                case "max":
                    op = CompareOp.Lte;
                    break;
                case "eq":
                    op = CompareOp.Eq;
                    break;
                case "gt":
                    op = CompareOp.Gt;
                    break;
                case "lt":
                    op = CompareOp.Lt;
                    break;
                case "thr":
                case "threshold":
                    op = CompareOp.Gte;
                    break;
                default:
                    return false;
            }

            return TryParseInt(value, out threshold);
        }

        private static bool TryParseCompareName(string value, out CompareOp op)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "gte":
                case "ge":
                case ">=":
                    op = CompareOp.Gte;
                    return true;
                case "lte":
                case "le":
                case "<=":
                    op = CompareOp.Lte;
                    return true;
                case "eq":
                case "==":
                case "=":
                    op = CompareOp.Eq;
                    return true;
                case "gt":
                case ">":
                    op = CompareOp.Gt;
                    return true;
                case "lt":
                case "<":
                    op = CompareOp.Lt;
                    return true;
                default:
                    op = CompareOp.None;
                    return false;
            }
        }

        private static bool TryParseInt(string value, out int result)
            => int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
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
