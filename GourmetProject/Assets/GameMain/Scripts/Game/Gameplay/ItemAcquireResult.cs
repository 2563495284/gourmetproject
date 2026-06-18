namespace GourmetProject.Game.Gameplay
{
    public enum ItemAcquireOutcome
    {
        None = 0,
        Added = 1,
        Upgraded = 2,
        Stacked = 3,
        ConvertedToGold = 4,
    }

    /// <summary>一次道具获得结算的结果，供奖励、事件、商店生成反馈。</summary>
    public readonly struct ItemAcquireResult
    {
        public ItemAcquireResult(ItemAcquireOutcome outcome, string itemId, string itemName, int level, int count, int gold)
        {
            Outcome = outcome;
            ItemId = itemId;
            ItemName = itemName;
            Level = level;
            Count = count;
            Gold = gold;
        }

        public ItemAcquireOutcome Outcome { get; }

        public string ItemId { get; }

        public string ItemName { get; }

        public int Level { get; }

        public int Count { get; }

        public int Gold { get; }

        public bool HasItem => !string.IsNullOrEmpty(ItemId);

        public string ToRewardText(string prefix)
        {
            switch (Outcome)
            {
                case ItemAcquireOutcome.Added:
                    return $"{prefix}道具「{ItemName}」";
                case ItemAcquireOutcome.Upgraded:
                    return $"{prefix}道具「{ItemName}」升级至 Lv.{Level}";
                case ItemAcquireOutcome.Stacked:
                    return $"{prefix}主动道具「{ItemName}」+1（持有 {Count}）";
                case ItemAcquireOutcome.ConvertedToGold:
                    return $"{prefix}金币 +{Gold}";
                default:
                    return string.Empty;
            }
        }
    }
}
