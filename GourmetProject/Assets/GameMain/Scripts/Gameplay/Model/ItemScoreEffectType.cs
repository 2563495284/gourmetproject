namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 装饰品和消耗品「结算类」效果类型（纯玩法层，不依赖 cfg/Game）。
    /// 由 Game 层把装饰品 EffectType 字符串映射到本枚举后，交给
    /// <see cref="Scoring.ItemScoreEffectSource"/> 在结算管线中执行。
    /// 仅覆盖影响「一局结算」的装饰品；金币/商店/时间轴等局外效果不走这里。
    /// </summary>
    public enum ItemScoreEffectType
    {
        None = 0,

        /// <summary>保留兼容：最终总分加法 +value；当前无配置或模型产出。</summary>
        FinalAddFlat = 1,

        /// <summary>保留兼容：最终总分倍率 ×value；当前无配置或模型产出。</summary>
        FinalAddMult = 2,

        /// <summary>每道匹配 <c>param</c> 的菜额外 +value 美味值（胡椒罐：带某标签/风味/分类）。</summary>
        TagBonus = 3,

        /// <summary>所有食物基础分永久 +value（写回实例，跨结算累积）。</summary>
        PermanentAddFlatAll = 4,

        /// <summary>所有食物永久倍率 ×(1+value)（写回实例，跨结算累积）。</summary>
        PermanentAddMultAll = 5,

        /// <summary>结算开始时，食物份数与阈值比较满足则所有参与结算的食物倍率加区 +value（param: "lte:N" 或 "gte:N"）。</summary>
        CountThresholdAllDishMult = 6,

        /// <summary>结算时每个食物倍率 +value×(食物总数)（每结算 1 个食物 +value 倍）。</summary>
        PerDishSettledMultFlat = 7,

        /// <summary>结算时每个食物倍率 +value×(餐桌技能总数)（每一个技能 +value 倍）。</summary>
        PerSkillMultFlat = 8,

        /// <summary>上菜顺序第 N 个（1-based）的菜倍率 ×value；param: "index:N"，N=-1 表示最后一个。</summary>
        NthServeMult = 9,

        /// <summary>每上 N 个食物后的「下一个」食物倍率 +value；param: "every:N"。</summary>
        EveryNthServeMult = 10,

        /// <summary>所有食物额外「视为」+value 个食物（影响计数类前提；本次结算 live 生效）。</summary>
        CountAsBonusAll = 11,

        /// <summary>上菜顺序第 N 个（1-based）的菜倍率 +value；param: "index:N"，N=-1 表示最后一个。</summary>
        NthServeMultFlat = 12,

        /// <summary>结算开始时，所有参与结算的食物加法区 +value。</summary>
        AllDishFlat = 13,

        /// <summary>结算开始时，所有参与结算的食物倍率加区 +value。</summary>
        AllDishMultFlat = 14,

        /// <summary>结算开始时，匹配 param 的食物倍率加区 +value。</summary>
        TagMultFlat = 15,

        /// <summary>结算开始时，匹配 param 的食物额外视为 +value 份。</summary>
        TagCountAsBonus = 16,

        /// <summary>结算开始时，所有食物临时视为 param 指定的分类。</summary>
        AllDishTemporaryCategory = 17,

        /// <summary>每个未使用的丢弃次数令所有食物分数 +value。</summary>
        AllDishFlatPerUnusedDiscard = 18,

        /// <summary>每个未使用的丢弃次数令所有食物倍率 +value。</summary>
        AllDishMultPerUnusedDiscard = 19,

        /// <summary>每个餐桌空格令所有食物分数 +value。</summary>
        AllDishFlatPerEmptyCell = 20,

        /// <summary>同本体食物出现至少两份时，各自倍率 +value。</summary>
        SameBaseDishMultFlat = 21,

        /// <summary>每道食物开始结算时，自身永久分数 +value。</summary>
        PerDishPermanentFlat = 22,

        /// <summary>食物数量与阈值比较满足时，所有食物倍率加区 +value（param: "lte:N" 或 "gte:N"）。</summary>
        CountThresholdFinalMult = 23,

        /// <summary>结算结束时，每道食物加法区 +value×自身有效份数。</summary>
        PerDishFlatTimesOwnCountAs = 24,

        /// <summary>结算结束时，每道食物倍率加区 +value×自身有效份数。</summary>
        PerDishMultFlatTimesOwnCountAs = 25,

        /// <summary>结算开始时，随机 value 道食物临时视为 param 指定分类。</summary>
        RandomDishesTemporaryCategory = 26,

        /// <summary>每结算一道当时视为蛋糕的食物，欢乐蛋糕层数 +value。</summary>
        CakeLayersPerCakeDish = 27,
    }
}
