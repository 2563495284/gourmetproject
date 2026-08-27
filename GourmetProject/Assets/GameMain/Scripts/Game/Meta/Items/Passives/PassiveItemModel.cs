using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Scoring;

namespace GourmetProject.Game.Meta.Passives
{
    /// <summary>
    /// 装饰品「模型」基类（对标杀戮尖塔2 的 RelicModel/AbstractModel）：
    /// 每个装饰品 id 对应一个子类，只覆写自己关心的钩子；数值来自 <see cref="Definition"/>（Luban 配置），
    /// per-instance 运行时状态由子类字段持有并通过 <see cref="CaptureState"/>/<see cref="RestoreState"/> 序列化。
    /// 分发器 <see cref="ItemRuntime"/> 只遍历「在场（持有）」模型折叠结果——未持有即无模型、无副作用。
    /// 所有钩子默认空实现/透传；聚合语义（求和/取最大/累乘）由分发器决定。
    /// </summary>
    public abstract class PassiveItemModel
    {
        public event System.Action<PassiveItemModel> Flashed;

        public event System.Action<PassiveItemModel> IconStateChanged;

        public event System.Action<PassiveItemModel> InfoTextChanged;

        private bool _iconUsed;

        protected GameRun Run { get; private set; }

        protected ItemDefinition Definition { get; private set; }

        protected RunItemState State { get; private set; }

        /// <summary>
        /// 当前模型对应的那一件装饰品和消耗品是否仍在持有列表中。
        /// 经营挑战会话可能仍保存模型注册过的事件委托；必须比较实例而不只比较 ID，
        /// 避免旧装饰品和消耗品移除后又获得同 ID 时，旧模型也重新产生效果。
        /// </summary>
        protected bool IsStillHeld
            => Run != null && ReferenceEquals(Run.GetItemState(ItemId), State);

        /// <summary>配置 effectValue。</summary>
        protected float Value => Definition?.EffectValue ?? 0f;

        /// <summary>配置 effectParam。</summary>
        protected string Param => Definition?.EffectParam ?? string.Empty;

        public string ItemId => Definition?.Id ?? string.Empty;

        public ItemDefinition Def => Definition;

        public virtual bool IsIconUsed => _iconUsed;

        public bool IsIconWax => false;

        public virtual string InfoText => string.Empty;

        public void Flash()
        {
            Flashed?.Invoke(this);
        }

        protected void MarkIconUsed()
        {
            SetIconUsed(true);
        }

        protected void SetIconUsed(bool used)
        {
            if (_iconUsed == used)
            {
                return;
            }

            _iconUsed = used;
            IconStateChanged?.Invoke(this);
        }

        public void RefreshIconState()
        {
            IconStateChanged?.Invoke(this);
        }

        public void RefreshInfoText()
        {
            InfoTextChanged?.Invoke(this);
        }

        public void Bind(GameRun run, ItemDefinition definition, RunItemState state)
        {
            Run = run;
            Definition = definition;
            State = state;
        }

        // ================= 生命周期 =================

        /// <summary>装饰品首次加入持有列表后立即结算一次（替代 PassiveOnAcquireEffects）。</summary>
        public virtual void OnAcquired()
        {
        }

        /// <summary>装饰品从持有列表移除时（很少用到）。</summary>
        public virtual void OnRemoved()
        {
        }

        /// <summary>
        /// 新一周时间轴建立后，把本模型声明的每周效果应用到运行态时间轴。
        /// 默认无效果；由 <see cref="ItemRuntime.ApplyWeekTimelinePassives"/> 统一派发。
        /// </summary>
        public virtual void ApplyToWeekTimeline()
        {
        }

        // ================= 价格族（透传折叠：price => price'） =================

        public virtual float ModifyShopPrice(ShopEntryKind kind, float price) => price;

        /// <summary>
        /// 按具体商品修正商店价格。默认回退到旧的仅分类重载，保证已有模型和旧调用继续生效；
        /// 需要区分强化/调整消耗品的模型可通过 <paramref name="itemId"/> 查询消耗品分类。
        /// </summary>
        public virtual float ModifyShopPrice(ShopEntryKind kind, string itemId, float price)
            => ModifyShopPrice(kind, price);

