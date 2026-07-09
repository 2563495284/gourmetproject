using GourmetProject.Core.Rng;
using GourmetProject.Runtime;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 局外即时效果结算（金币/道具/降目标/赌博/加菜…）。奖励/负面行动与事件选项共用，避免规则漂移。
    /// 只处理「即时」类 <see cref="cfg.EffectType"/>；跟进类（FoodBattle/Shop/GameOver/Victory）由
    /// <see cref="EventService"/> 转成 <see cref="EventResolveResult"/> 的后续动作，不在这里结算。
    /// 数值为占位经济，可在配置中调整。返回给玩家看的反馈文案。
    /// </summary>
    public static class EffectResolver
    {
        public static string Apply(GameRun run, cfg.EffectType effectType, float effectValue, string effectParam, IRandomStream rng)
        {
            if (run == null)
            {
                return string.Empty;
            }

            int value = (int)effectValue;
            switch (effectType)
            {
                case cfg.EffectType.GainGold:
                    run.Gold = System.Math.Max(0, run.Gold + value);
                    return value >= 0 ? $"获得金币 {value}。" : $"失去金币 {-value}。";

                case cfg.EffectType.LowerReq:
                    int baseReq = run.RequiredScore;
                    run.RequiredScoreOverride = RoundToInt(baseReq * (1f - effectValue));
                    return $"本周目标分降低至 {run.RequiredScoreOverride}。";

                case cfg.EffectType.GainItem:
                    cfg.ItemKind kind = effectParam == "Active" ? cfg.ItemKind.Active : cfg.ItemKind.Passive;
                    ItemAcquireResult item = ItemPoolService.GrantRandom(GameApp.Config.Tables, run, kind, rng, 20);
                    return item.ToRewardText("获得：") + "。";

                case cfg.EffectType.Gamble:
                    if (rng.NextBool())
                    {
                        run.Gold += value * 2;
                        return $"豪赌成功！金币 +{value * 2}。";
                    }

                    run.Gold = System.Math.Max(0, run.Gold - value);
                    return $"豪赌失败…金币 -{value}。";

                case cfg.EffectType.AddDish:
                case cfg.EffectType.UpgradeDish:
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
