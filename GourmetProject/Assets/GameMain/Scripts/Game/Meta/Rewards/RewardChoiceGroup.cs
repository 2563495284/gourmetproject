using System.Collections.Generic;

namespace GourmetProject.Game.Meta
{
    public sealed class RewardChoiceGroup
    {
        private readonly List<RewardChoice> _choices;
        private readonly List<int> _claimedIndices;

        public RewardChoiceGroup(
            string title,
            IReadOnlyList<RewardChoice> choices,
            int requiredChoiceCount = 1,
            IReadOnlyList<int> claimedIndices = null,
            bool skipped = false,
            string description = null,
            string ruleText = null,
            string sourceSlotId = null)
        {
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            RuleText = ruleText ?? string.Empty;
            SourceSlotId = sourceSlotId ?? string.Empty;
            _choices = new List<RewardChoice>(choices ?? System.Array.Empty<RewardChoice>());
            _claimedIndices = BuildIndices(claimedIndices);
            RequiredChoiceCount = NormalizeRequiredCount(requiredChoiceCount, _choices.Count);
            Skipped = skipped;
        }

        public string Title { get; }

        public string Description { get; }

        public string RuleText { get; }

        public string SourceSlotId { get; }

        public IReadOnlyList<RewardChoice> Choices => _choices;

        public IReadOnlyList<int> ClaimedIndices => _claimedIndices;

        public int RequiredChoiceCount { get; }

        public bool Skipped { get; private set; }

        public int FirstClaimedIndex => _claimedIndices.Count > 0 ? _claimedIndices[0] : -1;

        public bool HasChoices => _choices.Count > 0;

        public bool IsResolved => !HasChoices || _claimedIndices.Count >= RequiredChoiceCount;

        public bool IsClaimed(int index)
        {
            return _claimedIndices.Contains(index);
        }

        public void MarkClaimed(int index)
        {
            if (index < 0 || _claimedIndices.Contains(index))
            {
                return;
            }

            _claimedIndices.Add(index);
            Skipped = false;
        }

        public void MarkSkipped()
        {
            _claimedIndices.Clear();
            Skipped = true;
        }

        private static int NormalizeRequiredCount(int required, int choiceCount)
        {
            if (choiceCount <= 0)
            {
                return 0;
            }

            return System.Math.Min(choiceCount, System.Math.Max(1, required));
        }

        private static List<int> BuildIndices(IReadOnlyList<int> indices)
        {
            var result = new List<int>();
            if (indices == null)
            {
                return result;
            }

            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];
                if (index >= 0 && !result.Contains(index))
                {
                    result.Add(index);
                }
            }

            return result;
        }
    }
}
