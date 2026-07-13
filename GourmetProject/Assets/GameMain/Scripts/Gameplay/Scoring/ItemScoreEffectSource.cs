using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>
    /// 一条被动道具的结算规格（纯基元数据，不依赖 cfg/Game）：由 Game 层从 cfg.Item 映射而来。
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
    /// 被动道具结算效果来源：把持有道具的结算类效果统一转成 <see cref="IScoreEffect"/> 条目，
    /// 复用小丑牌式阶段结算管线。局级加/乘（FinalAddFlat/Mult）仍走 BattleSession.FinalFlat/Multiplier 快路径，
    /// 本来源只负责「逐菜/条件/顺序」类被动效果。
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

                ScorePhase phase = spec.Type == ItemScoreEffectType.CountThresholdFinalMult
                    ? ScorePhase.Final
                    : ScorePhase.AfterAllDishes;

                collector.Add(new ScoreEffectEntry(
                    phase,
                    ScoreSource.Relic(spec.ItemId, spec.ItemName),
                    new ItemScoreEffect(spec),
                    dish: null));
            }
        }
    }

    /// <summary>单条被动道具结算效果：在全局阶段一次性遍历餐桌按类型施加。</summary>
    public sealed class ItemScoreEffect : IScoreEffect
    {
        private readonly ItemScoreSpec _spec;

        public ItemScoreEffect(ItemScoreSpec spec)
        {
            _spec = spec;
        }

        public void Apply(ScoreContext ctx)
        {
            List<DishInstance> dishes = ctx.DiningTable.Dishes.ToList();
            if (dishes.Count == 0 && _spec.Type != ItemScoreEffectType.CountThresholdFinalMult)
            {
                return;
            }

            float value = _spec.Value;

            switch (_spec.Type)
            {
                case ItemScoreEffectType.TagBonus:
                    foreach (DishInstance d in dishes)
                    {
                        if (ItemDishMatcher.Matches(d, _spec.Param))
                        {
                            ctx.AddFlatTo(d, value);
                        }
                    }

                    break;

                case ItemScoreEffectType.PermanentAddFlatAll:
                    foreach (DishInstance d in dishes)
                    {
                        ctx.AddPermanentFlatTo(d, value);
                    }

                    break;

                case ItemScoreEffectType.PermanentAddMultAll:
                    foreach (DishInstance d in dishes)
                    {
                        ctx.AddPermanentMultTo(d, 1f + value);
                    }

                    break;

                case ItemScoreEffectType.CountThresholdFinalMult:
                {
                    int count = dishes.Count;
                    if (MatchesThreshold(count, _spec.Param))
                    {
                        ctx.MultiplyFinalBy(value);
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
                    int totalSkills = dishes.Sum(d => d.SkillIds.Count);
                    float add = value * totalSkills;
                    if (Math.Abs(add) > 0.0001f)
                    {
                        foreach (DishInstance d in dishes)
                        {
                            ctx.AddMultFlatTo(d, add);
                        }
                    }

                    break;
                }

                case ItemScoreEffectType.NthServeMult:
                {
                    // 上菜顺序 = 实例 Id 升序（Serve 时递增分配）。
                    List<DishInstance> ordered = dishes.OrderBy(d => d.Id).ToList();
                    int index = ParseIntParam(_spec.Param, "index", 1);
                    DishInstance target = index < 0
                        ? ordered[ordered.Count - 1]
                        : (index - 1 < ordered.Count && index - 1 >= 0 ? ordered[index - 1] : null);
                    if (target != null)
                    {
                        ctx.MultiplyTo(target, value);
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
    }

    /// <summary>道具标签匹配：把 effectParam 解析为对餐桌菜品的判定（分类/风味/技能）。</summary>
    public static class ItemDishMatcher
    {
        /// <summary>
        /// 判定一道菜是否匹配 <paramref name="param"/>。支持前缀精确匹配：
        /// <c>cat:xxx</c>（分类）、<c>flavor:xxx</c>（风味 id）、<c>skill:xxx</c>（含某技能 id）。
        /// 无前缀时依次尝试分类/风味/技能。空 param 匹配所有菜。
        /// </summary>
        public static bool Matches(DishInstance dish, string param)
        {
            if (dish == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(param))
            {
                return true;
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
                        return dish.Def.IsCategory(body);
                    case "flavor":
                        return HasFlavor(dish, body);
                    case "skill":
                        return HasSkill(dish, body);
                }
            }

            return dish.Def.IsCategory(param)
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
