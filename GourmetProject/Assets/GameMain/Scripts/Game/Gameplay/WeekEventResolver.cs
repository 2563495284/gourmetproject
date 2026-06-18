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
                    int baseReq = run.RequiredScore;
                    run.RequiredScoreOverride = Mathf_RoundToInt(baseReq * (1f - ev.EffectValue));
                    return $"本周目标分降低至 {run.RequiredScoreOverride}。";

                case "GainItem":
                    ItemAcquireResult item = ItemPoolService.GrantRandom(
                        GameApp.Config.Tables,
                        run,
                        cfg.ItemKind.Passive,
                        rng,
                        20);
                    return item.ToRewardText("事件奖励：") + "。";

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

        private static int Mathf_RoundToInt(float v) => (int)System.Math.Round(v, System.MidpointRounding.AwayFromZero);
    }
}
