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

            string unlocks = NewUnlocks(run, won);
            if (!string.IsNullOrEmpty(unlocks))
            {
                body.AppendLine();
                body.AppendLine("新解锁 / 新发现：");
                body.Append(unlocks);
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
            cfg.Tables tables = run.Tables ?? GameApp.Config.Tables;
            foreach (string bossId in run.CompletedBossIds)
            {
                cfg.Boss boss = tables.TbBoss.GetOrDefault(bossId);
                names.Add(boss != null ? boss.Name : bossId);
            }

            return "（" + string.Join("、", names) + "）";
        }

        private static int CountOwnedItems(GameRun run)
        {
            return run.Items.Count;
        }

        /// <summary>
        /// 当前还没有跨局解锁表，这里先把本局达成项整理为结算展示；
        /// 后续接入持久化解锁时，可把这些达成项改为真正的 unlock id。
        /// </summary>
        private static string NewUnlocks(GameRun run, bool won)
        {
            var unlocks = new List<string>();
            if (won)
            {
                unlocks.Add("无尽模式入口");
                unlocks.Add("胜利结算图鉴记录");
            }

            cfg.Tables tables = run.Tables ?? GameApp.Config.Tables;
            foreach (string bossId in run.CompletedBossIds)
            {
                cfg.Boss boss = tables.TbBoss.GetOrDefault(bossId);
                unlocks.Add($"Boss 图鉴：{(boss != null ? boss.Name : bossId)}");
            }

            if (run.WeekIndex >= 4)
            {
                unlocks.Add("困难美食行动池记录");
            }

            if (run.BonusDishIds.Count > 0)
            {
                unlocks.Add($"菜谱扩展记录 x{run.BonusDishIds.Count}");
            }

            if (run.StomachFragmentIds.Count > 0)
            {
                unlocks.Add($"胃部碎片记录 x{run.StomachFragmentIds.Count}");
            }

            if (run.UsedEventIds.Count > 0)
            {
                unlocks.Add($"事件图鉴记录 x{run.UsedEventIds.Count}");
            }

            if (unlocks.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (string unlock in unlocks)
            {
                sb.AppendLine($"- {unlock}");
            }

            return sb.ToString().TrimEnd();
        }
    }
}
