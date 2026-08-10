using System;
using BreakInfinity;

namespace GourmetProject.Game.Save
{
    /// <summary>与具体大数库序列化细节解耦的存档表示。</summary>
    [Serializable]
    public sealed class BigNumberSaveData
    {
        public bool HasValue;
        public double Mantissa;
        public long Exponent;

        public static BigNumberSaveData From(BigDouble value)
        {
            return new BigNumberSaveData
            {
                HasValue = true,
                Mantissa = value.Mantissa,
                Exponent = value.Exponent,
            };
        }

        public BigDouble GetValue(BigDouble legacyFallback)
        {
            return HasValue
                ? BigDouble.Normalize(Mantissa, Exponent)
                : legacyFallback;
        }

        public BigNumberSaveData Clone()
        {
            return new BigNumberSaveData
            {
                HasValue = HasValue,
                Mantissa = Mantissa,
                Exponent = Exponent,
            };
        }

        public static int ToLegacyInt(BigDouble value)
        {
            if (value >= int.MaxValue) return int.MaxValue;
            if (value <= int.MinValue) return int.MinValue;
            return (int)Math.Round(value.ToDouble(), MidpointRounding.AwayFromZero);
        }

        public static float ToLegacyFloat(BigDouble value)
        {
            if (value >= float.MaxValue) return float.MaxValue;
            if (value <= -float.MaxValue) return -float.MaxValue;
            return (float)value.ToDouble();
        }
    }
}
