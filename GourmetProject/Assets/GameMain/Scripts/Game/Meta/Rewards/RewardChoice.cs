using GourmetProject.Game.Run;
namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 奖励界面中的一个可选项。静态定义仍在 cfg 表中，这里只保存本次抽出的结果。
    /// </summary>
    public sealed class RewardChoice
    {
        public RewardChoice(
            cfg.RewardKind kind,
            string id,
            string name,
            string description,
            int goldAmount = 0,
            bool isFallbackGold = false,
            string flavorId = null)
        {
            Kind = kind;
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            Description = description ?? string.Empty;
            GoldAmount = goldAmount;
            IsFallbackGold = isFallbackGold;
            FlavorId = flavorId ?? string.Empty;
        }

        public cfg.RewardKind Kind { get; }

        public string Id { get; }

        public string Name { get; }

        public string Description { get; }

        public int GoldAmount { get; }

        public bool IsFallbackGold { get; }

        /// <summary>菜品奖励附带的风味 id（来自 withRandomFlavor 池）；空表示无附带风味。</summary>
        public string FlavorId { get; }

        public string DisplayText
        {
            get
            {
                if (Kind == cfg.RewardKind.Gold || IsFallbackGold)
                {
                    return $"{Name} +{GoldAmount}";
                }

                return string.IsNullOrEmpty(Description) ? Name : $"{Name}\n{Description}";
            }
        }

        public static RewardChoice Gold(int amount, string name = "金币", bool isFallback = false)
        {
            return new RewardChoice(cfg.RewardKind.Gold, string.Empty, name, string.Empty, amount, isFallback);
        }
    }
}
