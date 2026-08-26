using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>
    /// 一条装饰品的结算规格（纯基元数据，不依赖 cfg/Game）：由 Game 层从装饰品和消耗品配置映射而来。
    /// </summary>
    public readonly struct ItemScoreSpec
    {
        public ItemScoreSpec(ItemScoreEffectType type, float value, string param, string itemId, string itemName)
        {
            Type = type;
            Value = value;
            Param = param ?? string.Empty;
            ItemId = itemId ?? string.Empty;
            ItemName = itemName ?? itemId ?? string.Empty;
        }

        public ItemScoreEffectType Type { get; }

        public float Value { get; }

        public string Param { get; }

        public string ItemId { get; }

        public string ItemName { get; }
    }

    /// <summary>
    /// 装饰品结算效果来源：把持有装饰品和消耗品的结算类效果统一转成 <see cref="IScoreEffect"/> 条目，
    /// 复用小丑牌式阶段结算管线。本来源负责当前实际使用的「逐菜/条件/顺序」类被动效果；
    /// FinalAddFlat/Mult 仅保留兼容枚举，当前没有配置或模型产出。
    /// </summary>
    public sealed class ItemScoreEffectSource : IScoreEffectSource
    {
        private readonly IReadOnlyList<ItemScoreSpec> _specs;

        public ItemScoreEffectSource(IReadOnlyList<ItemScoreSpec> specs)
        {
            _specs = specs ?? Array.Empty<ItemScoreSpec>();
        }

        public void CollectEffects(ScoreSnapshot snapshot, ScoreEffectCollector collector)
        {
            foreach (ItemScoreSpec spec in _specs)
            {
                if (spec.Type == ItemScoreEffectType.None)
                {
                    continue;
                }

                ScorePhase phase = spec.Type switch
                {
                    // 首/末位 +N 必须在结算开场生效。演出层会先统一播放所有食物基础分，
                    // 再按 ScoreLine 顺序播放，因此 BeforeAll 明细会紧跟基础分批次出现，
                    // 且后续技能的倍率 before/after 会自然包含这次加成，不会视觉倒退。
                    ItemScoreEffectType.NthServeMultFlat => ScorePhase.BeforeAll,
                    ItemScoreEffectType.AllDishFlat => ScorePhase.BeforeAll,
                    ItemScoreEffectType.AllDishMultFlat => ScorePhase.BeforeAll,
                    ItemScoreEffectType.CountAsBonusAll => ScorePhase.BeforeAll,
                    ItemScoreEffectType.CountThresholdAllDishMult => ScorePhase.BeforeAll,
                    ItemScoreEffectType.TagBonus => ScorePhase.BeforeAll,
                    ItemScoreEffectType.TagMultFlat => ScorePhase.BeforeAll,
                    ItemScoreEffectType.TagCountAsBonus => ScorePhase.BeforeAll,
                    ItemScoreEffectType.AllDishTemporaryCategory => ScorePhase.BeforeAll,
                    ItemScoreEffectType.RandomDishesTemporaryCategory => ScorePhase.BeforeAll,
                    ItemScoreEffectType.AllDishFlatPerUnusedDiscard => ScorePhase.BeforeAll,
                    ItemScoreEffectType.AllDishMultPerUnusedDiscard => ScorePhase.BeforeAll,
                    ItemScoreEffectType.AllDishFlatPerEmptyCell => ScorePhase.BeforeAll,
                    ItemScoreEffectType.SameBaseDishMultFlat => ScorePhase.BeforeAll,
                    ItemScoreEffectType.CountThresholdFinalMult => ScorePhase.BeforeAll,
                    ItemScoreEffectType.PerDishPermanentFlat => ScorePhase.BeforeDish,
                    ItemScoreEffectType.PerDishFlatTimesOwnCountAs => ScorePhase.AfterAllDishes,
                    ItemScoreEffectType.PerDishMultFlatTimesOwnCountAs => ScorePhase.AfterAllDishes,
                    ItemScoreEffectType.CakeLayersPerCakeDish => ScorePhase.AfterAllDishes,
                    _ => ScorePhase.AfterAllDishes,
                };

                int priority = spec.Type switch
                {
                    ItemScoreEffectType.CountAsBonusAll => -100,
                    ItemScoreEffectType.TagCountAsBonus => -100,
                    ItemScoreEffectType.AllDishTemporaryCategory => -100,
                    ItemScoreEffectType.RandomDishesTemporaryCategory => -100,
                    ItemScoreEffectType.CountThresholdAllDishMult => -50,
                    ItemScoreEffectType.CountThresholdFinalMult => -50,
                    ItemScoreEffectType.CakeLayersPerCakeDish => -50,
                    _ => 0,
                };

                collector.Add(new ScoreEffectEntry(
                    phase,
                    ScoreSource.Relic(spec.ItemId, spec.ItemName),
                    new ItemScoreEffect(spec),
                    dish: null,
                    priority: priority));
            }
        }
    }

    /// <summary>单条装饰品结算效果：在全局阶段一次性遍历餐桌按类型施加。</summary>
    public sealed class ItemScoreEffect : IScoreEffect
    {
        private readonly ItemScoreSpec _spec;

        public ItemScoreEffect(ItemScoreSpec spec)
        {
            _spec = spec;
        }

        public void Apply(ScoreContext ctx)
        {
            List<DishInstance> dishes = ctx.Snapshot.DishesInDefaultOrder.ToList();
            if (dishes.Count == 0 && _spec.Type != ItemScoreEffectType.CountThresholdFinalMult)
            {
                return;
            }

            float value = _spec.Value;

            switch (_spec.Type)
            {
                case ItemScoreEffectType.AllDishFlat:
                    if (Math.Abs(value) > 0.0001f)
                    {
                        foreach (DishInstance d in ctx.Snapshot.DishesInDefaultOrder)
                        {
                            ctx.AddFlatTo(d, value);
                        }
                    }

                    break;

                case ItemScoreEffectType.AllDishMultFlat:
                    if (Math.Abs(value) > 0.0001f)
                    {
                        foreach (DishInstance d in ctx.Snapshot.DishesInDefaultOrder)
                        {
                            ctx.AddMultFlatTo(d, value);
                        }
                    }

                    break;

                case ItemScoreEffectType.TagBonus:
                    foreach (DishInstance d in dishes)
                    {
                        if (ItemDishMatcher.Matches(d, _spec.Param, ctx))
                        {
                            ctx.AddFlatTo(d, value);
                        }
                    }

                    break;

                case ItemScoreEffectType.TagMultFlat:
                    foreach (DishInstance d in dishes)
                    {
                        if (ItemDishMatcher.Matches(d, _spec.Param, ctx))
                        {
                            ctx.AddMultFlatTo(d, value);
                        }
                    }

                    break;

                case ItemScoreEffectType.CountAsBonusAll:
                {
                    int countAs = (int)Math.Round(value, MidpointRounding.AwayFromZero);
                    foreach (DishInstance d in dishes)
                    {
                        ctx.AddLiveCountAs(d, countAs);
                    }

                    break;
                }

                case ItemScoreEffectType.TagCountAsBonus:
                {
                    int countAs = (int)Math.Round(value, MidpointRounding.AwayFromZero);
                    foreach (DishInstance d in dishes)
                    {
                        if (ItemDishMatcher.Matches(d, _spec.Param, ctx))
                        {
                            ctx.AddLiveCountAs(d, countAs);
                        }
                    }

                    break;
                }

                case ItemScoreEffectType.AllDishTemporaryCategory:
                {
                    string category = ParseStringParam(_spec.Param, "category", _spec.Param);
                    foreach (DishInstance d in dishes)
                    {
                        ctx.AddLiveCategory(d, category);
                    }

                    break;
                }

                case ItemScoreEffectType.AllDishFlatPerUnusedDiscard:
                {
                    float add = value * ctx.Snapshot.RemainingFoodDiscards;
                    foreach (DishInstance d in dishes)
                    {
                        ctx.AddFlatTo(d, add);
                    }

                    break;
                }

                case ItemScoreEffectType.AllDishMultPerUnusedDiscard:
                {
                    float add = value * ctx.Snapshot.RemainingFoodDiscards;
                    foreach (DishInstance d in dishes)
                    {
                        ctx.AddMultFlatTo(d, add);
                    }

                    break;
                }

                case ItemScoreEffectType.AllDishFlatPerEmptyCell:
                {
                    float add = value * ctx.DiningTable.EmptyCellCount;
                    foreach (DishInstance d in dishes)
                    {
                        ctx.AddFlatTo(d, add);
                    }

                    break;
                }

                case ItemScoreEffectType.SameBaseDishMultFlat:
                    foreach (IGrouping<string, DishInstance> group in dishes.GroupBy(
                                 d => d.Def?.BaseId ?? d.Def?.Id ?? string.Empty,
                                 StringComparer.OrdinalIgnoreCase))
                    {
                        if (group.Count() < 2)
                        {
                            continue;
                        }

                        foreach (DishInstance d in group)
                        {
                            ctx.AddMultFlatTo(d, value);
                        }
                    }

                    break;

                case ItemScoreEffectType.PermanentAddFlatAll:
                    foreach (DishInstance d in dishes)
                    {
                        ctx.AddPermanentFlatTo(d, value);
                    }

                    break;

                case ItemScoreEffectType.PerDishPermanentFlat:
                    if (ctx.Dish != null)
                    {
                        ctx.AddPermanentFlatTo(ctx.Dish, value);
                    }

                    break;

                case ItemScoreEffectType.PermanentAddMultAll:
                    foreach (DishInstance d in dishes)
                    {
                        ctx.AddPermanentMultTo(d, 1f + value);
                    }

                    break;

                case ItemScoreEffectType.CountThresholdAllDishMult:
                {
                    // 结算开场按有效份数检查门槛；命中后逐个食物产生倍率加区明细，
                    // 让演出层对全场食物播放 +value，而不是生成一条总分倍率演出。
                    int count = dishes.Sum(ctx.GetEffectiveCountAs);
                    if (MatchesThreshold(count, _spec.Param))
                    {
                        foreach (DishInstance d in dishes)
                        {
                            ctx.AddMultFlatTo(d, value);
                        }
                    }

                    break;
                }

                case ItemScoreEffectType.CountThresholdFinalMult:
                {
                    // CountAs 类效果先执行；随后按本次结算的实际份数检查门槛，
                    // 命中后为每道参与结算的食物增加倍率加区。
                    int count = dishes.Sum(ctx.GetEffectiveCountAs);
                    if (MatchesThreshold(count, _spec.Param))
                    {
                        foreach (DishInstance d in dishes)
                        {
                            ctx.AddMultFlatTo(d, value);
                        }
                    }

                    break;
                }

                case ItemScoreEffectType.PerDishSettledMultFlat:
                {
                    float add = value * dishes.Count;
                    foreach (DishInstance d in dishes)
                    {
                        ctx.AddMultFlatTo(d, add);
                    }

                    break;
                }

                case ItemScoreEffectType.PerSkillMultFlat:
                {
                    // 每份食物只按自己的子技能条目数量获得倍率，不把其他食物的技能算进来。
                    foreach (DishInstance d in ctx.Snapshot.DishesInDefaultOrder)
                    {
                        float add = value * SkillConditionEvaluator.CountSubSkills(d, ctx.Db, ctx);
                        if (Math.Abs(add) > 0.0001f)
                        {
                            ctx.AddMultFlatTo(d, add);
                        }
                    }

                    break;
                }

                case ItemScoreEffectType.NthServeMult:
                case ItemScoreEffectType.NthServeMultFlat:
                {
                    // 上菜顺序 = 实例 Id 升序（Serve 时递增分配）。
                    List<DishInstance> ordered = dishes.OrderBy(d => d.Id).ToList();
                    int index = ParseIntParam(_spec.Param, "index", 1);
                    DishInstance target = index < 0
                        ? ordered[ordered.Count - 1]
                        : (index - 1 < ordered.Count && index - 1 >= 0 ? ordered[index - 1] : null);
                    if (target != null)
                    {
                        if (_spec.Type == ItemScoreEffectType.NthServeMult)
                        {
                            ctx.MultiplyTo(target, value);
                        }
                        else
                        {
                            ctx.AddMultFlatTo(target, value);
                        }
                    }

                    break;
                }

                case ItemScoreEffectType.EveryNthServeMult:
                {
                    int n = Math.Max(1, ParseIntParam(_spec.Param, "every", 3));
                    List<DishInstance> ordered = dishes.OrderBy(d => d.Id).ToList();
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        int serveIndex = i + 1; // 1-based
                        if (serveIndex > n && (serveIndex - 1) % n == 0)
                        {
                            ctx.AddMultFlatTo(ordered[i], value);
                        }
                    }

                    break;
                }

                case ItemScoreEffectType.PerDishFlatTimesOwnCountAs:
                    foreach (DishInstance d in dishes)
                    {
                        float add = value * ctx.GetEffectiveCountAs(d);
                        if (Math.Abs(add) > 0.0001f)
                        {
                            ctx.AddFlatTo(d, add);
                        }
                    }

                    break;

                case ItemScoreEffectType.PerDishMultFlatTimesOwnCountAs:
                    foreach (DishInstance d in dishes)
                    {
                        float add = value * ctx.GetEffectiveCountAs(d);
                        if (Math.Abs(add) > 0.0001f)
                        {
                            ctx.AddMultFlatTo(d, add);
                        }
                    }

                    break;

                case ItemScoreEffectType.RandomDishesTemporaryCategory:
                {
                    string category = ParseStringParam(_spec.Param, "category", _spec.Param);
                    int pickCount = Math.Max(0, (int)Math.Round(value, MidpointRounding.AwayFromZero));
                    if (string.IsNullOrEmpty(category) || pickCount <= 0)
                    {
                        break;
                    }

                    var candidates = new List<DishInstance>(dishes);
                    int take = Math.Min(pickCount, candidates.Count);
                    if (ctx.Snapshot.RandomIntegerSelector != null)
                    {
                        for (int i = 0; i < take; i++)
                        {
                            int swap = ctx.Snapshot.RandomIntegerSelector(i, candidates.Count - 1);
                            DishInstance tmp = candidates[i];
                            candidates[i] = candidates[swap];
                            candidates[swap] = tmp;
                        }
                    }

                    for (int i = 0; i < take; i++)
                    {
                        ctx.AddLiveCategory(candidates[i], category);
                    }

                    break;
                }

                case ItemScoreEffectType.CakeLayersPerCakeDish:
                {
                    int layers = (int)Math.Round(value, MidpointRounding.AwayFromZero);
                    if (layers == 0)
                    {
                        break;
                    }

                    foreach (DishInstance d in dishes)
                    {
                        if (ctx.IsCategory(d, "cake"))
                        {
                            ctx.AddHappyCakeLayers(layers, mult: false);
                        }
                    }

                    break;
                }
            }
        }

        private static bool MatchesThreshold(int count, string param)
        {
            // param: "lte:N" 或 "gte:N"（默认 gte:0 恒真）。
            if (string.IsNullOrEmpty(param))
            {
                return true;
            }

            int idx = param.IndexOf(':');
            string op = idx >= 0 ? param.Substring(0, idx).Trim().ToLowerInvariant() : param.Trim().ToLowerInvariant();
            int threshold = 0;
            if (idx >= 0)
            {
                int.TryParse(param.Substring(idx + 1).Trim(), out threshold);
            }

            switch (op)
            {
                case "lte":
                    return count <= threshold;
                case "gte":
                    return count >= threshold;
                case "lt":
                    return count < threshold;
                case "gt":
                    return count > threshold;
                case "eq":
                    return count == threshold;
                default:
                    return true;
            }
        }

        private static int ParseIntParam(string param, string key, int fallback)
        {
            if (string.IsNullOrEmpty(param))
            {
                return fallback;
            }

            foreach (string token in param.Split(';', ',', '|'))
            {
                int idx = token.IndexOf(':');
                if (idx < 0)
                {
                    continue;
                }

                string k = token.Substring(0, idx).Trim();
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(token.Substring(idx + 1).Trim(), out int v))
                {
                    return v;
                }
            }

            return fallback;
        }

        private static string ParseStringParam(string param, string key, string fallback)
        {
            if (string.IsNullOrEmpty(param))
            {
                return fallback ?? string.Empty;
            }

            foreach (string token in param.Split(';', ',', '|'))
            {
                int idx = token.IndexOf(':');
                if (idx < 0)
                {
                    continue;
                }

                if (string.Equals(token.Substring(0, idx).Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    return token.Substring(idx + 1).Trim();
                }
            }

            return fallback ?? string.Empty;
        }
    }

    /// <summary>装饰品和消耗品标签匹配：把 effectParam 解析为对餐桌食物的判定（分类/风味/技能）。</summary>
    public static class ItemDishMatcher
    {
        /// <summary>
        /// 判定1 个食物是否匹配 <paramref name="param"/>。支持前缀精确匹配：
        /// <c>cat:xxx</c>（分类）、<c>flavor:xxx</c>（风味 id）、<c>skill:xxx</c>（含某技能 id）。
        /// 无前缀时依次尝试分类/风味/技能。空 param 匹配所有食物。
        /// </summary>
        public static bool Matches(DishInstance dish, string param, ScoreContext ctx = null)
        {
            if (dish == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(param))
            {
                return true;
            }

            if (string.Equals(param, "flavored", StringComparison.OrdinalIgnoreCase))
            {
                return dish.FlavorIds.Count > 0;
            }

            int idx = param.IndexOf(':');
            if (idx > 0)
            {
                string prefix = param.Substring(0, idx).Trim().ToLowerInvariant();
                string body = param.Substring(idx + 1).Trim();
                switch (prefix)
                {
                    case "cat":
                    case "category":
                        return ctx?.IsCategory(dish, body) ?? dish.IsCategory(body);
                    case "flavor":
                        if (string.Equals(body, "any", StringComparison.OrdinalIgnoreCase))
                        {
                            return dish.FlavorIds.Count > 0;
                        }

                        return HasFlavor(dish, body);
                    case "skill":
                        return HasSkill(dish, body);
                    case "position":
                    {
                        bool edge = ctx?.DiningTable != null
                            && SkillConditionEvaluator.IsOnEdge(ctx.DiningTable, dish);
                        return string.Equals(body, "edge", StringComparison.OrdinalIgnoreCase)
                            ? edge
                            : (string.Equals(body, "non-edge", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(body, "nonedge", StringComparison.OrdinalIgnoreCase)) && !edge;
                    }
                }
            }

            return (ctx?.IsCategory(dish, param) ?? dish.IsCategory(param))
                || HasFlavor(dish, param)
                || HasSkill(dish, param);
        }

        private static bool HasSkill(DishInstance dish, string skillId)
        {
            foreach (string id in dish.SkillIds)
            {
                if (string.Equals(id, skillId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasFlavor(DishInstance dish, string flavorId)
        {
            foreach (string id in dish.FlavorIds)
            {
                if (string.Equals(id, flavorId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
