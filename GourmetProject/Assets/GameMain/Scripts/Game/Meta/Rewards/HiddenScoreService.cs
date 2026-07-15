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
        ActiveItem,
        Fragment,
        Gold,
    }

    /// <summary>v2 隐藏分统一入口：目标分、奖励池隐藏分、金币上下限派生。</summary>
    public static class HiddenScoreService
    {
        private const string TargetPurpose = "TargetScore";
        private const string DishPurpose = "Dish";
        private const string PassiveItemPurpose = "PassiveItem";
        private const string ActiveItemPurpose = "ActiveItem";
        private const string FragmentPurpose = "Fragment";

        public static int TargetScore(GameRun run, ActionExecutionContext context = null)
        {
            int derived = EvaluateTargetScore(run, HiddenOffset(run, context, HiddenScorePurpose.TargetScore));
            return Math.Max(1, derived);
        }

        public static int DishHiddenScore(GameRun run, ActionExecutionContext context = null)
        {
            return EvaluateLinear(DishPurpose, run, HiddenOffset(run, context, HiddenScorePurpose.Dish));
        }

        public static int PassiveItemHiddenScore(GameRun run, ActionExecutionContext context = null)
        {
            return EvaluateLinear(PassiveItemPurpose, run, HiddenOffset(run, context, HiddenScorePurpose.PassiveItem));
        }

        public static int ActiveItemHiddenScore(GameRun run, ActionExecutionContext context = null)
        {
            return EvaluateLinear(ActiveItemPurpose, run, HiddenOffset(run, context, HiddenScorePurpose.ActiveItem));
        }

        public static int FragmentHiddenScore(GameRun run, ActionExecutionContext context = null)
        {
            return EvaluateLinear(FragmentPurpose, run, HiddenOffset(run, context, HiddenScorePurpose.Fragment));
        }

        public static GoldRange GoldRewardRange(GameRun run, ActionExecutionContext context = null)
        {
            return GoldRewardRange(run, context, 0);
        }

        public static GoldRange GoldRewardRange(GameRun run, ActionExecutionContext context, int hiddenOffset)
        {
            cfg.GoldRewardCurve curve = ResolveGoldCurve(run?.Tables);
            if (run == null || curve == null)
            {
                return new GoldRange(20, 35);
            }

            int totalOffset = hiddenOffset + HiddenOffset(run, context, HiddenScorePurpose.Gold);
            double minHidden = curve.MinBase + run.WeekIndex * curve.MinPerWeek + run.CurrentDay * curve.MinPerDay + totalOffset;
            double maxHidden = curve.MaxBase + run.WeekIndex * curve.MaxPerWeek + run.CurrentDay * curve.MaxPerDay + totalOffset;
            double fluctuation = Math.Max(0, curve.FluctuationPct);
            double minValue = minHidden * (1d - fluctuation);
            double maxValue = maxHidden * (1d + fluctuation);
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
            double value = curve.MinBase + hidden * 0.5f + (run?.WeekIndex ?? 0) * curve.MinPerWeek;
            return Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero));
        }

        private static int EvaluateLinear(string purpose, GameRun run, int hiddenOffset)
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

        private static int EvaluateTargetScore(GameRun run, int hiddenOffset)
        {
            cfg.HiddenScoreCurve curve = ResolveCurve(run?.Tables, TargetPurpose, run);
            if (curve == null || run == null)
            {
                return 0;
            }

            double exponent = curve.ExpConstant;
            exponent += run.WeekIndex * curve.ExpWeekCoeff;
            exponent += run.CurrentDay * curve.ExpDayCoeff;
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

        private static cfg.HiddenScoreCurve ResolveCurve(cfg.Tables tables, string purpose, GameRun run)
        {
            tables ??= GameApp.Config.Tables;
            cfg.HiddenScoreCurve fallback = null;
            int week = run?.WeekIndex ?? 0;
            foreach (cfg.HiddenScoreCurve curve in tables.TbHiddenScoreCurve.DataList)
            {
                if (!string.Equals(curve.Purpose, purpose, StringComparison.OrdinalIgnoreCase))
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

        private static int HiddenOffset(GameRun run, ActionExecutionContext context, HiddenScorePurpose purpose)
        {
            int offset = FoodHiddenOffset(ResolveFood(run?.Tables, context?.Action), purpose);
            if (run != null)
            {
                offset += (int)Math.Round(new ItemRuntime(run).HiddenScoreOffset(purpose), MidpointRounding.AwayFromZero);
            }

            return offset;
        }

        private static int FoodHiddenOffset(cfg.Food food, HiddenScorePurpose purpose)
        {
            if (food == null)
            {
                return 0;
            }

            switch (purpose)
            {
                case HiddenScorePurpose.TargetScore:
                    return food.TargetScoreHiddenOffset;
                case HiddenScorePurpose.Dish:
                    return food.DishHiddenOffset;
                case HiddenScorePurpose.PassiveItem:
                    return food.PassiveItemHiddenOffset;
                case HiddenScorePurpose.ActiveItem:
                    return food.ActiveItemHiddenOffset;
                case HiddenScorePurpose.Fragment:
                    return food.FragmentHiddenOffset;
                case HiddenScorePurpose.Gold:
                    return food.GoldHiddenOffset;
                default:
                    return 0;
            }
        }

        private static cfg.GoldRewardCurve ResolveGoldCurve(cfg.Tables tables)
        {
            tables ??= GameApp.Config.Tables;
            return (tables ?? GameApp.Config.Tables).TbGoldRewardCurve.GetOrDefault("gold_default");
        }

    }
}