        public virtual float ModifyDeletePrice(float price) => price;

        /// <summary>抽奖机付费抽奖价格修正；免费抽奖不会调用出正数。</summary>
        public virtual float ModifySlotSpinCost(float cost) => cost;

        /// <summary>删牌固定价（取最低）。返回 false 表示不提供固定价。</summary>
        public virtual bool TryGetRemovePriceFixed(out int fixedPrice)
        {
            fixedPrice = 0;
            return false;
        }

        public virtual bool BlockRemoveDish() => false;

        public virtual bool AutoRestock(ShopEntryKind kind) => false;

        // ================= 目标美味值族（百分比累加） =================

        /// <summary>对某档位要求分的百分比修正（可正可负；分发器累加）。</summary>
        public virtual float RequiredScorePct(cfg.FoodActionKind tier) => 0f;

        // ================= 金币 / 利息族 =================

        /// <summary>食物奖励金币百分比修正（累加）。</summary>
        public virtual float MealRewardGoldPct() => 0f;

        /// <summary>每次进入一个根事件页面时获得的金币。</summary>
        public virtual int EventEnterGold() => 0;

        public virtual int BossCompleteGold() => 0;

        /// <summary>结算一次 星级评鉴完成奖励；可由有一次性状态的模型在此消费奖励资格。</summary>
        public virtual int ClaimBossCompleteGold() => BossCompleteGold();

        public virtual int MealBonusGoldPerMeal() => 0;

        public virtual int ShopEnterGold() => 0;

        public virtual int ActiveUseGold() => 0;

        /// <summary>周末金币保底目标（取最大）。返回 false 表示不提供。</summary>
        public virtual bool TryGetMinGoldGuarantee(out int value)
        {
            value = 0;
            return false;
        }

        public virtual int ExtraActiveSlots() => 0;

        public virtual bool BlocksActiveItems() => false;

        public virtual int FoodFlavorLimitBonus() => 0;

        /// <summary>普通行动固定增加的耗时天数；节点行动和休息不调用。</summary>
        public virtual float DailyActionCostBonusDays() => 0f;

        /// <summary>自然经过的非 星级评鉴节点执行次数。</summary>
        public virtual int TimelineNodeRepeatCount() => 1;

        /// <summary>按节点行为决定执行次数；默认兼容旧的无参数钩子。</summary>
        public virtual int TimelineNodeRepeatCount(cfg.ActionBehavior behavior) => TimelineNodeRepeatCount();

        /// <summary>普通行动经过节点日时，时间轴停摆概率。</summary>
        public virtual float TimelineStopChance() => 0f;

        /// <summary>是否消费本被动跳过指定类型的下一个节点。</summary>
        public virtual bool SkipsTimelineBehavior(cfg.ActionBehavior behavior) => false;

        /// <summary>利息节点单次上限目标值（取最大）。返回 false 表示不提供。</summary>
        public virtual bool TryGetInterestCapOverride(out int value)
        {
            value = 0;
            return false;
        }

        // ================= 不死族 =================

        /// <summary>是否为「不死」装饰品和消耗品（供保底与失败判定；本模型对应装饰品和消耗品会被消耗移除）。</summary>
        public virtual bool IsUndying() => false;

        /// <summary>不死生效后要把红心恢复到的目标颗数（不超过上限）。默认 1。</summary>
        public virtual int UndyingRestoreHearts() => 1;

        // ================= 上菜族 =================

        /// <summary>观星「每 N 次上菜后可预见」的周期（无则 0）。</summary>
        public virtual int StarGazeEvery() => 0;

        /// <summary>观星「每局前 N 次上菜可预见」的次数（无则 0）。</summary>
        public virtual int StarGazeFirst() => 0;

        /// <summary>每场经营挑战可额外丢弃的出菜数量（各装饰品和消耗品累加）。</summary>
        public virtual int FoodDiscardLimitBonus() => 0;

