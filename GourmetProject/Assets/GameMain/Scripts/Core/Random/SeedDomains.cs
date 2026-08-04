namespace GourmetProject.Core.Rng
{
    /// <summary>
    /// 随机种子的「系统域」常量。每个域从主种子独立派生一颗域种子，域内再按实例 key 派生流，
    /// 域之间互不污染：改动某个系统的随机消耗不会影响其它系统的序列。
    ///
    /// 约定：
    /// - 玩法域（map/action/effect/event/boss/shop/reward/recipe/combat）参与存档复现。
    /// - 表现域 <see cref="Cosmetic"/> 仅用于特效/抖动/飘字等表现层，禁止参与任何玩法判定，
    ///   且不写入存档（见 <see cref="RandomService.Capture"/>），以免改特效污染玩法复现。
    /// 通过 <see cref="RandomService.DomainStream"/> 取域内实例流。
    /// </summary>
    public static class SeedDomains
    {
        /// <summary>每周时间轴 / 关卡布局生成。</summary>
        public const string Map = "map";

        /// <summary>行动组调度与「n 选一」候选生成。</summary>
        public const string Action = "action";

        /// <summary>行动 / 事件效果结算、赌博、随机给予装饰品和消耗品。</summary>
        public const string Effect = "effect";

        /// <summary>随机事件抽取与选项结算。</summary>
        public const string Event = "event";

        /// <summary>抽奖机结果；与事件、通用奖励随机流隔离。</summary>
        public const string Slot = "slot";

        /// <summary>Boss 加权抽取。</summary>
        public const string Boss = "boss";

        /// <summary>商店刷新（食物 / 碎片池及槽位等非装饰品和消耗品池随机）。</summary>
        public const string Shop = "shop";

        /// <summary>
        /// 装饰品和消耗品池抽取专用域，与商店其它随机（食物/碎片/槽位）隔离，
        /// 避免调整装饰品和消耗品数量时污染同一刷新里的食物/碎片序列。当前由商店装饰品和消耗品栏接入，预留给奖励装饰品和消耗品。
        /// </summary>
        public const string Loot = "loot";

        /// <summary>过关奖励（金币区间、额外槽、候选池）。</summary>
        public const string Reward = "reward";

        /// <summary>经营挑战开局食谱食谱生成。</summary>
        public const string Recipe = "recipe";

        /// <summary>局内经营挑战上菜 / AI 决策。</summary>
        public const string Combat = "combat";

        /// <summary>
        /// 消耗品使用副作用（生成/复制/随机类效果落地）专用域。
        /// 按存档里的「消耗品使用序号」派生实例流，保证同种子同输入下第 K 次使用可复现。
        /// </summary>
        public const string Item = "item";

        /// <summary>表现层随机（特效、抖动、飘字等），不参与玩法复现，不入存档。</summary>
        public const string Cosmetic = "cosmetic";
    }
}
