using System.Collections.Generic;
using GourmetProject.Game.UI.Meta;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class WeekEventCardArtMappingTests
    {
        [Test]
        public void FoodActions_MapNormalAndHardRewardsToTenDistinctCardResources()
        {
            var expected = new Dictionary<(cfg.FoodActionKind, cfg.RewardKind), string>
            {
                {
                    (cfg.FoodActionKind.Normal, cfg.RewardKind.Gold),
                    "card_action_food_normal_gold"
                },
                {
                    (cfg.FoodActionKind.Normal, cfg.RewardKind.FragmentChoice),
                    "card_action_food_normal_fragment"
                },
                {
                    (cfg.FoodActionKind.Normal, cfg.RewardKind.PassiveItemChoice),
                    "card_action_food_normal_passive"
                },
                {
                    (cfg.FoodActionKind.Normal, cfg.RewardKind.ActiveItemStrengthen),
                    "card_action_food_normal_active_strengthen"
                },
                {
                    (cfg.FoodActionKind.Normal, cfg.RewardKind.ActiveItemAdjust),
                    "card_action_food_normal_active_adjust"
                },
                {
                    (cfg.FoodActionKind.Super, cfg.RewardKind.Gold),
                    "card_action_food_hard_gold"
                },
                {
                    (cfg.FoodActionKind.Super, cfg.RewardKind.FragmentChoice),
                    "card_action_food_hard_fragment"
                },
                {
                    (cfg.FoodActionKind.Super, cfg.RewardKind.PassiveItemChoice),
                    "card_action_food_hard_passive"
                },
                {
                    (cfg.FoodActionKind.Super, cfg.RewardKind.ActiveItemStrengthen),
                    "card_action_food_hard_active_strengthen"
                },
                {
                    (cfg.FoodActionKind.Super, cfg.RewardKind.ActiveItemAdjust),
                    "card_action_food_hard_active_adjust"
                },
            };
            var actualNames = new HashSet<string>();

            foreach (KeyValuePair<(cfg.FoodActionKind, cfg.RewardKind), string> mapping in expected)
            {
                string actual = WeekEventCardView.FoodRewardSpriteName(
                    mapping.Key.Item1,
                    mapping.Key.Item2);

                Assert.That(actual, Is.EqualTo(mapping.Value));
                Assert.That(actualNames.Add(actual), Is.True, $"{mapping.Key} 与其他行动复用了同一张卡面");
            }

            Assert.That(actualNames.Count, Is.EqualTo(10));
        }
    }
}
