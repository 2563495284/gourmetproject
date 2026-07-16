using System;
using GourmetProject.Runtime;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    public readonly struct GoldRange
    {
        public GoldRange(int min, int max)
        {
            Min = Math.Min(min, max);
            Max = Math.Max(min, max);
        }

        public int Min { get; }

        public int Max { get; }
    }

    public enum HiddenScorePurpose
    {
        TargetScore,
        Dish,
        PassiveItem,
        Fragment,
        Gold,
    }

    /// <summary>v2 隐藏分统一入口：目标分、奖励池隐藏分、金币区间派生。</summary>
    public static class HiddenScoreService
    {
        public static int TargetScore(GameRun run, ActionExecutionContext context = null, float extraTargetScoreHiddenOffset = 0f)
        {
            float offset = HiddenOffset(run, context, HiddenScorePurpose.TargetScore) + extraTargetScoreHiddenOffset;
            int derived = EvaluateTargetScore(run, offset, context?.TargetScoreDayOverride);
            return Math.Max(1, derived);
        }

        public static int DishHiddenScore(GameRun run, ActionExecutionContext context = null)
        {
            return EvaluateLinear(cfg.HiddenScorePurpose.Dish, run, HiddenOffset(run, context, HiddenScorePurpose.Dish));
        }

        public static int PassiveItemHiddenScore(GameRun run, ActionExecutionContext context = null)
        {
            return EvaluateLinear(cfg.HiddenScorePurpose.PassiveItem, run, HiddenOffset(run, context, HiddenScorePurpose.PassiveItem));
        }

        public static int FragmentHiddenScore(GameRun run, ActionExecutionContext context = null)
        {
            return EvaluateLinear(cfg.HiddenScorePurpose.Fragment, run, HiddenOffset(run, context, HiddenScorePurpose.Fragment));
        }

        public static GoldRange GoldRewardRange(GameRun run, ActionExecutionContext context = null)
        {
            return GoldRewardRange(run, context, 0f);
        }

        public static GoldRange GoldRewardRange(GameRun run, ActionExecutionContext context, float hiddenOffset)
        {
            cfg.GoldRewardCurve curve = ResolveGoldCurve(run?.Tables);
            if (run == null || curve == null)
            {
                return new GoldRange(20, 35);
            }

            float totalOffset = hiddenOffset + HiddenOffset(run, context, HiddenScorePurpose.Gold);
            double hidden = curve.BaseValue + run.WeekIndex * curve.PerWeek + run.CurrentDay * curve.PerDay + totalOffset;
            double fluctuation = Math.Max(0, curve.FluctuationPct);
            double minValue = hidden * (1d - fluctuation);
            double maxValue = hidden * (1d + fluctuation);
            return new GoldRange(
                Math.Max(0, (int)Math.Round(minValue, MidpointRounding.AwayFromZero)),
                Math.Max(0, (int)Math.Round(maxValue, MidpointRounding.AwayFromZero)));
        }

        public static int FragmentFallbackGold(GameRun run, ActionExecutionContext context = null)
        {
            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            cfg.GoldRewardCurve curve = tables.TbGoldRewardCurve.GetOrDefault("gold_default");
            if (curve == null)
            {
                return 40;
            }

            int hidden = FragmentHiddenScore(run, context);
            double value = curve.BaseValue + hidden * 0.5f + (run?.WeekIndex ?? 0) * curve.PerWeek;
            return Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero));
        }

        private static int EvaluateLinear(cfg.HiddenScorePurpose purpose, GameRun run, float hiddenOffset)
        {
            cfg.HiddenScoreCurve curve = ResolveCurve(run?.Tables, purpose, run);
            if (curve == null || run == null)
            {
                return 0;
            }

            double value = curve.LinearBase;
            value += run.WeekIndex * curve.WeekCoeff;
            value += run.CurrentDay * curve.DayCoeff;
            value += hiddenOffset;
            return RoundCurveValue(curve, value);
        }

        private static int EvaluateTargetScore(GameRun run, float hiddenOffset, float? dayOverride)
        {
            cfg.HiddenScoreCurve curve = ResolveCurve(run?.Tables, cfg.HiddenScorePurpose.TargetScore, run);
            if (curve == null || run == null)
            {
                return 0;
            }

            float currentDay = dayOverride ?? run.CurrentDay;
            double exponent = curve.ExpConstant;
            exponent += run.WeekIndex * curve.ExpWeekCoeff;
            exponent += currentDay * curve.ExpDayCoeff;
            exponent += hiddenOffset;
            return RoundCurveValue(curve, Math.Exp(exponent));
        }

        private static int RoundCurveValue(cfg.HiddenScoreCurve curve, double value)
        {
            if (double.IsNaN(value))
            {
                return 0;
            }

            double bounded = double.IsInfinity(value) ? int.MaxValue : Math.Min(value, int.MaxValue);
            long rounded = (long)Math.Round(bounded, MidpointRounding.AwayFromZero);
            long roundTo = curve.RoundTo > 0 ? curve.RoundTo : 1;
            rounded = ((rounded + roundTo - 1) / roundTo) * roundTo;
            return (int)Math.Min(rounded, int.MaxValue);
        }

        private static cfg.HiddenScoreCurve ResolveCurve(cfg.Tables tables, cfg.HiddenScorePurpose purpose, GameRun run)
        {
            tables ??= GameApp.Config.Tables;
            cfg.HiddenScoreCurve fallback = null;
            int week = run?.WeekIndex ?? 0;
            foreach (cfg.HiddenScoreCurve curve in tables.TbHiddenScoreCurve.DataList)
            {
                if (curve.Purpose != purpose)
                {
                    continue;
                }

                fallback ??= curve;
                if (!MatchesSegment(curve, week))
                {
                    continue;
                }

                return curve;
            }

            return fallback;
        }

        private static bool MatchesSegment(cfg.HiddenScoreCurve curve, int week)
        {
            if (curve.WeekList.Count > 0 && !curve.WeekList.Contains(week))
            {
                return false;
            }

            return true;
        }

        private static cfg.Food ResolveFood(cfg.Tables tables, cfg.GameAction action)
        {
            return FoodService.Resolve(tables, action);
        }

        private static float HiddenOffset(GameRun run, ActionExecutionContext context, HiddenScorePurpose purpose)
        {
            float offset = FoodHiddenOffset(ResolveFood(run?.Tables, context?.Action), purpose);
            if (run != null)
            {
                offset += new ItemRuntime(run).HiddenScoreOffset(purpose);
                offset += run.EventHiddenScoreOffset(purpose);
            }

            return offset;
        }

        private static float FoodHiddenOffset(cfg.Food food, HiddenScorePurpose purpose)
        {
            if (food == null)
            {
                return 0f;
            }

            return purpose switch
            {
                HiddenScorePurpose.TargetScore => food.TargetScoreHiddenOffset,
                HiddenScorePurpose.Dish => food.DishHiddenOffset,
                HiddenScorePurpose.PassiveItem => food.PassiveItemHiddenOffset,
                HiddenScorePurpose.Fragment => food.FragmentHiddenOffset,
                HiddenScorePurpose.Gold => food.GoldHiddenOffset,
                _ => 0f,
            };
        }

        private static cfg.GoldRewardCurve ResolveGoldCurve(cfg.Tables tables)
        {
            tables ??= GameApp.Config.Tables;
            return (tables ?? GameApp.Config.Tables).TbGoldRewardCurve.GetOrDefault("gold_default");
        }

    }
}
