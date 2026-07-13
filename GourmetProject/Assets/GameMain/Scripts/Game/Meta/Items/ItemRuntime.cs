using System.Collections.Generic;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>美食档位，用于目标分道具修正区分普通/超级/盛宴。</summary>
    public enum MealTier
    {
        Normal,
        Super,
        Feast,
    }

    /// <summary>
    /// 道具运行时聚合器：把当前 Run 持有的被动道具按效果类型归类，向各子系统（金币、商店、目标分、时间轴等）
    /// 提供统一查询入口，避免各处直接遍历持有列表 + 字符串比较。结算类效果不走这里（见 ItemScoreEffectAdapter）。
    /// </summary>
    public sealed class ItemRuntime
    {
        private readonly GameRun _run;

        public ItemRuntime(GameRun run)
        {
            _run = run;
        }

        /// <summary>遍历持有的被动道具（同 id 仅一条，不升级）。</summary>
        public IEnumerable<(ItemDefinition item, RunItemState state)> PassiveItems()
        {
            var result = new List<(ItemDefinition item, RunItemState state)>();
            if (_run == null)
            {
                return result;
            }

            foreach (RunItemState state in _run.Items)
            {
                ItemDefinition item = ItemDefinition.Get(_run.Tables, state.ItemId, cfg.ItemKind.Passive);
                if (item != null)
                {
                    result.Add((item, state));
                }
            }

            return result;
        }

        /// <summary>是否持有指定 effectType 的被动道具。</summary>
        public bool HasPassive(string effectType)
        {
            foreach ((ItemDefinition item, RunItemState _) in PassiveItems())
            {
                if (item.EffectType == effectType)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>某 effectType 的被动道具 EffectValue 之和（无则 0）。</summary>
        public float SumValue(string effectType)
        {
            float sum = 0f;
            foreach ((ItemDefinition item, RunItemState state) in PassiveItems())
            {
                if (item.EffectType == effectType)
                {
                    sum += item.EffectValue;
                }
            }

            return sum;
        }

        /// <summary>某 effectType 的被动道具 EffectValue 最大值（用于「打折取最优」「固定价取最低」等场景）。</summary>
        public bool TryGetMaxValue(string effectType, out float value)
        {
            bool found = false;
            value = 0f;
            foreach ((ItemDefinition item, RunItemState _) in PassiveItems())
            {
                if (item.EffectType != effectType)
                {
                    continue;
                }

                if (!found || item.EffectValue > value)
                {
                    value = item.EffectValue;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>某 effectType 的被动道具 EffectParam（首个命中；无则空串）。</summary>
        public string FirstParam(string effectType)
        {
            foreach ((ItemDefinition item, RunItemState _) in PassiveItems())
            {
                if (item.EffectType == effectType)
                {
                    return item.EffectParam ?? string.Empty;
                }
            }

            return string.Empty;
        }

        // ================= 商店 / 删牌价格族 hook =================

        /// <summary>按商品类型计算折后价：先按对应类别折扣（取最优/累乘），再叠加涨价（负面）。下限 1。</summary>
        public int ModifyShopPrice(ShopEntryKind kind, int basePrice)
        {
            string discountType = kind switch
            {
                ShopEntryKind.PassiveItem => ItemEffectTypes.ShopDiscountPassive,
                ShopEntryKind.ActiveItem => ItemEffectTypes.ShopDiscountActive,
                ShopEntryKind.Dish => ItemEffectTypes.ShopDiscountFood,
                ShopEntryKind.Fragment => ItemEffectTypes.ShopDiscountFragment,
                _ => string.Empty,
            };

            return ApplyPriceModifiers(basePrice, discountType);
        }

        /// <summary>菜谱本购买价：走「食物」折扣分类（ShopDiscountRecipe）。</summary>
        public int ModifyRecipeBookPrice(int basePrice)
        {
            return ApplyPriceModifiers(basePrice, ItemEffectTypes.ShopDiscountRecipe);
        }

        /// <summary>删牌价：RemovePriceFixed 固定优先（取最低固定价），否则按删牌折扣 + 涨价。下限 1。</summary>
        public int ModifyDeletePrice(int basePrice)
        {
            if (TryGetMinFixed(ItemEffectTypes.RemovePriceFixed, out int fixedPrice))
            {
                basePrice = fixedPrice;
            }

            return ApplyPriceModifiers(basePrice, ItemEffectTypes.ShopDiscountRemove);
        }

        /// <summary>是否禁止删除菜品（负面「囤积癖」）。</summary>
        public bool BlockRemoveDish() => HasPassive(ItemEffectTypes.NoRemoveDish);

        /// <summary>商店是否自动补货。</summary>
        public bool AutoRestock() => HasPassive(ItemEffectTypes.ShopRestock);

        private int ApplyPriceModifiers(int basePrice, string discountType)
        {
            float price = basePrice;

            // 折扣：累乘所有匹配折扣（value 为折扣比例，如 0.2 表示 -20%）。
            foreach ((ItemDefinition item, RunItemState _) in PassiveItems())
            {
                if (!string.IsNullOrEmpty(discountType) && item.EffectType == discountType)
                {
                    float d = item.EffectValue;
                    if (d > 0f && d < 1f)
                    {
                        price *= 1f - d;
                    }
                }
            }

            // 涨价（负面）：累乘。
            foreach ((ItemDefinition item, RunItemState _) in PassiveItems())
            {
                if (item.EffectType == ItemEffectTypes.ShopPriceUp && item.EffectValue > 0f)
                {
                    price *= 1f + item.EffectValue;
                }
            }

            int result = (int)System.Math.Round(price, System.MidpointRounding.AwayFromZero);
            return result < 1 ? 1 : result;
        }

        // ================= 目标分修正族 hook =================

        /// <summary>按美食档位对要求分做百分比修正（可正可负，多件累加）。下限 1。</summary>
        public int ModifyRequiredScore(int baseReq, MealTier tier)
        {
            string type = tier switch
            {
                MealTier.Normal => ItemEffectTypes.RequiredScoreNormalPct,
                MealTier.Super => ItemEffectTypes.RequiredScoreSuperPct,
                MealTier.Feast => ItemEffectTypes.RequiredScoreFeastPct,
                _ => ItemEffectTypes.RequiredScoreNormalPct,
            };

            float pct = SumValue(type);
            int result = (int)System.Math.Round(baseReq * (1f + pct), System.MidpointRounding.AwayFromZero);
            return result < 1 ? 1 : result;
        }

        // ================= 金币 / 利息族 hook =================

        /// <summary>美食奖励金币按百分比修正（GoldMealPercent 累加；含负面「克扣工钱」）。下限 0。</summary>
        public int ModifyMealRewardGold(int baseGold)
        {
            float pct = SumValue(ItemEffectTypes.GoldMealPercent);
            float g = baseGold * (1f + pct);
            int result = (int)System.Math.Round(g, System.MidpointRounding.AwayFromZero);
            return result < 0 ? 0 : result;
        }

        /// <summary>完成一个事件应额外获得的金币（GoldOnEventComplete 之和）。</summary>
        public int EventCompleteGold() => (int)SumValue(ItemEffectTypes.GoldOnEventComplete);

        /// <summary>完成本次 Boss 应额外获得的金币（GoldOnBossComplete 之和）。</summary>
        public int BossCompleteGold() => (int)SumValue(ItemEffectTypes.GoldOnBossComplete);

        /// <summary>「美食分红」每局额外金币（GoldMealBonus 之和；剩余局数由 GameRun 计数控制）。</summary>
        public int MealBonusGoldPerMeal() => (int)SumValue(ItemEffectTypes.GoldMealBonus);

        /// <summary>进入商店应额外获得的金币（GoldOnShopEnter 之和）。</summary>
        public int ShopEnterGold() => (int)SumValue(ItemEffectTypes.GoldOnShopEnter);

        /// <summary>使用主动道具应额外获得的金币（GoldOnActiveUse 之和）。</summary>
        public int ActiveUseGold() => (int)SumValue(ItemEffectTypes.GoldOnActiveUse);

        /// <summary>回合结束金币保底（MinGoldGuarantee 取最大）。无则返回 0。</summary>
        public int MinGoldGuarantee()
        {
            TryGetMaxValue(ItemEffectTypes.MinGoldGuarantee, out float v);
            return (int)v;
        }

        /// <summary>本周结束是否清空金币（负面「月光族」）。</summary>
        public bool ClearsGoldOnWeekEnd() => HasPassive(ItemEffectTypes.GoldWeekClear);

        /// <summary>额外主动道具栏数量（ExtraActiveSlot 之和）。</summary>
        public int ExtraActiveSlots() => (int)SumValue(ItemEffectTypes.ExtraActiveSlot);

        /// <summary>是否禁止获得主动道具（负面/极简「BlockActive」）。</summary>
        public bool BlocksActiveItems() => HasPassive(ItemEffectTypes.BlockActive);

        // ================= 不死族 hook =================

        /// <summary>是否持有「不死」道具（名刀·加护）。</summary>
        public bool HasUndying() => HasPassive(ItemEffectTypes.Undying);

        // ================= 上菜 / 调整族 hook =================

        /// <summary>调整次数加成（AdjustCountBonus 之和）。</summary>
        public int AdjustCountBonus() => (int)SumValue(ItemEffectTypes.AdjustCountBonus);

        /// <summary>每个未使用调整次数带来的倍率加成（AdjustToMult 取最大）。</summary>
        public float AdjustToMultPerUnused()
        {
            TryGetMaxValue(ItemEffectTypes.AdjustToMult, out float v);
            return v;
        }

        /// <summary>每个未使用调整次数返还的金币（GoldPerUnusedAdjust 之和）。</summary>
        public int GoldPerUnusedAdjust() => (int)SumValue(ItemEffectTypes.GoldPerUnusedAdjust);

        /// <summary>每局第一次上菜后是否可免费移动（FreeMoveFirst）。</summary>
        public bool FreeMoveFirstServe() => HasPassive(ItemEffectTypes.FreeMoveFirst);

        /// <summary>观星「每 N 次上菜后可预见」的周期（无则 0）。</summary>
        public int StarGazeEvery() => ParseTokenInt(ItemEffectTypes.StarGazeEveryN, "every");

        /// <summary>观星「每局前 N 次上菜可预见」的次数（无则 0）。</summary>
        public int StarGazeFirst() => ParseTokenInt(ItemEffectTypes.StarGazeFirstN, "first");

        // ================= 利息 / 时间轴族 hook =================

        /// <summary>是否每周额外结算一次利息（ExtraInterest）。</summary>
        public bool HasExtraInterest() => HasPassive(ItemEffectTypes.ExtraInterest);

        /// <summary>道具指定的利息上限目标值（InterestCapBonus 取最大）。</summary>
        public int InterestCapOverride()
        {
            TryGetMaxValue(ItemEffectTypes.InterestCapBonus, out float v);
            return (int)v;
        }

        // ================= 奖励 / 多选一族 hook =================

        /// <summary>多选一可选「数量」增减（ChoiceCountBonus - ChoiceCountPenalty 绝对值）。</summary>
        public int ChoiceCountDelta()
        {
            return (int)(SumValue(ItemEffectTypes.ChoiceCountBonus) + SumValue(ItemEffectTypes.ChoiceCountPenalty));
        }

        /// <summary>多选一可选「次数」增加（ChoiceTimesBonus 之和）。</summary>
        public int ChoiceTimesBonus() => (int)SumValue(ItemEffectTypes.ChoiceTimesBonus);

        // ================= 事件 / 行动概率族 hook =================

        /// <summary>遇到奖励事件的额外概率（LuckyEventChance 之和）。</summary>
        public float LuckyEventChanceBonus() => SumValue(ItemEffectTypes.LuckyEventChance);

        /// <summary>遇到事件的额外概率（MoreEvents 之和）。</summary>
        public float MoreEventsBonus() => SumValue(ItemEffectTypes.MoreEvents);

        /// <summary>每 N 个事件保底一个奖励事件的周期（LuckyEventGuarantee 取最大；无则 0）。</summary>
        public int LuckyEventGuaranteeEvery()
        {
            TryGetMaxValue(ItemEffectTypes.LuckyEventGuarantee, out float v);
            return (int)v;
        }

        private int ParseTokenInt(string effectType, string key)
        {
            string param = FirstParam(effectType);
            if (string.IsNullOrEmpty(param))
            {
                return 0;
            }

            foreach (string token in param.Split(';', ',', '|'))
            {
                int idx = token.IndexOf(':');
                if (idx < 0)
                {
                    continue;
                }

                if (string.Equals(token.Substring(0, idx).Trim(), key, System.StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(token.Substring(idx + 1).Trim(), out int v))
                {
                    return v;
                }
            }

            return 0;
        }

        // ================= 蛋糕层数族 hook =================

        /// <summary>本次品鉴初始额外蛋糕层数（CakeLayerInitBonus 之和）。</summary>
        public int CakeInitialLayers() => (int)SumValue(ItemEffectTypes.CakeLayerInitBonus);

        /// <summary>蛋糕层数 buff 阈值下调（CakeLayerReqMinus 之和）。</summary>
        public int CakeThresholdReduction() => (int)SumValue(ItemEffectTypes.CakeLayerReqMinus);

        /// <summary>蛋糕层数每次净增的额外加成（CakeLayerAccel 之和）。</summary>
        public int CakeAccelBonus() => (int)SumValue(ItemEffectTypes.CakeLayerAccel);

        /// <summary>跨品鉴保留的蛋糕层数比例（CakeLayerRetain 取最大；0 表示不保留）。</summary>
        public float CakeRetainFraction()
        {
            TryGetMaxValue(ItemEffectTypes.CakeLayerRetain, out float v);
            return v;
        }

        private bool TryGetMinFixed(string effectType, out int value)
        {
            bool found = false;
            value = 0;
            foreach ((ItemDefinition item, RunItemState _) in PassiveItems())
            {
                if (item.EffectType != effectType)
                {
                    continue;
                }

                int v = (int)item.EffectValue;
                if (!found || v < value)
                {
                    value = v;
                    found = true;
                }
            }

            return found;
        }
    }
}
