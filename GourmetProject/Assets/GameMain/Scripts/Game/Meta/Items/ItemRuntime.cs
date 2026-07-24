using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 道具「钩子分发器」门面（对标杀戮尖塔2 的 Hook）：只遍历当前 Run 在场（持有）的被动道具模型
    /// <see cref="PassiveItemModel"/>，按各钩子语义折叠（求和/取最大/累乘/任一）。未持有即无模型、无副作用。
    /// 各子系统（金币/商店/目标分/事件/蛋糕等）继续调用这里的方法，签名保持不变。
    /// </summary>
    public sealed class ItemRuntime
    {
        private readonly GameRun _run;

        public ItemRuntime(GameRun run)
        {
            _run = run;
        }

        private IEnumerable<PassiveItemModel> Models =>
            _run != null ? _run.PassiveModels : System.Array.Empty<PassiveItemModel>();

        // ================= 商店 / 删牌价格族 =================

        /// <summary>按商品类型计算折后价：各模型依次修正（折扣累乘 + 涨价累乘），下限 1。</summary>
        public int ModifyShopPrice(ShopEntryKind kind, int basePrice)
        {
            float price = basePrice;
            foreach (PassiveItemModel m in Models)
            {
                price = m.ModifyShopPrice(kind, price);
            }

            return ClampPrice(price);
        }

        /// <summary>删牌价：固定价优先（取最低），否则按删牌折扣 + 涨价。下限 1。</summary>
        public int ModifyDeletePrice(int basePrice)
        {
            bool hasFixed = false;
            int fixedMin = 0;
            foreach (PassiveItemModel m in Models)
            {
                if (m.TryGetRemovePriceFixed(out int fp) && (!hasFixed || fp < fixedMin))
                {
                    fixedMin = fp;
                    hasFixed = true;
                }
            }

            float price = hasFixed ? fixedMin : basePrice;
            foreach (PassiveItemModel m in Models)
            {
                price = m.ModifyDeletePrice(price);
            }

            return ClampPrice(price);
        }

        /// <summary>是否禁止删除菜品（负面「囤积癖」）。</summary>
        public bool BlockRemoveDish() => AnyFlag(m => m.BlockRemoveDish());

        /// <summary>商店是否自动补货。</summary>
        public bool AutoRestock() => AnyFlag(m => m.AutoRestock());

        public void FlashTriggered(System.Func<PassiveItemModel, bool> predicate)
        {
            if (predicate == null)
            {
                return;
            }

            foreach (PassiveItemModel m in Models)
            {
                if (predicate(m))
                {
                    m.Flash();
                }
            }
        }

        public void RefreshIconState(System.Func<PassiveItemModel, bool> predicate)
        {
            if (predicate == null)
            {
                return;
            }

            foreach (PassiveItemModel m in Models)
            {
                if (predicate(m))
                {
                    m.RefreshIconState();
                }
            }
        }

        public void RefreshInfoText(System.Func<PassiveItemModel, bool> predicate)
        {
            if (predicate == null)
            {
                return;
            }

            foreach (PassiveItemModel m in Models)
            {
                if (predicate(m))
                {
                    m.RefreshInfoText();
                }
            }
        }

        // ================= 目标分修正族 =================

        /// <summary>按美食档位对要求分做百分比修正（可正可负，多件累加）。下限 1。</summary>
        public int ModifyRequiredScore(int baseReq, cfg.FoodActionKind tier)
        {
            float pct = 0f;
            foreach (PassiveItemModel m in Models)
            {
                pct += m.RequiredScorePct(tier);
            }

            int result = (int)System.Math.Round(baseReq * (1f + pct), System.MidpointRounding.AwayFromZero);
            return result < 1 ? 1 : result;
        }

        // ================= 金币 / 利息族 =================

        /// <summary>美食奖励金币按百分比修正（累加；含负面「克扣工钱」）。下限 0。</summary>
        public int ModifyMealRewardGold(int baseGold)
        {
            float pct = 0f;
            foreach (PassiveItemModel m in Models)
            {
                pct += m.MealRewardGoldPct();
            }

            int result = (int)System.Math.Round(baseGold * (1f + pct), System.MidpointRounding.AwayFromZero);
            return result < 0 ? 0 : result;
        }

        public int EventCompleteGold() => SumInt(m => m.EventCompleteGold());

        public int BossCompleteGold() => SumInt(m => m.BossCompleteGold());

        public int MealBonusGoldPerMeal() => SumInt(m => m.MealBonusGoldPerMeal());

        public int ShopEnterGold() => SumInt(m => m.ShopEnterGold());

        public int ActiveUseGold() => SumInt(m => m.ActiveUseGold());

        /// <summary>回合结束金币保底（取最大）。无则返回 0。</summary>
        public int MinGoldGuarantee()
        {
            int best = 0;
            foreach (PassiveItemModel m in Models)
            {
                if (m.TryGetMinGoldGuarantee(out int v) && v > best)
                {
                    best = v;
                }
            }

            return best;
        }

        public bool ClearsGoldOnWeekEnd() => AnyFlag(m => m.ClearsGoldOnWeekEnd());

        public int ExtraActiveSlots() => SumInt(m => m.ExtraActiveSlots());

        public bool BlocksActiveItems() => AnyFlag(m => m.BlocksActiveItems());

        public int FoodFlavorLimitBonus() => SumInt(m => m.FoodFlavorLimitBonus());

        public bool HasExtraInterest() => AnyFlag(m => m.HasExtraInterest());

        /// <summary>道具指定的利息上限目标值（取最大）。</summary>
        public int InterestCapOverride()
        {
            int best = 0;
            foreach (PassiveItemModel m in Models)
            {
                if (m.TryGetInterestCapOverride(out int v) && v > best)
                {
                    best = v;
                }
            }

            return best;
        }

        // ================= 不死族 =================

        /// <summary>是否持有「不死」道具（名刀·加护）。</summary>
        public bool HasUndying() => AnyFlag(m => m.IsUndying());

        // ================= 上菜族 =================

        /// <summary>观星「每 N 次上菜后可预见」的周期（取最大；无则 0）。</summary>
        public int StarGazeEvery() => MaxInt(m => m.StarGazeEvery());

        /// <summary>观星「每局前 N 次上菜可预见」的次数（取最大；无则 0）。</summary>
        public int StarGazeFirst() => MaxInt(m => m.StarGazeFirst());

        /// <summary>每场战斗可额外丢弃的出菜数量（各道具累加）。</summary>
        public int FoodDiscardLimitBonus() => SumInt(m => m.FoodDiscardLimitBonus());

        // ================= 奖励 / 多选一族 =================

        /// <summary>多选一可选「数量」增减（各模型累加）。</summary>
        public int ChoiceCountDelta() => SumInt(m => m.ChoiceCountDelta());

        /// <summary>多选一可选「次数」增加（各模型累加）。</summary>
        public int ChoiceTimesBonus() => SumInt(m => m.ChoiceTimesBonus());

        public RewardOffer ModifyBattleRewardOffer(
            RewardOffer offer,
            ActionExecutionContext actionContext,
            IRandomStream rng)
        {
            foreach (PassiveItemModel m in Models)
            {
                offer = m.ModifyBattleRewardOffer(offer, actionContext, rng);
            }

            return offer;
        }

        public float SweetTransferTargetMultiplier()
        {
            float best = 1f;
            foreach (PassiveItemModel m in Models)
            {
                if (m.TryGetSweetTransferTargetMultiplier(out float v) && v > best)
                {
                    best = v;
                }
            }

            return best;
        }

        public float SweetTransferSourceMultiplier()
        {
            float best = 1f;
            foreach (PassiveItemModel m in Models)
            {
                if (m.TryGetSweetTransferSourceMultiplier(out float v) && v > best)
                {
                    best = v;
                }
            }

            return best;
        }

        // ================= 事件 / 行动概率族 =================

        /// <summary>遇到奖励事件的额外概率（累加）。</summary>
        public float LuckyEventChanceBonus() => SumFloat(m => m.LuckyEventChanceBonus());

        /// <summary>遇到事件的额外概率（累加）。</summary>
        public float MoreEventsBonus() => SumFloat(m => m.MoreEventsBonus());

        /// <summary>每 N 个事件保底一个奖励事件的周期（取最大；无则 0）。</summary>
        public int LuckyEventGuaranteeEvery() => MaxInt(m => m.LuckyEventGuaranteeEvery());

        // ================= 隐藏分族 =================

        /// <summary>按用途聚合持有被动道具的隐藏分常驻修正。</summary>
        public float HiddenScoreOffset(HiddenScorePurpose purpose) => SumFloat(m => m.HiddenScoreOffset(purpose));

        /// <summary>旧通用奖励隐藏分入口；保留为食物奖励隐藏分修正的兼容别名。</summary>
        public float HiddenScoreBonus() => HiddenScoreOffset(HiddenScorePurpose.Dish);

        // ================= 蛋糕层数族 =================

        public int CakeInitialLayers() => SumInt(m => m.CakeInitialLayers());

        public int CakeThresholdReduction() => SumInt(m => m.CakeThresholdReduction());

        public int CakeAccelBonus() => SumInt(m => m.CakeAccelBonus());

        public int GoldForCakeLayers(int happyCakeLayers) => SumInt(m => m.GoldForCakeLayers(happyCakeLayers));

        /// <summary>跨品鉴保留的蛋糕层数比例（取最大；0 表示不保留）。</summary>
        public float CakeRetainFraction()
        {
            float best = 0f;
            foreach (PassiveItemModel m in Models)
            {
                if (m.TryGetCakeRetainFraction(out float v) && v > best)
                {
                    best = v;
                }
            }

            return best;
        }

        // ================= 折叠工具 =================

        private static int ClampPrice(float price)
        {
            int result = (int)System.Math.Floor(price);
            return result < 1 ? 1 : result;
        }

        private int SumInt(System.Func<PassiveItemModel, int> selector)
        {
            int sum = 0;
            foreach (PassiveItemModel m in Models)
            {
                sum += selector(m);
            }

            return sum;
        }

        private float SumFloat(System.Func<PassiveItemModel, float> selector)
        {
            float sum = 0f;
            foreach (PassiveItemModel m in Models)
            {
                sum += selector(m);
            }

            return sum;
        }

        private int MaxInt(System.Func<PassiveItemModel, int> selector)
        {
            int best = 0;
            foreach (PassiveItemModel m in Models)
            {
                int v = selector(m);
                if (v > best)
                {
                    best = v;
                }
            }

            return best;
        }

        private bool AnyFlag(System.Func<PassiveItemModel, bool> predicate)
        {
            foreach (PassiveItemModel m in Models)
            {
                if (predicate(m))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
