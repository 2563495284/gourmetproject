using System;
using System.Collections.Generic;
using System.Globalization;
using GourmetProject.Core.Rng;
using GourmetProject.Runtime;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 局外即时效果结算（金币/装饰品和消耗品/降目标/赌博/加菜…）。奖励/负面行动与事件选项共用，避免规则漂移。
    /// 只处理「即时」类 <see cref="cfg.EffectType"/>；跟进类（FoodBattle/Shop/GameOver/Victory）由
    /// <see cref="EventService"/> 转成 <see cref="EventResolveResult"/> 的后续动作，不在这里结算。
    /// 数值为占位经济，可在配置中调整。返回给玩家看的反馈文案。
    /// </summary>
    public static class EffectResolver
    {
        public static string Apply(GameRun run, cfg.EffectType effectType, float effectValue, string effectParam, IRandomStream rng)
        {
            return Apply(run, effectType, effectValue, effectParam, rng, out _);
        }

        public static string Apply(
            GameRun run,
            cfg.EffectType effectType,
            float effectValue,
            string effectParam,
            IRandomStream rng,
            out RecipeMutationResult recipeMutation)
        {
            recipeMutation = null;
            if (run == null)
            {
                return string.Empty;
            }

            effectParam = NormalizeParam(effectParam);
            int value = (int)effectValue;
            switch (effectType)
            {
                case cfg.EffectType.GainGold:
                    run.Gold = System.Math.Max(0, run.Gold + value);
                    return value >= 0 ? $"获得金币 {value}。" : $"失去金币 {-value}。";

                case cfg.EffectType.LowerReq:
                    int baseReq = run.RequiredScore;
                    run.RequiredScoreOverride = RoundToInt(baseReq * (1f - effectValue));
                    return $"本周目标美味值降低至 {run.RequiredScoreOverride}。";

                case cfg.EffectType.GainItem:
                    return EnqueueConfigReward(run, rng, effectParam, "获得装饰品和消耗品", 40);

                case cfg.EffectType.Gamble:
                    if (rng.NextBool())
                    {
                        run.Gold += value * 2;
                        return $"豪赌成功！金币 +{value * 2}。";
                    }

                    run.Gold = System.Math.Max(0, run.Gold - value);
                    return $"豪赌失败…金币 -{value}。";

                case cfg.EffectType.AddDish:
                    return EnqueueConfigReward(run, rng, effectParam, "食物奖励", 30);

                case cfg.EffectType.UpgradeDish:
                    // 「提升食物」是改造而非发放，暂折金币占位（不在本次奖励统一范围）。
                    run.Gold += System.Math.Max(1, value) * 15;
                    return $"暂以金币 +{System.Math.Max(1, value) * 15} 折算（食物成长后续接入）。";

                case cfg.EffectType.AddHiddenScoreOffset:
                    return AddHiddenScoreOffset(run, effectValue, effectParam);

                case cfg.EffectType.EnqueueDishChoice:
                    return EnqueueConfigReward(run, rng, effectParam, "食物奖励", 30);

                case cfg.EffectType.EnqueueItemChoice:
                    return EnqueueConfigReward(run, rng, effectParam, "装饰品和消耗品奖励", 40);

                case cfg.EffectType.AddRandomRecipeFlavor:
                    return AddRandomRecipeFlavor(
                        run,
                        rng,
                        System.Math.Max(1, value),
                        out recipeMutation);

                case cfg.EffectType.GainSpecificItem:
                    return EnqueueConfigReward(run, rng, effectParam, "获得装饰品和消耗品", 40);

                case cfg.EffectType.GrantFragmentPack:
                    return EnqueueConfigReward(run, rng, effectParam, "餐桌格", 40);

                case cfg.EffectType.AddShopPricePct:
                    run.AddEventShopPricePct(effectValue);
                    return effectValue >= 0f ? $"商店价格提高 {FormatPct(effectValue)}。" : $"商店价格降低 {FormatPct(-effectValue)}。";

                case cfg.EffectType.AddChoiceCountPenalty:
                    int delta = ParseInt(effectParam, -1);
                    run.AddEventChoiceCountPenalty(System.Math.Max(1, value), delta);
                    return $"后续 {System.Math.Max(1, value)} 次奖励选择数量 {FormatSigned(delta)}。";

                case cfg.EffectType.AddNextFoodTargetOffset:
                    run.AddNextFoodTargetScoreHiddenOffset(effectValue);
                    return $"下一场经营挑战目标美味值隐藏分 {FormatSigned(effectValue)}。";

                case cfg.EffectType.AddNextMealGold:
                    run.AddNextMealRewardGold(value);
                    return $"下一场经营挑战奖励金币 +{value}。";

                case cfg.EffectType.RemoveActiveItemsForGold:
                    return RemoveActiveItemsForGold(run, value);

                case cfg.EffectType.IncrementEventCounter:
                    return IncrementEventCounter(run, value, effectParam);

                case cfg.EffectType.GainRandomFlavoredDishes:
                    return EnqueueConfigReward(run, rng, effectParam, "风味食物", 60);

                case cfg.EffectType.RemoveRandomRecipeDish:
                    return RemoveRandomRecipeDish(run, rng, System.Math.Max(1, value), effectParam);

                case cfg.EffectType.LoseAllGold:
                    int lost = run.Gold;
                    run.Gold = 0;
                    return $"失去所有金币（-{lost}）。";

                case cfg.EffectType.GainLegendaryItem:
                    return EnqueueConfigReward(run, rng, effectParam, "传奇装饰品和消耗品", 80);

                case cfg.EffectType.CollectInterest:
                    return CollectInterest(run, effectParam);

                case cfg.EffectType.AddBusinessGoldPct:
                    if (TryParseNextBusinessCount(effectParam, out int nextBusinessCount))
                    {
                        run.AddBusinessGoldPct(effectValue, nextBusinessCount);
                        return nextBusinessCount == 1
                            ? $"下一次营业基础金币 {FormatMultiplier(effectValue)}。"
                            : $"后续 {nextBusinessCount} 次营业基础金币 {FormatMultiplier(effectValue)}。";
                    }

                    if (string.Equals(effectParam, "CurrentWeek", StringComparison.OrdinalIgnoreCase))
                    {
                        run.AddBusinessGoldPct(effectValue, nextBusiness: false);
                        return $"本周后续营业基础金币 {FormatMultiplier(effectValue)}。";
                    }

                    return $"营业金币效果参数无效：{effectParam}。";

                case cfg.EffectType.RestoreHearts:
                    if (value <= 0)
                    {
                        return "未恢复红心。";
                    }

                    int beforeHearts = run.HeartsRemaining;
                    int afterHearts = run.RestoreHearts(value);
                    int restoredHearts = afterHearts - beforeHearts;
                    return restoredHearts > 0 ? $"恢复{restoredHearts}颗红心。" : "红心已满。";

                case cfg.EffectType.AddAllRecipeScoreFlat:
                    return AddAllRecipeScoreFlat(run, effectValue);

                case cfg.EffectType.AddBossTargetScorePct:
                    run.AddBossTargetScorePct(effectValue);
                    return $"后续星级评鉴目标美味值 {FormatMultiplier(effectValue)}。";

                case cfg.EffectType.AddBossBaseGoldPct:
                    run.AddBossBaseGoldPct(effectValue);
                    return $"后续星级评鉴基础金币 {FormatMultiplier(effectValue)}。";

                case cfg.EffectType.GrantRandomActiveItems:
                    return GrantRandomItems(run, rng, cfg.ItemKind.Active, System.Math.Max(1, value), effectParam);

                case cfg.EffectType.GrantRandomPassiveItems:
                    return GrantRandomItems(run, rng, cfg.ItemKind.Passive, System.Math.Max(1, value), effectParam);

                case cfg.EffectType.LoseEscalatingGold:
                    return LoseEscalatingGold(run, value, effectParam);

                case cfg.EffectType.UiTodo:
                    return string.IsNullOrWhiteSpace(effectParam) ? "TODO: 后续接入 UI 交互。" : effectParam;

                default:
                    return string.Empty;
            }
        }

        private static string CollectInterest(GameRun run, string eventId)
        {
            cfg.GameEvent ev = string.IsNullOrWhiteSpace(eventId)
                ? null
                : run.Tables.TbEvent.GetOrDefault(eventId);
            if (ev == null)
            {
                return $"事件配置缺失：{eventId}";
            }

            int threshold = run.InterestThreshold;
            int goldPer = run.InterestGoldPer > 0 ? run.InterestGoldPer : 1;
            int maxGain = run.InterestCap;
            int gain = TimelineMath.Interest(run.Gold, threshold, goldPer, maxGain);
            run.Gold += gain;

            return (ev.ResultText ?? string.Empty)
                .Replace("{gain}", gain.ToString(CultureInfo.InvariantCulture))
                .Replace("{threshold}", threshold.ToString(CultureInfo.InvariantCulture))
                .Replace("{goldPer}", goldPer.ToString(CultureInfo.InvariantCulture))
                .Replace("{maxGain}", maxGain.ToString(CultureInfo.InvariantCulture))
                .Replace("{currentGold}", run.Gold.ToString(CultureInfo.InvariantCulture));
        }

        private static string AddHiddenScoreOffset(GameRun run, float amount, string param)
        {
            foreach (HiddenScorePurpose purpose in ResolvePurposes(param))
            {
                run.AddEventHiddenScoreOffset(purpose, amount);
            }

            return $"隐藏分修正 {FormatSigned(amount)}（{PurposeLabel(param)}）。";
        }

        private static bool TryParseNextBusinessCount(string param, out int count)
        {
            if (string.Equals(param, "Next", StringComparison.OrdinalIgnoreCase))
            {
                count = 1;
                return true;
            }

            const string prefix = "Next:";
            if (param.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(
                    param.Substring(prefix.Length),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out count)
                && count > 0
                && count <= GameRun.MaxQueuedBusinessGoldEffects)
            {
                return true;
            }

            count = 0;
            return false;
        }

        private static IEnumerable<HiddenScorePurpose> ResolvePurposes(string param)
        {
            string value = (param ?? string.Empty).Trim();
            if (string.Equals(value, "Reward", StringComparison.OrdinalIgnoreCase))
            {
                yield return HiddenScorePurpose.Dish;
                yield return HiddenScorePurpose.PassiveItem;
                yield return HiddenScorePurpose.Fragment;
                yield return HiddenScorePurpose.Gold;
                yield break;
            }

            if (string.Equals(value, "All", StringComparison.OrdinalIgnoreCase))
            {
                yield return HiddenScorePurpose.TargetScore;
                yield return HiddenScorePurpose.Dish;
                yield return HiddenScorePurpose.PassiveItem;
                yield return HiddenScorePurpose.Fragment;
                yield return HiddenScorePurpose.Gold;
                yield break;
            }

            if (Enum.TryParse(value, true, out HiddenScorePurpose parsed))
            {
                yield return parsed;
                yield break;
            }

            yield return HiddenScorePurpose.Dish;
        }

        /// <summary>
        /// 奖励类效果统一入口：effectParam = "奖励槽组id[|标题]"。按配置 roll 出 offer 入通用领奖队列，等同于一次正常领奖。
        /// 无配置 / 无候选时折金币兜底。
        /// </summary>
        private static string EnqueueConfigReward(GameRun run, IRandomStream rng, string param, string defaultTitle, int goldFallback)
        {
            if (rng == null)
            {
                return "奖励生成失败：缺少随机流。";
            }

            ParseGroupAndTitle(param, out string slotGroupId, out string title);
            string rewardTitle = string.IsNullOrWhiteSpace(title) ? defaultTitle : title;
            if (string.IsNullOrEmpty(slotGroupId))
            {
                run.Gold += goldFallback;
                return $"奖励配置缺失，折算金币 +{goldFallback}。";
            }

            RewardOffer offer = RewardGranter.BuildConfigOffer(run, rng, slotGroupId, run.LastActionContext);
            if (offer == null)
            {
                run.Gold += goldFallback;
                return $"奖励池为空，折算金币 +{goldFallback}。";
            }

            run.EnqueueGenericRewardOffer(BuildGenericRewardKey(run, slotGroupId, rewardTitle), rewardTitle, offer);
            return $"获得奖励：{rewardTitle}。";
        }

        /// <summary>解析 "slotGroupId" 或 "slotGroupId|标题"。</summary>
        private static void ParseGroupAndTitle(string param, out string slotGroupId, out string title)
        {
            slotGroupId = string.Empty;
            title = string.Empty;
            param = NormalizeParam(param);
            if (string.IsNullOrWhiteSpace(param))
            {
                return;
            }

            string[] parts = param.Split('|');
            slotGroupId = parts[0].Trim();
            if (parts.Length > 1)
            {
                title = parts[1].Trim();
            }
        }

        private static string AddRandomRecipeFlavor(
            GameRun run,
            IRandomStream rng,
            int count,
            out RecipeMutationResult mutation)
        {
            mutation = new RecipeMutationResult { Title = "添加风味" };
            var emptyFlavorTargets = new List<int>();
            var allTargets = new List<int>();
            IReadOnlyList<RecipeBookSlot> recipe = run.RecipeEntries;
            for (int dishIndex = 0; dishIndex < recipe.Count; dishIndex++)
            {
                allTargets.Add(dishIndex);
                if (run.GetRecipeFlavorIds(dishIndex).Count == 0)
                {
                    emptyFlavorTargets.Add(dishIndex);
                }
            }

            if (allTargets.Count == 0)
            {
                return "食谱为空，无法添加风味。";
            }

            List<FlavorDef> flavors = AllFlavors(run);
            if (flavors.Count == 0)
            {
                return "没有可用风味，无法添加。";
            }

            int applied = 0;
            int targetCount = System.Math.Min(count, allTargets.Count);
            for (int i = 0; i < targetCount && allTargets.Count > 0; i++)
            {
                List<int> pool = emptyFlavorTargets.Count > 0 ? emptyFlavorTargets : allTargets;
                int targetIndex = rng != null ? rng.Range(0, pool.Count) : 0;
                int dishIndex = pool[targetIndex];
                List<FlavorDef> availableFlavors = AvailableFlavors(run, dishIndex, flavors);
                RecipeDishSnapshot before = PassiveRecipeMutationService.Snapshot(run, dishIndex);
                if (availableFlavors.Count > 0
                    && run.AddRecipeFlavor(dishIndex, PickFlavor(availableFlavors, rng).Id))
                {
                    applied++;
                    mutation.Entries.Add(new RecipeMutationEntry
                    {
                        BookIndex = 0,
                        DishIndex = dishIndex,
                        Before = before,
                        After = PassiveRecipeMutationService.Snapshot(run, dishIndex),
                    });
                }

                emptyFlavorTargets.Remove(dishIndex);
                allTargets.Remove(dishIndex);
            }

            return applied > 0 ? $"为食谱中的 {applied} 个食物添加了随机风味。" : "没有食物获得风味。";
        }

        private static List<FlavorDef> AvailableFlavors(GameRun run, int dishIndex, IReadOnlyList<FlavorDef> flavors)
        {
            var available = new List<FlavorDef>();
            IReadOnlyList<string> existing = run.GetRecipeFlavorIds(dishIndex);
            foreach (FlavorDef flavor in flavors)
            {
                bool duplicate = false;
                for (int i = 0; i < existing.Count; i++)
                {
                    if (existing[i] == flavor.Id)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    available.Add(flavor);
                }
            }

            return available;
        }

        private static string AddAllRecipeScoreFlat(GameRun run, float amount)
        {
            int affected = 0;
            int recipeCount = run.RecipeEntries.Count;
            for (int dishIndex = 0; dishIndex < recipeCount; dishIndex++)
            {
                if (run.AddRecipeScoreFlat(dishIndex, amount))
                {
                    affected++;
                }
            }

            return affected > 0
                ? $"触发时已有的 {affected} 个食物永久分数 {FormatSigned(amount)}。"
                : "食谱为空，没有食物获得分数。";
        }

        private static string GrantRandomItems(
            GameRun run,
            IRandomStream rng,
            cfg.ItemKind kind,
            int count,
            string param)
        {
            if (rng == null)
            {
                return "奖励生成失败：缺少随机流。";
            }

            IReadOnlyCollection<string> allowedIds = null;
            cfg.ActiveItemCategory? activeCategory = null;
            bool? requireNegative = null;
            bool withReplacement = false;
            string normalized = NormalizeParam(param);
            if (kind == cfg.ItemKind.Active)
            {
                withReplacement = normalized.IndexOf("Replace", StringComparison.OrdinalIgnoreCase) >= 0;
                if (normalized.IndexOf("Adjust", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    activeCategory = cfg.ActiveItemCategory.Adjust;
                }
                else
                {
                    allowedIds = ParseItemIds(normalized);
                }
            }
            else
            {
                requireNegative = string.Equals(normalized, "Negative", StringComparison.OrdinalIgnoreCase);
            }

            List<ItemDefinition> items = ItemPoolService.RollFiltered(
                run.Tables,
                run,
                kind,
                rng,
                count,
                allowedIds,
                activeCategory,
                requireNegative,
                withReplacement);
            if (items.Count == 0)
            {
                const int fallbackGold = 40;
                run.Gold += fallbackGold;
                return $"奖励池为空，折算金币 +{fallbackGold}。";
            }

            var choices = new List<RewardChoice>(items.Count);
            foreach (ItemDefinition item in items)
            {
                cfg.RewardKind rewardKind = item.IsPassive
                    ? cfg.RewardKind.PassiveItemChoice
                    : (item.ActiveItemCategory == cfg.ActiveItemCategory.Adjust
                        ? cfg.RewardKind.ActiveItemAdjust
                        : cfg.RewardKind.ActiveItemStrengthen);
                choices.Add(new RewardChoice(rewardKind, item.Id, item.Name, item.Desc, goldAmount: 40));
            }

            string title = kind == cfg.ItemKind.Passive ? "装饰品奖励" : "消耗品奖励";
            var group = new RewardChoiceGroup(title, choices, choices.Count, ruleText: "点击领取");
            var offer = new RewardOffer(0, new[] { group }, null, baseGoldClaimed: true);
            run.EnqueueGenericRewardOffer(BuildGenericRewardKey(run, kind.ToString(), title), title, offer);
            return $"获得 {items.Count} 个{(kind == cfg.ItemKind.Passive ? "装饰品" : "消耗品")}。";
        }

        /// <summary>
        /// 直接获得一个随机负面装饰品，不生成通用奖励，也不进入 RewardForm。
        /// 仍复用正常装饰品池的解锁、唯一性、隐藏分和权重规则；空池时直接折算金币。
        /// </summary>
        public static RandomizedItemResult GrantRandomNegativePassiveDirect(
            GameRun run,
            IRandomStream rng,
            int fallbackGold = 40)
        {
            if (run == null || rng == null)
            {
                return null;
            }

            List<ItemDefinition> items = ItemPoolService.RollFiltered(
                run.Tables,
                run,
                cfg.ItemKind.Passive,
                rng,
                1,
                allowedIds: null,
                activeCategory: null,
                requireNegative: true,
                withReplacement: false);
            if (items.Count == 0)
            {
                run.Gold += System.Math.Max(0, fallbackGold);
                return null;
            }

            ItemDefinition item = items[0];
            ItemAcquireResult acquireResult = run.AcquireItem(
                item.Id,
                System.Math.Max(0, fallbackGold));
            return new RandomizedItemResult(item, acquireResult);
        }

        private static IReadOnlyCollection<string> ParseItemIds(string param)
        {
            var ids = new List<string>();
            string[] parts = SplitParamList(param);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.StartsWith("Ids:", StringComparison.OrdinalIgnoreCase))
                {
                    part = part.Substring(4);
                }

                if (part.StartsWith("item_", StringComparison.OrdinalIgnoreCase))
                {
                    ids.Add(part);
                }
            }

            return ids;
        }

        private static string RemoveRandomRecipeDish(GameRun run, IRandomStream rng, int count, string param)
        {
            bool requireFlavor = string.Equals(param, "flavored", StringComparison.OrdinalIgnoreCase);
            int removed = 0;
            for (int i = 0; i < count; i++)
            {
                List<(int DishIndex, string DishName)> targets = RecipeDishTargets(run, requireFlavor);
                if (targets.Count == 0)
                {
                    break;
                }

                (int dishIndex, _) = targets[rng != null ? rng.Range(0, targets.Count) : 0];
                if (run.RemoveBonusDishAt(dishIndex))
                {
                    removed++;
                }
            }

            if (removed <= 0)
            {
                return requireFlavor ? "食谱中没有带风味的食物可献上。" : "食谱为空，无法献上食物。";
            }

            return requireFlavor ? $"随机献上了 {removed} 道带风味的食物。" : $"随机献上了 {removed} 道食物。";
        }

        private static List<(int DishIndex, string DishName)> RecipeDishTargets(GameRun run, bool requireFlavor)
        {
            var targets = new List<(int, string)>();
            if (run == null)
            {
                return targets;
            }

            IReadOnlyList<RecipeBookSlot> recipe = run.RecipeEntries;
            for (int dishIndex = 0; dishIndex < recipe.Count; dishIndex++)
            {
                RecipeBookSlot slot = recipe[dishIndex];
                DishDef dish = run.Database?.GetDish(slot.DishId);
                bool hasFlavor = (dish != null && dish.HasFlavor) || slot.ExtraFlavorIds.Count > 0;
                if (!requireFlavor || hasFlavor)
                {
                    targets.Add((dishIndex, dish?.Name ?? slot.DishId));
                }
            }

            return targets;
        }

        private static string RemoveActiveItemsForGold(GameRun run, int goldPerItem)
        {
            List<string> ids = new List<string>();
            foreach (RunItemState state in run.ActiveItemStates)
            {
                if (state != null && !string.IsNullOrEmpty(state.ItemId))
                {
                    ids.Add(state.ItemId);
                }
            }

            int removed = 0;
            foreach (string id in ids)
            {
                if (run.RemoveItem(id))
                {
                    removed++;
                }
            }

            int gained = removed * System.Math.Max(0, goldPerItem);
            run.Gold += gained;
            return removed > 0 ? $"失去消耗品 {removed} 个，获得金币 {gained}。" : "没有消耗品可献上。";
        }

        private static string IncrementEventCounter(GameRun run, int threshold, string param)
        {
            string[] parts = SplitParamList(param);
            string counterId = parts.Length > 0 ? parts[0] : string.Empty;
            string forcedEventId = parts.Length > 1 ? parts[1] : string.Empty;
            if (forcedEventId == "act_event")
            {
                run.ScheduleEventCounterAfterActEvents(counterId, System.Math.Max(1, threshold));
                return $"砂锅的汤汁慢慢变深，经历 {System.Math.Max(1, threshold)} 次事件行动后会彻底变黑。";
            }

            int current = run.IncrementEventCounter(counterId, System.Math.Max(1, threshold), forcedEventId);
            if (current == 0 && !string.IsNullOrEmpty(forcedEventId))
            {
                return "砂锅的汤汁彻底变黑了，下次事件会出现许愿砂锅II。";
            }

            return $"砂锅反应累计 {current}/{System.Math.Max(1, threshold)}。";
        }

        private static string LoseEscalatingGold(GameRun run, int firstCost, string param)
        {
            string[] parts = SplitParamList(param);
            string counterId = parts.Length > 0 ? parts[0] : "event_escalating_gold";
            int increment = parts.Length > 1 ? ParseInt(parts[1], firstCost) : firstCost;
            firstCost = System.Math.Max(0, firstCost);
            increment = System.Math.Max(0, increment);

            // threshold=0 表示只使用 GameRun 已持久化的通用事件计数，不触发强制事件或封顶。
            int triggerCount = run.IncrementEventCounter(counterId, 0, string.Empty);
            int required = firstCost + increment * System.Math.Max(0, triggerCount - 1);
            int paid = System.Math.Min(run.Gold, required);
            run.Gold -= paid;

            if (paid < required)
            {
                return $"第 {triggerCount} 次中毒，被送往医院抢救；抢救费应为 {required} 金币，现有 {paid} 金币已全部支付。";
            }

            return $"第 {triggerCount} 次中毒，被送往医院抢救，支付 {paid} 金币。";
        }

        private static List<FlavorDef> AllFlavors(GameRun run)
        {
            return run?.Database?.AllFlavors != null
                ? new List<FlavorDef>(run.Database.AllFlavors)
                : new List<FlavorDef>();
        }

        private static FlavorDef PickFlavor(IReadOnlyList<FlavorDef> flavors, IRandomStream rng)
        {
            return flavors[rng != null ? rng.Range(0, flavors.Count) : 0];
        }

        private static string[] SplitParamList(string param)
        {
            param = NormalizeParam(param);
            if (string.IsNullOrWhiteSpace(param))
            {
                return Array.Empty<string>();
            }

            string[] raw = param.Split('|', ';', ',');
            List<string> result = new List<string>();
            for (int i = 0; i < raw.Length; i++)
            {
                string value = raw[i].Trim();
                if (value.Length > 0)
                {
                    result.Add(value);
                }
            }

            return result.ToArray();
        }

        private static string BuildGenericRewardKey(GameRun run, string kind, string title)
        {
            string safeTitle = string.IsNullOrWhiteSpace(title) ? "reward" : title.GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture);
            string day = run.CurrentDay.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            return $"event_{kind}_{safeTitle}_w{run.WeekIndex}_d{day}_s{run.RunActionStepIndex}";
        }

        private static int ParseInt(string text, int fallback)
        {
            return int.TryParse(text, out int value) ? value : fallback;
        }

        private static string NormalizeParam(string param)
        {
            string value = param ?? string.Empty;
            return value == "-" ? string.Empty : value;
        }

        private static string PurposeLabel(string param)
        {
            return string.IsNullOrWhiteSpace(param) ? "Dish" : param;
        }

        private static string FormatPct(float pct)
        {
            return $"{System.Math.Round(pct * 100f, 1, System.MidpointRounding.AwayFromZero)}%";
        }

        private static string FormatMultiplier(float pct)
        {
            return $"×{System.Math.Max(0f, 1f + pct).ToString("0.##", CultureInfo.InvariantCulture)}";
        }

        private static string FormatSigned(float value)
        {
            return value >= 0f ? $"+{value}" : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FormatSigned(int value)
        {
            return value >= 0 ? $"+{value}" : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static int RoundToInt(float v) => (int)System.Math.Round(v, System.MidpointRounding.AwayFromZero);
    }
}
