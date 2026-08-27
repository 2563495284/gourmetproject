using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Scoring;

namespace GourmetProject.Game.Meta
{
    public sealed class FoodSettlementReward
    {
        public FoodSettlementReward(string sourceItemId, string title, RewardOffer offer)
        {
            SourceItemId = sourceItemId ?? string.Empty;
            Title = title ?? string.Empty;
            Offer = offer;
        }

        public string SourceItemId { get; }

        public string Title { get; }

        public RewardOffer Offer { get; }
    }

    /// <summary>
    /// 装饰品和消耗品「钩子分发器」门面（对标杀戮尖塔2 的 Hook）：只遍历当前 Run 在场（持有）的装饰品模型
    /// <see cref="PassiveItemModel"/>，按各钩子语义折叠（求和/取最大/累乘/任一）。未持有即无模型、无副作用。
    /// 各子系统（金币/商店/目标美味值/事件/蛋糕等）继续调用这里的方法，签名保持不变。
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

        /// <summary>旧分类入口：无法区分消耗品子分类，保留供旧调用兼容。</summary>
        public int ModifyShopPrice(ShopEntryKind kind, int basePrice)
            => ModifyShopPrice(kind, string.Empty, basePrice);

        /// <summary>
        /// 按商品类型与具体商品计算折后价：各模型依持有顺序修正（折扣累乘 + 涨价累乘），下限 1。
        /// 消耗品折扣通过 <paramref name="itemId"/> 区分强化箱与调整单。
        /// </summary>
        public int ModifyShopPrice(ShopEntryKind kind, string itemId, int basePrice)
        {
            float price = basePrice;
            foreach (PassiveItemModel m in Models)
            {
                price = m.ModifyShopPrice(kind, itemId, price);
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

        public int ModifySlotSpinCost(int baseCost)
        {
            if (baseCost <= 0)
            {
                return 0;
            }

            float cost = baseCost;
            foreach (PassiveItemModel m in Models)
            {
                cost = m.ModifySlotSpinCost(cost);
            }

            return System.Math.Max(1, (int)System.Math.Floor(System.Math.Max(0f, cost) + 0.0001f));
        }

        /// <summary>是否禁止删除食物（负面「囤积癖」）。</summary>
        public bool BlockRemoveDish() => AnyFlag(m => m.BlockRemoveDish());

        /// <summary>指定商品分类是否自动补货；碎片没有任何默认补货。</summary>
        public bool AutoRestock(ShopEntryKind kind) => AnyFlag(m => m.AutoRestock(kind));

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

        // ================= 目标美味值修正族 =================

        /// <summary>按食物档位对要求分做百分比修正（可正可负，多件累加）。下限 1。</summary>
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

        /// <summary>食物奖励金币按百分比修正（累加；含负面「克扣工钱」）。下限 0。</summary>
        public int ModifyMealRewardGold(int baseGold)
        {
            int result = (int)System.Math.Round(baseGold * MealRewardGoldMultiplier(), System.MidpointRounding.AwayFromZero);
            return result < 0 ? 0 : result;
        }

        public float MealRewardGoldMultiplier()
        {
            float pct = 0f;
            foreach (PassiveItemModel m in Models)
            {
                pct += m.MealRewardGoldPct();
            }

            return System.Math.Max(0f, 1f + pct);
        }

        public int EventEnterGold() => SumInt(m => m.EventEnterGold());

        public int BossCompleteGold() => SumInt(m => m.BossCompleteGold());

        /// <summary>领取当前持有装饰品和消耗品的 星级评鉴完成奖励；一次性模型会在领取时写入自身状态。</summary>
        public int ClaimBossCompleteGold()
        {
            int total = 0;
            foreach (PassiveItemModel m in Models)
            {
                int amount = m.ClaimBossCompleteGold();
                if (amount <= 0)
                {
                    continue;
                }

                total += amount;
                m.Flash();
            }

            return total;
        }

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

        public int ExtraActiveSlots() => SumInt(m => m.ExtraActiveSlots());

        public bool BlocksActiveItems() => AnyFlag(m => m.BlocksActiveItems());

        public int FoodFlavorLimitBonus() => SumInt(m => m.FoodFlavorLimitBonus());

        public float DailyActionCostBonusDays() => SumFloat(m => m.DailyActionCostBonusDays());

        public int TimelineNodeRepeatCount() => System.Math.Max(1, MaxInt(m => m.TimelineNodeRepeatCount()));

        public int TimelineNodeRepeatCount(cfg.ActionBehavior behavior)
            => System.Math.Max(1, MaxInt(m => m.TimelineNodeRepeatCount(behavior)));

        public float TimelineStopChance()
        {
            float chance = SumFloat(m => m.TimelineStopChance());
            return System.Math.Max(0f, System.Math.Min(1f, chance));
        }

        /// <summary>新一周时间轴建立后，让当前持有的被动模型各自应用配置驱动的周效果。</summary>
        public void ApplyWeekTimelinePassives()
        {
            foreach (PassiveItemModel m in Models)
            {
                m.ApplyToWeekTimeline();
            }
        }

        public bool TryConsumeTimelineSkip(cfg.ActionBehavior behavior)
        {
            foreach (PassiveItemModel m in Models)
            {
                if (!m.SkipsTimelineBehavior(behavior))
                {
                    continue;
                }

                string itemId = m.ItemId;
                m.Flash();
                return _run.RemoveItem(itemId);
            }

            return false;
        }

        /// <summary>装饰品和消耗品指定的利息上限目标值（取最大）。</summary>
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

        /// <summary>是否持有「不死」装饰品和消耗品（名刀·加护）。</summary>
        public bool HasUndying() => AnyFlag(m => m.IsUndying());

        // ================= 上菜族 =================

        /// <summary>观星「每 N 次上菜后可预见」的周期（取最大；无则 0）。</summary>
        public int StarGazeEvery() => MaxInt(m => m.StarGazeEvery());

        /// <summary>观星「每局前 N 次上菜可预见」的次数（取最大；无则 0）。</summary>
        public int StarGazeFirst() => MaxInt(m => m.StarGazeFirst());

        /// <summary>每场经营挑战可额外丢弃的出菜数量（各装饰品和消耗品累加）。</summary>
        public int FoodDiscardLimitBonus() => SumInt(m => m.FoodDiscardLimitBonus());

        public int HeartCapacityBonus() => SumInt(m => m.HeartCapacityBonus());

        /// <summary>
        /// 当前每场营业 / 星级评鉴的食物丢弃次数上限。
        /// 局外 HUD 与新建经营挑战会话共用这一个入口，避免两边数值漂移。
        /// </summary>
        public int FoodDiscardCapacity()
        {
            if (_run == null)
            {
                return 0;
            }

            int baseLimit = _run.Tables?.TbGameBase?.FoodDeleteCount ?? 0;
            return System.Math.Max(0, baseLimit + FoodDiscardLimitBonus());
        }

        // ================= 奖励 / 多选一族 =================

        /// <summary>多选一可选「数量」增减（各模型累加）。</summary>
        public int ChoiceCountDelta() => SumInt(m => m.ChoiceCountDelta());

        /// <summary>多选一可选「次数」增加（各模型累加）。</summary>
        public int ChoiceTimesBonus() => SumInt(m => m.ChoiceTimesBonus());

        public void NotifyRewardAbandoned()
        {
            foreach (PassiveItemModel m in Models)
            {
                m.OnRewardAbandoned();
            }
        }

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

        /// <summary>结算 Food 经营挑战，并收集需要走独立通用领奖队列的被动奖励。</summary>
        public IReadOnlyList<FoodSettlementReward> OnFoodBattleSettled(
            ActionExecutionContext actionContext,
            bool survived,
            IRandomStream rng)
        {
            var rewards = new List<FoodSettlementReward>();
            foreach (PassiveItemModel m in Models)
            {
                RewardOffer offer = m.OnFoodBattleSettled(actionContext, survived, rng);
                if (offer != null)
                {
                    rewards.Add(new FoodSettlementReward(
                        m.ItemId,
                        m.FoodBattleSettlementRewardTitle,
                        offer));
                }
            }

            return rewards;
        }

        /// <summary>每次成功传递时，目标在本次结算获得的倍率加值。</summary>
        public float SweetTransferTargetMultiplier()
        {
            float total = 0f;
            foreach (PassiveItemModel m in Models)
            {
                if (m.TryGetSweetTransferTargetMultiplier(out float v) && v > 0f)
                {
                    total += v;
                }
            }

            return total;
        }

        /// <summary>每次成功传递时，来源在本次结算获得的倍率加值。</summary>
        public float SweetTransferSourceMultiplier()
        {
            float total = 0f;
            foreach (PassiveItemModel m in Models)
            {
                if (m.TryGetSweetTransferSourceMultiplier(out float v) && v > 0f)
                {
                    total += v;
                }
            }

            return total;
        }

        /// <summary>每次成功传递时，目标永久分数的累加值。</summary>
        public float SweetTransferTargetFlat()
        {
            float total = 0f;
            foreach (PassiveItemModel m in Models)
            {
                if (m.TryGetSweetTransferTargetFlat(out float v) && v > 0f)
                {
                    total += v;
                }
            }

            return total;
        }

        /// <summary>每次成功传递时，来源永久分数的累加值。</summary>
        public float SweetTransferSourceFlat()
        {
            float total = 0f;
            foreach (PassiveItemModel m in Models)
            {
                if (m.TryGetSweetTransferSourceFlat(out float v) && v > 0f)
                {
                    total += v;
                }
            }

            return total;
        }

        public int SweetTransferExtraTargetCount()
            => System.Math.Max(0, SumInt(m => m.SweetTransferExtraTargetCount()));

        public IReadOnlyList<SweetTransferExtraTargetRollSpec> SweetTransferExtraTargetRolls()
        {
            var result = new List<SweetTransferExtraTargetRollSpec>();
            foreach (PassiveItemModel model in Models)
            {
                foreach (SweetTransferExtraTargetRollSpec spec in model.SweetTransferExtraTargetRolls())
                {
                    if (spec.IsValid)
                    {
                        result.Add(spec);
                    }
                }
            }

            return result;
        }

        // ================= 事件 / 行动概率族 =================

        /// <summary>遇到奖励事件的额外概率（累加）。</summary>
        public float LuckyEventChanceBonus() => SumFloat(m => m.LuckyEventChanceBonus());

        /// <summary>遇到事件的额外概率（累加）。</summary>
        public float MoreEventsBonus() => SumFloat(m => m.MoreEventsBonus());

        /// <summary>事件类别的权重与上下保底目标增幅（累加）。</summary>
        public float EventActionLargeGroupWeightBonus() =>
            SumFloat(m => m.EventActionLargeGroupWeightBonus());

        /// <summary>火热类别的权重与上下保底目标增幅（累加）。</summary>
        public float SuperActionLargeGroupWeightBonus() =>
            SumFloat(m => m.SuperActionLargeGroupWeightBonus());

        /// <summary>抽奖机中奖归一概率增幅（累加）。</summary>
        public float SlotWinChanceBonus() => SumFloat(m => m.SlotWinChanceBonus());

        /// <summary>奖励保底前需要经历的自然事件抽取数（取最大；无则 0）。</summary>
        public int LuckyEventGuaranteeEvery() => MaxInt(m => m.LuckyEventGuaranteeEvery());

        // ================= 隐藏分族 =================

        /// <summary>按用途聚合持有装饰品的隐藏分常驻修正。</summary>
        public float HiddenScoreOffset(HiddenScorePurpose purpose) => SumFloat(m => m.HiddenScoreOffset(purpose));

        public float ItemLuckOffset() => HiddenScoreOffset(HiddenScorePurpose.ItemLuck);

        /// <summary>旧通用奖励隐藏分入口；保留为食物奖励隐藏分修正的兼容别名。</summary>
        public float HiddenScoreBonus() => HiddenScoreOffset(HiddenScorePurpose.Dish);

        // ================= 蛋糕层数族 =================

        public int CakeInitialLayers() => SumInt(m => m.CakeInitialLayers());

        public int CakeThresholdReduction() => SumInt(m => m.CakeThresholdReduction());

        public int CakeAccelBonus() => SumInt(m => m.CakeAccelBonus());

        public int GoldForCakeLayers(int happyCakeLayers) => SumInt(m => m.GoldForCakeLayers(happyCakeLayers));

        /// <summary>跨经营挑战保留的蛋糕层数比例（取最大；0 表示不保留）。</summary>
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
