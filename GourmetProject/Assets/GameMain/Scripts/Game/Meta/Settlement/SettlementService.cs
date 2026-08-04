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
    /// 局外结算：根据运行状态生成游戏通关/失败结算文本（周数、完成星级评鉴、金币、构筑规模、触发事件、新解锁等）。
    /// 结算界面使用这份摘要填充通关结果页或失败确认弹窗。
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
                ? "你的经营方向达到顶峰，餐厅名扬四海！"
                : "游戏失败，餐厅黯然歇业……");
            body.AppendLine();
            body.AppendLine($"周数：第 {statistics.WeekIndex} 周{(statistics.IsEndless ? "（无尽）" : string.Empty)}");
            body.AppendLine($"天数：第 {statistics.CurrentDay} 天");
            body.AppendLine($"总美味值：{statistics.LastTotal} / 目标美味值：{statistics.LastTarget}");
            body.AppendLine($"完成星级评鉴：{statistics.CompletedBossIds.Count} 次{BossNames(run, statistics.CompletedBossIds)}");
            body.AppendLine($"金币：{statistics.Gold}");
            body.AppendLine($"持有装饰品和消耗品：{statistics.OwnedItemCount} 个");
            body.AppendLine($"食谱附加食物：{statistics.BonusDishCount} 个");
            body.AppendLine($"餐桌格：{statistics.StomachFragmentCount} 个");
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
                Title = statistics.Won ? "游戏通关！" : "游戏失败……",
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