        /// <summary>持有期间提供的红心上限加成。</summary>
        public virtual int HeartCapacityBonus() => 0;

        // ================= 奖励 / 多选一族 =================

        /// <summary>多选一可选「数量」增减（分发器累加）。</summary>
        public virtual int ChoiceCountDelta() => 0;

        /// <summary>多选一可选「次数」增加（分发器累加）。</summary>
        public virtual int ChoiceTimesBonus() => 0;

        /// <summary>玩家明确放弃了至少一个未领取完的奖励组。</summary>
        public virtual void OnRewardAbandoned()
        {
        }

        /// <summary>经营挑战胜利奖励生成后，允许装饰品追加奖励组。</summary>
        public virtual RewardOffer ModifyBattleRewardOffer(
            RewardOffer offer,
            ActionExecutionContext actionContext,
            IRandomStream rng) => offer;

        /// <summary>
        /// 一场 Food 经营挑战完成时触发。与胜利奖励生成解耦，因此失败但仍存活时也会调用；
        /// <paramref name="survived"/> 为 false 时，模型仍可累计进度，但不应发放续局奖励。
        /// </summary>
        public virtual RewardOffer OnFoodBattleSettled(
            ActionExecutionContext actionContext,
            bool survived,
            IRandomStream rng) => null;

        public virtual string FoodBattleSettlementRewardTitle => Def?.Name ?? "额外奖励";

        // ================= 事件 / 行动概率族 =================

        /// <summary>遇到奖励事件的额外概率（分发器累加）。</summary>
        public virtual float LuckyEventChanceBonus() => 0f;

        /// <summary>遇到事件的额外概率（分发器累加）。</summary>
        public virtual float MoreEventsBonus() => 0f;

        /// <summary>事件类别的权重与上下保底目标增幅（分发器累加）。</summary>
        public virtual float EventActionLargeGroupWeightBonus() => 0f;

        /// <summary>火热类别的权重与上下保底目标增幅（分发器累加）。</summary>
        public virtual float SuperActionLargeGroupWeightBonus() => 0f;

        /// <summary>抽奖机中奖归一概率增幅（分发器累加）。</summary>
        public virtual float SlotWinChanceBonus() => 0f;

        /// <summary>奖励保底前需要经历的自然事件抽取数（取最大；无则 0）。</summary>
        public virtual int LuckyEventGuaranteeEvery() => 0;

        /// <summary>LuckyEventGuarantee 保底计数：自上次保底以来已完成的自然 act_event 抽取数。</summary>
        public virtual int EventGuaranteeStreak => 0;

        public virtual void IncrementEventGuaranteeStreak()
        {
        }

        public virtual void ResetEventGuaranteeStreak()
        {
        }

        // ================= 进度修正族（隐藏分 / 装饰品运气） =================

        /// <summary>按用途提供进度常驻修正（分发器累加）。</summary>
        public virtual float HiddenScoreOffset(HiddenScorePurpose purpose) => Def?.HiddenScoreOffset(purpose) ?? 0f;

        /// <summary>旧通用奖励隐藏分入口；保留为食物奖励隐藏分修正的兼容别名。</summary>
        public virtual float HiddenScoreBonus() => HiddenScoreOffset(HiddenScorePurpose.Dish);

        // ================= 蛋糕层数族 =================

        public virtual int CakeInitialLayers() => 0;

        public virtual int CakeThresholdReduction() => 0;

        public virtual int CakeAccelBonus() => 0;

        /// <summary>最终蛋糕层数满足条件时给予的金币。</summary>
        public virtual int GoldForCakeLayers(int happyCakeLayers) => 0;

        /// <summary>跨经营挑战保留的蛋糕层数比例（取最大）。返回 false 表示不提供。</summary>
        public virtual bool TryGetCakeRetainFraction(out float value)
        {
            value = 0f;
            return false;
        }

        // ================= 经营挑战注入 / 结算族 =================

