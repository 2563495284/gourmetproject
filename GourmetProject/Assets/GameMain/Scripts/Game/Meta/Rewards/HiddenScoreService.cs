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

    /// <summary>v2 隐藏分统一入口：基础隐藏分与目标分、奖励池隐藏分、金币上下限派生。</summary>
    public static class HiddenScoreService
    {
        private const string BasePurpose = "Base";
        private const string TargetPurpose = "TargetScore";
        private const string DishPurpose = "Dish";
        private const string PassiveItemPurpose = "PassiveItem";
        private const string ActiveItemPurpose = "ActiveItem";
        private const string FragmentPurpose = "Fragment";

        public static int BaseHiddenScore(GameRun run, ActionExecutionContext context = null)
        {
            return Evaluate(BasePurpose, run, context, 0);
        }

        public static int TargetScore(GameRun run, ActionExecutionContext context = null)
        {
            cfg.Food food = ResolveFood(run?.Tables, context?.Action);
            int derived = Evaluate(TargetPurpose, run, context, BaseHiddenScore(run, context));
            if (food != null && food.TargetScoreMul > 0f)
            {
                derived = (int)Math.Round(derived * food.TargetScoreMul, MidpointRounding.AwayFromZero);
            }

            return Math.Max(1, derived);
        }

        public static int DishHiddenScore(GameRun run, ActionExecutionContext context = null)
        {
            return Evaluate(DishPurpose, run, context, BaseHiddenScore(run, context));
        }

        public static int PassiveItemHiddenScore(GameRun run, ActionExecutionContext context = null)
        {
            return Evaluate(PassiveItemPurpose, run, context, BaseHiddenScore(run, context));
        }

        public static int ActiveItemHiddenScore(GameRun run, ActionExecutionContext context = null)
        {
            return Evaluate(ActiveItemPurpose, run, context, BaseHiddenScore(run, context));
        }

        public static int FragmentHiddenScore(GameRun run, ActionExecutionContext context = null)
        {
            return Evaluate(FragmentPurpose, run, context, BaseHiddenScore(run, context));
        }

        public static GoldRange GoldRewardRange(GameRun run, ActionExecutionContext context = null)
        {
            return GoldRewardRange(run, context, 0);
        }

        public static GoldRange GoldRewardRange(GameRun run, ActionExecutionContext context, int hiddenOffset)
        {
            cfg.Food food = ResolveFood(run?.Tables, context?.Action);
            cfg.GoldRewardCurve curve = ResolveGoldCurve(run?.Tables, food);
            if (run == null || curve == null)
            {
                return new GoldRange(20, 35);
            }

            double minHidden = curve.MinBase + run.WeekIndex * curve.MinPerWeek + run.CurrentDay * curve.MinPerDay + hiddenOffset;
            double maxHidden = curve.MaxBase + run.WeekIndex * curve.MaxPerWeek + run.CurrentDay * curve.MaxPerDay + hiddenOffset;
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
            cfg.GoldRewardCurve curve = tables.TbGoldRewardCurve.GetOrDefault("gold_fragment_convert");
            if (curve == null)
            {
                return 40;
            }

            int hidden = FragmentHiddenScore(run, context);
            double value = curve.MinBase + hidden * 0.5f + (run?.WeekIndex ?? 0) * curve.MinPerWeek;
            return Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero));
        }

        private static int Evaluate(string purpose, GameRun run, ActionExecutionContext context, int baseHidden)
        {
            cfg.HiddenScoreCurve curve = ResolveCurve(run?.Tables, purpose, run, context);
            if (curve == null || run == null)
            {
                return 0;
            }

            double value = curve.BaseValue + baseHidden * curve.BaseMultiplier;
            value += run.WeekIndex * curve.PerWeek;
            value += run.CurrentDay * curve.PerDay;
            value += run.ActionStepIndex * curve.PerStep;
            value += (ResolveFood(run?.Tables, context?.Action)?.HiddenScoreBonus ?? 0);
            value += ItemHiddenBonus(run) * curve.ItemBonusMultiplier;

            int rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
            int roundTo = curve.RoundTo > 0 ? curve.RoundTo : 1;
            rounded = ((rounded + roundTo - 1) / roundTo) * roundTo;
            return Math.Max(curve.MinValue, rounded);
        }

        private static cfg.HiddenScoreCurve ResolveCurve(cfg.Tables tables, string purpose, GameRun run, ActionExecutionContext context)
        {
            tables ??= GameApp.Config.Tables;
            cfg.HiddenScoreCurve fallback = null;
            cfg.HiddenScoreCurve best = null;
            int week = run?.WeekIndex ?? 0;
            int runStep = CurveRunStep(run, context);
            foreach (cfg.HiddenScoreCurve curve in tables.TbHiddenScoreCurve.DataList)
            {
                if (!string.Equals(curve.Purpose, purpose, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                fallback ??= curve;
                if (!MatchesSegment(curve, week, runStep))
                {
                    continue;
                }

                if (best == null || curve.SegmentPriority > best.SegmentPriority)
                {
                    best = curve;
                }
            }

            return best ?? fallback;
        }

        private static bool MatchesSegment(cfg.HiddenScoreCurve curve, int week, int runStep)
        {
            if (curve.MinWeek > 0 && week < curve.MinWeek)
            {
                return false;
            }

            if (curve.MaxWeek > 0 && week > curve.MaxWeek)
            {
                return false;
            }

            if (curve.MinRunStep > 0 && runStep < curve.MinRunStep)
            {
                return false;
            }

            if (curve.MaxRunStep > 0 && runStep > curve.MaxRunStep)
            {
                return false;
            }

            return true;
        }

        private static int CurveRunStep(GameRun run, ActionExecutionContext context)
        {
            if (context != null)
            {
                return context.RunStepIndex + 1;
            }

            return Math.Max(1, run?.RunActionStepIndex ?? 0);
        }

        private static cfg.Food ResolveFood(cfg.Tables tables, cfg.GameAction action)
        {
            return FoodService.Resolve(tables, action);
        }

        private static cfg.GoldRewardCurve ResolveGoldCurve(cfg.Tables tables, cfg.Food food)
        {
            string curveId = food?.GoldCurveId;
            if (string.IsNullOrEmpty(curveId))
            {
                curveId = "gold_normal";
            }

            tables ??= GameApp.Config.Tables;
            return (tables ?? GameApp.Config.Tables).TbGoldRewardCurve.GetOrDefault(curveId);
        }

        private static float ItemHiddenBonus(GameRun run)
        {
            // 隐藏分加成改由被动道具模型钩子提供（不再按 effectType 字符串判定）。
            return run != null ? new ItemRuntime(run).HiddenScoreBonus() : 0f;
        }
    }
}
