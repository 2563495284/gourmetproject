using System.Collections.Generic;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Scoring;

namespace GourmetProject.Game.Meta.Passives
{
    /// <summary>
    /// 被动道具「模型」基类（对标杀戮尖塔2 的 RelicModel/AbstractModel）：
    /// 每个被动道具 id 对应一个子类，只覆写自己关心的钩子；数值来自 <see cref="Definition"/>（Luban 配置），
    /// per-instance 运行时状态由子类字段持有并通过 <see cref="CaptureState"/>/<see cref="RestoreState"/> 序列化。
    /// 分发器 <see cref="ItemRuntime"/> 只遍历「在场（持有）」模型折叠结果——未持有即无模型、无副作用。
    /// 所有钩子默认空实现/透传；聚合语义（求和/取最大/累乘）由分发器决定。
    /// </summary>
    public abstract class PassiveItemModel
    {
        public event System.Action<PassiveItemModel> Flashed;

        public event System.Action<PassiveItemModel> IconStateChanged;

        private bool _iconUsed;

        protected GameRun Run { get; private set; }

        protected ItemDefinition Definition { get; private set; }

        protected RunItemState State { get; private set; }

        /// <summary>配置 effectValue。</summary>
        protected float Value => Definition?.EffectValue ?? 0f;

        /// <summary>配置 effectParam。</summary>
        protected string Param => Definition?.EffectParam ?? string.Empty;

        public string ItemId => Definition?.Id ?? string.Empty;

        public ItemDefinition Def => Definition;

        public virtual bool IsIconUsed => _iconUsed;

        public bool IsIconWax => false;

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

        public void Bind(GameRun run, ItemDefinition definition, RunItemState state)
        {
            Run = run;
            Definition = definition;
            State = state;
        }

        // ================= 生命周期 =================

        /// <summary>被动道具首次加入持有列表后立即结算一次（替代 PassiveOnAcquireEffects）。</summary>
        public virtual void OnAcquired()
        {
        }

        /// <summary>被动道具从持有列表移除时（很少用到）。</summary>
        public virtual void OnRemoved()
        {
        }

        // ================= 价格族（透传折叠：price => price'） =================

        public virtual float ModifyShopPrice(ShopEntryKind kind, float price) => price;

        public virtual float ModifyRecipeBookPrice(float price) => price;

        public virtual float ModifyDeletePrice(float price) => price;

        /// <summary>删牌固定价（取最低）。返回 false 表示不提供固定价。</summary>
        public virtual bool TryGetRemovePriceFixed(out int fixedPrice)
        {
            fixedPrice = 0;
            return false;
        }

        public virtual bool BlockRemoveDish() => false;

        public virtual bool AutoRestock() => false;

        // ================= 目标分族（百分比累加） =================

        /// <summary>对某档位要求分的百分比修正（可正可负；分发器累加）。</summary>
        public virtual float RequiredScorePct(MealTier tier) => 0f;

        // ================= 金币 / 利息族 =================

        /// <summary>美食奖励金币百分比修正（累加）。</summary>
        public virtual float MealRewardGoldPct() => 0f;

        public virtual int EventCompleteGold() => 0;

        public virtual int BossCompleteGold() => 0;

        public virtual int MealBonusGoldPerMeal() => 0;

        public virtual int ShopEnterGold() => 0;

        public virtual int ActiveUseGold() => 0;

        /// <summary>周末金币保底目标（取最大）。返回 false 表示不提供。</summary>
        public virtual bool TryGetMinGoldGuarantee(out int value)
        {
            value = 0;
            return false;
        }

        public virtual bool ClearsGoldOnWeekEnd() => false;

        public virtual int ExtraActiveSlots() => 0;

        public virtual bool BlocksActiveItems() => false;

        public virtual int FoodFlavorLimitBonus() => 0;

        public virtual bool HasExtraInterest() => false;

        /// <summary>利息节点单次上限目标值（取最大）。返回 false 表示不提供。</summary>
        public virtual bool TryGetInterestCapOverride(out int value)
        {
            value = 0;
            return false;
        }

        public virtual int GoldPerUnusedAdjust() => 0;

        // ================= 不死族 =================

        /// <summary>是否为「不死」道具（供保底与失败判定；本模型对应道具会被消耗移除）。</summary>
        public virtual bool IsUndying() => false;

        // ================= 上菜 / 调整族 =================

        public virtual int AdjustCountBonus() => 0;

        /// <summary>每个未使用调整次数带来的倍率加成（取最大）。返回 false 表示不提供。</summary>
        public virtual bool TryGetAdjustToMult(out float value)
        {
            value = 0f;
            return false;
        }

        public virtual bool FreeMoveFirstServe() => false;

        /// <summary>观星「每 N 次上菜后可预见」的周期（无则 0）。</summary>
        public virtual int StarGazeEvery() => 0;

        /// <summary>观星「每局前 N 次上菜可预见」的次数（无则 0）。</summary>
        public virtual int StarGazeFirst() => 0;

        // ================= 奖励 / 多选一族 =================

        /// <summary>多选一可选「数量」增减（分发器累加）。</summary>
        public virtual int ChoiceCountDelta() => 0;

        /// <summary>多选一可选「次数」增加（分发器累加）。</summary>
        public virtual int ChoiceTimesBonus() => 0;

        // ================= 事件 / 行动概率族 =================

        /// <summary>遇到奖励事件的额外概率（分发器累加）。</summary>
        public virtual float LuckyEventChanceBonus() => 0f;

        /// <summary>遇到事件的额外概率（分发器累加）。</summary>
        public virtual float MoreEventsBonus() => 0f;

        /// <summary>每 N 个事件保底一个奖励事件的周期（取最大；无则 0）。</summary>
        public virtual int LuckyEventGuaranteeEvery() => 0;

        /// <summary>LuckyEventGuarantee 保底计数：自上次保底奖励以来 act_event 抽到的 Event 型结果数。</summary>
        public virtual int EventGuaranteeStreak => 0;

        public virtual void IncrementEventGuaranteeStreak()
        {
        }

        public virtual void ResetEventGuaranteeStreak()
        {
        }

        // ================= 隐藏分族 =================

        /// <summary>奖励隐藏分加成（累加）。取代旧的 effectType=="HiddenScoreBonus"/"RewardHiddenBonus" 判定。</summary>
        public virtual float HiddenScoreBonus() => 0f;

        // ================= 蛋糕层数族 =================

        public virtual int CakeInitialLayers() => 0;

        public virtual int CakeThresholdReduction() => 0;

        public virtual int CakeAccelBonus() => 0;

        /// <summary>跨品鉴保留的蛋糕层数比例（取最大）。返回 false 表示不提供。</summary>
        public virtual bool TryGetCakeRetainFraction(out float value)
        {
            value = 0f;
            return false;
        }

        // ================= 战斗注入 / 结算族 =================

        /// <summary>把局级修正注入战斗会话（替代 PassiveItemEffectRegistry；Final 加/乘等）。</summary>
        public virtual void ApplyToBattle(BattleSession session)
        {
        }

        /// <summary>本次结算每道菜额外「视为食物数」加成（分发器累加）。</summary>
        public virtual int ExtraCountAsPerDish() => 0;

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
