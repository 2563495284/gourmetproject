namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>结算演出 cue 揭示的行为通道，供 tips 渐进显示对应信息。</summary>
    public enum SettlementRevealChannel
    {
        None = 0,
        Flat = 1,
        Multiplier = 2,
        CopySkill = 3,
        SweetTransfer = 4,
    }

    /// <summary>
    /// 一个结算 cue 播放时对外发出的「揭示信号」：告诉 tips 该道菜刚表演到什么行为。
    /// </summary>
    public readonly struct SettlementRevealSignal
    {
        public static readonly SettlementRevealSignal None = new SettlementRevealSignal(SettlementRevealChannel.None, 0, 0f, 0);

        public SettlementRevealSignal(SettlementRevealChannel channel, int dishInstanceId, float after, int count)
        {
            Channel = channel;
            DishInstanceId = dishInstanceId;
            After = after;
            Count = count;
        }

        public SettlementRevealChannel Channel { get; }

        public int DishInstanceId { get; }

        /// <summary>Flat / Multiplier 通道：该维度累加后的当前值（ScoreLine.After）。</summary>
        public float After { get; }

        /// <summary>CopySkill / SweetTransfer 通道：本次新增的技能条目数量。</summary>
        public int Count { get; }
    }
}
