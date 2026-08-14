using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 技能描述拼接器：把子技能模板（sub_skill.descTemplate）里的占位符按组合层参数（合成后的 SkillRuleDef）回填，
    /// 再将同一技能的各子技能描述用「。+换行」拼接成技能完整描述。纯逻辑、无 Unity 依赖，便于单测。
    ///
    /// 支持的占位符（同时兼容 {token} 与 ${token}）：
    ///   {0}{1}..  actionValue[i]（signed=true 补正负号，倍率类 signed=false 原样，配合模板里的 ×）
    ///   {value}   首个 actionValue 的无符号格式（用于“额外视为 N 份”这类已含增量语义的文案）
    ///   {cscope}  带 [context] 语义标记的前提作用域词（自身/相邻/周围/同行/同列/本行/本列/全场/其他/欢乐蛋糕）
    ///   {ascope}  带 [context] 语义标记的行为范围核心（不含「食物」及随机目标数量）
    ///   {unit}    计数单位：份 / 个 / 种；{thr} 阈值；{count} 目标数
    ///   {countas} 兼容旧配置：把 AddCountAs 增量 actionValue 显示为基础 1 份加增量后的总数
    ///   {atargets}/{targets} 旧模板兼容别名：等价于 {ascope}食物
    ///   {tiers}   condParam 里 tiers: 解析为 3/5/8；{tiervals} actionParam 里 tiervals: 解析为 1.5/2.5/5
    ///   {floor}   actionParam 里 multfloor:/floor: 的值；{cat} 分类名（cake→蛋糕）
    /// 未识别或索引越界的占位符原样保留，便于策划自查。
    /// </summary>
    public static class SkillDescComposer
    {
        /// <summary>技能内各子技能描述之间的分隔符。</summary>
        public const string Separator = "。\n";

        private const string PlainFormat = "0.######";
        private const string SignedFormat = "+0.######;-0.######;0";

        private static readonly Regex Token = new Regex(@"\$?\{([A-Za-z]+|\d+)\}", RegexOptions.Compiled);

        /// <summary>把若干子技能描述按顺序用「。+换行」拼接（空片段跳过）。</summary>
        public static string ComposeSkill(IReadOnlyList<string> componentDescs)
        {
            if (componentDescs == null || componentDescs.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (string part in componentDescs)
            {
                if (string.IsNullOrEmpty(part))
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append(Separator);
                }

                sb.Append(part);
            }

            return sb.ToString();
        }

        /// <summary>用一条合成后的规则回填一个子技能描述模板。</summary>
        public static string ComposeComponent(string template, SkillRuleDef rule, bool signed)
        {
            if (string.IsNullOrEmpty(template) || rule == null)
            {
                return template ?? string.Empty;
            }

            string numberFormat = signed ? SignedFormat : PlainFormat;
            return Token.Replace(template, match =>
            {
                string key = match.Groups[1].Value;
                if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
                {
                    if (idx >= 0 && idx < rule.ActionValues.Count)
                    {
                        return rule.ActionValues[idx].ToString(numberFormat, CultureInfo.InvariantCulture);
                    }

                    return match.Value;
                }

                switch (key)
                {
                    case "cscope": return SemanticScope(ScopeWord(rule.CondScope));
                    case "ascope": return SemanticActionScope(rule.ActionScope, rule.ActionCount);
                    case "atargets":
                    case "targets": return SemanticActionScope(rule.ActionScope, rule.ActionCount) + "食物";
                    case "unit": return CountUnitWord(rule);
                    case "value": return rule.ActionValue.ToString(PlainFormat, CultureInfo.InvariantCulture);
                    // 本体「视为N份食物」：actionValue 存的是相对 base(=1) 的增量 N-1，显示总数 N。
                    case "countas": return ((int)System.Math.Round(rule.ActionValue, System.MidpointRounding.AwayFromZero) + 1).ToString(CultureInfo.InvariantCulture);
                    case "thr": return SkillConditionParamParser.ThresholdOrDefault(rule.CondParam).ToString(CultureInfo.InvariantCulture);
                    case "count": return rule.ActionCount.ToString(CultureInfo.InvariantCulture);
                    case "tiers": return JoinBar(ExtractAfter(rule.CondParam, "tiers:"));
                    case "tiervals": return FormatNumberList(
                        ExtractAfter(FindEntry(rule.ActionParams, "tiervals:"), "tiervals:"),
                        numberFormat);
                    case "floor": return ExtractNumber(rule.ActionParams, "multfloor:", "floor:");
                    case "cat": return SemanticTerm(CatWord(rule));
                    default: return match.Value;
                }
            });
        }

        private static string ScopeWord(SkillScope scope)
        {
            switch (scope)
            {
                case SkillScope.Self: return "";
                case SkillScope.Adjacent: return "相邻";
                case SkillScope.Round: return "周围";
                case SkillScope.Row: return "同行";
                case SkillScope.Column: return "同列";
                case SkillScope.RowAndColumn: return "同行同列";
                case SkillScope.RoundAndSelf: return "周围及自身";
                case SkillScope.RowAndSelf: return "本行";
                case SkillScope.ColumnAndSelf: return "本列";
                case SkillScope.All: return "全场";
                case SkillScope.Other: return "其他";
                case SkillScope.Left: return "左侧";
                case SkillScope.Up: return "上侧";
                case SkillScope.Right: return "右侧";
                case SkillScope.Down: return "下侧";
                case SkillScope.LeftAndSelf: return "左侧及自身";
                case SkillScope.UpAndSelf: return "上侧及自身";
                case SkillScope.RightAndSelf: return "右侧及自身";
                case SkillScope.DownAndSelf: return "下侧及自身";
                default: return string.Empty;
            }
        }

        private static string ActionScopePhrase(SkillScope scope, int count)
        {
            return ActionScopeText(scope, count);
        }

        private static string SemanticScope(string scope)
        {
            return string.IsNullOrEmpty(scope) ? string.Empty : $"[context]{scope}[/context]";
        }

        private static string SemanticTerm(string term)
        {
            switch (term)
            {
                case "甜蜜传递":
                case "欢乐蛋糕":
                case "蛋糕":
                case "消耗品":
                case "装饰品":
                case "丢弃":
                    return $"[term]{term}[/term]";
                case "份数":
                    return $"[strong]{term}[/strong]";
                default:
                    return term ?? string.Empty;
            }
        }

        private static string SemanticActionScope(SkillScope scope, int count)
        {
            string phrase = ActionScopePhrase(scope, count);
            if (string.IsNullOrEmpty(phrase))
            {
                return string.Empty;
            }

            // 范围只包含方向/区域核心；随机目标的数量与“个”保持正文样式。
            if (count > 0)
            {
                string scopeCore = ScopeWord(scope);
                if (string.IsNullOrEmpty(scopeCore) ||
                    !phrase.StartsWith(scopeCore, System.StringComparison.Ordinal))
                {
                    return phrase;
                }

                return $"[context]{scopeCore}[/context]{phrase.Substring(scopeCore.Length)}";
            }

            return SemanticScope(phrase);
        }

        private static string ActionScopeText(SkillScope scope, int count)
        {
            if (scope == SkillScope.Self)
            {
                return "";
            }

            if (count <= 0)
            {
                switch (scope)
                {
                    case SkillScope.Adjacent: return "相邻";
                    case SkillScope.Round: return "周围";
                    case SkillScope.Row: return "同行";
                    case SkillScope.Column: return "同列";
                    case SkillScope.RowAndColumn: return "同行同列";
                    case SkillScope.RoundAndSelf: return "周围及自身";
                    case SkillScope.RowAndSelf: return "本行";
                    case SkillScope.ColumnAndSelf: return "本列";
                    case SkillScope.All: return "所有";
                    case SkillScope.Other: return "其他";
                    case SkillScope.Left: return "左侧";
                    case SkillScope.Up: return "上侧";
                    case SkillScope.Right: return "右侧";
                    case SkillScope.Down: return "下侧";
                    case SkillScope.LeftAndSelf: return "左侧及自身";
                    case SkillScope.UpAndSelf: return "上侧及自身";
                    case SkillScope.RightAndSelf: return "右侧及自身";
                    case SkillScope.DownAndSelf: return "下侧及自身";
                    default: return ScopeWord(scope);
                }
            }

            switch (scope)
            {
                case SkillScope.Adjacent: return $"相邻 {count} 个";
                case SkillScope.Round: return $"周围 {count} 个";
                case SkillScope.Row: return $"同行 {count} 个";
                case SkillScope.Column: return $"同列 {count} 个";
                case SkillScope.RowAndColumn: return $"同行同列 {count} 个";
                case SkillScope.RoundAndSelf: return $"周围及自身 {count} 个";
                case SkillScope.RowAndSelf: return $"本行 {count} 个";
                case SkillScope.ColumnAndSelf: return $"本列 {count} 个";
                case SkillScope.All: return $"{count} 个";
                case SkillScope.Other: return $"其他 {count} 个";
                case SkillScope.Left: return $"左侧 {count} 个";
                case SkillScope.Up: return $"上侧 {count} 个";
                case SkillScope.Right: return $"右侧 {count} 个";
                case SkillScope.Down: return $"下侧 {count} 个";
                case SkillScope.LeftAndSelf: return $"左侧及自身 {count} 个";
                case SkillScope.UpAndSelf: return $"上侧及自身 {count} 个";
                case SkillScope.RightAndSelf: return $"右侧及自身 {count} 个";
                case SkillScope.DownAndSelf: return $"下侧及自身 {count} 个";
                default: return ScopeWord(scope);
            }
        }

        private static string CatWord(SkillRuleDef rule)
        {
            string cat = ExtractAfter(FindEntry(rule.ActionParams, "cat:"), "cat:");
            if (string.IsNullOrEmpty(cat))
            {
                cat = ExtractAfter(rule.CondParam, "cat:");
            }
            if (string.IsNullOrEmpty(cat))
            {
                cat = CategoryFromCondition(rule.CondParam);
            }

            switch (cat)
            {
                case "cake": return "蛋糕";
                case "": return string.Empty;
                default: return cat;
            }
        }

        private static string CountUnitWord(SkillRuleDef rule)
        {
            if (rule.CondUnit == CountUnit.Kinds)
            {
                return "种";
            }

            if (rule.CondUnit == CountUnit.PhysicalInstances
                || rule.CondType == SkillConditionType.SkillCount
                || rule.CondType == SkillConditionType.SkillTypeCount
                || rule.CondType == SkillConditionType.TagCount
                || rule.CondType == SkillConditionType.EmptyCell
                || rule.CondType == SkillConditionType.OccupiedCell)
            {
                return "个";
            }

            return "份";
        }

        /// <summary>
        /// CategoryCount 的 condParam 允许把分类直接写在第一个普通片段里，
        /// 例如 "cake;tiers:3|5|8"，同时跳过 tiers/div 等控制片段。
        /// </summary>
        private static string CategoryFromCondition(string condParam)
        {
            if (string.IsNullOrEmpty(condParam))
            {
                return string.Empty;
            }

            string[] segments = condParam.Split(';');
            foreach (string raw in segments)
            {
                string segment = raw.Trim();
                if (segment.Length == 0 ||
                    segment.StartsWith("tiers:", System.StringComparison.OrdinalIgnoreCase) ||
                    segment.StartsWith("div:", System.StringComparison.OrdinalIgnoreCase) ||
                    segment.StartsWith("include:", System.StringComparison.OrdinalIgnoreCase) ||
                    segment.StartsWith("source:", System.StringComparison.OrdinalIgnoreCase) ||
                    segment.StartsWith("skilltype:", System.StringComparison.OrdinalIgnoreCase) ||
                    segment.StartsWith("cat:", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return segment;
            }

            return string.Empty;
        }

        /// <summary>取 source 中 token 之后、首个 ';' 之前的原始片段（如 "cake;tiers:3|5|8" + "tiers:" → "3|5|8"）。</summary>
        private static string ExtractAfter(string source, string token)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(token))
            {
                return string.Empty;
            }

            int idx = source.IndexOf(token, System.StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return string.Empty;
            }

            string body = source.Substring(idx + token.Length);
            int end = body.IndexOf(';');
            return (end >= 0 ? body.Substring(0, end) : body).Trim();
        }

        /// <summary>在 params 中找到首个含 token 的元素。</summary>
        private static string FindEntry(IReadOnlyList<string> parameters, string token)
        {
            if (parameters == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < parameters.Count; i++)
            {
                string p = parameters[i];
                if (!string.IsNullOrEmpty(p) && p.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return p;
                }
            }

            return string.Empty;
        }

        private static string ExtractNumber(IReadOnlyList<string> parameters, params string[] tokens)
        {
            foreach (string token in tokens)
            {
                string entry = FindEntry(parameters, token);
                string value = ExtractAfter(entry, token);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        /// <summary>把 "3|5|8" / "3,5,8" 归一化为 "3/5/8"。</summary>
        private static string JoinBar(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            string[] parts = raw.Split('|', ',');
            var trimmed = new List<string>(parts.Length);
            foreach (string p in parts)
            {
                string t = p.Trim();
                if (t.Length > 0)
                {
                    trimmed.Add(t);
                }
            }

            return string.Join("/", trimmed);
        }

        /// <summary>把多档数值归一化为斜杠分隔，并与普通 {0} 一样应用 signed 格式。</summary>
        private static string FormatNumberList(string raw, string numberFormat)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            string[] parts = raw.Split('|', ',');
            var formatted = new List<string>(parts.Length);
            foreach (string part in parts)
            {
                string value = part.Trim();
                if (value.Length == 0)
                {
                    continue;
                }

                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
                {
                    formatted.Add(number.ToString(numberFormat, CultureInfo.InvariantCulture));
                }
                else
                {
                    formatted.Add(value);
                }
            }

            return string.Join("/", formatted);
        }
    }
}
