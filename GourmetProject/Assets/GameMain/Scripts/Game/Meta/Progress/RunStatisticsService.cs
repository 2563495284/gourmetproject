using System.Collections.Generic;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>从单局运行态提取结算统计，供结算页和跨局进度共同使用。</summary>
    public static class RunStatisticsService
    {
        public static RunStatistics Build(GameRun run, bool won, int lastTotal, int lastTarget)
        {
            var statistics = new RunStatistics
            {
                Won = won,
                LastTotal = lastTotal,
                LastTarget = lastTarget,
            };

            if (run == null)
            {
                return statistics;
            }

            statistics.WeekIndex = run.WeekIndex;
            // 统计/解锁条件仍以整天为单位：向上取整为“已进入的第 N 天”。
            statistics.CurrentDay = (int)System.Math.Ceiling((double)run.CurrentDay);
            statistics.IsEndless = run.IsEndless;
            statistics.Gold = run.Gold;
            statistics.OwnedItemCount = run.Items.Count;
            statistics.BonusDishCount = run.BonusDishIds.Count;
            statistics.StomachFragmentCount = run.StomachFragmentIds.Count;
            statistics.TriggeredEventCount = run.UsedEventIds.Count;
            statistics.RunActionStepIndex = run.RunActionStepIndex;
            statistics.CompletedBossIds = new List<string>(run.CompletedBossIds);
            statistics.UsedEventIds = new List<string>(run.UsedEventIds);
            return statistics;
        }
    }
}
