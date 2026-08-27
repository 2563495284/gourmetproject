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
            cfg.RewardPackage package = ResolvePackage(run, actionContext);
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
            RewardChoiceGroup specificGroup = RollSlotGroup(context, package.SpecificSlotGroupId);
            RewardOffer offer = new RewardOffer(
                baseGold,
                fixedGroups,
                specificGroup);
            offer = new ItemRuntime(run).ModifyBattleRewardOffer(offer, actionContext, rng);
            TryApplyRewardDouble(run, actionContext, context, package, goldRange, offer);
            return offer;
        }

        private static void TryApplyRewardDouble(
            GameRun run,
            ActionExecutionContext actionContext,
            RewardContext context,
            cfg.RewardPackage package,
            GoldRange goldRange,
            RewardOffer offer)
        {
            if (run == null
                || offer == null
                || context.Rng == null
                || run.NextBusinessRewardDoubleStacks <= 0
                || actionContext?.IsDailyAction != true
                || !IsNextBusinessRewardDoubleEligible(run.Tables, actionContext.Action))
            {
                return;
            }

            if (!run.TryConsumeNextBusinessRewardDoubleStack())
            {
                return;
            }

            RewardDoubleTarget target = (RewardDoubleTarget)(context.Rng.Range(0, 3) + 1);
            var doubledContext = new RewardContext(
                context.Tables,
                context.Run,
                context.Week,
                context.Package,
                context.Rng,
                context.BaseGold,
                context.ActionContext,
                context.Progress,
                consumeEventChoiceCountDelta: false,
                applyChoiceCountModifiers: false);

            switch (target)
            {
                case RewardDoubleTarget.BaseDish:
                    offer.ConfigureDoubleReward(target);
                    foreach (string groupId in FixedSlotGroupIds(package))
                    {
                        AddDoubledGroup(offer, RollSlotGroup(doubledContext, groupId));
                    }
                    break;
                case RewardDoubleTarget.BaseGold:
                    int rawBonusGold = context.Rng.Range(goldRange.Min, goldRange.Max + 1);
                    offer.ConfigureDoubleReward(target, rawBonusGold);
                    ResolveBusinessGoldAmounts(run, offer, actionContext);
                    break;
                case RewardDoubleTarget.Specific:
                    offer.ConfigureDoubleReward(target);
                    AddDoubledGroup(offer, RollSlotGroup(doubledContext, package?.SpecificSlotGroupId));
                    break;
            }
        }

        private static void AddDoubledGroup(RewardOffer offer, RewardChoiceGroup doubled)
        {
            if (offer == null || doubled == null || !doubled.HasChoices)
            {
                return;
            }

            offer.AddFixedGroup(new RewardChoiceGroup(
                string.IsNullOrWhiteSpace(doubled.Title) ? "翻倍奖励" : $"翻倍奖励 · {doubled.Title}",
                doubled.Choices,
                doubled.RequiredChoiceCount,
                description: doubled.Description,
                ruleText: doubled.RuleText,
                sourceSlotId: doubled.SourceSlotId));
        }

        internal static bool IsNextBusinessRewardDoubleEligible(
            cfg.Tables tables,
            cfg.GameAction action)
        {
            cfg.Food food = FoodService.Resolve(tables, action);
            return food != null
                && (food.ActionKind == cfg.FoodActionKind.Normal
                    || food.ActionKind == cfg.FoodActionKind.Super);
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
            RewardChoiceGroup group = RollSlotGroup(context, slotGroupId);
            return group != null && group.HasChoices
                ? new RewardOffer(0, new[] { group }, null, baseGoldClaimed: true)
                : null;
        }

        /// <summary>
        /// 从已经选定的 reward_slot 构建奖励。抽奖机先把空奖与各槽放在同一个权重池中，
        /// 因而必须复用已经命中的槽，避免在组内二次随机。
        /// </summary>
        public static RewardOffer BuildConfigOffer(
            GameRun run,
            IRandomStream rng,
            cfg.RewardSlot slot,
            ActionExecutionContext actionContext = null)
        {
            if (run == null || rng == null || slot == null)
            {
                return null;
            }

            var context = new RewardContext(
                run.Tables,
                run,
                null,
                null,
                rng,
                0,
                actionContext,
                consumeEventChoiceCountDelta: false);
            System.Collections.Generic.List<RewardChoice> choices = RewardPoolService.RollChoices(context, slot);
            int requiredPickCount = choices.Count > 0
                ? System.Math.Min(choices.Count, System.Math.Max(1, slot.RequiredPickCount))
                : 0;
            if (requiredPickCount > 0 && slot.Kind == cfg.RewardKind.DishChoice)
            {
                requiredPickCount = System.Math.Min(
                    choices.Count,
                    System.Math.Max(1, requiredPickCount + new ItemRuntime(run).ChoiceTimesBonus()));
            }

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
            return RollSlotGroup(context, slotGroupId);
        }

        /// <summary>
        /// 组合式奖励 offer：固定金币 + 若干固定槽组 + 一个特定槽组（如全家福 = 金币 + 被动 + 食物）。全部空则返回 null。
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
                    RewardChoiceGroup group = RollSlotGroup(context, fixedSlotGroupIds[i]);
                    if (group != null && group.HasChoices)
                    {
                        fixedGroups.Add(group);
                    }
                }
            }

            RewardChoiceGroup specificGroup = RollSlotGroup(context, specificSlotGroupId);

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
                    return "食物";
                case cfg.RewardKind.PassiveItemChoice:
                    return "装饰品";
                case cfg.RewardKind.ActiveItemGrant:
                case cfg.RewardKind.ActiveItemStrengthen:
                case cfg.RewardKind.ActiveItemAdjust:
                    return "消耗品";
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

            string doubledGoldText = ApplyBonusGold(run, offer);
            if (!string.IsNullOrEmpty(doubledGoldText))
            {
                lines.Add(doubledGoldText);
            }

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

            if (offer.GoldAmountsResolved)
            {
                run.Gold += offer.BaseGold;
                offer.MarkBaseGoldClaimed();
                return $"金币 +{offer.BaseGold}";
            }

            ResolveBusinessGoldAmounts(run, offer);
            run.Gold += offer.BaseGold;
            offer.MarkBaseGoldClaimed();
            return $"金币 +{offer.BaseGold}";
        }

        public static string ApplyBonusGold(GameRun run, RewardOffer offer)
        {
            if (run == null || offer == null || !offer.HasBonusGold || offer.BonusGoldClaimed)
            {
                return string.Empty;
            }

            if (!offer.GoldAmountsResolved)
            {
                ResolveBusinessGoldAmounts(run, offer);
            }

            run.Gold += offer.BonusGold;
            offer.MarkBonusGoldClaimed();
            return $"翻倍金币 +{offer.BonusGold}";
        }

        private static void ResolveBusinessGoldAmounts(
            GameRun run,
            RewardOffer offer,
            ActionExecutionContext actionContext = null)
        {
            if (run == null || offer == null || offer.GoldAmountsResolved)
            {
                return;
            }

            // 营业基础金币按装饰品和消耗品修正（利润提成 / 克扣工钱）；星级评鉴和其它奖励不属于营业。
            var itemRuntime = new ItemRuntime(run);
            cfg.Food food = FoodService.Resolve(
                run.Tables,
                (actionContext ?? run.LastActionContext)?.Action);
            bool isBusiness = IsNextBusinessRewardDoubleEligible(
                run.Tables,
                (actionContext ?? run.LastActionContext)?.Action);
            bool isBoss = food != null && food.ActionKind == cfg.FoodActionKind.Feast;
            float itemMultiplier = isBusiness ? itemRuntime.MealRewardGoldMultiplier() : 1f;
            float eventMultiplier = isBusiness
                ? run.CurrentWeekBusinessGoldMultiplier * run.ConsumeNextBusinessGoldMultiplier()
                : (isBoss ? run.BossBaseGoldMultiplier : 1f);
            int gold = (int)System.Math.Round(
                offer.RawBaseGold * itemMultiplier * eventMultiplier,
                System.MidpointRounding.AwayFromZero);
            gold = System.Math.Max(0, gold);
            int bonusGold = offer.HasBonusGold
                ? (int)System.Math.Round(
                    offer.RawBonusGold * itemMultiplier * eventMultiplier,
                    System.MidpointRounding.AwayFromZero)
                : 0;
            bonusGold = System.Math.Max(0, bonusGold);
            if (isBusiness && System.Math.Abs(itemMultiplier - 1f) > 0.0001f)
            {
                itemRuntime.FlashTriggered(m => System.Math.Abs(m.MealRewardGoldPct()) > 0.0001f);
            }

            int eventBonusGold = run.ConsumeNextMealRewardGold();
            if (eventBonusGold != 0)
            {
                gold += eventBonusGold;
            }

            offer.LockGoldAmounts(gold, bonusGold);
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
                    return added ? $"食物加入食谱：{choice.Name}" : $"食物折算失败：{choice.Name}";
                case cfg.RewardKind.PassiveItemChoice:
                case cfg.RewardKind.ActiveItemGrant:
                case cfg.RewardKind.ActiveItemStrengthen:
                case cfg.RewardKind.ActiveItemAdjust:
                    return run.AcquireItem(choice.Id, choice.GoldAmount > 0 ? choice.GoldAmount : 40).ToRewardText(string.Empty);
                case cfg.RewardKind.FragmentChoice:
                    return ApplyFragmentPack(run, new[] { choice });
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// 玩家在领奖界面主动确认候选时使用。主动道具栏已满时必须保留候选，
        /// 不能沿用通用获得入口的折金币兜底，否则 UI 会把未入栏的道具误记为已领取。
        /// </summary>
        public static bool TryClaimChoice(GameRun run, RewardChoice choice, out string rewardText)
        {
            rewardText = string.Empty;
            if (run == null || choice == null)
            {
                return false;
            }

            if (IsActiveItemReward(choice.Kind) && !run.HasFreeActiveSlot)
            {
                return false;
            }

            rewardText = ApplyChoice(run, choice);
            return true;
        }

        private static bool IsActiveItemReward(cfg.RewardKind kind)
        {
            return kind == cfg.RewardKind.ActiveItemGrant
                || kind == cfg.RewardKind.ActiveItemStrengthen
                || kind == cfg.RewardKind.ActiveItemAdjust;
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
            var rotations = new System.Collections.Generic.List<int>(choices.Count);
            for (int i = 0; i < choices.Count; i++)
            {
                RewardChoice choice = choices[i];
                if (choice != null && choice.Kind == cfg.RewardKind.FragmentChoice && !string.IsNullOrEmpty(choice.Id))
                {
                    ids.Add(choice.Id);
                    rotations.Add(choice.FragmentRotation);
                }
            }

            if (ids.Count == 0)
            {
                return string.Empty;
            }

            run.SetPendingFragmentPack(ids, rotations);
            return ids.Count > 1 ? $"获得餐桌格包：{ids.Count} 选 1" : "获得餐桌格包";
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

            return run.TotalWeeks > 0 ? run.Tables?.TbWeek?.GetOrDefault(run.TotalWeeks) : null;
        }

        private static cfg.RewardPackage ResolvePackage(GameRun run, ActionExecutionContext actionContext)
        {
            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            string packageId = FoodService.Resolve(tables, actionContext?.Action)?.RewardPackageId;
            return string.IsNullOrEmpty(packageId) ? null : tables.TbRewardPackage.GetOrDefault(packageId);
        }

        private static RewardChoiceGroup RollSlotGroup(RewardContext context, string groupId)
        {
            var slots = new System.Collections.Generic.List<cfg.RewardSlot>();
            if (string.IsNullOrEmpty(groupId))
            {
                return null;
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
                return null;
            }

            var weights = new System.Collections.Generic.List<float>(slots.Count);
            for (int i = 0; i < slots.Count; i++)
            {
                float defaultWeight = System.Math.Max(float.Epsilon, context.Tables.TbGameBase.DefaultRandomWeight);
                weights.Add(slots[i].Weight > 0f ? slots[i].Weight : defaultWeight);
            }

            cfg.RewardSlot chosen = slots[context.Rng.WeightedPickIndex(weights)];
            System.Collections.Generic.List<RewardChoice> choices = RewardPoolService.RollChoices(context, chosen);
            int requiredPickCount = choices.Count > 0
                ? System.Math.Min(choices.Count, System.Math.Max(1, chosen.RequiredPickCount))
                : 0;
            if (requiredPickCount > 0
                && context.ApplyChoiceCountModifiers
                && chosen.Kind == cfg.RewardKind.DishChoice
                && context.Run != null)
            {
                requiredPickCount = System.Math.Min(
                    choices.Count,
                    System.Math.Max(1, requiredPickCount + new ItemRuntime(context.Run).ChoiceTimesBonus()));
            }

            return new RewardChoiceGroup(
                string.IsNullOrWhiteSpace(chosen.Name) ? GroupTitleFor(choices) : chosen.Name,
                choices,
                requiredPickCount,
                ruleText: ExpandRuleTemplate(chosen.RuleTemplate, choices, requiredPickCount),
                sourceSlotId: chosen.Id);
        }

        private static System.Collections.Generic.List<RewardChoiceGroup> RollFixedGroups(RewardContext context, cfg.RewardPackage package)
        {
            var result = new System.Collections.Generic.List<RewardChoiceGroup>();
            foreach (string groupId in FixedSlotGroupIds(package))
            {
                RewardChoiceGroup group = RollSlotGroup(context, groupId);
                if (group != null && group.HasChoices)
                {
                    result.Add(group);
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

        private static string ExpandRuleTemplate(
            string template,
            System.Collections.Generic.IReadOnlyList<RewardChoice> choices,
            int requiredPickCount)
        {
            string expanded = string.IsNullOrWhiteSpace(template)
                ? string.Empty
                : template
                    .Replace("{choiceCount}", (choices?.Count ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Replace("{requiredPickCount}", requiredPickCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(expanded) && !expanded.Contains("{") && !expanded.Contains("}"))
            {
                return expanded;
            }

            if (!string.IsNullOrWhiteSpace(template))
            {
                Log.Warning($"Reward rule template contains an unsupported placeholder: '{template}'.", Tag);
            }

            return FallbackRuleText(choices, requiredPickCount);
        }

        private static string FallbackRuleText(
            System.Collections.Generic.IReadOnlyList<RewardChoice> choices,
            int requiredPickCount)
        {
            int count = choices?.Count ?? 0;
            string type = GroupTitleFor(choices);
            if (count == 1 && requiredPickCount == 1)
            {
                return $"随机获得 1 个{type}。";
            }

            return requiredPickCount >= count
                ? $"获得全部 {count} 个{type}。"
                : $"从 {count} 个{type}中选择 {requiredPickCount} 个。";
        }

    }
}
