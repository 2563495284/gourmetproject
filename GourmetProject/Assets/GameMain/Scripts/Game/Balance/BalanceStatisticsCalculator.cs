using System;
using System.Collections.Generic;
using System.Linq;

namespace GourmetProject.Game.Balance
{
    public static class BalanceStatisticsCalculator
    {
        public static BalanceStatistics Calculate(string scenarioName, BuildCheckpoint checkpoint, IReadOnlyList<BalanceSampleResult> samples, int roundTo = 1)
        {
            var result = new BalanceStatistics
            {
                ScenarioName = scenarioName ?? string.Empty,
                CheckpointName = checkpoint?.Name ?? string.Empty,
                Week = checkpoint?.Week ?? 0,
                Day = checkpoint?.Day ?? 0f,
                SampleCount = samples?.Count ?? 0,
                CurrentRequiredScore = checkpoint?.RequiredScore ?? 1,
            };
            if (samples == null || samples.Count == 0)
            {
                result.Warnings.Add("没有样本。");
                return result;
            }

            List<float> scores = samples
                .Where(s => s != null && s.IsValid)
                .Select(s => (float)Math.Clamp(s.TotalScore.ToDouble(), -float.MaxValue, float.MaxValue))
                .OrderBy(v => v)
                .ToList();
            result.ValidCount = scores.Count;
            result.ValidRate = (float)scores.Count / samples.Count;
            if (scores.Count == 0)
            {
                result.Warnings.Add("所有样本均无效。");
                return result;
            }

            result.Mean = scores.Average();
            BalanceSampleResult firstValid = samples.FirstOrDefault(s => s != null && s.IsValid);
            if (firstValid != null && firstValid.EffectiveRequiredScore > 0) result.CurrentRequiredScore = firstValid.EffectiveRequiredScore;
            result.StandardDeviation = (float)Math.Sqrt(scores.Sum(v => Math.Pow(v - result.Mean, 2d)) / scores.Count);
            result.CoefficientOfVariation = Math.Abs(result.Mean) > 0.0001f ? result.StandardDeviation / Math.Abs(result.Mean) : 0f;
            result.P10 = Quantile(scores, 0.10f);
            result.P25 = Quantile(scores, 0.25f);
            result.P50 = Quantile(scores, 0.50f);
            result.P75 = Quantile(scores, 0.75f);
            result.P90 = Quantile(scores, 0.90f);
            result.Max = scores[scores.Count - 1];
            result.CurrentPassRate = (float)scores.Count(v => v >= result.CurrentRequiredScore) / scores.Count;
            result.SuggestedNormal = RoundUp(Quantile(scores, 0.30f), roundTo);
            result.SuggestedHard = RoundUp(Quantile(scores, 0.50f), roundTo);
            result.SuggestedChallenge = RoundUp(Quantile(scores, 0.75f), roundTo);

            if (result.ValidRate < 0.99f) result.Warnings.Add($"无效样本占比 {(1f - result.ValidRate):P1}，超过 1%。");
            if (result.CoefficientOfVariation > 0.30f) result.Warnings.Add($"CV={result.CoefficientOfVariation:F2}，波动过高。");
            if (result.P50 > 0f && result.P90 / result.P50 > 2f) result.Warnings.Add($"P90/P50={result.P90 / result.P50:F2}，上限膨胀。");
            return result;
        }

        public static float Quantile(IReadOnlyList<float> sortedValues, float probability)
        {
            if (sortedValues == null || sortedValues.Count == 0) return 0f;
            probability = Math.Max(0f, Math.Min(1f, probability));
            double position = (sortedValues.Count - 1) * probability;
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            if (lower == upper) return sortedValues[lower];
            float t = (float)(position - lower);
            return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * t;
        }

        private static int RoundUp(float value, int roundTo)
        {
            int unit = Math.Max(1, roundTo);
            return Math.Max(1, (int)Math.Ceiling(value / unit) * unit);
        }
    }
}
