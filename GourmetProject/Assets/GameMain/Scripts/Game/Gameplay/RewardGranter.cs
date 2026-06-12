using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Gameplay
{
    /// <summary>过关奖励发放：依据 TbWeek.rewardType 给予金币/道具等。返回反馈文案。</summary>
    public static class RewardGranter
    {
        public static string Grant(GameRun run, cfg.Week week, IRandomStream rng)
        {
            int weekIndex = run.WeekIndex;
            string rewardType = week?.RewardType ?? "Gold";

            switch (rewardType)
            {
                case "Item":
                    string item = GrantRandomPassiveItem(run, rng);
                    return item != null ? $"过关奖励：道具「{item}」" : "过关奖励：金币 +60";

                case "Dish":
                    // 菜品奖励在牌组成长接入前，先折算为金币。
                    run.Gold += 40 + weekIndex * 10;
                    return $"过关奖励：金币 +{40 + weekIndex * 10}（菜品奖励后续接入）";

                case "Boss":
                    run.Gold += 80 + weekIndex * 15;
                    return $"击败 Boss！奖励金币 +{80 + weekIndex * 15}";

                default: // Gold
                    int gold = 50 + weekIndex * 10;
                    run.Gold += gold;
                    return $"过关奖励：金币 +{gold}";
            }
        }

        private static string GrantRandomPassiveItem(GameRun run, IRandomStream rng)
        {
            cfg.Tables tables = GameApp.Config.Tables;
            var candidates = new List<string>();
            foreach (cfg.Item item in tables.TbItem.DataList)
            {
                if (item.Kind == cfg.ItemKind.Passive && !run.ItemIds.Contains(item.Id))
                {
                    candidates.Add(item.Id);
                }
            }

            if (candidates.Count == 0)
            {
                run.Gold += 60;
                return null;
            }

            string picked = candidates[rng.Range(0, candidates.Count)];
            run.ItemIds.Add(picked);
            return tables.TbItem.GetOrDefault(picked)?.Name ?? picked;
        }
    }
}
