using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 标签描述占位符回填：把 desc 模板里的 {0} {1} ... 按顺序替换为 effectValue 数值，
    /// 避免数值在 desc 文本与 effectValue 两处重复维护。纯逻辑、无 Unity 依赖，便于单测。
    /// 数值统一去尾零（3→"3"、1.5→"1.5"）。
    /// signed=true 时正数补"+"号（加减类效果用，策划模板无需手写正负号）；
    /// signed=false 时原样（倍率类用，如 ×1.5）。负数永远自带"-"。
    /// </summary>
    public static class TagDescFormatter
    {
        private const string PlainFormat = "0.######";
        private const string SignedFormat = "+0.######;-0.######;0";

        private static readonly Regex Token = new Regex(@"\{(\d+)\}", RegexOptions.Compiled);

        /// <summary>
        /// 用 values 回填 template 中的 {i} 占位符。索引越界的占位符原样保留，便于策划自查。
        /// </summary>
        public static string Format(string template, IReadOnlyList<float> values, bool signed = false)
        {
            if (string.IsNullOrEmpty(template) || values == null || values.Count == 0)
            {
                return template ?? string.Empty;
            }

            string numberFormat = signed ? SignedFormat : PlainFormat;
            return Token.Replace(template, match =>
            {
                int index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                if (index < 0 || index >= values.Count)
                {
                    return match.Value;
                }

                return values[index].ToString(numberFormat, CultureInfo.InvariantCulture);
            });
        }
    }
}
