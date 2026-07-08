namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 道具「结算类」效果类型（纯玩法层，不依赖 cfg/Game）。
    /// 由 Game 层把 cfg.Item.EffectType 字符串映射到本枚举后，交给
    /// <see cref="Scoring.ItemScoreEffectSource"/> 在结算管线中执行。
    /// 仅覆盖影响「一局结算」的被动道具；金币/商店/时间轴等局外效果不走这里。
    /// </summary>
    public enum ItemScoreEffectType
    {
        None = 0,

        /// <summary>最终总分加法 +value（大餐盘）。</summary>
        FinalAddFlat = 1,

        /// <summary>最终总分乘区 ×value（主厨刀）。</summary>
        FinalAddMult = 2,

        /// <summary>每道匹配 <c>param</c> 的菜额外 +value 美味度（胡椒罐：带某标签/风味/分类）。</summary>
        TagBonus = 3,

        /// <summary>所有食物基础分永久 +value（写回实例，跨结算累积）。</summary>
        PermanentAddFlatAll = 4,

        /// <summary>所有食物永久乘区 ×(1+value)（写回实例，跨结算累积）。</summary>
        PermanentAddMultAll = 5,

        /// <summary>食物数量与阈值比较满足时，最终总分乘区 ×value（数量检测；param: "lte:N" 或 "gte:N"）。</summary>
        CountThresholdFinalMult = 6,

        /// <summary>结算时每道菜倍率 +value×(食物总数)（每结算 1 个食物 +value 倍）。</summary>
        PerDishSettledMultFlat = 7,

        /// <summary>结算时每道菜倍率 +value×(棋盘技能总数)（每一个技能 +value 倍）。</summary>
        PerSkillMultFlat = 8,

        /// <summary>上菜顺序第 N 个（1-based）的菜倍率 ×value；param: "index:N"，N=-1 表示最后一个。</summary>
        NthServeMult = 9,

        /// <summary>每上 N 个食物后的「下一个」食物倍率 +value；param: "every:N"。</summary>
        EveryNthServeMult = 10,

        /// <summary>所有食物额外「视为」+value 个食物（影响计数类前提；本次结算 live 生效）。</summary>
        CountAsBonusAll = 11,
    }
}
