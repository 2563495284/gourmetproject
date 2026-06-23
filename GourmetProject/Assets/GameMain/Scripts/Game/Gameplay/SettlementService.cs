using System.Collections.Generic;
using System.Text;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Gameplay
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
            var body = new StringBuilder();
            body.AppendLine(won
                ? "你征服了最终美食家，餐厅名扬四海！"
                : "美食挑战失败，餐厅黯然歇业…");
            body.AppendLine();
            body.AppendLine($"周数：第 {run.WeekIndex} 周{(run.IsEndless ? "（无尽）" : string.Empty)}");
            body.AppendLine($"本场得分：{lastTotal} / 目标 {lastTarget}");
            body.AppendLine($"击败 Boss：{run.CompletedBossIds.Count} 个{BossNames(run)}");
            body.AppendLine($"金币：{run.Gold}");
            body.AppendLine($"持有道具：{CountOwnedItems(run)} 件");
            body.AppendLine($"菜谱附加菜品：{run.BonusDishIds.Count} 道");
            body.AppendLine($"胃部碎片：{run.StomachFragmentIds.Count} 块");
            body.AppendLine($"触发事件：{run.UsedEventIds.Count} 次（不可重复计）");

            string unlocks = NewUnlocks(run);
            if (!string.IsNullOrEmpty(unlocks))
            {
                body.AppendLine();
                body.AppendLine($"新解锁：{unlocks}");
            }

            return new SettlementSummary
            {
                Won = won,
                Title = won ? "通关！" : "失败…",
                Body = body.ToString().TrimEnd(),
                ButtonLabel = "返回菜单",
            };
        }

        private static string BossNames(GameRun run)
        {
            if (run.CompletedBossIds.Count == 0)
            {
                return string.Empty;
            }

            var names = new List<string>(run.CompletedBossIds.Count);
            foreach (string bossId in run.CompletedBossIds)
            {
                cfg.Boss boss = GameApp.Config.Tables.TbBoss.GetOrDefault(bossId);
                names.Add(boss != null ? boss.Name : bossId);
            }

            return "（" + string.Join("、", names) + "）";
        }

        private static int CountOwnedItems(GameRun run)
        {
            int count = 0;
            foreach (RunItemState state in run.Items)
            {
                if (!state.IsEmpty)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>解锁系统占位：当前没有跨局解锁记录，返回空（后续接入存档解锁表时填充）。</summary>
        private static string NewUnlocks(GameRun run)
        {
            return string.Empty;
        }
    }
}
