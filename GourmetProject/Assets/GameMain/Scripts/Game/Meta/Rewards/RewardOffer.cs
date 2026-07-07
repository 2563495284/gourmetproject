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
            IReadOnlyList<RewardChoice> extraChoices,
            bool baseGoldClaimed = false,
            int mainChoiceIndex = -1,
            int extraChoiceIndex = -1,
            bool mainChoiceSkipped = false,
            bool extraChoiceSkipped = false)
        {
            BaseGold = baseGold;
            _mainChoices = new List<RewardChoice>(mainChoices ?? System.Array.Empty<RewardChoice>());
            _extraChoices = new List<RewardChoice>(extraChoices ?? System.Array.Empty<RewardChoice>());
            BaseGoldClaimed = baseGoldClaimed;
            MainChoiceIndex = mainChoiceIndex;
            ExtraChoiceIndex = extraChoiceIndex;
            MainChoiceSkipped = mainChoiceSkipped;
            ExtraChoiceSkipped = extraChoiceSkipped;
        }

        public int BaseGold { get; }

        public bool BaseGoldClaimed { get; private set; }

        public int MainChoiceIndex { get; private set; }

        public int ExtraChoiceIndex { get; private set; }

        public bool MainChoiceSkipped { get; private set; }

        public bool ExtraChoiceSkipped { get; private set; }

        public IReadOnlyList<RewardChoice> MainChoices => _mainChoices;

        public IReadOnlyList<RewardChoice> ExtraChoices => _extraChoices;

        public bool HasMainChoices => _mainChoices.Count > 0;

        public bool HasExtraChoices => _extraChoices.Count > 0;

        public bool MainChoiceClaimed => MainChoiceIndex >= 0;

        public bool ExtraChoiceClaimed => ExtraChoiceIndex >= 0;

        public bool MainChoiceResolved => MainChoiceClaimed;

        public bool ExtraChoiceResolved => ExtraChoiceClaimed;

        public bool IsFullyClaimed =>
            BaseGoldClaimed &&
            (!HasMainChoices || MainChoiceResolved) &&
            (!HasExtraChoices || ExtraChoiceResolved);

        public void MarkBaseGoldClaimed()
        {
            BaseGoldClaimed = true;
        }

        public void MarkMainChoiceClaimed(int index)
        {
            MainChoiceIndex = index;
            MainChoiceSkipped = false;
        }

        public void MarkExtraChoiceClaimed(int index)
        {
            ExtraChoiceIndex = index;
            ExtraChoiceSkipped = false;
        }

        public void MarkMainChoiceSkipped()
        {
            MainChoiceIndex = -1;
            MainChoiceSkipped = true;
        }

        public void MarkExtraChoiceSkipped()
        {
            ExtraChoiceIndex = -1;
            ExtraChoiceSkipped = true;
        }
    }
}
