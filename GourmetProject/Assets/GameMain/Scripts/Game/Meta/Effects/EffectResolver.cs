using System;
using System.Collections.Generic;
using System.Globalization;
using GourmetProject.Core.Rng;
using GourmetProject.Runtime;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Model;

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
                    return $"本周目标分降低至 {run.RequiredScoreOverride}。";

                case cfg.EffectType.GainItem:
                    return EnqueueConfigReward(run, rng, effectParam, "获得道具", 40);

                case cfg.EffectType.Gamble:
                    if (rng.NextBool())
                    {
                        run.Gold += value * 2;
                        return $"豪赌成功！金币 +{value * 2}。";
                    }

                    run.Gold = System.Math.Max(0, run.Gold - value);
                    return $"豪赌失败…金币 -{value}。";

                case cfg.EffectType.AddDish:
                    return EnqueueConfigReward(run, rng, effectParam, "菜品奖励", 30);

                case cfg.EffectType.UpgradeDish:
                    // 「提升菜品」是改造而非发放，暂折金币占位（不在本次奖励统一范围）。
                    run.Gold += System.Math.Max(1, value) * 15;
                    return $"暂以金币 +{System.Math.Max(1, value) * 15} 折算（菜品成长后续接入）。";

                case cfg.EffectType.AddHiddenScoreOffset:
                    return AddHiddenScoreOffset(run, effectValue, effectParam);

                case cfg.EffectType.EnqueueDishChoice:
                    return EnqueueConfigReward(run, rng, effectParam, "菜品奖励", 30);

                case cfg.EffectType.EnqueueItemChoice:
                    return EnqueueConfigReward(run, rng, effectParam, "道具奖励", 40);

                case cfg.EffectType.AddRandomRecipeFlavor:
                    return AddRandomRecipeFlavor(run, rng, System.Math.Max(1, value));

                case cfg.EffectType.GainSpecificItem:
                    return EnqueueConfigReward(run, rng, effectParam, "获得道具", 40);

                case cfg.EffectType.GrantFragmentPack:
                    return EnqueueConfigReward(run, rng, effectParam, "餐桌碎片", 40);

                case cfg.EffectType.AddShopPricePct:
                    run.AddEventShopPricePct(effectValue);
                    return effectValue >= 0f ? $"商店价格提高 {FormatPct(effectValue)}。" : $"商店价格降低 {FormatPct(-effectValue)}。";

                case cfg.EffectType.AddChoiceCountPenalty:
                    int delta = ParseInt(effectParam, -1);
                    run.AddEventChoiceCountPenalty(System.Math.Max(1, value), delta);
                    return $"后续 {System.Math.Max(1, value)} 次奖励选择数量 {FormatSigned(delta)}。";

                case cfg.EffectType.AddNextFoodTargetOffset:
                    run.AddNextFoodTargetScoreHiddenOffset(effectValue);
                    return $"下一场美食挑战目标分隐藏分 {FormatSigned(effectValue)}。";

                case cfg.EffectType.AddNextMealGold:
                    run.AddNextMealRewardGold(value);
                    return $"下一次美食奖励金币 +{value}。";

                case cfg.EffectType.RemoveActiveItemsForGold:
                    return RemoveActiveItemsForGold(run, value);

                case cfg.EffectType.IncrementEventCounter:
                    return IncrementEventCounter(run, value, effectParam);

                case cfg.EffectType.GainRandomFlavoredDishes:
                    return EnqueueConfigReward(run, rng, effectParam, "风味美食", 60);

                case cfg.EffectType.RemoveRandomRecipeDish:
                    return RemoveRandomRecipeDish(run, rng, System.Math.Max(1, value), effectParam);

                case cfg.EffectType.LoseAllGold:
                    int lost = run.Gold;
                    run.Gold = 0;
                    return $"失去所有金币（-{lost}）。";

                case cfg.EffectType.GainLegendaryItem:
                    return EnqueueConfigReward(run, rng, effectParam, "传奇道具", 80);

                case cfg.EffectType.CollectInterest:
                    return CollectInterest(run, effectParam);

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

        private static string AddRandomRecipeFlavor(GameRun run, IRandomStream rng, int count)
        {
            var emptyFlavorTargets = new List<int>();
            var allTargets = new List<int>();
            IReadOnlyList<RecipeBookSlot> recipe = run.RecipeEntries;
            for (int dishIndex = 0; dishIndex < recipe.Count; dishIndex++)
            {
                allTargets.Add(dishIndex);
                if (recipe[dishIndex].ExtraFlavorIds.Count == 0)
                {
                    emptyFlavorTargets.Add(dishIndex);
                }
            }

            if (allTargets.Count == 0)
            {
                return "菜谱为空，无法添加风味。";
            }

            List<FlavorDef> flavors = AllFlavors(run);
            if (flavors.Count == 0)
            {
                return "没有可用风味，无法添加。";
            }

            int applied = 0;
            for (int i = 0; i < count; i++)
            {
                List<int> pool = emptyFlavorTargets.Count > 0 ? emptyFlavorTargets : allTargets;
                int targetIndex = rng != null ? rng.Range(0, pool.Count) : 0;
                int dishIndex = pool[targetIndex];
                string flavorId = PickFlavor(flavors, rng).Id;
                if (run.AddRecipeFlavor(dishIndex, flavorId))
                {
                    applied++;
                    emptyFlavorTargets.Remove(dishIndex);
                }
            }

            return applied > 0 ? $"为菜谱中的 {applied} 道菜添加了随机风味。" : "没有菜品获得风味。";
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
                return requireFlavor ? "菜谱中没有带风味的美食可献上。" : "菜谱为空，无法献上美食。";
            }

            return requireFlavor ? $"随机献上了 {removed} 道带风味的美食。" : $"随机献上了 {removed} 道美食。";
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
            return removed > 0 ? $"失去主动道具 {removed} 个，获得金币 {gained}。" : "没有主动道具可献上。";
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
