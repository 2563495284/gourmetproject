using GourmetProject.Core.Rng;
using GourmetProject.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Model;

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
                return new RewardOffer(
                    30,
                    (System.Collections.Generic.IReadOnlyList<RewardChoice>)null,
                    (System.Collections.Generic.IReadOnlyList<RewardChoice>)null);
            }

            GoldRange goldRange = HiddenScoreService.GoldRewardRange(run, actionContext);
            int baseGold = rng.Range(goldRange.Min, goldRange.Max + 1);
            var context = new RewardContext(run.Tables, run, effectiveWeek, package, rng, baseGold, actionContext);
            System.Collections.Generic.List<RewardChoiceGroup> fixedGroups = RollFixedGroups(context, package);
            System.Collections.Generic.List<RewardChoice> specificChoices = RollSlotGroup(context, package.SpecificSlotGroupId, out int specificPickCount);
            RewardOffer offer = new RewardOffer(
                baseGold,
                fixedGroups,
                new RewardChoiceGroup("特定奖励", specificChoices, specificPickCount));
            return new ItemRuntime(run).ModifyBattleRewardOffer(offer, actionContext, rng);
        }

        /// <summary>
        /// 统一奖励入口：按 reward_slot 槽组配置 roll 出一份「纯领取」奖励 offer（不含金币），供事件 / 被动 OnAcquire 走通用领奖队列。
        /// 无候选返回 null（调用方按需折金币兜底）。
        /// </summary>
        public static RewardOffer BuildConfigOffer(GameRun run, IRandomStream rng, string slotGroupId, ActionExecutionContext actionContext = null)
        {
            if (run == null || rng == null || string.IsNullOrEmpty(slotGroupId))
            {
                return null;
            }

            var context = new RewardContext(run.Tables, run, null, null, rng, 0, actionContext);
            System.Collections.Generic.List<RewardChoice> choices = RollSlotGroup(context, slotGroupId, out int requiredPickCount);
            return choices.Count > 0
                ? new RewardOffer(0, choices, null, baseGoldClaimed: true, mainRequiredChoiceCount: requiredPickCount)
                : null;
        }

        public static RewardChoiceGroup BuildConfigChoiceGroup(
            GameRun run,
            IRandomStream rng,
            string slotGroupId,
            string title,
            ActionExecutionContext actionContext = null)
        {
            if (run == null || rng == null || string.IsNullOrEmpty(slotGroupId))
            {
                return null;
            }

            var context = new RewardContext(run.Tables, run, null, null, rng, 0, actionContext);
            System.Collections.Generic.List<RewardChoice> choices = RollSlotGroup(context, slotGroupId, out int requiredPickCount);
            return choices.Count > 0
                ? new RewardChoiceGroup(string.IsNullOrWhiteSpace(title) ? GroupTitleFor(choices) : title, choices, requiredPickCount)
                : null;
        }

        /// <summary>
        /// 组合式奖励 offer：固定金币 + 若干固定槽组 + 一个特定槽组（如全家福 = 金币 + 被动 + 菜品）。全部空则返回 null。
        /// </summary>
        public static RewardOffer BuildConfigOffer(
            GameRun run,
            IRandomStream rng,
            int baseGold,
            System.Collections.Generic.IReadOnlyList<string> fixedSlotGroupIds,
            string specificSlotGroupId,
            ActionExecutionContext actionContext = null)
        {
            if (run == null || rng == null)
            {
                return null;
            }

            var context = new RewardContext(run.Tables, run, null, null, rng, baseGold, actionContext);
            var fixedGroups = new System.Collections.Generic.List<RewardChoiceGroup>();
            if (fixedSlotGroupIds != null)
            {
                for (int i = 0; i < fixedSlotGroupIds.Count; i++)
                {
                    System.Collections.Generic.List<RewardChoice> choices = RollSlotGroup(context, fixedSlotGroupIds[i], out int pick);
                    if (choices.Count > 0)
                    {
                        fixedGroups.Add(new RewardChoiceGroup(GroupTitleFor(choices), choices, pick));
                    }
                }
            }

            System.Collections.Generic.List<RewardChoice> specific = RollSlotGroup(context, specificSlotGroupId, out int specificPick);
            RewardChoiceGroup specificGroup = specific.Count > 0
                ? new RewardChoiceGroup("特定奖励", specific, specificPick)
                : null;

            if (baseGold <= 0 && fixedGroups.Count == 0 && specificGroup == null)
            {
                return null;
            }

            return new RewardOffer(baseGold, fixedGroups, specificGroup);
        }

        private static string GroupTitleFor(System.Collections.Generic.IReadOnlyList<RewardChoice> choices)
        {
            if (choices == null || choices.Count == 0)
            {
                return "奖励";
            }

            switch (choices[0].Kind)
            {
                case cfg.RewardKind.DishChoice:
                    return "菜品";
                case cfg.RewardKind.PassiveItemChoice:
                    return "被动道具";
                case cfg.RewardKind.ActiveItemGrant:
                    return "主动道具";
                case cfg.RewardKind.FragmentChoice:
                    return "格子奖励";
                default:
                    return "奖励";
            }
        }

        public static string Apply(GameRun run, RewardOffer offer, RewardChoice mainChoice, RewardChoice extraChoice)
        {
            return Apply(run, offer, mainChoice, extraChoice, null);
        }

        public static string Apply(GameRun run, RewardOffer offer, RewardChoice mainChoice, RewardChoice extraChoice, RewardChoice bonusChoice)
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

            string bonusText = ApplyChoice(run, bonusChoice);
            if (!string.IsNullOrEmpty(bonusText))
            {
                lines.Add(bonusText);
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
            if (gold != offer.BaseGold)
            {
                itemRuntime.FlashTriggered(m => System.Math.Abs(m.MealRewardGoldPct()) > 0.0001f);
            }

            // 美食分红（GoldMealBonus）：剩余生效局数内每局额外金币，并消耗一局额度。
            if (run.MealBonusRemaining > 0)
            {
                int bonusGold = itemRuntime.MealBonusGoldPerMeal();
                if (bonusGold != 0)
                {
                    itemRuntime.FlashTriggered(m => m.MealBonusGoldPerMeal() != 0);
                }

                gold += bonusGold;
                run.ConsumeMealBonusMeal();
                itemRuntime.RefreshIconState(m => m.MealBonusGoldPerMeal() != 0);
            }

            int eventBonusGold = run.ConsumeNextMealRewardGold();
            if (eventBonusGold != 0)
            {
                gold += eventBonusGold;
            }

            run.Gold += gold;

            // 「分数变1」按局递减：普通/超级美食奖励结算视为一局（Boss/盛宴不走此路径）。
            bool consumedScoreToOne = run.ScoreToOneRemaining > 0;
            run.ConsumeScoreToOneMeal();
            if (consumedScoreToOne)
            {
                itemRuntime.RefreshIconState(m => m.ItemId == "item_score_to_one");
                itemRuntime.RefreshInfoText(m => m.ItemId == "item_score_to_one");
            }

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
                    bool added = string.IsNullOrEmpty(choice.FlavorId)
                        ? run.AddBonusDish(choice.Id)
                        : run.AddBonusDishWithFlavor(choice.Id, choice.FlavorId);
                    return added ? $"菜品加入菜谱池：{choice.Name}" : $"菜品折算失败：{choice.Name}";
                case cfg.RewardKind.PassiveItemChoice:
                case cfg.RewardKind.ActiveItemGrant:
                    return run.AcquireItem(choice.Id, choice.GoldAmount > 0 ? choice.GoldAmount : 40).ToRewardText(string.Empty);
                case cfg.RewardKind.FragmentChoice:
                    return ApplyFragmentPack(run, new[] { choice });
                default:
                    return string.Empty;
            }
        }

        public static bool ApplyDishChoice(GameRun run, RewardChoice choice)
        {
            if (run == null || choice == null || choice.Kind != cfg.RewardKind.DishChoice)
            {
                return false;
            }

            return run.AddBonusDish(choice.Id, choice.FlavorId);
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
            return ids.Count > 1 ? $"获得餐桌碎片包：{ids.Count} 选 1" : "获得餐桌碎片包";
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

        private static System.Collections.Generic.List<RewardChoice> RollSlotGroup(RewardContext context, string groupId, out int requiredPickCount)
        {
            requiredPickCount = 0;
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
                float defaultWeight = System.Math.Max(float.Epsilon, context.Tables.TbGameBase.DefaultRandomWeight);
                weights.Add(slots[i].Weight > 0f ? slots[i].Weight : defaultWeight);
            }

            cfg.RewardSlot chosen = slots[context.Rng.WeightedPickIndex(weights)];
            System.Collections.Generic.List<RewardChoice> choices = RewardPoolService.RollChoices(context, chosen);
            requiredPickCount = choices.Count > 0
                ? System.Math.Min(choices.Count, System.Math.Max(1, chosen.RequiredPickCount))
                : 0;
            if (requiredPickCount > 0 && chosen.Kind == cfg.RewardKind.DishChoice && context.Run != null)
            {
                requiredPickCount = System.Math.Min(
                    choices.Count,
                    System.Math.Max(1, requiredPickCount + new ItemRuntime(context.Run).ChoiceTimesBonus()));
            }

            return choices;
        }

        private static System.Collections.Generic.List<RewardChoiceGroup> RollFixedGroups(RewardContext context, cfg.RewardPackage package)
        {
            var result = new System.Collections.Generic.List<RewardChoiceGroup>();
            foreach (string groupId in FixedSlotGroupIds(package))
            {
                System.Collections.Generic.List<RewardChoice> choices = RollSlotGroup(context, groupId, out int requiredPickCount);
                if (choices.Count > 0)
                {
                    result.Add(new RewardChoiceGroup(FixedGroupTitle(choices), choices, requiredPickCount));
                }
            }

            return result;
        }

        private static System.Collections.Generic.IEnumerable<string> FixedSlotGroupIds(cfg.RewardPackage package)
        {
            if (package == null)
            {
                yield break;
            }

            object value = package.BaseDishSlotGroupId;
            if (value is System.Collections.Generic.IEnumerable<string> ids)
            {
                foreach (string id in ids)
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        yield return id;
                    }
                }

                yield break;
            }

            string single = value as string;
            if (!string.IsNullOrEmpty(single))
            {
                yield return single;
            }
        }

        private static string FixedGroupTitle(System.Collections.Generic.IReadOnlyList<RewardChoice> choices)
        {
            if (choices == null || choices.Count == 0)
            {
                return "固定奖励";
            }

            switch (choices[0].Kind)
            {
                case cfg.RewardKind.DishChoice:
                    return "基础菜品";
                case cfg.RewardKind.Gold:
                    return "金币奖励";
                case cfg.RewardKind.PassiveItemChoice:
                    return "被动道具";
                case cfg.RewardKind.ActiveItemGrant:
                    return "主动道具";
                case cfg.RewardKind.FragmentChoice:
                    return "格子奖励";
                default:
                    return "固定奖励";
            }
        }

    }
}