        /// <summary>把局级修正注入经营挑战会话（替代 PassiveItemEffectRegistry；Final 加/乘等）。</summary>
        public virtual void ApplyToBattle(BattleSession session)
        {
        }

        public virtual void OnSweetTransferTriggered(SweetTransferOccurrence occurrence)
        {
        }

        /// <summary>每传递到一个目标时，该目标在本次结算获得的倍率加值。</summary>
        public virtual bool TryGetSweetTransferTargetMultiplier(out float value)
        {
            value = 0f;
            return false;
        }

        /// <summary>每传递到一个目标时，来源在本次结算获得的倍率加值。</summary>
        public virtual bool TryGetSweetTransferSourceMultiplier(out float value)
        {
            value = 0f;
            return false;
        }

        /// <summary>每传递到一个目标时，该目标永久分数的累加值。</summary>
        public virtual bool TryGetSweetTransferTargetFlat(out float value)
        {
            value = 0f;
            return false;
        }

        /// <summary>每传递到一个目标时，来源永久分数的累加值。</summary>
        public virtual bool TryGetSweetTransferSourceFlat(out float value)
        {
            value = 0f;
            return false;
        }

        /// <summary>每次甜蜜传递额外选择的目标数。</summary>
        public virtual int SweetTransferExtraTargetCount() => 0;

        /// <summary>每次甜蜜传递独立判定的额外目标加权随机规格。</summary>
        public virtual IEnumerable<SweetTransferExtraTargetRollSpec> SweetTransferExtraTargetRolls()
        {
            yield break;
        }

        /// <summary>贡献逐菜/条件/顺序类结算规格（替代 ItemScoreEffectAdapter 的 effectType 映射）。</summary>
        public virtual IEnumerable<ItemScoreSpec> BuildScoreSpecs()
        {
            yield break;
        }

        // ================= per-instance 状态序列化 =================

        /// <summary>导出可序列化状态（默认只保存图标 used 状态）。</summary>
        public virtual string CaptureState() => CaptureIconState();

        /// <summary>从存档恢复状态（默认只恢复图标 used 状态）。</summary>
        public virtual void RestoreState(string data)
        {
            RestoreIconState(data);
        }

        protected string CaptureIconState() => IsIconUsed ? "used:1" : string.Empty;

        protected void RestoreIconState(string data)
        {
            _iconUsed = ParseStateBool(data, "used", false);
        }

        protected static int ParseStateInt(string data, string key, int fallback)
        {
            if (string.IsNullOrEmpty(data))
            {
                return fallback;
            }

            foreach (string token in data.Split(';', ',', '|'))
            {
                int idx = token.IndexOf(':');
                if (idx < 0)
                {
                    continue;
                }

                if (string.Equals(token.Substring(0, idx).Trim(), key, System.StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(token.Substring(idx + 1).Trim(), out int value))
                {
                    return value;
                }
            }

            return int.TryParse(data, out int raw) ? raw : fallback;
        }

        protected static bool ParseStateBool(string data, string key, bool fallback)
        {
            int value = ParseStateInt(data, key, fallback ? 1 : 0);
            return value != 0;
        }

        protected static string ParseStateString(string data, string key, string fallback = "")
        {
            if (string.IsNullOrEmpty(data))
            {
                return fallback ?? string.Empty;
            }

            foreach (string token in data.Split(';', ',', '|'))
            {
                int idx = token.IndexOf(':');
                if (idx < 0)
                {
                    continue;
                }

                if (string.Equals(token.Substring(0, idx).Trim(), key, System.StringComparison.OrdinalIgnoreCase))
                {
                    return token.Substring(idx + 1).Trim();
                }
            }

            return fallback ?? string.Empty;
        }

        protected static string JoinState(params string[] entries)
        {
            var parts = new List<string>();
            if (entries != null)
            {
                foreach (string entry in entries)
                {
                    if (!string.IsNullOrEmpty(entry))
                    {
                        parts.Add(entry);
                    }
                }
            }

            return string.Join(";", parts);
        }
    }
}
