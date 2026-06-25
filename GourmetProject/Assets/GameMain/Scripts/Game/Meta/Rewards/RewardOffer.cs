using System.Collections.Generic;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 一次过关奖励的完整候选。金币是固定发放；主奖励/额外奖励由玩家选择后再应用。
    /// </summary>
    public sealed class RewardOffer
    {
        private readonly List<RewardChoice> _mainChoices;
        private readonly List<RewardChoice> _extraChoices;

        public RewardOffer(
            int baseGold,
            IReadOnlyList<RewardChoice> mainChoices,
            IReadOnlyList<RewardChoice> extraChoices)
        {
            BaseGold = baseGold;
            _mainChoices = new List<RewardChoice>(mainChoices ?? System.Array.Empty<RewardChoice>());
            _extraChoices = new List<RewardChoice>(extraChoices ?? System.Array.Empty<RewardChoice>());
        }

        public int BaseGold { get; }

        public IReadOnlyList<RewardChoice> MainChoices => _mainChoices;

        public IReadOnlyList<RewardChoice> ExtraChoices => _extraChoices;

        public bool HasMainChoices => _mainChoices.Count > 0;

        public bool HasExtraChoices => _extraChoices.Count > 0;
    }
}
