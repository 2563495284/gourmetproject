using System;
using System.Globalization;
using BreakInfinity;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>统一美味值显示：八位数以下完整整数，八位数起使用最多三位有效数字的科学计数。</summary>
    public static class ScoreNumberFormatter
    {
        public static readonly BigDouble ScientificThreshold = new BigDouble(10_000_000d);

        public static string Format(BigDouble value)
        {
            if (BigDouble.IsNaN(value) || BigDouble.IsInfinity(value))
            {
                return value.ToString();
            }

            if (BigDouble.Abs(value) < ScientificThreshold)
            {
                return BigDouble.Round(value, MidpointRounding.AwayFromZero).ToString("F0");
            }

            BigDouble rounded = BigDouble.Normalize(
                Math.Round(value.Mantissa, 2, MidpointRounding.AwayFromZero),
                value.Exponent);
            return rounded.Mantissa.ToString("0.##", CultureInfo.InvariantCulture)
                   + "e"
                   + rounded.Exponent.ToString(CultureInfo.InvariantCulture);
        }
    }
}
