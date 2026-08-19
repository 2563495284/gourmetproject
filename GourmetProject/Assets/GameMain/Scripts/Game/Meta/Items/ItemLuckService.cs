using System;
using GourmetProject.Game.Run;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 装饰品运气入口。周数、当周天数与运行时修正先合并，再限制到配置的指数安全区间。
    /// </summary>
    public static class ItemLuckService
    {
        private const string DefaultCurveId = "item_luck_default";

        public static float GetLuck(
            GameRun run,
            ActionExecutionContext context = null,
            float additionalOffset = 0f)
        {
            if (run == null)
            {
                return 0f;
            }

            cfg.Tables tables = run.Tables ?? GameApp.Config.Tables;
            cfg.ItemLuckCurve curve = tables?.TbItemLuckCurve?.GetOrDefault(DefaultCurveId);
            if (curve == null)
            {
                return 0f;
            }

            float luck = curve.BaseValue;
            luck += Math.Max(0, run.WeekIndex - 1) * curve.PerWeek;
            luck += Math.Max(0f, run.CurrentDay - 1f) * curve.PerDay;
            luck += new ItemRuntime(run).ItemLuckOffset();
            luck += run.EventHiddenScoreOffset(HiddenScorePurpose.ItemLuck);
            luck += additionalOffset;

            return ClampLuck(tables, luck);
        }

        public static float ClampLuck(cfg.Tables tables, float luck)
        {
            tables ??= GameApp.Config.Tables;
            cfg.ItemLuckCurve curve = tables?.TbItemLuckCurve?.GetOrDefault(DefaultCurveId);
            if (curve == null)
            {
                return luck;
            }

            float min = Math.Min(curve.MinLuck, curve.MaxLuck);
            float max = Math.Max(curve.MinLuck, curve.MaxLuck);
            return Math.Max(min, Math.Min(max, luck));
        }
    }
}
