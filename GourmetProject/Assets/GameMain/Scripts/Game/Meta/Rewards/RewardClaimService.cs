using System;
using System.Collections.Generic;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>奖励领取的共享领域服务；UI 与自动玩家只负责选择候选。</summary>
    public static class RewardClaimService
    {
        public static bool ClaimBaseGold(GameRun run, RewardOffer offer)
        {
            if (run == null || offer == null || offer.BaseGoldClaimed)
            {
                return false;
            }

            RewardGranter.ApplyBaseGold(run, offer);
            return true;
        }

        public static bool ClaimBonusGold(GameRun run, RewardOffer offer)
        {
            if (run == null || offer == null || !offer.HasBonusGold || offer.BonusGoldClaimed)
            {
                return false;
            }

            RewardGranter.ApplyBonusGold(run, offer);
            return true;
        }

        public static bool TryClaimChoice(
            GameRun run,
            RewardChoiceGroup group,
            int index,
            out string rewardText)
        {
            rewardText = string.Empty;
            if (run == null || group == null || group.IsResolved
                || index < 0 || index >= group.Choices.Count || group.IsClaimed(index))
            {
                return false;
            }

            if (!RewardGranter.TryClaimChoice(run, group.Choices[index], out rewardText))
            {
                return false;
            }

            group.MarkClaimed(index);
            return true;
        }

        public static RewardClaimResult ClaimOffer(
            GameRun run,
            RewardOffer offer,
            Func<RewardChoiceGroup, IReadOnlyList<int>, int> chooseIndex,
            Action<GameRun> resolvePendingFragment = null,
            bool allowAbandon = false)
        {
            var result = new RewardClaimResult();
            if (run == null || offer == null || chooseIndex == null)
            {
                result.Error = "奖励领取参数不完整";
                return result;
            }

            ClaimBaseGold(run, offer);
            ClaimBonusGold(run, offer);

            foreach (RewardChoiceGroup group in EnumerateGroups(offer))
            {
                var remaining = new List<int>();
                for (int i = 0; i < group.Choices.Count; i++)
                {
                    if (!group.IsClaimed(i))
                    {
                        remaining.Add(i);
                    }
                }

                while (!group.IsResolved && remaining.Count > 0)
                {
                    int chosen = chooseIndex(group, remaining);
                    if (!remaining.Contains(chosen))
                    {
                        result.Error = $"奖励策略返回非法候选：{chosen}";
                        return result;
                    }

                    if (TryClaimChoice(run, group, chosen, out string text))
                    {
                        RewardChoice choice = group.Choices[chosen];
                        result.ClaimedIds.Add(string.IsNullOrEmpty(choice?.Id) ? text : choice.Id);
                        if (choice?.Kind == cfg.RewardKind.FragmentChoice)
                        {
                            resolvePendingFragment?.Invoke(run);
                        }
                    }

                    remaining.Remove(chosen);
                }

                if (!group.IsResolved)
                {
                    if (allowAbandon)
                    {
                        group.MarkSkipped();
                        result.AbandonedGroups.Add(group.Title);
                        continue;
                    }

                    result.Error = $"奖励组“{group.Title}”无法完成领取";
                    return result;
                }
            }

            result.Success = offer.IsFullyClaimed || allowAbandon;
            if (!result.Success && string.IsNullOrEmpty(result.Error))
            {
                result.Error = "奖励未完整领取";
            }

            return result;
        }

        private static IEnumerable<RewardChoiceGroup> EnumerateGroups(RewardOffer offer)
        {
            foreach (RewardChoiceGroup group in offer.FixedGroups)
            {
                if (group != null)
                {
                    yield return group;
                }
            }

            if (offer.SpecificGroup != null)
            {
                yield return offer.SpecificGroup;
            }
        }
    }

    public sealed class RewardClaimResult
    {
        public bool Success;
        public string Error = string.Empty;
        public List<string> ClaimedIds { get; } = new List<string>();
        public List<string> AbandonedGroups { get; } = new List<string>();
    }
}
