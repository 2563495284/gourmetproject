using GourmetProject.Core.Rng;
using GourmetProject.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>过关奖励：先按配置生成候选，玩家确认后再应用到运行状态。</summary>
    public static class RewardGranter
    {
        private const string Tag = "Reward";

        public static RewardOffer GenerateOffer(GameRun run, cfg.Week week, IRandomStream rng)
        {
            return GenerateOffer(run, week, rng, null);
        }

        public static RewardOffer GenerateOffer(GameRun run, cfg.Week week, IRandomStream rng, ActionExecutionContext actionContext)
        {
            cfg.Week effectiveWeek = ResolveWeek(run, week);
            cfg.RewardPackage package = ResolvePackage(run, effectiveWeek, actionContext);
            if (package == null)
            {
                Log.Warning("Missing reward package. Falling back to gold-only reward.", Tag);
                return new RewardOffer(30, null, null);
            }

            GoldRange goldRange = HiddenScoreService.GoldRewardRange(run, actionContext, package);
            int baseGold = rng.Range(goldRange.Min, goldRange.Max + 1);
            var context = new RewardContext(GameApp.Config.Tables, run, effectiveWeek, package, rng, actionContext);
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

            var lines = new System.Collections.Generic.List<string>
            {
                ApplyBaseGold(run, offer)
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

            return "过关奖励：" + string.Join("。", lines);
        }

        public static string ApplyBaseGold(GameRun run, RewardOffer offer)
        {
            if (run == null || offer == null || offer.BaseGoldClaimed)
            {
                return string.Empty;
            }

            // 美食奖励金币按道具修正（利润提成 / 克扣工钱）。
            var itemRuntime = new ItemRuntime(run);
            int gold = itemRuntime.ModifyMealRewardGold(offer.BaseGold);

            // 美食分红（GoldMealBonus）：剩余生效局数内每局额外金币，并消耗一局额度。
            if (run.MealBonusRemaining > 0)
            {
                gold += itemRuntime.MealBonusGoldPerMeal();
                run.ConsumeMealBonusMeal();
            }

            run.Gold += gold;

            // 「分数变1」按局递减：普通/超级美食奖励结算视为一局（Boss/盛宴不走此路径）。
            run.ConsumeScoreToOneMeal();

            offer.MarkBaseGoldClaimed();
            return $"金币 +{gold}";
        }

        public static string ApplyChoice(GameRun run, RewardChoice choice)
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
                    return ApplyFragmentPack(run, new[] { choice });
                default:
                    return string.Empty;
            }
        }

        public static bool ApplyDishChoiceToBook(GameRun run, RewardChoice choice, int bookIndex)
        {
            if (run == null || choice == null || choice.Kind != cfg.RewardKind.DishChoice)
            {
                return false;
            }

            return run.AddBonusDishToBook(choice.Id, bookIndex);
        }

        public static string ApplyFragmentPack(GameRun run, System.Collections.Generic.IReadOnlyList<RewardChoice> choices)
        {
            if (run == null || choices == null || choices.Count == 0)
            {
                return string.Empty;
            }

            var ids = new System.Collections.Generic.List<string>(choices.Count);
            for (int i = 0; i < choices.Count; i++)
            {
                RewardChoice choice = choices[i];
                if (choice != null && choice.Kind == cfg.RewardKind.FragmentChoice && !string.IsNullOrEmpty(choice.Id))
                {
                    ids.Add(choice.Id);
                }
            }

            if (ids.Count == 0)
            {
                return string.Empty;
            }

            run.SetPendingFragmentPack(ids);
            return ids.Count > 1 ? $"获得胃部碎片包：{ids.Count} 选 1" : "获得胃部碎片包";
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

        private static cfg.RewardPackage ResolvePackage(GameRun run, cfg.Week week, ActionExecutionContext actionContext)
        {
            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            string packageId = FoodService.Resolve(tables, actionContext?.Action)?.RewardPackageId;
            if (!string.IsNullOrEmpty(packageId))
            {
                cfg.RewardPackage actionPackage = tables.TbRewardPackage.GetOrDefault(packageId);
                if (actionPackage != null)
                {
                    return actionPackage;
                }
            }

            return week == null ? null : tables.TbRewardPackage.GetOrDefault(week.RewardPackageId);
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

    }
}
