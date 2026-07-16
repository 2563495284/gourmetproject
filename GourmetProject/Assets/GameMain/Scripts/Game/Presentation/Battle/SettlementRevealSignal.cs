namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 一个结算 cue 播放时对外发出的「揭示信号」：可组合，一条 cue 可同时揭示
    /// 分数 / 倍率 / 复制技能 / 甜蜜传递卡片（例如目标菜触发甜蜜传递效果时，既揭示加分又揭示传递卡片）。
    /// </summary>
    public readonly struct SettlementRevealSignal
    {
        public static readonly SettlementRevealSignal None = default;

        public SettlementRevealSignal(
            int dishInstanceId,
            bool hasFlat,
            float flat,
            bool hasMultiplier,
            float multiplier,
            int copySkillDelta,
            int transferredDelta)
        {
            DishInstanceId = dishInstanceId;
            HasFlat = hasFlat;
            Flat = flat;
            HasMultiplier = hasMultiplier;
            Multiplier = multiplier;
            CopySkillDelta = copySkillDelta;
            TransferredDelta = transferredDelta;
        }

        public int DishInstanceId { get; }

        /// <summary>是否揭示「加法分」维度；<see cref="Flat"/> 为该维度累加后的当前值（ScoreLine.After）。</summary>
        public bool HasFlat { get; }

        public float Flat { get; }

        /// <summary>是否揭示「乘区」维度；<see cref="Multiplier"/> 为该维度累加后的当前值（ScoreLine.After）。</summary>
        public bool HasMultiplier { get; }

        public float Multiplier { get; }

        /// <summary>本次新揭示的复制技能条数（追加进技能列表末尾）。</summary>
        public int CopySkillDelta { get; }

        /// <summary>本次新揭示的甜蜜传递子技能条数（追加进传递列表末尾）。</summary>
        public int TransferredDelta { get; }

        public bool IsEmpty => !HasFlat && !HasMultiplier && CopySkillDelta == 0 && TransferredDelta == 0;

        public static SettlementRevealSignal FlatReveal(int dishInstanceId, float flatAfter, int transferredDelta = 0)
        {
            return new SettlementRevealSignal(dishInstanceId, true, flatAfter, false, 0f, 0, transferredDelta);
        }

        public static SettlementRevealSignal MultiplierReveal(int dishInstanceId, float multiplierAfter, int transferredDelta = 0)
        {
            return new SettlementRevealSignal(dishInstanceId, false, 0f, true, multiplierAfter, 0, transferredDelta);
        }

        public static SettlementRevealSignal CopySkillReveal(int dishInstanceId, int count)
        {
            return new SettlementRevealSignal(dishInstanceId, false, 0f, false, 0f, count, 0);
        }
    }
}
