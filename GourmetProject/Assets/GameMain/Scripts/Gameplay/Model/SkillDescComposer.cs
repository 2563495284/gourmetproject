using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 技能描述拼接器：把子技能模板（sub_skill.descTemplate）里的占位符按组合层参数（合成后的 SkillRuleDef）回填，
    /// 再将同一技能的各子技能描述用「；」拼接成技能完整描述。纯逻辑、无 Unity 依赖，便于单测。
    ///
    /// 支持的占位符：
    ///   {0}{1}..  actionValue[i]（signed=true 补正负号，倍率类 signed=false 原样，配合模板里的 ×）
    ///   {cscope}  前提作用域词（自身/周围/同行/同列/全场）
    ///   {ascope}  行为作用域词（同上，用于「复制周围食物的技能」这类句式）
    ///   {atargets} 行为作用域「X食物」前缀：Self→空串，其余→周围食物/同行食物/…（用于「XX食物倍率+Y」）
    ///   {unit}    计数单位：个 / 种；{thr} 阈值；{count} 目标数
    ///   {countas} 本体「视为N个食物」总数（=actionValue+1，因 base countAs 恒为 1，actionValue 存增量 N-1）
    ///   {targets} 甜蜜传递目标短语（作用域+目标数，0=所有）
    ///   {tiers}   condParam 里 tiers: 解析为 3/5/8；{tiervals} actionParam 里 tiervals: 解析为 1.5/2.5/5
    ///   {floor}   actionParam 里 multfloor:/floor: 的值；{cat} 分类名（cake→蛋糕）
    /// 未识别或索引越界的占位符原样保留，便于策划自查。
    /// </summary>
    public static class SkillDescComposer
    {
        /// <summary>技能内各子技能描述之间的分隔符。</summary>
        public const string Separator = "；";

        private const string PlainFormat = "0.######";
        private const string SignedFormat = "+0.######;-0.######;0";

        private static readonly Regex Token = new Regex(@"\{([A-Za-z]+|\d+)\}", RegexOptions.Compiled);

        /// <summary>把若干子技能描述按顺序用「；」拼接（空片段跳过）。</summary>
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
                    case "cscope": return ScopeWord(rule.CondScope);
                    case "ascope": return ScopeWord(rule.ActionScope);
                    case "atargets": return ScopeDishesPrefix(rule.ActionScope);
                    case "unit": return rule.CondUnit == CountUnit.Kinds ? "种" : "个";
                    // 本体「视为N个食物」：actionValue 存的是相对 base(=1) 的增量 N-1，显示总数 N。
                    case "countas": return ((int)System.Math.Round(rule.ActionValue, System.MidpointRounding.AwayFromZero) + 1).ToString(CultureInfo.InvariantCulture);
                    case "thr": return rule.CondThreshold.ToString(CultureInfo.InvariantCulture);
                    case "count": return rule.ActionCount.ToString(CultureInfo.InvariantCulture);
                    case "targets": return TransferTarget(rule.ActionScope, rule.ActionCount);
                    case "tiers": return JoinBar(ExtractAfter(rule.CondParam, "tiers:"));
                    case "tiervals": return JoinBar(ExtractAfter(FindEntry(rule.ActionParams, "tiervals:"), "tiervals:"));
                    case "floor": return ExtractNumber(rule.ActionParams, "multfloor:", "floor:");
                    case "cat": return CatWord(rule);
                    default: return match.Value;
                }
            });
        }

        private static string ScopeWord(SkillScope scope)
        {
            switch (scope)
            {
                case SkillScope.Self: return "自身";
                case SkillScope.Adjacent: return "周围";
                case SkillScope.Row: return "同行";
                case SkillScope.Column: return "同列";
                case SkillScope.All: return "全场";
                default: return string.Empty;
            }
        }

        /// <summary>行为作用域「X食物」前缀；Self 返回空串（「倍率 +3」而非「自身食物倍率 +3」）。</summary>
        private static string ScopeDishesPrefix(SkillScope scope)
        {
            switch (scope)
            {
                case SkillScope.Adjacent: return "周围食物";
                case SkillScope.Row: return "同行食物";
                case SkillScope.Column: return "同列食物";
                case SkillScope.All: return "所有食物";
                case SkillScope.Self:
                default: return string.Empty;
            }
        }

        /// <summary>甜蜜传递目标短语：作用域词 + 目标数（0=所有）。</summary>
        private static string TransferTarget(SkillScope scope, int count)
        {
            string prefix;
            switch (scope)
            {
                case SkillScope.Adjacent: prefix = "周围"; break;
                case SkillScope.Row: prefix = "同行"; break;
                case SkillScope.Column: prefix = "同列"; break;
                case SkillScope.All:
                default: prefix = string.Empty; break;
            }

            if (count <= 0)
            {
                return prefix + "所有食物";
            }

            // prefix 为空（全场）时保留数字前的空格，与「给 1 个食物」的行文风格一致。
            return $"{prefix} {count} 个食物";
        }

        private static string CatWord(SkillRuleDef rule)
        {
            string cat = ExtractAfter(FindEntry(rule.ActionParams, "cat:"), "cat:");
            if (string.IsNullOrEmpty(cat))
            {
                cat = ExtractAfter(rule.CondParam, "cat:");
            }

            switch (cat)
            {
                case "cake": return "蛋糕";
                case "": return string.Empty;
                default: return cat;
            }
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
    }
}
