using GourmetProject.Core.Rng;
using GourmetProject.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.Gameplay
{
    /// <summary>过关奖励：先按配置生成候选，玩家确认后再应用到运行状态。</summary>
    public static class RewardGranter
    {
        private const string Tag = "Reward";

        public static RewardOffer GenerateOffer(GameRun run, cfg.Week week, IRandomStream rng)
        {
            cfg.Week effectiveWeek = ResolveWeek(run, week);
            cfg.RewardPackage package = ResolvePackage(effectiveWeek);
            if (package == null)
            {
                Log.Warning("Missing reward package. Falling back to gold-only reward.", Tag);
                return new RewardOffer(30, null, null);
            }

            int goldMin = System.Math.Min(package.GoldMin, package.GoldMax);
            int goldMax = System.Math.Max(package.GoldMin, package.GoldMax);
            int baseGold = rng.Range(goldMin, goldMax + 1);
            var context = new RewardContext(GameApp.Config.Tables, run, effectiveWeek, package, rng);
            return new RewardOffer(
                baseGold,
                RollSlotGroup(context, package.MainSlotGroupId),
                rng.NextBool(package.ExtraChance) ? RollSlotGroup(context, package.ExtraSlotGroupId) : null);
        }

        public static string Apply(GameRun run, RewardOffer offer, RewardChoice mainChoice, RewardChoice extraChoice)
        {
            if (run == null || offer == null)
            {
                return string.Empty;
            }

            run.Gold += offer.BaseGold;
            var lines = new System.Collections.Generic.List<string>
            {
                $"金币 +{offer.BaseGold}"
            };

            string mainText = ApplyChoice(run, mainChoice);
            if (!string.IsNullOrEmpty(mainText))
            {
                lines.Add(mainText);
            }

            string extraText = ApplyChoice(run, extraChoice);
            if (!string.IsNullOrEmpty(extraText))
            {
                lines.Add(extraText);
            }

            RunPersistence.Save(run);
            return "过关奖励：" + string.Join("；", lines);
        }

        private static cfg.Week ResolveWeek(GameRun run, cfg.Week week)
        {
            if (week != null)
            {
                return week;
            }

            if (run.CurrentWeek != null)
            {
                return run.CurrentWeek;
            }

            return run.TotalWeeks > 0 ? GameApp.Config.Tables.TbWeek.GetOrDefault(run.TotalWeeks) : null;
        }

        private static cfg.RewardPackage ResolvePackage(cfg.Week week)
        {
            return week == null
                ? null
                : GameApp.Config.Tables.TbRewardPackage.GetOrDefault(week.RewardPackageId);
        }

        private static System.Collections.Generic.List<RewardChoice> RollSlotGroup(RewardContext context, string groupId)
        {
            var slots = new System.Collections.Generic.List<cfg.RewardSlot>();
            if (string.IsNullOrEmpty(groupId))
            {
                return new System.Collections.Generic.List<RewardChoice>();
            }

            foreach (cfg.RewardSlot slot in context.Tables.TbRewardSlot.DataList)
            {
                if (slot.GroupId == groupId)
                {
                    slots.Add(slot);
                }
            }

            if (slots.Count == 0)
            {
                Log.Warning($"Reward slot group '{groupId}' is empty.", Tag);
                return new System.Collections.Generic.List<RewardChoice>();
            }

            var weights = new System.Collections.Generic.List<float>(slots.Count);
            for (int i = 0; i < slots.Count; i++)
            {
                weights.Add(slots[i].Weight > 0f ? slots[i].Weight : 1f);
            }

            cfg.RewardSlot chosen = slots[context.Rng.WeightedPickIndex(weights)];
            return RewardPoolService.RollChoices(context, chosen);
        }

        private static string ApplyChoice(GameRun run, RewardChoice choice)
        {
            if (choice == null)
            {
                return string.Empty;
            }

            if (choice.Kind == cfg.RewardKind.Gold || choice.IsFallbackGold)
            {
                run.Gold += choice.GoldAmount;
                return $"{choice.Name} +{choice.GoldAmount}";
            }

            switch (choice.Kind)
            {
                case cfg.RewardKind.DishChoice:
                    return run.AddBonusDish(choice.Id) ? $"菜品加入菜谱池：{choice.Name}" : $"菜品折算失败：{choice.Name}";
                case cfg.RewardKind.PassiveItemChoice:
                case cfg.RewardKind.ActiveItemGrant:
                    return run.AcquireItem(choice.Id, choice.GoldAmount > 0 ? choice.GoldAmount : 40).ToRewardText(string.Empty);
                case cfg.RewardKind.FragmentChoice:
                    if (run.AddStomachFragment(choice.Id))
                    {
                        return $"获得胃部碎片：{choice.Name}";
                    }

                    run.Gold += 40;
                    return $"胃部碎片已折算：金币 +40";
                default:
                    return string.Empty;
            }
        }
    }
}
