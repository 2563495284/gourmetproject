using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Gameplay
{
    /// <summary>
    /// 三选一事件效果结算。效果数值为占位经济，可后续在 TbEvent 调整。返回给玩家看的反馈文案。
    /// </summary>
    public static class WeekEventResolver
    {
        public static string Apply(GameRun run, cfg.GameEvent ev, IRandomStream rng)
        {
            if (ev == null)
            {
                return string.Empty;
            }

            int value = (int)ev.EffectValue;
            switch (ev.EffectType)
            {
                case "GainGold":
                    run.Gold += value;
                    return $"获得金币 {value}。";

                case "LowerReq":
                    int baseReq = run.CurrentWeek?.RequiredScore ?? 100;
                    run.RequiredScoreOverride = Mathf_RoundToInt(baseReq * (1f - ev.EffectValue));
                    return $"本周目标分降低至 {run.RequiredScoreOverride}。";

                case "GainItem":
                    string granted = GrantRandomPassiveItem(run, rng);
                    return granted != null ? $"获得道具：{ItemName(granted)}。" : "没有可获得的新道具，改为金币 +20。";

                case "Gamble":
                    if (rng.NextBool())
                    {
                        run.Gold += value * 2;
                        return $"豪赌成功！金币 +{value * 2}。";
                    }

                    run.Gold = System.Math.Max(0, run.Gold - value);
                    return $"豪赌失败…金币 -{value}。";

                case "AddDish":
                case "UpgradeDish":
                    // 牌组/菜品成长在后续阶段接入，这里先折算为金币奖励占位。
                    run.Gold += value * 15;
                    return $"暂以金币 +{value * 15} 折算（菜品成长后续接入）。";

                default:
                    return ev.Desc;
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
                run.Gold += 20;
                return null;
            }

            string picked = candidates[rng.Range(0, candidates.Count)];
            run.ItemIds.Add(picked);
            return picked;
        }

        private static string ItemName(string itemId)
        {
            cfg.Item item = GameApp.Config.Tables.TbItem.GetOrDefault(itemId);
            return item?.Name ?? itemId;
        }

        private static int Mathf_RoundToInt(float v) => (int)System.Math.Round(v, System.MidpointRounding.AwayFromZero);
    }
}
