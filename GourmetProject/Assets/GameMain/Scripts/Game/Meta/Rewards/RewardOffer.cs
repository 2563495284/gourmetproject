using System.Collections.Generic;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 一次过关奖励的完整候选。金币是固定发放；主奖励/额外奖励由玩家选择后再应用。
    /// </summary>
    public sealed class RewardOffer
    {
        private readonly List<RewardChoiceGroup> _fixedGroups;
        private readonly RewardChoiceGroup _specificGroup;

        public RewardOffer(
            int baseGold,
            IReadOnlyList<RewardChoice> mainChoices,
            IReadOnlyList<RewardChoice> extraChoices,
            IReadOnlyList<RewardChoice> bonusChoices = null,
            bool baseGoldClaimed = false,
            int mainChoiceIndex = -1,
            int extraChoiceIndex = -1,
            bool mainChoiceSkipped = false,
            bool extraChoiceSkipped = false,
            int mainRequiredChoiceCount = 1,
            int extraRequiredChoiceCount = 1,
            int bonusChoiceIndex = -1,
            bool bonusChoiceSkipped = false,
            int bonusRequiredChoiceCount = 1,
            IReadOnlyList<int> mainChoiceIndices = null,
            IReadOnlyList<int> extraChoiceIndices = null,
            IReadOnlyList<int> bonusChoiceIndices = null)
        {
            BaseGold = baseGold;
            _fixedGroups = new List<RewardChoiceGroup>
            {
                new RewardChoiceGroup(GroupTitleFor(mainChoices, "基础菜品"), mainChoices, mainRequiredChoiceCount, MergeIndices(mainChoiceIndices, mainChoiceIndex), mainChoiceSkipped),
            };
            if (bonusChoices != null && bonusChoices.Count > 0)
            {
                _fixedGroups.Add(new RewardChoiceGroup(GroupTitleFor(bonusChoices, "额外奖励"), bonusChoices, bonusRequiredChoiceCount, MergeIndices(bonusChoiceIndices, bonusChoiceIndex), bonusChoiceSkipped));
            }

            _specificGroup = new RewardChoiceGroup("特定奖励", extraChoices, extraRequiredChoiceCount, MergeIndices(extraChoiceIndices, extraChoiceIndex), extraChoiceSkipped);
            BaseGoldClaimed = baseGoldClaimed;
        }

        public RewardOffer(
            int baseGold,
            IReadOnlyList<RewardChoiceGroup> fixedGroups,
            RewardChoiceGroup specificGroup,
            bool baseGoldClaimed = false)
        {
            BaseGold = baseGold;
            _fixedGroups = new List<RewardChoiceGroup>(fixedGroups ?? System.Array.Empty<RewardChoiceGroup>());
            _specificGroup = specificGroup ?? new RewardChoiceGroup("特定奖励", null, 0);
            BaseGoldClaimed = baseGoldClaimed;
        }

        public int BaseGold { get; }

        public bool BaseGoldClaimed { get; private set; }

        public int MainChoiceIndex => MainGroup.FirstClaimedIndex;

        public int ExtraChoiceIndex => SpecificGroup.FirstClaimedIndex;

        public int BonusChoiceIndex => BonusGroup.FirstClaimedIndex;

        public bool MainChoiceSkipped => MainGroup.Skipped;

        public bool ExtraChoiceSkipped => SpecificGroup.Skipped;

        public bool BonusChoiceSkipped => BonusGroup.Skipped;

        public int MainRequiredChoiceCount => MainGroup.RequiredChoiceCount;

        public int ExtraRequiredChoiceCount => SpecificGroup.RequiredChoiceCount;

        public int BonusRequiredChoiceCount => BonusGroup.RequiredChoiceCount;

        public IReadOnlyList<RewardChoiceGroup> FixedGroups => _fixedGroups;

        public RewardChoiceGroup SpecificGroup => _specificGroup;

        public RewardChoiceGroup MainGroup => _fixedGroups.Count > 0 ? _fixedGroups[0] : EmptyGroup;

        public RewardChoiceGroup BonusGroup => _fixedGroups.Count > 1 ? _fixedGroups[1] : EmptyGroup;

        public IReadOnlyList<RewardChoice> MainChoices => MainGroup.Choices;

        public IReadOnlyList<RewardChoice> ExtraChoices => SpecificGroup.Choices;

        public IReadOnlyList<RewardChoice> BonusChoices => BonusGroup.Choices;

        public IReadOnlyList<int> MainChoiceIndices => MainGroup.ClaimedIndices;

        public IReadOnlyList<int> ExtraChoiceIndices => SpecificGroup.ClaimedIndices;

        public IReadOnlyList<int> BonusChoiceIndices => BonusGroup.ClaimedIndices;

        public bool HasMainChoices => MainGroup.HasChoices;

        public bool HasExtraChoices => SpecificGroup.HasChoices;

        public bool HasBonusChoices => BonusGroup.HasChoices;

        public bool MainChoiceClaimed => MainChoiceIndices.Count > 0;

        public bool ExtraChoiceClaimed => ExtraChoiceIndices.Count > 0;

        public bool BonusChoiceClaimed => BonusChoiceIndices.Count > 0;

        public bool MainChoiceResolved => MainGroup.IsResolved;

        public bool ExtraChoiceResolved => SpecificGroup.IsResolved;

        public bool BonusChoiceResolved => BonusGroup.IsResolved;

        public bool IsFullyClaimed =>
            BaseGoldClaimed &&
            FixedGroupsResolved &&
            SpecificGroup.IsResolved;

        public void MarkBaseGoldClaimed()
        {
            BaseGoldClaimed = true;
        }

        public void MarkMainChoiceClaimed(int index)
        {
            MainGroup.MarkClaimed(index);
        }

        public void MarkExtraChoiceClaimed(int index)
        {
            SpecificGroup.MarkClaimed(index);
        }

        public void MarkBonusChoiceClaimed(int index)
        {
            BonusGroup.MarkClaimed(index);
        }

        public void MarkMainChoiceSkipped()
        {
            MainGroup.MarkSkipped();
        }

        public void MarkExtraChoiceSkipped()
        {
            SpecificGroup.MarkSkipped();
        }

        public void MarkBonusChoiceSkipped()
        {
            BonusGroup.MarkSkipped();
        }

        public void AddFixedGroup(RewardChoiceGroup group)
        {
            if (group != null && group.HasChoices)
            {
                _fixedGroups.Add(group);
            }
        }

        public RewardChoiceGroup GetFixedGroup(int index)
        {
            return index >= 0 && index < _fixedGroups.Count ? _fixedGroups[index] : EmptyGroup;
        }

        private bool FixedGroupsResolved
        {
            get
            {
                for (int i = 0; i < _fixedGroups.Count; i++)
                {
                    if (!_fixedGroups[i].IsResolved)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private static IReadOnlyList<int> MergeIndices(IReadOnlyList<int> indices, int legacyIndex)
        {
            var result = new List<int>();
            if (indices != null)
            {
                for (int i = 0; i < indices.Count; i++)
                {
                    if (indices[i] >= 0 && !result.Contains(indices[i]))
                    {
                        result.Add(indices[i]);
                    }
                }
            }

            if (legacyIndex >= 0 && !result.Contains(legacyIndex))
            {
                result.Add(legacyIndex);
            }

            return result;
        }

        private static string GroupTitleFor(IReadOnlyList<RewardChoice> choices, string fallback)
        {
            if (choices == null || choices.Count == 0 || choices[0] == null)
            {
                return fallback;
            }

            switch (choices[0].Kind)
            {
                case cfg.RewardKind.Gold:
                    return "金币奖励";
                case cfg.RewardKind.DishChoice:
                    return "基础菜品";
                case cfg.RewardKind.PassiveItemChoice:
                    return "被动道具";
                case cfg.RewardKind.ActiveItemGrant:
                    return "主动道具";
                case cfg.RewardKind.FragmentChoice:
                    return "格子奖励";
                default:
                    return fallback;
            }
        }

        private static readonly RewardChoiceGroup EmptyGroup = new RewardChoiceGroup(string.Empty, null, 0);
    }
}
