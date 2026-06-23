using GourmetProject.Core.Rng;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Gameplay
{
    /// <summary>
    /// 局外即时效果结算（金币/道具/降目标/赌博/加菜…）。行动(Reward/Negative)与事件选项共用，避免规则漂移。
    /// 数值为占位经济，可在配置中调整。返回给玩家看的反馈文案。
    /// </summary>
    public static class EffectResolver
    {
        public static string Apply(GameRun run, string effectType, float effectValue, string effectParam, IRandomStream rng)
        {
            if (run == null || string.IsNullOrEmpty(effectType))
            {
                return string.Empty;
            }

            int value = (int)effectValue;
            switch (effectType)
            {
                case "GainGold":
                    run.Gold += value;
                    return $"获得金币 {value}。";

                case "LowerReq":
                    int baseReq = run.RequiredScore;
                    run.RequiredScoreOverride = RoundToInt(baseReq * (1f - effectValue));
                    return $"本周目标分降低至 {run.RequiredScoreOverride}。";

                case "GainItem":
                    cfg.ItemKind kind = effectParam == "Active" ? cfg.ItemKind.Active : cfg.ItemKind.Passive;
                    ItemAcquireResult item = ItemPoolService.GrantRandom(GameApp.Config.Tables, run, kind, rng, 20);
                    return item.ToRewardText("获得：") + "。";

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
                    // 牌组/菜品成长后续接入，这里先折算为金币占位。
                    run.Gold += System.Math.Max(1, value) * 15;
                    return $"暂以金币 +{System.Math.Max(1, value) * 15} 折算（菜品成长后续接入）。";

                default:
                    return string.Empty;
            }
        }

        private static int RoundToInt(float v) => (int)System.Math.Round(v, System.MidpointRounding.AwayFromZero);
    }
}
