using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;

namespace GourmetProject.Game.Meta.Passives
{
    /// <summary>被动道具 effectParam 解析工具（key:value，分隔符 ; , |）。</summary>
    internal static class PassiveParam
    {
        public static int ParseInt(string param, string key, int fallback)
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

                if (string.Equals(token.Substring(0, idx).Trim(), key, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(token.Substring(idx + 1).Trim(), out int v))
                {
                    return v;
                }
            }

            return fallback;
        }
    }

    /// <summary>商店某类商品折扣家族：对匹配 <see cref="_kind"/> 的商品价按 (1-Value) 打折。</summary>
    public abstract class ShopDiscountKindModel : PassiveItemModel
    {
        private readonly ShopEntryKind _kind;

        protected ShopDiscountKindModel(ShopEntryKind kind)
        {
            _kind = kind;
        }

        public override float ModifyShopPrice(ShopEntryKind kind, float price)
        {
            if (kind == _kind)
            {
                float d = Value;
                if (d > 0f && d < 1f)
                {
                    return price * (1f - d);
                }
            }

            return price;
        }
    }

    /// <summary>目标分档位百分比修正家族：对匹配 <see cref="_tier"/> 的档位累加 Value（可正可负）。</summary>
    public abstract class RequiredScorePctModel : PassiveItemModel
    {
        private readonly MealTier _tier;

        protected RequiredScorePctModel(MealTier tier)
        {
            _tier = tier;
        }

        public override float RequiredScorePct(MealTier tier) => tier == _tier ? Value : 0f;
    }

    /// <summary>结算规格家族：贡献一条 <see cref="ItemScoreSpec"/>（逐菜/条件/顺序类效果）。</summary>
    public abstract class ScoreSpecModel : PassiveItemModel
    {
        private readonly ItemScoreEffectType _type;

        protected ScoreSpecModel(ItemScoreEffectType type)
        {
            _type = type;
        }

        public override IEnumerable<ItemScoreSpec> BuildScoreSpecs()
        {
            yield return new ItemScoreSpec(_type, Value, Param, ItemId, Def?.Name ?? ItemId);
        }
    }
}
