using System.Collections.Generic;
using System.Text;
using GourmetProject.Runtime;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>一次单局结算的归一化数据。</summary>
    public sealed class SettlementSummary
    {
        public bool Won;
        public string Title;
        public string Body;
        public string ButtonLabel;
    }

    /// <summary>
    /// 局外结算：根据运行状态生成胜/负结算文本（周数、击败 Boss、金币、构筑规模、触发事件、新解锁等）。
    /// 当前没有独立结算 prefab，复用 BattleForm 的结果面板展示这段文本。
    /// </summary>
    public static class SettlementService
    {
        public static SettlementSummary Build(GameRun run, bool won, int lastTotal, int lastTarget)
        {
            return Build(run, won, lastTotal, lastTarget, null);
        }

        public static SettlementSummary Build(GameRun run, bool won, int lastTotal, int lastTarget, MetaProgressUpdate progressUpdate)
        {
            RunStatistics statistics = progressUpdate?.Statistics ?? RunStatisticsService.Build(run, won, lastTotal, lastTarget);
            var body = new StringBuilder();
            body.AppendLine(statistics.Won
                ? "你征服了最终美食家，餐厅名扬四海！"
                : "美食挑战失败，餐厅黯然歇业…");
            body.AppendLine();
            body.AppendLine($"周数：第 {statistics.WeekIndex} 周{(statistics.IsEndless ? "（无尽）" : string.Empty)}");
            body.AppendLine($"天数：第 {statistics.CurrentDay} 天");
            body.AppendLine($"本场得分：{statistics.LastTotal} / 目标 {statistics.LastTarget}");
            body.AppendLine($"击败 Boss：{statistics.CompletedBossIds.Count} 个{BossNames(run, statistics.CompletedBossIds)}");
            body.AppendLine($"金币：{statistics.Gold}");
            body.AppendLine($"持有道具：{statistics.OwnedItemCount} 件");
            body.AppendLine($"菜谱附加菜品：{statistics.BonusDishCount} 道");
            body.AppendLine($"餐桌碎片：{statistics.StomachFragmentCount} 块");
            body.AppendLine($"触发事件：{statistics.TriggeredEventCount} 种");

            string unlocks = FormatUnlocks(progressUpdate?.NewUnlocks);
            if (!string.IsNullOrEmpty(unlocks))
            {
                body.AppendLine();
                body.AppendLine("新解锁：");
                body.Append(unlocks);
            }

            return new SettlementSummary
            {
                Won = statistics.Won,
                Title = statistics.Won ? "通关！" : "失败…",
                Body = body.ToString().TrimEnd(),
                ButtonLabel = "返回菜单",
            };
        }

        private static string BossNames(GameRun run, IReadOnlyList<string> bossIds)
        {
            if (bossIds == null || bossIds.Count == 0)
            {
                return string.Empty;
            }

            var names = new List<string>(bossIds.Count);
            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            foreach (string bossId in bossIds)
            {
                cfg.Food boss = tables?.TbFood.GetOrDefault(bossId);
                names.Add(boss != null ? boss.Name : bossId);
            }

            return "（" + string.Join("、", names) + "）";
        }

        private static string FormatUnlocks(IReadOnlyList<UnlockEntry> unlocks)
        {
            if (unlocks == null || unlocks.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (UnlockEntry unlock in unlocks)
            {
                if (unlock == null)
                {
                    continue;
                }

                sb.AppendLine($"- {unlock.Kind}：{unlock.Name}");
            }

            return sb.ToString().TrimEnd();
        }
    }
}
